import { DateInputBox } from './DateInputBox';
import { TimeInputBox } from './TimeInputBox';
import { patchDateTimeDate, splitDateTimeText } from './dateTimeText';

/** Date and time text inputs matching the two child controls in the C# composite. */
export function DateTimeInputBox({ id, value, onChange, readOnly = false, disabled = false, ariaInvalid, groupLabel = '日期和时间' }: {
  id: string;
  value: string;
  onChange: (value: string) => void;
  readOnly?: boolean;
  disabled?: boolean;
  ariaInvalid?: boolean;
  groupLabel?: string;
}) {
  const parts = splitDateTimeText(value);
  if (!parts) {
    // Unknown source formats stay byte-for-byte visible and editable. No locale
    // parser or browser date control is allowed to reinterpret them.
    return <input
      id={id}
      type="text"
      value={value}
      readOnly={readOnly}
      disabled={disabled}
      aria-invalid={ariaInvalid || undefined}
      onChange={(event) => onChange(event.currentTarget.value)}
    />;
  }

  const handleDateChange = (date: string) => onChange(patchDateTimeDate(value, date));
  const handleTimeChange = (time: string) => onChange(`${parts.date}${parts.separator}${time}${parts.suffix}`);
  return <div className="editor-date-time-input-box" role="group" aria-label={groupLabel}>
    <DateInputBox id={id} value={parts.date} onChange={handleDateChange} readOnly={readOnly} disabled={disabled} ariaInvalid={ariaInvalid} />
    <span aria-hidden="true">{parts.separator === 'T' ? ' ' : parts.separator}</span>
    <TimeInputBox id={`${id}-time`} value={parts.time} onChange={handleTimeChange} readOnly={readOnly} disabled={disabled} ariaInvalid={ariaInvalid} />
    {parts.suffix && <span className="editor-date-time-zone" aria-label={`时区 ${parts.suffix}`}>{parts.suffix}</span>}
  </div>;
}
