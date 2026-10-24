import type { Language } from '../../../shared/workbench';
import type { RasterContourProperties } from './rasterSymbologyWorkflow';
import { colorInputValue } from './rasterSymbologyWorkflow';

interface Props {
  language: Language;
  value: RasterContourProperties;
  disabled: boolean;
  onChange: (value: RasterContourProperties) => void;
}

export function ContourSettingsGroup({ language, value, disabled, onChange }: Props) {
  const zh = language === 'zh';
  const updateNumber = (key: 'interval' | 'indexEvery' | 'opacity' | 'density', raw: string) => {
    const parsed = Number(raw);
    if (Number.isFinite(parsed)) onChange({ ...value, [key]: parsed });
  };

  return <details className="raster-symbology-subgroup" data-testid="raster-contour-settings">
    <summary>{zh ? '等值线' : 'Contours'}</summary>
    <label className="raster-symbology-check">
      <input type="checkbox" checked={value.isEnabled} disabled={disabled}
        data-testid="raster-contours-enabled" aria-label={zh ? '启用等值线' : 'Enable contours'}
        onChange={event => onChange({ ...value, isEnabled: event.target.checked })} />
      <span>{zh ? '启用等值线' : 'Enable contours'}</span>
    </label>
    <label className="raster-symbology-check">
      <input type="checkbox" checked={value.autoInterval} disabled={disabled || !value.isEnabled}
        data-testid="raster-contours-auto" aria-label={zh ? '自动间隔' : 'Automatic interval'}
        onChange={event => onChange({ ...value, autoInterval: event.target.checked })} />
      <span>{zh ? '自动间隔' : 'Automatic interval'}</span>
    </label>
    <div className="raster-symbology-property-grid">
      <label className="raster-symbology-field">
        <span>{zh ? '间隔' : 'Interval'}</span>
        <input type="number" min="0.000001" max="1000000" step="any" value={value.interval}
          disabled={disabled || !value.isEnabled || value.autoInterval} data-testid="raster-contours-interval"
          aria-label={zh ? '等值线间隔' : 'Contour interval'}
          onChange={event => updateNumber('interval', event.target.value)} />
      </label>
      <label className="raster-symbology-field">
        <span>{zh ? '每隔几条加粗' : 'Index every'}</span>
        <input type="number" min="0" max="1000000" step="1" value={value.indexEvery}
          disabled={disabled || !value.isEnabled} data-testid="raster-contours-index-every"
          aria-label={zh ? '等值线加粗间隔' : 'Contour index interval'}
          onChange={event => updateNumber('indexEvery', event.target.value)} />
      </label>
      <label className="raster-symbology-field">
        <span>{zh ? '透明度' : 'Opacity'}</span>
        <input type="number" min="0" max="100" step="1" value={value.opacity}
          disabled={disabled || !value.isEnabled} data-testid="raster-contours-opacity"
          aria-label={zh ? '等值线透明度' : 'Contour opacity'}
          onChange={event => updateNumber('opacity', event.target.value)} />
      </label>
      <label className="raster-symbology-field">
        <span>{zh ? '密度' : 'Density'}</span>
        <input type="number" min="0.025" max="4" step="any" value={value.density}
          disabled={disabled || !value.isEnabled} data-testid="raster-contours-density"
          aria-label={zh ? '等值线密度' : 'Contour density'}
          onChange={event => updateNumber('density', event.target.value)} />
      </label>
      <label className="raster-symbology-field raster-symbology-contour-color">
        <span>{zh ? '线条颜色' : 'Line color'}</span>
        <input type="color" value={colorInputValue(value.color)} disabled={disabled || !value.isEnabled}
          data-testid="raster-contours-color" aria-label={zh ? '等值线颜色' : 'Contour color'}
          onChange={event => onChange({ ...value, color: `${event.target.value.slice(1)}FF` })} />
      </label>
      <label className="raster-symbology-check">
        <input type="checkbox" checked={value.showLabels} disabled={disabled || !value.isEnabled}
          data-testid="raster-contours-show-labels" aria-label={zh ? '显示等值线标签' : 'Show contour labels'}
          onChange={event => onChange({ ...value, showLabels: event.target.checked })} />
        <span>{zh ? '显示标签' : 'Show labels'}</span>
      </label>
    </div>
  </details>;
}
