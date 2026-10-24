import type { Language } from '../../shared/workbench';
import type { ComputeRunState } from './ComputeButton';

const labels = {
  zh: { pause: '暂停', resume: '继续', stop: '取消计算', unavailable: '当前 C# 服务没有提供暂停或继续操作。', stopping: '正在等待服务端确认取消' },
  en: { pause: 'Pause', resume: 'Continue', stop: 'Cancel compute', unavailable: 'The current C# service does not provide pause or continue operations.', stopping: 'Waiting for the server to confirm cancellation' },
} satisfies Record<Language, Record<string, string>>;

/** Stop maps to the existing cooperative cancellation path; pause/resume are intentionally unavailable. */
export function PauseContinueStop({ language, state, onStop }: {
  language: Language;
  state: ComputeRunState;
  onStop: () => void;
}) {
  const t = labels[language];
  const cancelling = state === 'cancelling';
  return <div className="compute-pause-continue-stop" aria-label={language === 'zh' ? '计算控制' : 'Compute controls'}>
    <button type="button" disabled aria-disabled="true" title={t.unavailable}>{t.pause}</button>
    <button type="button" disabled aria-disabled="true" title={t.unavailable}>{t.resume}</button>
    <button
      type="button"
      className="cancel-button"
      data-testid="compute-cancel"
      data-compute-action="cancel"
      aria-label={t.stop}
      title={cancelling ? t.stopping : undefined}
      disabled={cancelling}
      onClick={onStop}
    >{cancelling ? t.stopping : t.stop}</button>
    <span className="compute-control-unavailable" role="note">{t.unavailable}</span>
  </div>;
}

export default PauseContinueStop;
