import { useEffect, useMemo, useState, type ReactNode } from 'react';
import type { Language } from '../shared/workbench';
import { GenericEnumFilterControl, type GenericEnumFilterOption } from './GenericEnumFilterControl';
import './dataGrid.css';

export interface DataGridColumn {
  id: string;
  title: string;
  sourceType?: string;
  readOnly?: boolean;
  width?: number | null;
  autoSuggestions?: string[];
}

export type DataGridSortDirection = 'ascending' | 'descending' | null;

export interface DataGridFooterContext {
  totalRows: number;
  visibleRows: number;
  pageIndex: number;
  pageCount: number;
  pageSize: number;
  setPageIndex: (page: number) => void;
}

interface Props<Row> {
  language: Language;
  rows: readonly Row[];
  columns: DataGridColumn[];
  getRowId: (row: Row) => string;
  getCellValue: (row: Row, column: DataGridColumn) => string | null | undefined;
  renderCell: (row: Row, column: DataGridColumn, sourceIndex: number) => ReactNode;
  rowHeaderLabel: string;
  rowHeaderContent?: ReactNode;
  renderRowHeader?: (row: Row, sourceIndex: number) => ReactNode;
  renderColumnHeader?: (column: DataGridColumn, sortDirection: DataGridSortDirection, toggleSort: () => void) => ReactNode;
  cellClassName?: (row: Row, column: DataGridColumn, sourceIndex: number) => string | undefined;
  rowAttributes?: (row: Row, sourceIndex: number) => Record<string, string | undefined>;
  rowTestId?: (row: Row, sourceIndex: number) => string | undefined;
  tableClassName: string;
  tableWrapperClassName?: string;
  pageSize?: number;
  footerClassName?: string;
  renderFooter?: (context: DataGridFooterContext) => ReactNode;
  resetKey?: unknown;
}

interface IndexedRow<Row> {
  row: Row;
  sourceIndex: number;
}

interface DiscreteValues {
  options: GenericEnumFilterOption[];
  tokenSet: Set<string>;
  overLimit: boolean;
}

const DISCRETE_FILTER_LIMIT = 80;

function discreteToken(value: string | null | undefined): string {
  return JSON.stringify(value ?? null);
}

function compareCellValues(left: string | null | undefined, right: string | null | undefined, sourceType?: string): number {
  const leftEmpty = left == null || left === '';
  const rightEmpty = right == null || right === '';
  if (leftEmpty || rightEmpty) return leftEmpty === rightEmpty ? 0 : leftEmpty ? -1 : 1;

  const normalizedType = sourceType?.toLocaleLowerCase();
  if (normalizedType === 'int' || normalizedType === 'int32' || normalizedType === 'double' || normalizedType === 'float' || normalizedType === 'single' || normalizedType === 'number') {
    const leftNumber = Number(left);
    const rightNumber = Number(right);
    if (Number.isFinite(leftNumber) && Number.isFinite(rightNumber)) return leftNumber - rightNumber;
  }
  return String(left).localeCompare(String(right), undefined, { numeric: true, sensitivity: 'base' });
}

function localizedValueLabel(value: string | null | undefined, language: Language): string {
  if (value == null) return language === 'zh' ? '空值' : 'Null';
  if (value === '') return language === 'zh' ? '空字符串' : 'Empty string';
  return value;
}

