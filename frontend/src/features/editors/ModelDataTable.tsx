import type { ModelRow } from './model';
import { editorText } from './editorLocalization';

export interface DataColumn {
  id: string;
  title: string;
  sourceType?: string;
  readOnly?: boolean;
  width?: number | null;
  autoSuggestions?: string[];
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
  if (rows.length === 0) return <div className="editor-empty-state">{language === 'zh' ? '该表没有行。' : 'This table has no rows.'}</div>;
  return (
    <div className="editor-table-scroll">
      <table className="editor-data-table">
        <thead><tr><th aria-label={language === 'zh' ? '行编号' : 'Row ID'}>#</th>{displayColumns.map((column) => <th key={column.id}>{editorText(language, column.title)}</th>)}</tr></thead>
        <tbody>{rows.map((row, index) => <tr key={row.id} data-testid={`model-table-row-${encodeURIComponent(tableID)}-${encodeURIComponent(row.id)}`} data-table-id={tableID} data-row-id={row.id}>
          <th scope="row">{index + 1}</th>
          {displayColumns.map((column) => {
            const value = row.values[column.id] ?? '';
            const readOnly = !canEdit || column.readOnly === true;
            const numeric = column.sourceType === 'double' || column.sourceType === 'float' || column.sourceType === 'int';
            const listID = column.autoSuggestions?.length ? `${tableID}-${column.id}-suggestions` : undefined;
            return <td key={column.id}>
              <input
                type="text"
                inputMode={numeric ? 'decimal' : undefined}
                value={value}
                readOnly={readOnly}
                aria-label={`${editorText(language, column.title)}, ${language === 'zh' ? '第' : 'row '}${index + 1}${language === 'zh' ? '行' : ''}`}
                list={listID}
                onChange={(event) => onCellChange(tableID, row.id, column.id, event.currentTarget.value)}
              />
              {column.autoSuggestions?.length ? <datalist id={listID}>{column.autoSuggestions.map((suggestion) => <option key={suggestion} value={suggestion} />)}</datalist> : null}
            </td>;
          })}
        </tr>)}</tbody>
      </table>
    </div>
  );
}
