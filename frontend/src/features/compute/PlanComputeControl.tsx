import { useEffect, useState } from 'react';
import type { Language } from '../../shared/workbench';
import type { ComputeProgressSample } from './computeTypes';
import type { ComputeRunState } from './ComputeButton';

const labels = {
  zh: { progress: '求解进度', latest: '后端最近消息', none: '求解器尚未发送进度消息。', elapsed: '已用时间', remaining: '预计剩余时间', estimate: '按最近一次后端进度线性估算', updates: '实际进度更新', percent: '后端报告进度' },
  en: { progress: 'Solver progress', latest: 'Latest backend message', none: 'The solver has not sent a progress message.', elapsed: 'Elapsed', remaining: 'Estimated remaining', estimate: 'Linear estimate from the latest backend progress', updates: 'Actual progress updates', percent: 'Backend-reported progress' },
} satisfies Record<Language, Record<string, string>>;

function formatDuration(milliseconds: number, language: Language): string {
  const total = Math.max(0, Math.floor(milliseconds / 1000));
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const seconds = total % 60;
  if (language === 'zh') return `${hours ? `${hours}小时` : ''}${minutes ? `${minutes}分` : ''}${seconds}秒`;
  return `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`;
}

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
  const elapsed = startedAt ? Math.max(0, currentTime - startedAt) : undefined;
  const remaining = elapsed !== undefined && latest && latest.progress > 0 && latest.progress < 100
    ? Math.max(0, elapsed * (100 / latest.progress - 1))
    : undefined;

  return (
    <section className="progress-section" aria-label={t.progress}>
      <div className="section-row"><h3>{t.progress}</h3><span>{latest ? `${latest.progress}%` : '—'}</span></div>
      <div
        className="progress-track"
        role="progressbar"
        data-compute-progress
        aria-label={t.percent}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={latest?.progress}
        data-compute-progress-value={latest?.progress}
      >
        <div className="progress-fill" style={{ width: `${latest?.progress ?? 0}%` }} />
      </div>
      <div className="compute-progress-metrics">
        <span>{t.elapsed}: <strong>{elapsed === undefined ? '—' : formatDuration(elapsed, language)}</strong></span>
        <span>{t.updates}: <strong>{samples.length}</strong></span>
        {remaining !== undefined && <span title={t.estimate}>{t.remaining}: <strong>{formatDuration(remaining, language)}</strong><small> ≈</small></span>}
      </div>
      {latestRawProgress
        ? <><div className="small-label">{t.latest}</div><pre className="code-block progress-code">{latestRawProgress}</pre></>
        : <div className="progress-empty">{t.none}</div>}
    </section>
  );
}
