import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';

import { LayerErrorList, readLayerErrorMessages } from './LayerErrorList';

/** Row-level message list corresponding to the source LayerErrorIndicator control. */
export function LayerErrorIndicator({ language, errors }: { language: Language; errors: readonly unknown[] }) {
  const messages = readLayerErrorMessages(errors);
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
        <LayerErrorList language={language} messages={messages} />
      </div>
    </details>
  );
}
