import type { EditorPageProps, EditorSchema } from './model';
import { SchemaFieldRenderer } from './SchemaFieldRenderer';
import { ChannelV6ImportControl } from './channelV6ImportControl';

export function ChannelModParentEditor({ language, snapshot, draft, onFieldChange, onImagesChange, schema, fieldKeyPrefix, hasImported, inputId, channelV6Import }: EditorPageProps & { schema: EditorSchema; fieldKeyPrefix: string; hasImported: boolean; inputId: string }) {
  const hasReturnedFields = schema.fields.some((field) => {
    const key = `${fieldKeyPrefix}/${field.id}`;
    return Object.hasOwn(snapshot.fields, key) || Object.hasOwn(draft.fields, key);
  });
  const visibleSchema = withoutSourceVisibilityGates(schema);
  return (
    <section className="editor-field-sections" aria-label={language === 'zh' ? '渠道修改全局设置' : 'Channel modification settings'}>
      <ChannelV6ImportControl language={language} workflow={channelV6Import} inputId={inputId} canImport={!hasImported} />
      {hasImported && hasReturnedFields && <>
        <h4>{language === 'zh' ? '渠道修改全局设置' : 'Channel modification settings'}</h4>
        <SchemaFieldRenderer schema={visibleSchema} fieldKeyPrefix={fieldKeyPrefix} snapshot={snapshot} values={draft.fields} images={draft.images} canEdit={snapshot.canEdit} onFieldChange={onFieldChange} onImagesChange={onImagesChange} language={language} />
      </>}
    </section>
  );
}

function withoutSourceVisibilityGates(schema: EditorSchema): EditorSchema {
  return { ...schema, fields: schema.fields.map((field) => ({ ...field, visibleBy: [] })) };
}
