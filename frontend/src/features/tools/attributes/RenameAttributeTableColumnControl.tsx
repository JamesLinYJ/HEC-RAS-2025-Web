import type { Language } from '../../../shared/workbench';

interface Props {
  language: Language;
  originalName: string;
  name: string;
  existingNames: string[];
  onNameChange: (value: string) => void;
}

export function RenameAttributeTableColumnControl({ language, originalName, name, existingNames, onNameChange }: Props) {
  const zh = language === 'zh';
  const normalized = name.trim().toLocaleLowerCase();
  const duplicate = Boolean(normalized) && existingNames.some(existing => existing.trim().toLocaleLowerCase() === normalized);
  return <div className="attribute-column-form">
    <label><span>{zh ? '原字段名称' : 'Original field name'}</span><input value={originalName} readOnly /></label>
    <label><span>{zh ? '新字段名称' : 'New field name'}</span>
      <input autoFocus value={name} maxLength={128} onChange={event => onNameChange(event.target.value)} aria-invalid={!normalized || duplicate} />
    </label>
    {!normalized && <small className="attribute-validation">{zh ? '字段名不能为空。' : 'Enter a field name.'}</small>}
    {duplicate && <small className="attribute-validation">{zh ? '字段名与另一字段重复；Host 仍会执行最终名称校验。' : 'The field name matches another field; the Host performs final name validation.'}</small>}
  </div>;
}
