import type { ReactNode } from 'react';
import type { EditorFieldSchema, EditorPageProps, SchemaChoice, SpatialPointData, SeriesTransformOperation } from './model';
import { ModelDataTable } from './ModelDataTable';
import { UnitAwareNumberField } from './UnitAwareNumberField';
import { validateFieldValue } from './SchemaFieldRenderer';
import { editorText } from './editorLocalization';
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

        {selectedType === 'Point' && <SpatialInputs {...props} spatial={spatial} series={series} />}
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

export function SpatialInputs({ spatial, series, language, snapshot, draft, onRowCellChange, onSampleChange, onSeriesTransform, canTransformSeries }: EditorPageProps & { spatial?: SpatialPointData; series: NonNullable<NonNullable<EditorPageProps['snapshot']['boundary']>['series'][string]>[] }) {
  const inputs = spatial?.inputs ?? [];
  const stations = spatial?.stations ?? [];
  if (inputs.length === 0 && series.length === 0 && stations.length === 0) {
    return <p className="editor-meteorology-note">{language === 'zh' ? '此模型快照没有该类型的空间输入。可打开“空间数据与输入管理”查看快照，或使用已接通的 C# 空间变换添加测站与输入。' : 'This model snapshot contains no spatial inputs of this type. Open Spatial data and inputs to inspect the snapshot or use the connected C# spatial transform to add stations and inputs.'}</p>;
  }
  return <div className="editor-meteorology-spatial">
    {stations.length > 0 && <details className="editor-series-panel" open>
      <summary>{language === 'zh' ? '测站坐标' : 'Station coordinates'} <span>{stations.length}</span></summary>
      <div className="editor-table-scroll"><table className="editor-data-table">
        <thead><tr><th>{language === 'zh' ? '测站' : 'Station'}</th><th>X</th><th>Y</th></tr></thead>
        <tbody>{stations.map((station) => <tr key={station.id}><th scope="row">{station.name || station.id}</th><td>{station.x}</td><td>{station.y}</td></tr>)}</tbody>
      </table></div>
    </details>}
    {inputs.map((input, index) => {
      const expectedPage = input.pageId ?? input.scope + '/Time Series';
      const match = series.find((item) => item.scope === input.scope && item.pageId === expectedPage);
      const station = stations.find((item) => item.id === input.stationId);
      const title = station?.name ?? (language === 'zh' ? `空间数据 ${index + 1}` : `Spatial input ${index + 1}`);
      return <article className="editor-meteorology-input" key={input.id}>
        <h4>{title}</h4>
        <details className="editor-source-details"><summary>{language === 'zh' ? '数据身份' : 'Data identity'}</summary><code>{input.pageId ?? input.scope}</code></details>
        {match ? <SpatialSeries {...{ series: match, language, snapshot, draft, onRowCellChange, onSampleChange, onSeriesTransform, canTransformSeries }} /> : <p className="editor-meteorology-note">{language === 'zh' ? '后端快照未提供此输入的时序表。' : 'The backend snapshot did not include a time-series table for this input.'}</p>}
      </article>;
    })}
    {inputs.length === 0 && series.map((item) => <SpatialSeries key={item.pageId} series={item} language={language} snapshot={snapshot} draft={draft} onRowCellChange={onRowCellChange} onSampleChange={onSampleChange} onSeriesTransform={onSeriesTransform} canTransformSeries={canTransformSeries} />)}
  </div>;
}

