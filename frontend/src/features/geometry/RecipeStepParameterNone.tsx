import type { Language } from '../../shared/workbench';

export interface RecipeStepParameterNoneProps {
  language: Language;
  stepName: string;
}

/** The source UI's no-parameter template; visibility is based on the Host's empty schema. */
export function RecipeStepParameterNone({ language, stepName }: RecipeStepParameterNoneProps) {
  return <div className="geometry-inline-help geometry-recipe-parameter-none" role="note">
    {language === 'zh' ? `“${stepName}”步骤没有参数。` : `The “${stepName}” step has no parameters.`}
  </div>;
}

export default RecipeStepParameterNone;
