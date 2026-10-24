import type { Language } from '../../shared/workbench';
import type { CatalogTreeNode } from './CatalogTreeSnapshot';
import { RasCatalogSummary } from './RasCatalogSummary';

export function GeometryCatalogSummary({ language, node, icon, typeLabel }: {
  language: Language; node: CatalogTreeNode; icon: string; typeLabel: string;
}) {
  // The WinUI GeometryCatalogSummary binds the common layer name, filename,
  // description, dates and layer errors; those common fields are rendered by
  // RasCatalogSummary. Geometry-specific source details remain in the tree.
  return <RasCatalogSummary language={language} node={node} icon={icon} typeLabel={typeLabel} />;
}
