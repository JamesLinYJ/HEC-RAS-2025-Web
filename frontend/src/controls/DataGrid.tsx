import { useEffect, useMemo, useRef, useState, type ClipboardEvent, type FocusEvent, type KeyboardEvent, type PointerEvent, type ReactNode } from 'react';
import type { Language } from '../shared/workbench';
import { GenericEnumFilterControl, type GenericEnumFilterOption } from './GenericEnumFilterControl';
import { clipboardMatrixFits, parseClipboardMatrix, serializeClipboardMatrix } from './dataGridClipboard';
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
  getClipboardCellValue?: (row: Row, column: DataGridColumn) => string | null | undefined;
  renderCell: (row: Row, column: DataGridColumn, sourceIndex: number) => ReactNode;
  canPasteCell?: (row: Row, column: DataGridColumn) => boolean;
  onCellPaste?: (row: Row, column: DataGridColumn, value: string) => void;
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

interface CellIdentity {
  rowID: string;
  columnID: string;
}

interface CellPosition {
  identity: CellIdentity;
  rowIndex: number;
  columnIndex: number;
}

interface CellBounds {
  minRow: number;
  maxRow: number;
  minColumn: number;
  maxColumn: number;
}

interface ResizeSession {
  columnID: string;
  startX: number;
  startWidth: number;
}

const cellIdentityKey = (rowID: string, columnID: string): string => JSON.stringify([rowID, columnID]);

function parseCellIdentityKey(value: string): CellIdentity | null {
  try {
    const parsed: unknown = JSON.parse(value);
    if (Array.isArray(parsed) && typeof parsed[0] === 'string' && typeof parsed[1] === 'string') {
      return { rowID: parsed[0], columnID: parsed[1] };
    }
  } catch {
    // Ignore stale or malformed selection entries.
  }
  return null;
}

