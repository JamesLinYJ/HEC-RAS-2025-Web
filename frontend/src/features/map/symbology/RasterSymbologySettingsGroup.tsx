import type { Language } from '../../../shared/workbench';
import type { RasterPlotOptionDescriptor, RasterSymbologyDraft } from './rasterSymbologyWorkflow';
import { AdditionalOptionProperties } from './AdditionalOptionProperties';
import { ContourSettingsGroup } from './ContourSettingsGroup';
import { HillshadeSettingsGroup } from './HillshadeSettingsGroup';

interface Props {
  language: Language;
  draft: RasterSymbologyDraft;
  plotOptions: RasterPlotOptionDescriptor[];
  disabled: boolean;
  onDraftChange: (patch: Partial<RasterSymbologyDraft>) => void;
  onPlotOptionChange: (propertyName: string, value: string) => void;
  onReset: () => void;
}

export function RasterSymbologySettingsGroup({ language, draft, plotOptions, disabled,
  onDraftChange, onPlotOptionChange, onReset }: Props) {
  const zh = language === 'zh';
  return (
    <section className="raster-symbology-settings">
      <div className="raster-symbology-section-title"><h4>{zh ? '符号设置' : 'Symbology settings'}</h4>
        <button type="button" className="raster-symbology-text-button" onClick={onReset} disabled={disabled}>{zh ? '恢复 Host 默认值' : 'Reset to Host defaults'}</button>
      </div>
      <label className="raster-symbology-check">
        <input type="checkbox" checked={draft.isEditing} onChange={event => onDraftChange({ isEditing: event.target.checked })} disabled={disabled} />
        <span>{zh ? '编辑颜色与分级值' : 'Edit colors and values'}</span>
      </label>
      <label className="raster-symbology-check">
        <input type="checkbox" checked={draft.grayscale} onChange={event => onDraftChange({ grayscale: event.target.checked })} disabled={disabled} />
        <span>{zh ? '灰度颜色带' : 'Grayscale color ramp'}</span>
      </label>
      <label className="raster-symbology-check">
        <input type="checkbox" checked={draft.isSurfaceFillEnabled}
          onChange={event => onDraftChange({ isSurfaceFillEnabled: event.target.checked })}
          disabled={disabled} data-testid="raster-surface-fill-enabled"
          aria-label={zh ? '启用表面填色' : 'Enable surface fill'} />
        <span>{zh ? '启用表面填色' : 'Enable surface fill'}</span>
      </label>
      <div className="raster-symbology-property-grid">
        <label className="raster-symbology-field">
          <span>{zh ? '透明度（0–255）' : 'Alpha (0–255)'}</span>
          <input type="number" min="0" max="255" step="1" value={draft.alpha} disabled={disabled}
            data-testid="raster-alpha" aria-label={zh ? '栅格透明度' : 'Raster alpha'}
            onChange={event => {
              const parsed = Number(event.target.value);
              if (Number.isFinite(parsed)) onDraftChange({ alpha: parsed });
            }} />
        </label>
        <label className="raster-symbology-field">
          <span>{zh ? '平滑半径' : 'Smoothing radius'}</span>
          <input type="number" min="0" max="1000000" step="any" value={draft.smoothingRadius}
            disabled={disabled} data-testid="raster-smoothing-radius"
            aria-label={zh ? '平滑半径' : 'Smoothing radius'}
            onChange={event => {
              const parsed = Number(event.target.value);
              if (Number.isFinite(parsed)) onDraftChange({ smoothingRadius: parsed });
            }} />
        </label>
        <label className="raster-symbology-field">
          <span>{zh ? '重采样单元尺寸比例' : 'Resample cell-size ratio'}</span>
          <input type="number" min="0" max="1000000" step="any" value={draft.resampleCellSizeRatio}
            disabled={disabled} data-testid="raster-resample-cell-size-ratio"
            aria-label={zh ? '重采样单元尺寸比例' : 'Resample cell-size ratio'}
            onChange={event => {
              const parsed = Number(event.target.value);
              if (Number.isFinite(parsed)) onDraftChange({ resampleCellSizeRatio: parsed });
            }} />
        </label>
      </div>
      <HillshadeSettingsGroup language={language} value={draft.hillshade} disabled={disabled}
        onChange={hillshade => onDraftChange({ hillshade })} />
      <ContourSettingsGroup language={language} value={draft.contours} disabled={disabled}
        onChange={contours => onDraftChange({ contours })} />
      <AdditionalOptionProperties language={language} options={plotOptions} disabled={disabled}
        onChange={onPlotOptionChange} />
    </section>
  );
}
