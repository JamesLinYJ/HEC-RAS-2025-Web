import type { Language } from '../../shared/workbench';
import type { CatalogTreeNode } from './CatalogTreeSnapshot';
import { RasCatalogSummary } from './RasCatalogSummary';

export function TerrainCatalogSummary({ language, node, icon, typeLabel }: {
  language: Language; node: CatalogTreeNode; icon: string; typeLabel: string;
}) {
  // The WinUI TerrainCatalogSummary binds the common layer metadata. Terrain
  // inputs are represented only by actual child nodes supplied by the catalog.
  return <RasCatalogSummary language={language} node={node} icon={icon} typeLabel={typeLabel} />;
}
