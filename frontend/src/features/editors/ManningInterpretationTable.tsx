import type { DataGridColumn, DataGridSortDirection } from '../../controls/DataGrid';
import { DataGrid } from '../../controls/DataGrid';
import type { EditorPageProps, ModelRow } from './model';
import { assessManningRows, isFiniteManningFloat } from './manningEditorUi';

export function ManningInterpretationTable(props: {
  language: EditorPageProps['language'];
  rows: ModelRow[];
  projectionRows: ModelRow[];
  canEdit: boolean;
  busy: boolean;
  onCellChange: EditorPageProps['onRowCellChange'];
}) {
  const { language, rows, projectionRows, canEdit, busy, onCellChange } = props;
  const zh = language === 'zh';
  const columns: DataGridColumn[] = [
    { id: 'Key', title: zh ? '分类' : 'Classification', sourceType: 'string', readOnly: true },
    { id: 'Value', title: zh ? '糙率 n' : "Manning's n", sourceType: 'float' },
  ];
  const assessment = assessManningRows(rows, projectionRows);

  if (rows.length === 0) {
    return <>
      {!assessment.identityIsSafe && <p className="editor-config-notice" role="alert" data-testid="manning-interpretation-identity-warning">
        {zh ? '解释表的行身份与当前服务端投影不一致。为避免把值写到错误分类，编辑和糙率操作已停用；请丢弃此草稿并重新读取模型。' : 'The interpretation row identities do not match the current Host projection. Editing and Manning operations are disabled; discard this draft and reload the model.'}
      </p>}
      <div className="editor-empty-state" data-testid="manning-interpretation-empty">{zh ? '当前分类没有糙率解释行。' : 'The current classification has no Manning interpretation rows.'}</div>
    </>;
  }

  return <>
    {!assessment.identityIsSafe && <p className="editor-config-notice" role="alert" data-testid="manning-interpretation-identity-warning">
      {zh ? '解释表的行身份与当前服务端投影不一致。为避免把值写到错误分类，编辑和糙率操作已停用；请丢弃此草稿并重新读取模型。' : 'The interpretation row identities do not match the current Host projection. Editing and Manning operations are disabled; discard this draft and reload the model.'}
    </p>}
    {assessment.identityIsSafe && assessment.invalidValueRowIDs.length > 0 && <p className="editor-config-notice" role="alert" data-testid="manning-interpretation-value-warning">
      {zh ? `${assessment.invalidValueRowIDs.length} 行的糙率值不是可用的有限数值。修正后才能刷新或套用默认值。` : `${assessment.invalidValueRowIDs.length} Manning values are not finite numeric values. Correct them before refreshing or applying defaults.`}
    </p>}
    <DataGrid<ModelRow>
      language={language}
      rows={rows}
      columns={columns}
      getRowId={(row) => row.id}
      getCellValue={(row, column) => row.values[column.id] ?? ''}
      rowHeaderLabel={zh ? '行编号' : 'Row ID'}
      rowHeaderContent="#"
      renderRowHeader={(_row, index) => index + 1}
      renderCell={(row, column, sourceIndex) => {
        const value = row.values[column.id] ?? '';
        const readOnly = column.id === 'Key' || !canEdit || busy || !assessment.identityIsSafe;
        const invalid = column.id === 'Value' && !isFiniteManningFloat(value);
        return <input
          type="text"
          inputMode={column.id === 'Value' ? 'decimal' : undefined}
          value={value}
          readOnly={readOnly}
          aria-label={`${column.id === 'Key' ? (zh ? '分类' : 'Classification') : (zh ? '糙率 n' : "Manning's n")}, ${zh ? `第${sourceIndex + 1}行` : `row ${sourceIndex + 1}`}`}
          aria-invalid={invalid || undefined}
          data-testid={column.id === 'Value' ? `manning-interpretation-value-${encodeURIComponent(row.id)}` : undefined}
          onChange={(event) => onCellChange('ClassificationLayerManningNEditor/InterpretationTable', row.id, column.id, event.currentTarget.value)}
        />;
      }}
      renderColumnHeader={(column, direction: DataGridSortDirection, toggleSort) => <button type="button" className="ras-data-grid-sort-button" onClick={toggleSort}
        aria-label={`${column.title}, ${zh ? '按此列排序' : 'sort by this column'}`}>
        <span>{column.title}</span><span aria-hidden="true">{direction === 'ascending' ? '↑' : direction === 'descending' ? '↓' : '↕'}</span>
      </button>}
      rowAttributes={(row) => ({ 'data-table-id': 'ClassificationLayerManningNEditor/InterpretationTable', 'data-row-id': row.id })}
      rowTestId={(row) => `manning-interpretation-row-${encodeURIComponent(row.id)}`}
      tableClassName="editor-data-table"
      tableWrapperClassName="editor-table-scroll"
      resetKey={projectionRows.map((row) => row.id).join('\u001f')}
    />
  </>;
}
