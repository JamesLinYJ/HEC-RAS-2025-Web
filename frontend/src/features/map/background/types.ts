import type { BackendGateway, Language, WorkbenchContext } from '../../../shared/workbench';
import type { BackgroundMapsVMState } from '../BackgroundMapsVM';
import type { MapDocumentTab, MapExtent } from '../mapTypes';
import type { MapViewport, ViewSize } from '../mapViewport';

export interface BackgroundMapSettings {
  areBackgroundMapsOn: boolean;
  isOpenStreetMapOn: boolean;
  isSatelliteImageryOn: boolean;
  isShadedReliefOn: boolean;
  osmStyle: string;
  density: number;
  showLabels: boolean;
  satelliteOpacity: number;
}

export interface BackgroundMapViewport extends MapExtent {
  pixelWidth: number;
  pixelHeight: number;
}

export interface BackgroundMapProjection {
  type: 'EPSG' | 'Proj' | 'ESRI' | 'WKT';
  value: string;
}

export type BackgroundMapProjectionRead =
  | { projection: BackgroundMapProjection; issue: null }
  | { projection: null; issue: 'missing' | 'local' | 'invalid' | 'unknown' };

export interface BackgroundMapCoordinate { x: number; y: number }
export interface BackgroundMapPathPart { points: BackgroundMapCoordinate[]; isClosed: boolean }
export type BackgroundMapSourceID = 'osm' | 'satellite' | 'shadedRelief';
export interface BackgroundMapSourceStatus {
  id: BackgroundMapSourceID;
  state: 'available' | 'disabled' | 'outsideCoverage' | 'failed';
  message: string | null;
  attribution: string | null;
}

export interface BackgroundMapFeature {
  id: string;
  sourceLayer: string;
  styleLayer: string;
  geometryType: 'LineString' | 'Polygon';
  parts: BackgroundMapPathPart[];
  /** CSS #RRGGBBAA converted from the Host's #AARRGGBB. */
  strokeColor: string | null;
  fillColor: string | null;
  strokeOpacity: number;
  fillOpacity: number;
  strokeWidth: number;
  lineWidth: number;
  lineGapWidth: number;
  isDashed: boolean;
  dash: number[];
  zIndex: number;
  clipPolygon: BackgroundMapCoordinate[];
  isTranslated: boolean;
  /** Source appearance translations and dash lengths are in screen pixels. */
  translationX: number;
  translationY: number;
}

export interface BackgroundMapLabel {
  id: string;
  text: string;
  point: BackgroundMapCoordinate | null;
  line: BackgroundMapCoordinate[];
  fontFamily: string | null;
  fontSize: number;
  rotation: number;
  textColor: string | null;
  haloColor: string | null;
  haloWidth: number;
  zIndex: number;
  bold: boolean;
  italic: boolean;
  maximumWidth: number;
}

export interface BackgroundMapRaster {
  id: 'satellite' | 'shadedRelief';
  source: string;
  width: number;
  height: number;
  rgba: string;
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
  opacity: number;
}

export interface BackgroundMapRenderRequest {
  requestID: string;
  mapID: string;
  projectSessionID: string;
  projectionType: BackgroundMapProjection['type'];
  projectionValue: string;
  viewport: BackgroundMapViewport;
  worldUnitsPerPixel: number;
  settings: BackgroundMapSettings;
}

export interface BackgroundMapRenderResult {
  requestID: string;
  mapID: string;
  projectSessionID: string;
  viewport: BackgroundMapViewport;
  rasters: BackgroundMapRaster[];
  features: BackgroundMapFeature[];
  labels: BackgroundMapLabel[];
  sources: BackgroundMapSourceStatus[];
  osmBackgroundColor: string | null;
}

export interface BackgroundMapFrame extends Omit<BackgroundMapRenderResult, 'rasters'> {
  /** Local identity includes exact project XML, revisions, map and settings. */
  sourceKey: string;
  rasters: Array<Omit<BackgroundMapRaster, 'rgba'> & { imageSrc: string }>;
}

export type BackgroundMapRenderingStatus = 'disabled' | 'blocked' | 'loading' | 'ready' | 'partial' | 'error' | 'cancelled';

export interface BackgroundMapRenderingState {
  frame: BackgroundMapFrame | null;
  status: BackgroundMapRenderingStatus;
  message: string | null;
  sources: BackgroundMapSourceStatus[];
  projectionResolved: boolean;
  rendererConnected: boolean;
  refresh: () => void;
  cancel: () => void;
}

export interface UseBackgroundMapRenderingOptions {
  backend: BackendGateway;
  backendRevision?: number;
  context: WorkbenchContext;
  document: MapDocumentTab | null;
  camera: MapViewport;
  size: ViewSize;
  settings: BackgroundMapsVMState;
  /** Use the canvas's actual coordinate-space decision, never its translated CRS badge. */
  coordinatesAreProject: boolean;
  language: Language;
}
