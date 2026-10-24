import type { TerrainInputEditorProps } from './RasterTerrainInputProperties';
import { SchemaFieldRenderer } from './SchemaFieldRenderer';
import { ChannelModParentEditor } from './ChannelModParentEditor';
import { ChannelModEditor } from './ChannelModEditor';
import { ChannelModTopologyEditor } from './ChannelModTopologyEditor';

export function ChannelModTerrainInputProperties(props: TerrainInputEditorProps & { selectedInputIsLoaded: boolean }) {
  const { language, snapshot, draft, onFieldChange, onImagesChange, selected, schema, fieldKeyPrefix, selectedInputIsLoaded } = props;
  const parentSchema = props.schemas.find((candidate) => candidate.id === 'ChannelModParentEditor');
  const channelSchema = props.schemas.find((candidate) => candidate.id === 'ChannelModEditor');
  const terrainInputs = snapshot.terrain?.inputs ?? [];
  const extentSource = [selected, ...terrainInputs.filter((input) => input.id !== selected.id && input.schema === 'RasterTerrainInputProperties'),
    ...terrainInputs.filter((input) => input.id !== selected.id && input.schema !== 'RasterTerrainInputProperties')]
    .find((input) => isValidExtent(input.validExtent));
  const referenceExtent = extentSource?.validExtent ?? null;
  return (
    <section className="editor-field-sections" aria-label={language === 'zh' ? '渠道修改输入属性' : 'Channel modification input properties'}>
      <h4>{language === 'zh' ? '渠道修改输入属性' : 'Channel modification input properties'}</h4>
      <SchemaFieldRenderer schema={schema} fieldKeyPrefix={fieldKeyPrefix} snapshot={snapshot} values={draft.fields} images={draft.images} canEdit={snapshot.canEdit} onFieldChange={onFieldChange} onImagesChange={onImagesChange} language={language} />
      {selectedInputIsLoaded && parentSchema && <ChannelModParentEditor {...props} schema={parentSchema} fieldKeyPrefix={`${selected.scope}/${parentSchema.id}`} hasImported />}
      {selectedInputIsLoaded && channelSchema && <ChannelModEditor {...props} schema={channelSchema} />}
      {selectedInputIsLoaded && <ChannelModTopologyEditor
        language={language}
        inputId={selected.id}
        sourceRevision={snapshot.revision}
        referenceExtent={referenceExtent}
        referenceExtentSource={extentSource?.name ?? null}
        canEdit={props.canEditTerrainTopology === true}
        onRun={props.onTerrainTopology}
      />}
    </section>
  );
}

function isValidExtent(value: { minX: number; maxX: number; minY: number; maxY: number } | null | undefined): value is { minX: number; maxX: number; minY: number; maxY: number } {
  return !!value && Number.isFinite(value.minX) && Number.isFinite(value.maxX)
    && Number.isFinite(value.minY) && Number.isFinite(value.maxY)
    && value.minX <= value.maxX && value.minY <= value.maxY;
}
