import type { Language } from '../../../shared/workbench';
import { conditionColumnChoices, conditionOperators, operatorsForColumn } from './vectorSymbologyWorkflow';
import type { VectorSymbologyColumn, VectorSymbologyCondition, VectorSymbologyProperty } from './vectorSymbologyWorkflow';

interface Props {
  condition: VectorSymbologyCondition;
  columns: VectorSymbologyColumn[];
  property: VectorSymbologyProperty;
  language: Language;
  disabled?: boolean;
  onChange: (change: Partial<VectorSymbologyCondition>) => void;
  onRemove: () => void;
}

export function ConditionalRuleEditor({ condition, columns, property, language, disabled = false, onChange, onRemove }: Props) {
  const zh = language === 'zh';
  const selectableColumns = conditionColumnChoices(columns);
  const selectedColumn = selectableColumns.find(column => column.name === condition.column);
  const allowedOperators = selectedColumn ? operatorsForColumn(selectedColumn.type) : conditionOperators.numeric;

  return (
    <div className="vector-symbology-condition-row">
      <label className="vector-symbology-field">
        <span>{zh ? '属性列' : 'Attribute column'}</span>
        <select value={selectedColumn?.id ?? ''} disabled={disabled}
          onChange={event => {
            const column = columns.find(candidate => candidate.id === event.currentTarget.value);
            if (column && !selectableColumns.some(candidate => candidate.id === column.id)) {
              onChange({ column: '', operator: conditionOperators.numeric[0] });
              return;
            }
            if (!column) { onChange({ column: '', operator: conditionOperators.numeric[0] }); return; }
            const operators = operatorsForColumn(column.type);
            onChange({ column: column.name, operator: operators.includes(condition.operator) ? condition.operator : operators[0] });
          }}>
          <option value="">{zh ? '选择唯一且受支持的源列' : 'Choose a unique supported source column'}</option>
          {columns.map(column => {
            const selectable = selectableColumns.some(candidate => candidate.id === column.id);
            const duplicateName = columns.filter(candidate => candidate.name === column.name).length > 1;
            const reason = duplicateName ? (zh ? '同名列不可用' : 'duplicate name')
              : !selectable ? (zh ? '类型不可用' : 'unsupported type') : '';
            return <option key={column.id} value={column.id} disabled={!selectable}>
              {column.name} · {column.type} · #{column.ordinal}{reason ? ` · ${reason}` : ''}
            </option>;
          })}
        </select>
        {columns.some(column => columns.filter(candidate => candidate.name === column.name).length > 1 || !selectableColumns.some(candidate => candidate.id === column.id)) &&
          <small className="vector-symbology-field-note">{zh ? '同名列和 Host 不支持的列类型已禁用，避免发送无法定位的列名。' : 'Duplicate names and unsupported types are disabled so the Host receives an unambiguous column name.'}</small>}
      </label>
      <label className="vector-symbology-field vector-symbology-operator-field">
        <span>{zh ? '运算符' : 'Operator'}</span>
        <select value={condition.operator} disabled={disabled} onChange={event => onChange({ operator: event.currentTarget.value })}>
          {allowedOperators.map(operator => <option key={operator} value={operator}>{operator}</option>)}
        </select>
      </label>
      <label className="vector-symbology-field">
        <span>{zh ? '阈值' : 'Threshold'}</span>
        <input value={condition.threshold} disabled={disabled} onChange={event => onChange({ threshold: event.currentTarget.value })}
          placeholder={zh ? (selectedColumn?.type === 'string' ? '文本值' : '数值') : (selectedColumn?.type === 'string' ? 'Text value' : 'Number')} />
      </label>
      <label className="vector-symbology-field">
        <span>{zh ? `匹配后 ${property}` : `Result value for ${property}`}</span>
        <input value={condition.resultValue} disabled={disabled} onChange={event => onChange({ resultValue: event.currentTarget.value })}
          inputMode={property === 'PenWidth' || property === 'PointSize' ? 'decimal' : 'text'}
          placeholder={zh ? (property === 'PenColor' || property === 'FillColor' ? '#RRGGBB 或 C# 颜色名' : '由 C# Host 校验') : 'Validated by the C# Host'} />
      </label>
      <button type="button" className="vector-symbology-icon-button" disabled={disabled} onClick={onRemove}
        aria-label={zh ? '删除条件' : 'Remove condition'} title={zh ? '删除条件' : 'Remove condition'}>×</button>
    </div>
  );
}
