import type { Language } from '../../../shared/workbench';
import type { TerrainUSGSProduct } from './usgsTypes';

export function USGSMapHoverControl({ language, product }: { language: Language; product: TerrainUSGSProduct }) {
  const zh = language === 'zh';
  return <dl className="usgs-hover-properties">
    <div><dt>{zh ? '产品' : 'Product'}</dt><dd>{product.title}</dd></div>
    <div><dt>{zh ? '分辨率' : 'Resolution'}</dt><dd>{product.resolution}</dd></div>
    {product.cellSize != null && Number.isFinite(product.cellSize) && <div><dt>{zh ? '像元大小' : 'Cell size'}</dt><dd>{product.cellSize}</dd></div>}
    <div><dt>{zh ? '发布日期' : 'Publication date'}</dt><dd>{product.publicationDate || '—'}</dd></div>
    <div><dt>{zh ? '产品 ID' : 'Product ID'}</dt><dd><code>{product.id}</code></dd></div>
  </dl>;
}
