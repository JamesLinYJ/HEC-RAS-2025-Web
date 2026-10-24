import { useMemo, useState } from 'react';
import './timeControls.css';

type IntervalUnit = 'milliseconds' | 'seconds' | 'minutes' | 'hours' | 'days';

const unitMilliseconds: Record<IntervalUnit, number> = {
  milliseconds: 1,
  seconds: 1_000,
  minutes: 60_000,
  hours: 3_600_000,
  days: 86_400_000,
};

interface IntervalPickerProps {
  times: string[];
  selectedIndex: number;
  onSelectIndex: (index: number) => void;
  language: 'zh' | 'en';
}

export function IntervalPicker({ times, selectedIndex, onSelectIndex, language }: IntervalPickerProps) {
  const zh = language === 'zh';
  const [amount, setAmount] = useState('1');
  const [unit, setUnit] = useState<IntervalUnit>('hours');
  const stamps = useMemo(() => times.map((time) => Date.parse(time)), [times]);
  const ordered = stamps.length === times.length && stamps.every((value, index) => Number.isFinite(value) && (index === 0 || value >= (stamps[index - 1] ?? value)));
  const numericAmount = Number(amount);
  const interval = numericAmount * unitMilliseconds[unit];
  const validInterval = Number.isFinite(interval) && interval > 0 && interval <= 8_640_000_000_000_000;
  const current = Math.max(0, Math.min(Math.max(0, times.length - 1), selectedIndex));
  const canStep = ordered && validInterval && times.length > 1;

  const move = (direction: -1 | 1): void => {
    if (!canStep) return;
    const now = stamps[current];
    if (now === undefined) return;
    const target = now + direction * interval;
    if (!Number.isFinite(target)) return;
    if (direction > 0) {
      let next = current + 1;
      while (next < stamps.length && (stamps[next] ?? Number.NEGATIVE_INFINITY) < target) next += 1;
      onSelectIndex(Math.min(stamps.length - 1, next));
    } else {
      let next = current - 1;
      while (next > 0 && (stamps[next] ?? Number.POSITIVE_INFINITY) > target) next -= 1;
      onSelectIndex(Math.max(0, next));
    }
  };

  const unavailableMessage = times.length < 2
    ? (zh ? '至少需要两个已有时步才能按间隔跳转。' : 'At least two native output times are required for interval navigation.')
    : !ordered
      ? (zh ? '原始时间戳无法解析或不是递增顺序，间隔跳转已停用。' : 'Interval navigation is disabled because the original timestamps are invalid or out of order.')
      : !validInterval
        ? (zh ? '请输入有限的正时间间隔。' : 'Enter a finite positive time interval.')
        : null;

  return <fieldset className="results-interval-picker" aria-label={zh ? '时间间隔跳转' : 'Time interval navigation'}>
    <legend>{zh ? '按时间间隔跳转' : 'Navigate by interval'}</legend>
    <label><span>{zh ? '间隔' : 'Interval'}</span><input type="number" min="0.001" step="any" value={amount} aria-label={zh ? '间隔数值' : 'Interval amount'} onChange={(event) => setAmount(event.currentTarget.value)} /></label>
    <label><span>{zh ? '单位' : 'Unit'}</span><select value={unit} aria-label={zh ? '间隔单位' : 'Interval unit'} onChange={(event) => setUnit(event.currentTarget.value as IntervalUnit)}>
      <option value="days">{zh ? '天' : 'Days'}</option><option value="hours">{zh ? '小时' : 'Hours'}</option><option value="minutes">{zh ? '分钟' : 'Minutes'}</option><option value="seconds">{zh ? '秒' : 'Seconds'}</option><option value="milliseconds">{zh ? '毫秒' : 'Milliseconds'}</option>
    </select></label>
    <button type="button" onClick={() => move(-1)} disabled={!canStep || current === 0} aria-label={zh ? '向前一个时间间隔' : 'Previous time interval'}>←</button>
    <button type="button" onClick={() => move(1)} disabled={!canStep || current >= times.length - 1} aria-label={zh ? '向后一个时间间隔' : 'Next time interval'}>→</button>
    {unavailableMessage && <p role="status">{unavailableMessage}</p>}
    {!unavailableMessage && <p>{zh ? '跳转目标始终落在结果文件已有时步上。' : 'Navigation always lands on an output time already present in the result file.'}</p>}
  </fieldset>;
}
