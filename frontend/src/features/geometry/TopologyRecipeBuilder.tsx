import { RecipeBuilder, type RecipeBuilderProps } from './RecipeBuilder';

export type TopologyRecipeBuilderProps = RecipeBuilderProps;

/** Source topology-specific binding: connects a selected control-point Recipe to the generic RecipeBuilder. */
export function TopologyRecipeBuilder(props: TopologyRecipeBuilderProps) {
  return <RecipeBuilder {...props} />;
}

export default TopologyRecipeBuilder;
