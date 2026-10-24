import type { ReactNode } from 'react';
import type { Language } from '../../../shared/workbench';
import type { ResultInspectorSnapshot } from './resultDetailsTypes';

interface DetailedResultHeaderProps {
  snapshot: ResultInspectorSnapshot;
  language: Language;
  children?: ReactNode;
}

/** Read-only header and run summary from the exact selected HDF Result. */
export function DetailedResultHeader({ snapshot, language, children }: DetailedResultHeaderProps) {
  const text = (zh: string, en: string): string => language === 'zh' ? zh : en;
  const locale = language === 'zh' ? 'zh-CN' : 'en-US';
  const severityLabel = (severity: string): string => {
    if (language !== 'zh') return severity;
    const labels: Record<string, string> = { error: '错误', warning: '警告', information: '信息', info: '信息' };
    return labels[severity.toLowerCase()] ?? severity;
  };
  const duration = snapshot.computeSummary?.totalDurationSeconds;

  return <details className="results-node-details">
    <summary>
      <span className="results-detail-chevron">⌄</span>
      {text('结果文件详情', 'Result file details')}
      <span className="results-details-name">{snapshot.name}</span>
      <span className={snapshot.hasComputed ? 'results-computed-badge' : 'results-uncomputed-badge'}>
        {snapshot.hasComputed ? text('已计算', 'Computed') : text('未计算', 'Not computed')}
      </span>
    </summary>
    <div className="results-details-grid">
      <div><span>{text('结果路径', 'Result path')}</span><strong title={snapshot.resultPath}>{snapshot.resultPath}</strong></div>
      <div><span>{text('HDF 修订 SHA-256', 'HDF revision SHA-256')}</span><strong title={snapshot.revision}>{snapshot.revision}</strong></div>
      <div><span>{text('配置文件中结果数', 'Stored profiles')}</span><strong>{snapshot.profileCount.toLocaleString(locale)}</strong></div>
      <div><span>{text('计算状态', 'Compute status')}</span><strong>{snapshot.computeSummary ? (snapshot.computeSummary.success ? text('成功', 'Succeeded') : text('失败', 'Failed')) : '—'}</strong></div>
      <div><span>{text('开始时间', 'Start time')}</span><strong>{snapshot.computeSummary?.startTime ?? '—'}</strong></div>
      <div><span>{text('结束时间', 'End time')}</span><strong>{snapshot.computeSummary?.endTime ?? '—'}</strong></div>
      <div><span>{text('求解器', 'Solver')}</span><strong>{snapshot.computeSummary?.solver ?? '—'}</strong></div>
      <div><span>{text('软件版本', 'Software version')}</span><strong>{snapshot.computeSummary?.softwareVersion ?? '—'}</strong></div>
      <div><span>{text('计算耗时', 'Compute duration')}</span><strong>{Number.isFinite(duration) ? duration?.toLocaleString(locale, { maximumFractionDigits: 1 }) + ' s' : '—'}</strong></div>
      <div><span>{text('求解器初始化', 'Solver initialization')}</span><strong>{snapshot.computeSummary?.solverInitDuration ?? '—'}</strong></div>
      <div><span>{text('输出耗时', 'Output duration')}</span><strong>{snapshot.computeSummary?.outputDuration ?? '—'}</strong></div>
      <div><span>{text('迁移状态', 'Migration')}</span><strong>{snapshot.needsMigration ? text('需要迁移', 'Migration required') : text('无需迁移', 'No migration required')}</strong></div>
    </div>
    {snapshot.needsMigration && <p className="results-detail-warning">{text('此结果文件需要迁移后才能完整使用。', 'This result file requires migration before full use.')}</p>}
    {snapshot.computeSummary?.failureReason && <p className="results-detail-warning">
      {text('计算失败原因（后端原文）：', 'Compute failure reason (backend text): ')}<code>{snapshot.computeSummary.failureReason}</code>
    </p>}
    {snapshot.computeSummary?.computeLog && <details className="results-detail-extra">
      <summary>{text('计算日志（后端原文）', 'Compute log (backend text)')}</summary>
      <pre>{snapshot.computeSummary.computeLog}</pre>
    </details>}
    {snapshot.outputBlocks.length > 0 && <div className="results-block-list">
      <h4>{text('输出块与时步', 'Output blocks and time steps')}</h4>
      {snapshot.outputBlocks.map((block) => <div className="results-block-row" key={block.name}>
        <strong>{block.name}</strong><span>{block.profileCount.toLocaleString(locale)} {text('个时步', 'profiles')}</span>
        <small>{block.times[0] ?? '—'}{block.times.length > 1 ? ' — ' + block.times[block.times.length - 1] : ''}</small>
      </div>)}
    </div>}
    {snapshot.layerErrors.length > 0 && <div className="results-layer-messages">
      <h4>{text('结果信息', 'Result messages')}</h4>
      {snapshot.layerErrors.map((item, index) => <div className="results-layer-message" key={item.code + '-' + index}>
        <code>{severityLabel(item.severity)}{item.code ? ' · ' + item.code : ''}</code>
        <span>{item.evaluatedMessage}</span>
        {(item.logData?.length ?? 0) > 0 && <details>
          <summary>{text('原始诊断字段', 'Raw diagnostic fields')}</summary>
          <dl>{item.logData.map((entry, entryIndex) => <div key={`${entry.key}-${entryIndex}`}><dt>{entry.key}</dt><dd>{entry.value ?? '—'}</dd></div>)}</dl>
        </details>}
      </div>)}
    </div>}
    {children}
  </details>;
}
