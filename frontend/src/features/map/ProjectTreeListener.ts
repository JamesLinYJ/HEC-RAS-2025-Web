import { BackendOperationUnavailableError } from '../../shared/workbench';
import type { BackendGateway, BackendRequestOptions, WorkbenchContext } from '../../shared/workbench';
import type { CatalogTreeNode, CatalogTreeNodeLocator, MapDocumentTab } from './mapTypes';

export interface MapDocumentReceipt {
  mapIndex: number;
  mapName: string;
  mapType: string;
  previousVisualizationRevision: string | null;
  visualizationRevision: string;
}

export interface MapTreeSnapshot {
  roots: CatalogTreeNode[];
  previousVisualizationRevision: string | null;
  visualizationRevision: string;
  expandedNodeIDs: string[];
  selectedNodeID: string | null;
  hiddenLayerIDs: string[];
}

export interface MapSourceRemovalCandidate {
  sourcePath: string;
  sourceRevision: string;
  nodeType: 'MapLayer';
  mapType: string;
}

export type MapSourceRemovalCheck =
  | { canRemove: true; candidate: MapSourceRemovalCandidate }
  | { canRemove: false; reason: 'unsupported' | 'missing-source' | 'stale-source' };

const removableVectorMapTypes = new Set(['PointsVector', 'PolylinesVector', 'PolygonsVector']);
const removableRasterMapTypes = new Set(['DiscreteRasterMapLayer', 'ImageRasterMapLayer', 'SingleBandRasterMapLayer']);
const sha256Pattern = /^[\da-f]{64}$/i;

function stableJSON(value: unknown): string {
  if (value === null || typeof value !== 'object') return JSON.stringify(value) ?? 'null';
  if (Array.isArray(value)) return `[${value.map(stableJSON).join(',')}]`;
  const record = value as Record<string, unknown>;
  return `{${Object.keys(record).sort().map(key => `${JSON.stringify(key)}:${stableJSON(record[key])}`).join(',')}}`;
}

function normalizedPath(value: string): string {
  const normalized = value.replace(/\\/g, '/').replace(/\/+$/, '') || '/';
  return /^[a-z]:\//i.test(normalized) || normalized.startsWith('//') ? normalized.toLowerCase() : normalized;
}

function sameSourcePath(left: string, right: string): boolean {
  return normalizedPath(left) === normalizedPath(right);
}

function isAbsoluteSourcePath(value: string): boolean {
  return value.startsWith('/') || value.startsWith('\\\\') || /^[a-z]:[\\/]/i.test(value);
}

/** Keep removal limited to serialized sources whose exact source revision is already in this tree. */
export function mapSourceRemovalCheck(
  node: CatalogTreeNode | null,
  parent: CatalogTreeNode | null,
  capturedSourceRevision?: string | null,
): MapSourceRemovalCheck {
  const locator = node?.locator;
  if (!node || !locator || locator.nodeType !== 'MapLayer' || !Array.isArray(locator.nodePath) ||
      locator.nodePath.length < 2 || locator.nodePath.some(index => !Number.isInteger(index) || index < 0) ||
      !locator.rootName.trim() || !locator.nodeName.trim() || locator.nodeName !== node.name ||
      !locator.mapType?.trim() || !locator.filename?.trim() && !locator.filenameRelative?.trim()) {
    return { canRemove: false, reason: 'unsupported' };
  }

  const mapType = locator.mapType;
  const vector = removableVectorMapTypes.has(mapType);
  const raster = removableRasterMapTypes.has(mapType);
  if (!vector && !raster) return { canRemove: false, reason: 'unsupported' };

  const sourcePath = node.assetID?.trim() ?? '';
  const sourceIdentity = node.diagnosticSourceIdentity?.trim() ?? '';
  const diagnosticRevision = node.diagnosticSourceRevision?.trim() ?? '';
  const capturedRevision = capturedSourceRevision?.trim() ?? '';
  if (diagnosticRevision && capturedRevision && diagnosticRevision.toUpperCase() !== capturedRevision.toUpperCase()) {
    return { canRemove: false, reason: 'stale-source' };
  }
  const sourceRevision = diagnosticRevision || capturedRevision;
  if (!sourcePath || !sourceRevision || (!sourceIdentity && !capturedRevision) ||
      !isAbsoluteSourcePath(sourcePath) || sourceIdentity && !sameSourcePath(sourceIdentity, sourcePath)) {
    return { canRemove: false, reason: 'missing-source' };
  }

  const lowerPath = sourcePath.toLocaleLowerCase();
  if (lowerPath.endsWith('.gdb') || parent?.locator?.nodeType === 'GdbDataset') {
    return { canRemove: false, reason: 'unsupported' };
  }
  if (vector) {
    if (!sha256Pattern.test(sourceRevision) || !/\.(json|geojson)$/i.test(sourcePath)) {
      return { canRemove: false, reason: 'stale-source' };
    }
  } else if (!sha256Pattern.test(sourceRevision)) {
    return { canRemove: false, reason: 'stale-source' };
  }

  return { canRemove: true, candidate: { sourcePath, sourceRevision, nodeType: 'MapLayer', mapType } };
}

