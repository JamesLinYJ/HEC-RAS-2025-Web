import type { Language } from '../../shared/workbench';
import type { GeometryTopologyRecipeSnapshot } from './geometryTypes';
import { RecipeStepParameterBase } from './RecipeStepParameterBase';
import { RecipeStepParameterBoolean } from './RecipeStepParameterBoolean';
import { RecipeStepParameterDouble } from './RecipeStepParameterDouble';
import { RecipeStepParameterInteger } from './RecipeStepParameterInteger';
import { RecipeStepParameterNone } from './RecipeStepParameterNone';

export type RecipeStepSchema = GeometryTopologyRecipeSnapshot['stepSchema'][number];
export interface RecipeStepDraft {
  /** Local display identity; the Host source contract identifies steps by ordered recipe text. */
  id: string;
  name: string;
  parameters: Record<string, string>;
}

export interface RecipeStepCardProps {
  language: Language;
  step: RecipeStepDraft;
  schema?: RecipeStepSchema;
  label: string;
  index: number;
  stepCount: number;
  selected: boolean;
  disabled?: boolean;
  onSelect: () => void;
  onParametersChange: (parameters: Record<string, string>) => void;
  onMove: (direction: -1 | 1) => void;
  onRemove: () => void;
}

function helpURLForStep(name: string): string {
  const base = 'https://www.hec.usace.army.mil/confluence/hecras/advanced-meshing';
  const fragment: Record<string, string> = {
    RemoveFaces: '#AdvancedMeshing-MergeFaces',
    Smooth: '#AdvancedMeshing-SmoothAngles',
    Split: '#AdvancedMeshing-Split',
    SwapEdges: '#AdvancedMeshing-SwapEdges',
  };
  return base + (fragment[name] ?? '');
}

function parameterKey(parameters: Record<string, string>, name: string): string {
  return Object.keys(parameters).find((key) => key.toLocaleLowerCase() === name.toLocaleLowerCase()) ?? name;
}

function finiteValue(value: string | undefined): number | undefined {
  if (value == null || value.trim() === '') return undefined;
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : undefined;
}

function sourceNumber(value: number): string {
  return String(Number(value.toPrecision(15)));
}

function usesPercentThreshold(stepName: string, parameterName: string): boolean {
  const step = stepName.toLocaleLowerCase();
  return parameterName.toLocaleLowerCase() === 'threshold'
    && (step === 'split' || step === 'removefaces' || step === 'removenodes' || step === 'opttopology');
}

function parameterDisplayValue(step: RecipeStepDraft, parameterName: string, fallback: string): string {
  const key = parameterKey(step.parameters, parameterName);
  const rawValue = step.parameters[key] ?? fallback;
  const parsed = finiteValue(rawValue);
  if (parsed == null) return rawValue;
  if (usesPercentThreshold(step.name, parameterName)) return sourceNumber(parsed * 100);
  if (step.name.toLocaleLowerCase() === 'opttopology' && parameterName.toLocaleLowerCase() === 'deltacost') {
    const threshold = finiteValue(step.parameters[parameterKey(step.parameters, 'threshold')]);
    // The source RecipeBuilder binds DeltaThreshold = deltaCost / threshold. With a zero
    // threshold the source UI has no finite value to display, so keep the raw value visible.
    return threshold == null || threshold === 0 ? rawValue : sourceNumber(parsed / threshold);
  }
  return rawValue;
}

function changedSourceParameters(step: RecipeStepDraft, parameterName: string, displayValue: string): Record<string, string> {
  const next = { ...step.parameters };
  const key = parameterKey(next, parameterName);
  const parsed = finiteValue(displayValue);
  if (!usesPercentThreshold(step.name, parameterName)
      && !(step.name.toLocaleLowerCase() === 'opttopology' && parameterName.toLocaleLowerCase() === 'deltacost')) {
    next[key] = displayValue;
    return next;
  }

  if (parsed == null) {
    next[key] = displayValue;
    return next;
  }

  if (usesPercentThreshold(step.name, parameterName)) {
    const previousThreshold = finiteValue(next[key]);
    const deltaKey = parameterKey(next, 'deltaCost');
    const previousDeltaCost = finiteValue(next[deltaKey]);
    const nextThreshold = parsed / 100;
    next[key] = sourceNumber(nextThreshold);
    if (step.name.toLocaleLowerCase() === 'opttopology' && previousThreshold != null
        && previousThreshold !== 0 && previousDeltaCost != null) {
      next[deltaKey] = sourceNumber(nextThreshold * (previousDeltaCost / previousThreshold));
    }
    return next;
  }

  const threshold = finiteValue(next[parameterKey(next, 'threshold')]);
  // The editor disables DeltaThreshold while threshold is zero; this guard also
  // preserves the raw value should the component receive a stale event.
  next[key] = threshold == null || threshold === 0 ? displayValue : sourceNumber(parsed * threshold);
  return next;
}

