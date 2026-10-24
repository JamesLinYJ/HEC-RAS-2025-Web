import { useMemo } from 'react';
import type { Language } from '../../../shared/workbench';
import type { DssPairedData } from './types';

interface PairedDataGraphViewProps {
  data: DssPairedData;
  pathname: string;
  language: Language;
}

const width = 720;
const height = 252;
const margin = { left: 58, right: 14, top: 16, bottom: 38 };
const colors = ['#62c5a4', '#7caee6', '#e3b970', '#c18de0', '#e18181', '#70c6cf'];

function displayNumber(value: number): string {
  return Number.isFinite(value) ? Number(value.toPrecision(5)).toString() : '—';
}

export function PairedDataGraphView({ data, pathname, language }: PairedDataGraphViewProps) {
  const zh = language === 'zh';
  const plot = useMemo(() => {
    let xMin = Number.POSITIVE_INFINITY;
    let xMax = Number.NEGATIVE_INFINITY;
    let yMin = Number.POSITIVE_INFINITY;
    let yMax = Number.NEGATIVE_INFINITY;
    for (const value of data.ordinates) {
      if (value !== null && Number.isFinite(value)) { xMin = Math.min(xMin, value); xMax = Math.max(xMax, value); }
    }
    for (const curve of data.curves) for (const value of curve.values) {
      if (value !== null && Number.isFinite(value)) { yMin = Math.min(yMin, value); yMax = Math.max(yMax, value); }
    }
    if (![xMin, xMax, yMin, yMax].every(Number.isFinite)) return null;
    if (xMin === xMax) { const pad = Math.max(Math.abs(xMin) * 0.05, 1); xMin -= pad; xMax += pad; }
    if (yMin === yMax) { const pad = Math.max(Math.abs(yMin) * 0.05, 1); yMin -= pad; yMax += pad; }
    const innerWidth = width - margin.left - margin.right;
    const innerHeight = height - margin.top - margin.bottom;
    const xFor = (value: number): number => margin.left + (value - xMin) / (xMax - xMin) * innerWidth;
    const yFor = (value: number): number => margin.top + (yMax - value) / (yMax - yMin) * innerHeight;
    const paths = data.curves.map((curve) => {
      const segments: string[] = [];
      let segment: string[] = [];
      const count = Math.min(data.ordinates.length, curve.values.length);
      for (let index = 0; index < count; index += 1) {
        const x = data.ordinates[index];
        const y = curve.values[index];
        if (x === null || y === null || !Number.isFinite(x) || !Number.isFinite(y)) {
          if (segment.length > 0) segments.push(segment.join(' '));
          segment = [];
          continue;
        }
        segment.push(`${segment.length === 0 ? 'M' : 'L'} ${xFor(x)} ${yFor(y)}`);
      }
      if (segment.length > 0) segments.push(segment.join(' '));
      return segments;
    });
    const ticks = 4;
    const xTicks = Array.from({ length: ticks + 1 }, (_, index) => {
      const value = xMin + (xMax - xMin) * index / ticks;
      return { x: xFor(value), label: displayNumber(value) };
    });
    const yTicks = Array.from({ length: ticks + 1 }, (_, index) => {
      const value = yMax - (yMax - yMin) * index / ticks;
      return { y: yFor(value), label: displayNumber(value) };
    });
    return { paths, xTicks, yTicks };
  }, [data]);

  return <section className="project-dss-paired-graph" aria-label={zh ? 'DSS 配对数据图' : 'DSS paired-data graph'}>
    <div className="project-dss-visual-heading">
      <strong>{zh ? '配对数据图' : 'Paired-data graph'}</strong>
      <span>{data.independentType || (zh ? '自变量' : 'Independent')} ({data.independentUnits || (zh ? '单位未提供' : 'units not provided')}) · {data.dependentType || (zh ? '因变量' : 'Dependent')} ({data.dependentUnits || (zh ? '单位未提供' : 'units not provided')})</span>
    </div>
    {!plot && <p role="status">{zh ? 'Host 未返回可绘制的配对数值。' : 'The host did not return plottable paired values.'}</p>}
    {plot && <svg className="project-dss-time-series-svg" viewBox={`0 0 ${width} ${height}`} role="img" aria-label={pathname}>
      <title>{pathname}</title>
      {plot.yTicks.map((tick, index) => <g key={`y:${index}`}><line x1={margin.left} x2={width - margin.right} y1={tick.y} y2={tick.y} className="project-dss-graph-grid" /><text x={margin.left - 7} y={tick.y + 3} textAnchor="end" className="project-dss-graph-label">{tick.label}</text></g>)}
      {plot.xTicks.map((tick, index) => <g key={`x:${index}`}><line x1={tick.x} x2={tick.x} y1={margin.top} y2={height - margin.bottom} className="project-dss-graph-grid project-dss-graph-grid-vertical" /><text x={tick.x} y={height - 12} textAnchor={index === 0 ? 'start' : index === plot.xTicks.length - 1 ? 'end' : 'middle'} className="project-dss-graph-label">{tick.label}</text></g>)}
      <line x1={margin.left} x2={margin.left} y1={margin.top} y2={height - margin.bottom} className="project-dss-graph-axis" />
      <line x1={margin.left} x2={width - margin.right} y1={height - margin.bottom} y2={height - margin.bottom} className="project-dss-graph-axis" />
      {plot.paths.map((segments, curveIndex) => segments.map((path, segmentIndex) => <path key={`${curveIndex}:${segmentIndex}`} d={path} className="project-dss-graph-line" style={{ stroke: colors[curveIndex % colors.length] }} />))}
    </svg>}
    <div className="project-dss-curve-legend">{data.curves.map((curve, index) => <span key={`${curve.label}:${index}`}><i style={{ background: colors[index % colors.length] }} />{curve.label || `${zh ? '曲线' : 'Curve'} ${index + 1}`}</span>)}</div>
    <p className="project-dss-graph-footnote">{zh ? '仅使用 Host 返回的配对自变量与曲线值；NoData 处断线，不插值。' : 'Uses only host-returned paired ordinates and curve values; NoData breaks the line without interpolation.'}</p>
  </section>;
}
