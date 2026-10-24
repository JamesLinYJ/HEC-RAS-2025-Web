import { useEffect, useState } from 'react';
import { isEditorFieldReadOnly, type EditorPageProps } from './model';
import type { BoundarySeries } from './RegularTimeSeriesPayloadEditor';
import { ConstantPayloadEditor } from './ConstantPayloadEditor';
import { ExternalPayloadEditor } from './ExternalPayloadEditor';
import { RegularSpatiallyVariableTimeSeriesPayloadEditor } from './RegularSpatiallyVariableTimeSeriesPayloadEditor';
import { RegularTimeSeriesPayloadEditor } from './RegularTimeSeriesPayloadEditor';

type BoundaryLine = NonNullable<EditorPageProps['snapshot']['boundary']>['lines'][number];
type BoundaryPayload = BoundaryLine['payloads'][number];
type BoundaryChoice = NonNullable<BoundaryLine['newPayload']>['choices'][string][number];
const payloadFieldPrefix = 'NewPayloadPage/ViewModel_';

/** React port of the source BoundaryConditionLinePayloadsList and its add flow. */
export function BoundaryConditionLinePayloadsList({ line, series, createOpen, onCreateOpenChange, ...props }: EditorPageProps & {
  line: BoundaryLine;
  series: BoundarySeries[];
  createOpen: boolean;
  onCreateOpenChange: (open: boolean) => void;
}) {
  const [newFields, setNewFields] = useState<Record<string, string>>({});
  const [createError, setCreateError] = useState('');
  const newPayload = line.newPayload;

  useEffect(() => {
    if (!createOpen || !newPayload) return;
    setNewFields({ ...newPayload.fields });
    setCreateError('');
  }, [createOpen, line.id, newPayload]);

  const readNew = (name: string) => newFields[`${payloadFieldPrefix}${name}`] ?? '';
  const writeNew = (name: string, value: string) => setNewFields((current) => ({ ...current, [`${payloadFieldPrefix}${name}`]: value }));
  const canMutate = props.snapshot.canEdit && !!props.onBoundaryTransform;
  const addPayload = async (): Promise<void> => {
    if (!newPayload || !props.onBoundaryTransform) return;
    const name = readNew('PayloadName').trim();
    const variable = readNew('HydraulicType');
    const type = readNew('HydraulicDataType');
    const numericValue = readNew('HydraulicConstantValue');
    if (!name) {
      setCreateError(props.language === 'zh' ? '请输入载荷名称。' : 'Enter a payload name.');
      return;
    }
    if (variable === 'Normal Depth' && type !== 'Constant' || variable === 'StageVelocityVelocity' && type !== 'SpatiallyVariableTabular') {
      setCreateError(props.language === 'zh' ? '此水力变量不支持所选数据类型。' : 'This hydraulic variable does not support the selected data type.');
      return;
    }
    if (type === 'Constant' && (numericValue.trim() === '' || !Number.isFinite(Number(numericValue)))) {
      setCreateError(props.language === 'zh' ? '请输入有效的恒定值。' : 'Enter a valid constant value.');
      return;
    }
    if ((type === 'Tabular' || type === 'SpatiallyVariableTabular') && !['Regular', 'Irregular'].includes(readNew('TimeSeriesType'))) {
      setCreateError(props.language === 'zh' ? '请选择时序类型。' : 'Choose a time-series type.');
      return;
    }
    if (type === 'Remote' && !readNew('SelectedExternalConnection')) {
      setCreateError(props.language === 'zh' ? '请先选择外部连接。' : 'Select an external connection first.');
      return;
    }
    const accepted = await props.onBoundaryTransform(line.id, 'create', null, { ...newFields, [`${payloadFieldPrefix}PayloadName`]: name });
    if (accepted) {
      onCreateOpenChange(false);
      setCreateError('');
    }
  };

  const removePayload = async (payload: BoundaryPayload): Promise<void> => {
    if (!props.onBoundaryTransform) return;
    await props.onBoundaryTransform(line.id, 'remove', payload.id, {});
  };

  return <section className="editor-boundary-content" aria-label={props.language === 'zh' ? '边界线载荷列表' : 'Boundary line payload list'}>
    <div className="editor-payload-heading">
      <h3>{props.language === 'zh' ? '边界载荷' : 'Boundary payloads'} <span className="editor-field-muted">{line.payloads.length}</span></h3>
      <button type="button" className="editor-button" disabled={!canMutate || !newPayload} onClick={() => onCreateOpenChange(!createOpen)}>{createOpen ? (props.language === 'zh' ? '收起' : 'Close') : (props.language === 'zh' ? '添加载荷…' : 'Add payload…')}</button>
    </div>
    {!props.onBoundaryTransform && <p className="editor-field-muted">{props.language === 'zh' ? '当前工作台未提供边界载荷变换回调，添加和移除已停用。' : 'The current workbench has no boundary payload transform callback, so add and remove are disabled.'}</p>}
    {createOpen && <NewPayloadForm {...props} line={line} values={newFields} setValue={writeNew} readValue={readNew} error={createError} disabled={!canMutate || !newPayload} onCancel={() => onCreateOpenChange(false)} onSubmit={() => { void addPayload(); }} />}
    {line.payloads.length === 0 && <div className="editor-empty-state">{props.language === 'zh' ? '此边界线尚无载荷。' : 'This boundary line has no payloads.'}</div>}
    <div className="editor-payload-grid">
      {line.payloads.map((payload) => <PayloadCard key={payload.id} payload={payload} line={line} series={series.find((item) => item.scope.startsWith(payload.scope))} {...props} onRemove={() => { void removePayload(payload); }} />)}
    </div>
  </section>;
}

