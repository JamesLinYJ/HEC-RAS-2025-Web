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
