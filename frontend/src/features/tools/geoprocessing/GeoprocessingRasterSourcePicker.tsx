import { useEffect, useMemo, useRef, useState } from 'react';
import type { BackendGateway, Language, WorkbenchContext } from '../../../shared/workbench';
import { errorDiagnostics, text } from '../../../shared/workbench';
import { loadMapDocumentCatalog } from '../../map/ProjectTreeListener';
import type { CatalogTreeNode, CatalogTreeNodeLocator, MapDocumentTab } from '../../map/mapTypes';
import { parseRasterMapSourceIdentity, type RasterMapSourceIdentity } from '../../map/symbology/rasterSymbologyWorkflow';
import { sameSourcePath } from '../attributes/attributeTableTypes';

export interface GeoprocessingRasterCapture {
  owner: string;
  source: RasterMapSourceIdentity;
  sourceRevision: string;
  visualizationRevision: string;
  width: number;
  height: number;
  bandCount: number;
  driver: string;
  dataType: string;
  projectionWKT: string | null;
  unitSystem: string | null;
  cellSizeX: number;
  mapName: string;
  layerName: string;
  sourcePath: string;
}

export type GeoprocessingRasterSelection = { capture: GeoprocessingRasterCapture | null };

type Candidate = { id: string; node: CatalogTreeNode; locator: CatalogTreeNodeLocator; document: MapDocumentTab };

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value);
}

function isSha256(value: unknown): value is string {
  return typeof value === 'string' && /^[\da-f]{64}$/i.test(value);
}

function visualizationPathFor(projectPath: string): string {
  const separator = Math.max(projectPath.lastIndexOf('/'), projectPath.lastIndexOf('\\'));
  const extension = projectPath.lastIndexOf('.');
  return extension > separator ? projectPath.slice(0, extension) + '.vis' : projectPath + '.vis';
}

function nodeKey(document: MapDocumentTab, locator: CatalogTreeNodeLocator): string {
  return JSON.stringify([document.mapIndex, locator.nodePath ?? null, locator.nodeName, locator.assetID ?? null]);
}

function collectCandidates(documents: MapDocumentTab[]): Candidate[] {
  const candidates: Candidate[] = [];
  for (const document of documents) {
    const visit = (nodes: CatalogTreeNode[]): void => {
      for (const node of nodes) {
        const locator = node.locator;
        if (locator?.nodeType === 'MapLayer' && locator.mapType === 'SingleBandRasterMapLayer' &&
            Array.isArray(locator.nodePath) && locator.nodePath.length > 0 &&
            locator.nodePath.every(index => Number.isInteger(index) && index >= 0) &&
            typeof locator.filename === 'string' && locator.filename.length > 0 &&
            typeof node.assetID === 'string' && node.assetID.length > 0 &&
            (locator.assetID === undefined || locator.assetID === null || locator.assetID === node.assetID)) {
          candidates.push({ id: nodeKey(document, locator), node, locator, document });
        }
        if (node.children?.length) visit(node.children);
      }
    };
    visit(document.roots);
  }
  return candidates;
}

