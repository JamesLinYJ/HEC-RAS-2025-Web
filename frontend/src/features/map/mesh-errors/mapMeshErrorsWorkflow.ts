import type { BackendGateway, MapVisualizationCommitReceipt, WorkbenchContext } from '../../../shared/workbench';
import { mapCatalogMatchesDocument, sameMapID, sameMapLocator, sameMapRevision, stableMapValue } from '../associatedMapLayers';
import { loadMapDocumentCatalog, projectXmlSha } from '../ProjectTreeListener';
import type { LoadedMapDocumentCatalog } from '../ProjectTreeListener';
import type { MapAnalysisMapRequest, MapResultGeometryOwner } from '../analysis/mapAnalysisTypes';
import type { CatalogTreeNode, CatalogTreeNodeLocator, MapDocumentTab, MapExtent } from '../mapTypes';
import { mapResultSelectionOverridesFromCatalog } from '../useMapResultFrames';
import type { MapResultSourceCatalog, MapResultSelectionOverridePayload } from '../useMapResultFrames';
import { sameServerPath } from '../views/ViewManager';

const sha = /^[a-f\d]{64}$/i;
const record = (value: unknown): Record<string, unknown> | null =>
  value !== null && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : null;
const identity = (value: unknown) => JSON.stringify(stableMapValue(value));
const mismatch = () => new Error('地图网格检查的来源或修订已变化，请重新读取。');
const pathEqual = (left: string | null | undefined, right: string | null | undefined) =>
  typeof left === 'string' && typeof right === 'string' && sameServerPath(left, right);

export interface MapMeshErrorIssue {
  id: string;
  meshIndex: number;
  nodeIndex: number;
  faceCount: number;
  node: { x: number; y: number };
  cellsMinX: number | null;
  cellsMinY: number | null;
  cellsMaxX: number | null;
  cellsMaxY: number | null;
  cells: { cellIndex: number; coordinates: { x: number; y: number }[] }[];
}

export interface MapMeshErrorSnapshot {
  projectSessionID: string;
  mapWorkflowIdentity: string;
  contextToken: string;
  projectCanonicalPath: string;
  projectRevision: string;
  projectXmlSHA: string;
  visualizationRevision: string;
  previousVisualizationRevision: string;
  mapID: string;
  mapIndex: number;
  mapName: string;
  mapType: string;
  flowAreaNode: CatalogTreeNodeLocator;
  sourcePath: string;
  sourceRevision: string;
  checkMeshErrors: boolean;
  maxFacesPerNode: number;
  status: 'disabled' | 'available' | 'partial' | 'writerProtected' | 'savedReadbackUnavailable';
  diagnostic: string | null;
  issues: MapMeshErrorIssue[];
  totalIssueCount: number;
  omittedIssueCount: number;
}

export interface MapMeshErrorCandidate {
  key: string;
  context: WorkbenchContext;
  document: MapDocumentTab;
  map: MapAnalysisMapRequest;
  flow: CatalogTreeNode;
  geometry: CatalogTreeNodeLocator;
  result: CatalogTreeNodeLocator | null;
  sourcePath: string;
  expectedSourceRevision: string | null;
  contextToken: string;
  selectionOverrides: MapResultSelectionOverridePayload[];
}

export interface MapMeshErrorCapture extends MapMeshErrorCandidate {
  sourceRevision: string;
  request: {
    map: MapAnalysisMapRequest;
    flowAreaNode: CatalogTreeNodeLocator;
    projectSessionID: string;
    mapWorkflowIdentity: string;
    contextToken: string;
    sourcePath: string;
    sourceRevision: string;
    resultOwner: MapResultGeometryOwner | null;
    sourceTreeCapture: true;
    sourceTreeGeometryNode: CatalogTreeNodeLocator;
    sourceTreeResultSourcesContextToken: string | null;
  };
}

function entries(roots: CatalogTreeNode[], ancestors: CatalogTreeNode[] = []): { node: CatalogTreeNode; ancestors: CatalogTreeNode[] }[] {
  return roots.flatMap(node => [{ node, ancestors }, ...entries(node.children ?? [], [...ancestors, node])]);
}

