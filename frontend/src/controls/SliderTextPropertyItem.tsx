import { SliderNumberBox } from './SliderNumberBox';
import { SliderPropertyItem } from './SliderPropertyItem';

/** Slider and raw text editor used by Visual.WinUI.Controls.SliderTextPropertyItem. */
export function SliderTextPropertyItem({
  id,
  value,
  onChange,
  minValue,
  maxValue,
  step = 'any',
  readOnly = false,
  disabled = false,
  ariaInvalid = false,
}: {
  id: string;
  value: string;
  onChange: (value: string) => void;
  minValue?: number;
  maxValue?: number;
  step?: number | 'any';
  readOnly?: boolean;
  disabled?: boolean;
  ariaInvalid?: boolean;
}) {
  const hasRange = minValue != null && maxValue != null && Number.isFinite(minValue) && Number.isFinite(maxValue) && maxValue > minValue;
  return <div className="editor-slider-text-property-item">
    <SliderNumberBox id={id} value={value} onChange={onChange} readOnly={readOnly} disabled={disabled} ariaInvalid={ariaInvalid} />
    {hasRange && <SliderPropertyItem id={`${id}-slider`} value={value} minValue={minValue} maxValue={maxValue} step={step} disabled={disabled} ariaInvalid={ariaInvalid} onChange={onChange} />}
  </div>;
}
