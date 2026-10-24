import type { TerrainInputEditorProps } from './RasterTerrainInputProperties';
import { SchemaFieldRenderer } from './SchemaFieldRenderer';

/** React port of Ras.Visual.WinUI.Controls.FunctionalTerrainInputProperties. */
export function FunctionalTerrainInputProperties({ language, snapshot, draft, onFieldChange, onImagesChange, schema, fieldKeyPrefix }: TerrainInputEditorProps) {
  return (
    <section className="editor-field-sections" aria-label={language === 'zh' ? '函数地形属性' : 'Functional terrain properties'}>
      <h4>{language === 'zh' ? '函数地形属性' : 'Functional terrain properties'}</h4>
      <SchemaFieldRenderer schema={schema} fieldKeyPrefix={fieldKeyPrefix} snapshot={snapshot} values={draft.fields} images={draft.images} canEdit={snapshot.canEdit} onFieldChange={onFieldChange} onImagesChange={onImagesChange} language={language} />
      <small className="editor-field-muted">{language === 'zh' ? '表达式和有效范围按原控件字段提交；导入范围提示由地形导入操作提供。' : 'The expression and valid extent use the source editor fields; the import-extent hint belongs to the terrain import operation.'}</small>
    </section>
  );
}
