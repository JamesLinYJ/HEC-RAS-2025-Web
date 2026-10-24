import type { Language } from '../../../shared/workbench';
import { ConditionalRuleEditor } from './ConditionalRuleEditor';
import type { VectorSymbologyColumn, VectorSymbologyCondition, VectorSymbologyProperty, VectorSymbologyRuleDraft } from './vectorSymbologyWorkflow';

interface Props {
  rule: VectorSymbologyRuleDraft;
  columns: VectorSymbologyColumn[];
  language: Language;
  disabled?: boolean;
  onConditionAdd: () => void;
  onConditionChange: (conditionID: string, change: Partial<VectorSymbologyCondition>) => void;
  onConditionRemove: (conditionID: string) => void;
}

export function VectorSymbologyPropertyItemWrapper({ rule, columns, language, disabled = false,
  onConditionAdd, onConditionChange, onConditionRemove }: Props) {
  const zh = language === 'zh';
  const atLimit = rule.conditions.length >= 256;

  return (
    <details className="vector-symbology-condition-wrapper" open>
      <summary>{zh ? `条件规则（${rule.conditions.length}）` : `Conditions (${rule.conditions.length})`}</summary>
      <p className="vector-symbology-muted">
        {zh ? 'Host 按以下顺序检查条件，并返回首条匹配的覆写；空白行会按原规则忽略。' : 'The Host checks conditions in order and returns the first match; incomplete rows are ignored by the retained rules.'}
      </p>
      <div className="vector-symbology-condition-list">
        {rule.conditions.map(condition => <ConditionalRuleEditor key={condition.id} condition={condition}
          columns={columns} property={rule.property as VectorSymbologyProperty} language={language} disabled={disabled}
          onChange={change => onConditionChange(condition.id, change)} onRemove={() => onConditionRemove(condition.id)} />)}
      </div>
      <button type="button" className="vector-symbology-secondary-button" disabled={disabled || atLimit} onClick={onConditionAdd}>
        {zh ? (atLimit ? '已达 256 条条件上限' : '添加条件') : (atLimit ? '256-condition limit reached' : 'Add condition')}
      </button>
    </details>
  );
}
