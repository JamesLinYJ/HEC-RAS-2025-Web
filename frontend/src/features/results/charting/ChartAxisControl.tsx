import { useRef, useState } from 'react';
import type { PointerEvent as ReactPointerEvent, WheelEvent as ReactWheelEvent } from 'react';
import type { ChartScale } from './chartTypes';
import { CHART_LAYOUT, CHART_PLOT_HEIGHT, CHART_PLOT_WIDTH, formatAxisTicks, xForIndex } from './chartTypes';
import type { ChartSettings } from './chartTypes';

interface ChartAxisControlProps {
  scale: ChartScale;
  showGrid: boolean;
  xTickIndexes: number[];
  xTickLabels: string[];
  settings: ChartSettings;
  onSettingsChange: (settings: ChartSettings) => void;
  language: 'zh' | 'en';
}

export function ChartAxisControl({ scale, showGrid, xTickIndexes, xTickLabels, settings, onSettingsChange, language }: ChartAxisControlProps) {
  const dragRef = useRef<{ pointerId: number; first: number } | null>(null);
  const [dragEnd, setDragEnd] = useState<number | null>(null);
  const ticks = Array.from({ length: 5 }, (_, index) => scale.min + (scale.max - scale.min) * (4 - index) / 4);
  const tickLabels = formatAxisTicks(ticks);
  const { width, height, left, right, top, bottom } = CHART_LAYOUT;

  const viewY = (event: ReactPointerEvent<SVGRectElement> | ReactWheelEvent<SVGRectElement>): number => {
    const rect = event.currentTarget.ownerSVGElement?.getBoundingClientRect();
    if (!rect || rect.height <= 0) return top + CHART_PLOT_HEIGHT / 2;
    return Math.max(top, Math.min(height - bottom, (event.clientY - rect.top) * height / rect.height));
  };

  const valueAtY = (y: number): number => scale.max - ((y - top) / CHART_PLOT_HEIGHT) * (scale.max - scale.min);

  const handleAxisWheel = (event: ReactWheelEvent<SVGRectElement>): void => {
    if (scale.max <= scale.min) return;
    event.preventDefault();
    event.stopPropagation();
    const focus = valueAtY(viewY(event));
    const span = scale.max - scale.min;
    const nextSpan = span * (event.deltaY < 0 ? 0.78 : 1.28);
    const ratio = (focus - scale.min) / span;
    const min = focus - ratio * nextSpan;
    const max = min + nextSpan;
    onSettingsChange({ ...settings, yMin: String(min), yMax: String(max) });
  };

  const handleAxisPointerDown = (event: ReactPointerEvent<SVGRectElement>): void => {
    if (event.button !== 0) return;
    event.preventDefault();
    event.stopPropagation();
    const first = viewY(event);
    dragRef.current = { pointerId: event.pointerId, first };
    setDragEnd(first);
    event.currentTarget.setPointerCapture(event.pointerId);
  };

  const handleAxisPointerMove = (event: ReactPointerEvent<SVGRectElement>): void => {
    if (dragRef.current?.pointerId !== event.pointerId) return;
    event.stopPropagation();
    setDragEnd(viewY(event));
  };

  const handleAxisPointerUp = (event: ReactPointerEvent<SVGRectElement>): void => {
    const drag = dragRef.current;
    if (!drag || drag.pointerId !== event.pointerId) return;
    event.preventDefault();
    event.stopPropagation();
    dragRef.current = null;
    setDragEnd(null);
    const firstValue = valueAtY(drag.first);
    const secondValue = valueAtY(viewY(event));
    const min = Math.min(firstValue, secondValue);
    const max = Math.max(firstValue, secondValue);
    if (max > min) onSettingsChange({ ...settings, yMin: String(min), yMax: String(max) });
    if (event.currentTarget.hasPointerCapture(event.pointerId)) event.currentTarget.releasePointerCapture(event.pointerId);
  };

  const handleAxisPointerCancel = (event: ReactPointerEvent<SVGRectElement>): void => {
    if (dragRef.current?.pointerId !== event.pointerId) return;
    event.stopPropagation();
    dragRef.current = null;
    setDragEnd(null);
  };

  const selectionY = dragRef.current && dragEnd !== null ? Math.min(dragRef.current.first, dragEnd) : 0;
  const selectionHeight = dragRef.current && dragEnd !== null ? Math.abs(dragEnd - dragRef.current.first) : 0;

  return <g className="results-chart-axes" role="group" aria-label={language === 'zh' ? '图表坐标轴' : 'Chart axes'}>
    {tickLabels.map((label, index) => {
      const y = top + CHART_PLOT_HEIGHT * index / 4;
      return <g key={'y-' + index}>
        {showGrid && <line x1={left} x2={width - right} y1={y} y2={y} className="results-chart-grid" />}
        <text x={left - 12} y={y + 4} textAnchor="end" className="results-chart-axis-label">{label}</text>
      </g>;
    })}
    <line x1={left} x2={left} y1={top} y2={height - bottom} className="results-chart-axis" />
    <line x1={left} x2={width - right} y1={height - bottom} y2={height - bottom} className="results-chart-axis" />
    {xTickIndexes.map((index, tickIndex) => <g key={'x-' + index}>
      {showGrid && <line x1={xForIndex(index, scale)} x2={xForIndex(index, scale)} y1={top} y2={height - bottom} className="results-chart-grid results-chart-grid-vertical" />}
      <text x={xForIndex(index, scale)} y={height - bottom + 24} textAnchor="middle" className="results-chart-axis-label">{xTickLabels[tickIndex]}</text>
    </g>)}
    {selectionHeight > 0 && <rect x={left} y={selectionY} width={CHART_PLOT_WIDTH} height={selectionHeight} className="results-chart-y-selection" />}
    <rect
      x="0"
      y={top}
      width={left}
      height={CHART_PLOT_HEIGHT}
      fill="transparent"
      className="results-chart-y-axis-hit-area"
      aria-label={language === 'zh' ? 'Y 轴：滚轮缩放，拖动选择数值范围' : 'Y axis: use the wheel to zoom and drag to select a value range'}
      onWheel={handleAxisWheel}
      onPointerDown={handleAxisPointerDown}
      onPointerMove={handleAxisPointerMove}
      onPointerUp={handleAxisPointerUp}
      onPointerCancel={handleAxisPointerCancel}
    />
  </g>;
}
