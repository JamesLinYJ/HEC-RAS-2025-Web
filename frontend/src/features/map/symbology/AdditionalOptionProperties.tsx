import type { Language } from '../../../shared/workbench';
import type { RasterPlotOptionDescriptor } from './rasterSymbologyWorkflow';

interface Props {
  language: Language;
  options: RasterPlotOptionDescriptor[];
  disabled: boolean;
  onChange: (propertyName: string, value: string) => void;
}

const chineseLabels: Record<string, string> = {
  ResampleMode: '重采样方式',
  ShowTiling: '显示栅格瓦片边界',
};

const chineseChoices: Record<string, string> = {
  Nearest: '最近邻',
  Linear: '线性',
  Smooth: '平滑',
  AreaAveraged: '面积平均',
  Hybrid: '混合',
};

export function AdditionalOptionProperties({ language, options, disabled, onChange }: Props) {
  const zh = language === 'zh';
  if (options.length === 0) return null;

  return (
    <details className="raster-symbology-plot-options" data-testid="raster-additional-options">
      <summary>{zh ? '栅格渲染选项' : 'Raster rendering options'} ({options.length})</summary>
      <div className="raster-symbology-plot-option-list">
        {options.map(option => {
          const label = zh ? chineseLabels[option.propertyName] ?? option.label : option.label;
          const controlDisabled = disabled || !option.isEnabled;
          return <div className="raster-symbology-plot-option" key={option.propertyName} title={option.description}>
            {option.kind === 'bool' ? <label className="raster-symbology-check">
              <input type="checkbox" checked={option.value === 'true'} disabled={controlDisabled}
                aria-label={label} data-testid={`raster-plot-option-${option.propertyName}`}
                onChange={event => onChange(option.propertyName, event.target.checked ? 'true' : 'false')} />
              <span>{label}</span>
            </label> : <label className="raster-symbology-field">
              <span>{label}</span>
              <select value={option.value} disabled={controlDisabled} aria-label={label}
                data-testid={`raster-plot-option-${option.propertyName}`}
                onChange={event => onChange(option.propertyName, event.target.value)}>
                {option.enumValues.map(choice => <option key={choice.value} value={choice.value}>
                  {zh ? chineseChoices[choice.value] ?? choice.label : choice.label}
                </option>)}
              </select>
            </label>}
            {!option.isEnabled && option.enabledIf && <small className="raster-symbology-option-disabled">
              {zh ? `需要先启用 ${option.enabledIf}` : `Requires ${option.enabledIf}`}
            </small>}
          </div>;
        })}
      </div>
      <p className="raster-symbology-note">{zh
        ? '这些选项由 Host 栅格渲染器提供；应用后写入当前图层的 .vis 并在重载时重新绘制。'
        : 'These options come from the Host raster renderer, are saved to this layer in .vis, and are reapplied when it is loaded.'}</p>
    </details>
  );
}
