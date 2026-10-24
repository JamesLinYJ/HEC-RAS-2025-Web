import type { XY } from './mapTypes';

export interface RasMapPositionIndicatorProps {
  point: XY | null;
  crs: string;
  language: 'zh' | 'en';
}

function coordinateLabel(value: number): string {
  if (!Number.isFinite(value)) return '—';
  return Number(value.toPrecision(6)).toString();
}

function unitsForCrs(crs: string, language: 'zh' | 'en'): string {
  if (crs === 'EPSG:4326') return '°';
  if (crs === 'EPSG:3857') return 'm';
  return language === 'zh' ? '源坐标单位' : 'source units';
}

export function RasMapPositionIndicator({ point, crs, language }: RasMapPositionIndicatorProps) {
  const labels = language === 'zh'
    ? { waiting: '移动指针读取坐标', x: 'X', y: 'Y' }
    : { waiting: 'Move the pointer to read coordinates', x: 'X', y: 'Y' };

  return (
    <span className="map-position-indicator" aria-live="polite" aria-label={point
      ? `${labels.x} ${coordinateLabel(point[0])}, ${labels.y} ${coordinateLabel(point[1])} ${unitsForCrs(crs, language)}`
      : labels.waiting}>
      <i className="map-status-dot" />
      {point
        ? <><span>{labels.x}: {coordinateLabel(point[0])}</span><span>{labels.y}: {coordinateLabel(point[1])}</span><small>{unitsForCrs(crs, language)}</small></>
        : <span>{labels.waiting}</span>}
    </span>
  );
}
