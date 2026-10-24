import type { Language } from '../../../shared/workbench';
import type { AttributeTableMutation } from './attributeTableTypes';

type NewColumnType = Extract<AttributeTableMutation, { operation: 'addColumn' }>['type'];

interface Props {
  language: Language;
  name: string;
  type: NewColumnType;
  existingNames: string[];
  onNameChange: (value: string) => void;
  onTypeChange: (value: NewColumnType) => void;
}

export function AddAttributeTableColumnControl({ language, name, type, existingNames, onNameChange, onTypeChange }: Props) {
  const zh = language === 'zh';
  const normalized = name.trim().toLocaleLowerCase();
  const duplicate = Boolean(normalized) && existingNames.some(existing => existing.trim().toLocaleLowerCase() === normalized);
  return <div className="attribute-column-form">
    <label><span>{zh ? '新字段名称' : 'New field name'}</span>
      <input autoFocus value={name} maxLength={128} onChange={event => onNameChange(event.target.value)} aria-invalid={!normalized || duplicate} />
    </label>
    <label><span>{zh ? '字段类型' : 'Field type'}</span><select value={type} onChange={event => onTypeChange(event.target.value as NewColumnType)}>
      <option value="String">{zh ? '文本（String）' : 'Text (String)'}</option>
      <option value="Integer">{zh ? '整数（Integer）' : 'Integer'}</option>
      <option value="Numeric">{zh ? '数值（Numeric）' : 'Numeric'}</option>
      <option value="Boolean">{zh ? '布尔（Boolean）' : 'Boolean'}</option>
    </select></label>
    {!normalized && <small className="attribute-validation">{zh ? '字段名不能为空。' : 'Enter a field name.'}</small>}
    {duplicate && <small className="attribute-validation">{zh ? '字段名与已有字段重复；Host 仍会执行最终名称校验。' : 'The field name matches an existing field; the Host performs final name validation.'}</small>}
  </div>;
}
