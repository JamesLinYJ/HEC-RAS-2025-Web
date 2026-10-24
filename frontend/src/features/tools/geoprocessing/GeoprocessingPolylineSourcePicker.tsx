import { useEffect, useMemo, useRef, useState } from 'react';
import type { BackendGateway, Language, WorkbenchContext } from '../../../shared/workbench';
import { loadMapDocumentCatalog, projectXmlSha } from '../../map/ProjectTreeListener';
import type { CatalogTreeNode, CatalogTreeNodeLocator, MapDocumentTab } from '../../map/mapTypes';

const maximumSelectableLinePaths = 250;

export interface GeoprocessingPolylineInput {
  featureIdentity: string;
  pathIdentity: string;
  sourceFeatureIndex: number;
  pathIndex: number;
  coordinates: number[][];
  zValues?: string[];
  mValues?: string[];
}

export interface GeoprocessingPolylineSourceCapture {
  owner: string;
  sourcePath: string;
  sourceRevision: string;
  targetProjectionWKT: string | null;
  mapName: string;
  layerName: string;
  lines: GeoprocessingPolylineInput[];
}

export interface GeoprocessingSourceSelection {
  capture: GeoprocessingPolylineSourceCapture | null;
  inputMode: 'All' | 'Selected';
  selectedPathIDs: string[];
}

type SourceCandidate = { node: CatalogTreeNode; locator: CatalogTreeNodeLocator; id: string };
type SourceFileRevision = { path: string; role: string; required: boolean; exists: boolean; revision: string | null };
function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value);
}

function isSha256(value: unknown): value is string {
  return typeof value === 'string' && /^[\da-f]{64}$/i.test(value);
}

