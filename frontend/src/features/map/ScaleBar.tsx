import type { Language } from '../../shared/workbench';
import './scaleBar.css';

export type MapScaleUnitSystem = 'SI' | 'USCustomary' | 'coordinates';

/** Use physical units only when the active camera is in a confirmed local project CRS. */
export function projectScaleUnitSystem(projectXml: string | null, projectCoordinates: boolean): MapScaleUnitSystem {
  if (!projectCoordinates || !projectXml) return 'coordinates';
  const document = new DOMParser().parseFromString(projectXml, 'application/xml');
  if (document.querySelector('parsererror')) return 'coordinates';
  const projection = Array.from(document.documentElement.children).find(node => node.localName === 'Projection');
  if (projection?.getAttribute('Type') !== 'Local') return 'coordinates';
  const value = projection.textContent?.trim();
  return value === 'SI' || value === 'USCustomary' ? value : 'coordinates';
}

/** ScaleBar.cs: choose a 1/2/5 distance within 200 actual screen pixels. */
export function ScaleBar({ pixelsPerUnit, availableWidth, unitSystem, language }: {
  pixelsPerUnit: number;
  availableWidth: number;
  unitSystem: MapScaleUnitSystem;
  language: Language;
}) {
  if (!Number.isFinite(pixelsPerUnit) || pixelsPerUnit <= 0 || !Number.isFinite(availableWidth) || availableWidth < 80) return null;
  const targetPixels = Math.min(200, availableWidth - 24);
  const worldDistance = targetPixels / pixelsPerUnit;
  const factor = worldDistance > 10_000 ? unitSystem === 'SI' ? 1_000 : unitSystem === 'USCustomary' ? 5_280 : 1 : 1;
  const distance = worldDistance / factor;
  const magnitude = 10 ** Math.floor(Math.log10(distance));
  const rounded = [1, 2, 5].reduce((best, value) => value * magnitude <= distance ? value * magnitude : best, magnitude);
  const width = rounded * factor * pixelsPerUnit;
  if (!Number.isFinite(width) || width <= 0 || !Number.isFinite(rounded) || rounded <= 0) return null;
  const unit = unitSystem === 'SI' ? factor === 1_000 ? 'km' : 'm'
    : unitSystem === 'USCustomary' ? factor === 5_280 ? 'mi' : 'ft'
      : language === 'zh' ? '坐标单位' : 'coordinate units';
  const formatter = new Intl.NumberFormat(language === 'zh' ? 'zh-CN' : 'en-US', {
    maximumSignificantDigits: 6, notation: rounded < 0.0001 || rounded >= 1e9 ? 'scientific' : 'standard',
  });
  const label = `${formatter.format(rounded)} ${unit}`;
  return <div className="map-scale-bar" role="img" aria-label={language === 'zh' ? `地图比例尺：${label}` : `Map scale: ${label}`}
    data-testid="map-scale-bar" data-world-distance={rounded * factor} data-pixel-width={width} data-unit={unit}>
    <div className="map-scale-bar-labels" style={{ width }}><span>0</span><span>{label}</span></div>
    <div className="map-scale-bar-line" style={{ width }}><i /></div>
  </div>;
}