/** Only null paths may acquire serialized indices when a sparse .vis node materializes. */
export function sameMaterializedMeshLocator(previous: CatalogTreeNodeLocator, next: CatalogTreeNodeLocator): boolean {
  if (!record(previous) || !record(next)) return false;
  const comparable = { ...previous };
  for (const field of ['nodePath', 'parentGeometryNodePath', 'parentResultNodePath'] as const) {
    const before = previous[field], after = next[field];
    if (identity(before ?? null) !== identity(after ?? null) &&
        !(before == null && Array.isArray(after) && after.length > 0 && after.every(index => Number.isInteger(index) && index >= 0))) return false;
    comparable[field] = after;
  }
  return sameMapLocator(comparable, next);
}

/** The selected node must belong to this exact saved map and Host-resolved Result selection tree. */
export function mapMeshErrorCandidate(context: WorkbenchContext, document: MapDocumentTab | null,
  catalog: MapResultSourceCatalog | null, map: MapAnalysisMapRequest | null, nodeID: string | null): MapMeshErrorCandidate | null {
  if (!document || !catalog || !map || !nodeID || !mapCatalogMatchesDocument(catalog, context, document) ||
      !context.projectPath || !context.projectXml || !context.projectRevision ||
      !sha.test(catalog.contextToken) || map.projectSessionID !== context.projectSessionID ||
      !pathEqual(map.projectPath, context.projectPath) || map.projectXml !== context.projectXml ||
      !sameMapRevision(map.projectXmlSHA, catalog.projectXmlSHA) || !sameMapID(map.mapID, document.mapID) ||
      map.mapIndex !== document.mapIndex || map.mapName !== document.name || map.mapType !== document.mapType ||
      !sameMapRevision(map.expectedVisualizationRevision, document.visualizationRevision ?? '')) return null;
  const matches = entries(catalog.treeRoots).filter(entry => entry.node.id === nodeID);
  if (matches.length !== 1) return null;
  const { node: flow, ancestors } = matches[0];
  if (flow.sourceClass !== 'FlowAreaLayerVisualContext' || flow.locator?.nodeType !== 'FlowAreaLayer') return null;
  const geometry = [...ancestors].reverse().find(node => node.locator?.nodeType === 'Geometry')?.locator;
  const result = [...ancestors].reverse().find(node => ['Result', 'MultiResult'].includes(node.locator?.nodeType ?? ''))?.locator ?? null;
  const sourcePath = flow.locator.parentGeometryAssetID;
  if (!geometry || !sourcePath || !pathEqual(geometry.assetID, sourcePath) ||
      geometry.nodeName !== flow.locator.parentGeometryNodeName || geometry.rootName !== flow.locator.rootName ||
      identity(geometry.nodePath ?? null) !== identity(flow.locator.parentGeometryNodePath ?? null)) return null;
  const expectedSourceRevision = result ? flow.locator.parentResultRevision ?? null : null;
  if (result && (!sha.test(expectedSourceRevision ?? '') || !pathEqual(flow.locator.parentResultAssetID, sourcePath) ||
      !pathEqual(geometry.parentResultAssetID, sourcePath) ||
      !sameMapRevision(geometry.parentResultRevision ?? '', expectedSourceRevision ?? '') ||
      flow.locator.parentResultNodeType !== result.nodeType || flow.locator.parentResultNodeName !== result.nodeName ||
      identity(flow.locator.parentResultNodePath ?? null) !== identity(result.nodePath ?? null))) return null;
  if (!result && (flow.locator.parentResultAssetID || flow.locator.parentResultNodeType)) return null;
  if (result?.nodeType === 'MultiResult') {
    const pinned = catalog.treeSelectionOverrides.filter(choice => choice.planName === result.nodeName &&
      typeof choice.planAssetID === 'string' && pathEqual(choice.planAssetID, result.assetID));
    if (pinned.length !== 1 || typeof pinned[0].selectedResultPath !== 'string' ||
        !pathEqual(pinned[0].selectedResultPath, sourcePath) ||
        typeof pinned[0].selectedResultRevision !== 'string' ||
        !sameMapRevision(pinned[0].selectedResultRevision, expectedSourceRevision ?? '')) return null;
  }
  // Do not hash a source currently advertised as writer-owned, even for a hidden tree node.
  if (catalog.activeNodes.some(node => node.selections.some(selection => selection.selectedResult?.isComputing &&
      pathEqual(selection.selectedResult.resultPath, sourcePath)))) return null;
  const selectionOverrides = mapResultSelectionOverridesFromCatalog(catalog);
  const capturedMap = { ...map, resultSelectionOverrides: catalog.treeSelectionOverrides };
  const key = identity([context.projectSessionID, context.projectRevision, context.selectedAsset, capturedMap, flow.locator, geometry, result,
    catalog.contextToken, sourcePath, expectedSourceRevision, selectionOverrides]);
  return { key, context, document, map: capturedMap, flow, geometry, result, sourcePath,
    expectedSourceRevision, contextToken: catalog.contextToken, selectionOverrides };
}

