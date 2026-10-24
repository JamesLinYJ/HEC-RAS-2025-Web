import type { ResultDataset, ResultSeries } from '../data';
import type { ChartScale, TimePrecision } from './chartTypes';
import { CHART_COLORS, CHART_LAYOUT, formatTime, formatValue, isFiniteValue, xForIndex, yForValue } from './chartTypes';

interface ChartPlotProps {
  dataset: ResultDataset;
  series: ResultSeries[];
  scale: ChartScale;
  activeIndex: number;
  selectedVisible: boolean;
  showMarkers: boolean;
  stepLine: boolean;
  timePrecision: TimePrecision;
  language: 'zh' | 'en';
  clipId: string;
  dragRange: [number, number] | null;
}

export function ChartPlot({ dataset, series, scale, activeIndex, selectedVisible, showMarkers, stepLine, timePrecision, language, clipId, dragRange }: ChartPlotProps) {
  const pathFor = (item: ResultSeries): string => {
    let path = '';
    let priorIndex: number | undefined;
    for (let index = scale.start; index <= scale.end; index += 1) {
      const value = item.values[index];
      if (!isFiniteValue(value)) continue;
      const x = xForIndex(index, scale);
      const y = yForValue(value, scale);
      if (priorIndex === undefined || index !== priorIndex + 1) path += ' M ' + x + ' ' + y;
      else if (stepLine) path += ' H ' + x + ' V ' + y;
      else path += ' L ' + x + ' ' + y;
      priorIndex = index;
    }
    return path.trim();
  };
  const { width, height, left, right, top, bottom } = CHART_LAYOUT;
  const tooltipX = Math.min(width - 226, xForIndex(activeIndex, scale) + 10);
  const dragStart = dragRange?.[0];
  const dragEnd = dragRange?.[1];

  return <>
    <g clipPath={'url(#' + clipId + ')'}>{series.map((item, seriesIndex) => {
      const points = item.values.flatMap((value, index) => index >= scale.start && index <= scale.end && isFiniteValue(value) ? [{ value, index }] : []);
      const color = CHART_COLORS[seriesIndex % CHART_COLORS.length];
      const selectedValue = item.values[activeIndex];
      return <g key={item.id}>
        <path d={pathFor(item)} className="results-chart-series" style={{ stroke: color }} />
        {showMarkers && points.map((point) => <circle key={point.index} cx={xForIndex(point.index, scale)} cy={yForValue(point.value, scale)} r="2.8" className="results-chart-marker" style={{ fill: color }} />)}
        {selectedVisible && isFiniteValue(selectedValue) && <circle cx={xForIndex(activeIndex, scale)} cy={yForValue(selectedValue, scale)} r="5" className="results-chart-active-point" style={{ stroke: color }} />}
      </g>;
    })}</g>
    {selectedVisible && <g>
      <line x1={xForIndex(activeIndex, scale)} x2={xForIndex(activeIndex, scale)} y1={top} y2={height - bottom} className="results-chart-cursor" />
      <rect x={tooltipX} y={top + 8} width="210" height={Math.min(40 + series.length * 22, 40 + 8 * 22)} rx="8" className="results-chart-tooltip" />
      <text x={tooltipX + 10} y={top + 25} className="results-chart-tooltip-text">{formatTime(dataset.times[activeIndex] ?? '', language, timePrecision)}</text>
      {series.slice(0, 8).map((item, index) => {
        const value = item.values[activeIndex];
        return <text key={item.id} x={tooltipX + 10} y={top + 45 + index * 22} className="results-chart-tooltip-text">{item.name}: {isFiniteValue(value) ? formatValue(value) : '—'}{item.units ? ' ' + item.units : ''}</text>;
      })}
    </g>}
    {dragStart !== undefined && dragEnd !== undefined && <rect
      x={left + ((Math.min(dragStart, dragEnd) * Math.max(1, scale.points - 1) - scale.start) / Math.max(1, scale.end - scale.start)) * (width - left - right)}
      y={top}
      width={Math.abs(dragEnd - dragStart) * Math.max(1, scale.points - 1) / Math.max(1, scale.end - scale.start) * (width - left - right)}
      height={height - top - bottom}
      className="results-chart-selection"
    />}
  </>;
}
