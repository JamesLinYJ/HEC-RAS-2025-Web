import { useEffect, useState } from 'react';
import type { Language } from '../../shared/workbench';
import type { ComputeProgressSample } from './computeTypes';
import type { ComputeRunState } from './ComputeButton';
import { ProgressBarControl } from '../../controls/ProgressBarControl';
import { TimeDifference } from '../../controls/TimeDifference';

const labels = {
  zh: { latest: '原始进度事件（诊断）', none: '当前没有可识别的进度样本。', elapsed: '已用时间', remaining: '预计剩余时间', estimate: '估算值：根据已用时间与最近一次后端报告进度按线性比例推算，并非求解器承诺的完成时间。', updates: '实际进度更新' },
  en: { latest: 'Raw progress event (diagnostics)', none: 'There are no recognized progress samples yet.', elapsed: 'Elapsed', remaining: 'Estimated remaining', estimate: 'Estimate: linearly projected from elapsed time and the latest backend-reported progress; the solver has not promised a completion time.', updates: 'Actual progress updates' },
} satisfies Record<Language, Record<string, string>>;

export interface PlanComputeControlProps {
  language: Language;
  state: ComputeRunState;
  samples: ComputeProgressSample[];
  latestRawProgress?: string;
  startedAt?: number;
  endedAt?: number;
}

export function PlanComputeControl({ language, state, samples, latestRawProgress, startedAt, endedAt }: PlanComputeControlProps) {
  const t = labels[language];
  const [now, setNow] = useState(() => Date.now());
  const latest = samples.at(-1);
  const busy = state === 'running' || state === 'cancelling';

  useEffect(() => {
    if (!startedAt || endedAt) return;
    const timer = window.setInterval(() => setNow(Date.now()), 250);
    return () => window.clearInterval(timer);
  }, [startedAt, endedAt]);

  const currentTime = endedAt ?? (busy ? now : startedAt ?? now);
  const remaining = busy && startedAt !== undefined && latest && latest.progress > 0 && latest.progress < 100
    ? Math.max(0, (currentTime - startedAt) * (100 / latest.progress - 1))
    : undefined;

  return (
    <section className="compute-run-feedback" aria-label={language === 'zh' ? '求解反馈' : 'Solver feedback'}>
      <ProgressBarControl language={language} state={state} progress={latest?.progress} />
      <div className="compute-progress-metrics">
        <TimeDifference language={language} label={t.elapsed} startTime={startedAt} endTime={startedAt === undefined ? undefined : currentTime} />
        <span>{t.updates}: <strong>{samples.length}</strong></span>
        {remaining !== undefined && <TimeDifference language={language} label={t.remaining} durationMilliseconds={remaining} approximate title={t.estimate} />}
      </div>
      {samples.length === 0 && <div className="progress-empty">{t.none}</div>}
      {latestRawProgress !== undefined && <details className="compute-progress-diagnostics">
        <summary>{t.latest}</summary>
        <pre className="code-block progress-code">{latestRawProgress}</pre>
      </details>}
    </section>
  );
}
