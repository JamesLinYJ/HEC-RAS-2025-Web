import type { MapDisplaySettings } from './MapSettings';

interface MapSettingsPanelProps {
  settings: MapDisplaySettings;
  onChange: (settings: MapDisplaySettings) => void;
  language: 'zh' | 'en';
}

export function MapSettingsPanel({ settings, onChange, language }: MapSettingsPanelProps) {
  const labels = language === 'zh'
    ? { title: '地图显示', overlay: '地图覆盖层', grid: '坐标网格', selection: '选择标注', velocity: '结果速度箭头' }
    : { title: 'Map display', overlay: 'Map overlay', grid: 'Coordinate grid', selection: 'Selection labels', velocity: 'Result velocity arrows' };
  const set = (key: keyof MapDisplaySettings, value: boolean) => onChange({ ...settings, [key]: value });
  return (
    <section className="map-setting-group" aria-labelledby="map-display-title">
      <h3 id="map-display-title">{labels.title}</h3>
      <label className="map-setting-row">
      <input type="checkbox" checked={settings.IsMapOverlayVisible} onChange={(event) => set('IsMapOverlayVisible', event.currentTarget.checked)} />
        <span>{labels.overlay}</span>
      </label>
      <label className="map-setting-row">
        <input type="checkbox" checked={settings.showCoordinateGrid} onChange={(event) => set('showCoordinateGrid', event.currentTarget.checked)} />
        <span>{labels.grid}</span>
      </label>
      <label className="map-setting-row">
        <input type="checkbox" checked={settings.showSelectionLabels} onChange={(event) => set('showSelectionLabels', event.currentTarget.checked)} />
        <span>{labels.selection}</span>
      </label>
      <label className="map-setting-row">
        <input type="checkbox" data-testid="map-show-velocity-arrows" checked={settings.showVelocityArrows} onChange={(event) => set('showVelocityArrows', event.currentTarget.checked)} />
        <span>{labels.velocity}</span>
      </label>
    </section>
  );
}
