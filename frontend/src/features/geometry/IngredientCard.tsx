import type { Language } from '../../shared/workbench';
import type { GeometryTopologyRecipeSnapshot } from './geometryTypes';

export type RecipeIngredient = GeometryTopologyRecipeSnapshot['stepSchema'][number];

export interface IngredientCardProps {
  language: Language;
  ingredient: RecipeIngredient;
  label: string;
  disabled?: boolean;
  onAdd: (ingredient: RecipeIngredient) => void;
}

/** Source IngredientCard role: add one available source step to the editable Recipe stage. */
export function IngredientCard({ language, ingredient, label, disabled, onAdd }: IngredientCardProps) {
  return <article className="geometry-recipe-ingredient-card">
    <div className="geometry-recipe-ingredient-copy">
      <strong>{label}</strong>
      <span>{ingredient.name}</span>
      {ingredient.aliases.length > 0 ? <small>{language === 'zh' ? '别名' : 'Aliases'}: {ingredient.aliases.join(' · ')}</small> : null}
      <small>{language === 'zh' ? `${ingredient.parameters.length} 个参数` : `${ingredient.parameters.length} parameters`}</small>
    </div>
    <button className="geometry-secondary" type="button" disabled={disabled}
      aria-label={language === 'zh' ? `添加 ${label}` : `Add ${label}`} onClick={() => onAdd(ingredient)}>
      {language === 'zh' ? '添加步骤' : 'Add step'}
    </button>
  </article>;
}

export default IngredientCard;