function pathExtension(value: string | null | undefined): string {
  if (!value) return '';
  const path = value.split(/[?#]/, 1)[0] ?? '';
  const leaf = path.split(/[\\/]/).at(-1) ?? '';
  const separator = leaf.lastIndexOf('.');
  return separator < 0 ? '' : leaf.slice(separator).toLocaleLowerCase();
}

function collectPolylineCandidates(roots: CatalogTreeNode[]): SourceCandidate[] {
  const candidates: SourceCandidate[] = [];
  const visit = (node: CatalogTreeNode): void => {
    const locator = node.locator;
    if (locator?.nodeType === 'MapLayer' && locator.mapType === 'PolylinesVector' &&
        (pathExtension(locator.filename) === '.geojson' || pathExtension(locator.filename) === '.json' ||
          pathExtension(locator.filenameRelative) === '.geojson' || pathExtension(locator.filenameRelative) === '.json')) {
      candidates.push({ node, locator, id: node.id });
    }
    for (const child of node.children ?? []) visit(child);
  };
  roots.forEach(visit);
  return candidates;
}

function sameLocator(left: CatalogTreeNodeLocator, right: CatalogTreeNodeLocator): boolean {
  return left.rootName === right.rootName && left.nodeType === right.nodeType && left.nodeName === right.nodeName &&
    left.mapType === right.mapType && left.assetID === right.assetID && left.filename === right.filename &&
    left.filenameRelative === right.filenameRelative && JSON.stringify(left.nodePath ?? null) === JSON.stringify(right.nodePath ?? null);
}

function parseSourceLoad(value: unknown, context: WorkbenchContext, document: MapDocumentTab,
  projectXmlSHA: string | null, candidate: SourceCandidate, layerName: string, owner: string): GeoprocessingPolylineSourceCapture {
  const response = isRecord(value) ? value : null;
  const sourceValue = response?.source;
  const dataValue = response?.data;
  if (!response || !isRecord(sourceValue) || !isRecord(sourceValue.map) ||
      !isRecord(sourceValue.family) || !isRecord(dataValue)) {
    throw new Error('服务端没有返回完整的折线来源读取结果。');
  }
  const source = sourceValue as Record<string, unknown>;
  const map = source.map as Record<string, unknown>;
  const family = source.family as Record<string, unknown>;
  const data = dataValue as Record<string, unknown>;
  const locator = source.locator;
  if (!isRecord(locator)) throw new Error('服务端返回的地图图层定位信息无效。');
  const sourceLocator = locator as unknown as CatalogTreeNodeLocator;
  const mapProjectPath = map.projectPath;
  const mapProjectRevision = map.projectRevision;
  const mapProjectXmlSHA = map.projectXmlSHA;
  const mapVisualizationRevision = map.visualizationRevision;
  const mapID = map.mapID;
  const mapIndex = map.mapIndex;
  const mapName = map.mapName;
  const mapType = map.mapType;
  const isPersisted = map.isPersisted;
  const familyName = family.family;
  const familySourcePath = family.sourcePath;
  const familyRevision = family.revision;
  const familyFiles = family.files;
  const shapeType = data.shapeType;
  const dataCount = data.count;
  const dataFeatures = data.features;
  const projectionStatus = data.projectionStatus;
  const targetProjectionWKT = data.targetProjectionWKT;
  if (typeof mapProjectPath !== 'string' || !isSha256(mapProjectRevision) ||
      !(mapProjectXmlSHA === null || isSha256(mapProjectXmlSHA)) ||
      !(mapVisualizationRevision === null || isSha256(mapVisualizationRevision)) ||
      !(mapID === null || typeof mapID === 'string') ||
      !(mapIndex === null || (Number.isInteger(mapIndex) && (mapIndex as number) >= 0)) ||
      !(mapName === null || typeof mapName === 'string') || typeof mapType !== 'string' ||
      isPersisted !== true || typeof familyName !== 'string' || typeof familySourcePath !== 'string' ||
      !familySourcePath.trim() || !isSha256(familyRevision) || !Array.isArray(familyFiles) ||
      shapeType !== 'Polyline' || !Number.isInteger(dataCount) || !Array.isArray(dataFeatures) ||
      dataCount !== dataFeatures.length || typeof projectionStatus !== 'string' ||
      !(targetProjectionWKT === null || typeof targetProjectionWKT === 'string')) {
    throw new Error('服务端返回的工程、图层或折线来源身份无效。');
  }
  if (!context.projectPath || !context.projectRevision ||
      mapProjectPath !== context.projectPath ||
      mapProjectRevision.toUpperCase() !== context.projectRevision.toUpperCase() ||
      (mapProjectXmlSHA as string | null)?.toUpperCase() !== projectXmlSHA?.toUpperCase() ||
      (mapVisualizationRevision as string | null)?.toUpperCase() !== document.visualizationRevision?.toUpperCase() ||
      mapID !== document.mapID || mapIndex !== document.mapIndex || mapName !== document.name ||
      mapType !== document.mapType || !sameLocator(sourceLocator, candidate.locator)) {
    throw new Error('读取折线期间工程、地图、可视化文件或所选图层身份发生变化。请重新读取地图目录。');
  }
  if (familyName !== 'geojson') throw new Error('当前操作只接收已保存的 GeoJSON 折线图层。');
  const sourceFiles = familyFiles.filter((file): file is SourceFileRevision => isRecord(file) &&
    file.role === 'source' && file.required === true && file.exists === true && isSha256(file.revision) &&
    typeof file.path === 'string');
  if (familyFiles.length !== 1 || sourceFiles.length !== 1 || sourceFiles[0]!.path !== familySourcePath) {
    throw new Error('GeoJSON 来源回执没有提供唯一且一致的主文件 SHA-256。');
  }
  if (typeof targetProjectionWKT === 'string' && !targetProjectionWKT.trim()) {
    throw new Error('工程投影回执为空，无法确认折线坐标空间。');
  }
  if (projectionStatus === 'projected' && typeof targetProjectionWKT !== 'string') {
    throw new Error('服务端没有返回已投影折线对应的工程 WKT。');
  }
  if (projectionStatus !== 'projected' && projectionStatus !== 'localProjectSourceCoordinates') {
    throw new Error('服务端返回了不支持的折线坐标空间。');
  }

  const lines: GeoprocessingPolylineInput[] = [];
  for (const rawFeature of dataFeatures) {
    if (!isRecord(rawFeature) || !Number.isInteger(rawFeature.sourceOrdinal) || (rawFeature.sourceOrdinal as number) < 0 ||
        !(rawFeature.sourceID === null || typeof rawFeature.sourceID === 'string') ||
        rawFeature.shapeType !== 'Polyline' || !Array.isArray(rawFeature.parts)) {
      throw new Error('服务端返回的折线要素身份无效。');
    }
    const featureIndex = rawFeature.sourceOrdinal as number;
    const featureIdentity = typeof rawFeature.sourceID === 'string' && rawFeature.sourceID.trim()
      ? `geojson-feature-id:${rawFeature.sourceID}` : `geojson-feature:${featureIndex}`;
    for (const rawPart of rawFeature.parts) {
      if (!isRecord(rawPart) || !Number.isInteger(rawPart.partOrdinal) || (rawPart.partOrdinal as number) < 0 ||
          typeof rawPart.hasZ !== 'boolean' || typeof rawPart.hasM !== 'boolean' || !Array.isArray(rawPart.vertices)) {
        throw new Error('服务端返回的折线路径身份无效。');
      }
      const coordinates: number[][] = [];
      const zValues: string[] = [];
      const mValues: string[] = [];
      for (const rawVertex of rawPart.vertices) {
        if (!isRecord(rawVertex) || typeof rawVertex.x !== 'number' || !Number.isFinite(rawVertex.x) ||
            typeof rawVertex.y !== 'number' || !Number.isFinite(rawVertex.y) ||
            typeof rawVertex.sourceX !== 'number' || !Number.isFinite(rawVertex.sourceX) ||
            typeof rawVertex.sourceY !== 'number' || !Number.isFinite(rawVertex.sourceY) ||
            !(rawVertex.z === null || (typeof rawVertex.z === 'number' && Number.isFinite(rawVertex.z))) ||
            !(rawVertex.m === null || (typeof rawVertex.m === 'number' && Number.isFinite(rawVertex.m)))) {
          throw new Error('服务端返回的折线顶点不是有限的来源坐标。');
        }
        coordinates.push([rawVertex.x, rawVertex.y]);
        zValues.push(rawVertex.z === null ? 'NaN' : String(rawVertex.z));
        mValues.push(rawVertex.m === null ? 'NaN' : String(rawVertex.m));
      }
      if (coordinates.length < 2) throw new Error('所选 GeoJSON 包含不足两个顶点的折线路径，未将不完整输入交给工具。');
      const partIndex = rawPart.partOrdinal as number;
      lines.push({
        featureIdentity,
        pathIdentity: `geojson-path:${featureIndex}:${partIndex}`,
        sourceFeatureIndex: featureIndex,
        pathIndex: partIndex,
        coordinates,
        ...(rawPart.hasZ ? { zValues } : {}),
        ...(rawPart.hasM ? { mValues } : {}),
      });
    }
  }
  if (lines.length === 0 || new Set(lines.map(line => line.pathIdentity)).size !== lines.length) {
    throw new Error('已保存地图中没有可执行的 GeoJSON 折线路径。');
  }
  return {
    owner,
    sourcePath: familySourcePath,
    sourceRevision: sourceFiles[0]!.revision!,
    targetProjectionWKT: targetProjectionWKT as string | null,
    mapName: document.name,
    layerName,
    lines,
  };
}

export function GeoprocessingPolylineSourcePicker({ language, context, backend, owner, selection, onSelectionChange }:
  { language: Language; context: WorkbenchContext; backend: BackendGateway; owner: string;
    selection: GeoprocessingSourceSelection; onSelectionChange: (next: GeoprocessingSourceSelection) => void }) {
  const zh = language === 'zh';
  const [catalog, setCatalog] = useState<Awaited<ReturnType<typeof loadMapDocumentCatalog>> | null>(null);
  const [selectedMapID, setSelectedMapID] = useState('');
  const [selectedLayerID, setSelectedLayerID] = useState('');
  const [loadingMaps, setLoadingMaps] = useState(false);
  const [loadingSource, setLoadingSource] = useState(false);
  const [error, setError] = useState<string>();
  const [diagnostic, setDiagnostic] = useState<string>();
  const mapGeneration = useRef(0);
  const sourceGeneration = useRef(0);
  const mapRequest = useRef<AbortController | null>(null);
  const sourceRequest = useRef<AbortController | null>(null);
  const ownerRef = useRef(owner); ownerRef.current = owner;
  const currentDocument = catalog?.documents.find(document => document.id === selectedMapID);
  const candidates = useMemo(() => currentDocument ? collectPolylineCandidates(currentDocument.roots) : [], [currentDocument]);
  const selectedCandidate = candidates.find(candidate => candidate.id === selectedLayerID);

  useEffect(() => {
    mapGeneration.current += 1;
    sourceGeneration.current += 1;
    mapRequest.current?.abort(); mapRequest.current = null;
    sourceRequest.current?.abort(); sourceRequest.current = null;
    setCatalog(null); setSelectedMapID(''); setSelectedLayerID(''); setLoadingMaps(false); setLoadingSource(false);
    setError(undefined); setDiagnostic(undefined);
    onSelectionChange({ capture: null, inputMode: 'All', selectedPathIDs: [] });
  }, [owner, onSelectionChange]);
  useEffect(() => () => {
    mapRequest.current?.abort(); sourceRequest.current?.abort();
  }, []);
  useEffect(() => {
    if (candidates.length === 0) { setSelectedLayerID(''); return; }
    if (!candidates.some(candidate => candidate.id === selectedLayerID)) setSelectedLayerID(candidates[0]!.id);
  }, [candidates, selectedLayerID]);

  const isCurrent = (generation: number, expectedOwner: string, signal: AbortSignal): boolean =>
    generation === mapGeneration.current && ownerRef.current === expectedOwner && !signal.aborted;
  const loadMaps = async (): Promise<void> => {
    mapRequest.current?.abort();
    sourceRequest.current?.abort(); sourceRequest.current = null;
    onSelectionChange({ capture: null, inputMode: 'All', selectedPathIDs: [] });
    setCatalog(null); setSelectedMapID(''); setSelectedLayerID(''); setError(undefined); setDiagnostic(undefined);
    if (!context.projectPath || !context.projectXml || !context.projectRevision) {
      setError(zh ? '请先打开已有工程，再读取其已保存地图。' : 'Open a saved project before loading its saved maps.');
      return;
    }
    if (!backend.supports('map.document.list') || !backend.supports('map.tree.load') || !backend.supports('map.vector.source.load')) {
      setError(zh ? '当前服务未开放已保存地图目录或折线读取。' : 'The current service does not provide the saved-map catalog or vector reader.');
      return;
    }
    const controller = new AbortController(); mapRequest.current = controller;
    const expectedOwner = owner; const generation = ++mapGeneration.current; setLoadingMaps(true);
    try {
      const loaded = await loadMapDocumentCatalog(backend, context, { signal: controller.signal });
      if (!isCurrent(generation, expectedOwner, controller.signal)) return;
      if (loaded.projectRevision.toUpperCase() !== context.projectRevision.toUpperCase()) {
        throw new Error('工程修订在读取地图目录期间发生变化。');
      }
      setCatalog(loaded);
      const active = loaded.documents.find(document => document.mapIndex === loaded.activeMapIndex) ?? loaded.documents[0];
      setSelectedMapID(active?.id ?? '');
      if (!loaded.documents.length) setError(zh ? '当前工程没有已保存地图。' : 'The project has no saved maps.');
      else if (!loaded.documents.some(document => collectPolylineCandidates(document.roots).length > 0)) {
        setError(zh ? '已保存地图中没有 .geojson/.json 折线图层。' : 'No saved .geojson/.json polyline layer is available.');
      }
    } catch (caught) {
      if (controller.signal.aborted || ownerRef.current !== expectedOwner) return;
      setError(zh ? '读取已保存地图失败。' : 'Could not load the saved maps.');
      const details = caught instanceof Error ? caught.message : String(caught);
      setDiagnostic(details);
    } finally {
      if (generation === mapGeneration.current && ownerRef.current === expectedOwner) {
        setLoadingMaps(false);
        if (mapRequest.current === controller) mapRequest.current = null;
      }
    }
  };

  const changeMap = (id: string): void => {
    setSelectedMapID(id); setSelectedLayerID(''); setError(undefined); setDiagnostic(undefined);
    sourceRequest.current?.abort(); sourceRequest.current = null;
    onSelectionChange({ capture: null, inputMode: 'All', selectedPathIDs: [] });
  };

  const loadSource = async (): Promise<void> => {
    if (!selectedCandidate || !currentDocument || !context.projectPath || !context.projectXml || !context.projectRevision) return;
    sourceRequest.current?.abort();
    onSelectionChange({ capture: null, inputMode: 'All', selectedPathIDs: [] });
    setError(undefined); setDiagnostic(undefined);
    const controller = new AbortController(); sourceRequest.current = controller;
    const expectedOwner = owner; const generation = ++sourceGeneration.current; setLoadingSource(true);
    try {
      const xmlSHA = await projectXmlSha(context.projectXml);
      if (controller.signal.aborted || ownerRef.current !== expectedOwner) return;
      const map = {
        projectPath: context.projectPath,
        projectXml: context.projectXml,
        projectXmlSHA: xmlSHA,
        mapID: currentDocument.mapID,
        mapIndex: currentDocument.mapIndex,
        mapName: currentDocument.name,
        mapType: currentDocument.mapType,
        expectedVisualizationRevision: currentDocument.visualizationRevision,
        projectSessionID: context.projectSessionID,
      };
      const value = await backend.request<unknown>('map.vector.source.load', {
        map, node: selectedCandidate.locator, expectedFamily: null,
      }, { signal: controller.signal });
      if (controller.signal.aborted || ownerRef.current !== expectedOwner || generation !== sourceGeneration.current) return;
      const capture = parseSourceLoad(value, context, currentDocument, xmlSHA, selectedCandidate, selectedCandidate.node.name, expectedOwner);
      onSelectionChange({ capture, inputMode: 'All', selectedPathIDs: [] });
    } catch (caught) {
      if (controller.signal.aborted || ownerRef.current !== expectedOwner) return;
      setError(zh ? '未能读取并核验所选 GeoJSON 折线。' : 'The selected GeoJSON polylines could not be read and verified.');
      setDiagnostic(caught instanceof Error ? caught.message : String(caught));
    } finally {
      if (generation === sourceGeneration.current && ownerRef.current === expectedOwner) {
        setLoadingSource(false);
        if (sourceRequest.current === controller) sourceRequest.current = null;
      }
    }
  };

  const selectAll = (checked: boolean): void => {
    if (!selection.capture) return;
    onSelectionChange({ ...selection, selectedPathIDs: checked ? selection.capture.lines.map(line => line.pathIdentity) : [] });
  };
  const togglePath = (pathIdentity: string, checked: boolean): void => {
    const ids = new Set(selection.selectedPathIDs);
    if (checked) ids.add(pathIdentity); else ids.delete(pathIdentity);
    onSelectionChange({ ...selection, selectedPathIDs: [...ids] });
  };

  return <section className="tools-input-details gp-source-picker">
    <div className="tools-subheading-row"><h4>{zh ? '折线来源' : 'Polyline source'}</h4><span className="tools-badge">{zh ? '已保存地图' : 'Saved map'}</span></div>
    <p className="tools-field-help">{zh ? '从当前工程的已保存地图中选择 GeoJSON 折线；执行前核验来源文件与图层，并读取实际几何和工程坐标。' : 'Choose a saved GeoJSON polyline. The service verifies its source file and layer, then reads the real geometry and project coordinates before execution.'}</p>
    <div className="gp-source-picker-actions">
      <button className="tools-button tools-button-secondary" type="button" onClick={() => void loadMaps()} disabled={loadingMaps || loadingSource || !context.projectPath}>
        {loadingMaps ? (zh ? '读取地图中…' : 'Loading maps…') : (zh ? '读取已保存地图' : 'Load saved maps')}
      </button>
      {loadingMaps && <button className="tools-button tools-button-secondary" type="button" onClick={() => mapRequest.current?.abort()}>{zh ? '取消读取' : 'Cancel load'}</button>}
    </div>
    {catalog && catalog.documents.length > 0 && <div className="gp-source-picker-grid">
      <label className="tools-field"><span>{zh ? '地图' : 'Map'}</span><select value={selectedMapID} onChange={event => changeMap(event.currentTarget.value)} disabled={loadingSource}>
        {catalog.documents.map(document => <option value={document.id} key={document.id}>{document.name} · {document.mapType}</option>)}
      </select></label>
      <label className="tools-field"><span>{zh ? 'GeoJSON 折线图层' : 'GeoJSON polyline layer'}</span><select value={selectedLayerID} onChange={event => { setSelectedLayerID(event.currentTarget.value); onSelectionChange({ capture: null, inputMode: 'All', selectedPathIDs: [] }); }} disabled={loadingSource || candidates.length === 0}>
        {candidates.length === 0 ? <option value="">{zh ? '当前地图没有可读图层' : 'No readable layer in this map'}</option>
          : candidates.map(candidate => <option value={candidate.id} key={candidate.id}>{candidate.node.name}</option>)}
      </select></label>
      <button className="tools-button tools-button-secondary" type="button" onClick={() => void loadSource()} disabled={loadingSource || loadingMaps || !selectedCandidate}>
        {loadingSource ? (zh ? '读取折线中…' : 'Reading polylines…') : (zh ? '读取所选折线' : 'Read selected polylines')}
      </button>
      {loadingSource && <button className="tools-button tools-button-secondary" type="button" onClick={() => { sourceGeneration.current += 1; sourceRequest.current?.abort(); sourceRequest.current = null; setLoadingSource(false); }}>{zh ? '取消读取' : 'Cancel read'}</button>}
    </div>}
    {selection.capture && <>
      <div className="tools-context-card gp-source-capture-card"><span className="tools-file-mark">GEO</span><div>
        <strong>{selection.capture.layerName}</strong><small>{selection.capture.mapName} · {selection.capture.lines.length} {zh ? '条路径' : 'paths'}</small>
        <small>{selection.capture.sourcePath}</small><small>{zh ? '来源文件修订' : 'Source file revision'} · {selection.capture.sourceRevision}</small>
        <small>{selection.capture.targetProjectionWKT ? (zh ? '已读取工程投影 WKT' : 'Project projection WKT captured') : (zh ? '本地工程坐标' : 'Local project coordinates')}</small>
      </div></div>
      <label className="tools-field tools-mode-field"><span>{zh ? '折线路径范围' : 'Polyline paths'}</span><select value={selection.inputMode} onChange={event => onSelectionChange({ ...selection, inputMode: event.currentTarget.value as 'All' | 'Selected' })}>
        <option value="All">{zh ? `全部路径（${selection.capture.lines.length}）` : `All paths (${selection.capture.lines.length})`}</option>
        <option value="Selected" disabled={selection.capture.lines.length > maximumSelectableLinePaths}>
          {selection.capture.lines.length > maximumSelectableLinePaths
            ? (zh ? `所选路径（超过交互选择上限 ${maximumSelectableLinePaths}）` : `Selected paths (over ${maximumSelectableLinePaths} path limit)`)
            : (zh ? `所选路径（${selection.selectedPathIDs.length}）` : `Selected paths (${selection.selectedPathIDs.length})`)}
        </option>
      </select></label>
      {selection.capture.lines.length > maximumSelectableLinePaths && <small className="tools-field-help">{zh ? '路径较多时仍可提交全部路径；为避免截断选择清单，本页不开放部分路径筛选。' : 'All paths remain available; partial selection is disabled to avoid truncating a large source list.'}</small>}
      {selection.inputMode === 'Selected' && selection.capture.lines.length <= maximumSelectableLinePaths && <div className="gp-source-path-selection">
        <div className="gp-source-path-selection-header"><strong>{zh ? '选择实际输入路径' : 'Choose input paths'}</strong>
          <button type="button" className="tools-text-button" onClick={() => selectAll(selection.selectedPathIDs.length !== selection.capture!.lines.length)}>
            {selection.selectedPathIDs.length === selection.capture.lines.length ? (zh ? '清除全选' : 'Clear all') : (zh ? '全选' : 'Select all')}
          </button>
        </div>
        {selection.capture.lines.map(line => <label className="gp-source-path-option" key={line.pathIdentity}>
          <input type="checkbox" checked={selection.selectedPathIDs.includes(line.pathIdentity)} onChange={event => togglePath(line.pathIdentity, event.currentTarget.checked)} />
          <span>{line.featureIdentity} · {zh ? '路径' : 'path'} {line.pathIndex + 1} · {line.coordinates.length} {zh ? '个顶点' : 'vertices'}</span>
        </label>)}
        {selection.selectedPathIDs.length === 0 && <small role="status">{zh ? '至少选择一条折线路径。' : 'Select at least one polyline path.'}</small>}
      </div>}
    </>}
    {error && <div className="tools-inline-error" role="alert">{error}{diagnostic && <details><summary>{zh ? '来源读取诊断' : 'Source read diagnostic'}</summary><pre>{diagnostic}</pre></details>}</div>}
    {!error && !catalog && <p className="tools-muted">{zh ? '尚未读取当前工程的已保存地图。' : 'Saved maps have not been loaded for the active project.'}</p>}
  </section>;
}
