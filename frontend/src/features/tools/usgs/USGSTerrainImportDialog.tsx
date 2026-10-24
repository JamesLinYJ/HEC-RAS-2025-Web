import { useMemo, useState } from 'react';
import type { Language } from '../../../shared/workbench';
import type { TerrainUSGSProduct, TerrainUSGSQueryData } from './usgsTypes';
import { filterUSGSProducts, initialUSGSFilter, USGSFilterControl, type USGSFilterState } from './USGSFilterControl';
import { UsgsIndexMapControl } from './UsgsIndexMapControl';
import { USGSMapToolTip } from './USGSMapToolTip';
import { USGSSummary } from './USGSSummary';

interface Props {
  language: Language;
  query: TerrainUSGSQueryData;
  selectedIds: Set<string>;
  importingIds: Set<string>;
  busy: boolean;
  onToggle: (id: string) => void;
  onSelectVisible: (ids: string[], selected: boolean) => void;
  onDownloadAndImport: () => void;
}

export function USGSTerrainImportDialog({ language, query, selectedIds, importingIds, busy, onToggle, onSelectVisible, onDownloadAndImport }: Props) {
  const zh = language === 'zh';
  const [filter, setFilter] = useState<USGSFilterState>(initialUSGSFilter);
  const [hovered, setHovered] = useState<TerrainUSGSProduct>();
  const visible = useMemo(() => filterUSGSProducts(query.products, selectedIds, importingIds, filter), [query.products, selectedIds, importingIds, filter]);
  const allVisibleSelected = visible.length > 0 && visible.every(item => selectedIds.has(item.id));
  return <div className="usgs-dialog">
    <div className="usgs-dialog-heading"><div><span className="usgs-eyebrow">USGS · 3DEP</span><h3>{zh ? 'USGS 地形产品' : 'USGS terrain products'}</h3></div>
      <div className="usgs-result-count">{zh ? `查询匹配 ${query.total} 项 · 可用 ${query.products.length} 项` : `${query.total} matches · ${query.products.length} available`}</div></div>
    <p className="usgs-projection">{zh ? '目标投影' : 'Target projection'}：<code>{query.targetProjection || (zh ? '未设置' : 'Not set')}</code></p>
    <USGSFilterControl language={language} products={query.products} selectedIds={selectedIds} importingIds={importingIds} value={filter} onChange={setFilter} />
    <div className="usgs-map-layout"><UsgsIndexMapControl language={language} products={visible} extent={query.extent}
      selectedIds={selectedIds} onSelect={onToggle} onHover={setHovered} />
      <USGSMapToolTip language={language} product={hovered} /></div>
    <div className="usgs-list-heading"><h4>{zh ? '产品列表' : 'Products'}</h4>
      <label><input type="checkbox" checked={allVisibleSelected} onChange={event => onSelectVisible(visible.map(item => item.id), event.target.checked)} />{zh ? '选择当前可见项' : 'Select visible items'}</label></div>
    {visible.length ? <div className="usgs-product-list">{visible.map(product => <USGSSummary key={product.id} language={language} product={product}
      selected={selectedIds.has(product.id)} importing={importingIds.has(product.id)} onToggle={() => onToggle(product.id)} />)}</div>
      : <p className="usgs-empty">{zh ? '没有符合当前筛选条件的服务端产品。' : 'No server products match the current filters.'}</p>}
    <footer className="usgs-dialog-footer"><span>{zh ? `已选 ${selectedIds.size} 项` : `${selectedIds.size} selected`}</span>
      <button type="button" className="usgs-primary" disabled={busy || selectedIds.size === 0} onClick={onDownloadAndImport}>
        {busy ? (zh ? '下载与导入处理中…' : 'Downloading and importing…') : (zh ? '下载并导入所选地形' : 'Download and import selected terrain')}
      </button></footer>
  </div>;
}
