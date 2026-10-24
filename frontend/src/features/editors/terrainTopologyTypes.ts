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
  | 'setMetadata';

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

export interface TerrainPolygonEditSettings {
  shape: string;
  width: number;
  height: number;
  rotation: number;
  value: number;
  modificationType: string;
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

/** Host response; the parent callback validates and projects snapshot into the model draft. */
export interface TerrainTopologyResponse {
  snapshot: ModelSnapshot;
  topology: TerrainTopologyData;
}
