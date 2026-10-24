import type { Language } from '../../../shared/workbench';
import type { RasterSymbologyColorStop } from './rasterSymbologyWorkflow';
import { colorInputValue } from './rasterSymbologyWorkflow';

interface Props {
  language: Language;
  stop: RasterSymbologyColorStop;
  index: number;
  editableValue: boolean;
  disabled: boolean;
  canDelete: boolean;
  canAdd: boolean;
  onChange: (id: string, patch: Partial<Pick<RasterSymbologyColorStop, 'color' | 'value'>>) => void;
  onAddBelow: (id: string) => void;
  onDelete: (id: string) => void;
}

export function ColorInfoEditor({ language, stop, index, editableValue, disabled, canDelete, canAdd, onChange, onAddBelow, onDelete }: Props) {
  const zh = language === 'zh';
  const colorHex = stop.color.trim().replace(/^#/, '');
  return (
    <div className="raster-symbology-stop-row">
      <span className="raster-symbology-stop-index">{index + 1}</span>
      <label className="raster-symbology-color-field">
        <span className="raster-symbology-visually-hidden">{zh ? `断点 ${index + 1} 颜色` : `Stop ${index + 1} color`}</span>
        <input type="color" value={colorInputValue(stop.color)} onChange={event => onChange(stop.id, { color: `${event.target.value}${colorHex.length === 8 ? colorHex.slice(6) : 'FF'}` })} disabled={disabled} />
      </label>
      <label className="raster-symbology-value-field">
        <span className="raster-symbology-visually-hidden">{zh ? `断点 ${index + 1} 数值` : `Stop ${index + 1} value`}</span>
        <input type="number" inputMode="decimal" step="any" value={Number.isFinite(stop.value) ? stop.value : ''}
          onChange={event => onChange(stop.id, { value: event.currentTarget.value === '' ? Number.NaN : Number(event.currentTarget.value) })}
          disabled={disabled || !editableValue} aria-invalid={!Number.isFinite(stop.value)} />
      </label>
      {!editableValue && <span className="raster-symbology-lock-note">{zh ? '自动' : 'Auto'}</span>}
      <button type="button" className="raster-symbology-icon-button" onClick={() => onAddBelow(stop.id)} disabled={disabled || !canAdd} aria-label={zh ? `在第 ${index + 1} 行下方添加` : `Add below row ${index + 1}`}>
        {zh ? '＋' : '+'}
      </button>
      <button type="button" className="raster-symbology-icon-button is-danger" onClick={() => onDelete(stop.id)} disabled={disabled || !canDelete} aria-label={zh ? `删除第 ${index + 1} 行` : `Delete row ${index + 1}`}>
        {zh ? '−' : '−'}
      </button>
    </div>
  );
}
