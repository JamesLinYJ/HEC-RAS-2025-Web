import { useId, useMemo, useRef, useState } from 'react';
import type { PointerEvent as ReactPointerEvent, WheelEvent as ReactWheelEvent } from 'react';
import type { ResultDataset, ResultSeries } from '../data';
import type { ChartSettings } from './chartTypes';
import { createChartScale, formatTimeTicks, CHART_LAYOUT, CHART_PLOT_HEIGHT, CHART_PLOT_WIDTH } from './chartTypes';
import { ChartAxisControl } from './ChartAxisControl';
import { ChartLegend } from './ChartLegend';
import { ChartPlot } from './ChartPlot';
import { ChartScrollbar } from './ChartScrollbar';
import './charting.css';

interface ChartViewProps {
  dataset: ResultDataset | null;
  series: ResultSeries[];
  selectedIndex: number;
  onSelectIndex: (index: number) => void;
  range: [number, number];
  onRangeChange: (range: [number, number]) => void;
  settings: ChartSettings;
  onSettingsChange: (settings: ChartSettings) => void;
  language: 'zh' | 'en';
}

export function ChartView({ dataset, series, selectedIndex, onSelectIndex, range, onRangeChange, settings, onSettingsChange, language }: ChartViewProps) {
  const svgRef = useRef<SVGSVGElement>(null);
  const dragRef = useRef<{ x: number; pointerId: number } | null>(null);
  const [dragRange, setDragRange] = useState<[number, number] | null>(null);
  const [cursorIndex, setCursorIndex] = useState<number | null>(null);
  const clipId = 'result-chart-clip-' + useId().replaceAll(':', '');
  const text = (zh: string, en: string): string => language === 'zh' ? zh : en;

  const model = useMemo(() => {
    if (!dataset) return null;
    return createChartScale(dataset, series, range, settings);
  }, [dataset, series, range, settings.yMin, settings.yMax]);

  const indexAt = (event: ReactPointerEvent<SVGSVGElement>): number => {
    const rect = event.currentTarget.getBoundingClientRect();
    const viewX = (event.clientX - rect.left) * CHART_LAYOUT.width / rect.width;
    const fraction = Math.max(0, Math.min(1, (viewX - CHART_LAYOUT.left) / CHART_PLOT_WIDTH));
    return model ? Math.round(model.start + fraction * (model.end - model.start)) : 0;
  };

  const rangeAtViewX = (viewX: number): number => {
    const rect = svgRef.current?.getBoundingClientRect();
    if (!rect || !model) return 0;
    const x = (viewX - rect.left) * CHART_LAYOUT.width / rect.width;
    return Math.max(0, Math.min(1, (model.start + ((x - CHART_LAYOUT.left) / CHART_PLOT_WIDTH) * (model.end - model.start)) / Math.max(1, model.points - 1)));
  };

  const handleWheel = (event: ReactWheelEvent<SVGSVGElement>): void => {
    if (!dataset || dataset.times.length < 3) return;
    event.preventDefault();
    const rect = event.currentTarget.getBoundingClientRect();
    const viewX = (event.clientX - rect.left) * CHART_LAYOUT.width / rect.width;
    const viewFraction = Math.max(0, Math.min(1, (viewX - CHART_LAYOUT.left) / CHART_PLOT_WIDTH));
    const focus = range[0] + viewFraction * (range[1] - range[0]);
    const width = range[1] - range[0];
    const nextWidth = Math.max(1 / Math.max(2, dataset.times.length - 1), Math.min(1, width * (event.deltaY < 0 ? 0.78 : 1.28)));
    const ratio = width > 0 ? (focus - range[0]) / width : 0.5;
    let start = focus - ratio * nextWidth;
    start = Math.max(0, Math.min(1 - nextWidth, start));
    onRangeChange([start, start + nextWidth]);
  };

  if (!dataset || !model) {
    return <div className="results-chart-empty"><span className="results-chart-icon" aria-hidden="true">⌁</span><p>{text('载入一条结果序列后将在此显示时间曲线。', 'Load a result series to display its time curve here.')}</p></div>;
  }

  const activeIndex = cursorIndex ?? selectedIndex;
  const xTickIndexes = Array.from(new Set([model.start, Math.round((model.start + model.end) / 2), model.end]));
  const timeTickDisplay = formatTimeTicks(xTickIndexes.map((index) => dataset.times[index] ?? ''), xTickIndexes, language);
  const selectedVisible = activeIndex >= model.start && activeIndex <= model.end;

  return <div className="results-chart-wrap">
    <svg
      ref={svgRef}
      className="results-chart-svg"
      viewBox={`0 0 ${CHART_LAYOUT.width} ${CHART_LAYOUT.height}`}
      role="img"
      aria-label={text('时间序列折线图；滚轮缩放，拖动选择缩放区间', 'Time-series line chart; use the wheel to zoom and drag to select a range')}
      onPointerMove={(event) => {
        const index = indexAt(event);
        setCursorIndex(index);
        onSelectIndex(index);
        if (dragRef.current?.pointerId === event.pointerId) {
          setDragRange([rangeAtViewX(dragRef.current.x), rangeAtViewX(event.clientX)]);
        }
      }}
      onPointerLeave={() => { if (!dragRef.current) setCursorIndex(null); }}
      onPointerDown={(event) => {
        if (event.button !== 0) return;
        dragRef.current = { x: event.clientX, pointerId: event.pointerId };
        event.currentTarget.setPointerCapture(event.pointerId);
      }}
      onPointerUp={(event) => {
        const drag = dragRef.current;
        if (!drag || drag.pointerId !== event.pointerId) return;
        const first = rangeAtViewX(drag.x);
        const second = rangeAtViewX(event.clientX);
        dragRef.current = null;
        setDragRange(null);
        if (Math.abs(second - first) > 0.025) {
          const next: [number, number] = [Math.max(0, Math.min(first, second)), Math.min(1, Math.max(first, second))];
          onRangeChange(next);
          onSelectIndex(Math.round((next[0] + next[1]) / 2 * Math.max(1, model.points - 1)));
        } else {
          onSelectIndex(indexAt(event));
        }
        if (event.currentTarget.hasPointerCapture(event.pointerId)) event.currentTarget.releasePointerCapture(event.pointerId);
      }}
      onPointerCancel={(event) => {
        if (dragRef.current?.pointerId !== event.pointerId) return;
        dragRef.current = null;
        setDragRange(null);
      }}
      onWheel={handleWheel}
    >
      <defs><clipPath id={clipId}><rect x={CHART_LAYOUT.left} y={CHART_LAYOUT.top} width={CHART_PLOT_WIDTH} height={CHART_PLOT_HEIGHT} /></clipPath></defs>
      <ChartAxisControl scale={model} showGrid={settings.grid} xTickIndexes={xTickIndexes} xTickLabels={timeTickDisplay.labels} settings={settings} onSettingsChange={onSettingsChange} language={language} />
      <ChartPlot
        dataset={dataset}
        series={series}
        scale={model}
        activeIndex={activeIndex}
        selectedVisible={selectedVisible}
        showMarkers={settings.markers}
        stepLine={settings.step}
        timePrecision={timeTickDisplay.precision}
        language={language}
        clipId={clipId}
        dragRange={dragRange}
      />
    </svg>
    <ChartScrollbar dataset={dataset} series={series} range={range} onRangeChange={onRangeChange} language={language} />
    <div className="results-chart-footer">
      <ChartLegend series={series} language={language} />
      <button type="button" className="results-link-button" onClick={() => onRangeChange([0, 1])}>{text('重置缩放', 'Reset zoom')}</button>
    </div>
    <details className="results-chart-settings">
      <summary>{text('图表设置', 'Chart settings')}</summary>
      <div className="results-chart-settings-grid">
        <label><input type="checkbox" checked={settings.grid} onChange={(event) => onSettingsChange({ ...settings, grid: event.target.checked })} />{text('显示网格', 'Show grid')}</label>
        <label><input type="checkbox" checked={settings.markers} onChange={(event) => onSettingsChange({ ...settings, markers: event.target.checked })} />{text('显示数据点', 'Show markers')}</label>
        <label><input type="checkbox" checked={settings.step} onChange={(event) => onSettingsChange({ ...settings, step: event.target.checked })} />{text('阶梯线', 'Step line')}</label>
        <label>{text('Y 轴最小值', 'Y-axis minimum')}<input value={settings.yMin} inputMode="decimal" placeholder={text('自动', 'Auto')} onChange={(event) => onSettingsChange({ ...settings, yMin: event.target.value })} /></label>
        <label>{text('Y 轴最大值', 'Y-axis maximum')}<input value={settings.yMax} inputMode="decimal" placeholder={text('自动', 'Auto')} onChange={(event) => onSettingsChange({ ...settings, yMax: event.target.value })} /></label>
      </div>
    </details>
  </div>;
}
