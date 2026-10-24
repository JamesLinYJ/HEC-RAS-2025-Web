import type { Language } from '../../shared/workbench';
import type { GeometryTopologyFilterClause, GeometryTopologyPropertyDescriptor } from './geometryTypes';

export interface GeometryTagFilterDraft extends GeometryTopologyFilterClause {
  key: string;
}

export interface TagFilterItemControlProps {
  language: Language;
  filterItem: GeometryTagFilterDraft;
  tagDefinitions: Array<{ id: string; type: string }>;
  propertyDescriptors: GeometryTopologyPropertyDescriptor[];
  disabled?: boolean;
  onChange: (next: GeometryTagFilterDraft) => void;
  onRemove: (key: string) => void;
}

const baseOperators = ['IsNotBlank', 'IsBlank'] as const;
const textOperators = [...baseOperators, 'Equals', 'NotEquals', 'Contains', 'DoesNotContain'] as const;
const compareOperators = [...baseOperators, 'Equals', 'NotEquals', 'LessThan', 'AtMost', 'GreaterThan', 'AtLeast'] as const;
type Operator = typeof compareOperators[number] | typeof textOperators[number];

function operators(type: string | undefined, isEnum = false): readonly Operator[] {
  if (!type) return baseOperators;
  const normalized = type.toLowerCase().replace(/^system\./, '');
  if (normalized === 'string' || normalized === 'char') return textOperators;
  if (isEnum || ['bool', 'boolean', 'byte', 'sbyte', 'short', 'int16', 'ushort', 'uint16', 'int', 'int32', 'integer', 'uint', 'uint32', 'long', 'int64', 'ulong', 'uint64', 'single', 'float', 'double', 'decimal', 'numeric'].includes(normalized))
    return compareOperators;
  return baseOperators;
}

function booleanSource(type: string | undefined): boolean {
  return ['bool', 'boolean', 'system.boolean'].includes(type?.toLowerCase() ?? '');
}

function numericSource(type: string | undefined, isEnum = false): boolean {
  if (!type || isEnum) return false;
  const normalized = type.toLowerCase().replace(/^system\./, '');
  return ['byte', 'sbyte', 'short', 'int16', 'ushort', 'uint16', 'int', 'int32', 'integer', 'uint', 'uint32', 'long', 'int64', 'ulong', 'uint64', 'single', 'float', 'double', 'decimal', 'numeric'].includes(normalized);
}

function operatorLabel(language: Language, operator: Operator): string {
  if (language !== 'zh') return ({ IsNotBlank: 'is not blank', IsBlank: 'is blank', Equals: 'equals', NotEquals: 'does not equal', Contains: 'contains', DoesNotContain: 'does not contain', GreaterThan: 'greater than', AtLeast: 'at least', LessThan: 'less than', AtMost: 'at most' } as const)[operator];
  return ({ IsNotBlank: '不为空', IsBlank: '为空', Equals: '等于', NotEquals: '不等于', Contains: '包含', DoesNotContain: '不包含', GreaterThan: '大于', AtLeast: '大于等于', LessThan: '小于', AtMost: '小于等于' } as const)[operator];
}

function tagTypeLabel(language: Language, type: string): string {
  if (language !== 'zh') return type;
  return ({ String: '文本', Integer: '整数', Numeric: '数值', Boolean: '布尔值' } as Record<string, string>)[type] ?? type;
}

function takesValue(operator: string): boolean { return operator !== 'IsBlank' && operator !== 'IsNotBlank'; }

