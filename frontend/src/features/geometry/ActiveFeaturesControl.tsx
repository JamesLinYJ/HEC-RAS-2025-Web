import { useMemo, useState, type ReactNode } from 'react';
import type { Language } from '../../shared/workbench';
import { gkind, gt } from './geometryText';
import type { GeometryRow } from './geometryTypes';

export interface ActiveFeaturesSelectionState {
  filter: string;
  setFilter: (value: string) => void;
  visibleRows: GeometryRow[];
  selectedIDs: string[];
  selectedRows: GeometryRow[];
  toggleRow: (id: string) => void;
  toggleVisible: () => void;
  updateSelection: (ids: string[]) => void;
}

export interface ActiveFeaturesControlProps {
  language: Language;
  kind: string;
  rows: GeometryRow[];
  selectedRowIDs?: string[];
  onSelectionChange?: (ids: string[]) => void;
  actions?: (selectedRows: GeometryRow[], selection: ActiveFeaturesSelectionState) => ReactNode;
  children: (selection: ActiveFeaturesSelectionState) => ReactNode;
}

/** Source ActiveFeaturesControl behavior: filter the active rows and keep list selection bidirectional. */
export function ActiveFeaturesControl({ language, kind, rows, selectedRowIDs, onSelectionChange, actions, children }: ActiveFeaturesControlProps) {
  const [filter, setFilter] = useState('');
  const [internalSelectedIDs, setInternalSelectedIDs] = useState<string[]>([]);
  const selectedIDs = selectedRowIDs ?? internalSelectedIDs;
  const visibleRows = useMemo(() => {
    const needle = filter.trim().toLocaleLowerCase();
    if (!needle) return rows;
    return rows.filter((row) => [row.id, String(row.fid ?? ''), ...Object.values(row.values ?? {})]
      .some((value) => String(value).toLocaleLowerCase().includes(needle)));
  }, [filter, rows]);
  const visibleIDs = useMemo(() => new Set(visibleRows.map((row) => row.id)), [visibleRows]);
  const selectedRows = rows.filter((row) => selectedIDs.includes(row.id));

  const updateSelection = (ids: string[]): void => {
    if (selectedRowIDs === undefined) setInternalSelectedIDs(ids);
    onSelectionChange?.(ids);
  };
  const toggleRow = (id: string): void => {
    updateSelection(selectedIDs.includes(id) ? selectedIDs.filter((current) => current !== id) : [...selectedIDs, id]);
  };
  const toggleVisible = (): void => {
    const allVisibleSelected = visibleRows.length > 0 && visibleRows.every((row) => selectedIDs.includes(row.id));
    updateSelection(allVisibleSelected
      ? selectedIDs.filter((id) => !visibleIDs.has(id))
      : [...new Set([...selectedIDs, ...visibleRows.map((row) => row.id)])]);
  };
  const selection: ActiveFeaturesSelectionState = { filter, setFilter, visibleRows, selectedIDs, selectedRows, toggleRow, toggleVisible, updateSelection };

  return <section className="geometry-panel" aria-label={`${gkind(language, kind)} ${gt(language, 'table')}`}>
    <header className="geometry-panel-heading">
      <div><h2>{gkind(language, kind)} · {gt(language, 'table')}</h2>
        <p>{rows.length} {gt(language, 'rows')}{visibleRows.length !== rows.length ? ` · ${visibleRows.length} ${language === 'zh' ? '条匹配' : 'matching'}` : ''}
          {selectedRows.length ? ` · ${selectedRows.length} ${gt(language, 'selected')}` : ''}</p>
      </div>
      <div className="geometry-toolbar-group">
        <input className="geometry-filter" value={filter} onChange={(event) => setFilter(event.target.value)}
          placeholder={gt(language, 'filter')} aria-label={gt(language, 'filter')} />
        {filter && <button className="geometry-secondary" type="button" onClick={() => setFilter('')}>{language === 'zh' ? '清除筛选' : 'Clear filters'}</button>}
        {actions?.(selectedRows, selection)}
      </div>
    </header>
    {children(selection)}
  </section>;
}

export default ActiveFeaturesControl;
