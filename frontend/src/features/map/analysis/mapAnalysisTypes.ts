import type { BackendGateway, Language } from '../../../shared/workbench';
import type { CatalogTreeNode, CatalogTreeNodeLocator, XY } from '../mapTypes';

/** Exact map request captured by useMapResultFrames for its current source catalog. */
export interface MapAnalysisMapRequest extends Record<string, unknown> {
  projectPath: string;
  projectXml: string;
  projectXmlSHA: string;
  mapID: string;
  mapIndex: number;
  mapName: string;
  mapType: string;
  expectedVisualizationRevision: string;
  projectSessionID: string;
  coreLayerContext?: Record<string, unknown> | null;
}

export interface MapAnalysisResultChoice {
  resultPath: string;
  revision: string;
  resultName: string;
  planName: string | null;
  endTime: string | null;
  isComputing: boolean;
}

export interface MapAnalysisResultSelection {
  selectionIndex: number;
  selectionMode: string;
  selectedResult: MapAnalysisResultChoice | null;
}

export interface MapAnalysisActiveNode {
  node: CatalogTreeNodeLocator;
  nodeKind: string;
  name: string;
  selections: MapAnalysisResultSelection[];
  selectedMapType: string | null;
  profileIndex: number | null;
}

export interface MapAnalysisSelectionOverride extends Record<string, unknown> {
  node: CatalogTreeNodeLocator;
  selectionIndex: number;
  mode: string;
  resultPath?: string;
  resultRevision?: string;
}

/**
 * One immutable owner snapshot shared by map Result analysis panels. It is
 * built only from the current useMapResultFrames loaded request and checked
 * source catalog. A Result's slot zero and the current output profile are
 * intentionally represented as separate fields.
 */
export interface MapResultAnalysisCapture {
  ownerKey: string;
  map: MapAnalysisMapRequest;
  projectRevision: string;
  projectXmlSHA: string;
  visualizationRevision: string;
  contextToken: string;
  selectionOverrides: MapAnalysisSelectionOverride[];
  activeNode: MapAnalysisActiveNode;
  treeRoots: CatalogTreeNode[];
  slotZero: MapAnalysisResultSelection | null;
  profileIndex: number | null;
  profileTime: string | null;
  renderSnapshot: {
    contextToken: string;
    selectedProfileIndex: number;
    selectedProfileTime: string | null;
    sourceID: string;
  } | null;
  /** Changes when map, .vis, Result selections or active Result node changes. */
  captureKey: string;
}

export interface MapResultGeometryOwner {
  resultNode: CatalogTreeNodeLocator;
  contextToken: string;
  selectionOverrides: MapAnalysisSelectionOverride[];
  selectionIndex: 0;
  selectedResultPath: string;
  selectedResultRevision: string;
}

export interface MapHoverMeshCapture {
  node: CatalogTreeNodeLocator;
  geometryNode: CatalogTreeNodeLocator;
  sourceIdentity: string;
  geometryContextToken: string;
  sourcePath: string;
  sourceRevision: string;
  planNodes: null;
  resultOwner: MapResultGeometryOwner;
  model: null;
  ownerMode: 'resultSource';
  meshDraftState: '';
  meshDraftSHA256: string;
  topologyDraftState: null;
  topologyDraftSHA256: string;
  flowAreaVisibilityOverride: false;
}

export interface MapHoverMeshLayerCapture {
  node: CatalogTreeNodeLocator;
  kind: 'mesh';
  mesh: MapHoverMeshCapture;
}

export interface MapResultMeshSourceCapture {
  ownerKey: string;
  captureKey: string;
  resultOwner: MapResultGeometryOwner;
  geometryNode: CatalogTreeNodeLocator;
  flowAreaNode: CatalogTreeNodeLocator;
  geometryContextToken: string;
  geometryPath: string;
  geometryRevision: string;
  hoverLayer: MapHoverMeshLayerCapture;
}

export interface MapMeshElementHit {
  ownerKey: string;
  captureKey: string;
  contextToken: string;
  mapID: string;
  mapIndex: number;
  visualizationRevision: string;
  resultNode: CatalogTreeNodeLocator;
  resultPath: string;
  resultRevision: string;
  selectionIndex: 0;
  geometryNode: CatalogTreeNodeLocator;
  geometryContextToken: string;
  geometryPath: string;
  geometryRevision: string;
  flowAreaNode: CatalogTreeNodeLocator;
  /** FlowArea index returned by Host as hover entry meshIndex. */
  flowAreaIndex: number;
  /** Stable mesh element FID returned by Host as hover entry elementIndex. */
  elementIndex: number;
  elementKind: 'Cell' | 'Face';
}

export interface MapSampleLinePart {
  partIndex: number;
  coordinates: XY[];
}

/** An explicitly selected part of one checked, persisted PolylinesVector feature. */
export interface MapSampleLineSource {
  ownerKey: string;
  layerID: string;
  nodeID: string;
  featureID: string;
  featureName: string;
  mapType: 'PolylinesVector';
  vectorPath: string;
  vectorRevision: string;
  parts: MapSampleLinePart[];
}

/**
 * Common props for Result analysis panels. They do not edit source geometry or
 * project XML, but some Host analysis calls may create a Result postprocess
 * cache. Source and mesh-hit captures stay nullable until exact.
 */
export interface MapAnalysisPanelProps {
  backend: BackendGateway;
  language: Language;
  capture: MapResultAnalysisCapture | null;
  meshHit: MapMeshElementHit | null;
  sampleLineSource: MapSampleLineSource | null;
}
