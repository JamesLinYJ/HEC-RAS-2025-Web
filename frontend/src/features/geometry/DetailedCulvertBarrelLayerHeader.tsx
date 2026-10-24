import type { Language } from '../../shared/workbench';
import type { GeometryRow } from './geometryTypes';

export interface DetailedCulvertBarrelLayerHeaderProps {
  language: Language;
  rows: GeometryRow[];
  selectedRows: GeometryRow[];
}

/** Displays the linked culvert-barrel layer name and current table selection. */
export function DetailedCulvertBarrelLayerHeader({ language, rows, selectedRows }: DetailedCulvertBarrelLayerHeaderProps) {
  const layerName = language === 'zh' ? '涵洞管身图层' : 'Culvert Barrels';
  const countText = language === 'zh' ? `${rows.length} 个要素` : `${rows.length} features`;
  const selectedText = language === 'zh' ? `${selectedRows.length} 项已选` : `${selectedRows.length} selected`;
  return <section className="geometry-panel" aria-label={layerName}>
    <header className="geometry-panel-heading"><div>
      <h3>{layerName}</h3>
      <p>{language === 'zh' ? `${countText} · ${selectedText}` : `${countText} · ${selectedText}`}</p>
    </div><span className="geometry-count-pill">{selectedRows.length}</span></header>
  </section>;
}

export default DetailedCulvertBarrelLayerHeader;
