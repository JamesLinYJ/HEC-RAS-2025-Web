/** Local view settings from BackgroundMapsVM.cs and its OSMBackgroundMap. */
export interface BackgroundMapsVMState {
  AreBackgroundMapsOn: boolean;
  IsSatelliteImageryOn: boolean;
  IsShadedReliefOn: boolean;
  IsOpenStreetMapOn: boolean;
  SatelliteMapOpacity: number;
  /** OSMStylePicker starts with the original service style identifier, basic. */
  OsmStyle: string;
  /** OSM tile zoom-level offset; the original picker allows -1 through 5. */
  Density: number;
  ShowLabels: boolean;
}

export const OSM_IS_ENABLED_BY_DEFAULT = false;

export const DEFAULT_BACKGROUND_MAPS_VM_STATE: BackgroundMapsVMState = {
  AreBackgroundMapsOn: false,
  IsSatelliteImageryOn: false,
  IsShadedReliefOn: false,
  IsOpenStreetMapOn: true,
  SatelliteMapOpacity: 255,
  OsmStyle: 'basic',
  Density: 1,
  ShowLabels: true,
};

export function isBackgroundMapDraftDirty(state: BackgroundMapsVMState): boolean {
  return state.AreBackgroundMapsOn !== DEFAULT_BACKGROUND_MAPS_VM_STATE.AreBackgroundMapsOn ||
    state.IsOpenStreetMapOn !== DEFAULT_BACKGROUND_MAPS_VM_STATE.IsOpenStreetMapOn ||
    state.IsShadedReliefOn !== DEFAULT_BACKGROUND_MAPS_VM_STATE.IsShadedReliefOn ||
    state.IsSatelliteImageryOn !== DEFAULT_BACKGROUND_MAPS_VM_STATE.IsSatelliteImageryOn ||
    state.SatelliteMapOpacity !== DEFAULT_BACKGROUND_MAPS_VM_STATE.SatelliteMapOpacity ||
    state.OsmStyle !== DEFAULT_BACKGROUND_MAPS_VM_STATE.OsmStyle ||
    state.Density !== DEFAULT_BACKGROUND_MAPS_VM_STATE.Density ||
    state.ShowLabels !== DEFAULT_BACKGROUND_MAPS_VM_STATE.ShowLabels;
}

/** Legacy source-CRS fallback; connected rendering resolves the actual project projection. */
export function hasKnownProjectedMapCoordinates(crs: string): boolean {
  return crs === 'EPSG:3857';
}

/** Mirrors BackgroundMapsVM.ShouldOpenStreetMapDraw. */
export function shouldOpenStreetMapDraw(state: BackgroundMapsVMState): boolean {
  return state.IsOpenStreetMapOn && state.AreBackgroundMapsOn;
}

export interface BackgroundMapRenderPrerequisites {
  projectionResolved: boolean;
  backendRendererConnected: boolean;
}

export type BackgroundMapRenderStatus = 'ready' | 'projection-required' | 'renderer-unavailable';

export function getBackgroundMapRenderStatus(prerequisites: BackgroundMapRenderPrerequisites): BackgroundMapRenderStatus {
  if (!prerequisites.projectionResolved) return 'projection-required';
  if (!prerequisites.backendRendererConnected) return 'renderer-unavailable';
  return 'ready';
}