function exactLocatorKey(locator: CatalogTreeNodeLocator): string {
  return stableJSON(locator);
}

function locatorIdentityKey(locator: CatalogTreeNodeLocator): string {
  const identity = { ...locator } as Record<string, unknown>;
  delete identity.nodePath;
  delete identity.parentGeometryNodePath;
  delete identity.parentPlanNodePath;
  delete identity.parentResultNodePath;
  return stableJSON(identity);
}

function treeIdentityCounts(roots: CatalogTreeNode[]): Map<string, number> {
  const counts = new Map<string, number>();
  const visit = (nodes: CatalogTreeNode[]): void => {
    for (const node of nodes) {
      const key = node.locator
        ? 'locator:' + locatorIdentityKey(node.locator)
        : 'unlocated:' + stableJSON({ sourceClass: node.sourceClass, name: node.name, assetID: node.assetID ?? null });
      counts.set(key, (counts.get(key) ?? 0) + 1);
      if (node.children?.length) visit(node.children);
    }
  };
  visit(roots);
  return counts;
}

export function mapTreeSourceIdentityCount(roots: CatalogTreeNode[], locator: CatalogTreeNodeLocator): number {
  return treeIdentityCounts(roots).get('locator:' + locatorIdentityKey(locator)) ?? 0;
}

export function sameMapTreeSourceIdentity(left: CatalogTreeNodeLocator, right: CatalogTreeNodeLocator): boolean {
  return locatorIdentityKey(left) === locatorIdentityKey(right);
}

export function mapTreeRemovalDeltaMatches(
  beforeRoots: CatalogTreeNode[],
  afterRoots: CatalogTreeNode[],
  locator: CatalogTreeNodeLocator,
): boolean {
  const before = treeIdentityCounts(beforeRoots);
  const after = treeIdentityCounts(afterRoots);
  const targetKey = 'locator:' + locatorIdentityKey(locator);
  const beforeTarget = before.get(targetKey) ?? 0;
  if (beforeTarget < 1 || (after.get(targetKey) ?? 0) !== beforeTarget - 1) return false;
  for (const [key, count] of before) {
    if ((after.get(key) ?? 0) !== (key === targetKey ? count - 1 : count)) return false;
  }
  for (const [key, count] of after) if (!before.has(key) && count > 0) return false;
  return true;
}

function isCatalogTreeNode(value: unknown): value is CatalogTreeNode {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const node = value as Record<string, unknown>;
  if (typeof node.id !== 'string' || !node.id.trim() || typeof node.sourceClass !== 'string' ||
      typeof node.name !== 'string' || !node.name.trim() ||
      (node.children !== undefined && node.children !== null &&
        (!Array.isArray(node.children) || !node.children.every(isCatalogTreeNode)))) return false;
  if (node.locator === undefined || node.locator === null) return true;
  if (typeof node.locator !== 'object' || Array.isArray(node.locator)) return false;
  const locator = node.locator as Record<string, unknown>;
  return typeof locator.rootName === 'string' && Boolean(locator.rootName.trim()) &&
    typeof locator.nodeType === 'string' && Boolean(locator.nodeType.trim()) &&
    typeof locator.nodeName === 'string' && Boolean(locator.nodeName.trim()) &&
    (locator.nodePath === undefined || locator.nodePath === null ||
      Array.isArray(locator.nodePath) && locator.nodePath.every(index => Number.isInteger(index) && Number(index) >= 0));
}

