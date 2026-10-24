import type { Language } from '../../../shared/workbench';
import { enabledPlotOptionProperties } from './vectorRenderWorkflow';
import type { VectorPlotOptionDescriptor } from './vectorRenderTypes';

const knownOptionLabels: Record<string, readonly [string, string]> = {
  'Label FIDs': ['显示 FID 标注', 'Label FIDs'],
  'Show Direction Arrows': ['显示方向箭头', 'Show direction arrows'],
  'Show Stroke Vertices': ['显示折线顶点', 'Show stroke vertices'],
  'Show Z Values': ['显示 Z 值', 'Show Z values'],
};

function optionLabel(option: VectorPlotOptionDescriptor, language: Language): string {
  return knownOptionLabels[option.name]?.[language === 'zh' ? 0 : 1] ?? option.label;
}

export function VectorPlotOptionsEditor({
  options,
  values,
  language,
  disabled,
  onChange,
}: {
  options: VectorPlotOptionDescriptor[];
  values: Record<string, string>;
  language: Language;
  disabled: boolean;
  onChange: (propertyName: string, value: string) => void;
}) {
  const enabled = enabledPlotOptionProperties(options, values);
  const zh = language === 'zh';
  if (options.length === 0) {
    return <p className="vector-render-empty-options">{zh ? '当前矢量图层没有可配置的绘图选项。' : 'The selected vector layer has no drawing options.'}</p>;
  }
  return (
    <fieldset className="vector-render-options" disabled={disabled} data-testid="vector-plot-options">
      <legend>{zh ? '当前视图的绘图选项' : 'Drawing options for this view'}</legend>
      <p className="vector-render-muted">
        {zh ? '这些选项由当前真实图层的绘制器提供，只应用于当前视图，不写入工程 .vis。' : 'These options come from the selected layer renderer. They apply to this view only and are not saved in the project .vis file.'}
      </p>
      {options.map(option => {
        const available = enabled.has(option.propertyName);
        const label = optionLabel(option, language);
        const value = values[option.propertyName] ?? option.value;
        const unsupported = !option.isEnabled && !option.enabledIf;
        return (
          <label className="vector-render-option" key={option.propertyName} data-testid={`vector-plot-option-${option.propertyName}`}>
            <span className="vector-render-option-label">
              <strong>{label}</strong>
              <code>{option.name}</code>
              {option.description.trim() && <small>{option.description}</small>}
              {!available && <small className="vector-render-muted">
                {unsupported
                  ? (zh ? '当前绘制器未启用此选项。' : 'This renderer does not enable this option.')
                  : (zh ? `需先满足：${option.enabledIf}` : `Requires: ${option.enabledIf}`)}
              </small>}
            </span>
            {option.kind === 'bool' ? (
              <input
                type="checkbox"
                checked={value === 'true'}
                disabled={disabled || !available}
                aria-label={label}
                onChange={event => onChange(option.propertyName, event.currentTarget.checked ? 'true' : 'false')}
              />
            ) : option.kind === 'enum' && option.enumValues.length > 0 ? (
              <select value={value} disabled={disabled || !available} aria-label={label}
                onChange={event => onChange(option.propertyName, event.currentTarget.value)}>
                {option.enumValues.map(choice => <option key={choice.value} value={choice.value}>{choice.label}</option>)}
              </select>
            ) : (
              <code className="vector-render-unsupported-value">{value}</code>
            )}
          </label>
        );
      })}
    </fieldset>
  );
}
