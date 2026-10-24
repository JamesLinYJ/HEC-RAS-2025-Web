import type { Language } from '../../shared/workbench';
import { gt } from './geometryText';
import type { GeometryRow } from './geometryTypes';

export interface DetailedCulvertBarrelHeaderProps {
  language: Language;
  rows: GeometryRow[];
}

export function DetailedCulvertBarrelHeader({ language, rows }: DetailedCulvertBarrelHeaderProps) {
  const names = [...new Set(rows.map((row) => row.values.Name ?? '').filter(Boolean))];
  const name = names.length === 1 ? names[0] : names.length > 1 ? (language === 'zh' ? '所选涵洞名称不同' : 'Selected culvert names differ') : '';
  const count = language === 'zh' ? `${rows.length} 项已选` : `${rows.length} selected`;

  return (
    <div className="geometry-panel-heading">
      <div><h3>{gt(language, 'barrel')}</h3><p>{rows.length ? (name || count) : (language === 'zh' ? '从涵洞表中选择一行或多行。' : 'Select one or more rows in the culvert table.')}</p></div>
      {rows.length > 0 && <span className="geometry-count-pill">{count}</span>}
    </div>
  );
}

export default DetailedCulvertBarrelHeader;