export function mapTreeExactLocatorCount(roots: CatalogTreeNode[], locator: CatalogTreeNodeLocator): number {
  let count = 0;
  const visit = (nodes: CatalogTreeNode[]): void => {
    for (const node of nodes) {
      if (node.locator && exactLocatorKey(node.locator) === exactLocatorKey(locator)) count += 1;
      if (node.children?.length) visit(node.children);
    }
  };
  visit(roots);
  return count;
}

function validateMapTreeRemovalReceipt(
  value: unknown,
  tab: MapDocumentTab,
  locator: CatalogTreeNodeLocator,
): MapTreeSnapshot {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    throw new Error('地图移除回执不是有效对象。');
  }
  const result = value as Record<string, unknown>;
  if (!Array.isArray(result.roots) ||
      typeof result.previousVisualizationRevision !== 'string' || !sha256Pattern.test(result.previousVisualizationRevision) ||
      typeof result.visualizationRevision !== 'string' || !sha256Pattern.test(result.visualizationRevision) ||
      result.previousVisualizationRevision.toUpperCase() !== tab.visualizationRevision?.toUpperCase() ||
      result.visualizationRevision.toUpperCase() === result.previousVisualizationRevision.toUpperCase() ||
      !Array.isArray(result.expandedNodeIDs) || !result.expandedNodeIDs.every(id => typeof id === 'string') ||
      !(result.selectedNodeID === null || typeof result.selectedNodeID === 'string') ||
      !Array.isArray(result.hiddenLayerIDs) || !result.hiddenLayerIDs.every(id => typeof id === 'string') ||
      !result.roots.every(isCatalogTreeNode)) {
    throw new Error('地图移除回执与当前 .vis 修订或图层树结构不匹配。');
  }

  const roots = result.roots as CatalogTreeNode[];
  if (!mapTreeRemovalDeltaMatches(tab.roots, roots, locator)) {
    throw new Error('地图树回执没有精确移除一个目标来源；请重新读取地图核对。');
  }

  const exactBeforeCount = mapTreeExactLocatorCount(tab.roots, locator);
  if (exactBeforeCount !== 1) throw new Error('当前地图中找不到唯一的精确来源节点；请重新读取地图。');

  return {
    roots,
    previousVisualizationRevision: result.previousVisualizationRevision,
    visualizationRevision: result.visualizationRevision,
    expandedNodeIDs: result.expandedNodeIDs as string[],
    selectedNodeID: result.selectedNodeID as string | null,
    hiddenLayerIDs: result.hiddenLayerIDs as string[],
  };
}

export interface MapDocumentSummary {
  mapIndex: number;
  mapName: string;
  mapType: string;
}

export interface MapDocumentCatalog {
  projectRevision: string;
  visualizationRevision: string | null;
  maps: MapDocumentSummary[];
  activeMapIndex: number | null;
}

export interface LoadedMapDocumentCatalog extends MapDocumentCatalog {
  documents: MapDocumentTab[];
}

export async function projectXmlSha(xml: string | null): Promise<string | null> {
  if (xml === null) return null;
  if (!globalThis.crypto?.subtle) throw new Error('This browser cannot verify the project XML snapshot.');
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(xml));
  return Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, '0')).join('').toUpperCase();
}

export function hasProject(context: WorkbenchContext): boolean {
  return Boolean(context.projectPath);
}

function mapTreeRequest(context: WorkbenchContext, tab: MapDocumentTab, projectXmlSHA: string | null) {
  if (!context.projectPath) throw new Error('Open a saved .ras project before loading its map tree.');
  return {
    projectPath: context.projectPath,
    projectXml: context.projectXml,
    projectXmlSHA,
    mapID: tab.mapID,
    mapIndex: tab.mapIndex,
    mapName: tab.name,
    mapType: tab.mapType,
    expectedVisualizationRevision: tab.visualizationRevision,
    projectSessionID: context.projectSessionID,
    ...(context.coreLayerContext ? { coreLayerContext: context.coreLayerContext } : {}),
  };
}

