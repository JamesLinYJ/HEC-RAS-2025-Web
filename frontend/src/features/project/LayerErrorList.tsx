import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';

export interface LayerErrorMessage { code: string; severity: string; message: string }

export function readLayerErrorMessages(errors: readonly unknown[]): LayerErrorMessage[] {
  return errors.flatMap(value => {
    if (!value || typeof value !== 'object') return [];
    const record = value as Record<string, unknown>;
    return [{ code: typeof record.code === 'string' ? record.code : '',
      severity: typeof record.severity === 'string' ? record.severity : '',
      message: typeof record.evaluatedMessage === 'string' ? record.evaluatedMessage : '' }];
  });
}

/** Source-backed diagnostics displayed by LayerErrorList.cs. */
export function LayerErrorList({ language, messages }: { language: Language; messages: readonly LayerErrorMessage[] }) {
  const severityLabel = (severity: string): string => language === 'zh'
    ? ({ error: '错误', warning: '警告', info: '信息' } as Record<string, string>)[severity.toLowerCase()] ?? severity
    : severity;
  return <ul>{messages.map((message, index) => <li key={`${message.code}:${index}`}>
    {(message.severity || message.code) && <span>{[severityLabel(message.severity), message.code].filter(Boolean).join(' · ')}</span>}
    <p>{message.message || text(language, '未提供说明', 'Description not provided')}</p>
  </li>)}</ul>;
}
