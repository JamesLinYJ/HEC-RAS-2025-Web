/** Browser settings that mirror MapSettings.cs plus map-canvas presentation options. */
export interface MapSettings {
  IsMapOverlayVisible: boolean;
}

export interface MapDisplaySettings extends MapSettings {
  showCoordinateGrid: boolean;
  showSelectionLabels: boolean;
  showVelocityArrows: boolean;
}

export const DEFAULT_MAP_DISPLAY_SETTINGS: MapDisplaySettings = {
  IsMapOverlayVisible: true,
  showCoordinateGrid: false,
  showSelectionLabels: true,
  showVelocityArrows: false,
};
