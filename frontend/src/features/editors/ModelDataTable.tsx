import { useMemo } from 'react';
import type { ModelRow } from './model';
import { editorText } from './editorLocalization';
import { DataGrid, type DataGridColumn } from '../../controls/DataGrid';

export interface DataColumn extends DataGridColumn {
  readOnly?: boolean;
}

export function ModelDataTable({ tableID, rows, columns, canEdit, onCellChange, language }: {
  tableID: string;
  rows: ModelRow[];
  columns: DataColumn[];
  canEdit: boolean;
  onCellChange: (tableID: string, rowID: string, columnID: string, value: string) => void;
  language: 'zh' | 'en';
}) {
  const displayColumns: DataColumn[] = columns.length ? columns : Object.keys(rows[0]?.values ?? {}).map((id) => ({ id, title: id, sourceType: 'string', readOnly: id === 'Key' || id === 'ID' }));
  const localizedColumns = useMemo(() => displayColumns.map(column => ({ ...column, title: editorText(language, column.title) })), [displayColumns, language]);
  if (rows.length === 0) return <div className="editor-empty-state">{language === 'zh' ? '该表没有行。' : 'This table has no rows.'}</div>;
  return <DataGrid
        language={language}
        rows={rows}
        columns={localizedColumns}
        getRowId={row => row.id}
        getCellValue={(row, column) => row.values[column.id] ?? ''}
        canPasteCell={(_row, column) => canEdit && column.readOnly !== true}
        onCellPaste={(row, column, value) => {
          if (canEdit && column.readOnly !== true) onCellChange(tableID, row.id, column.id, value);
        }}
        rowHeaderLabel={language === 'zh' ? '行编号' : 'Row ID'}
        rowHeaderContent="#"
        renderCell={(row, column, sourceIndex) => {
          const value = row.values[column.id] ?? '';
          const readOnly = !canEdit || column.readOnly === true;
          const numeric = column.sourceType === 'double' || column.sourceType === 'float' || column.sourceType === 'int';
          const listID = column.autoSuggestions?.length ? `${tableID}-${column.id}-suggestions` : undefined;
          return <>
            <input
              type="text"
              inputMode={numeric ? 'decimal' : undefined}
              value={value}
              readOnly={readOnly}
              aria-label={`${column.title}, ${language === 'zh' ? '第' : 'row '}${sourceIndex + 1}${language === 'zh' ? '行' : ''}`}
              list={listID}
              onChange={(event) => onCellChange(tableID, row.id, column.id, event.currentTarget.value)}
            />
            {column.autoSuggestions?.length ? <datalist id={listID}>{column.autoSuggestions.map((suggestion) => <option key={suggestion} value={suggestion} />)}</datalist> : null}
          </>;
        }}
        renderColumnHeader={(column, direction, toggleSort) => <button type="button" className="ras-data-grid-sort-button" onClick={toggleSort}
          aria-label={`${column.title}, ${language === 'zh' ? '按此列排序' : 'sort by this column'}`}>
          <span>{column.title}</span><span aria-hidden="true">{direction === 'ascending' ? '↑' : direction === 'descending' ? '↓' : '↕'}</span>
        </button>}
        renderRowHeader={(_row, sourceIndex) => sourceIndex + 1}
        rowAttributes={row => ({ 'data-table-id': tableID, 'data-row-id': row.id })}
        rowTestId={row => `model-table-row-${encodeURIComponent(tableID)}-${encodeURIComponent(row.id)}`}
        tableClassName="editor-data-table"
        tableWrapperClassName="editor-table-scroll"
      />;
}
