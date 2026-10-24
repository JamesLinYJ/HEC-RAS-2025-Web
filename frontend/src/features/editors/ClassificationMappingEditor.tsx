import type { EditorPageProps, ModelRow } from './model';
import { ModelDataTable } from './ModelDataTable';
import { validateClassificationMappingRows } from './classificationMappingValidation';

/** DataGrid-backed classification map editor hosted by RasterClassificationInputProperties. */
export function ClassificationMappingEditor({ language, rows, sourceRows, tableID, canEdit, onCellChange }: {
  language: EditorPageProps['language'];
  rows: ModelRow[];
  sourceRows: ModelRow[];
  tableID: string;
  canEdit: boolean;
  onCellChange: EditorPageProps['onRowCellChange'];
}) {
  const displayColumns = [
    { id: 'Key', title: language === 'zh' ? '栅格值' : 'Raster value', sourceType: 'int', readOnly: true },
    { id: 'Value', title: language === 'zh' ? '分类' : 'Classification', sourceType: 'string' },
  ];
  const validation = validateClassificationMappingRows(sourceRows, rows);
  const validationMessage = validation ? ({
    'duplicate-source-identity': language === 'zh' ? '来源映射行编号重复，已停用编辑以保护行身份。' : 'The source mapping contains duplicate row identities. Editing is disabled to preserve row identity.',
    'invalid-source-key': language === 'zh' ? '来源映射键不是有效的 32 位整数，已停用编辑。' : 'A source mapping key is not a valid 32-bit integer. Editing is disabled.',
    'duplicate-source-key': language === 'zh' ? '来源映射包含重复的栅格值，已停用编辑。' : 'The source mapping contains duplicate raster values. Editing is disabled.',
    'row-set-changed': language === 'zh' ? '映射行集合与来源不一致。行不能在此处新增、删除或替换。' : 'The mapping rows no longer match the source. Rows cannot be added, removed, or replaced here.',
    'row-identity-changed': language === 'zh' ? '映射键已改变。栅格值与行编号由来源固定，不能编辑。' : 'A mapping key changed. Raster values and row identities are fixed by the source and cannot be edited.',
    'invalid-row-shape': language === 'zh' ? '映射行字段不完整，已停用编辑。' : 'A mapping row has an invalid field set. Editing is disabled.',
  })[validation] : '';

  return (
    <section className="editor-table-card" aria-label={language === 'zh' ? '分类值映射' : 'Classification mapping'}>
      <div className="editor-terrain-table-heading">
        <h3>{language === 'zh' ? '分类值映射' : 'Classification mapping'}</h3>
        <span>{rows.length} {language === 'zh' ? '项' : 'entries'}</span>
      </div>
      <p className="editor-field-muted">{language === 'zh' ? '栅格值及行身份由来源固定；可编辑分类名称。行结构不支持在此处增删。' : 'Raster values and row identities are fixed by the source. Classification names are editable; mapping rows cannot be added or removed here.'}</p>
      {validationMessage && <p className="editor-config-notice" role="alert">{validationMessage}</p>}
      <ModelDataTable tableID={tableID} rows={rows} columns={displayColumns} canEdit={canEdit && validation == null} onCellChange={onCellChange} language={language} />
    </section>
  );
}
