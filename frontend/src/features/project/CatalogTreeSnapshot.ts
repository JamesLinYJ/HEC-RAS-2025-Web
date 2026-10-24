/** Wire model returned by backend/Host/CatalogTreeService.cs. */
export interface CatalogTreeMapDocument {
  id: string;
  name: string;
  kind: string;
}

export interface CatalogTreeRequest {
  projectPath: string;
  projectXml?: string | null;
  mode?: string;
  focusedGeometryIds?: string[] | null;
  focusedPlanIds?: string[] | null;
  mapId?: string | null;
  maps?: CatalogTreeMapDocument[] | null;
  focusedBoundaryConditionIds?: string[] | null;
  projectSessionID?: string | null;
  externalCoreLayerReferences?: unknown[] | null;
  coreLayerContext?: unknown;
}

export interface CatalogTreeProperty {
  id: string;
  label: string;
  value: string;
}

export interface CatalogTreeSummary {
  name?: string | null;
  filename?: string | null;
  dateCreated?: string | null;
  dateLastModified?: string | null;
  description?: string | null;
}

export interface CatalogTreeBackupSummary {
  filename: string;
  dateCreated: string;
  dateLastModified: string;
  canSaveAsNewGeometry?: boolean;
  revision?: string | null;
  parentGeometryPath?: string | null;
  parentGeometryRevision?: string | null;
  projectRevision?: string | null;
}

export interface CatalogTreeNodeLocator {
  rootName: string;
  nodeType: string;
  nodeName: string;
  nodePath?: number[] | null;
  assetID?: string | null;
  mapType?: string | null;
  filename?: string | null;
  filenameRelative?: string | null;
  parentGeometryAssetID?: string | null;
  parentGeometryNodePath?: number[] | null;
  parentGeometryNodeName?: string | null;
  associationAssetID?: string | null;
  associationKind?: string | null;
  parentPlanAssetID?: string | null;
  parentPlanNodePath?: number[] | null;
  parentPlanNodeName?: string | null;
  parentResultNodeType?: string | null;
  parentResultAssetID?: string | null;
  parentResultNodePath?: number[] | null;
  parentResultNodeName?: string | null;
  parentResultRevision?: string | null;
  parentBoundaryConditionAssetID?: string | null;
  parentBoundaryConditionNodePath?: number[] | null;
  parentBoundaryConditionNodeName?: string | null;
  sessionHdfResultOwner?: unknown;
}

export interface CatalogTreeNode {
  id: string;
  sourceClass: string;
  name: string;
  localizeName?: boolean;
  children?: CatalogTreeNode[] | null;
  assetID?: string | null;
  catalogSummary?: CatalogTreeSummary | null;
  backupSummary?: CatalogTreeBackupSummary | null;
  properties?: CatalogTreeProperty[] | null;
  checked?: string | null;
  isPinned?: boolean;
  canPin?: boolean;
  canRename?: boolean;
  canMove?: boolean;
  canAcceptChildren?: boolean;
  canCreateFolder?: boolean;
  locator?: CatalogTreeNodeLocator | null;
  layerErrors?: unknown[] | null;
  diagnosticStatus?: string | null;
  diagnosticSourceIdentity?: string | null;
  diagnosticSourceRevision?: string | null;
  diagnosticReadError?: string | null;
  resultComputeState?: string | null;
  resultComputeFailureReason?: string | null;
  resultComputeSourcePath?: string | null;
  resultComputeSourceRevision?: string | null;
  classificationEditModeOpacity?: number | null;
}

export interface CatalogTreeSnapshot {
  roots: CatalogTreeNode[];
}

export function walkCatalogTree(nodes: CatalogTreeNode[]): CatalogTreeNode[] {
  return nodes.flatMap((node) => [node, ...walkCatalogTree(node.children ?? [])]);
}

/** Catalog view-model classes and model.load kinds are distinct C# wire contracts. */
export function catalogAssetKind(node: CatalogTreeNode): string {
  return ({
    ObservablePlan: 'Plan', Plan: 'Plan',
    ObservableGeometry: 'Geometry', Geometry: 'Geometry',
    ObservableTerrain: 'Terrain', Terrain: 'Terrain',
    ObservableBoundaryCondition: 'Boundary Conditions', BoundaryCondition: 'Boundary Conditions',
    ObservableNValueLayer: 'Surface Layers', NValueLayer: 'Surface Layers',
    ObservableClassificationLayer: 'Surface Layers', ClassificationLayer: 'Surface Layers',
    ObservableResult: 'Result', Result: 'Result',
  } as Record<string, string>)[node.sourceClass] ?? node.sourceClass;
}
