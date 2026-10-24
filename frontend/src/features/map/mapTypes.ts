import type { FeaturePageProps } from '../../shared/workbench';

export type XY = readonly [number, number];
export type GeoLine = readonly XY[];
export type GeoPolygon = readonly GeoLine[];

export type GeoGeometry =
  | { type: 'Point'; coordinates: XY }
  | { type: 'MultiPoint'; coordinates: readonly XY[] }
  | { type: 'LineString'; coordinates: GeoLine }
  | { type: 'MultiLineString'; coordinates: readonly GeoLine[] }
  | { type: 'Polygon'; coordinates: GeoPolygon }
  | { type: 'MultiPolygon'; coordinates: readonly GeoPolygon[] }
  | { type: 'GeometryCollection'; geometries: readonly GeoGeometry[] };

export interface GeoFeature {
  id: string;
  name: string;
  geometry: GeoGeometry;
  properties: Record<string, unknown>;
}

export interface MapExtent {
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
}

export interface MapImageLayer {
  id: string;
  name: string;
  imageSrc: string;
  extent: MapExtent;
  sourceKind: 'raster' | 'result' | 'terrain' | 'nvalue' | 'classification';
  sourcePath?: string;
  sourceRevision?: string;
  treeNodeID?: string;
  associationContextToken?: string;
  profileIndex?: number;
  opacity?: number;
  /** Classification cells must not acquire interpolated colors while zooming. */
  sampling?: 'nearest';
}

export interface GeoJsonLayer {
  id: string;
  name: string;
  sourceName: string;
  crs: string;
  features: GeoFeature[];
  extent: MapExtent;
  visible: boolean;
  opacity: number;
  /** Coordinates validated/reprojected by C#, or drawn in that same project coordinate space. */
  coordinateSpace?: 'project';
}

export interface MapDocumentTab {
  id: string;
  mapID: string;
  name: string;
  mapIndex: number;
  mapType: string;
  visualizationRevision: string | null;
  /** Receipt from the most recent visibility-only mutation; absent after any other map update. */
  visibilityTransition?: MapVisibilityTransition | null;
  roots: CatalogTreeNode[];
  treeError?: string;
}

export interface MapVisibilityTransition {
  previousVisualizationRevision: string;
  visualizationRevision: string;
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
}

export interface CatalogTreeNode {
  id: string;
  sourceClass: string;
  name: string;
  assetID?: string | null;
  localizeName?: boolean;
  children?: CatalogTreeNode[] | null;
  checked?: string | null;
  canRename?: boolean;
  canMove?: boolean;
  canAcceptChildren?: boolean;
  canCreateFolder?: boolean;
  locator?: CatalogTreeNodeLocator | null;
  diagnosticStatus?: string | null;
  diagnosticSourceIdentity?: string | null;
  diagnosticSourceRevision?: string | null;
  diagnosticReadError?: string | null;
}

export type MapFeaturePageProps = FeaturePageProps & {
  onDirtyStateChange?: (owner: string, dirty: boolean) => void;
};
