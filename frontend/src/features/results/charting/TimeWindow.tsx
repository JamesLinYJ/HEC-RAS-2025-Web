import { useEffect, useState } from 'react';
import { hasCompleteTimeBounds, indexRangeFromNormalized, normalizedRangeFromIndexes } from './timeWindowMath';
import type { NormalizedTimeRange } from './timeWindowMath';
import './timeControls.css';

interface TimeWindowProps {
  times: string[];
  range: NormalizedTimeRange;
  onRangeChange: (range: NormalizedTimeRange) => void;
  language: 'zh' | 'en';
}

export function TimeWindow({ times, range, onRangeChange, language }: TimeWindowProps) {
  const zh = language === 'zh';
  const [start, end] = indexRangeFromNormalized(range, times.length);
  const [draft, setDraft] = useState<[number, number] | null>(null);
  const [draftError, setDraftError] = useState<string | null>(null);

  useEffect(() => {
    setDraft(null);
    setDraftError(null);
  }, [range[0], range[1], times]);

  if (!hasCompleteTimeBounds(times) || times.length < 2) {
    const message = !hasCompleteTimeBounds(times)
      ? (zh ? '没有完整的原始起止时刻，无法编辑时间窗。' : 'The original time bounds are incomplete, so this time window cannot be edited.')
      : (zh ? '结果只有一个原始时刻，没有可编辑的时间区间。' : 'The result has only one native time, so there is no interval to edit.');
    return <fieldset className="results-time-window" disabled><legend>{zh ? '时间窗' : 'Time window'}</legend><p>{message}</p></fieldset>;
  }

  const draftStart = draft?.[0] ?? start;
  const draftEnd = draft?.[1] ?? end;
  const valid = draftStart < draftEnd;
  const updateBoundary = (kind: 'start' | 'end', value: number): void => {
    const next: [number, number] = kind === 'start' ? [value, draftEnd] : [draftStart, value];
    setDraft(next);
    if (next[0] >= next[1]) {
      setDraftError(zh ? '开始时刻必须早于结束时刻。' : 'The start time must be earlier than the end time.');
      return;
    }
    setDraftError(null);
    onRangeChange(normalizedRangeFromIndexes(next[0], next[1], times.length));
    setDraft(null);
  };

  return <fieldset className="results-time-window">
    <legend>{zh ? '时间窗' : 'Time window'}</legend>
    <label><span>{zh ? '开始时刻' : 'Start time'}</span><select value={draftStart} aria-label={zh ? '时间窗开始时刻' : 'Time window start time'} onChange={(event) => updateBoundary('start', Number(event.currentTarget.value))}>
      {times.map((time, index) => <option key={`${index}:${time}`} value={index}>{time}</option>)}
    </select></label>
    <label><span>{zh ? '结束时刻' : 'End time'}</span><select value={draftEnd} aria-label={zh ? '时间窗结束时刻' : 'Time window end time'} onChange={(event) => updateBoundary('end', Number(event.currentTarget.value))}>
      {times.map((time, index) => <option key={`${index}:${time}`} value={index}>{time}</option>)}
    </select></label>
    {!valid && draftError && <p role="alert">{draftError}</p>}
    {valid && <p>{zh ? '仅改变图表显示范围；原始时刻、数值与 NoData 不会被筛除或重采样。' : 'This changes only the chart viewport; original times, values, and NoData samples are not filtered or resampled.'}</p>}
  </fieldset>;
}
