/** Controlled wall-clock counterpart for Visual.WinUI.Controls.TimeInputBox. */
export function TimeInputBox({ id, value, onChange, readOnly = false, disabled = false, ariaInvalid }: {
  id: string;
  value: string;
  onChange: (value: string) => void;
  readOnly?: boolean;
  disabled?: boolean;
  ariaInvalid?: boolean;
}) {
  return <input
    id={id}
    type="text"
    inputMode="text"
    autoComplete="off"
    value={value}
    placeholder="HH:mm:ss"
    readOnly={readOnly}
    disabled={disabled}
    aria-invalid={ariaInvalid || undefined}
    onChange={(event) => onChange(event.currentTarget.value)}
  />;
}
