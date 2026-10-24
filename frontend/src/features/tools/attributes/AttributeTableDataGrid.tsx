import { useEffect, useMemo, useState, type ReactNode } from 'react';
import type { Language } from '../../../shared/workbench';
import { formatAttributeCell, isSupportedAttributeCellType } from './attributeTableTypes';
import type { AttributeCellEdit, AttributeTableColumn, AttributeTableData } from './attributeTableTypes';

interface Props {
  language: Language;
  table: AttributeTableData;
  busy: boolean;
  onAdd: () => void;
  onRename: (column: AttributeTableColumn) => void;
  onRemove: (column: AttributeTableColumn) => void;
  onSetCells: (cells: AttributeCellEdit[]) => Promise<void>;
}

const PAGE_SIZE = 100;
const cellKey = (rowId: string, columnId: string): string => JSON.stringify([rowId, columnId]);

export function AttributeTableDataGrid({ language, table, busy, onAdd, onRename, onRemove, onSetCells }: Props) {
  const zh = language === 'zh';
  const [selectedColumnId, setSelectedColumnId] = useState<string>();
  const [drafts, setDrafts] = useState<Record<string, AttributeCellEdit>>({});
  const [page, setPage] = useState(0);
  const selectedColumn = table.columns.find(column => column.id === selectedColumnId);
  const pageCount = Math.max(1, Math.ceil(table.rows.length / PAGE_SIZE));
  const visibleRows = useMemo(() => table.rows.slice(page * PAGE_SIZE, (page + 1) * PAGE_SIZE), [table.rows, page]);

  useEffect(() => { setDrafts({}); setPage(0); setSelectedColumnId(undefined); }, [table]);

  const setDraftValue = (rowId: string, column: AttributeTableColumn, value: string | null): void => {
    const row = table.rows.find(item => item.id === rowId);
    if (!row) return;
    const key = cellKey(rowId, column.id);
    setDrafts(current => {
      const next = { ...current };
      if ((row.values[column.id] ?? null) === value) delete next[key];
      else next[key] = { rowId, columnId: column.id, value };
      return next;
    });
  };

  const cellEditor = (rowId: string, column: AttributeTableColumn, stored: string | null): ReactNode => {
    const key = cellKey(rowId, column.id);
    const hasDraft = Object.prototype.hasOwnProperty.call(drafts, key);
    const value = hasDraft ? drafts[key]!.value : stored;
    const editable = !column.readOnly && isSupportedAttributeCellType(column.type);
    if (!editable) return <span className="attribute-cell-readonly" title={column.type}>{formatAttributeCell(stored)}</span>;
    if (column.type === 'Boolean') {
      const booleanValue = value?.toLocaleLowerCase();
      return <select aria-label={`${column.name} ${rowId}`} disabled={busy} value={booleanValue === 'true' || booleanValue === 'false' ? booleanValue : '__null'}
        onChange={event => setDraftValue(rowId, column, event.target.value === '__null' ? null : event.target.value)}>
        <option value="__null">{zh ? '空值' : 'Null'}</option><option value="true">true</option><option value="false">false</option>
      </select>;
    }
    const numeric = column.type === 'Int32' || column.type === 'Double' || column.type === 'Single';
    return <span className="attribute-cell-editor"><input aria-label={`${column.name} ${rowId}`} disabled={busy}
      type="text" inputMode={numeric ? 'decimal' : undefined} value={value ?? ''}
      onChange={event => setDraftValue(rowId, column, event.target.value)} />
      <button type="button" className="attribute-null-button" title={zh ? '设为空值' : 'Set to null'} aria-label={zh ? `将 ${column.name} 设为空值` : `Set ${column.name} to null`}
        disabled={busy || value === null} onClick={() => setDraftValue(rowId, column, null)}>∅</button></span>;
  };

  const applyDrafts = (): void => {
    const edits = Object.values(drafts);
    if (!edits.length || busy) return;
    void onSetCells(edits);
  };

  return <section className="attribute-grid-panel" aria-label={zh ? '属性表数据网格' : 'Attribute table data grid'}>
    <div className="attribute-grid-toolbar">
      <div className="attribute-grid-selection"><strong>{selectedColumn ? selectedColumn.name : (zh ? '未选择字段' : 'No field selected')}</strong>
        {selectedColumn && <small>{selectedColumn.type}{selectedColumn.readOnly ? ` · ${zh ? '只读' : 'read-only'}` : ''}</small>}</div>
      <div className="attribute-grid-actions">
        <button type="button" disabled={busy} onClick={onAdd}>{zh ? '添加字段' : 'Add field'}</button>
        <button type="button" disabled={busy || !selectedColumn || Boolean(selectedColumn.readOnly)} onClick={() => selectedColumn && onRename(selectedColumn)}>{zh ? '重命名字段' : 'Rename field'}</button>
        <button type="button" className="is-danger" disabled={busy || !selectedColumn || Boolean(selectedColumn.readOnly)} onClick={() => selectedColumn && onRemove(selectedColumn)}>{zh ? '移除字段' : 'Remove field'}</button>
        <button type="button" className="is-primary" disabled={busy || Object.keys(drafts).length === 0} onClick={applyDrafts}>{zh ? `应用 ${Object.keys(drafts).length} 个单元格到预览` : `Apply ${Object.keys(drafts).length} cell edits to preview`}</button>
      </div>
    </div>
    {table.columns.length === 0 ? <p className="attribute-empty">{zh ? '属性表没有字段。' : 'The attribute table has no columns.'}</p> : <div className="attribute-table-scroll">
      <table className="attribute-table-grid"><thead><tr><th className="attribute-row-number">{zh ? '记录' : 'Record'}</th>
        {table.columns.map(column => <th key={column.id} className={column.id === selectedColumnId ? 'is-selected' : undefined}>
          <button type="button" className="attribute-column-heading" onClick={() => setSelectedColumnId(column.id)} aria-pressed={column.id === selectedColumnId}>
            <span>{column.name}</span><small>{column.type}{column.readOnly ? ` · ${zh ? '只读' : 'read-only'}` : ''}</small>
          </button></th>)}</tr></thead><tbody>{visibleRows.map((row, index) => <tr key={row.id}>
          <th scope="row" title={row.id}>{(page * PAGE_SIZE + index + 1).toLocaleString(language === 'zh' ? 'zh-CN' : 'en-US')}<small>{row.id}</small></th>
          {table.columns.map(column => { const key = cellKey(row.id, column.id); const changed = Object.prototype.hasOwnProperty.call(drafts, key); return <td key={column.id} className={changed ? 'has-draft' : undefined}>{cellEditor(row.id, column, row.values[column.id] ?? null)}</td>; })}
        </tr>)}</tbody></table>
    </div>}
    <footer className="attribute-grid-footer"><span>{table.rows.length.toLocaleString(language === 'zh' ? 'zh-CN' : 'en-US')} {zh ? '条记录' : 'records'} · {table.columns.length} {zh ? '个字段' : 'fields'}</span>
      <div><button type="button" disabled={page <= 0} onClick={() => setPage(value => Math.max(0, value - 1))}>{zh ? '上一页' : 'Previous'}</button>
        <span>{page + 1} / {pageCount}</span><button type="button" disabled={page + 1 >= pageCount} onClick={() => setPage(value => Math.min(pageCount - 1, value + 1))}>{zh ? '下一页' : 'Next'}</button></div></footer>
  </section>;
}
