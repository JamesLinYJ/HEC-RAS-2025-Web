import type { Language } from '../../../shared/workbench';

export interface ResultInspectorSnapshot {
  resultPath: string;
  revision: string;
  name: string;
  hasComputed: boolean;
  needsMigration: boolean;
  profileCount: number;
  computeSummary: null | {
    startTime: string;
    endTime: string;
    success: boolean;
    failureReason: string | null;
    solver: string | null;
    softwareVersion: string | null;
    totalDuration: string;
    totalDurationSeconds: number;
    computeLog: string | null;
  };
  plan: null | { name: string; description: string | null; shortID: string | null; filename: string | null; embeddedInResult: boolean };
  geometry: null | { name: string; description: string | null; filename: string | null; embeddedInResult: boolean };
  boundaryCondition: null | { name: string; description: string | null; filename: string | null; embeddedInResult: boolean };
  exactTerrainName: string | null;
  associatedTerrainName: string | null;
  overrideTerrainName: string | null;
  showAutoDetectedTerrain: boolean;
  outputBlocks: Array<{ name: string; profileCount: number; profileNames: string[]; times: string[] }>;
  layerErrors: Array<{ code: string; severity: string; evaluatedMessage: string }>;
}

interface ResultNodeDetailsProps {
  snapshot: ResultInspectorSnapshot | null;
  loading: boolean;
  error: string | null;
  language: Language;
}

export function ResultNodeDetails({ snapshot, loading, error, language }: ResultNodeDetailsProps) {
  const text = (zh: string, en: string): string => language === 'zh' ? zh : en;
  const locale = language === 'zh' ? 'zh-CN' : 'en-US';
  const severityLabel = (severity: string): string => {
    if (language !== 'zh') return severity;
    const labels: Record<string, string> = { error: '错误', warning: '警告', information: '信息', info: '信息' };
    return labels[severity.toLowerCase()] ?? severity;
  };
  if (loading || error || !snapshot) return <div className="results-details-compact">
    <span className={'results-state-dot ' + (loading ? 'is-loading' : '')} />
    <span>{loading ? text('正在检查 HDF 结果元数据…', 'Inspecting HDF result metadata…') : error ? <>{text('结果读取状态：', 'Result read status: ')}<code>{error}</code></> : text('选择工程中的 HDF 结果后可查看源文件详情。', 'Select an HDF result from the project to inspect its source details.')}</span>
  </div>;

  const duration = snapshot.computeSummary?.totalDurationSeconds;
  return <details className="results-node-details">
    <summary><span className="results-detail-chevron">⌄</span>{text('结果文件详情', 'Result file details')}<span className="results-details-name">{snapshot.name}</span><span className={snapshot.hasComputed ? 'results-computed-badge' : 'results-uncomputed-badge'}>{snapshot.hasComputed ? text('已计算', 'Computed') : text('未计算', 'Not computed')}</span></summary>
    <div className="results-details-grid">
      <div><span>{text('配置文件', 'Plan')}</span><strong>{snapshot.plan?.name ?? '—'}</strong></div>
      <div><span>{text('几何', 'Geometry')}</span><strong>{snapshot.geometry?.name ?? '—'}</strong></div>
      <div><span>{text('边界条件', 'Boundary conditions')}</span><strong>{snapshot.boundaryCondition?.name ?? '—'}</strong></div>
      <div><span>{text('配置文件中结果数', 'Stored profiles')}</span><strong>{snapshot.profileCount.toLocaleString(locale)}</strong></div>
      <div><span>{text('开始时间', 'Start time')}</span><strong>{snapshot.computeSummary?.startTime ?? '—'}</strong></div>
      <div><span>{text('结束时间', 'End time')}</span><strong>{snapshot.computeSummary?.endTime ?? '—'}</strong></div>
      <div><span>{text('求解器', 'Solver')}</span><strong>{snapshot.computeSummary?.solver ?? '—'}</strong></div>
      <div><span>{text('计算耗时', 'Compute duration')}</span><strong>{Number.isFinite(duration) ? duration?.toLocaleString(locale, { maximumFractionDigits: 1 }) + ' s' : '—'}</strong></div>
    </div>
    {snapshot.needsMigration && <p className="results-detail-warning">{text('此结果文件需要迁移后才能完整使用。', 'This result file requires migration before full use.')}</p>}
    {snapshot.computeSummary?.failureReason && <p className="results-detail-warning">{text('计算失败原因（后端原文）：', 'Compute failure reason (backend text): ')}<code>{snapshot.computeSummary.failureReason}</code></p>}
    {snapshot.outputBlocks.length > 0 && <div className="results-block-list">
      <h4>{text('输出块与时步', 'Output blocks and time steps')}</h4>
      {snapshot.outputBlocks.map((block) => <div className="results-block-row" key={block.name}>
        <strong>{block.name}</strong><span>{block.profileCount} {text('个时步', 'profiles')}</span><small>{block.times[0] ?? '—'}{block.times.length > 1 ? ' — ' + block.times[block.times.length - 1] : ''}</small>
      </div>)}
    </div>}
    {snapshot.layerErrors.length > 0 && <div className="results-layer-messages">
      <h4>{text('结果信息', 'Result messages')}</h4>
      {snapshot.layerErrors.map((item, index) => <div key={item.code + '-' + index}><code>{severityLabel(item.severity)}{item.code ? ' · ' + item.code : ''}</code><span>{text('后端信息：', 'Backend message: ')}{item.evaluatedMessage}</span></div>)}
    </div>}
  </details>;
}
