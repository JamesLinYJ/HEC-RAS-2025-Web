/** Controlled, timezone-neutral counterpart for Visual.WinUI.Controls.DateInputBox. */
export function DateInputBox({ id, value, onChange, readOnly = false, disabled = false, ariaInvalid }: {
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
    placeholder="YYYY-MM-DD"
    readOnly={readOnly}
    disabled={disabled}
    aria-invalid={ariaInvalid || undefined}
    onChange={(event) => onChange(event.currentTarget.value)}
  />;
}
