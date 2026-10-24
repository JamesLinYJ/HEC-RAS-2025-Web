import type { CatalogTreeNode, CatalogTreeNodeLocator, MapDocumentTab, MapExtent } from './mapTypes';
import type { WorkbenchContext } from '../../shared/workbench';
import type { MapResultSelectionOverridePayload, MapResultSourceCatalog } from './useMapResultFrames';

export type AssociatedLayerKind = 'Terrain' | 'NValue' | 'Classification';

export interface MapAssociationResultOwner {
  resultNode: CatalogTreeNodeLocator;
  contextToken: string;
  selectionOverrides: MapResultSelectionOverridePayload[];
  selectionIndex: 0;
  selectedResultPath: string;
  selectedResultRevision: string;
}

export interface MapAssociationDependency { path: string; revision: string; role: string }

export interface MapAssociationOwners {
  geometryNodes: CatalogTreeNodeLocator[];
  planNodes: CatalogTreeNodeLocator[];
  resultOwners: MapAssociationResultOwner[];
}

export interface MapAssociationCandidate { node: CatalogTreeNode; locator: CatalogTreeNodeLocator; kind: AssociatedLayerKind }

export interface MapAssociationHoverCapture extends MapAssociationOwners {
  treeNodeID: string;
  frameID: string;
  node: CatalogTreeNodeLocator;
  kind: 'terrain' | 'nvalue' | 'classification';
  sourcePath: string;
  sourceRevision: string;
  dependencies: MapAssociationDependency[];
}

const checkable = new Set(['Geometry', 'Result', 'MultiResult', 'Terrain', 'BoundaryCondition',
  'WindLayer', 'PrecipitationLayer', 'NValue', 'Classification', 'Feature', 'MapLayer',
  'GdbDataset', 'GdbVectorLayer', 'ConceptualMesh', 'FlowAreaLayer', 'CulvertBarrelLayer']);

export function stableMapValue(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(stableMapValue);
  if (!value || typeof value !== 'object') return value;
  const item = value as Record<string, unknown>;
  return Object.fromEntries(Object.keys(item).sort().map(key => [key, stableMapValue(item[key])]));
}

export function mapLocatorKey(value: CatalogTreeNodeLocator): string { return JSON.stringify(stableMapValue(value)); }
export function sameMapLocator(left: CatalogTreeNodeLocator, right: CatalogTreeNodeLocator): boolean {
  return mapLocatorKey(left) === mapLocatorKey(right);
}
export function sameMapRevision(left: string, right: string): boolean { return left.toUpperCase() === right.toUpperCase(); }
export function sameMapID(left: string, right: string): boolean {
  return left.replace(/[{}]/g, '').toUpperCase() === right.replace(/[{}]/g, '').toUpperCase();
}

export function mapCatalogMatchesDocument(catalog: MapResultSourceCatalog | null, context: WorkbenchContext,
  document: MapDocumentTab | null): catalog is MapResultSourceCatalog {
  return Boolean(catalog && document && context.projectPath && context.projectRevision && document.visualizationRevision &&
    catalog.projectPath === context.projectPath && sameMapRevision(catalog.projectRevision, context.projectRevision) &&
    sameMapID(catalog.mapID, document.mapID) && catalog.mapIndex === document.mapIndex &&
    catalog.mapName === document.name && catalog.mapType === document.mapType &&
    sameMapRevision(catalog.visualizationRevision, document.visualizationRevision));
}

export function mapTreeRows(roots: CatalogTreeNode[]): Array<{ node: CatalogTreeNode; visible: boolean }> {
  const rows: Array<{ node: CatalogTreeNode; visible: boolean }> = [];
  const visit = (nodes: CatalogTreeNode[], parentVisible: boolean) => {
    for (const node of nodes) {
      if (!node || typeof node.id !== 'string' || typeof node.name !== 'string' || typeof node.sourceClass !== 'string' ||
          (node.children != null && !Array.isArray(node.children))) throw new Error('关联图层树返回了无效节点。');
      const visible = parentVisible && (!checkable.has(node.locator?.nodeType ?? '') || node.checked === 'checked');
      rows.push({ node, visible });
      if (node.children?.length) visit(node.children, visible);
    }
  };
  visit(roots, true);
  return rows;
}

