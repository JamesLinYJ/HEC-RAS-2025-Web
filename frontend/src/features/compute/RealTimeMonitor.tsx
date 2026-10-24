import { useMemo, useState } from 'react';
import type { Language } from '../../shared/workbench';
import type { ComputeProgressSample } from './computeTypes';

const labels = {
  zh: { title: '实时求解监控', range: '时间范围', all: '全部', seconds30: '最近 30 秒', minutes5: '最近 5 分钟', minutes15: '最近 15 分钟', empty: '后端尚未发送可绘制的进度数据。', progress: '进度 (%)', updates: '仅绘制求解器实际发送的进度点。', samples: '数据点' },
  en: { title: 'Live solver monitor', range: 'Time range', all: 'All', seconds30: 'Last 30 seconds', minutes5: 'Last 5 minutes', minutes15: 'Last 15 minutes', empty: 'The backend has not sent plottable progress data.', progress: 'Progress (%)', updates: 'Only progress points sent by the solver are plotted.', samples: 'Samples' },
} satisfies Record<Language, Record<string, string>>;

type RangeSeconds = 0 | 30 | 300 | 900;

export interface RealTimeMonitorProps {
  language: Language;
  samples: ComputeProgressSample[];
}

export function RealTimeMonitor({ language, samples }: RealTimeMonitorProps) {
  const t = labels[language];
  const [range, setRange] = useState<RangeSeconds>(0);
  const visibleSamples = useMemo(() => {
    if (range === 0 || samples.length === 0) return samples;
    const newest = samples[samples.length - 1].timestamp;
    const earliest = newest - range * 1000;
    return samples.filter((sample) => sample.timestamp >= earliest);
  }, [range, samples]);

  const oldest = visibleSamples[0]?.timestamp;
  const newest = visibleSamples.at(-1)?.timestamp;
  const chartStart = range > 0 && newest !== undefined ? newest - range * 1000 : oldest;
  const chartSpan = chartStart === undefined || newest === undefined ? 0 : newest - chartStart;
  const toX = (sample: ComputeProgressSample): number => chartSpan > 0
    ? 8 + ((sample.timestamp - (chartStart ?? sample.timestamp)) / chartSpan) * 284
    : 150;
  const coordinates = visibleSamples.map((sample) => {
    const x = toX(sample);
    const y = 89 - (sample.progress / 100) * 78;
    return `${x},${y}`;
  }).join(' ');

  return (
    <section className="panel compute-monitor-panel" aria-label={t.title}>
      <div className="panel-heading">
        <div className="panel-icon compute-icon"><span aria-hidden="true">⌁</span></div>
        <div><div className="panel-kicker">03 / {t.samples}</div><h2>{t.title}</h2></div>
        <label className="compute-monitor-range"><span>{t.range}</span>
          <select value={range} onChange={(event) => setRange(Number(event.currentTarget.value) as RangeSeconds)}>
            <option value={0}>{t.all}</option>
            <option value={30}>{t.seconds30}</option>
            <option value={300}>{t.minutes5}</option>
            <option value={900}>{t.minutes15}</option>
          </select>
        </label>
      </div>
      {visibleSamples.length === 0 ? <div className="compute-monitor-empty">{t.empty}</div> : (
        <>
          <svg className="compute-monitor-chart" viewBox="0 0 300 100" role="img" aria-label={`${t.progress}: ${visibleSamples.map((sample) => `${sample.progress}%`).join(', ')}`}>
            <line x1="8" x2="292" y1="11" y2="11" className="compute-chart-gridline" />
            <line x1="8" x2="292" y1="50" y2="50" className="compute-chart-gridline" />
            <line x1="8" x2="292" y1="89" y2="89" className="compute-chart-gridline" />
            <text x="2" y="13" className="compute-chart-label">100</text>
            <text x="2" y="52" className="compute-chart-label">50</text>
            <text x="2" y="91" className="compute-chart-label">0</text>
            {visibleSamples.length > 1 && <polyline points={coordinates} className="compute-chart-line" />}
            {visibleSamples.map((sample) => {
              const x = toX(sample);
              const y = 89 - (sample.progress / 100) * 78;
              return <circle key={`${sample.sequence}-${sample.timestamp}`} cx={x} cy={y} r="2.7" className="compute-chart-point"><title>{`${sample.progress}% · ${sample.message}`}</title></circle>;
            })}
          </svg>
          <div className="compute-monitor-footer"><span>{t.updates}</span><strong>{visibleSamples.length}</strong></div>
        </>
      )}
    </section>
  );
}
