import type { ReactNode } from 'react';
import type { EditorFieldSchema, EditorPageProps, SchemaChoice, SpatialPointData } from './model';
import { UnitAwareNumberField } from './UnitAwareNumberField';
import { validateFieldValue } from './SchemaFieldRenderer';
import { editorText } from './editorLocalization';
import { PointControl, SpatialInputs } from './PointControl';
import './meteorology.css';

export interface MeteorologyLayerDefinition {
  layerId: string;
  schemaId: string;
  title: [string, string];
  enabledField: string;
  typeField: string;
  ratioField?: string;
  constantField?: string;
  modes: Array<'Constant' | 'Point' | 'Gridded'>;
  constantUnit?: [string, string];
  ratioUnit?: [string, string];
}

export interface MeteorologyLayerControlProps extends EditorPageProps {
  definition: MeteorologyLayerDefinition;
  renderConstant?: (props: EditorPageProps) => ReactNode;
}

export function MeteorologyLayerControl({ definition, renderConstant, ...props }: MeteorologyLayerControlProps) {
  const { language, snapshot, draft } = props;
  const schema = props.schemas.find((item) => item.id === definition.schemaId);
  const boundary = draft.boundaryView ?? snapshot.boundary;
  const spatial = boundary?.spatial?.[definition.layerId];
  const enabledKey = `${definition.schemaId}/${definition.enabledField}`;
  const typeKey = `${definition.schemaId}/${definition.typeField}`;
  const enabledValue = draft.fields[enabledKey] ?? snapshot.fields[enabledKey] ?? 'false';
  const selectedType = draft.fields[typeKey] ?? snapshot.fields[typeKey] ?? 'Constant';
  const enabled = enabledValue === 'true' || enabledValue === 'True' || enabledValue === '1';
  const series = spatialSeries(props, definition.layerId, spatial);
  const canEdit = snapshot.canEdit && !snapshot.readOnlyFields?.includes(enabledKey);
  const typeReadOnly = !snapshot.canEdit || snapshot.readOnlyFields?.includes(typeKey) === true;
  const typeField = schema?.fields.find((field) => field.id === definition.typeField);
  const snapshotChoices = snapshot.fieldChoices?.[typeKey];
  const choices = mergeSpatialChoices(definition.modes, snapshotChoices ?? typeField?.choices ?? [], selectedType, language);
  const layerTitle = language === 'zh' ? definition.title[0] : definition.title[1];

  return (
    <section className="editor-meteorology-control" aria-label={layerTitle}>
      <header className="editor-meteorology-layer-header">
        <div>
          <p className="editor-eyebrow">{language === 'zh' ? '气象边界条件' : 'Meteorological boundary condition'}</p>
          <h3>{layerTitle}</h3>
        </div>
        <label className="editor-toggle">
          <input type="checkbox" checked={enabled} disabled={!canEdit} onChange={(event) => props.onFieldChange(enabledKey, event.currentTarget.checked ? 'true' : 'false')} />
          {language === 'zh' ? '启用' : 'Enabled'}
        </label>
      </header>

      {enabled && <>
        <div className="editor-field-grid editor-meteorology-fields">
          <div className="editor-field">
            <label htmlFor={`${definition.schemaId}-spatial-type`}>{editorText(language, snapshot.fieldTitles?.[typeKey] ?? typeField?.title ?? (language === 'zh' ? '空间数据类型' : 'Spatial data type'))}</label>
            <select id={`${definition.schemaId}-spatial-type`} value={selectedType} disabled={typeReadOnly} onChange={(event) => props.onFieldChange(typeKey, event.currentTarget.value)}>
              {choices.map((choice) => <option key={choice.id} value={choice.id}>{editorText(language, choice.title)}</option>)}
            </select>
          </div>
          {definition.ratioField && <MeteorologyNumericField {...props} schema={schema} schemaId={definition.schemaId} fieldId={definition.ratioField} unit={definition.ratioUnit} />}
          {selectedType === 'Constant' && definition.constantField && (renderConstant ? renderConstant(props) : <SpatialConstantField props={props} schema={schema} schemaId={definition.schemaId} fieldId={definition.constantField} unitData={spatial?.valueUnits} unitLabel={spatial?.unit ?? (language === 'zh' ? definition.constantUnit?.[0] : definition.constantUnit?.[1])} />)}
        </div>

        {selectedType === 'Point' && <PointControl {...props} layerId={definition.layerId} layerTitle={layerTitle} spatial={spatial} series={series} />}
        {selectedType === 'Gridded' && <SpatialInputs {...props} spatial={spatial} series={series} />}
      </>}

      {spatial && <div className="editor-meteorology-source-summary">
        <span>{language === 'zh' ? '输入数据' : 'Input data'}: {spatial.inputs.length}</span>
        <span>{language === 'zh' ? '测站' : 'Stations'}: {spatial.stations.length}</span>
        {spatial.unit && <span>{language === 'zh' ? '源单位' : 'Source unit'}: {spatial.unit}</span>}
        {spatial.isVector && <span>{language === 'zh' ? '矢量数据' : 'Vector data'}</span>}
      </div>}
    </section>
  );
}