function boundsForCells(positions: readonly CellPosition[]): CellBounds | null {
  if (positions.length === 0) return null;
  return positions.reduce<CellBounds>((bounds, position) => ({
    minRow: Math.min(bounds.minRow, position.rowIndex),
    maxRow: Math.max(bounds.maxRow, position.rowIndex),
    minColumn: Math.min(bounds.minColumn, position.columnIndex),
    maxColumn: Math.max(bounds.maxColumn, position.columnIndex),
  }), { minRow: Infinity, maxRow: -Infinity, minColumn: Infinity, maxColumn: -Infinity });
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
  language, rows, columns, getRowId, getCellValue, getClipboardCellValue, renderCell, canPasteCell, onCellPaste, rowHeaderLabel, renderRowHeader,
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
  const [activeCell, setActiveCell] = useState<CellIdentity | null>(null);
  const [selectionAnchor, setSelectionAnchor] = useState<CellIdentity | null>(null);
  const [selectedCellKeys, setSelectedCellKeys] = useState<string[]>([]);
  const selectedCellKeySet = useMemo(() => new Set(selectedCellKeys), [selectedCellKeys]);
  const [selectionMessage, setSelectionMessage] = useState('');
  const [columnWidths, setColumnWidths] = useState<Record<string, number>>({});
  const resizeSession = useRef<ResizeSession | null>(null);
  const copiedGridText = useRef<string | null>(null);
  const gridRoot = useRef<HTMLDivElement | null>(null);
  const pointerSelection = useRef(false);
  const keyboardFocus = useRef(false);

  useEffect(() => {
    setSearch('');
    setFilterColumnId(initialFilterColumnId);
    setSelectedDiscreteValues(null);
    setSort(null);
    setPageIndex(0);
    setActiveCell(null);
    setSelectionAnchor(null);
    setSelectedCellKeys([]);
    setSelectionMessage('');
    setColumnWidths({});
  }, [initialFilterColumnId, resetKey]);

  useEffect(() => {
    const handlePointerMove = (event: globalThis.PointerEvent): void => {
      const session = resizeSession.current;
      if (!session) return;
      const nextWidth = Math.max(72, Math.min(1200, session.startWidth + event.clientX - session.startX));
      setColumnWidths(current => ({ ...current, [session.columnID]: nextWidth }));
    };
    const stopResize = (): void => {
      resizeSession.current = null;
      requestAnimationFrame(() => { pointerSelection.current = false; });
    };
    window.addEventListener('pointermove', handlePointerMove);
    window.addEventListener('pointerup', stopResize);
    window.addEventListener('pointercancel', stopResize);
    return () => {
      window.removeEventListener('pointermove', handlePointerMove);
      window.removeEventListener('pointerup', stopResize);
      window.removeEventListener('pointercancel', stopResize);
    };
  }, []);

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
  const pageStart = pageSize ? currentPage * pageSize : 0;
  const rowIndexByID = useMemo(() => new Map<string, number>(filteredRows.map((item, index): [string, number] => [getRowId(item.row), index])), [filteredRows, getRowId]);
  const columnIndexByID = useMemo(() => new Map<string, number>(columns.map((column, index): [string, number] => [column.id, index])), [columns]);

  const positionFor = (identity: CellIdentity): CellPosition | null => {
    const rowIndex = rowIndexByID.get(identity.rowID);
    const columnIndex = columnIndexByID.get(identity.columnID);
    return rowIndex === undefined || columnIndex === undefined ? null : { identity, rowIndex, columnIndex };
  };

  const selectedPositions = (): CellPosition[] => selectedCellKeys
    .map(parseCellIdentityKey)
    .filter((identity): identity is CellIdentity => identity !== null)
    .map(positionFor)
    .filter((position): position is CellPosition => position !== null);

  const activePosition = activeCell ? positionFor(activeCell) : null;
  const activeCellIsOnPage = activePosition !== null && activePosition.rowIndex >= pageStart && activePosition.rowIndex < pageStart + pageRows.length;
  const tabStopCell = activeCellIsOnPage
    ? activeCell
    : pageRows[0] && columns[0] ? { rowID: getRowId(pageRows[0].row), columnID: columns[0].id } : null;

  const selectRectangle = (anchor: CellIdentity, focus: CellIdentity): string[] => {
    const start = positionFor(anchor);
    const end = positionFor(focus);
    if (!start || !end) return [cellIdentityKey(focus.rowID, focus.columnID)];
    const minRow = Math.min(start.rowIndex, end.rowIndex);
    const maxRow = Math.max(start.rowIndex, end.rowIndex);
    const minColumn = Math.min(start.columnIndex, end.columnIndex);
    const maxColumn = Math.max(start.columnIndex, end.columnIndex);
    const keys: string[] = [];
    for (let rowIndex = minRow; rowIndex <= maxRow; rowIndex += 1) {
      for (let columnIndex = minColumn; columnIndex <= maxColumn; columnIndex += 1) {
        keys.push(cellIdentityKey(getRowId(filteredRows[rowIndex]!.row), columns[columnIndex]!.id));
      }
    }
    return keys;
  };

  const selectCell = (identity: CellIdentity, modifiers: { shift?: boolean; toggle?: boolean } = {}): void => {
    setActiveCell(identity);
    if (modifiers.shift && selectionAnchor) {
      const nextKeys = selectRectangle(selectionAnchor, identity);
      setSelectedCellKeys(nextKeys);
      setSelectionMessage(language === 'zh' ? `已选择 ${nextKeys.length} 个单元格。` : `${nextKeys.length} cells selected.`);
      return;
    }
    if (modifiers.toggle) {
      const key = cellIdentityKey(identity.rowID, identity.columnID);
      const next = selectedCellKeys.includes(key)
        ? selectedCellKeys.filter(item => item !== key)
        : [...selectedCellKeys, key];
      setSelectedCellKeys(next);
      setSelectionMessage(language === 'zh' ? `已选择 ${next.length} 个单元格。` : `${next.length} cells selected.`);
      setSelectionAnchor(identity);
      return;
    }
    setSelectionAnchor(identity);
    setSelectedCellKeys([cellIdentityKey(identity.rowID, identity.columnID)]);
    setSelectionMessage(language === 'zh' ? '已选择 1 个单元格。' : '1 cell selected.');
  };

  const copySelectionText = (): { text: string; cellCount: number } | null => {
    const positions = selectedPositions();
    if (positions.length === 0 && activeCell) {
      const active = positionFor(activeCell);
      if (active) positions.push(active);
    }
    const bounds = boundsForCells(positions);
    if (!bounds) return null;
    const selected = new Set(positions.map(position => cellIdentityKey(position.identity.rowID, position.identity.columnID)));
    const lines: string[] = [];
    for (let rowIndex = bounds.minRow; rowIndex <= bounds.maxRow; rowIndex += 1) {
      const values: string[] = [];
      for (let columnIndex = bounds.minColumn; columnIndex <= bounds.maxColumn; columnIndex += 1) {
        const row = filteredRows[rowIndex]!.row;
        const column = columns[columnIndex]!;
        const key = cellIdentityKey(getRowId(row), column.id);
        const raw = getClipboardCellValue ? getClipboardCellValue(row, column) : getCellValue(row, column);
        values.push(selected.has(key) ? (raw ?? '') : '');
      }
      lines.push(serializeClipboardMatrix([values]));
    }
    return { text: lines.join('\r\n'), cellCount: positions.length };
  };

  const applyClipboardText = (text: string): void => {
    if (!onCellPaste) return;
    const matrix = parseClipboardMatrix(text);
    if (!matrix) {
      setSelectionMessage(language === 'zh' ? '粘贴内容的引号格式无效，未应用任何单元格。' : 'The clipboard contains malformed quoted TSV; no cells were changed.');
      return;
    }
    if (matrix.length === 0 || matrix.every(line => line.length === 0)) return;
    const positions = selectedPositions();
    const active = activeCell ? positionFor(activeCell) : null;
    if (positions.length === 0 && !active) {
      setSelectionMessage(language === 'zh' ? '请先选择粘贴起始单元格。' : 'Select a destination cell before pasting.');
      return;
    }
    const bounds = boundsForCells(positions);
    const origin = bounds
      ? { rowIndex: bounds.minRow, columnIndex: bounds.minColumn }
      : { rowIndex: active!.rowIndex, columnIndex: active!.columnIndex };
    const fillSelection = matrix.length === 1 && matrix[0]!.length === 1 && positions.length > 1;
    if (!fillSelection && !clipboardMatrixFits(matrix, origin.rowIndex, origin.columnIndex, filteredRows.length, columns.length)) {
      setSelectionMessage(language === 'zh' ? '粘贴区域超出当前筛选表格范围，未应用任何单元格。' : 'The paste range extends beyond the filtered grid; no cells were changed.');
      return;
    }
    const accepted: CellIdentity[] = [];
    let skipped = 0;
    if (fillSelection) {
      const value = matrix[0]![0]!;
      for (const position of positions) {
        const row = filteredRows[position.rowIndex]!.row;
        const column = columns[position.columnIndex]!;
        if (column.readOnly || canPasteCell?.(row, column) === false) { skipped += 1; continue; }
        onCellPaste(row, column, value);
        accepted.push(position.identity);
      }
    } else {
      for (let rowOffset = 0; rowOffset < matrix.length; rowOffset += 1) {
        const row = filteredRows[origin.rowIndex + rowOffset]!.row;
        for (let columnOffset = 0; columnOffset < matrix[rowOffset]!.length; columnOffset += 1) {
          const column = columns[origin.columnIndex + columnOffset]!;
          const value = matrix[rowOffset]![columnOffset]!;
          if (column.readOnly || canPasteCell?.(row, column) === false) { skipped += 1; continue; }
          onCellPaste(row, column, value);
          accepted.push({ rowID: getRowId(row), columnID: column.id });
        }
      }
    }
    if (accepted.length > 0) {
      const keys = accepted.map(identity => cellIdentityKey(identity.rowID, identity.columnID));
      setSelectedCellKeys(keys);
      setSelectionAnchor(accepted[0]!);
      setActiveCell(accepted[accepted.length - 1]!);
    }
    const summary = language === 'zh'
      ? `已粘贴 ${accepted.length} 个单元格${skipped ? `；跳过 ${skipped} 个只读或当前视图外的单元格` : ''}。`
      : `Pasted ${accepted.length} cells${skipped ? `; skipped ${skipped} read-only or out-of-view cells` : ''}.`;
    setSelectionMessage(summary);
  };

  const handleCopy = (event: ClipboardEvent<HTMLDivElement>): void => {
    const target = event.target;
    if (!(target instanceof Element) || !target.closest('td[data-grid-row-id]')) return;
    copiedGridText.current = null;
    const input = target.closest('input, textarea') as HTMLInputElement | HTMLTextAreaElement | null;
    if (input && input.selectionStart !== input.selectionEnd) return;
    const selection = copySelectionText();
    if (!selection) return;
    event.preventDefault();
    event.clipboardData.setData('text/plain', selection.text);
    copiedGridText.current = selection.text;
    setSelectionMessage(language === 'zh' ? `已复制 ${selection.cellCount} 个单元格。` : `Copied ${selection.cellCount} cells.`);
  };

  const handlePaste = (event: ClipboardEvent<HTMLDivElement>): void => {
    const target = event.target;
    if (!onCellPaste || !(target instanceof Element) || !target.closest('td[data-grid-row-id]')) return;
    const text = event.clipboardData.getData('text/plain');
    const input = target.closest('input, textarea') as HTMLInputElement | HTMLTextAreaElement | null;
    const copiedFromGrid = copiedGridText.current === text;
    copiedGridText.current = null;
    const gridRangePaste = /[\t\r\n]/.test(text) || text.startsWith('"') || selectedPositions().length > 1 || copiedFromGrid;
    if (input && !input.readOnly && !input.disabled && !gridRangePaste) return;
    event.preventDefault();
    applyClipboardText(text);
  };

  const handleCellFocus = (event: FocusEvent<HTMLDivElement>): void => {
    const target = event.target;
    if (pointerSelection.current || keyboardFocus.current || !(target instanceof Element)) return;
    const cell = target.closest('td[data-grid-row-id][data-grid-column-id]');
    if (!cell) return;
    const rowID = cell.getAttribute('data-grid-row-id');
    const columnID = cell.getAttribute('data-grid-column-id');
    if (rowID && columnID) selectCell({ rowID, columnID });
  };

  const handleGridKeyDown = (event: KeyboardEvent<HTMLDivElement>): void => {
    const commandKey = event.ctrlKey || event.metaKey;
    const target = event.target instanceof Element ? event.target : null;
    const cell = target?.closest<HTMLTableCellElement>('td[data-grid-row-id][data-grid-column-id]') ?? null;
    const targetIsCell = Boolean(cell);
    const targetIdentity = cell?.dataset.gridRowId && cell.dataset.gridColumnId
      ? { rowID: cell.dataset.gridRowId, columnID: cell.dataset.gridColumnId }
      : activeCell;
    const arrow = ['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'].includes(event.key);
    const focusIsEditor = Boolean(target?.closest('input, select, textarea, button'));
    const commandRange = commandKey && event.shiftKey && targetIsCell;
    const cellNavigation = targetIsCell && !focusIsEditor && !commandKey && arrow;
    if ((commandRange || cellNavigation) && targetIdentity && arrow) {
      const from = positionFor(targetIdentity);
      if (!from) return;
      const direction = event.key === 'ArrowUp' ? [-1, 0] : event.key === 'ArrowDown' ? [1, 0] : event.key === 'ArrowLeft' ? [0, -1] : [0, 1];
      const nextRow = Math.max(0, Math.min(filteredRows.length - 1, from.rowIndex + direction[0]!));
      const nextColumn = Math.max(0, Math.min(columns.length - 1, from.columnIndex + direction[1]!));
      const next = { rowID: getRowId(filteredRows[nextRow]!.row), columnID: columns[nextColumn]!.id };
      event.preventDefault();
      const extending = commandRange || event.shiftKey;
      const anchor = extending ? selectionAnchor ?? targetIdentity : next;
      const keys = extending ? selectRectangle(anchor, next) : [cellIdentityKey(next.rowID, next.columnID)];
      setSelectionAnchor(anchor);
      setSelectedCellKeys(keys);
      setActiveCell(next);
      setSelectionMessage(language === 'zh' ? `已选择 ${keys.length} 个单元格。` : `${keys.length} cells selected.`);
      if (pageSize) setPageIndex(Math.floor(nextRow / pageSize));
      keyboardFocus.current = true;
      requestAnimationFrame(() => {
        const target = Array.from(gridRoot.current?.querySelectorAll<HTMLElement>('td[data-grid-row-id][data-grid-column-id]') ?? [])
          .find(cell => cell.dataset.gridRowId === next.rowID && cell.dataset.gridColumnId === next.columnID);
        if (cellNavigation || !commandRange && !event.shiftKey) {
          target?.focus();
        } else {
          (target?.querySelector<HTMLElement>('input, select, textarea, button') ?? target)?.focus();
        }
        requestAnimationFrame(() => { keyboardFocus.current = false; });
      });
      return;
    }
    if (targetIsCell && cell && !focusIsEditor && (event.key === 'Enter' || event.key === 'F2')) {
      const input = cell.querySelector<HTMLElement>('input, select, textarea, button');
      if (input) { event.preventDefault(); input.focus(); }
      return;
    }
    if (event.key === 'Escape' && (event.target === gridRoot.current || (event.target instanceof Element && !event.target.closest('input, select, textarea')))) {
      setActiveCell(null);
      setSelectionAnchor(null);
      setSelectedCellKeys([]);
      setSelectionMessage(language === 'zh' ? '已清除单元格选择。' : 'Cell selection cleared.');
    }
  };

  const beginColumnResize = (event: PointerEvent<HTMLButtonElement>, column: DataGridColumn): void => {
    event.preventDefault();
    event.stopPropagation();
    const header = event.currentTarget.closest('th');
    resizeSession.current = {
      columnID: column.id,
      startX: event.clientX,
      startWidth: columnWidths[column.id] ?? column.width ?? header?.getBoundingClientRect().width ?? 160,
    };
  };

  const resetColumnWidth = (columnID: string): void => setColumnWidths(current => {
    const next = { ...current };
    delete next[columnID];
    return next;
  });

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

  return <div className="ras-data-grid" ref={gridRoot} role="region"
    aria-label={language === 'zh' ? '数据表格；点击单元格选择，Shift 点击或 Ctrl 加 Shift 加方向键扩展；Ctrl 或 Command 加 C 复制，Ctrl 或 Command 加 V 粘贴。' : 'Data table. Click a cell to select; Shift-click or Control-Shift-arrow extends the selection. Control or Command-C copies; Control or Command-V pastes.'}
    onFocusCapture={handleCellFocus} onKeyDown={handleGridKeyDown} onCopy={handleCopy} onPaste={handlePaste}>
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
      {selectionMessage ? <span className="ras-data-grid-selection-status" role="status" aria-live="polite">{selectionMessage}</span> : null}
    </div>
    <div className={tableWrapperClassName}>
    <table className={tableClassName} role="grid" aria-multiselectable="true">
      <thead><tr><th scope="col" aria-label={rowHeaderLabel}>{rowHeaderContent ?? rowHeaderLabel}</th>
        {columns.map(column => {
          const direction = sort?.columnId === column.id ? sort.direction : null;
          const header = renderColumnHeader
            ? renderColumnHeader(column, direction, toggleSort(column))
            : <button type="button" className="ras-data-grid-sort-button" onClick={toggleSort(column)}
              aria-label={`${column.title}, ${zh ? '按此列排序' : 'sort by this column'}`}>
              <span>{column.title}</span><span aria-hidden="true">{direction === 'ascending' ? '↑' : direction === 'descending' ? '↓' : '↕'}</span>
            </button>;
          const width = columnWidths[column.id] ?? column.width;
          return <th key={column.id} scope="col" aria-sort={direction ?? 'none'} style={width ? { width, minWidth: width } : undefined}>
            <div className="ras-data-grid-column-heading">
              <div className="ras-data-grid-column-content">{header}</div>
              <button type="button" className="ras-data-grid-column-resize" aria-label={language === 'zh' ? `调整 ${column.title} 列宽` : `Resize ${column.title} column`}
                title={language === 'zh' ? '拖动调整列宽，双击恢复' : 'Drag to resize; double-click to reset'}
                onPointerDown={event => beginColumnResize(event, column)} onClick={event => event.stopPropagation()}
                onDoubleClick={event => { event.preventDefault(); event.stopPropagation(); resetColumnWidth(column.id); }}
                onKeyDown={event => {
                  if (!event.shiftKey || !['ArrowLeft', 'ArrowRight'].includes(event.key)) return;
                  event.preventDefault();
                  event.stopPropagation();
                  const current = columnWidths[column.id] ?? column.width ?? event.currentTarget.closest('th')?.getBoundingClientRect().width ?? 160;
                  setColumnWidths(previous => ({ ...previous, [column.id]: Math.max(72, Math.min(1200, current + (event.key === 'ArrowRight' ? 16 : -16))) }));
                }} />
            </div>
          </th>;
        })}
      </tr></thead>
      <tbody>{pageRows.map(({ row, sourceIndex }) => {
        const attributes = rowAttributes?.(row, sourceIndex) ?? {};
        return <tr key={getRowId(row)} data-row-id={getRowId(row)} data-testid={rowTestId?.(row, sourceIndex)} {...attributes}>
          <th scope="row">{renderRowHeader ? renderRowHeader(row, sourceIndex) : sourceIndex + 1}</th>
          {columns.map(column => {
            const identity = { rowID: getRowId(row), columnID: column.id };
            const selected = selectedCellKeySet.has(cellIdentityKey(identity.rowID, identity.columnID));
            const isTabStop = tabStopCell?.rowID === identity.rowID && tabStopCell.columnID === identity.columnID;
            return <td key={column.id} className={cellClassName?.(row, column, sourceIndex)} role="gridcell" aria-selected={selected}
              data-grid-row-id={identity.rowID} data-grid-column-id={identity.columnID} tabIndex={isTabStop ? 0 : -1}
              onPointerDown={() => { pointerSelection.current = true; }}
              onClick={event => {
                selectCell(identity, { shift: event.shiftKey, toggle: event.ctrlKey || event.metaKey });
                pointerSelection.current = false;
              }}>{renderCell(row, column, sourceIndex)}</td>;
          })}
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