function parseMapDocumentCatalog(value: unknown): MapDocumentCatalog {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    throw new Error('The saved map catalog response is invalid.');
  }
  const catalog = value as Record<string, unknown>;
  if (typeof catalog.projectRevision !== 'string' || !catalog.projectRevision.trim() ||
      !(catalog.visualizationRevision === null || typeof catalog.visualizationRevision === 'string') ||
      !Array.isArray(catalog.maps) ||
      !(catalog.activeMapIndex === null || (typeof catalog.activeMapIndex === 'number' && Number.isInteger(catalog.activeMapIndex) && catalog.activeMapIndex >= 0))) {
    throw new Error('The saved map catalog response is incomplete.');
  }
  const maps = catalog.maps.map((value) => {
    if (typeof value !== 'object' || value === null || Array.isArray(value)) {
      throw new Error('The saved map catalog contains an invalid map entry.');
    }
    const map = value as Record<string, unknown>;
    if (typeof map.mapIndex !== 'number' || !Number.isInteger(map.mapIndex) || map.mapIndex < 0 ||
        typeof map.mapName !== 'string' || !map.mapName.trim() || typeof map.mapType !== 'string' || !map.mapType.trim()) {
      throw new Error('The saved map catalog contains an incomplete map entry.');
    }
    return { mapIndex: map.mapIndex as number, mapName: map.mapName, mapType: map.mapType };
  });
  if (typeof catalog.visualizationRevision === 'string' && !catalog.visualizationRevision.trim()) {
    throw new Error('The saved map catalog response is incomplete.');
  }
  if (catalog.activeMapIndex !== null && !maps.some((map) => map.mapIndex === catalog.activeMapIndex)) {
    throw new Error('The saved map catalog response is incomplete.');
  }
  return {
    projectRevision: catalog.projectRevision,
    visualizationRevision: catalog.visualizationRevision as string | null,
    maps,
    activeMapIndex: catalog.activeMapIndex as number | null,
  };
}

const savedMapIDs = new Map<string, string>();

function mapDocumentTabID(context: WorkbenchContext, map: MapDocumentSummary): string {
  const key = JSON.stringify([context.projectSessionID, context.projectPath, map.mapIndex, map.mapName, map.mapType]);
  const existing = savedMapIDs.get(key);
  if (existing) return existing;
  const id = crypto.randomUUID();
  savedMapIDs.set(key, id);
  return id;
}

function sameMapCatalog(left: MapDocumentCatalog, right: MapDocumentCatalog): boolean {
  return left.projectRevision === right.projectRevision &&
    left.visualizationRevision === right.visualizationRevision &&
    left.activeMapIndex === right.activeMapIndex &&
    left.maps.length === right.maps.length &&
    left.maps.every((map, index) => {
      const candidate = right.maps[index];
      return map.mapIndex === candidate.mapIndex && map.mapName === candidate.mapName && map.mapType === candidate.mapType;
    });
}

