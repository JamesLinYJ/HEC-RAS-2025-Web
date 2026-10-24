import type { ChangeEvent } from 'react';
import type { ResultSeries } from '../data';
import { hasCompleteTimeBounds, indexRangeFromNormalized } from './timeWindowMath';
import './timeControls.css';

interface TimelineProps {
  times: string[];
  series: ResultSeries[];
  selectedIndex: number;
  onSelectIndex: (index: number) => void;
  range: [number, number];
  language: 'zh' | 'en';
}

export function Timeline({ times, series, selectedIndex, onSelectIndex, range, language }: TimelineProps) {
  const zh = language === 'zh';
  if (!hasCompleteTimeBounds(times)) {
    return <div className="results-time-timeline is-unavailable" role="status">{zh ? '没有完整的结果起止时刻，时间轴范围不可用。' : 'The result has no complete start and end times; its timeline is unavailable.'}</div>;
  }

  const last = times.length - 1;
  const activeIndex = Math.max(0, Math.min(last, selectedIndex));
  const [windowStart, windowEnd] = indexRangeFromNormalized(range, times.length);
  const startPercent = last > 0 ? windowStart / last * 100 : 50;
  const endPercent = last > 0 ? windowEnd / last * 100 : 50;
  const currentTime = times[activeIndex] ?? '';
  const hasValueAtCurrentTime = series.some((item) => Number.isFinite(item.values[activeIndex]));
  const ariaValue = `${currentTime}${series.length > 0 && !hasValueAtCurrentTime ? ' · NoData' : ''}`;

  const changeIndex = (event: ChangeEvent<HTMLInputElement>): void => {
    const next = Number(event.currentTarget.value);
    if (Number.isInteger(next) && next >= 0 && next <= last) onSelectIndex(next);
  };

  return <section className="results-time-timeline" aria-label={zh ? '结果时间轴' : 'Result timeline'}>
    <div className="results-time-timeline-heading">
      <strong>{zh ? '结果时间范围' : 'Result time span'}</strong>
      <span>{times.length.toLocaleString(zh ? 'zh-CN' : 'en-US')} {zh ? '个原始时步' : 'native output times'}</span>
    </div>
    <div className="results-time-timeline-track">
      <span className={'results-time-timeline-coverage' + (last === 0 ? ' is-point' : '')} title={`${times[0]} – ${times[last]}`} />
      <span className="results-time-timeline-window" style={{ left: `${startPercent}%`, width: `${Math.max(0, endPercent - startPercent)}%` }} title={`${times[windowStart]} – ${times[windowEnd]}`} />
      <input
        type="range"
        min={0}
        max={Math.max(0, last)}
        step={1}
        value={activeIndex}
        disabled={last === 0}
        aria-label={zh ? '定位结果时步' : 'Locate result time step'}
        aria-valuetext={ariaValue}
        onChange={changeIndex}
      />
    </div>
    <div className="results-time-timeline-labels"><span>{times[0]}</span><span>{currentTime}{series.length > 0 && !hasValueAtCurrentTime ? ' · NoData' : ''}</span><span>{times[last]}</span></div>
    <p className="results-time-timeline-note">{zh ? '底色为结果文件的实际时间覆盖；高亮为当前图表窗口。滑动只定位到已有时步，不插值或重采样。' : 'The base bar shows the file’s actual time coverage; the highlight is the current chart window. Scrubbing selects an existing output time without interpolation or resampling.'}</p>
  </section>;
}
