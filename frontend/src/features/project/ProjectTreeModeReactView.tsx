import type { RasCatalogProps } from './RasCatalog';
import { RasCatalog } from './RasCatalog';

export type { ProjectTreeContextMenu } from './RasCatalog';
export type ProjectTreeModeReactViewProps = RasCatalogProps;

/** Compatibility entry used by ProjectTreeMode; the catalog controls live in their matching components. */
export function ProjectTreeModeReactView(props: ProjectTreeModeReactViewProps) {
  return <RasCatalog {...props} />;
}
