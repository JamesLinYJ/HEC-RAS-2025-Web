/** Serializable state represented by BackgroundMapsVM.cs. */
export interface BackgroundMapsVMState {
  AreBackgroundMapsOn: boolean;
  IsSatelliteImageryOn: boolean;
  IsShadedReliefOn: boolean;
  IsOpenStreetMapOn: boolean;
  SatelliteMapOpacity: number;
}

export const OSM_IS_ENABLED_BY_DEFAULT = false;

export const DEFAULT_BACKGROUND_MAPS_VM_STATE: BackgroundMapsVMState = {
  AreBackgroundMapsOn: false,
  IsSatelliteImageryOn: false,
  IsShadedReliefOn: false,
  IsOpenStreetMapOn: true,
  SatelliteMapOpacity: 255,
};

export function isBackgroundMapDraftDirty(state: BackgroundMapsVMState): boolean {
  return state.AreBackgroundMapsOn !== DEFAULT_BACKGROUND_MAPS_VM_STATE.AreBackgroundMapsOn ||
    state.IsOpenStreetMapOn !== DEFAULT_BACKGROUND_MAPS_VM_STATE.IsOpenStreetMapOn ||
    state.IsShadedReliefOn !== DEFAULT_BACKGROUND_MAPS_VM_STATE.IsShadedReliefOn ||
    state.IsSatelliteImageryOn !== DEFAULT_BACKGROUND_MAPS_VM_STATE.IsSatelliteImageryOn ||
    state.SatelliteMapOpacity !== DEFAULT_BACKGROUND_MAPS_VM_STATE.SatelliteMapOpacity;
}

/** The current backend renderer accepts projected CRS values; EPSG:3857 is the only such GeoJSON CRS currently identified by this view. */
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
