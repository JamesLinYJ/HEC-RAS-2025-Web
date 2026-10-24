import { isEditorFieldReadOnly, type EditorPageProps } from './model';
import type { BoundarySeries } from './RegularTimeSeriesPayloadEditor';
import { ConstantPayloadEditor } from './ConstantPayloadEditor';
import { ExternalPayloadEditor } from './ExternalPayloadEditor';
import { IrregularTimeSeriesPayloadEditor } from './IrregularTimeSeriesPayloadEditor';
import { RegularSpatiallyVariableTimeSeriesPayloadEditor } from './RegularSpatiallyVariableTimeSeriesPayloadEditor';
import { RegularTimeSeriesPayloadEditor } from './RegularTimeSeriesPayloadEditor';
import { SpatiallyVariableTimeSeriesPayloadEditor } from './SpatiallyVariableTimeSeriesPayloadEditor';

type BoundaryLine = NonNullable<EditorPageProps['snapshot']['boundary']>['lines'][number];
type BoundaryPayload = BoundaryLine['payloads'][number];

/** Source PayloadListItem behavior and the payload-specific editor assembly. */
export function PayloadListItem({ payload, line, series, onRemove, ...props }: EditorPageProps & {
  payload: BoundaryPayload;
  line: BoundaryLine;
  series?: BoundarySeries;
  onRemove: () => void;
}) {
  const nameID = `${payload.scope}/PayloadListItem/Payload_Name`;
  const enabledID = `${payload.scope}/PayloadListItem/Payload_IsEnabled`;
  const descriptionID = `${payload.scope}/PayloadDetails/Description`;
  const variableID = `${payload.scope}/PayloadDetails/Variable`;
  const name = props.draft.fields[nameID] ?? props.snapshot.fields[nameID] ?? payload.name ?? payload.friendlyName;
  const enabled = (props.draft.fields[enabledID] ?? props.snapshot.fields[enabledID] ?? String(payload.isEnabled)) === 'true';
  const description = props.draft.fields[descriptionID] ?? props.snapshot.fields[descriptionID] ?? payload.description;
  const variable = props.draft.fields[variableID] ?? props.snapshot.fields[variableID] ?? payload.variable;
  const setEnabled = (nextEnabled: boolean): void => {
    if (nextEnabled && payload.isHydraulic) {
      for (const candidate of line.payloads.filter((item) => item.isHydraulic)) {
        props.onFieldChange(`${candidate.scope}/PayloadListItem/Payload_IsEnabled`, candidate.id === payload.id ? 'true' : 'false');
      }
    } else props.onFieldChange(enabledID, nextEnabled ? 'true' : 'false');
  };
  const seriesIsRegular = series?.schemaId === 'RegularTimeSeriesPayloadEditor'
    || series?.schemaId === 'RegularSpatiallyVariableTimeSeriesPayloadEditor' && hasRegularTimeDefinition(series, props);

  return <article className="editor-payload-card">
    <div className="editor-payload-heading">
      <h3>{name}</h3>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
        <label className="editor-toggle"><input type="checkbox" disabled={!props.snapshot.canEdit || isEditorFieldReadOnly(props, enabledID)} checked={enabled} onChange={(event) => setEnabled(event.currentTarget.checked)} />{props.language === 'zh' ? '启用' : 'Enabled'}</label>
        <button type="button" className="editor-button" disabled={!props.snapshot.canEdit || !props.onBoundaryTransform} onClick={onRemove}>{props.language === 'zh' ? '移除' : 'Remove'}</button>
      </div>
    </div>
    <div className="editor-field-grid">
      <div className="editor-field"><label htmlFor={`payload-${payload.id}-name`}>{props.language === 'zh' ? '名称' : 'Name'}</label><input id={`payload-${payload.id}-name`} type="text" value={props.draft.fields[nameID] ?? props.snapshot.fields[nameID] ?? payload.name} readOnly={!props.snapshot.canEdit || isEditorFieldReadOnly(props, nameID)} onChange={(event) => props.onFieldChange(nameID, event.currentTarget.value)} /></div>
      <div className="editor-field"><label htmlFor={`payload-${payload.id}-variable`}>{props.language === 'zh' ? '变量' : 'Variable'}</label><input id={`payload-${payload.id}-variable`} type="text" value={variable} readOnly /></div>
      <div className="editor-field"><label htmlFor={`payload-${payload.id}-description`}>{props.language === 'zh' ? '说明' : 'Description'}</label><input id={`payload-${payload.id}-description`} type="text" value={description} readOnly={!props.snapshot.canEdit || isEditorFieldReadOnly(props, descriptionID)} onChange={(event) => props.onFieldChange(descriptionID, event.currentTarget.value)} /></div>
    </div>
    {payload.editorSchemaId === 'ConstantPayloadEditor' && <ConstantPayloadEditor {...props} line={line} payload={payload} />}
    {payload.editorSchemaId === 'ExternalPayloadEditor' && <ExternalPayloadEditor {...props} payload={payload} />}
    {series?.schemaId === 'RegularSpatiallyVariableTimeSeriesPayloadEditor' && seriesIsRegular && <RegularSpatiallyVariableTimeSeriesPayloadEditor {...props} series={series} />}
    {series?.schemaId === 'RegularSpatiallyVariableTimeSeriesPayloadEditor' && !seriesIsRegular && <SpatiallyVariableTimeSeriesPayloadEditor {...props} series={series} />}
    {series?.schemaId === 'RegularTimeSeriesPayloadEditor' && <RegularTimeSeriesPayloadEditor {...props} series={series} />}
    {series?.schemaId === 'IrregularTimeSeriesPayloadEditor' && <IrregularTimeSeriesPayloadEditor {...props} series={series} />}
    {!series && payload.editorSchemaId !== 'ConstantPayloadEditor' && payload.editorSchemaId !== 'ExternalPayloadEditor' && <p className="editor-field-muted">{props.language === 'zh' ? '后端快照未提供此载荷的可编辑时序表。' : 'The backend snapshot did not include an editable time-series table for this payload.'}</p>}
    {series && !['RegularTimeSeriesPayloadEditor', 'IrregularTimeSeriesPayloadEditor', 'RegularSpatiallyVariableTimeSeriesPayloadEditor'].includes(series.schemaId) && <p className="editor-field-muted">{props.language === 'zh' ? `当前没有迁移 ${series.schemaId} 的时序编辑控件。` : `No time-series editor has been ported for ${series.schemaId}.`}</p>}
    <details className="editor-source-details"><summary>{props.language === 'zh' ? '原始控件标识' : 'Source control identity'}</summary><code>{payload.editorSchemaId} · {payload.scope}</code></details>
  </article>;
}

export function hasRegularTimeDefinition(series: BoundarySeries, props: Pick<EditorPageProps, 'snapshot' | 'draft'>): boolean {
  const intervalID = `${series.scope}/${series.schemaId}/TimeSeriesDefinition_Interval`;
  const useFixedID = `${series.scope}/${series.schemaId}/TimeSeriesDefinition_UseFixedStartTime`;
  return intervalID in props.snapshot.fields || intervalID in props.draft.fields
    || useFixedID in props.snapshot.fields || useFixedID in props.draft.fields;
}
