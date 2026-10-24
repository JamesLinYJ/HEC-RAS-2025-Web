import { useMemo, useState } from 'react';
import type { Language } from '../../shared/workbench';
import type { PlanComputeMessage } from './computeTypes';

const labels = {
  zh: { empty: '服务端尚未返回消息。', showAll: (count: number) => `显示其余 ${count} 条`, collapse: '收起', repeats: (count: number) => `另有 ${count} 条相同消息`, details: '消息参数', target: '关联对象', sort: '按严重级别排序' },
  en: { empty: 'The server has not returned any messages.', showAll: (count: number) => `Show ${count} more`, collapse: 'Collapse', repeats: (count: number) => `${count} additional matching messages`, details: 'Message parameters', target: 'Related asset', sort: 'Sort by severity' },
} satisfies Record<Language, {
  empty: string;
  showAll: (count: number) => string;
  collapse: string;
  repeats: (count: number) => string;
  details: string;
  target: string;
  sort: string;
}>;

const severityRank: Record<string, number> = { Error: 5, Warning: 4, Information: 3, Debug: 2, Trace: 1 };

function severityClass(severity: string): string {
  switch (severity) {
    case 'Error': return 'is-error';
    case 'Warning': return 'is-warning';
    case 'Debug': return 'is-debug';
    case 'Trace': return 'is-trace';
    default: return 'is-information';
  }
}

/** One-to-one web rendering of the source MessageListControl over actual Host messages. */
export function MessageListControl({ messages, language, displayLimit = 8, sortBySeverity = true, useSeverityRowBackground = true }: {
  messages: readonly PlanComputeMessage[];
  language: Language;
  displayLimit?: number;
  sortBySeverity?: boolean;
  useSeverityRowBackground?: boolean;
}) {
  const t = labels[language];
  const [expanded, setExpanded] = useState(false);
  const [sort, setSort] = useState(sortBySeverity);
  const ordered = useMemo(() => {
    const indexed = messages.map((message, index) => ({ message, index }));
    if (sort) indexed.sort((left, right) =>
      (severityRank[right.message.defaultSeverity] ?? 0) - (severityRank[left.message.defaultSeverity] ?? 0)
      || left.index - right.index);
    return indexed.map(({ message, index }) => ({ message, index }));
  }, [messages, sort]);
  const limit = Number.isFinite(displayLimit) && displayLimit > 0 ? Math.floor(displayLimit) : 0;
  const visible = expanded || limit === 0 ? ordered : ordered.slice(0, limit);
  const hiddenCount = ordered.length - visible.length;

  if (messages.length === 0) return <p className="compute-operation-messages-empty">{t.empty}</p>;

  return <div className="compute-operation-messages">
    <label className="compute-operation-message-sort">
      <input type="checkbox" checked={sort} onChange={(event) => setSort(event.currentTarget.checked)} />
      <span>{t.sort}</span>
    </label>
    <ol className="compute-operation-message-list">
      {visible.map(({ message, index }) => {
        const repeatCount = message.extraCount ?? 0;
        const rowClass = `compute-operation-message ${severityClass(message.defaultSeverity)}${useSeverityRowBackground ? ' has-severity-background' : ''}`;
        return <li
          key={`${message.index ?? index}:${message.code}:${index}`}
          className={rowClass}
          data-message-code={message.code}
          data-message-severity={message.defaultSeverity}
          data-message-index={message.index}
        >
          <div className="compute-operation-message-heading">
            <span className="compute-operation-message-severity">{message.defaultSeverity}</span>
            <code>{message.code}</code>
            {repeatCount > 0 && <span className="compute-operation-message-repeat">{t.repeats(repeatCount)}</span>}
          </div>
          <p>{message.evaluatedMessage}</p>
          {message.logData.length > 0 && <details>
            <summary>{t.details}</summary>
            <dl>{message.logData.map((datum, datumIndex) => <div key={`${datum.key}:${datumIndex}`}>
              <dt>{datum.key}</dt><dd>{datum.value ?? '—'}</dd>
              {datum.target && <><dt>{t.target}</dt><dd>{datum.target.featureName ?? datum.target.assetPath}</dd></>}
            </div>)}</dl>
          </details>}
        </li>;
      })}
    </ol>
    {(hiddenCount > 0 || expanded && limit > 0) && <button type="button" className="compute-operation-message-overflow" onClick={() => setExpanded((value) => !value)}>
      {expanded ? t.collapse : t.showAll(hiddenCount)}
    </button>}
  </div>;
}

export default MessageListControl;