function SpatialSeries({ series, language, snapshot, draft, onRowCellChange, onSampleChange, onSeriesTransform, canTransformSeries }: Pick<EditorPageProps, 'language' | 'snapshot' | 'draft' | 'onRowCellChange' | 'onSampleChange' | 'onSeriesTransform' | 'canTransformSeries'> & { series: NonNullable<NonNullable<EditorPageProps['snapshot']['boundary']>['series'][string]> }) {
  const rows = series.tableId ? draft.rows[series.tableId] ?? series.rows : [];
  const samples = draft.samples[series.pageId] ?? series.samples;
  if (series.tableId && series.tableId !== series.pageId + '/Values') return <p className="editor-meteorology-note">{language === 'zh' ? '时序表身份与空间边界契约不匹配，已停用编辑。' : 'The time-series table identity does not match the spatial boundary contract; editing is disabled.'}</p>;
  return <div className="editor-meteorology-series-editor">
    <SeriesTransformActions {...{ series, rows, samples, language, snapshot, canTransformSeries, onSeriesTransform }} />
    {series.tableId
      ? <ModelDataTable tableID={series.tableId} rows={rows} columns={series.columns} canEdit={snapshot.canEdit} onCellChange={onRowCellChange} language={language} />
      : samples.length === 0
        ? <p className="editor-empty-state">{language === 'zh' ? '该输入没有时序行。' : 'This input has no time-series rows.'}</p>
        : <div className="editor-table-scroll"><table className="editor-data-table">
          <thead><tr><th>#</th><th>{editorText(language, series.xTitle)}</th><th>{editorText(language, series.yTitle)}</th></tr></thead>
          <tbody>{samples.map((sample, index) => {
            const raw = draft.sampleText[series.pageId]?.[sample.id];
            const x = raw?.x ?? (series.xIsDate ? toSafeDateTimeLocal(sample.x) : String(sample.x));
            const y = raw?.y ?? (sample.y == null ? '' : String(sample.y));
            const dateInput = series.xIsDate && isDateTimeLocalValue(x);
            return <tr key={sample.id}>
              <th scope="row">{index + 1}</th>
              <td><input type={dateInput ? 'datetime-local' : 'text'} inputMode={dateInput ? undefined : series.xIsDate ? 'text' : 'decimal'} value={x} readOnly={!snapshot.canEdit || !series.canEditX} onChange={(event) => onSampleChange?.(series.pageId, sample.id, 'x', event.currentTarget.value)} /></td>
              <td><input type="text" inputMode="decimal" value={y} readOnly={!snapshot.canEdit} onChange={(event) => onSampleChange?.(series.pageId, sample.id, 'y', event.currentTarget.value)} /></td>
            </tr>;
          })}</tbody>
        </table></div>}
  </div>;
}

function SeriesTransformActions({ series, rows, samples, language, snapshot, canTransformSeries, onSeriesTransform }: Pick<EditorPageProps, 'language' | 'snapshot' | 'canTransformSeries' | 'onSeriesTransform'> & {
  series: NonNullable<NonNullable<EditorPageProps['snapshot']['boundary']>['series'][string]>;
  rows: NonNullable<EditorPageProps['draft']['rows'][string]>;
  samples: NonNullable<EditorPageProps['draft']['samples'][string]>;
}) {
  const canAppend = series.operations.includes('appendRow');
  const canRemove = series.operations.includes('removeLastRow');
  if ((!canAppend && !canRemove) || !onSeriesTransform) return null;
  const rowCount = series.tableId ? rows.length : samples.length;
  const disabled = !snapshot.canEdit || !canTransformSeries;
  const run = (operation: SeriesTransformOperation) => { void onSeriesTransform(series.pageId, operation); };
  return <div className="editor-meteorology-series-actions" aria-label={language === 'zh' ? '时序行操作' : 'Time-series row actions'}>
    {canAppend && <button type="button" className="editor-button" disabled={disabled} data-testid={`series-transform-${encodeURIComponent(series.pageId)}-appendRow`} data-page-id={series.pageId} data-operation="appendRow" onClick={() => run('appendRow')}>{language === 'zh' ? '添加末行' : 'Append row'}</button>}
    {canRemove && <button type="button" className="editor-button" disabled={disabled || rowCount === 0} data-testid={`series-transform-${encodeURIComponent(series.pageId)}-removeLastRow`} data-page-id={series.pageId} data-operation="removeLastRow" onClick={() => run('removeLastRow')}>{language === 'zh' ? '移除末行' : 'Remove last row'}</button>}
  </div>;
}

function toSafeDateTimeLocal(epochSeconds: number): string {
  const date = new Date(epochSeconds * 1000);
  if (!Number.isFinite(epochSeconds) || !Number.isFinite(date.getTime())) return String(epochSeconds);
  const pad = (value: number) => String(value).padStart(2, '0');
  return String(date.getFullYear()).padStart(4, '0') + '-' + pad(date.getMonth() + 1) + '-' + pad(date.getDate()) + 'T' + pad(date.getHours()) + ':' + pad(date.getMinutes());
}

function isDateTimeLocalValue(value: string): boolean {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})$/.exec(value);
  if (!match) return false;
  const [, year, month, day, hour, minute] = match;
  const date = new Date(Number(year), Number(month) - 1, Number(day), Number(hour), Number(minute));
  return date.getFullYear() === Number(year) && date.getMonth() === Number(month) - 1 && date.getDate() === Number(day)
    && date.getHours() === Number(hour) && date.getMinutes() === Number(minute);
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
  if (selected && !choices.some((choice) => choice.id === selected)) choices.unshift({ id: selected, title: language === 'zh' ? `当前值：${selected}（原控件不支持）` : `Current value: ${selected} (unsupported by the source control)` });
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