/** Shared table view. Filtering and sorting only reorder the view; row and cell callbacks keep source identities. */
export function DataGrid<Row>({
  language, rows, columns, getRowId, getCellValue, renderCell, rowHeaderLabel, renderRowHeader,
  rowHeaderContent, renderColumnHeader, cellClassName, rowAttributes, rowTestId, tableClassName, tableWrapperClassName, pageSize, footerClassName,
  renderFooter, resetKey,
}: Props<Row>) {
  const zh = language === 'zh';
  const initialFilterColumnId = columns[0]?.id ?? '';
  const [search, setSearch] = useState('');
  const [filterColumnId, setFilterColumnId] = useState(initialFilterColumnId);
  const [selectedDiscreteValues, setSelectedDiscreteValues] = useState<readonly string[] | null>(null);
  const [sort, setSort] = useState<{ columnId: string; direction: Exclude<DataGridSortDirection, null> } | null>(null);
  const [pageIndex, setPageIndex] = useState(0);

  useEffect(() => {
    setSearch('');
    setFilterColumnId(initialFilterColumnId);
    setSelectedDiscreteValues(null);
    setSort(null);
    setPageIndex(0);
  }, [initialFilterColumnId, resetKey]);

  const selectedFilterColumn = columns.find(column => column.id === filterColumnId) ?? columns[0];
  const discreteValues = useMemo<DiscreteValues>(() => {
    if (!selectedFilterColumn) return { options: [], tokenSet: new Set(), overLimit: false };

    const tokens = new Set<string>();
    const values = new Map<string, string | null>();
    if (selectedFilterColumn.autoSuggestions?.length) {
      for (const value of selectedFilterColumn.autoSuggestions) {
        const token = discreteToken(value);
        if (tokens.has(token)) continue;
        tokens.add(token);
        values.set(token, value);
        if (tokens.size > DISCRETE_FILTER_LIMIT) break;
      }
    } else {
      for (const row of rows) {
        const value = getCellValue(row, selectedFilterColumn) ?? null;
        const token = discreteToken(value);
        if (tokens.has(token)) continue;
        tokens.add(token);
        values.set(token, value);
        if (tokens.size > DISCRETE_FILTER_LIMIT) break;
      }
    }

    const overLimit = tokens.size > DISCRETE_FILTER_LIMIT;
    const options = overLimit ? [] : [...values.entries()].map(([value, label]) => ({
      value,
      label: localizedValueLabel(label, language),
    }));
    return { options, tokenSet: new Set(values.keys()), overLimit };
  }, [getCellValue, language, rows, selectedFilterColumn]);

  const activeDiscreteSelection = selectedDiscreteValues === null || discreteValues.overLimit
    ? null
    : selectedDiscreteValues.filter(value => discreteValues.tokenSet.has(value));

  const filteredRows = useMemo<IndexedRow<Row>[]>(() => {
    const query = search.trim().toLocaleLowerCase(zh ? 'zh-CN' : 'en-US');
    const selected = activeDiscreteSelection === null ? null : new Set(activeDiscreteSelection);
    const filtered: IndexedRow<Row>[] = [];

    rows.forEach((row, sourceIndex) => {
      if (query && !columns.some(column => (getCellValue(row, column) ?? '').toLocaleLowerCase(zh ? 'zh-CN' : 'en-US').includes(query))) return;
      if (selected && selectedFilterColumn && !selected.has(discreteToken(getCellValue(row, selectedFilterColumn)))) return;
      filtered.push({ row, sourceIndex });
    });

    if (sort) {
      const column = columns.find(candidate => candidate.id === sort.columnId);
      if (column) {
        const direction = sort.direction === 'ascending' ? 1 : -1;
        filtered.sort((left, right) => {
          const compared = compareCellValues(getCellValue(left.row, column), getCellValue(right.row, column), column.sourceType);
          return compared === 0 ? left.sourceIndex - right.sourceIndex : compared * direction;
        });
      }
    }
    return filtered;
  }, [activeDiscreteSelection, columns, getCellValue, rows, search, selectedFilterColumn, sort, zh]);

  const totalPages = pageSize ? Math.max(1, Math.ceil(filteredRows.length / pageSize)) : 1;
  const currentPage = Math.min(pageIndex, totalPages - 1);
  useEffect(() => {
    if (pageIndex !== currentPage) setPageIndex(currentPage);
  }, [currentPage, pageIndex]);

  const pageRows = pageSize
    ? filteredRows.slice(currentPage * pageSize, (currentPage + 1) * pageSize)
    : filteredRows;

  const toggleSort = (column: DataGridColumn): (() => void) => () => {
    setPageIndex(0);
    setSort(current => current?.columnId !== column.id
      ? { columnId: column.id, direction: 'ascending' }
      : current.direction === 'ascending' ? { columnId: column.id, direction: 'descending' } : null);
  };

  const footerContext: DataGridFooterContext = {
    totalRows: rows.length,
    visibleRows: filteredRows.length,
    pageIndex: currentPage,
    pageCount: totalPages,
    pageSize: pageSize ?? filteredRows.length,
    setPageIndex: page => setPageIndex(Math.max(0, Math.min(totalPages - 1, page))),
  };

  return <div className="ras-data-grid">
    <div className="ras-data-grid-toolbar">
      <label className="ras-data-grid-search">
        <span>{zh ? '搜索表格' : 'Search table'}</span>
        <input type="search" value={search} onChange={event => { setSearch(event.currentTarget.value); setPageIndex(0); }}
          aria-label={zh ? '搜索表格' : 'Search table'} />
      </label>
      <label className="ras-data-grid-filter-column">
        <span>{zh ? '离散值字段' : 'Discrete-value column'}</span>
        <select value={selectedFilterColumn?.id ?? ''} disabled={columns.length === 0}
          aria-label={zh ? '选择离散值过滤字段' : 'Choose discrete-value filter column'}
          onChange={event => { setFilterColumnId(event.currentTarget.value); setSelectedDiscreteValues(null); setPageIndex(0); }}>
          {columns.map(column => <option key={column.id} value={column.id}>{column.title}</option>)}
        </select>
      </label>
      {selectedFilterColumn && !discreteValues.overLimit && discreteValues.options.length > 0
        ? <GenericEnumFilterControl language={language} label={zh ? '筛选离散值' : 'Filter discrete values'}
          options={discreteValues.options} selectedValues={activeDiscreteSelection}
          onSelectionChange={values => { setSelectedDiscreteValues(values); setPageIndex(0); }} />
        : selectedFilterColumn && discreteValues.overLimit
          ? <span className="ras-data-grid-filter-hint" role="status">{zh ? `值种类超过 ${DISCRETE_FILTER_LIMIT} 项，请使用文本搜索。` : `More than ${DISCRETE_FILTER_LIMIT} distinct values; use text search.`}</span>
          : null}
      <span className="ras-data-grid-count" role="status">{zh
        ? `显示 ${filteredRows.length.toLocaleString('zh-CN')} / ${rows.length.toLocaleString('zh-CN')} 行`
        : `Showing ${filteredRows.length.toLocaleString('en-US')} / ${rows.length.toLocaleString('en-US')} rows`}</span>
    </div>
    <div className={tableWrapperClassName}>
    <table className={tableClassName}>
      <thead><tr><th scope="col" aria-label={rowHeaderLabel}>{rowHeaderContent ?? rowHeaderLabel}</th>
        {columns.map(column => {
          const direction = sort?.columnId === column.id ? sort.direction : null;
          const header = renderColumnHeader
            ? renderColumnHeader(column, direction, toggleSort(column))
            : <button type="button" className="ras-data-grid-sort-button" onClick={toggleSort(column)}
              aria-label={`${column.title}, ${zh ? '按此列排序' : 'sort by this column'}`}>
              <span>{column.title}</span><span aria-hidden="true">{direction === 'ascending' ? '↑' : direction === 'descending' ? '↓' : '↕'}</span>
            </button>;
          return <th key={column.id} scope="col" aria-sort={direction ?? 'none'} style={column.width ? { width: column.width } : undefined}>{header}</th>;
        })}
      </tr></thead>
      <tbody>{pageRows.map(({ row, sourceIndex }) => {
        const attributes = rowAttributes?.(row, sourceIndex) ?? {};
        return <tr key={getRowId(row)} data-row-id={getRowId(row)} data-testid={rowTestId?.(row, sourceIndex)} {...attributes}>
          <th scope="row">{renderRowHeader ? renderRowHeader(row, sourceIndex) : sourceIndex + 1}</th>
          {columns.map(column => <td key={column.id} className={cellClassName?.(row, column, sourceIndex)}>{renderCell(row, column, sourceIndex)}</td>)}
        </tr>;
      })}</tbody>
    </table>
    </div>
    {renderFooter ? <footer className={footerClassName ?? 'ras-data-grid-footer'}>{renderFooter(footerContext)}</footer>
      : pageSize ? <footer className={footerClassName ?? 'ras-data-grid-footer'}>
        <button type="button" disabled={currentPage <= 0} onClick={() => setPageIndex(currentPage - 1)}>{zh ? '上一页' : 'Previous'}</button>
        <span>{currentPage + 1} / {totalPages}</span>
        <button type="button" disabled={currentPage + 1 >= totalPages} onClick={() => setPageIndex(currentPage + 1)}>{zh ? '下一页' : 'Next'}</button>
      </footer> : null}
  </div>;
}
