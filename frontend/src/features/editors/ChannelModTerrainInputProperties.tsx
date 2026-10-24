import type { TerrainInputEditorProps } from './RasterTerrainInputProperties';
import { SchemaFieldRenderer } from './SchemaFieldRenderer';
import { ChannelModParentEditor } from './ChannelModParentEditor';
import { ChannelModEditor } from './ChannelModEditor';

export function ChannelModTerrainInputProperties(props: TerrainInputEditorProps & { selectedInputIsLoaded: boolean }) {
  const { language, snapshot, draft, onFieldChange, onImagesChange, selected, schema, fieldKeyPrefix, selectedInputIsLoaded } = props;
  const parentSchema = props.schemas.find((candidate) => candidate.id === 'ChannelModParentEditor');
  const channelSchema = props.schemas.find((candidate) => candidate.id === 'ChannelModEditor');
  return (
    <section className="editor-field-sections" aria-label={language === 'zh' ? '渠道修改输入属性' : 'Channel modification input properties'}>
      <h4>{language === 'zh' ? '渠道修改输入属性' : 'Channel modification input properties'}</h4>
      <SchemaFieldRenderer schema={schema} fieldKeyPrefix={fieldKeyPrefix} snapshot={snapshot} values={draft.fields} images={draft.images} canEdit={snapshot.canEdit} onFieldChange={onFieldChange} onImagesChange={onImagesChange} language={language} />
      {selectedInputIsLoaded && parentSchema && <ChannelModParentEditor {...props} schema={parentSchema} fieldKeyPrefix={`${selected.scope}/${parentSchema.id}`} hasImported />}
      {selectedInputIsLoaded && channelSchema && <ChannelModEditor {...props} schema={channelSchema} />}
    </section>
  );
}
