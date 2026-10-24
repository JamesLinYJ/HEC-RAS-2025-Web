import type { Language } from '../../shared/workbench';
import { RecipeStepParameterBase } from './RecipeStepParameterBase';

export interface RecipeStepParameterIntegerProps {
  language: Language;
  name: string;
  label?: string;
  sourceType: string;
  unit?: string;
  value: string;
  disabled?: boolean;
  onChange: (value: string) => void;
}

/** Integer source parameters are rendered with integer step guidance and server validation. */
export function RecipeStepParameterInteger({ language, name, label, sourceType, unit, value, disabled, onChange }: RecipeStepParameterIntegerProps) {
  return <RecipeStepParameterBase language={language} name={name} label={label} sourceType={sourceType} unit={unit}>
    <input type="number" step="1" inputMode="numeric" aria-label={name} value={value} disabled={disabled}
      onChange={(event) => onChange(event.target.value)} />
  </RecipeStepParameterBase>;
}

export default RecipeStepParameterInteger;