export async function captureMapMeshErrors(backend: BackendGateway, candidate: MapMeshErrorCandidate,
  signal: AbortSignal): Promise<MapMeshErrorCapture> {
  const inspected = record(await backend.request<unknown>('workspace.file.inspect', { sourcePath: candidate.sourcePath }, { signal }));
  if (!inspected || typeof inspected.canonicalPath !== 'string' || !pathEqual(inspected.canonicalPath, candidate.sourcePath) ||
      typeof inspected.revision !== 'string' || !sha.test(inspected.revision) ||
      (candidate.expectedSourceRevision && !sameMapRevision(inspected.revision, candidate.expectedSourceRevision))) throw mismatch();
  if (signal.aborted) throw new DOMException('已取消读取。', 'AbortError');
  const sourceRevision = inspected.revision;
  const resultOwner: MapResultGeometryOwner | null = candidate.result ? {
    resultNode: candidate.result, contextToken: candidate.contextToken,
    selectionOverrides: [{ node: candidate.result, selectionIndex: 0, mode: 'Explicit',
      resultPath: candidate.sourcePath, resultRevision: sourceRevision }],
    selectionIndex: 0, selectedResultPath: candidate.sourcePath, selectedResultRevision: sourceRevision,
  } : null;
  const request: MapMeshErrorCapture['request'] = {
    map: candidate.map, flowAreaNode: candidate.flow.locator!, projectSessionID: candidate.context.projectSessionID,
    mapWorkflowIdentity: identity(['map.mesh-errors', candidate.key, sourceRevision]), contextToken: candidate.contextToken,
    sourcePath: candidate.sourcePath, sourceRevision, resultOwner, sourceTreeCapture: true,
    sourceTreeGeometryNode: candidate.geometry,
    sourceTreeResultSourcesContextToken: resultOwner ? candidate.contextToken : null,
  };
  return { ...candidate, sourceRevision, request };
}

const finite = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value);
const integer = (value: unknown): value is number => finite(value) && Number.isInteger(value) && value >= 0;
function coordinate(value: unknown): value is { x: number; y: number } {
  const item = record(value);
  return !!item && finite(item.x) && finite(item.y);
}

export function normalizeMeshThreshold(entry: number): number {
  if (!Number.isFinite(entry)) throw new Error('请输入有限的节点边数。');
  return Math.min(64, Math.max(3, Math.sign(entry) * Math.floor(Math.abs(entry) + 0.5)));
}

