import type { CatalogTreeNode } from '../CatalogTreeSnapshot';
import { GenericProjectBehaviors } from './GenericProjectBehaviors';

/** ProjectBehaviors.cs registers ExportProjection in the selected project's export group. */
export const ProjectBehaviors = {
  AttachActions_Layer_ContextMenu(node: CatalogTreeNode | null) {
    return node && ['ObservableProject', 'Project', 'ModelSectionContext'].includes(node.sourceClass)
      ? [GenericProjectBehaviors.ExportProjection] : [];
  },
};
