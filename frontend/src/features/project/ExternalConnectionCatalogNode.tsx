import type { CatalogTreeNode } from './CatalogTreeSnapshot';

/** Catalog row glyph retains the absolute source path as its accessible title. */
export function ExternalConnectionCatalogNode({ node, icon }: { node: CatalogTreeNode; icon: string }) {
  const absolutePath = node.properties?.find((property) => property.id === 'AbsolutePath')?.value;
  const title = absolutePath ? `${node.name}\n${absolutePath}` : node.name;
  return <span className="project-tree-node-icon project-tree-external-connection-icon" title={title} aria-hidden="true">{icon}</span>;
}
