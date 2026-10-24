import { useRef } from 'react';
import type { KeyboardEvent as ReactKeyboardEvent, PointerEvent as ReactPointerEvent } from 'react';
import type { ResultDataset, ResultSeries } from '../data';
import { CHART_COLORS, isFiniteValue } from './chartTypes';

interface ChartScrollbarProps {
  dataset: ResultDataset;
  series: ResultSeries[];
  range: [number, number];
  onRangeChange: (range: [number, number]) => void;
  language: 'zh' | 'en';
}

interface ScrollDrag {
  pointerId: number;
  startX: number;
  startRange: [number, number];
}

const PREVIEW_WIDTH = 960;
const PREVIEW_HEIGHT = 28;

function previewPath(values: Array<number | null>, indexCount: number, min: number, max: number): string {
  if (indexCount === 0) return '';
  const span = max - min;
  let path = '';
  let priorIndex: number | undefined;
  for (let index = 0; index < indexCount; index += 1) {
    const value = values[index];
    if (!isFiniteValue(value)) continue;
    const x = indexCount <= 1 ? PREVIEW_WIDTH / 2 : index / (indexCount - 1) * PREVIEW_WIDTH;
    const y = PREVIEW_HEIGHT - 3 - (value - min) / span * (PREVIEW_HEIGHT - 6);
    path += (priorIndex === undefined || index !== priorIndex + 1 ? ' M ' : ' L ') + x + ' ' + y;
    priorIndex = index;
  }
  return path.trim();
}

export function ChartScrollbar({ dataset, series, range, onRangeChange, language }: ChartScrollbarProps) {
  const trackRef = useRef<HTMLDivElement>(null);
  const dragRef = useRef<ScrollDrag | null>(null);
  const pointCount = dataset.times.length;
  const width = Math.max(0, Math.min(1, range[1] - range[0]));
  const label = language === 'zh' ? '时间范围滚动条' : 'Time range scrollbar';

  if (pointCount < 2 || width >= 0.999999) return null;

  let min = Number.POSITIVE_INFINITY;
  let max = Number.NEGATIVE_INFINITY;
  for (const item of series) {
    for (const value of item.values) {
      if (!isFiniteValue(value)) continue;
      min = Math.min(min, value);
      max = Math.max(max, value);
    }
  }
  if (!Number.isFinite(min) || !Number.isFinite(max)) {
    min = 0;
    max = 1;
  } else if (min === max) {
    const padding = Math.max(Math.abs(max) * 0.05, 1);
    min -= padding;
    max += padding;
  }

  const moveTo = (center: number): void => {
    const nextStart = Math.max(0, Math.min(1 - width, center - width / 2));
    onRangeChange([nextStart, nextStart + width]);
  };

  const handleTrackPointerDown = (event: ReactPointerEvent<HTMLDivElement>): void => {
    if (event.button !== 0 || !trackRef.current) return;
    const rect = trackRef.current.getBoundingClientRect();
    if (rect.width <= 0) return;
    moveTo((event.clientX - rect.left) / rect.width);
  };

  const handleThumbPointerDown = (event: ReactPointerEvent<HTMLDivElement>): void => {
    if (event.button !== 0 || !trackRef.current) return;
    event.stopPropagation();
    dragRef.current = { pointerId: event.pointerId, startX: event.clientX, startRange: range };
    event.currentTarget.setPointerCapture(event.pointerId);
  };

  const handleThumbPointerMove = (event: ReactPointerEvent<HTMLDivElement>): void => {
    const drag = dragRef.current;
    const track = trackRef.current;
    if (!drag || drag.pointerId !== event.pointerId || !track) return;
    const rect = track.getBoundingClientRect();
    if (rect.width <= 0) return;
    const delta = (event.clientX - drag.startX) / rect.width;
    const nextStart = Math.max(0, Math.min(1 - width, drag.startRange[0] + delta));
    onRangeChange([nextStart, nextStart + width]);
  };

  const handleThumbPointerUp = (event: ReactPointerEvent<HTMLDivElement>): void => {
    if (dragRef.current?.pointerId !== event.pointerId) return;
    dragRef.current = null;
    if (event.currentTarget.hasPointerCapture(event.pointerId)) event.currentTarget.releasePointerCapture(event.pointerId);
  };

  const handleThumbPointerCancel = (event: ReactPointerEvent<HTMLDivElement>): void => {
    if (dragRef.current?.pointerId === event.pointerId) dragRef.current = null;
  };

  const handleThumbKeyDown = (event: ReactKeyboardEvent<HTMLDivElement>): void => {
    const step = 1 / Math.max(1, pointCount - 1);
    const currentStart = Math.max(0, Math.min(1 - width, range[0]));
    const nextStart = event.shiftKey ? step * 5 : step;
    if (event.key === 'ArrowLeft' || event.key === 'ArrowDown') {
      event.preventDefault();
      const start = Math.max(0, currentStart - nextStart);
      onRangeChange([start, start + width]);
    } else if (event.key === 'ArrowRight' || event.key === 'ArrowUp') {
      event.preventDefault();
      const start = Math.min(1 - width, currentStart + nextStart);
      onRangeChange([start, start + width]);
    } else if (event.key === 'Home') {
      event.preventDefault();
      onRangeChange([0, width]);
    } else if (event.key === 'End') {
      event.preventDefault();
      onRangeChange([1 - width, 1]);
    }
  };

  const visibleWidth = Math.max(width, Math.min(0.016, 1));
  const visibleStart = Math.max(0, Math.min(1 - visibleWidth, range[0]));
  const valueText = `${Math.round(range[0] * (pointCount - 1) + 1)}–${Math.round(range[1] * (pointCount - 1) + 1)} / ${pointCount}`;

  return <div className="results-chart-scrollbar" role="group" aria-label={label}>
    <div ref={trackRef} className="results-chart-scrollbar-track" onPointerDown={handleTrackPointerDown}>
      <svg className="results-chart-scrollbar-preview" viewBox={`0 0 ${PREVIEW_WIDTH} ${PREVIEW_HEIGHT}`} preserveAspectRatio="none" aria-hidden="true">
        {series.map((item, index) => <path key={item.id} d={previewPath(item.values, pointCount, min, max)} style={{ stroke: CHART_COLORS[index % CHART_COLORS.length] }} />)}
      </svg>
      <div
        className="results-chart-scrollbar-thumb"
        role="slider"
        tabIndex={0}
        aria-label={label}
        aria-valuemin={0}
        aria-valuemax={Math.round((1 - width) * (pointCount - 1))}
        aria-valuenow={Math.round(range[0] * (pointCount - 1))}
        aria-valuetext={valueText}
        onPointerDown={handleThumbPointerDown}
        onPointerMove={handleThumbPointerMove}
        onPointerUp={handleThumbPointerUp}
        onPointerCancel={handleThumbPointerCancel}
        onKeyDown={handleThumbKeyDown}
        style={{ left: `${visibleStart * 100}%`, width: `${visibleWidth * 100}%` }}
      />
    </div>
  </div>;
}
