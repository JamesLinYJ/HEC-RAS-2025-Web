import type { EditorPageProps, EditorSchema } from './model';
import { SchemaFieldRenderer } from './SchemaFieldRenderer';
import { ModelDataTable } from './ModelDataTable';

const propertyTableID = 'LeveeModPropertiesEditor/PropertySets';

export function LeveeModPropertiesDetailEditor({ language, snapshot, draft, onFieldChange, onImagesChange, onRowCellChange, schema }: EditorPageProps & { selected: NonNullable<NonNullable<EditorPageProps['snapshot']['terrain']>['inputs']>[number]; schema?: EditorSchema }) {
  const propertyTable = snapshot.terrain?.tables?.[propertyTableID];
  const selectedRows = propertyTable?.selectedRowIDs ?? propertyTable?.requestedSelectionRowIDs ?? [];
  const selectedPropertyScope = selectedRows.length ? `TerrainSelection/${[...selectedRows].sort().join('+')}/LeveeModPropertiesDetailEditor` : '';
  const hasSelectedFields = !!schema && selectedRows.length > 0 && schema.fields.some((field) => {
    const key = `${selectedPropertyScope}/${field.id}`;
    return Object.hasOwn(snapshot.fields, key) || Object.hasOwn(draft.fields, key);
  });
  const propertyRows = draft.rows[propertyTableID] ?? snapshot.terrain?.rows?.[propertyTableID] ?? [];
  return (
    <section className="editor-terrain-card editor-levee-properties" aria-label={language === 'zh' ? '挖填属性集编辑器' : 'Cut/fill property set editor'}>
      <h4>{language === 'zh' ? '简单挖填属性集' : 'Simple cut/fill property sets'}</h4>
      {hasSelectedFields && schema && <SchemaFieldRenderer schema={schema} fieldKeyPrefix={selectedPropertyScope} snapshot={snapshot} values={draft.fields} images={draft.images} canEdit={snapshot.canEdit} onFieldChange={onFieldChange} onImagesChange={onImagesChange} language={language} />}
      {propertyTable && <div className="editor-table-card">
        <div className="editor-terrain-table-heading"><h5>{language === 'zh' ? '属性集' : 'Property sets'}</h5><span>{propertyRows.length} {language === 'zh' ? '行' : 'rows'}</span></div>
        <ModelDataTable tableID={propertyTableID} rows={propertyRows} columns={propertyTable.columns} canEdit={snapshot.canEdit} onCellChange={onRowCellChange} language={language} />
        {(propertyTable.canAddRows || propertyTable.canDeleteRows || propertyTable.canCopyRows) && <small className="editor-field-muted">{language === 'zh' ? '原控件支持属性集的新增、删除和选择；当前接口只回显属性集并提交已连接的字段草稿。' : 'The source editor supports adding, deleting, and selecting property sets; this view displays the returned sets and submits connected field drafts.'}</small>}
      </div>}
      {propertyTable && selectedRows.length === 0 && <small className="editor-field-muted">{language === 'zh' ? '详细参数仅对服务端返回的已选属性集显示。' : 'Detailed values are shown only for property sets selected in the returned snapshot.'}</small>}
    </section>
  );
}
