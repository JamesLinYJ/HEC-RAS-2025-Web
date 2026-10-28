import type { ChangeEvent } from 'react';
import type { EditorPageProps, ModelRow, ModelTable, TerrainTransformCommand } from './model';
import { ModelDataTable } from './ModelDataTable';

/** Selection-linked table behavior from Ras.Visual.WinUI.Controls.LeveeModsTable. */
export function LeveeModsTable({ tableID, title, table, rows, language, canEdit, onCellChange, onTransform, selectionMode = 'none' }: {
  tableID: string;
  title: string;
  table: ModelTable;
  rows: ModelRow[];
  language: 'zh' | 'en';
  canEdit: boolean;
  onCellChange: EditorPageProps['onRowCellChange'];
  onTransform?: (command: TerrainTransformCommand) => Promise<boolean>;
  selectionMode?: 'none' | 'single' | 'multiple';
}) {
  const selectedIDs = table.selectedRowIDs ?? table.requestedSelectionRowIDs ?? [];
  const selected = new Set(selectedIDs);
  const enabled = canEdit && !!onTransform;
  const setSelection = (rowID: string, checked: boolean) => {
    let next: string[];
    if (selectionMode === 'single') next = checked ? [rowID] : [];
    else next = checked ? [...selectedIDs, rowID] : selectedIDs.filter((id) => id !== rowID);
    void onTransform?.({ operation: 'selectRows', tableId: tableID, rowIDs: next });
  };
  const changeSelection = (event: ChangeEvent<HTMLInputElement>, rowID: string) => {
    if (selectionMode === 'single') setSelection(rowID, true);
    else setSelection(rowID, event.currentTarget.checked);
  };

  return <div className="editor-table-card">
    <div className="editor-terrain-table-heading">
      <h5>{title}</h5>
      <span>{rows.length} {language === 'zh' ? '行' : 'rows'}</span>
    </div>
    {selectionMode !== 'none' && <fieldset className="editor-terrain-row-selection" disabled={!enabled}>
      <legend>{language === 'zh' ? '选择要素' : 'Select features'}</legend>
      {rows.map((row, index) => <label key={row.id} className="editor-toggle editor-terrain-row-option">
        <input
          type={selectionMode === 'single' ? 'radio' : 'checkbox'}
          name={selectionMode === 'single' ? `terrain-selection-${tableID}` : undefined}
          value={row.id}
          checked={selected.has(row.id)}
          onChange={(event) => changeSelection(event, row.id)}
        />
        <span>{row.values.Name || row.values.LeveePropertyName || `${language === 'zh' ? '第' : 'Row '}${index + 1}${language === 'zh' ? '行' : ''}`}</span>
      </label>)}
      {rows.length === 0 && <small className="editor-field-muted">{language === 'zh' ? '没有可选要素。' : 'There are no selectable rows.'}</small>}
      {selectedIDs.length > 0 && <button type="button" className="editor-button" onClick={() => void onTransform?.({ operation: 'selectRows', tableId: tableID, rowIDs: [] })}>{language === 'zh' ? '清除选择' : 'Clear selection'}</button>}
    </fieldset>}
    <ModelDataTable tableID={tableID} rows={rows} columns={table.columns} canEdit={canEdit} onCellChange={onCellChange} language={language} />
    {(table.canAddRows || table.canCopyRows || table.canDeleteRows) && <div className="editor-button-row">
      {table.canAddRows && <button type="button" className="editor-button" disabled={!enabled} onClick={() => void onTransform?.({ operation: 'addRow', tableId: tableID })}>
        {language === 'zh' ? '新增一行' : 'Add row'}
      </button>}
      {table.canCopyRows && <button type="button" className="editor-button" disabled={!enabled || rows.length === 0} onClick={() => void onTransform?.({ operation: 'copyRows', tableId: tableID, rowIDs: rows.map((row) => row.id) })}>
        {language === 'zh' ? '复制全部采样行' : 'Copy all sampled rows'}
      </button>}
      {table.canDeleteRows && <button type="button" className="editor-button" disabled={!enabled || selectedIDs.length === 0} onClick={() => void onTransform?.({ operation: 'removeRows', tableId: tableID, rowIDs: selectedIDs })}>
        {language === 'zh' ? `删除所选行 (${selectedIDs.length})` : `Delete selected rows (${selectedIDs.length})`}
      </button>}
    </div>}
  </div>;
}
