import type { EditorPageProps, EditorSchema, ModelSnapshot } from './model';
import { SchemaFieldRenderer } from './SchemaFieldRenderer';

export interface TerrainInputEditorProps extends EditorPageProps {
  selected: NonNullable<ModelSnapshot['terrain']>['inputs'][number];
  schema: EditorSchema;
  fieldKeyPrefix: string;
}

/** React port of Ras.Visual.WinUI.Controls.RasterTerrainInputProperties. */
export function RasterTerrainInputProperties({ language, snapshot, draft, onFieldChange, onImagesChange, selected, schema, fieldKeyPrefix }: TerrainInputEditorProps) {
  return (
    <section className="editor-field-sections" aria-label={language === 'zh' ? '栅格属性' : 'Raster properties'}>
      <h4>{language === 'zh' ? '栅格属性' : 'Raster properties'}</h4>
      <SchemaFieldRenderer schema={schema} fieldKeyPrefix={fieldKeyPrefix} snapshot={snapshot} values={draft.fields} images={draft.images} canEdit={snapshot.canEdit} onFieldChange={onFieldChange} onImagesChange={onImagesChange} language={language} />
      {selected.overviewCount != null && <small className="editor-field-muted">{language === 'zh' ? `已读取 ${selected.overviewCount} 个金字塔层级。` : `${selected.overviewCount} raster pyramid levels were read.`}</small>}
    </section>
  );
}