/** Validate the source-scoped response before deriving an ordinary root .vis receipt. */
export function parseMapMeshErrorSnapshot(value: unknown, capture: MapMeshErrorCapture,
  action: 'load' | 'set', expectedSettings?: { enabled: boolean; threshold: number }): MapMeshErrorSnapshot {
  const item = record(value), request = capture.request, map = request.map;
  if (!item || item.projectSessionID !== request.projectSessionID || item.mapWorkflowIdentity !== request.mapWorkflowIdentity ||
      item.contextToken !== request.contextToken || typeof item.projectCanonicalPath !== 'string' ||
      !pathEqual(item.projectCanonicalPath, map.projectPath) || !sameMapRevision(String(item.projectRevision), capture.context.projectRevision ?? '') ||
      !sameMapRevision(String(item.projectXmlSHA), map.projectXmlSHA) || !sameMapID(String(item.mapID), map.mapID) ||
      item.mapIndex !== map.mapIndex || item.mapName !== map.mapName || item.mapType !== map.mapType ||
      typeof item.sourcePath !== 'string' || !pathEqual(item.sourcePath, request.sourcePath) ||
      !sameMapRevision(String(item.sourceRevision), request.sourceRevision) ||
      !record(item.flowAreaNode) || !(action === 'set'
        ? sameMaterializedMeshLocator(request.flowAreaNode, item.flowAreaNode as unknown as CatalogTreeNodeLocator)
        : sameMapLocator(request.flowAreaNode, item.flowAreaNode as unknown as CatalogTreeNodeLocator)) ||
      typeof item.visualizationRevision !== 'string' || !sha.test(item.visualizationRevision) ||
      !sameMapRevision(String(item.previousVisualizationRevision), map.expectedVisualizationRevision) ||
      (action === 'load' && !sameMapRevision(item.visualizationRevision, map.expectedVisualizationRevision)) ||
      typeof item.checkMeshErrors !== 'boolean' || !integer(item.maxFacesPerNode) || item.maxFacesPerNode < 3 || item.maxFacesPerNode > 64 ||
      !['disabled', 'available', 'partial', 'writerProtected', 'savedReadbackUnavailable'].includes(String(item.status)) ||
      (item.status === 'savedReadbackUnavailable' && action !== 'set') ||
      (item.diagnostic != null && typeof item.diagnostic !== 'string') ||
      !integer(item.totalIssueCount) || !integer(item.omittedIssueCount) || !Array.isArray(item.issues) ||
      item.issues.length > 10000 || item.issues.length + item.omittedIssueCount !== item.totalIssueCount) throw mismatch();
  if (expectedSettings && (item.checkMeshErrors !== expectedSettings.enabled ||
      item.maxFacesPerNode !== normalizeMeshThreshold(expectedSettings.threshold))) throw mismatch();
  const issues = item.issues as unknown[];
  const ids = new Set<string>();
  let previousFaces = Infinity;
  for (const raw of issues) {
    const issue = record(raw);
    if (!issue || typeof issue.id !== 'string' || !issue.id || ids.has(issue.id) ||
        !integer(issue.meshIndex) || !integer(issue.nodeIndex) || !integer(issue.faceCount) ||
        issue.faceCount <= item.maxFacesPerNode || issue.faceCount > previousFaces || !coordinate(issue.node) || !Array.isArray(issue.cells)) throw mismatch();
    const extents = [issue.cellsMinX, issue.cellsMinY, issue.cellsMaxX, issue.cellsMaxY];
    if (!extents.every(value => value == null) && (!extents.every(finite) ||
        (issue.cellsMinX as number) > (issue.cellsMaxX as number) || (issue.cellsMinY as number) > (issue.cellsMaxY as number))) throw mismatch();
    if (issue.cells.some(rawCell => {
      const cell = record(rawCell);
      return !cell || !integer(cell.cellIndex) || !Array.isArray(cell.coordinates) ||
        cell.coordinates.length < 3 || !cell.coordinates.every(coordinate);
    })) throw mismatch();
    ids.add(issue.id);
    previousFaces = issue.faceCount;
  }
  if (item.status === 'disabled' && item.checkMeshErrors ||
      ['disabled', 'writerProtected', 'savedReadbackUnavailable'].includes(String(item.status)) && item.totalIssueCount !== 0 ||
      ['available', 'partial'].includes(String(item.status)) && !item.checkMeshErrors ||
      item.status === 'available' && item.omittedIssueCount !== 0 ||
      item.status === 'partial' && item.omittedIssueCount === 0) throw mismatch();
  return item as unknown as MapMeshErrorSnapshot;
}

export function meshErrorWriteReceipt(snapshot: MapMeshErrorSnapshot): MapVisualizationCommitReceipt {
  return { mapIndex: snapshot.mapIndex, mapName: snapshot.mapName, mapType: snapshot.mapType,
    previousVisualizationRevision: snapshot.previousVisualizationRevision, visualizationRevision: snapshot.visualizationRevision };
}

