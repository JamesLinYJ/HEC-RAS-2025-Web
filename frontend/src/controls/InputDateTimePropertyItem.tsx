/** Controlled date/time counterpart for Visual.WinUI.Controls.InputDateTimePropertyItem. */
export function InputDateTimePropertyItem({ id, value, onChange, readOnly = false, disabled = false, mode = 'datetime-local', ariaInvalid }: {
  id: string;
  value: string;
  onChange: (value: string) => void;
  readOnly?: boolean;
  disabled?: boolean;
  mode?: 'date' | 'datetime-local';
  ariaInvalid?: boolean;
}) {
  const shown = mode === 'datetime-local' ? toDateTimeLocal(value) : toDate(value);
  return <input
    id={id}
    type={mode}
    value={shown}
    readOnly={readOnly}
    disabled={disabled}
    aria-invalid={ariaInvalid || undefined}
    onChange={(event) => onChange(event.currentTarget.value)}
  />;
}

function toDateTimeLocal(value: string): string {
  const match = /^(\d{4}-\d{2}-\d{2})[T ](\d{2}:\d{2})/.exec(value);
  return match ? `${match[1]}T${match[2]}` : '';
}

function toDate(value: string): string {
  const match = /^(\d{4}-\d{2}-\d{2})/.exec(value);
  return match?.[1] ?? '';
}
