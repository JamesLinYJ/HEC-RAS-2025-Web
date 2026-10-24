import type { Language } from '../shared/workbench';

function formatDuration(milliseconds: number, language: Language): string {
  const seconds = Math.max(0, Math.floor(milliseconds / 1000));
  const hours = Math.floor(seconds / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  const remainder = seconds % 60;
  if (language === 'zh') {
    return `${hours ? `${hours}小时` : ''}${minutes ? `${minutes}分` : ''}${remainder}秒`;
  }
  return `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}:${String(remainder).padStart(2, '0')}`;
}

/** Formats a measured interval or a separately identified duration estimate. */
export function TimeDifference({ language, label, startTime, endTime, durationMilliseconds, approximate = false, title }: {
  language: Language;
  label: string;
  startTime?: number;
  endTime?: number;
  durationMilliseconds?: number;
  approximate?: boolean;
  title?: string;
}) {
  const measured = startTime !== undefined && endTime !== undefined ? endTime - startTime : undefined;
  const duration = durationMilliseconds ?? measured;
  const value = duration === undefined || !Number.isFinite(duration) ? '—' : formatDuration(duration, language);
  return <span className="compute-time-difference" title={title}>
    <span>{label}:</span>
    <strong>{approximate && value !== '—' ? `≈ ${value}` : value}</strong>
  </span>;
}

export default TimeDifference;
