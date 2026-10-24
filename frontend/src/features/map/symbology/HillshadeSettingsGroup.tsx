import type { Language } from '../../../shared/workbench';
import type { RasterHillshadeProperties } from './rasterSymbologyWorkflow';

interface Props {
  language: Language;
  value: RasterHillshadeProperties;
  disabled: boolean;
  onChange: (value: RasterHillshadeProperties) => void;
}

export function HillshadeSettingsGroup({ language, value, disabled, onChange }: Props) {
  const zh = language === 'zh';
  const updateNumber = (key: 'zFactor' | 'azimuth' | 'zenith', raw: string) => {
    const parsed = Number(raw);
    if (Number.isFinite(parsed)) onChange({ ...value, [key]: parsed });
  };

  return <details className="raster-symbology-subgroup" data-testid="raster-hillshade-settings">
    <summary>{zh ? '阴影' : 'Hillshade'}</summary>
    <label className="raster-symbology-check">
      <input type="checkbox" checked={value.isEnabled} disabled={disabled}
        data-testid="raster-hillshade-enabled"
        aria-label={zh ? '启用阴影' : 'Enable hillshade'}
        onChange={event => onChange({ ...value, isEnabled: event.target.checked })} />
      <span>{zh ? '启用阴影' : 'Enable hillshade'}</span>
    </label>
    <div className="raster-symbology-property-grid">
      <label className="raster-symbology-field">
        <span>{zh ? 'Z 系数' : 'Z factor'}</span>
        <input type="number" min="0" max="1000000" step="any" value={value.zFactor}
          disabled={disabled || !value.isEnabled} data-testid="raster-hillshade-zfactor"
          aria-label={zh ? '阴影 Z 系数' : 'Hillshade Z factor'}
          onChange={event => updateNumber('zFactor', event.target.value)} />
      </label>
      <label className="raster-symbology-field">
        <span>{zh ? '方位角' : 'Azimuth'}</span>
        <input type="number" min="-1000000" max="1000000" step="any" value={value.azimuth}
          disabled={disabled || !value.isEnabled} data-testid="raster-hillshade-azimuth"
          aria-label={zh ? '阴影方位角' : 'Hillshade azimuth'}
          onChange={event => updateNumber('azimuth', event.target.value)} />
      </label>
      <label className="raster-symbology-field">
        <span>{zh ? '天顶角' : 'Zenith'}</span>
        <input type="number" min="-1000000" max="1000000" step="any" value={value.zenith}
          disabled={disabled || !value.isEnabled} data-testid="raster-hillshade-zenith"
          aria-label={zh ? '阴影天顶角' : 'Hillshade zenith'}
          onChange={event => updateNumber('zenith', event.target.value)} />
      </label>
    </div>
  </details>;
}
