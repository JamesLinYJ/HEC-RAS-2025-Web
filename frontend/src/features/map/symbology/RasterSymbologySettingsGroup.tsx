import type { Language } from '../../../shared/workbench';
import type { RasterPlotOptionDescriptor, RasterSymbologyDraft } from './rasterSymbologyWorkflow';

interface Props {
  language: Language;
  draft: RasterSymbologyDraft;
  plotOptions: RasterPlotOptionDescriptor[];
  disabled: boolean;
  onDraftChange: (patch: Partial<RasterSymbologyDraft>) => void;
  onReset: () => void;
}

export function RasterSymbologySettingsGroup({ language, draft, plotOptions, disabled, onDraftChange, onReset }: Props) {
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
      {plotOptions.length > 0 && <details className="raster-symbology-plot-options">
        <summary>{zh ? '栅格渲染选项' : 'Raster rendering options'} ({plotOptions.length})</summary>
        <div className="raster-symbology-plot-option-list">
          {plotOptions.map(option => <div className="raster-symbology-plot-option" key={option.propertyName} title={option.description}>
            <span>{option.label || option.name}</span><output>{option.value}</output>
          </div>)}
        </div>
        <p className="raster-symbology-note">{zh
          ? 'Host 当前仅将这些选项用于本次渲染回执；服务不会把它们写入 .vis，因此此处只读。'
          : 'The Host uses these options for rendering receipts but does not write them to .vis, so they are shown read-only.'}</p>
      </details>}
    </section>
  );
}
