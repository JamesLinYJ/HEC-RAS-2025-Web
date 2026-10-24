import type { Language } from '../../../shared/workbench';
import { vectorPropertyLabel } from './vectorSymbologyWorkflow';
import type { VectorSymbologyColumn, VectorSymbologyCondition, VectorSymbologyRuleDraft } from './vectorSymbologyWorkflow';
import { VectorSymbologyPropertyItemWrapper } from './VectorSymbologyPropertyItemWrapper';

interface Props {
  rule: VectorSymbologyRuleDraft;
  columns: VectorSymbologyColumn[];
  language: Language;
  disabled?: boolean;
  onRemove: () => void;
  onConditionAdd: () => void;
  onConditionChange: (conditionID: string, change: Partial<VectorSymbologyCondition>) => void;
  onConditionRemove: (conditionID: string) => void;
}

export function VectorSymbologyPropertyItem({ rule, columns, language, disabled = false, onRemove,
  onConditionAdd, onConditionChange, onConditionRemove }: Props) {
  const zh = language === 'zh';
  return (
    <article className="vector-symbology-property-item">
      <header className="vector-symbology-property-header">
        <div>
          <strong>{vectorPropertyLabel(rule.property, language)}</strong>
          <code>{rule.property}</code>
          <div className="vector-symbology-definition-id">{zh ? '定义 ID' : 'Definition ID'}: <code>{rule.definitionID}</code></div>
        </div>
        <button type="button" className="vector-symbology-secondary-button" disabled={disabled} onClick={onRemove}>
          {zh ? '移除此属性' : 'Remove property'}
        </button>
      </header>
      <VectorSymbologyPropertyItemWrapper rule={rule} columns={columns} language={language} disabled={disabled}
        onConditionAdd={onConditionAdd} onConditionChange={onConditionChange} onConditionRemove={onConditionRemove} />
    </article>
  );
}
