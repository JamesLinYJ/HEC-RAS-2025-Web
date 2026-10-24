import { useMemo } from 'react';
import type { Language } from '../../../shared/workbench';
import type { DssTimeSeriesData } from './types';

interface TimeSeriesGraphProps {
  data: DssTimeSeriesData;
  pathname: string;
  language: Language;
}

const width = 720;
const height = 252;
const margin = { left: 58, right: 14, top: 16, bottom: 38 };

function displayNumber(value: number): string {
  return Number.isFinite(value) ? Number(value.toPrecision(5)).toString() : '—';
}

function shortTime(value: string): string {
  return value.length > 20 ? `${value.slice(0, 19)}…` : value;
}

export function TimeSeriesGraph({ data, pathname, language }: TimeSeriesGraphProps) {
  const zh = language === 'zh';
  const plot = useMemo(() => {
    if (data.times.length !== data.values.length || data.times.length === 0) return null;
    const times = data.times.map((value) => Date.parse(value));
    if (times.some((value) => !Number.isFinite(value))) return null;
    const finiteValues = data.values.filter((value): value is number => value !== null && Number.isFinite(value));
    if (finiteValues.length === 0) return { paths: [] as string[], yTicks: [] as Array<{ y: number; value: number }>, xTicks: [] as Array<{ x: number; label: string; title: string }>, missingOnly: true };

    let xMin = Number.POSITIVE_INFINITY;
    let xMax = Number.NEGATIVE_INFINITY;
    for (const time of times) {
      xMin = Math.min(xMin, time);
      xMax = Math.max(xMax, time);
    }
    let yMin = Number.POSITIVE_INFINITY;
    let yMax = Number.NEGATIVE_INFINITY;
    for (const value of finiteValues) {
      yMin = Math.min(yMin, value);
      yMax = Math.max(yMax, value);
    }
    if (yMin === yMax) {
      const delta = Math.max(Math.abs(yMin) * 0.05, 1);
      yMin -= delta;
      yMax += delta;
    }
    const innerWidth = width - margin.left - margin.right;
    const innerHeight = height - margin.top - margin.bottom;
    const xFor = (time: number): number => margin.left + (xMax === xMin ? 0.5 : (time - xMin) / (xMax - xMin)) * innerWidth;
    const yFor = (value: number): number => margin.top + (yMax - value) / (yMax - yMin) * innerHeight;
    const segments: string[] = [];
    let segment: string[] = [];
    for (let index = 0; index < data.values.length; index += 1) {
      const value = data.values[index];
      if (value === null || !Number.isFinite(value)) {
        if (segment.length > 0) segments.push(segment.join(' '));
        segment = [];
        continue;
      }
      const time = times[index];
      if (time === undefined) continue;
      segment.push(`${segment.length === 0 ? 'M' : 'L'} ${xFor(time)} ${yFor(value)}`);
    }
    if (segment.length > 0) segments.push(segment.join(' '));
    const tickCount = 4;
    const yTicks = Array.from({ length: tickCount + 1 }, (_, index) => {
      const value = yMax - (yMax - yMin) * index / tickCount;
      return { y: yFor(value), value };
    });
    const xIndexes = [...new Set([0, Math.floor((data.times.length - 1) / 2), data.times.length - 1])];
    const xTicks = xIndexes.flatMap((index) => {
      const time = times[index];
      const label = data.times[index];
      return time === undefined || label === undefined ? [] : [{ x: xFor(time), label: shortTime(label), title: label }];
    });
    return { paths: segments, yTicks, xTicks, missingOnly: false };
  }, [data]);

  return <section className="project-dss-time-series-graph" aria-label={zh ? 'DSS 时序图' : 'DSS time-series graph'}>
    <div className="project-dss-visual-heading">
      <strong>{zh ? '时序图' : 'Time-series graph'}</strong>
      <span>{data.dataType || (zh ? '数据类型未提供' : 'Data type not provided')} · {data.units || (zh ? '单位未提供' : 'Units not provided')}</span>
    </div>
    {!plot && <p role="status">{zh ? 'Host 未返回长度一致且可解析的时刻和值，无法绘制该时序。原始表格仍可查看。' : 'The host did not return matching values and parseable timestamps, so this series cannot be plotted. The source table remains available.'}</p>}
    {plot?.missingOnly && <p role="status">{zh ? '此记录没有可绘制的有效数值；所有原值均为 NoData。' : 'This record has no plottable values; every source value is NoData.'}</p>}
    {plot && !plot.missingOnly && <svg className="project-dss-time-series-svg" viewBox={`0 0 ${width} ${height}`} role="img" aria-label={`${pathname} · ${data.units || (zh ? '单位未提供' : 'units not provided')}`}>
      <title>{pathname}</title>
      {plot.yTicks.map((tick, index) => <g key={`y:${index}`}>
        <line x1={margin.left} x2={width - margin.right} y1={tick.y} y2={tick.y} className="project-dss-graph-grid" />
        <text x={margin.left - 7} y={tick.y + 3} textAnchor="end" className="project-dss-graph-label">{displayNumber(tick.value)}</text>
      </g>)}
      <line x1={margin.left} x2={margin.left} y1={margin.top} y2={height - margin.bottom} className="project-dss-graph-axis" />
      <line x1={margin.left} x2={width - margin.right} y1={height - margin.bottom} y2={height - margin.bottom} className="project-dss-graph-axis" />
      {plot.paths.map((path, index) => <path key={`segment:${index}`} d={path} className="project-dss-graph-line" />)}
      {plot.xTicks.map((tick, index) => <text key={`x:${index}`} x={tick.x} y={height - 12} textAnchor={index === 0 ? 'start' : index === plot.xTicks.length - 1 ? 'end' : 'middle'} className="project-dss-graph-label"><title>{tick.title}</title>{tick.label}</text>)}
    </svg>}
    <p className="project-dss-graph-footnote">{zh ? `来自 Host 的 ${data.values.length} 个时序样本；NoData 处断线，不插值。` : `${data.values.length} source samples from the host; NoData breaks the line without interpolation.`}</p>
  </section>;
}
