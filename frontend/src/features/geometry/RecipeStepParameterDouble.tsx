import type { Language } from '../../shared/workbench';
import { RecipeStepParameterBase } from './RecipeStepParameterBase';

export interface RecipeStepParameterDoubleProps {
  language: Language;
  name: string;
  label?: string;
  sourceType: string;
  unit?: string;
  value: string;
  disabled?: boolean;
  onChange: (value: string) => void;
}

/** Floating-point recipe values use the source field type and retain typed text until inspection. */
export function RecipeStepParameterDouble({ language, name, label, sourceType, unit, value, disabled, onChange }: RecipeStepParameterDoubleProps) {
  return <RecipeStepParameterBase language={language} name={name} label={label} sourceType={sourceType} unit={unit}>
    <input type="number" step="any" inputMode="decimal" aria-label={name} value={value} disabled={disabled}
      onChange={(event) => onChange(event.target.value)} />
  </RecipeStepParameterBase>;
}

export default RecipeStepParameterDouble;
