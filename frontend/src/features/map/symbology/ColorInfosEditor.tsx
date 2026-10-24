import type { Language } from '../../../shared/workbench';
import { ColorInfoEditor } from './ColorInfoEditor';
import type { RasterMapViewExtent, RasterSymbologyDraft } from './rasterSymbologyWorkflow';

interface Props {
  language: Language;
  draft: RasterSymbologyDraft;
  viewport: RasterMapViewExtent | null;
  disabled: boolean;
  onValueSourceChange: (value: RasterSymbologyDraft['valueSource']) => void;
  onStopChange: (id: string, patch: { color?: string; value?: number }) => void;
  onAddBelow: (id: string) => void;
  onDelete: (id: string) => void;
  onUpdateHistogram: () => void;
  onViewHistogramChange: (enabled: boolean) => void;
}

export function ColorInfosEditor({ language, draft, viewport, disabled, onValueSourceChange, onStopChange,
  onAddBelow, onDelete, onUpdateHistogram, onViewHistogramChange }: Props) {
  const zh = language === 'zh';
  const valueSourceLabels = zh
    ? { Histogram: '直方图', Linear: '线性插值', Manual: '手动输入' }
    : { Histogram: 'Histogram', Linear: 'Linear interpolation', Manual: 'Manual values' };
  const rowValueIsEditable = (index: number): boolean => draft.valueSource === 'Manual'
    || draft.valueSource === 'Linear' && (index === 0 || index === draft.stops.length - 1);
  const canUseHistogram = draft.canComputeHistogram;
  const wantsViewHistogram = draft.updateHistogramWithView;

  return (
    <section className="raster-symbology-colors">
      <div className="raster-symbology-section-title"><h4>{zh ? '颜色与分级值' : 'Colors and values'}</h4><span>{draft.stops.length} / 1024</span></div>
      <div className="raster-symbology-histogram-row">
        <label className="raster-symbology-field">
          <span>{zh ? '分级值来源' : 'Value source'}</span>
          <select value={draft.valueSource} onChange={event => onValueSourceChange(event.target.value as RasterSymbologyDraft['valueSource'])} disabled={disabled}>
            {(['Histogram', 'Linear', 'Manual'] as const).map(value => <option key={value} value={value}>{valueSourceLabels[value]}</option>)}
          </select>
        </label>
        <button type="button" className="raster-symbology-button" onClick={onUpdateHistogram}
          disabled={disabled || !canUseHistogram || draft.valueSource !== 'Histogram' || wantsViewHistogram && !viewport}>
          {zh ? (wantsViewHistogram ? '按当前视图更新' : '更新直方图分级') : (wantsViewHistogram ? 'Update from current view' : 'Update histogram values')}
        </button>
      </div>
      {draft.valueSource === 'Histogram' && <label className="raster-symbology-check">
        <input type="checkbox" checked={wantsViewHistogram} onChange={event => onViewHistogramChange(event.target.checked)} disabled={disabled || !viewport} />
        <span>{zh ? '按当前地图视图计算直方图' : 'Compute the histogram for the current map view'}</span>
      </label>}
      {wantsViewHistogram && !viewport && <p className="raster-symbology-note" role="note">
        {zh ? '父级尚未提供有效地图视口；不能用栅格范围代替当前视图。' : 'A valid map viewport was not provided; the raster extent cannot stand in for the current view.'}
      </p>}
      {!canUseHistogram && <p className="raster-symbology-note">{zh ? 'Host 当前无法为此栅格计算直方图。' : 'The Host cannot compute a histogram for this raster.'}</p>}
      <div className="raster-symbology-stop-list">
        {draft.stops.map((stop, index) => <ColorInfoEditor key={stop.id} language={language} stop={stop} index={index}
          editableValue={rowValueIsEditable(index)} disabled={disabled} canDelete={draft.stops.length > 2} canAdd={draft.stops.length < 1024}
          onChange={onStopChange} onAddBelow={onAddBelow} onDelete={onDelete} />)}
      </div>
      {draft.valueSource === 'Linear' && <p className="raster-symbology-note">{zh ? '线性模式只编辑首尾分级值，中间值按端点线性插值。' : 'Linear mode edits the endpoints and interpolates intermediate values.'}</p>}
      {draft.valueSource === 'Manual' && <p className="raster-symbology-note">{zh ? '手动模式下每个分级值都可编辑。' : 'Every value can be edited in manual mode.'}</p>}
    </section>
  );
}
