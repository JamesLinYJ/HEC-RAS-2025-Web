/** Date/time fields retain wall-clock seconds and the source UTC offset. */
export function creationLocalDateTime(value: string | undefined): string {
  return value && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}/.test(value)
    ? value.replace(/([zZ]|[+-]\d{2}:\d{2})$/, '') : '';
}

export function creationDateTimeWithOffset(value: string, previous: string | undefined): string {
  if (!value) return '';
  const suffix = previous?.match(/([zZ]|[+-]\d{2}:\d{2})$/)?.[1] ?? '';
  // A datetime-local value may already contain seconds or a fractional second.
  return `${value.length === 16 ? value + ':00' : value}${suffix}`;
}