/** One original TagFilterItemControl clause with source-typed operator choices. */
export function TagFilterItemControl({ language, filterItem, tagDefinitions, propertyDescriptors, disabled = false, onChange, onRemove }: TagFilterItemControlProps) {
  const selectedTag = tagDefinitions.find((definition) => definition.id === filterItem.tagDefinitionID);
  const selectedProperty = propertyDescriptors.find((descriptor) => descriptor.columnName === filterItem.propertyColumnID);
  const availableOperators = operators(filterItem.type === 'Tag' ? selectedTag?.type : selectedProperty?.sourceType, selectedProperty?.isEnum);
  const currentOperator = availableOperators.includes(filterItem.operator as Operator) ? filterItem.operator as Operator : 'IsNotBlank';
  const update = (patch: Partial<GeometryTagFilterDraft>): void => onChange({ ...filterItem, ...patch });
  const valueSourceType = selectedTag?.type ?? selectedProperty?.sourceType;
  const isBoolean = booleanSource(valueSourceType);
  const numericValue = selectedTag ? selectedTag.type === 'Integer' || selectedTag.type === 'Numeric'
    : numericSource(selectedProperty?.sourceType, selectedProperty?.isEnum);

  return <div className="geometry-filter-row">
    <label className="geometry-form-field">{language === 'zh' ? '筛选来源' : 'Filter source'}
      <select value={filterItem.type} disabled={disabled} onChange={(event) => update({ type: event.target.value as 'Tag' | 'Property', operator: 'IsNotBlank' })}>
        <option value="Tag">{language === 'zh' ? '标签' : 'Tag'}</option><option value="Property">{language === 'zh' ? '属性' : 'Property'}</option>
      </select>
    </label>
    {filterItem.type === 'Tag' ? <label className="geometry-form-field">{language === 'zh' ? '标签定义' : 'Tag definition'}
      <select value={filterItem.tagDefinitionID ?? ''} disabled={disabled} onChange={(event) => update({ tagDefinitionID: event.target.value || null, operator: 'IsNotBlank' })}>
        <option value="">{language === 'zh' ? '选择标签' : 'Select tag'}</option>
        {tagDefinitions.map((definition) => <option value={definition.id} key={definition.id}>{definition.id} · {tagTypeLabel(language, definition.type)}</option>)}
      </select>
    </label> : <label className="geometry-form-field">{language === 'zh' ? '属性字段' : 'Property'}
      <select value={filterItem.propertyColumnID ?? ''} disabled={disabled} onChange={(event) => update({ propertyColumnID: event.target.value || null, operator: 'IsNotBlank' })}>
        <option value="">{propertyDescriptors.length ? (language === 'zh' ? '选择属性' : 'Select property') : (language === 'zh' ? '先读取属性字段' : 'Read property fields first')}</option>
        {propertyDescriptors.map((descriptor) => <option value={descriptor.columnName} key={descriptor.columnName}>{descriptor.name}</option>)}
      </select>
    </label>}
    <label className="geometry-form-field">{language === 'zh' ? '条件' : 'Condition'}
      <select value={currentOperator} disabled={disabled} onChange={(event) => update({ operator: event.target.value as Operator })}>
        {availableOperators.map((operator) => <option key={operator} value={operator}>{operatorLabel(language, operator)}</option>)}
      </select>
    </label>
    {takesValue(currentOperator) && <label className="geometry-form-field">{language === 'zh' ? '匹配值' : 'Value'}
      {isBoolean ? <select value={filterItem.value ?? ''} disabled={disabled} onChange={(event) => update({ value: event.target.value === '' ? null : event.target.value })}>
        <option value="">{language === 'zh' ? '选择布尔值' : 'Select Boolean'}</option><option value="true">{language === 'zh' ? '是' : 'True'}</option><option value="false">{language === 'zh' ? '否' : 'False'}</option>
      </select> : <input type={numericValue ? 'number' : 'text'} step={selectedTag?.type === 'Integer' ? '1' : 'any'} value={filterItem.value ?? ''} disabled={disabled}
        onChange={(event) => update({ value: event.target.value === '' ? null : event.target.value })} />}
    </label>}
    <button className="geometry-secondary" type="button" disabled={disabled} onClick={() => onRemove(filterItem.key)}>{language === 'zh' ? '移除' : 'Remove'}</button>
  </div>;
}

export default TagFilterItemControl;
