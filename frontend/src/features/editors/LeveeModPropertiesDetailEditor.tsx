import type { EditorPageProps, EditorSchema } from './model';
import { SchemaFieldRenderer } from './SchemaFieldRenderer';

const propertyTableID = 'LeveeModPropertiesEditor/PropertySets';

export function LeveeModPropertiesDetailEditor({ language, snapshot, draft, onFieldChange, onImagesChange, onTerrainTransform, schema }: EditorPageProps & { selected: NonNullable<NonNullable<EditorPageProps['snapshot']['terrain']>['inputs']>[number]; schema?: EditorSchema }) {
  const propertyTable = snapshot.terrain?.tables?.[propertyTableID];
  const selectedRows = propertyTable?.selectedRowIDs ?? propertyTable?.requestedSelectionRowIDs ?? [];
  const selectedPropertyScope = selectedRows.length ? `TerrainSelection/${[...selectedRows].sort().join('+')}/LeveeModPropertiesDetailEditor` : '';
  const hasSelectedFields = !!schema && selectedRows.length > 0 && schema.fields.some((field) => {
    const key = `${selectedPropertyScope}/${field.id}`;
    return Object.hasOwn(snapshot.fields, key) || Object.hasOwn(draft.fields, key);
  });
  const modeField = `${selectedPropertyScope}/Mode`;
  const modTypeField = `${selectedPropertyScope}/ModType`;
  const selectedMode = draft.fields[modeField] ?? '';
  const selectedModType = draft.fields[modTypeField] ?? '';
  const canForceSet = snapshot.canEdit && !!onTerrainTransform && selectedRows.length > 0;
  return (
    <div className="editor-levee-property-detail">
      {hasSelectedFields && schema && <SchemaFieldRenderer schema={schema} fieldKeyPrefix={selectedPropertyScope} snapshot={snapshot} values={draft.fields} images={draft.images} canEdit={snapshot.canEdit} onFieldChange={onFieldChange} onImagesChange={onImagesChange} language={language} />}
      {selectedRows.length > 0 && <div className="editor-button-row">
        <button type="button" className="editor-button" disabled={!canForceSet || !selectedMode} onClick={() => void onTerrainTransform?.({ operation: 'forceSetLeveeProperty', leveeForceSet: { command: 'forceSetMode', value: selectedMode, rowIDs: selectedRows } })}>
          {language === 'zh' ? '将模式应用到所选属性集' : 'Apply mode to selected sets'}
        </button>
        <button type="button" className="editor-button" disabled={!canForceSet || !selectedModType} onClick={() => void onTerrainTransform?.({ operation: 'forceSetLeveeProperty', leveeForceSet: { command: 'forceSetModType', value: selectedModType, rowIDs: selectedRows } })}>
          {language === 'zh' ? '将修改类型应用到所选属性集' : 'Apply modification type to selected sets'}
        </button>
      </div>}
      {propertyTable && selectedRows.length === 0 && <small className="editor-field-muted">{language === 'zh' ? '详细参数仅对服务端返回的已选属性集显示。' : 'Detailed values are shown only for property sets selected in the returned snapshot.'}</small>}
    </div>
  );
}
