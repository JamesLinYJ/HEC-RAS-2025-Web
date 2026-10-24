import type { Language } from '../../../shared/workbench';
import type { GeoprocessingFieldValues, GeoprocessingToolDefinition } from '../GeoprocessingToolBase';
import { ToolParameterForm } from './ToolParameterForm';

export function ToolOptionsFlyout({ open, tool, language, values, outputOptions, outputHandler, outputName, fileOutput,
  onFieldChange, onOutputHandlerChange, onOutputNameChange, onExportParameters, onClose }: {
  open: boolean;
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
  onClose: () => void;
}) {
  const zh = language === 'zh';
  return <section id="geoprocessing-tool-options" className="gp-control-options-flyout" hidden={!open}
    aria-label={zh ? '当前工具参数选项' : 'Current tool options'}>
    <header><div><span className="tools-kicker">{zh ? '参数设置' : 'PARAMETERS'}</span>
      <h4>{tool.title[language]}</h4></div>
      <button type="button" className="gp-control-close-options" onClick={onClose} aria-label={zh ? '收起参数选项' : 'Close tool options'}>×</button></header>
    <ToolParameterForm tool={tool} language={language} values={values} outputOptions={outputOptions}
      outputHandler={outputHandler} outputName={outputName} fileOutput={fileOutput} onFieldChange={onFieldChange}
      onOutputHandlerChange={onOutputHandlerChange} onOutputNameChange={onOutputNameChange}
      onExportParameters={onExportParameters} />
  </section>;
}
