import type { Language } from '../../../shared/workbench';
import type { RasterSymbologyColorStop } from './rasterSymbologyWorkflow';
import { colorInputValue } from './rasterSymbologyWorkflow';

interface Props { language: Language; name: string; stops: RasterSymbologyColorStop[]; grayscale?: boolean }

export function InterpolatedSurfaceFillEditor({ language, name, stops, grayscale = false }: Props) {
  const zh = language === 'zh';
  const first = stops[0]?.value;
  const last = stops[stops.length - 1]?.value;
  return (
    <section className="raster-symbology-fill-summary" aria-label={zh ? '插值表面填充' : 'Interpolated surface fill'}>
      <div className="raster-symbology-fill-title"><strong>{name || (zh ? '颜色带' : 'Color ramp')}</strong><span>{stops.length} {zh ? '个颜色断点' : 'color stops'}</span></div>
      <div className="raster-symbology-fill-ramp" aria-hidden="true">
        {stops.map((stop, index) => {
          const shade = stops.length <= 1 ? 0 : Math.round(255 * index / (stops.length - 1));
          const color = grayscale ? `#${shade.toString(16).padStart(2, '0').repeat(3)}` : colorInputValue(stop.color);
          return <i key={stop.id} style={{ left: `${stops.length <= 1 ? 0 : (index / (stops.length - 1)) * 100}%`, backgroundColor: color }} />;
        })}
      </div>
      <div className="raster-symbology-fill-range"><span>{formatValue(first)}</span><span>{formatValue(last)}</span></div>
    </section>
  );
}

function formatValue(value: number | undefined): string {
  return value === undefined || !Number.isFinite(value) ? '—' : new Intl.NumberFormat(undefined, { maximumSignificantDigits: 8 }).format(value);
}
