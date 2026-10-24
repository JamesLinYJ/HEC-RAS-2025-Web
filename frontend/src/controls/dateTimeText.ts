/**
 * Parse only the invariant date/time text forms used by the editor transport.
 * This deliberately avoids Date/Date.parse so viewing and editing never shift
 * a wall-clock value through the browser's local timezone.
 */
export interface DateTimeTextParts {
  date: string;
  separator: 'T' | ' ';
  time: string;
  suffix: string;
}

const dateTimePattern = /^(\d{4}(?:-\d{0,2}){0,2})([T ])(.*)$/;
const datePattern = /^(\d{4}-\d{2}-\d{2})$/;
const timePattern = /^(\d{2}):(\d{2})(?::(\d{2})(?:\.\d{1,7})?)?$/;

export function splitDateTimeText(value: string): DateTimeTextParts | null {
  const match = dateTimePattern.exec(value);
  if (!match) return null;
  const suffixMatch = /([zZ]|[+-]\d{2}:\d{2})$/.exec(match[3]);
  return {
    date: match[1],
    separator: match[2] as 'T' | ' ',
    time: suffixMatch ? match[3].slice(0, -suffixMatch[1].length) : match[3],
    suffix: suffixMatch?.[1] ?? '',
  };
}

export function patchDateTimeDate(value: string, date: string): string {
  const parts = splitDateTimeText(value);
  if (parts) return `${date}${parts.separator}${parts.time}${parts.suffix}`;
  return datePattern.test(value) ? date : value;
}

export function isValidDateTimeText(value: string): boolean {
  const parts = splitDateTimeText(value);
  const dateText = parts?.date ?? (datePattern.test(value) ? value : null);
  if (!dateText || !datePattern.test(dateText)) return false;

  const [year, month, day] = dateText.split('-').map(Number);
  if (year < 1) return false;
  const leapYear = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
  const daysInMonth = [31, leapYear ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31][month - 1];
  if (!daysInMonth || day < 1 || day > daysInMonth) return false;
  if (!parts) return true;

  const timeMatch = timePattern.exec(parts.time);
  if (!timeMatch) return false;
  const [, hour, minute, second = '0'] = timeMatch;
  if (Number(hour) > 23 || Number(minute) > 59 || Number(second) > 59) return false;
  if (parts.suffix && parts.suffix.toUpperCase() !== 'Z') {
    const [offsetHour, offsetMinute] = parts.suffix.slice(1).split(':').map(Number);
    if (offsetHour > 23 || offsetMinute > 59) return false;
  }
  return true;
}
