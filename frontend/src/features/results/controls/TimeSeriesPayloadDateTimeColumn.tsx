interface TimeSeriesPayloadDateTimeColumnProps {
  rowIndex: number;
  time: string;
}

/** Date/time cell counterpart for the source TimeSeriesPayloadDateTimeColumn control. */
export function TimeSeriesPayloadDateTimeColumn({ rowIndex, time }: TimeSeriesPayloadDateTimeColumnProps) {
  return <span className="results-date-time-cell"><span className="results-row-index">{String(rowIndex + 1).padStart(3, '0')}</span>{time}</span>;
}