export async function loadCapturedMeshErrors(backend: BackendGateway, capture: MapMeshErrorCapture, signal: AbortSignal) {
  return parseMapMeshErrorSnapshot(await backend.request<unknown>('map.tree.meshErrors', {
    ...capture.request, action: 'load',
  }, { signal }), capture, 'load');
}

export interface MapMeshErrorReadback {
  catalog: LoadedMapDocumentCatalog;
  document: MapDocumentTab;
  capture: MapMeshErrorCapture;
  snapshot: MapMeshErrorSnapshot;
}

/** A fresh catalog, source SHA and scan are required even when set already returned issues. */
export async function readbackMapMeshErrors(backend: BackendGateway, previous: MapMeshErrorCapture,
  signal: AbortSignal, saved?: MapMeshErrorSnapshot): Promise<MapMeshErrorReadback> {
  const catalog = await loadMapDocumentCatalog(backend, previous.context, { signal });
  const document = catalog.documents.find(item => sameMapID(item.mapID, previous.map.mapID) &&
    item.mapIndex === previous.map.mapIndex && item.name === previous.map.mapName && item.mapType === previous.map.mapType);
  if (!document?.visualizationRevision || (saved && !sameMapRevision(document.visualizationRevision, saved.visualizationRevision))) throw mismatch();
  const map: MapAnalysisMapRequest = { ...previous.map, expectedVisualizationRevision: document.visualizationRevision,
    projectXmlSHA: (await projectXmlSha(previous.context.projectXml))! };
  const raw = record(await backend.request<unknown>('map.resultSources', { map,
    selectionOverrides: previous.selectionOverrides }, { signal }));
  if (!raw || !Array.isArray(raw.treeRoots) || !Array.isArray(raw.treeSelectionOverrides) || !Array.isArray(raw.activeNodes) ||
      typeof raw.projectPath !== 'string' || typeof raw.projectRevision !== 'string' || typeof raw.contextToken !== 'string' ||
      typeof raw.projectXmlSHA !== 'string' || !sameMapRevision(raw.projectXmlSHA, map.projectXmlSHA)) throw mismatch();
  const sources = raw as unknown as MapResultSourceCatalog;
  if (!mapCatalogMatchesDocument(sources, previous.context, document)) throw mismatch();
  const locator = saved?.flowAreaNode ?? previous.request.flowAreaNode;
  const matches = entries(sources.treeRoots).filter(entry => entry.node.locator &&
    sameMaterializedMeshLocator(locator, entry.node.locator));
  if (matches.length !== 1) throw mismatch();
  const candidate = mapMeshErrorCandidate(previous.context, document, sources, map, matches[0].node.id);
  if (!candidate || !pathEqual(candidate.sourcePath, previous.sourcePath)) throw mismatch();
  const capture = await captureMapMeshErrors(backend, candidate, signal);
  if (!sameMapRevision(capture.sourceRevision, previous.sourceRevision)) throw mismatch();
  const snapshot = await loadCapturedMeshErrors(backend, capture, signal);
  if (saved && (snapshot.checkMeshErrors !== saved.checkMeshErrors || snapshot.maxFacesPerNode !== saved.maxFacesPerNode)) throw mismatch();
  return { catalog, document: { ...document, roots: sources.treeRoots }, capture, snapshot };
}

/** MeshErrorProperties.cs uses CellsExtent.RatioAroundCenter(2.0), then NodePoint. */
export function meshErrorZoomExtent(issue: MapMeshErrorIssue): MapExtent | null {
  if (![issue.cellsMinX, issue.cellsMinY, issue.cellsMaxX, issue.cellsMaxY].every(finite)) return null;
  const minX = issue.cellsMinX!, minY = issue.cellsMinY!, maxX = issue.cellsMaxX!, maxY = issue.cellsMaxY!;
  if (maxX <= minX || maxY <= minY) return null;
  const cx = minX / 2 + maxX / 2, cy = minY / 2 + maxY / 2;
  const extent = { minX: cx - (maxX - minX), minY: cy - (maxY - minY),
    maxX: cx + (maxX - minX), maxY: cy + (maxY - minY) };
  return Object.values(extent).every(finite) ? extent : null;
}
