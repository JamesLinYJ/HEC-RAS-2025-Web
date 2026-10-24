/** Bounded range counterpart for SliderPropertyItem.Value/MinValue/MaxValue. */
export function SliderPropertyItem({
  id,
  value,
  minValue,
  maxValue,
  step = 'any',
  disabled = false,
  ariaInvalid = false,
  onChange,
}: {
  id: string;
  value: string;
  minValue: number;
  maxValue: number;
  step?: number | 'any';
  disabled?: boolean;
  ariaInvalid?: boolean;
  onChange: (value: string) => void;
}) {
  const numericValue = Number(value);
  const validRange = Number.isFinite(minValue) && Number.isFinite(maxValue) && maxValue > minValue;
  const rangeValue = Number.isFinite(numericValue) ? Math.min(maxValue, Math.max(minValue, numericValue)) : minValue;
  return <input
    id={id}
    className="editor-slider-property-item"
    type="range"
    min={minValue}
    max={maxValue}
    step={step}
    value={rangeValue}
    disabled={disabled || !validRange}
    aria-invalid={ariaInvalid || undefined}
    aria-valuetext={value}
    onChange={(event) => onChange(event.currentTarget.value)}
  />;
}