function parameterLabel(language: Language, step: RecipeStepDraft, parameterName: string): string | undefined {
  const key = parameterName.toLocaleLowerCase();
  if (key === 'deltacost' && step.name.toLocaleLowerCase() === 'opttopology') return language === 'zh' ? '增量阈值' : 'Delta threshold';
  if (usesPercentThreshold(step.name, parameterName)) return language === 'zh' ? '阈值' : 'Threshold';
  if (language !== 'zh') return undefined;
  const labels: Record<string, string> = {
    count: '迭代次数', scaled: '缩放', sliding: '允许滑移', scaler: '缩放系数', method: '方法',
    smoothmethod: '平滑方法', alpha: 'Alpha', jitter: '扰动量', nudgeconvex: '凸性调整',
    merge: '合并', collapse: '塌缩', perimeter: '周长约束', skew: '偏斜阈值', cellarea: '单元面积阈值',
    facesmooth: '面平滑阈值', facelength: '面长度阈值',
  };
  return labels[key];
}

/** Ordered Recipe stage row with the source card's select/remove behavior and explicit reordering. */
export function RecipeStepCard({ language, step, schema, label, index, stepCount, selected, disabled, onSelect, onParametersChange, onMove, onRemove }: RecipeStepCardProps) {
  const sourceType = (value: string): string => value.split(/[.+]/).at(-1)?.toLowerCase() ?? value.toLowerCase();
  return <li className={`geometry-recipe-step${selected ? ' is-selected' : ''}`}>
    <header>
      <button className="geometry-recipe-step-select" type="button" aria-pressed={selected} disabled={disabled} onClick={onSelect}>
        <strong>{index + 1}. {label}</strong><small>{step.name}</small>
      </button>
      <div className="geometry-toolbar-group">
        <button className="geometry-secondary" type="button" disabled={disabled || index === 0}
          aria-label={language === 'zh' ? `上移步骤 ${index + 1}` : `Move step ${index + 1} up`} onClick={() => onMove(-1)}>
          {language === 'zh' ? '上移' : 'Move up'}
        </button>
        <button className="geometry-secondary" type="button" disabled={disabled || index >= stepCount - 1}
          aria-label={language === 'zh' ? `下移步骤 ${index + 1}` : `Move step ${index + 1} down`} onClick={() => onMove(1)}>
          {language === 'zh' ? '下移' : 'Move down'}
        </button>
        <button className="geometry-secondary" type="button" disabled={disabled}
          aria-label={language === 'zh' ? `移除步骤 ${index + 1}` : `Remove step ${index + 1}`} onClick={onRemove}>
          {language === 'zh' ? '移除' : 'Remove'}
        </button>
        <a className="geometry-secondary geometry-recipe-help" href={helpURLForStep(step.name)} target="_blank" rel="noreferrer">
          {language === 'zh' ? '说明' : 'Help'}
        </a>
      </div>
    </header>
    {!schema ? <div className="geometry-inline-help">{language === 'zh' ? `当前 Host 契约没有“${step.name}”步骤定义；保留原文，无法通过步骤表单修改。` : `The current Host contract has no schema for “${step.name}”; raw text is preserved and this step is read-only.`}</div> : schema.parameters.length === 0 ? (
      <RecipeStepParameterNone language={language} stepName={step.name} />
    ) : (
      <div className="geometry-recipe-parameters">
        {schema.parameters.map((parameter) => {
          const value = parameterDisplayValue(step, parameter.name, parameter.defaultValue);
          const type = sourceType(parameter.sourceType);
          const onChange = (next: string): void => onParametersChange(changedSourceParameters(step, parameter.name, next));
          const label = parameterLabel(language, step, parameter.name);
          const unit = usesPercentThreshold(step.name, parameter.name) ? '%' : undefined;
          const thresholdIsZero = step.name.toLocaleLowerCase() === 'opttopology'
            && parameter.name.toLocaleLowerCase() === 'deltacost'
            && finiteValue(step.parameters[parameterKey(step.parameters, 'threshold')]) === 0;
          const fieldDisabled = disabled || thresholdIsZero;
          if (type === 'bool' || type === 'boolean') return <RecipeStepParameterBoolean key={parameter.name} language={language} name={parameter.name} label={label} sourceType={parameter.sourceType} value={value} disabled={fieldDisabled} onChange={onChange} />;
          if (type === 'int' || type === 'integer') return <RecipeStepParameterInteger key={parameter.name} language={language} name={parameter.name} label={label} sourceType={parameter.sourceType} unit={unit} value={value} disabled={fieldDisabled} onChange={onChange} />;
          if (type === 'double' || type === 'float' || type === 'decimal') return <RecipeStepParameterDouble key={parameter.name} language={language} name={parameter.name} label={label} sourceType={parameter.sourceType} unit={unit} value={value} disabled={fieldDisabled} onChange={onChange} />;
          return <RecipeStepParameterBase key={parameter.name} language={language} name={parameter.name} sourceType={parameter.sourceType}>
            <input aria-label={parameter.name} value={value} readOnly disabled />
          </RecipeStepParameterBase>;
        })}
      </div>
    )}
  </li>;
}

export default RecipeStepCard;
