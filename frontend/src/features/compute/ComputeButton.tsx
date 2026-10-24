import type { Language } from '../../shared/workbench';

export type ComputeRunState = 'idle' | 'running' | 'cancelling' | 'completed' | 'cancelled' | 'failed' | 'terminated' | 'unverified' | 'unconfirmed';

const labels = {
  zh: {
    start: '运行已检查计划', cancel: '取消计算', starting: '正在计算…',
    idle: '待运行', running: '正在计算', cancelling: '正在请求协作取消', completed: '计算完成',
    cancelled: '计算已取消', failed: '计算失败', terminated: 'Worker 已停止，结果已丢弃', unverified: '结果格式无法识别', unconfirmed: '计算状态未确认',
  },
  en: {
    start: 'Run inspected plan', cancel: 'Cancel compute', starting: 'Computing…',
    idle: 'Ready', running: 'Computing', cancelling: 'Cooperative cancellation requested', completed: 'Compute completed',
    cancelled: 'Compute cancelled', failed: 'Compute failed', terminated: 'Worker stopped; result discarded', unverified: 'Unrecognized result format', unconfirmed: 'Compute state not confirmed',
  },
} satisfies Record<Language, Record<ComputeRunState, string> & { start: string; cancel: string; starting: string }>;

export interface ComputeButtonProps {
  language: Language;
  state: ComputeRunState;
  canRun: boolean;
  onRun: () => void;
  onCancel: () => void;
}

export function ComputeButton({ language, state, canRun, onRun, onCancel }: ComputeButtonProps) {
  const t = labels[language];
  const busy = state === 'running' || state === 'cancelling';
  return (
    <div className="compute-actions">
      <button className="primary-button" data-testid="compute-run" data-compute-action="run" onClick={onRun} disabled={!canRun || busy}>
        <span className="button-symbol">{busy ? <span className="spinner" /> : <span className="play-triangle">▶</span>}</span>
        {busy ? t.starting : t.start}
      </button>
      {busy && <button className="cancel-button" data-testid="compute-cancel" data-compute-action="cancel" onClick={onCancel}>{t.cancel}</button>}
      <span className={`status status-${state}`} role="status" data-testid="compute-status">
        <span className="status-dot" />{t[state]}
      </span>
    </div>
  );
}
