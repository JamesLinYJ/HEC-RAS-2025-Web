import type { Language } from '../../shared/workbench';
import type { AppliedRasterLegend } from './symbology/RasterSymbologyEditor';
import { OverlayItem } from './OverlayItem';

export interface RasMapLegendProps {
  legend: AppliedRasterLegend | null;
  language: Language;
  visible?: boolean;
}

interface DisplayStop { color: string; value: number }

/** Displays only the value/color stops confirmed by the Host's applied snapshot. */
export function RasMapLegend({ legend, language, visible = true }: RasMapLegendProps) {
  if (!visible || !legend) return null;
  const stops = toDisplayStops(legend);
  if (!stops) return null;

  const values = stops.map(stop => stop.value);
  const minimum = values[0];
  const maximum = values[values.length - 1];
  const range = maximum - minimum;
  if (!Number.isFinite(range) || range <= 0) return null;
  const positionedStops = stops.map(stop => ({
    ...stop,
    offset: ((stop.value - minimum) / range) * 100,
  }));
  if (positionedStops.some(stop => !Number.isFinite(stop.offset))) return null;
  const ramp = positionedStops.map(stop => `${stop.color} ${stop.offset}%`).join(', ');
  const number = new Intl.NumberFormat(language === 'zh' ? 'zh-CN' : 'en', { maximumSignificantDigits: 7 });
  const title = legend.source.nodeName || (language === 'zh' ? '栅格图例' : 'Raster legend');

  return (
    <OverlayItem title={title} collapsedTitle={language === 'zh' ? '图例' : 'Legend'}
      icon="▥" language={language} placement="top-right" className="ras-map-legend">
      <div className="ras-map-legend-scale" role="img"
        aria-label={language === 'zh' ? '栅格颜色与实际分级值' : 'Raster colors and applied values'}>
        <div className="ras-map-legend-ramp" style={{ background: `linear-gradient(to top, ${ramp})` }} aria-hidden="true" />
        <ol className="ras-map-legend-ticks" aria-label={language === 'zh' ? '分级值' : 'Values'}>
          {positionedStops.slice().reverse().map((stop, index) => (
            <li key={`${stop.value}:${index}`} style={{ bottom: `${stop.offset}%` }}>
              <span>{number.format(stop.value)}</span><i aria-hidden="true" />
            </li>
          ))}
        </ol>
      </div>
    </OverlayItem>
  );
}

function toDisplayStops(legend: AppliedRasterLegend): DisplayStop[] | null {
  const { draft, values } = legend;
  if (!values || values.length < 2 || values.length !== draft.stops.length) return null;
  const stops: DisplayStop[] = [];
  for (let index = 0; index < values.length; index++) {
    const value = values[index];
    const color = safeHexColor(draft.stops[index]?.color);
    if (!Number.isFinite(value) || !color || value !== draft.stops[index]?.value
      || index > 0 && value <= values[index - 1]) return null;
    stops.push({ color, value });
  }
  return stops;
}

function safeHexColor(value: unknown): string | null {
  if (typeof value !== 'string') return null;
  const color = value.trim();
  if (!/^(?:#)?(?:[\da-f]{6}|[\da-f]{8})$/i.test(color)) return null;
  return color.startsWith('#') ? color : `#${color}`;
}
