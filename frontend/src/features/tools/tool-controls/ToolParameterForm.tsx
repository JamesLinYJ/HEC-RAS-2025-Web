import type { Language } from '../../../shared/workbench';
import { displayToolOption, type GeoprocessingFieldValues, type GeoprocessingToolDefinition, type ToolField } from '../GeoprocessingToolBase';
import { ToolParameterField } from './ToolParameterField';

function visibleField(field: ToolField, values: GeoprocessingFieldValues): boolean {
  return !field.showWhen || values[field.showWhen.key] === field.showWhen.value;
}

export function ToolParameterForm({ tool, language, values, outputOptions, outputHandler, outputName, fileOutput,
  onFieldChange, onOutputHandlerChange, onOutputNameChange, onExportParameters }: {
  tool: GeoprocessingToolDefinition;
  language: Language;
  values: GeoprocessingFieldValues;
  outputOptions: string[];
  outputHandler: string;
  outputName: string;
  fileOutput: boolean;
  onFieldChange: (field: string, value: string | boolean) => void;
  onOutputHandlerChange: (value: string) => void;
  onOutputNameChange: (value: string) => void;
  onExportParameters: () => void;
}) {
  const zh = language === 'zh';
  const fields = tool.fields.filter(field => visibleField(field, values));
  return <div className="gp-control-parameter-form">
    <div className="tools-subheading-row"><h4>{zh ? '工具参数' : 'Tool parameters'}</h4>
      <button className="tools-text-button" type="button" onClick={onExportParameters}>{zh ? '导出参数 JSON' : 'Export parameter JSON'}</button></div>
    {fields.length ? <div className="tools-field-grid">
      {fields.map(field => <ToolParameterField key={field.key} field={field} value={values[field.key]} language={language}
        onChange={value => onFieldChange(field.key, value)} />)}
    </div> : <p className="tools-muted">{zh ? '此工具没有额外参数。' : 'This tool has no additional parameters.'}</p>}
    <div className="tools-field-grid tools-common-fields">
      <label className="tools-field gp-control-field"><span>{zh ? '输出方式' : 'Output handler'}</span>
        <select value={outputHandler} onChange={event => onOutputHandlerChange(event.target.value)}>
          {outputOptions.map(option => <option key={option} value={option}>{displayToolOption(option, language)}</option>)}
        </select></label>
      {fileOutput && <label className="tools-field gp-control-field"><span>{zh ? '输出名称' : 'Output name'}</span>
        <input value={outputName} onChange={event => onOutputNameChange(event.target.value)}
          placeholder={zh ? '输入名称' : 'Enter a name'} /></label>}
    </div>
    <p className="tools-field-help">{zh ? '参数会提交给服务端校验；校验回执不代表源数据已读取，也不代表工具已经运行。' : 'Parameters are sent to backend validation. A validation receipt does not mean the source was read or the tool was executed.'}</p>
  </div>;
}