/** Lists saved .vis maps and reads every map tree against the same project and visualization revisions. */
export async function loadMapDocumentCatalog(
  backend: BackendGateway,
  context: WorkbenchContext,
  options?: Pick<BackendRequestOptions, 'signal'>,
): Promise<LoadedMapDocumentCatalog> {
  if (!backend.supports('map.document.list')) throw new BackendOperationUnavailableError('map.document.list');
  if (!backend.supports('map.tree.load')) throw new BackendOperationUnavailableError('map.tree.load');
  if (!context.projectPath) throw new Error('Open a saved .ras project before loading saved maps.');
  if (!context.projectRevision?.trim()) throw new Error('The saved project revision is required to list its map documents.');

  const projectXmlSHA = await projectXmlSha(context.projectXml);
  const listRequest = {
    projectPath: context.projectPath,
    projectXml: context.projectXml,
    projectXmlSHA,
    expectedProjectRevision: context.projectRevision,
  };
  const catalogValue = await backend.request<unknown>('map.document.list', listRequest, options);
  const catalog = parseMapDocumentCatalog(catalogValue);
  if (catalog.projectRevision !== context.projectRevision) {
    throw new Error('The project changed while its saved maps were being read.');
  }
  if (catalog.maps.length > 0 && catalog.visualizationRevision === null) {
    throw new Error('The saved map catalog has no visualization revision.');
  }

  const documents = await Promise.all(catalog.maps.map(async (map) => {
    const id = mapDocumentTabID(context, map);
    const tab: MapDocumentTab = {
      id,
      mapID: id,
      name: map.mapName,
      mapIndex: map.mapIndex,
      mapType: map.mapType,
      visualizationRevision: catalog.visualizationRevision,
      roots: [],
    };
    const tree = await backend.request<MapTreeSnapshot>('map.tree.load', {
      ...mapTreeRequest(context, tab, projectXmlSHA),
    }, options);
    if (!Array.isArray(tree.roots) || tree.visualizationRevision !== catalog.visualizationRevision) {
      throw new Error('The saved map changed while its layer tree was being read.');
    }
    return { ...tab, roots: tree.roots, visualizationRevision: tree.visualizationRevision };
  }));

  const confirmation = parseMapDocumentCatalog(await backend.request<unknown>('map.document.list', listRequest, options));
  if (!sameMapCatalog(catalog, confirmation)) {
    throw new Error('The project or saved map list changed while its layer trees were being read.');
  }

  return { ...catalog, documents };
}

export async function createMapDocument(
  backend: BackendGateway,
  context: WorkbenchContext,
  kind: 'Project' | 'Buildable' | 'Geometry' | 'Plan',
  expectedVisualizationRevision: string | null,
): Promise<MapDocumentTab> {
  if (!backend.supports('map.document.create')) throw new BackendOperationUnavailableError('map.document.create');
  if (!context.projectPath) throw new Error('Open a saved .ras project before creating a map document.');
  const request = {
    projectPath: context.projectPath,
    projectXml: context.projectXml,
    projectXmlSHA: await projectXmlSha(context.projectXml),
    expectedVisualizationRevision,
    kind,
  };
  const receipt = await backend.request<MapDocumentReceipt>('map.document.create', request);
  if (!receipt || !Number.isInteger(receipt.mapIndex) || receipt.mapIndex < 0 ||
      typeof receipt.mapName !== 'string' || !receipt.mapName.trim() || receipt.mapType !== kind ||
      typeof receipt.visualizationRevision !== 'string' || !/^[\da-f]{64}$/i.test(receipt.visualizationRevision) ||
      (expectedVisualizationRevision === null ? receipt.previousVisualizationRevision !== null
        : typeof receipt.previousVisualizationRevision !== 'string' ||
          receipt.previousVisualizationRevision.toUpperCase() !== expectedVisualizationRevision.toUpperCase())) {
    throw new Error('The new-map receipt does not match the requested map type and visualization revision. Reload the saved maps before retrying.');
  }
  const mapID = mapDocumentTabID(context, { mapIndex: receipt.mapIndex, mapName: receipt.mapName, mapType: receipt.mapType });
  const tab: MapDocumentTab = {
    id: mapID,
    mapID,
    name: receipt.mapName,
    mapIndex: receipt.mapIndex,
    mapType: receipt.mapType,
    visualizationRevision: receipt.visualizationRevision,
    roots: [],
  };
  if (!backend.supports('map.tree.load')) return { ...tab, treeError: 'The backend map tree reader is unavailable.' };
  try {
    const loaded = await backend.request<MapTreeSnapshot>('map.tree.load', {
      ...mapTreeRequest(context, tab, await projectXmlSha(context.projectXml)),
    });
    if (!Array.isArray(loaded.roots) || loaded.visualizationRevision !== receipt.visualizationRevision) {
      throw new Error('The saved map changed while its layer tree was being read.');
    }
    return { ...tab, roots: loaded.roots, visualizationRevision: loaded.visualizationRevision };
  } catch (error) {
    return { ...tab, treeError: error instanceof Error ? error.message : String(error) };
  }
}

