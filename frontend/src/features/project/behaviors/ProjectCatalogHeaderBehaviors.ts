import { GenericProjectBehaviors } from './GenericProjectBehaviors';

/** ProjectCatalogHeaderBehaviors.cs registers the same action against the current tree's Project. */
export const ProjectCatalogHeaderBehaviors = {
  GetContextMenuActions: () => [GenericProjectBehaviors.ExportProjection],
};
