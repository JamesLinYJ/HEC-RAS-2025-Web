/** String-preserving numeric entry for Visual.WinUI.Controls.NumberBox. */
export function NumberBox({ id, value, onChange, readOnly = false, disabled = false, ariaInvalid, inputMode = 'decimal' }: {
  id: string;
  value: string;
  onChange: (value: string) => void;
  readOnly?: boolean;
  disabled?: boolean;
  ariaInvalid?: boolean;
  inputMode?: 'decimal' | 'numeric';
}) {
  return <input
    id={id}
    className="editor-number-box"
    type="text"
    inputMode={inputMode}
    value={value}
    readOnly={readOnly}
    disabled={disabled}
    aria-invalid={ariaInvalid || undefined}
    onChange={(event) => onChange(event.currentTarget.value)}
  />;
}
