import { useEffect, useMemo, useRef, useState } from 'react';
import type { BackendGateway, Language, WorkbenchContext } from '../../../shared/workbench';
import { errorDiagnostics, text } from '../../../shared/workbench';
import { loadMapDocumentCatalog, projectXmlSha } from '../../map/ProjectTreeListener';
import type { CatalogTreeNode, CatalogTreeNodeLocator, MapDocumentTab } from '../../map/mapTypes';
import { isShapefileFamilyCapture, sameSourcePath, type AttributeTableSourceFamilyCapture } from '../attributes/attributeTableTypes';

export interface GeoprocessingPointInput {
  featureIdentity: string;
  pathIdentity: string;
  sourceFeatureIndex: number;
  pathIndex: number;
  coordinates: number[];
}

export interface GeoprocessingPointCapture {
  owner: string;
  sourcePath: string;
  sourceRevision: string;
  sourceFamily: AttributeTableSourceFamilyCapture | null;
  sourceFamilyKind: 'geojson' | 'shapefile';
  targetProjectionWKT: string | null;
  projectionStatus: string;
  mapName: string;
  layerName: string;
  points: GeoprocessingPointInput[];
}

export interface GeoprocessingPointSelection {
  capture: GeoprocessingPointCapture | null;
  inputMode: 'All' | 'Selected';
  selectedPathIDs: string[];
}

type Candidate = { id: string; node: CatalogTreeNode; locator: CatalogTreeNodeLocator; document: MapDocumentTab };
type SourceFile = { path: string; inputPath: string; role: string; required: boolean; exists: boolean; revision: string | null };

