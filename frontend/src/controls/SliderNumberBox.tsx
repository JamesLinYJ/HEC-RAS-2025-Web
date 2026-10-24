/** Raw-string input counterpart for SliderNumberBox.Value. */
export function SliderNumberBox({
  id,
  value,
  onChange,
  readOnly = false,
  disabled = false,
  ariaInvalid = false,
}: {
  id: string;
  value: string;
  onChange: (value: string) => void;
  readOnly?: boolean;
  disabled?: boolean;
  ariaInvalid?: boolean;
}) {
  return <input
    id={id}
    className="editor-slider-number-box"
    type="text"
    inputMode="decimal"
    value={value}
    readOnly={readOnly}
    disabled={disabled}
    aria-invalid={ariaInvalid || undefined}
    onChange={(event) => onChange(event.currentTarget.value)}
  />;
}
