import type { Language } from '../../../shared/workbench';
import type { TerrainUSGSProduct } from './usgsTypes';

export type USGSSelectionCriteria = 'Importing' | 'Selected' | 'All';
export interface USGSFilterState {
  search: string;
  resolutions: string[];
  fromDate: string;
  toDate: string;
  direction: 'within' | 'before' | 'after';
  criteria: USGSSelectionCriteria;
}
interface Props {
  language: Language;
  products: TerrainUSGSProduct[];
  selectedIds: Set<string>;
  importingIds: Set<string>;
  value: USGSFilterState;
  onChange: (value: USGSFilterState) => void;
}

export function initialUSGSFilter(): USGSFilterState {
  return { search: '', resolutions: [], fromDate: '', toDate: '', direction: 'within', criteria: 'All' };
}

export function filterUSGSProducts(products: TerrainUSGSProduct[], selectedIds: Set<string>,
  importingIds: Set<string>, filter: USGSFilterState): TerrainUSGSProduct[] {
  const query = filter.search.trim().toLocaleLowerCase();
  const lower = filter.fromDate ? Date.parse(`${filter.fromDate}T00:00:00`) : Number.NaN;
  const upper = filter.toDate ? Date.parse(`${filter.toDate}T23:59:59.999`) : Number.NaN;
  return products.filter(product => {
    if (query && !`${product.title}\n${product.id}\n${product.resolution}`.toLocaleLowerCase().includes(query)) return false;
    if (filter.resolutions.length && !filter.resolutions.includes(product.resolution)) return false;
    if (filter.criteria === 'Selected' && !selectedIds.has(product.id)) return false;
    if (filter.criteria === 'Importing' && !importingIds.has(product.id)) return false;
    if (filter.fromDate || filter.toDate) {
      const time = product.publicationDate ? Date.parse(product.publicationDate) : Number.NaN;
      if (!Number.isFinite(time)) return false;
      if (filter.direction === 'within' && ((Number.isFinite(lower) && time < lower) || (Number.isFinite(upper) && time > upper))) return false;
      if (filter.direction === 'before' && Number.isFinite(lower) && time >= lower) return false;
      if (filter.direction === 'after' && Number.isFinite(upper) && time <= upper) return false;
    }
    return true;
  });
}

export function USGSFilterControl({ language, products, selectedIds, importingIds, value, onChange }: Props) {
  const zh = language === 'zh';
  const resolutions = [...new Set(products.map(item => item.resolution).filter(Boolean))].sort((a, b) => a.localeCompare(b));
  const patch = (next: Partial<USGSFilterState>): void => onChange({ ...value, ...next });
  return <fieldset className="usgs-filter">
    <legend>{zh ? `筛选查询结果 · 已选 ${selectedIds.size} · 导入中 ${importingIds.size}` : `Filter query results · ${selectedIds.size} selected · ${importingIds.size} importing`}</legend>
    <label><span>{zh ? '搜索产品名称、编号或分辨率' : 'Search title, ID, or resolution'}</span>
      <input value={value.search} onChange={event => patch({ search: event.target.value })} /></label>
    <div className="usgs-filter-resolutions"><span>{zh ? '数据分辨率' : 'Resolution'}</span>
      {resolutions.length ? resolutions.map(resolution => <label key={resolution}>
        <input type="checkbox" checked={value.resolutions.includes(resolution)} onChange={event => patch({
          resolutions: event.target.checked ? [...value.resolutions, resolution] : value.resolutions.filter(item => item !== resolution),
        })} />{resolution}</label>) : <small>{zh ? '查询结果中没有分辨率信息' : 'No resolution values in the query results'}</small>}
    </div>
    <label><span>{zh ? '日期条件' : 'Date condition'}</span><select value={value.direction} onChange={event => patch({ direction: event.target.value as USGSFilterState['direction'] })}>
      <option value="within">{zh ? '日期范围内' : 'Within range'}</option><option value="before">{zh ? '早于起始日期' : 'Before start date'}</option><option value="after">{zh ? '晚于结束日期' : 'After end date'}</option>
    </select></label>
    <label><span>{zh ? '起始日期' : 'From'}</span><input type="date" value={value.fromDate} onChange={event => patch({ fromDate: event.target.value })} /></label>
    <label><span>{zh ? '结束日期' : 'To'}</span><input type="date" value={value.toDate} onChange={event => patch({ toDate: event.target.value })} /></label>
    <label><span>{zh ? '列表范围' : 'Selection criteria'}</span><select value={value.criteria} onChange={event => patch({ criteria: event.target.value as USGSSelectionCriteria })}>
      <option value="All">{zh ? '全部产品' : 'All tiles'}</option><option value="Selected">{zh ? '已选产品' : 'Selected tiles'}</option><option value="Importing">{zh ? '正在导入' : 'Tiles to import'}</option>
    </select></label>
  </fieldset>;
}
