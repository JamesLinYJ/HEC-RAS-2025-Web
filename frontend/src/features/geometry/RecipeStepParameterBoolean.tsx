import type { Language } from '../../shared/workbench';
import { gt } from './geometryText';
import { RecipeStepParameterBase } from './RecipeStepParameterBase';

export interface RecipeStepParameterBooleanProps {
  language: Language;
  name: string;
  label?: string;
  sourceType?: string;
  value: string;
  disabled?: boolean;
  onChange: (value: string) => void;
}

/** Boolean source parameters remain text-compatible with the Recipe grammar. */
export function RecipeStepParameterBoolean({ language, name, label, sourceType = 'bool', value, disabled, onChange }: RecipeStepParameterBooleanProps) {
  return <RecipeStepParameterBase language={language} name={name} label={label} sourceType={sourceType}>
    <select aria-label={name} value={value.toLowerCase() === 'true' ? 'true' : 'false'} disabled={disabled}
      onChange={(event) => onChange(event.target.value)}>
      <option value="true">{gt(language, 'yes')}</option>
      <option value="false">{gt(language, 'no')}</option>
    </select>
  </RecipeStepParameterBase>;
}

export default RecipeStepParameterBoolean;
