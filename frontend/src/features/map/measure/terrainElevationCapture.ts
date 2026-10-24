import type { BackendGateway, WorkbenchContext } from '../../../shared/workbench';
import type { MapAnalysisMapRequest } from '../analysis/mapAnalysisTypes';
import type { MapAssociationDependency, MapAssociationHoverCapture, MapAssociationResultOwner } from '../associatedMapLayers';
import type { AssociatedMapImage } from '../useAssociatedMapLayers';
import type { CatalogTreeNode, CatalogTreeNodeLocator, MapDocumentTab } from '../mapTypes';
import type { MapResultSourceCatalog } from '../useMapResultFrames';
import { mapCatalogMatchesDocument, sameMapLocator, sameMapRevision, stableMapValue } from '../associatedMapLayers';

export interface TerrainElevationCapture {
  key: string;
  map: Record<string, unknown>;
  contextToken: string;
  projectRevision: string;
  terrainNode: CatalogTreeNodeLocator;
  terrainName: string;
  terrainPath: string;
  terrainRevision: string;
  /** Host MapTerrainElevationRequest only accepts source-hdf and terrain-input-raster. */
  dependencies: MapAssociationDependency[];
  /** Keep the complete validated association snapshot in the identity key, including plan-hdf owners. */
  associationDependencies: MapAssociationDependency[];
  resultOwners: MapAssociationResultOwner[];
}

export interface TerrainElevationCaptureState {
  capture: TerrainElevationCapture | null;
  reason: string;
  terrainName: string | null;
}

function isSHA256(value: unknown): value is string {
  return typeof value === 'string' && /^[\da-f]{64}$/i.test(value);
}

