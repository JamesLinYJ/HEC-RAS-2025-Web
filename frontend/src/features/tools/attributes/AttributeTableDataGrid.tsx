import { useEffect, useMemo, useState, type ReactNode } from 'react';
import type { Language } from '../../../shared/workbench';
import { formatAttributeCell, isSupportedAttributeCellType } from './attributeTableTypes';
import type { AttributeCellEdit, AttributeTableColumn, AttributeTableData } from './attributeTableTypes';
import { DataGrid } from '../../../controls/DataGrid';

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
  const selectedColumn = table.columns.find(column => column.id === selectedColumnId);
  const dataGridColumns = useMemo(() => table.columns.map(column => ({
    id: column.id,
    title: column.name,
    sourceType: column.type,
    readOnly: column.readOnly,
  })), [table.columns]);

  useEffect(() => { setDrafts({}); setSelectedColumnId(undefined); }, [table]);

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
      if (value !== null && booleanValue !== 'true' && booleanValue !== 'false') {
        return <input aria-label={`${column.name} ${rowId}`} disabled={busy} type="text" value={value}
          onChange={event => setDraftValue(rowId, column, event.currentTarget.value)} />;
      }
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
    {table.columns.length === 0 ? <p className="attribute-empty">{zh ? '属性表没有字段。' : 'The attribute table has no columns.'}</p> :
      <DataGrid
        language={language}
        rows={table.rows}
        columns={dataGridColumns}
        getRowId={row => row.id}
        getCellValue={(row, column) => row.values[column.id] ?? null}
        getClipboardCellValue={(row, column) => {
          const key = cellKey(row.id, column.id);
          return Object.prototype.hasOwnProperty.call(drafts, key) ? drafts[key]!.value : row.values[column.id] ?? null;
        }}
        canPasteCell={(_row, column) => !busy && column.readOnly !== true && isSupportedAttributeCellType(column.sourceType ?? '')}
        onCellPaste={(row, column, value) => {
          if (busy || column.readOnly) return;
          const attributeColumn = table.columns.find(item => item.id === column.id);
          if (attributeColumn && isSupportedAttributeCellType(attributeColumn.type)) setDraftValue(row.id, attributeColumn, value);
        }}
        renderCell={(row, column) => {
          const attributeColumn = table.columns.find(item => item.id === column.id)!;
          return cellEditor(row.id, attributeColumn, row.values[attributeColumn.id] ?? null);
        }}
        cellClassName={(row, column) => Object.prototype.hasOwnProperty.call(drafts, cellKey(row.id, column.id)) ? 'has-draft' : undefined}
        rowHeaderLabel={zh ? '记录' : 'Record'}
        renderRowHeader={(row, sourceIndex) => <span title={row.id}>{(sourceIndex + 1).toLocaleString(language === 'zh' ? 'zh-CN' : 'en-US')}<small>{row.id}</small></span>}
        renderColumnHeader={(column, direction, toggleSort) => {
          const attributeColumn = table.columns.find(item => item.id === column.id)!;
          return <button type="button" className="attribute-column-heading" onClick={() => { setSelectedColumnId(column.id); toggleSort(); }} aria-pressed={column.id === selectedColumnId}>
            <span>{attributeColumn.name}</span><small>{attributeColumn.type}{attributeColumn.readOnly ? ` · ${zh ? '只读' : 'read-only'}` : ''}</small>
            <span className="ras-data-grid-sort-indicator" aria-hidden="true">{direction === 'ascending' ? '↑' : direction === 'descending' ? '↓' : '↕'}</span>
          </button>;
        }}
        tableClassName="attribute-table-grid"
        tableWrapperClassName="attribute-table-scroll"
        pageSize={PAGE_SIZE}
        footerClassName="attribute-grid-footer"
        resetKey={table}
        renderFooter={({ totalRows, visibleRows, pageIndex, pageCount, setPageIndex }) => <>
          <span>{totalRows.toLocaleString(language === 'zh' ? 'zh-CN' : 'en-US')} {zh ? '条记录' : 'records'} · {table.columns.length} {zh ? '个字段' : 'fields'}
            {visibleRows !== totalRows ? ` · ${zh ? `筛选后 ${visibleRows} 条` : `${visibleRows} after filtering`}` : ''}</span>
          <div><button type="button" disabled={pageIndex <= 0} onClick={() => setPageIndex(pageIndex - 1)}>{zh ? '上一页' : 'Previous'}</button>
            <span>{pageIndex + 1} / {pageCount}</span><button type="button" disabled={pageIndex + 1 >= pageCount} onClick={() => setPageIndex(pageIndex + 1)}>{zh ? '下一页' : 'Next'}</button></div>
        </>}
      />
    }
  </section>;
}
