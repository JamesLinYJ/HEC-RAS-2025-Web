import type { EditorPageProps, SchemaChoice } from './model';
import { ControlHeader } from '../../controls/ControlHeader';
import { ExternalConnectionLinker } from '../../controls/ExternalConnectionLinker';

type BoundaryLine = NonNullable<EditorPageProps['snapshot']['boundary']>['lines'][number];

export interface NewPayloadPageProps extends EditorPageProps {
  line: BoundaryLine;
  values: Record<string, string>;
  setValue: (name: string, value: string) => void;
  readValue: (name: string) => string;
  error: string;
  disabled: boolean;
  onCancel: () => void;
  onSubmit: () => void;
}

/** Source NewPayloadPage form; keeps the exact ViewModel_* source field IDs. */
export function NewPayloadPage({ line, values, setValue, readValue, error, disabled, onCancel, onSubmit, ...props }: NewPayloadPageProps) {
  const prefix = 'NewPayloadPage/ViewModel_';
  const dataType = readValue('HydraulicDataType');
  const variable = readValue('HydraulicType');
  const choices = line.newPayload?.choices ?? {};
  const isTimeSeries = dataType === 'Tabular' || dataType === 'SpatiallyVariableTabular';
  const label = (text: string, english: string) => props.language === 'zh' ? text : english;
  const choiceTitle = (choice: SchemaChoice): string => {
    if (choice.titleIsUserData) return choice.title;
    if (props.language !== 'zh') return choice.title;
    const translated: Record<string, string> = {
      Constant: '恒定值', Tabular: '时序表', SpatiallyVariableTabular: '空间变化时序表', Remote: '外部连接',
      Regular: '等时间间隔', Irregular: '不等时间间隔',
    };
    return translated[choice.id] ?? choice.title;
  };
  const optionList = (name: string, fieldLabel: string, items: SchemaChoice[]) => <div className="editor-field">
    <label htmlFor={`new-payload-${line.id}-${name}`}>{fieldLabel}</label>
    <select id={`new-payload-${line.id}-${name}`} value={readValue(name)} disabled={disabled || items.length === 0} onChange={(event) => setValue(name, event.currentTarget.value)}>
      {items.map((choice) => <option key={choice.id} value={choice.id}>{choiceTitle(choice)}</option>)}
    </select>
  </div>;
  const currentFields = Object.fromEntries(Object.entries(values).map(([key, value]) => [key.replace(prefix, ''), value]));
  return <section className="editor-payload-card" aria-label={label('新建边界载荷', 'Create boundary payload')}>
    <div className="editor-payload-heading"><h3><ControlHeader title={label('新建边界载荷', 'New boundary payload')} /></h3></div>
    <div className="editor-field-grid">
      <div className="editor-field"><label htmlFor={`new-payload-${line.id}-name`}>{label('名称', 'Name')}</label><input id={`new-payload-${line.id}-name`} type="text" value={readValue('PayloadName')} readOnly={disabled} onChange={(event) => setValue('PayloadName', event.currentTarget.value)} /></div>
      {optionList('PayloadVariable', label('载荷变量', 'Payload variable'), choices[`${prefix}PayloadVariable`] ?? [])}
      {optionList('HydraulicType', label('水力变量', 'Hydraulic variable'), choices[`${prefix}HydraulicType`] ?? [])}
      {optionList('HydraulicDataType', label('数据类型', 'Data type'), choices[`${prefix}HydraulicDataType`] ?? [])}
      {isTimeSeries && optionList('TimeSeriesType', label('时序类型', 'Time-series type'), choices[`${prefix}TimeSeriesType`] ?? [])}
      {dataType === 'Constant' && <div className="editor-field">
        <label htmlFor={`new-payload-${line.id}-constant`}>{label('恒定值', 'Constant value')} <span className="editor-meteorology-unit">{line.newPayload?.constantUnits[variable] ?? ''}</span></label>
        <input id={`new-payload-${line.id}-constant`} type="number" step="any" inputMode="decimal" value={readValue('HydraulicConstantValue')} readOnly={disabled} onChange={(event) => setValue('HydraulicConstantValue', event.currentTarget.value)} />
      </div>}
      {dataType === 'Remote' && <ExternalConnectionLinker
        id={`new-payload-${line.id}-SelectedExternalConnection`}
        label={label('外部连接', 'External connection')}
        selectedConnectionId={readValue('SelectedExternalConnection')}
        choices={choices[`${prefix}SelectedExternalConnection`] ?? []}
        disabled={disabled}
        language={props.language}
        placeholder={label('请选择外部连接…', 'Select an external connection…')}
        emptyMessage={label('没有可用的外部连接。', 'No external connections are available.')}
        formatChoice={choiceTitle}
        onSelectionChange={(connectionId) => setValue('SelectedExternalConnection', connectionId)}
      />}
      {dataType === 'Remote' && <div className="editor-field"><label htmlFor={`new-payload-${line.id}-path`}>{label('外部数据路径', 'External data path')}</label><input id={`new-payload-${line.id}-path`} type="text" value={readValue('Path')} readOnly={disabled} onChange={(event) => setValue('Path', event.currentTarget.value)} /></div>}
    </div>
    {error && <p className="editor-inline-error" role="alert">{error}</p>}
    {!props.onBoundaryTransform && <p className="editor-field-muted">{label('当前父级工作台未接入载荷变换操作。', 'The parent workbench has not connected the payload transform operation.')}</p>}
    <div className="editor-workbench-actions">
      <button type="button" className="editor-button editor-button-primary" disabled={disabled || !props.onBoundaryTransform || Object.keys(currentFields).length === 0} onClick={onSubmit}>{label('加入草稿', 'Add to draft')}</button>
      <button type="button" className="editor-button" onClick={onCancel}>{label('取消', 'Cancel')}</button>
    </div>
    <p className="editor-field-muted">{label('载荷身份由 C# 服务端创建；确认后仍需点击父级“保存”写入模型文件。', 'The C# server assigns the payload identity. Save from the parent workbench to write it to the model file.')}</p>
  </section>;
}