function PayloadCard({ payload, line, series, onRemove, ...props }: EditorPageProps & {
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
    {series && series.schemaId === 'RegularSpatiallyVariableTimeSeriesPayloadEditor' && <RegularSpatiallyVariableTimeSeriesPayloadEditor {...props} series={series} />}
    {series && series.schemaId !== 'RegularSpatiallyVariableTimeSeriesPayloadEditor' && <RegularTimeSeriesPayloadEditor {...props} series={series} />}
    {!series && payload.editorSchemaId !== 'ConstantPayloadEditor' && payload.editorSchemaId !== 'ExternalPayloadEditor' && <p className="editor-field-muted">{props.language === 'zh' ? '后端快照未提供此载荷的可编辑时序表。' : 'The backend snapshot did not include an editable time-series table for this payload.'}</p>}
    <details className="editor-source-details"><summary>{props.language === 'zh' ? '原始控件标识' : 'Source control identity'}</summary><code>{payload.editorSchemaId} · {payload.scope}</code></details>
  </article>;
}

function NewPayloadForm({ line, values, setValue, readValue, error, disabled, onCancel, onSubmit, ...props }: EditorPageProps & {
  line: BoundaryLine;
  values: Record<string, string>;
  setValue: (name: string, value: string) => void;
  readValue: (name: string) => string;
  error: string;
  disabled: boolean;
  onCancel: () => void;
  onSubmit: () => void;
}) {
  const dataType = readValue('HydraulicDataType');
  const variable = readValue('HydraulicType');
  const choices = line.newPayload?.choices ?? {};
  const isTimeSeries = dataType === 'Tabular' || dataType === 'SpatiallyVariableTabular';
  const createSchemaId = 'NewPayloadPage/ViewModel_';
  const label = (text: string, english: string) => props.language === 'zh' ? text : english;
  const choiceTitle = (choice: BoundaryChoice): string => {
    if (props.language !== 'zh') return choice.title;
    const translated: Record<string, string> = {
      Constant: '恒定值', Tabular: '时序表', SpatiallyVariableTabular: '空间变化时序表', Remote: '外部连接',
      Regular: '等时间间隔', Irregular: '不等时间间隔',
    };
    return translated[choice.id] ?? choice.title;
  };
  const optionList = (name: string, fieldLabel: string, items: BoundaryChoice[]) => <div className="editor-field">
    <label htmlFor={`new-payload-${line.id}-${name}`}>{fieldLabel}</label>
    <select id={`new-payload-${line.id}-${name}`} value={readValue(name)} disabled={disabled || items.length === 0} onChange={(event) => setValue(name, event.currentTarget.value)}>
      {items.map((choice) => <option key={choice.id} value={choice.id}>{choiceTitle(choice)}</option>)}
    </select>
  </div>;
  const currentFields = Object.fromEntries(Object.entries(values).map(([key, value]) => [key.replace(createSchemaId, ''), value]));
  return <section className="editor-payload-card" aria-label={label('新建边界载荷', 'Create boundary payload')}>
    <div className="editor-payload-heading"><h3>{label('新建边界载荷', 'New boundary payload')}</h3></div>
    <div className="editor-field-grid">
      <div className="editor-field"><label htmlFor={`new-payload-${line.id}-name`}>{label('名称', 'Name')}</label><input id={`new-payload-${line.id}-name`} type="text" value={readValue('PayloadName')} readOnly={disabled} onChange={(event) => setValue('PayloadName', event.currentTarget.value)} /></div>
      {optionList('PayloadVariable', label('载荷变量', 'Payload variable'), choices.PayloadVariable ?? [])}
      {optionList('HydraulicType', label('水力变量', 'Hydraulic variable'), choices.HydraulicType ?? [])}
      {optionList('HydraulicDataType', label('数据类型', 'Data type'), choices.HydraulicDataType ?? [])}
      {isTimeSeries && optionList('TimeSeriesType', label('时序类型', 'Time-series type'), choices.TimeSeriesType ?? [])}
      {dataType === 'Constant' && <div className="editor-field">
        <label htmlFor={`new-payload-${line.id}-constant`}>{label('恒定值', 'Constant value')} <span className="editor-meteorology-unit">{line.newPayload?.constantUnits[variable] ?? ''}</span></label>
        <input id={`new-payload-${line.id}-constant`} type="number" step="any" inputMode="decimal" value={readValue('HydraulicConstantValue')} readOnly={disabled} onChange={(event) => setValue('HydraulicConstantValue', event.currentTarget.value)} />
      </div>}
      {dataType === 'Remote' && optionList('SelectedExternalConnection', label('外部连接', 'External connection'), choices.SelectedExternalConnection ?? [])}
      {dataType === 'Remote' && <div className="editor-field"><label htmlFor={`new-payload-${line.id}-path`}>{label('外部数据路径', 'External data path')}</label><input id={`new-payload-${line.id}-path`} type="text" value={readValue('Path')} readOnly={disabled} onChange={(event) => setValue('Path', event.currentTarget.value)} /></div>}
    </div>
    {error && <p className="editor-inline-error" role="alert">{error}</p>}
    {!props.onBoundaryTransform && <p className="editor-field-muted">{label('当前父级工作台未接入载荷变换操作。', 'The parent workbench has not connected the payload transform operation.')}</p>}
    <div className="editor-workbench-actions">
      <button type="button" className="editor-button editor-button-primary" disabled={disabled || !props.onBoundaryTransform || !newFieldsAvailable(currentFields)} onClick={onSubmit}>{label('加入草稿', 'Add to draft')}</button>
      <button type="button" className="editor-button" onClick={onCancel}>{label('取消', 'Cancel')}</button>
    </div>
    <p className="editor-field-muted">{label('载荷身份由 C# 服务端创建；确认后仍需点击父级“保存”写入模型文件。', 'The C# server assigns the payload identity. Save from the parent workbench to write it to the model file.')}</p>
  </section>;
}

function newFieldsAvailable(fields: Record<string, string>): boolean {
  return Object.keys(fields).length > 0;
}
