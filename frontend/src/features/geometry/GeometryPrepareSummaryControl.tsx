import { useState } from 'react';
import type { Language } from '../../shared/workbench';
import { gt } from './geometryText';
import type { GeometryMeshSnapshot } from './geometryTypes';

type PrepareSummary = NonNullable<GeometryMeshSnapshot['prepareSummary']>;

function formatStartTime(value: string | null | undefined, language: Language): string {
  if (!value) return '—';
  const date = new Date(value);
  if (!Number.isFinite(date.getTime())) return value;
  return new Intl.DateTimeFormat(language === 'zh' ? 'zh-CN' : 'en-US', { dateStyle: 'medium', timeStyle: 'short' }).format(date);
}

function formatDuration(value: number | null | undefined, language: Language): string {
  if (value == null || !Number.isFinite(value)) return '—';
  const total = Math.max(0, value);
  if (language === 'zh') return `${total.toFixed(total % 1 ? 2 : 0)} 秒`;
  return `${total.toFixed(total % 1 ? 2 : 0)} s`;
}

export interface GeometryPrepareSummaryControlProps {
  language: Language;
  summary?: PrepareSummary | null;
  pending: boolean;
}

/** Presents only the prepare summary/log returned in the mesh snapshot. */
export function GeometryPrepareSummaryControl({ language, summary, pending }: GeometryPrepareSummaryControlProps) {
  const [copyNotice, setCopyNotice] = useState('');
  const copyLog = async (): Promise<void> => {
    const log = summary?.prepareLog;
    if (!log) return;
    if (!navigator.clipboard) {
      setCopyNotice(language === 'zh' ? '无法访问剪贴板。' : 'Clipboard access is unavailable.');
      return;
    }
    try {
      await navigator.clipboard.writeText(log);
      setCopyNotice(language === 'zh' ? '准备日志已复制。' : 'Prepare log copied.');
    } catch {
      setCopyNotice(language === 'zh' ? '无法访问剪贴板。' : 'Clipboard access is unavailable.');
    }
  };
  const title = language === 'zh' ? '几何准备摘要' : 'Geometry prepare summary';
  const status = summary?.succeeded === true ? gt(language, 'complete')
    : summary?.succeeded === false ? gt(language, 'incomplete')
      : summary?.failureReason ? gt(language, 'incomplete') : gt(language, 'noData');
  return <section className="geometry-panel" aria-label={title}>
    <header className="geometry-panel-heading"><div><h3>{title}</h3><p>{pending ? gt(language, 'preparing') : summary ? (language === 'zh' ? '来自当前网格快照中的准备结果。' : 'Prepare result from the current mesh snapshot.') : (language === 'zh' ? '当前快照未包含几何准备摘要。' : 'The current snapshot has no geometry prepare summary.')}</p></div>
      <div className="geometry-toolbar-group"><span className={`geometry-status-pill ${summary?.succeeded ? 'is-clean' : summary?.succeeded === false ? 'is-dirty' : ''}`}>{status}</span>
        <button className="geometry-secondary" type="button" disabled={!summary?.prepareLog} onClick={() => void copyLog()}>{language === 'zh' ? '复制日志' : 'Copy log'}</button>
      </div>
    </header>
    {copyNotice && <div className="geometry-inline-help" role="status">{copyNotice}</div>}
    {summary ? <div className="geometry-panel-body">
      <div className="geometry-kv-grid">
        <div className="geometry-kv"><span>{gt(language, 'softwareVersion')}</span><strong>{summary.softwareVersion || '—'}</strong></div>
        <div className="geometry-kv"><span>{gt(language, 'duration')}</span><strong>{formatDuration(summary.totalDurationSeconds, language)}</strong></div>
        <div className="geometry-kv"><span>{gt(language, 'startTime')}</span><strong>{formatStartTime(summary.startTime, language)}</strong></div>
      </div>
      {summary.failureReason && <details className="geometry-inline-help" style={{ marginTop: 9 }}><summary>{gt(language, 'rawErrorDetails')}</summary><pre style={{ whiteSpace: 'pre-wrap' }}>{summary.failureReason}</pre></details>}
      {summary.prepareLog != null && <details className="geometry-inline-help" style={{ marginTop: 9 }}><summary>{language === 'zh' ? '查看准备日志' : 'Show prepare log'}</summary><pre style={{ whiteSpace: 'pre-wrap' }}>{summary.prepareLog || (language === 'zh' ? '准备日志为空。' : 'The prepare log is empty.')}</pre></details>}
      {!summary.prepareLog && <div className="geometry-inline-help" style={{ marginTop: 9 }}>{language === 'zh' ? '此准备结果没有日志消息。' : 'This prepare result contains no log messages.'}</div>}
    </div> : <div className="geometry-empty"><strong>{pending ? gt(language, 'preparing') : (language === 'zh' ? '尚无可显示的准备摘要。' : 'There is no prepare summary to display yet.')}</strong></div>}
  </section>;
}

export default GeometryPrepareSummaryControl;