export function GeoprocessingRasterSourcePicker({ language, context, backend, owner, selection, onSelectionChange }: {
  language: Language; context: WorkbenchContext; backend: BackendGateway; owner: string;
  selection: GeoprocessingRasterSelection; onSelectionChange: (selection: GeoprocessingRasterSelection) => void;
}) {
  const zh = language === 'zh';
  const [documents, setDocuments] = useState<MapDocumentTab[] | null>(null);
  const [selectedID, setSelectedID] = useState('');
  const [loadingMaps, setLoadingMaps] = useState(false);
  const [inspecting, setInspecting] = useState(false);
  const [error, setError] = useState<string>();
  const [diagnostic, setDiagnostic] = useState<string>();
  const generation = useRef(0);
  const controller = useRef<AbortController | null>(null);
  const ownerRef = useRef(owner); ownerRef.current = owner;
  const candidates = useMemo(() => documents ? collectCandidates(documents) : [], [documents]);
  const selected = candidates.find(candidate => candidate.id === selectedID) ?? null;

  useEffect(() => {
    generation.current += 1; controller.current?.abort(); controller.current = null;
    setDocuments(null); setSelectedID(''); setLoadingMaps(false); setInspecting(false);
    setError(undefined); setDiagnostic(undefined);
    onSelectionChange({ capture: null });
  }, [owner, onSelectionChange]);
  useEffect(() => () => { generation.current += 1; controller.current?.abort(); }, []);

  const cancelCurrent = (): void => {
    generation.current += 1;
    controller.current?.abort();
    controller.current = null;
    setLoadingMaps(false);
    setInspecting(false);
  };

  const readMaps = async (): Promise<void> => {
    cancelCurrent();
    setError(undefined); setDiagnostic(undefined); setDocuments(null); setSelectedID('');
    onSelectionChange({ capture: null });
    if (!context.projectPath || !context.projectRevision || !context.projectXml) {
      setError(text(language, '请先打开已保存且包含工程投影的项目。', 'Open a saved project with a readable projection first.'));
      return;
    }
    if (!backend.supports('map.document.list') || !backend.supports('map.tree.load')) {
      setError(text(language, '当前服务未开放已保存地图目录读取。', 'The current service does not expose saved-map catalog reading.'));
      return;
    }
    const requestOwner = owner;
    const requestGeneration = ++generation.current;
    const aborter = new AbortController(); controller.current = aborter;
    setLoadingMaps(true);
    try {
      const catalog = await loadMapDocumentCatalog(backend, context, { signal: aborter.signal });
      if (requestGeneration !== generation.current || ownerRef.current !== requestOwner || aborter.signal.aborted) return;
      setDocuments(catalog.documents);
      const first = collectCandidates(catalog.documents)[0];
      setSelectedID(first?.id ?? '');
      if (!first) setError(text(language, '当前已保存地图中没有可用的单波段栅格图层。', 'No single-band raster layer is present in the saved maps.'));
    } catch (caught) {
      if (requestGeneration !== generation.current || ownerRef.current !== requestOwner || aborter.signal.aborted) return;
      setError(text(language, '读取已保存地图目录失败。', 'Could not read the saved-map catalog.'));
      const details = errorDiagnostics(caught);
      try { setDiagnostic(typeof details === 'string' ? details : JSON.stringify(details, null, 2)); }
      catch { setDiagnostic(String(details)); }
    } finally {
      if (requestGeneration === generation.current) {
        setLoadingMaps(false);
        if (controller.current === aborter) controller.current = null;
      }
    }
  };

  const inspectSelected = async (): Promise<void> => {
    cancelCurrent(); setError(undefined); setDiagnostic(undefined); onSelectionChange({ capture: null });
    if (!selected || !context.projectPath || !context.projectRevision || !context.projectXml || !selected.document.visualizationRevision) {
      setError(text(language, '请选择带有有效可视化修订的已保存单波段栅格图层。', 'Choose a saved single-band raster layer with a valid visualization revision.'));
      return;
    }
    if (!backend.supports('map.raster.layer.inspect') || !backend.supports('rasterSymbology.inspect')) {
      setError(text(language, '当前服务没有完整的栅格来源检查能力。', 'The current service does not expose complete raster-source inspection.'));
      return;
    }
    const requestOwner = owner;
    const requestGeneration = ++generation.current;
    const aborter = new AbortController(); controller.current = aborter;
    setInspecting(true);
    const isCurrent = (): boolean => requestGeneration === generation.current && ownerRef.current === requestOwner && !aborter.signal.aborted;
    try {
      const rawLayer: unknown = await backend.request('map.raster.layer.inspect', { sourcePath: selected.node.assetID }, { signal: aborter.signal });
      if (!isCurrent()) return;
      if (!isRecord(rawLayer) || typeof rawLayer.sourcePath !== 'string' ||
          typeof rawLayer.sourceCanonicalPath !== 'string' || !isSha256(rawLayer.sourceRevision) ||
          !Number.isInteger(rawLayer.width) || (rawLayer.width as number) < 1 ||
          !Number.isInteger(rawLayer.height) || (rawLayer.height as number) < 1 ||
          !Number.isInteger(rawLayer.bandCount) || (rawLayer.bandCount as number) < 1 ||
          typeof rawLayer.driver !== 'string' || typeof rawLayer.dataType !== 'string' ||
          !(rawLayer.projectionWKT === null || typeof rawLayer.projectionWKT === 'string') ||
          !(rawLayer.unitSystem === null || typeof rawLayer.unitSystem === 'string') ||
          typeof rawLayer.cellSizeX !== 'number' || !Number.isFinite(rawLayer.cellSizeX)) {
        throw new Error('The raster layer inspection response is incomplete.');
      }
      if (!sameSourcePath(rawLayer.sourcePath, selected.node.assetID!) ||
          !sameSourcePath(rawLayer.sourceCanonicalPath, selected.node.assetID!)) {
        throw new Error('The inspected raster path does not match the exact saved map node.');
      }
      if (rawLayer.bandCount !== 1 || !['Single', 'Double'].includes(String(rawLayer.dataType))) {
        throw new Error('当前服务端地理处理仅支持单波段 Single/Double 栅格。');
      }

      const locator = selected.locator;
      const source = parseRasterMapSourceIdentity({
        id: `vis-map-${selected.document.mapIndex}-node-${locator.nodePath!.join('.')}`,
        projectPath: context.projectPath,
        visualizationPath: visualizationPathFor(context.projectPath),
        visualizationRevision: selected.document.visualizationRevision,
        mapIndex: selected.document.mapIndex,
        mapName: selected.document.name,
        mapType: selected.document.mapType,
        nodePath: [...locator.nodePath!],
        nodeName: locator.nodeName,
        mapLayerType: locator.mapType!,
        filename: locator.filename ?? null,
        filenameRelative: locator.filenameRelative ?? null,
        sourcePath: selected.node.assetID,
        sourceMapID: selected.document.mapID,
        sourceRevision: rawLayer.sourceRevision,
        gdbSource: null,
      });
      const rawBound: unknown = await backend.request('rasterSymbology.inspect', { source, projectXml: context.projectXml }, { signal: aborter.signal });
      if (!isCurrent()) return;
      if (!isRecord(rawBound) || !isSha256(rawBound.sourceRevision) || !isSha256(rawBound.visualizationRevision) ||
          rawBound.sourceRevision.toUpperCase() !== source.sourceRevision.toUpperCase() ||
          rawBound.visualizationRevision.toUpperCase() !== source.visualizationRevision.toUpperCase()) {
        throw new Error('The saved raster map identity or source revision changed during inspection.');
      }
      const capture: GeoprocessingRasterCapture = {
        owner: requestOwner,
        source,
        sourceRevision: rawBound.sourceRevision,
        visualizationRevision: rawBound.visualizationRevision,
        width: rawLayer.width as number,
        height: rawLayer.height as number,
        bandCount: rawLayer.bandCount as number,
        driver: rawLayer.driver,
        dataType: rawLayer.dataType,
        projectionWKT: typeof rawLayer.projectionWKT === 'string' ? rawLayer.projectionWKT : null,
        unitSystem: typeof rawLayer.unitSystem === 'string' ? rawLayer.unitSystem : null,
        cellSizeX: rawLayer.cellSizeX,
        mapName: selected.document.name,
        layerName: selected.node.name,
        sourcePath: rawLayer.sourceCanonicalPath,
      };
      onSelectionChange({ capture });
    } catch (caught) {
      if (!isCurrent()) return;
      setError(text(language, caught instanceof Error && caught.message.startsWith('当前服务端')
        ? caught.message : '无法确认所选地图节点对应的栅格来源。',
      caught instanceof Error ? caught.message : 'Could not verify the raster source for the selected map node.'));
      const details = errorDiagnostics(caught);
      try { setDiagnostic(typeof details === 'string' ? details : JSON.stringify(details, null, 2)); }
      catch { setDiagnostic(String(details)); }
    } finally {
      if (requestGeneration === generation.current) {
        setInspecting(false);
        if (controller.current === aborter) controller.current = null;
      }
    }
  };

  const changeCandidate = (id: string): void => {
    cancelCurrent(); setSelectedID(id); setError(undefined); setDiagnostic(undefined); onSelectionChange({ capture: null });
  };

  return <section className="tools-input-details gp-raster-source-picker" data-testid="geoprocessing-raster-source-picker">
    <div className="tools-subheading-row"><h4>{zh ? '已保存地图栅格' : 'Saved map raster'}</h4><span className="tools-badge">{zh ? 'Host 检查' : 'Host inspected'}</span></div>
    <p className="tools-field-help">{zh ? '从当前工程的已保存地图节点读取栅格身份、文件修订和投影；不会根据文件名猜测来源。' : 'Read the raster identity, file revision, and projection from a saved map node. The source is never inferred from a filename.'}</p>
    <div className="tools-actions">
      <button className="tools-button tools-button-secondary" type="button" onClick={() => void readMaps()} disabled={loadingMaps || inspecting || !context.projectPath || !context.projectRevision || !context.projectXml}>
        {loadingMaps ? (zh ? '读取地图…' : 'Reading maps…') : (zh ? '读取已保存地图' : 'Read saved maps')}
      </button>
    </div>
    {documents && <label className="tools-field"><span>{zh ? '地图图层' : 'Map layer'}</span>
      <select value={selectedID} onChange={event => changeCandidate(event.target.value)} disabled={inspecting}>
        <option value="">{zh ? '请选择单波段栅格' : 'Select a single-band raster'}</option>
        {candidates.map(candidate => <option key={candidate.id} value={candidate.id}>
          {candidate.document.name} · {candidate.node.name} · {candidate.locator.filenameRelative ?? candidate.locator.filename}
        </option>)}
      </select>
    </label>}
    <button className="tools-button tools-button-secondary" type="button" onClick={() => void inspectSelected()} disabled={!selected || loadingMaps || inspecting || !backend.supports('map.raster.layer.inspect') || !backend.supports('rasterSymbology.inspect')}>
      {inspecting ? (zh ? '检查来源…' : 'Inspecting source…') : (zh ? '检查栅格来源' : 'Inspect raster source')}
    </button>
    {error && <div className="tools-inline-error" role="alert">{error}{diagnostic && <details><summary>{zh ? '原始诊断' : 'Raw diagnostic'}</summary><pre>{diagnostic}</pre></details>}</div>}
    {selection.capture?.owner === owner && <dl className="tools-result-grid gp-raster-capture-summary">
      <div><dt>{zh ? '來源地图' : 'Saved map'}</dt><dd>{selection.capture.mapName} · {selection.capture.layerName}</dd></div>
      <div><dt>{zh ? '栅格尺寸' : 'Raster size'}</dt><dd>{selection.capture.width} × {selection.capture.height} · {selection.capture.bandCount} band</dd></div>
      <div><dt>{zh ? '格式 / 类型' : 'Driver / type'}</dt><dd>{selection.capture.driver} · {selection.capture.dataType}</dd></div>
      <div><dt>{zh ? '源 SHA-256' : 'Source SHA-256'}</dt><dd>{selection.capture.sourceRevision}</dd></div>
      <div><dt>{zh ? '源路径' : 'Source path'}</dt><dd>{selection.capture.sourcePath}</dd></div>
      {selection.capture.unitSystem && <div><dt>{zh ? '单位' : 'Units'}</dt><dd>{selection.capture.unitSystem}</dd></div>}
      {selection.capture.projectionWKT && <div><dt>{zh ? '栅格投影' : 'Raster projection'}</dt><dd><code>{selection.capture.projectionWKT}</code></dd></div>}
    </dl>}
    {!documents && !loadingMaps && <p className="tools-muted">{zh ? '请先读取已保存地图。' : 'Read the saved maps first.'}</p>}
  </section>;
}
