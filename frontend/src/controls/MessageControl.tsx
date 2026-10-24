import type { Language } from '../shared/workbench';
import type { ComputeProgressSample } from '../features/compute/computeTypes';

function formatTimestamp(timestamp: number, language: Language): { label: string; iso: string } {
  const date = new Date(timestamp);
  if (!Number.isFinite(date.getTime())) return { label: String(timestamp), iso: '' };
  const locale = language === 'zh' ? 'zh-CN' : 'en-GB';
  const time = new Intl.DateTimeFormat(locale, {
    hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23',
  }).format(date);
  return { label: `${time}.${String(date.getMilliseconds()).padStart(3, '0')}`, iso: date.toISOString() };
}

/** One actual progress sample, preserving its event sequence and receipt time. */
export function MessageControl({ sample, language, emptyMessage }: {
  sample: ComputeProgressSample;
  language: Language;
  emptyMessage: string;
}) {
  const timestamp = formatTimestamp(sample.timestamp, language);
  const separateTask = sample.taskName && sample.taskName !== sample.message ? sample.taskName : undefined;
  return <li
    className="compute-live-message"
    data-progress-sequence={sample.sequence}
    data-progress-timestamp={sample.timestamp}
  >
    <div className="compute-live-message-meta">
      <time dateTime={timestamp.iso || undefined} title={timestamp.iso || undefined}>{timestamp.label}</time>
      <span className="compute-live-message-sequence">#{sample.sequence}</span>
      {separateTask && <span className="compute-live-message-task">{separateTask}</span>}
    </div>
    <p>{sample.message || <span className="compute-live-message-empty">{emptyMessage}</span>}</p>
  </li>;
}

export default MessageControl;
