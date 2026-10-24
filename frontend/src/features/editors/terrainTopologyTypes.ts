import type { ModelSnapshot } from './model';

export type TerrainTopologyOperation =
  | 'load'
  | 'select'
  | 'draw'
  | 'remove'
  | 'undo'
  | 'redo'
  | 'split'
  | 'moveNodes'
  | 'moveVertex'
  | 'insertVertex'
  | 'deleteVertex'
  | 'maskRegion'
  | 'setMetadata'
  | 'selectAt'
  | 'stamp';

export interface TerrainMapTreeRequest {
  projectPath: string;
  projectXml: string | null;
  projectXmlSHA: string | null;
  mapID: string;
  mapIndex: number;
  mapName: string;
  mapType: string;
  expectedVisualizationRevision: string | null;
  projectSessionID?: string | null;
  externalCoreLayerReferences?: unknown[] | null;
  coreLayerContext?: Record<string, unknown> | null;
}

export interface TerrainMapViewport {
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
  pixelWidth: number;
  pixelHeight: number;
}

export interface TerrainInputMapRenderOptions {
  viewport: TerrainMapViewport;
  showProfileStations?: boolean;
  showTin?: boolean;
  debugMode?: boolean;
  renderScale?: number;
  tinOptions?: {
    surfaceFillMode: string;
    renderEdges: boolean;
    renderPoints: boolean;
    renderDeletedTriangles: boolean;
    labelTriangles: boolean;
    labelEdges: boolean;
    labelPoints: boolean;
  } | null;
}

/** Captured saved map tree and exact project-owned TerrainInput node identity. */
export interface TerrainInputMapContextRequest {
  map: TerrainMapTreeRequest;
  inputNodeID: string;
  expectedSourceClass: string;
  expectedTerrainPath: string;
  renderOptions?: TerrainInputMapRenderOptions | null;
  expectedInputIndex?: number | null;
  expectedTerrainRevision?: string | null;
}

export interface TerrainPolygonEditSettings {
  shape: 'Triangle' | 'Rectangle' | 'Custom';
  width: number;
  height: number;
  rotation: number;
  value: number;
  modificationType: 'SetValue' | 'TakeHigher' | 'TakeLower' | 'AddValue';
}

export interface TerrainMapProjectOwner {
  projectPath: string;
  projectXml: string;
  projectRevision: string;
  projectSessionID: string;
  coreLayerContext?: Record<string, unknown>;
  externalCoreLayerReferences?: unknown[];
}

export interface TerrainTopologyCoordinate {
  x: number;
  y: number;
}

/** Frontend command accepted by the parent Workbench, which supplies its captured ModelSaveRequest. */
export interface TerrainTopologyCommand {
  inputId: string;
  operation: TerrainTopologyOperation;
  featureIDs?: string[];
  points?: TerrainTopologyCoordinate[];
  mapCellSize?: number;
  expectedDraftIdentity?: string;
  splitMode?: 'Anywhere' | 'AtVertex';
  vertexIndex?: number;
  metadataKind?: 'arc' | 'node' | 'controlPoint';
  metadataColumn?: string;
  metadataValue?: string;
  mapContext?: TerrainInputMapContextRequest;
  polygonSettings?: TerrainPolygonEditSettings;
}

export interface TerrainTopologyMetadataField {
  id: string;
  title: string;
  sourceType: string;
  value: string | null;
  readOnly: boolean;
  options?: string[] | null;
}

export interface TerrainTopologyFeature {
  id: string;
  kind: string;
  points: TerrainTopologyCoordinate[];
  parentId?: string | null;
  vertexIndex?: number | null;
  controlPointIndex?: number | null;
  isMask?: boolean | null;
  metadata?: TerrainTopologyMetadataField[] | null;
  rings?: TerrainTopologyCoordinate[][] | null;
}

export interface TerrainTopologyData {
  inputId: string;
  sourceRevision: string;
  draftIdentity: string;
  features: TerrainTopologyFeature[];
  selectedFeatureIDs: string[];
  canUndo: boolean;
  canRedo: boolean;
  inputIndex?: number | null;
  polygonSettings?: TerrainPolygonEditSettings | null;
}

export interface TerrainInputMapContextFeature {
  id: string;
  kind: string;
  points: TerrainTopologyCoordinate[];
  closed: boolean;
}

export interface TerrainInputMapContextData {
  inputNodeID: string;
  inputIndex: number;
  terrainPath: string;
  terrainRevision: string;
  sourceClass: string;
  features: TerrainInputMapContextFeature[];
}

/** Host response; the parent callback validates and projects snapshot into the model draft. */
export interface TerrainTopologyResponse {
  snapshot: ModelSnapshot;
  topology: TerrainTopologyData;
}
