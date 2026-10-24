import type { Language } from '../../../shared/workbench';
import type { TerrainUSGSProduct } from './usgsTypes';
import { USGSMapHoverControl } from './USGSMapHoverControl';

export function USGSMapToolTip({ language, product }: { language: Language; product?: TerrainUSGSProduct }) {
  if (!product) return <div className="usgs-map-tooltip is-empty" aria-live="polite">{language === 'zh' ? '将指针移到产品边界上查看信息。' : 'Hover over a product footprint to inspect it.'}</div>;
  return <aside className="usgs-map-tooltip" aria-live="polite"><USGSMapHoverControl language={language} product={product} /></aside>;
}
