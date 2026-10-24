import type { Language } from '../../../shared/workbench';
import type { AssociationOption } from './AssociationTypes';

export function BoundaryConditionPicker({ language, options, value, onChange, disabled = false }: {
  language: Language;
  options: AssociationOption[];
  value: string;
  onChange: (path: string) => void;
  disabled?: boolean;
}) {
  return (
    <label className="model-association-field">
      <span>{language === 'zh' ? '边界条件' : 'Boundary condition'} <b>*</b></span>
      <select data-testid="association-boundary-condition" value={value} disabled={disabled || options.length === 0} onChange={(event) => onChange(event.target.value)}>
        <option value="">{language === 'zh' ? '选择工程边界条件' : 'Select project boundary condition'}</option>
        {options.map((option) => <option key={option.path} value={option.path} title={`${option.path}\nSHA-256 ${option.sha256}`}>{option.name} · {option.path}</option>)}
      </select>
      {options.length === 0 && <small>{language === 'zh' ? '工程目录中没有可用边界条件。' : 'No project boundary condition is available.'}</small>}
    </label>
  );
}
