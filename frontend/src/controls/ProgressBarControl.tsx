import type { Language } from '../shared/workbench';
import type { ComputeRunState } from '../features/compute/ComputeButton';
import { ErrorWarningInfoIcon, type ErrorWarningInfoSeverity } from './ErrorWarningInfoIcon';
import { ProgressBackground } from './ProgressBackground';

const labels: Record<Language, {
  progress: string;
  percent: string;
  state: Record<ComputeRunState, string>;
}> = {
  zh: {
    progress: '求解进度', percent: '后端报告进度',
    state: {
      idle: '等待计算', running: '正在计算', cancelling: '等待服务端确认取消',
      completed: '计算完成', cancelled: '计算已取消', failed: '计算失败',
      terminated: '计算进程已停止，结果已丢弃', unverified: '结果格式无法识别', unconfirmed: '计算状态未确认',
    },
  },
  en: {
    progress: 'Solver progress', percent: 'Backend-reported progress',
    state: {
      idle: 'Waiting to compute', running: 'Computing', cancelling: 'Waiting for server cancellation confirmation',
      completed: 'Compute completed', cancelled: 'Compute cancelled', failed: 'Compute failed',
      terminated: 'Worker stopped; result discarded', unverified: 'Unrecognized result format', unconfirmed: 'Compute state unconfirmed',
    },
  },
};

function severityForState(state: ComputeRunState): ErrorWarningInfoSeverity {
  if (state === 'failed' || state === 'unverified') return 'error';
  if (state === 'terminated' || state === 'unconfirmed') return 'warning';
  return 'info';
}

/** Progress and terminal-state presentation for the existing server-owned run. */
export function ProgressBarControl({ language, state, progress }: {
  language: Language;
  state: ComputeRunState;
  progress?: number;
}) {
  const t = labels[language];
  const isBusy = state === 'running' || state === 'cancelling';
  const value = progress !== undefined && Number.isFinite(progress)
    ? Math.min(100, Math.max(0, progress))
    : undefined;
  return <section className="progress-section" aria-label={t.progress}>
    <div className="section-row"><h3>{t.progress}</h3><span>{value === undefined ? '—' : `${value}%`}</span></div>
    <div
      className={`progress-track${isBusy && value === undefined ? ' progress-active' : ''}`}
      role="progressbar"
      data-compute-progress
      aria-label={t.percent}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={value}
      data-compute-progress-value={value}
    >
      <ProgressBackground progress={value} indeterminate={isBusy && value === undefined} />
    </div>
    <div className="compute-progress-state" role="status">
      <ErrorWarningInfoIcon severity={severityForState(state)} language={language} />
      <span>{t.state[state]}</span>
    </div>
  </section>;
}

export default ProgressBarControl;