function MeteorologyNumericField(props: EditorPageProps & { schema?: EditorPageProps['schemas'][number]; schemaId: string; fieldId: string; unit?: [string, string] }) {
  const { language, snapshot, draft, schemaId, fieldId, unit } = props;
  const fieldKey = `${schemaId}/${fieldId}`;
  const field = props.schema?.fields.find((item) => item.id === fieldId) ?? fallbackNumberField(fieldId);
  const value = draft.fields[fieldKey] ?? snapshot.fields[fieldKey] ?? '';
  const baseline = snapshot.fields[fieldKey];
  const readOnly = !snapshot.canEdit || field.readOnly === true || snapshot.readOnlyFields?.includes(fieldKey) === true;
  const error = value !== baseline ? validateFieldValue(field, value, language) : null;
  const label = editorText(language, snapshot.fieldTitles?.[fieldKey] ?? field.title);
  return <div className="editor-field">
    <label htmlFor={fieldKey}>{label}{unit && <span className="editor-meteorology-unit">{language === 'zh' ? unit[0] : unit[1]}</span>}</label>
    <input id={fieldKey} type="text" inputMode="decimal" value={value} readOnly={readOnly} aria-invalid={error ? 'true' : undefined} onChange={(event) => props.onFieldChange(fieldKey, event.currentTarget.value)} />
    {error && <small className="editor-field-error">{error}</small>}
  </div>;
}

function SpatialConstantField({ props, schema, schemaId, fieldId, unitData, unitLabel }: {
  props: EditorPageProps;
  schema?: EditorPageProps['schemas'][number];
  schemaId: string;
  fieldId: string;
  unitData?: SpatialPointData['valueUnits'];
  unitLabel?: string;
}) {
  const fieldKey = schemaId + '/' + fieldId;
  const field = schema?.fields.find((item) => item.id === fieldId) ?? fallbackNumberField(fieldId);
  return <UnitAwareNumberField
    backend={props.backend}
    id={fieldKey}
    title={editorText(props.language, props.snapshot.fieldTitles?.[fieldKey] ?? field.title)}
    value={props.draft.fields[fieldKey] ?? props.snapshot.fields[fieldKey] ?? ''}
    baseline={props.snapshot.fields[fieldKey]}
    field={field}
    unitData={unitData}
    unitLabel={unitLabel}
    disabled={!props.snapshot.canEdit || field.readOnly === true || props.snapshot.readOnlyFields?.includes(fieldKey) === true}
    language={props.language}
    onChange={(sourceValue) => props.onFieldChange(fieldKey, sourceValue)}
  />;
}

function spatialSeries(props: EditorPageProps, layerId: string, spatial?: SpatialPointData) {
  const all = Object.values((props.draft.boundaryView ?? props.snapshot.boundary)?.series ?? {});
  const inputs = spatial?.inputs ?? [];
  const exactInputs = new Map(inputs.map((input) => [input.scope, input.pageId ?? input.scope + '/Time Series']));
  return all.filter((series) => {
    const parts = series.scope.split('/');
    if (parts.length !== 4 || parts[0] !== 'Spatial' || parts[1] !== layerId || parts[2] !== 'Gauge') return false;
    if (series.pageId !== series.scope + '/Time Series') return false;
    return exactInputs.size === 0 || exactInputs.get(series.scope) === series.pageId;
  });
}

function mergeSpatialChoices(modes: MeteorologyLayerDefinition['modes'], sourceChoices: SchemaChoice[], selected: string, language: 'zh' | 'en'): SchemaChoice[] {
  const known = new Map(sourceChoices.map((choice) => [choice.id, choice]));
  const choices = modes.map((id) => known.get(id) ?? { id, title: spatialTypeTitle(id, language) });
  if (selected && !choices.some((choice) => choice.id === selected)) choices.unshift({ id: selected, title: language === 'zh' ? `当前值：${selected}（此页面暂不支持）` : `Current value: ${selected} (unsupported on this page)` });
  return choices;
}

function spatialTypeTitle(id: string, language: 'zh' | 'en'): string {
  if (id === 'Constant') return language === 'zh' ? '恒定值' : 'Constant';
  if (id === 'Point') return language === 'zh' ? '点测站' : 'Point stations';
  return language === 'zh' ? '栅格' : 'Gridded';
}

function fallbackNumberField(id: string): EditorFieldSchema {
  return { id, title: id, kind: 'number', sourceType: 'float' };
}
