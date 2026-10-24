import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';

interface LayerErrorMessage {
  code: string;
  severity: string;
  message: string;
}

function readMessage(value: unknown): LayerErrorMessage | null {
  if (value === null || typeof value !== 'object') return null;
  const record = value as Record<string, unknown>;
  const stringValue = (key: string): string => typeof record[key] === 'string' ? record[key] as string : '';
  return { code: stringValue('code'), severity: stringValue('severity'), message: stringValue('evaluatedMessage') };
}

function severityLabel(language: Language, severity: string): string {
  if (language !== 'zh') return severity;
  const labels: Record<string, string> = { error: '错误', warning: '警告', info: '信息' };
  return labels[severity.toLowerCase()] ?? severity;
}

/** Row-level message list corresponding to the source LayerErrorIndicator control. */
export function LayerErrorIndicator({ language, errors }: { language: Language; errors: readonly unknown[] }) {
  const messages = errors.map(readMessage).filter((message): message is LayerErrorMessage => message !== null);
  if (messages.length === 0) return null;

  const summary = messages.length === 1
    ? messages[0].message || text(language, '查看图层信息', 'View layer message')
    : text(language, `${messages.length} 项问题 · 点击查看`, `${messages.length} issues · click for details`);
  const severity = messages.some((message) => message.severity.toLowerCase() === 'error')
    ? 'error'
    : messages.some((message) => message.severity.toLowerCase() === 'warning') ? 'warning' : 'info';

  return (
    <details className={`project-layer-error-indicator is-${severity}`}>
      <summary aria-label={text(language, '查看图层信息', 'View layer messages')} title={summary}>!</summary>
      <div className="project-layer-error-popover">
        <strong>{messages.length === 1 ? summary : text(language, `${messages.length} 项图层问题`, `${messages.length} layer issues`)}</strong>
        <ul>{messages.map((message, index) => <li key={`${message.code}:${index}`}>
          {(message.severity || message.code) && <span>{[severityLabel(language, message.severity), message.code].filter(Boolean).join(' · ')}</span>}
          <p>{message.message || text(language, '未提供说明', 'Description not provided')}</p>
        </li>)}</ul>
      </div>
    </details>
  );
}