function isResultChild(locator: CatalogTreeNodeLocator, owner: MapAssociationResultOwner): boolean {
  return locator.rootName === owner.resultNode.rootName && locator.parentResultNodeType === owner.resultNode.nodeType &&
    locator.parentResultNodeName === owner.resultNode.nodeName && locator.parentResultAssetID === owner.selectedResultPath &&
    typeof locator.parentResultRevision === 'string' && sameMapRevision(locator.parentResultRevision, owner.selectedResultRevision) &&
    JSON.stringify(locator.parentResultNodePath ?? []) === JSON.stringify(owner.resultNode.nodePath ?? []);
}

/** Uses the Host's selected tree; a Result's terrain dependency alone is not a drawable layer. */
export function collectMapAssociations(document: MapDocumentTab, catalog: MapResultSourceCatalog,
  selectionOverrides: MapResultSelectionOverridePayload[]):
  MapAssociationOwners & { candidates: MapAssociationCandidate[] } {
  const rows = mapTreeRows(catalog.treeRoots);
  const resultOwners: MapAssociationResultOwner[] = catalog.activeNodes.flatMap(active => {
    const selected = active.selections.find(selection => selection.selectionIndex === 0)?.selectedResult;
    if (!selected || selected.isComputing || !rows.some(row => row.visible && row.node.locator && sameMapLocator(row.node.locator, active.node))) return [];
    return [{ resultNode: active.node, contextToken: catalog.contextToken, selectionOverrides,
      selectionIndex: 0 as const, selectedResultPath: selected.resultPath, selectedResultRevision: selected.revision }];
  });
  const geometryRoot = { Project: 'Geometries', Buildable: 'Layers', Geometry: 'Focused Geometries', Plan: 'Focused Plans' }[document.mapType];
  const geometryNodes = rows.flatMap(({ node }) => {
    const locator = node.locator;
    if (!locator || locator.nodeType !== 'Geometry') return [];
    if (locator.parentResultNodeType) return resultOwners.some(owner => isResultChild(locator, owner)) ? [locator] : [];
    return locator.rootName === geometryRoot ? [locator] : [];
  });
  // Plan rendering requires the complete ordered focused set, including unchecked owners.
  const planNodes = rows.flatMap(({ node }) => {
    const locator = node.locator;
    return locator?.rootName === 'Focused Plans' && locator.nodeType === 'Plan' &&
      !locator.parentPlanAssetID && !locator.parentPlanNodePath && !locator.parentPlanNodeName &&
      !locator.parentResultNodeType && !locator.parentResultAssetID && !locator.parentResultNodePath &&
      !locator.parentResultNodeName && !locator.parentResultRevision ? [locator] : [];
  });
  const candidates = rows.flatMap(({ node, visible }): MapAssociationCandidate[] => {
    const locator = node.locator;
    if (!visible || !locator || !['Terrain', 'NValue', 'Classification'].includes(locator.nodeType) ||
        locator.associationKind !== locator.nodeType || !locator.assetID) return [];
    if (locator.parentResultNodeType && !resultOwners.some(owner => isResultChild(locator, owner))) return [];
    return [{ node, locator, kind: locator.nodeType as AssociatedLayerKind }];
  });
  return { geometryNodes, planNodes, resultOwners, candidates };
}

export function validMapExtent(value: unknown): value is MapExtent {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const e = value as MapExtent;
  return [e.minX, e.minY, e.maxX, e.maxY].every(Number.isFinite) && e.minX < e.maxX && e.minY < e.maxY;
}