export async function setMapNodeVisibility(
  backend: BackendGateway,
  context: WorkbenchContext,
  tab: MapDocumentTab,
  node: CatalogTreeNodeLocator,
  checked: boolean,
  resultTreeSelections?: Record<string, unknown>[],
): Promise<MapTreeSnapshot> {
  if (!backend.supports('map.tree.perform')) throw new BackendOperationUnavailableError('map.tree.perform');
  const result = await backend.request<MapTreeSnapshot>('map.tree.perform', {
    map: { ...mapTreeRequest(context, tab, await projectXmlSha(context.projectXml)),
      ...(resultTreeSelections ? { resultSelectionOverrides: resultTreeSelections } : {}) },
    action: 'visibility',
    node,
    checked,
  });
  const previous = result?.previousVisualizationRevision;
  const current = result?.visualizationRevision;
  if (!Array.isArray(result?.roots) || typeof previous !== 'string' || !previous.trim() ||
      typeof current !== 'string' || !current.trim() || !tab.visualizationRevision?.trim() ||
      previous.toUpperCase() !== tab.visualizationRevision.toUpperCase()) {
    throw new Error('The visibility receipt does not match the saved map visualization revision.');
  }
  return result;
}

/** Removes one persisted layer node from one saved map; the source file itself is never changed. */
export async function removeMapSourceNode(
  backend: BackendGateway,
  context: WorkbenchContext,
  tab: MapDocumentTab,
  node: CatalogTreeNode,
  candidate: MapSourceRemovalCandidate,
  resultTreeSelections?: Record<string, unknown>[],
): Promise<MapTreeSnapshot> {
  if (!backend.supports('map.tree.perform')) throw new BackendOperationUnavailableError('map.tree.perform');
  if (!context.projectPath || !context.projectRevision || !context.projectXml || !context.projectSessionID.trim()) {
    throw new Error('移除地图来源需要当前已保存工程会话、.ras XML 和工程修订。');
  }
  if (!tab.mapID.trim() || !Number.isInteger(tab.mapIndex) || tab.mapIndex < 0 ||
      !tab.name.trim() || !tab.mapType.trim() || !tab.visualizationRevision ||
      !sha256Pattern.test(tab.visualizationRevision)) {
    throw new Error('当前地图身份或 .vis 修订不完整，无法安全移除。');
  }
  const locator = node.locator;
  const currentCheck = mapSourceRemovalCheck(node, null, candidate.sourceRevision);
  if (!locator || !currentCheck.canRemove || currentCheck.candidate.sourcePath !== candidate.sourcePath ||
      currentCheck.candidate.sourceRevision.toUpperCase() !== candidate.sourceRevision.toUpperCase() ||
      currentCheck.candidate.mapType !== candidate.mapType || candidate.nodeType !== 'MapLayer' ||
      mapTreeExactLocatorCount(tab.roots, locator) !== 1) {
    throw new Error('选中的地图来源或来源修订已变化；请重新读取地图树后再操作。');
  }

  const result = await backend.request<unknown>('map.tree.perform', {
    map: {
      ...mapTreeRequest(context, tab, await projectXmlSha(context.projectXml)),
      ...(resultTreeSelections ? { resultSelectionOverrides: resultTreeSelections } : {}),
    },
    action: 'removeSourceNode',
    node: locator,
    sourcePath: candidate.sourcePath,
    sourceRevision: candidate.sourceRevision,
  });
  return validateMapTreeRemovalReceipt(result, tab, locator);
}

export async function reloadMapTree(
  backend: BackendGateway,
  context: WorkbenchContext,
  tab: MapDocumentTab,
): Promise<MapTreeSnapshot> {
  if (!backend.supports('map.tree.load')) throw new BackendOperationUnavailableError('map.tree.load');
  return backend.request<MapTreeSnapshot>('map.tree.load', {
    ...mapTreeRequest(context, tab, await projectXmlSha(context.projectXml)),
  });
}
