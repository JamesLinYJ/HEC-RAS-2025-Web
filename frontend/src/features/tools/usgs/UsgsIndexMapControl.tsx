import type { Language } from '../../../shared/workbench';
import type { TerrainUSGSExtent, TerrainUSGSProduct } from './usgsTypes';

interface Props {
  language: Language;
  products: TerrainUSGSProduct[];
  extent: TerrainUSGSExtent;
  selectedIds: Set<string>;
  onSelect: (id: string) => void;
  onHover: (product?: TerrainUSGSProduct) => void;
}
const WIDTH = 640;
const HEIGHT = 340;
const PAD = 18;

export function UsgsIndexMapControl({ language, products, extent, selectedIds, onSelect, onHover }: Props) {
  const zh = language === 'zh';
  const points = products.flatMap(product => product.projectedBoundary ?? []).filter(point => Number.isFinite(point.x) && Number.isFinite(point.y));
  const xs = [...points.map(point => point.x), extent.minX, extent.maxX];
  const ys = [...points.map(point => point.y), extent.minY, extent.maxY];
  const minX = Math.min(...xs), maxX = Math.max(...xs), minY = Math.min(...ys), maxY = Math.max(...ys);
  const spanX = Math.max(maxX - minX, 1e-12), spanY = Math.max(maxY - minY, 1e-12);
  const scale = Math.min((WIDTH - PAD * 2) / spanX, (HEIGHT - PAD * 2) / spanY);
  const x = (value: number): number => PAD + (value - minX) * scale + ((WIDTH - PAD * 2) - spanX * scale) / 2;
  const y = (value: number): number => HEIGHT - PAD - (value - minY) * scale - ((HEIGHT - PAD * 2) - spanY * scale) / 2;
  const queryBox = `${x(extent.minX)},${y(extent.maxY)} ${x(extent.maxX)},${y(extent.maxY)} ${x(extent.maxX)},${y(extent.minY)} ${x(extent.minX)},${y(extent.minY)}`;
  return <div className="usgs-index-map" role="img" aria-label={zh ? 'USGS 产品真实投影边界预览' : 'USGS returned projected product footprints'}>
    <svg viewBox={`0 0 ${WIDTH} ${HEIGHT}`} aria-hidden="true">
      <rect x="0" y="0" width={WIDTH} height={HEIGHT} rx="12" className="usgs-map-background" />
      <polygon points={queryBox} className="usgs-query-extent" />
      {products.map(product => {
        const boundary = product.projectedBoundary;
        if (!boundary || boundary.length < 3 || boundary.some(point => !Number.isFinite(point.x) || !Number.isFinite(point.y))) return null;
        const selected = selectedIds.has(product.id);
        return <polygon key={product.id} points={boundary.map(point => `${x(point.x)},${y(point.y)}`).join(' ')}
          className={selected ? 'usgs-product-footprint is-selected' : 'usgs-product-footprint'}
          tabIndex={0} role="button" aria-label={product.title} aria-pressed={selected}
          onClick={() => onSelect(product.id)} onKeyDown={event => {
            if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); onSelect(product.id); }
          }} onMouseEnter={() => onHover(product)} onMouseLeave={() => onHover(undefined)}
          onFocus={() => onHover(product)} onBlur={() => onHover(undefined)} />;
      })}
    </svg>
    <span className="usgs-map-legend"><i aria-hidden="true" />{zh ? '查询范围' : 'Query extent'} <b aria-hidden="true" />{zh ? '产品边界' : 'Product footprint'}</span>
    {!products.some(product => product.projectedBoundary?.length) && <p className="usgs-map-empty">{zh ? '服务端没有返回可绘制的产品投影边界。' : 'The server returned no drawable projected product boundaries.'}</p>}
  </div>;
}