function isRecord(value: unknown): value is Record<string, unknown> { return Boolean(value) && typeof value === 'object' && !Array.isArray(value); }
function isSha256(value: unknown): value is string { return typeof value === 'string' && /^[\da-f]{64}$/i.test(value); }
function extension(path: string | null | undefined): string {
  const leaf = path?.split(/[?#]/, 1)[0]?.split(/[\\/]/).at(-1) ?? '';
  const index = leaf.lastIndexOf('.'); return index < 0 ? '' : leaf.slice(index).toLowerCase();
}
function sameLocator(left: CatalogTreeNodeLocator, right: CatalogTreeNodeLocator): boolean {
  return left.rootName === right.rootName && left.nodeType === right.nodeType && left.nodeName === right.nodeName &&
    left.mapType === right.mapType && left.assetID === right.assetID && left.filename === right.filename &&
    left.filenameRelative === right.filenameRelative && JSON.stringify(left.nodePath ?? null) === JSON.stringify(right.nodePath ?? null);
}
function collectCandidates(document: MapDocumentTab): Candidate[] {
  const result: Candidate[] = [];
  const visit = (nodes: CatalogTreeNode[]): void => {
    for (const node of nodes) {
      const locator = node.locator;
      const ext = extension(locator?.filename) || extension(locator?.filenameRelative);
      if (locator?.nodeType === 'MapLayer' && locator.mapType === 'PointsVector' &&
          Array.isArray(locator.nodePath) && locator.nodePath.length > 0 &&
          locator.nodePath.every(value => Number.isInteger(value) && value >= 0) &&
          ['.shp', '.geojson', '.json'].includes(ext) && typeof node.assetID === 'string' && node.assetID.trim()) {
        result.push({ id: JSON.stringify([document.mapIndex, locator.nodePath, locator.nodeName, node.assetID]), node, locator, document });
      }
      if (node.children?.length) visit(node.children);
    }
  };
  visit(document.roots); return result;
}

function parsePointSource(value: unknown, context: WorkbenchContext, document: MapDocumentTab,
  candidate: Candidate, owner: string, projectXmlSHA: string | null): GeoprocessingPointCapture {
  if (!isRecord(value) || !isRecord(value.source) || !isRecord(value.data)) {
    throw new Error('The point-source response is incomplete.');
  }
  const source = value.source;
  const map = source.map;
  const locatorValue = source.locator;
  const family = source.family;
  const data = value.data;
  if (!isRecord(map) || !isRecord(locatorValue) || !isRecord(family)) {
    throw new Error('The point-source response is incomplete.');
  }
  const locator = locatorValue as unknown as CatalogTreeNodeLocator;
  const familyName = family.family, sourcePath = family.sourcePath, familyRevision = family.revision;
  const familyFiles = family.files;
  if (typeof map.projectPath !== 'string' || !isSha256(map.projectRevision) ||
      !(map.projectXmlSHA === null || isSha256(map.projectXmlSHA)) ||
      !(map.visualizationRevision === null || isSha256(map.visualizationRevision)) ||
      map.mapID !== document.mapID || map.mapIndex !== document.mapIndex || map.mapName !== document.name ||
      map.mapType !== document.mapType || map.isPersisted !== true ||
      !context.projectPath || !context.projectRevision || map.projectPath !== context.projectPath ||
      map.projectRevision.toUpperCase() !== context.projectRevision.toUpperCase() ||
      (map.projectXmlSHA as string | null)?.toUpperCase() !== projectXmlSHA?.toUpperCase() ||
      (map.visualizationRevision as string | null)?.toUpperCase() !== document.visualizationRevision?.toUpperCase() ||
      !sameLocator(locator, candidate.locator) ||
      (familyName !== 'geojson' && familyName !== 'shapefile') || typeof sourcePath !== 'string' || !sourcePath.trim() ||
      !isSha256(familyRevision) || !Array.isArray(familyFiles) || data.shapeType !== 'Point' ||
      !Number.isInteger(data.count) || !Array.isArray(data.features) || data.count !== data.features.length ||
      typeof data.projectionStatus !== 'string' ||
      !(data.targetProjectionWKT === null || typeof data.targetProjectionWKT === 'string')) {
    throw new Error('The point-source receipt does not match the saved project and map node.');
  }
  const ext = extension(sourcePath);
  if ((familyName === 'geojson' && !['.geojson', '.json'].includes(ext)) || (familyName === 'shapefile' && ext !== '.shp')) {
    throw new Error('The point-source family and canonical path do not match.');
  }
  const files = familyFiles.filter((file): file is SourceFile => isRecord(file) && typeof file.path === 'string' &&
    typeof file.inputPath === 'string' && typeof file.role === 'string' && typeof file.required === 'boolean' &&
    typeof file.exists === 'boolean' && (file.revision === null || isSha256(file.revision)));
  const primary = files.filter(file => file.role === 'source' && file.required && file.exists && isSha256(file.revision));
  if (familyName === 'geojson') {
    if (familyFiles.length !== 1 || primary.length !== 1 || !sameSourcePath(primary[0]!.path, sourcePath)) {
      throw new Error('The GeoJSON source does not have one matching primary-file revision.');
    }
  } else if (primary.length !== 1 || !isShapefileFamilyCapture(family, sourcePath, primary[0]!.revision!)) {
    throw new Error('The Host did not return a complete, matching Shapefile family capture.');
  }
  const targetProjectionWKT = data.targetProjectionWKT;
  if ((data.projectionStatus === 'projected' && (typeof targetProjectionWKT !== 'string' || !targetProjectionWKT.trim())) ||
      !['projected', 'localProjectSourceCoordinates'].includes(data.projectionStatus)) {
    throw new Error('The point coordinates have no verified project-space mapping.');
  }
  const points: GeoprocessingPointInput[] = [];
  const seen = new Set<string>();
  for (const feature of data.features) {
    if (!isRecord(feature) || typeof feature.sourceOrdinal !== 'number' || !Number.isInteger(feature.sourceOrdinal) || feature.sourceOrdinal < 0 ||
        !(feature.sourceID === null || typeof feature.sourceID === 'string') || feature.shapeType !== 'Point' ||
        !Array.isArray(feature.parts) || feature.parts.length > 1) throw new Error('The Host returned an invalid point record.');
    if (feature.parts.length === 0) continue;
    const part = feature.parts[0];
    if (!isRecord(part) || typeof part.partOrdinal !== 'number' || !Number.isInteger(part.partOrdinal) || part.partOrdinal < 0 || !Array.isArray(part.vertices) || part.vertices.length !== 1) {
      throw new Error('The Host returned a point feature with an invalid geometry part.');
    }
    const vertex = part.vertices[0];
    if (!isRecord(vertex) || typeof vertex.x !== 'number' || !Number.isFinite(vertex.x) ||
        typeof vertex.y !== 'number' || !Number.isFinite(vertex.y) ||
        typeof vertex.sourceX !== 'number' || !Number.isFinite(vertex.sourceX) ||
        typeof vertex.sourceY !== 'number' || !Number.isFinite(vertex.sourceY)) {
      throw new Error('The Host returned a point without finite source and project coordinates.');
    }
    const index = feature.sourceOrdinal as number;
    const featureIdentity = familyName === 'geojson' && typeof feature.sourceID === 'string' && feature.sourceID.trim()
      ? `geojson-feature-id:${feature.sourceID}` : `${familyName}-feature:${index}`;
    const pathIdentity = `${familyName}-path:${index}:${part.partOrdinal as number}`;
    if (seen.has(pathIdentity)) throw new Error('The Host returned a duplicate point path identity.');
    seen.add(pathIdentity);
    points.push({ featureIdentity, pathIdentity, sourceFeatureIndex: index, pathIndex: part.partOrdinal as number,
      coordinates: [vertex.x, vertex.y] });
  }
  if (!points.length) throw new Error('The saved point layer contains no non-empty point records.');
  return { owner, sourcePath, sourceRevision: primary[0]!.revision!,
    sourceFamily: familyName === 'shapefile' ? family as unknown as AttributeTableSourceFamilyCapture : null,
    sourceFamilyKind: familyName, targetProjectionWKT: targetProjectionWKT as string | null,
    projectionStatus: data.projectionStatus, mapName: document.name, layerName: candidate.node.name, points };
}

export function GeoprocessingPointSourcePicker({ language, context, backend, owner, selection, onSelectionChange }: {
  language: Language; context: WorkbenchContext; backend: BackendGateway; owner: string; selection: GeoprocessingPointSelection;
  onSelectionChange: (selection: GeoprocessingPointSelection) => void;
}) {
  const zh = language === 'zh';
  const [documents, setDocuments] = useState<MapDocumentTab[] | null>(null);
  const [selectedMapID, setSelectedMapID] = useState('');
  const [selectedLayerID, setSelectedLayerID] = useState('');
  const [loadingMaps, setLoadingMaps] = useState(false);
  const [loadingSource, setLoadingSource] = useState(false);
  const [error, setError] = useState<string>();
  const [diagnostic, setDiagnostic] = useState<string>();
  const generation = useRef(0);
  const request = useRef<AbortController | null>(null);
  const ownerRef = useRef(owner); ownerRef.current = owner;
  const currentMap = documents?.find(document => document.id === selectedMapID) ?? null;
  const candidates = useMemo(() => currentMap ? collectCandidates(currentMap) : [], [currentMap]);
  const selected = candidates.find(candidate => candidate.id === selectedLayerID) ?? candidates[0] ?? null;
  const current = (token: number, expectedOwner: string, signal: AbortSignal): boolean =>
    generation.current === token && ownerRef.current === expectedOwner && !signal.aborted;
  const abort = (): void => { generation.current += 1; request.current?.abort(); request.current = null; setLoadingMaps(false); setLoadingSource(false); };

  useEffect(() => {
    generation.current += 1; request.current?.abort(); request.current = null;
    setDocuments(null); setSelectedMapID(''); setSelectedLayerID(''); setLoadingMaps(false); setLoadingSource(false);
    setError(undefined); setDiagnostic(undefined);
    onSelectionChange({ capture: null, inputMode: 'All', selectedPathIDs: [] });
  }, [owner, onSelectionChange]);
  useEffect(() => () => { generation.current += 1; request.current?.abort(); }, []);

  const readMaps = async (): Promise<void> => {
    abort(); setError(undefined); setDiagnostic(undefined); setDocuments(null); setSelectedMapID(''); setSelectedLayerID('');
    onSelectionChange({ capture: null, inputMode: 'All', selectedPathIDs: [] });
    if (!context.projectPath || !context.projectRevision || !context.projectXml || !backend.supports('map.document.list') || !backend.supports('map.tree.load')) {
      setError(text(language, '请先打开已保存工程，并确认服务支持读取地图目录。', 'Open a saved project and confirm saved-map reading is available.')); return;
    }
    const expectedOwner = owner, token = ++generation.current, controller = new AbortController(); request.current = controller; setLoadingMaps(true);
    try {
      const catalog = await loadMapDocumentCatalog(backend, context, { signal: controller.signal });
      if (!current(token, expectedOwner, controller.signal)) return;
      setDocuments(catalog.documents);
      const active = catalog.documents.find(document => document.mapIndex === catalog.activeMapIndex) ?? catalog.documents[0];
      setSelectedMapID(active?.id ?? '');
      if (!catalog.documents.length) setError(text(language, '当前工程没有已保存地图。', 'The project has no saved maps.'));
      else if (!catalog.documents.some(document => collectCandidates(document).length)) setError(text(language,
        '已保存地图中没有可读的 GeoJSON 或 Shapefile 点图层。', 'No readable GeoJSON or Shapefile point layer is available in the saved maps.'));
    } catch (caught) {
      if (!current(token, expectedOwner, controller.signal)) return;
      setError(text(language, '读取已保存地图目录失败。', 'Could not read the saved-map catalog.'));
      const details = errorDiagnostics(caught); setDiagnostic(typeof details === 'string' ? details : JSON.stringify(details, null, 2));
    } finally {
      if (token === generation.current) { setLoadingMaps(false); if (request.current === controller) request.current = null; }
    }
  };

  const readSource = async (): Promise<void> => {
    if (!selected || !currentMap || !context.projectPath || !context.projectRevision || !context.projectXml) return;
    abort(); setError(undefined); setDiagnostic(undefined); onSelectionChange({ capture: null, inputMode: 'All', selectedPathIDs: [] });
    if (!backend.supports('map.vector.source.load')) { setError(text(language, '当前服务未开放真实地图点读取。', 'The current service does not expose saved-map point reading.')); return; }
    const expectedOwner = owner, token = ++generation.current, controller = new AbortController(); request.current = controller; setLoadingSource(true);
    try {
      const xmlSHA = await projectXmlSha(context.projectXml);
      if (!current(token, expectedOwner, controller.signal)) return;
      const map = { projectPath: context.projectPath, projectXml: context.projectXml, projectXmlSHA: xmlSHA,
        mapID: currentMap.mapID, mapIndex: currentMap.mapIndex, mapName: currentMap.name, mapType: currentMap.mapType,
        expectedVisualizationRevision: currentMap.visualizationRevision, projectSessionID: context.projectSessionID };
      const raw = await backend.request('map.vector.source.load', { map, node: selected.locator, expectedFamily: null }, { signal: controller.signal });
      if (!current(token, expectedOwner, controller.signal)) return;
      const capture = parsePointSource(raw, context, currentMap, selected, expectedOwner, xmlSHA);
      onSelectionChange({ capture, inputMode: 'All', selectedPathIDs: [] });
    } catch (caught) {
      if (!current(token, expectedOwner, controller.signal)) return;
      setError(text(language, '读取点几何或校验来源身份失败。', 'Could not read the point geometry or verify its source identity.'));
      const details = errorDiagnostics(caught); setDiagnostic(typeof details === 'string' ? details : JSON.stringify(details, null, 2));
    } finally {
      if (token === generation.current) { setLoadingSource(false); if (request.current === controller) request.current = null; }
    }
  };

  const points = selection.capture?.owner === owner ? selection.capture.points : [];
  const selectedIDs = selection.inputMode === 'All' ? points.map(point => point.pathIdentity) : selection.selectedPathIDs;
  const toggleAll = (all: boolean): void => onSelectionChange({ ...selection, inputMode: all ? 'All' : 'Selected', selectedPathIDs: all ? [] : [] });
  const togglePoint = (id: string, checked: boolean): void => {
    const ids = new Set(selectedIDs); if (checked) ids.add(id); else ids.delete(id);
    onSelectionChange({ ...selection, inputMode: 'Selected', selectedPathIDs: [...ids] });
  };

  return <section className="tools-input-details gp-source-picker gp-point-source-picker" data-testid="geoprocessing-point-source-picker">
    <div className="tools-subheading-row"><h4>{zh ? '点来源' : 'Point source'}</h4><span className="tools-badge">{zh ? '已保存地图' : 'Saved map'}</span></div>
    <p className="tools-field-help">{zh ? '从已保存地图节点读取实际点坐标和文件族修订；坐标不会由浏览器手工生成。' : 'Read actual point coordinates and source-family revisions from a saved map node. The browser does not synthesize coordinates.'}</p>
    <div className="gp-source-picker-actions"><button className="tools-button tools-button-secondary" type="button" onClick={() => void readMaps()} disabled={loadingMaps || loadingSource || !context.projectPath}>
      {loadingMaps ? (zh ? '读取地图中…' : 'Loading maps…') : (zh ? '读取已保存地图' : 'Load saved maps')}
    </button></div>
    {documents && documents.length > 0 && <div className="gp-source-picker-grid">
      <label className="tools-field"><span>{zh ? '地图' : 'Map'}</span><select value={selectedMapID} onChange={event => {
        abort(); setSelectedMapID(event.currentTarget.value); setSelectedLayerID(''); setError(undefined); setDiagnostic(undefined);
        onSelectionChange({ capture: null, inputMode: 'All', selectedPathIDs: [] });
      }} disabled={loadingSource}>{documents.map(document => <option value={document.id} key={document.id}>{document.name} · {document.mapType}</option>)}</select></label>
      <label className="tools-field"><span>{zh ? '点图层' : 'Point layer'}</span><select value={selected?.id ?? ''} onChange={event => {
        abort(); setSelectedLayerID(event.currentTarget.value); setError(undefined); setDiagnostic(undefined);
        onSelectionChange({ capture: null, inputMode: 'All', selectedPathIDs: [] });
      }} disabled={loadingSource || !candidates.length}>
        {candidates.length ? candidates.map(candidate => <option key={candidate.id} value={candidate.id}>{candidate.node.name} · {candidate.locator.filenameRelative ?? candidate.locator.filename}</option>)
          : <option value="">{zh ? '当前地图没有可读点图层' : 'No readable point layer in this map'}</option>}
      </select></label>
      <button className="tools-button tools-button-secondary" type="button" onClick={() => void readSource()} disabled={!selected || loadingSource || loadingMaps}>
        {loadingSource ? (zh ? '读取点数据…' : 'Reading points…') : (zh ? '读取并核验点数据' : 'Read and verify points')}
      </button>
    </div>}
    {selection.capture?.owner === owner && <>
      <div className="gp-point-source-summary"><strong>{selection.capture.mapName} · {selection.capture.layerName}</strong><small>{selection.capture.sourcePath}</small><small>SHA-256 · {selection.capture.sourceRevision}</small></div>
      <label className="tools-field tools-mode-field"><span>{zh ? '参与处理的点' : 'Points to process'}</span><select value={selection.inputMode} onChange={event => toggleAll(event.currentTarget.value === 'All')}>
        <option value="All">{zh ? `全部 ${points.length} 个点` : `All ${points.length} points`}</option>
        <option value="Selected">{zh ? '逐项选择' : 'Select points individually'}</option>
      </select></label>
      {selection.inputMode === 'Selected' && <div className="gp-point-selection-list" role="group" aria-label={zh ? '选择参与处理的点' : 'Select points to process'}>
        {points.map(point => <label key={point.pathIdentity}><input type="checkbox" checked={selection.selectedPathIDs.includes(point.pathIdentity)}
          onChange={event => togglePoint(point.pathIdentity, event.currentTarget.checked)} />{point.pathIdentity}</label>)}
      </div>}
      <p className="tools-field-help">{zh ? `本次选择 ${selectedIDs.length} / ${points.length} 个点；原地图来源只读。` : `${selectedIDs.length} of ${points.length} captured points selected; the saved map source is read-only.`}</p>
    </>}
    {error && <div className="tools-inline-error" role="alert">{error}{diagnostic && <details><summary>{zh ? '原始诊断' : 'Raw diagnostic'}</summary><pre>{diagnostic}</pre></details>}</div>}
    {!documents && !loadingMaps && <p className="tools-muted">{zh ? '请先读取已保存地图。' : 'Read the saved maps first.'}</p>}
  </section>;
}