function isAbsolutePath(value: unknown): value is string {
  return typeof value === 'string' && (/^\//.test(value) || /^[a-z]:[\\/]/i.test(value) || /^\\\\/.test(value));
}

function matchingTreeNodes(nodes: CatalogTreeNode[], locator: CatalogTreeNodeLocator,
  ancestorsVisible = true): Array<{ node: CatalogTreeNode; visible: boolean }> {
  const matches: Array<{ node: CatalogTreeNode; visible: boolean }> = [];
  for (const node of nodes) {
    const visible = ancestorsVisible && node.checked !== 'unchecked';
    if (node.locator && sameMapLocator(node.locator, locator)) matches.push({ node, visible });
    if (node.children?.length) matches.push(...matchingTreeNodes(node.children, locator, visible));
  }
  return matches;
}

function validTerrainCapture(capture: MapAssociationHoverCapture, catalog: MapResultSourceCatalog): boolean {
  if (capture.kind !== 'terrain' || capture.node.nodeType !== 'Terrain' || capture.node.associationKind !== 'Terrain' ||
      !capture.node.assetID || !isAbsolutePath(capture.sourcePath) || !isSHA256(capture.sourceRevision) ||
      !capture.dependencies.length || capture.dependencies.length > 128) return false;
  if (!capture.dependencies.some(item => item.role === 'source-hdf' && item.path === capture.sourcePath &&
      sameMapRevision(item.revision, capture.sourceRevision))) return false;
  const seen = new Set<string>();
  for (const dependency of capture.dependencies) {
    if (!isAbsolutePath(dependency.path) || !isSHA256(dependency.revision) || !dependency.role) return false;
    if (dependency.role === 'source-hdf' || dependency.role === 'terrain-input-raster') {
      const key = `${dependency.role}\0${dependency.path}`;
      if (seen.has(key)) return false;
      seen.add(key);
    }
  }
  if (capture.resultOwners.length > 16 || capture.resultOwners.some(owner => owner.contextToken !== catalog.contextToken ||
      owner.selectionIndex !== 0 || !owner.selectedResultPath || !isSHA256(owner.selectedResultRevision) ||
      !Array.isArray(owner.selectionOverrides))) return false;
  const matches = matchingTreeNodes(catalog.treeRoots, capture.node);
  const match = matches[0];
  return matches.length === 1 && Boolean(match?.visible && match.node.id === capture.treeNodeID &&
    match.node.checked === 'checked' && match.node.locator?.assetID === capture.node.assetID);
}

export function captureVisibleTerrainForMeasurement(args: {
  backend: BackendGateway;
  context: WorkbenchContext;
  catalog: MapResultSourceCatalog | null;
  document: MapDocumentTab | null;
  map: MapAnalysisMapRequest | null;
  layers: readonly AssociatedMapImage[];
  selectedNodeID: string | null;
}): TerrainElevationCaptureState {
  const { backend, context, catalog, document, map } = args;
  if (!backend.supports('map.measureTerrainElevations')) {
    return { capture: null, reason: '当前 C# 服务未提供地形高程取样能力；仍可使用平面测距。', terrainName: null };
  }
  if (!catalog || !document || !map || !context.projectPath || !context.projectRevision || !context.projectXml ||
      !mapCatalogMatchesDocument(catalog, context, document) || !catalog.contextToken || !catalog.treeSelectionOverrides ||
      map.projectPath !== context.projectPath || map.projectXml !== context.projectXml ||
      map.projectSessionID !== context.projectSessionID ||
      (map.coreLayerContext?.identityToken ?? null) !== (context.coreLayerContext?.identityToken ?? null) ||
      !sameMapRevision(context.projectRevision, catalog.projectRevision) ||
      map.expectedVisualizationRevision !== document.visualizationRevision ||
      map.projectPath !== catalog.projectPath || map.projectXmlSHA.toUpperCase() !== catalog.projectXmlSHA.toUpperCase() ||
      map.projectSessionID.length === 0) {
    return { capture: null, reason: '当前地图或图层树尚未形成可核验的工程快照；仍可使用平面测距。', terrainName: null };
  }
  const terrains = args.layers.filter(layer => layer.sourceKind === 'terrain' &&
    layer.associationContextToken === catalog.contextToken && validTerrainCapture(layer.hoverCapture, catalog));
  if (terrains.length === 0) {
    return { capture: null, reason: '当前没有已勾选且可见、来源与依赖修订均已捕获的 Terrain 地形层；仍可使用平面测距。', terrainName: null };
  }
  const selected = args.selectedNodeID ? terrains.find(layer => layer.treeNodeID === args.selectedNodeID) : undefined;
  const layer = selected ?? (terrains.length === 1 ? terrains[0] : undefined);
  if (!layer) {
    return { capture: null, reason: '当前有多个可见 Terrain 地形层；请在图层树中选中要取样的地形层后重新测距。', terrainName: null };
  }
  const mapRequest = { ...map, resultSelectionOverrides: catalog.treeSelectionOverrides };
  const hoverCapture = layer.hoverCapture;
  const terrainDependencies = hoverCapture.dependencies.filter(item =>
    item.role === 'source-hdf' || item.role === 'terrain-input-raster');
  const identity = {
    map: mapRequest,
    contextToken: catalog.contextToken,
    projectRevision: catalog.projectRevision,
    terrainNode: hoverCapture.node,
    terrainPath: hoverCapture.sourcePath,
    terrainRevision: hoverCapture.sourceRevision.toUpperCase(),
    terrainDependencies: terrainDependencies.map(item => ({ ...item, revision: item.revision.toUpperCase() })),
    associationDependencies: hoverCapture.dependencies.map(item => ({ ...item, revision: item.revision.toUpperCase() })),
    resultOwners: hoverCapture.resultOwners,
  };
  return {
    capture: {
      key: JSON.stringify(stableMapValue(identity)),
      map: mapRequest,
      contextToken: catalog.contextToken,
      projectRevision: catalog.projectRevision,
      terrainNode: hoverCapture.node,
      terrainName: layer.name,
      terrainPath: hoverCapture.sourcePath,
      terrainRevision: hoverCapture.sourceRevision,
      dependencies: terrainDependencies,
      associationDependencies: hoverCapture.dependencies,
      resultOwners: hoverCapture.resultOwners,
    },
    reason: '',
    terrainName: layer.name,
  };
}
