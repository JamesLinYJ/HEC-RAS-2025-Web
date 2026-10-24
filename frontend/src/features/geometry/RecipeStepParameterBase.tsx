import type { ReactNode } from 'react';
import type { Language } from '../../shared/workbench';
import { gfield } from './geometryText';

export interface RecipeStepParameterBaseProps {
  language: Language;
  name: string;
  label?: string;
  sourceType: string;
  unit?: string;
  description?: string;
  children: ReactNode;
}

/** Shared label and source type presentation for one C# recipe-step parameter. */
export function RecipeStepParameterBase({ language, name, label: explicitLabel, sourceType, unit, description, children }: RecipeStepParameterBaseProps) {
  const sourceLabel = gfield(language, name, name);
  const label = explicitLabel ?? (sourceLabel === '字段' ? name : sourceLabel);
  return <label className="geometry-form-field geometry-recipe-parameter">
    <span>{label}{unit ? <small>{unit}</small> : null}<small>{sourceType}</small></span>
    {children}
    {description ? <small className="geometry-recipe-parameter-description">{description}</small> : null}
  </label>;
}

export default RecipeStepParameterBase;
