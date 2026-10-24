import { DateInputBox } from './DateInputBox';
import { DateTimeInputBox } from './DateTimeInputBox';
import { patchDateTimeDate, splitDateTimeText } from './dateTimeText';

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
  if (mode === 'date') {
    const parts = splitDateTimeText(value);
    return <DateInputBox id={id} value={parts?.date ?? value} onChange={(date) => onChange(parts ? patchDateTimeDate(value, date) : date)} readOnly={readOnly} disabled={disabled} ariaInvalid={ariaInvalid} />;
  }
  return <DateTimeInputBox id={id} value={value} onChange={onChange} readOnly={readOnly} disabled={disabled} ariaInvalid={ariaInvalid} />;
}
