import type { Language } from '../../../shared/workbench';
import type { ClassicCrossSectionProfile } from './classicCrossSectionTypes';

interface CrossSectionEditorControlProps {
  profile: ClassicCrossSectionProfile;
  language: Language;
}

const plot = { left: 54, right: 782, top: 18, bottom: 224 };

function finitePoints(profile: ClassicCrossSectionProfile) {
  return profile.points.filter((point): point is typeof point & { station: number; elevation: number } =>
    typeof point.station === 'number' && Number.isFinite(point.station)
    && typeof point.elevation === 'number' && Number.isFinite(point.elevation));
}

function buildSegments(profile: ClassicCrossSectionProfile, x: (value: number) => number, y: (value: number) => number): string[] {
  const segments: string[] = [];
  let current: string[] = [];
  const finish = (): void => {
    if (current.length > 0) segments.push(current.join(' '));
    current = [];
  };
  for (const point of profile.points) {
    if (typeof point.station !== 'number' || !Number.isFinite(point.station)
      || typeof point.elevation !== 'number' || !Number.isFinite(point.elevation)) {
      finish();
      continue;
    }
    current.push(`${x(point.station)},${y(point.elevation)}`);
  }
  finish();
  return segments;
}

export function CrossSectionEditorControl({ profile, language }: CrossSectionEditorControlProps) {
  const zh = language === 'zh';
  const points = finitePoints(profile);
  if (!profile.hasProfile || points.length === 0) {
    return <div className="results-cross-section-empty" data-testid="classic-cross-section-no-profile">
      {zh ? '此断面没有可用的计算站距—高程剖面。' : 'This section has no computation station-elevation profile.'}
    </div>;
  }

  let minX = Number.POSITIVE_INFINITY;
  let maxX = Number.NEGATIVE_INFINITY;
  let minY = Number.POSITIVE_INFINITY;
  let maxY = Number.NEGATIVE_INFINITY;
  for (const point of points) {
    minX = Math.min(minX, point.station);
    maxX = Math.max(maxX, point.station);
    minY = Math.min(minY, point.elevation);
    maxY = Math.max(maxY, point.elevation);
  }
  if (minX === maxX) { minX -= 0.5; maxX += 0.5; }
  if (minY === maxY) { const padding = Math.max(Math.abs(minY) * 0.05, 0.5); minY -= padding; maxY += padding; }
  const xPadding = (maxX - minX) * 0.035;
  const yPadding = (maxY - minY) * 0.08;
  minX -= xPadding; maxX += xPadding; minY -= yPadding; maxY += yPadding;
  const x = (value: number): number => plot.left + ((value - minX) / (maxX - minX)) * (plot.right - plot.left);
  const y = (value: number): number => plot.bottom - ((value - minY) / (maxY - minY)) * (plot.bottom - plot.top);
  const segments = buildSegments(profile, x, y);
  const locale = zh ? 'zh-CN' : 'en-US';
  const format = (value: number): string => value.toLocaleString(locale, { maximumFractionDigits: 4 });
  const banks = profile.banks.filter((point) => typeof point.station === 'number' && Number.isFinite(point.station)
    && typeof point.elevation === 'number' && Number.isFinite(point.elevation));
  const ticks = Array.from({ length: 5 }, (_, index) => index / 4);

  return <div className="results-cross-section-chart" data-testid="classic-cross-section-profile">
    <svg viewBox="0 0 800 260" role="img" aria-label={zh ? '经典几何断面站距和高程' : 'Classic geometry cross-section station and elevation'}>
      {ticks.map((fraction) => {
        const tickX = plot.left + fraction * (plot.right - plot.left);
        const tickY = plot.top + fraction * (plot.bottom - plot.top);
        const xValue = minX + fraction * (maxX - minX);
        const yValue = maxY - fraction * (maxY - minY);
        return <g key={fraction} className="results-cross-section-grid">
          <line x1={plot.left} x2={plot.right} y1={tickY} y2={tickY} />
          <line x1={tickX} x2={tickX} y1={plot.top} y2={plot.bottom} />
          <text x={plot.left - 8} y={tickY + 4} textAnchor="end">{format(yValue)}</text>
          <text x={tickX} y={plot.bottom + 18} textAnchor="middle">{format(xValue)}</text>
        </g>;
      })}
      <line className="results-cross-section-axis" x1={plot.left} x2={plot.right} y1={plot.bottom} y2={plot.bottom} />
      <line className="results-cross-section-axis" x1={plot.left} x2={plot.left} y1={plot.top} y2={plot.bottom} />
      {banks.map((bank, index) => <g key={`${bank.ordinal}-${index}`} className="results-cross-section-bank">
        <line x1={x(bank.station!)} x2={x(bank.station!)} y1={y(bank.elevation!)} y2={plot.bottom} />
        <circle cx={x(bank.station!)} cy={y(bank.elevation!)} r="4" />
      </g>)}
      {segments.map((segment, index) => <polyline key={index} points={segment} className="results-cross-section-line" />)}
      {points.length === 1 && <circle cx={x(points[0]!.station)} cy={y(points[0]!.elevation)} r="3.5" className="results-cross-section-point" />}
      <text className="results-cross-section-label" x={(plot.left + plot.right) / 2} y="254" textAnchor="middle">{zh ? '站距' : 'Station'}</text>
      <text className="results-cross-section-label" transform={`translate(16 ${(plot.top + plot.bottom) / 2}) rotate(-90)`} textAnchor="middle">{zh ? '高程' : 'Elevation'}</text>
    </svg>
    <div className="results-cross-section-legend"><i />{zh ? '计算站距—高程剖面' : 'Computation station-elevation profile'}{banks.length > 0 && <><b />{zh ? '岸线采样点' : 'Bank samples'}</>}</div>
  </div>;
}
