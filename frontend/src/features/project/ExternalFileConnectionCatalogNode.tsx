import type { CatalogTreeNode } from './CatalogTreeSnapshot';

/** File-connection node glyph uses the exact AbsolutePath metadata as its accessible title. */
export function ExternalFileConnectionCatalogNode({ node, icon }: { node: CatalogTreeNode; icon: string }) {
  const absolutePath = node.properties?.find((property) => property.id === 'AbsolutePath')?.value;
  return <span className="project-tree-node-icon" title={absolutePath || node.name} aria-hidden="true">{icon}</span>;
}
