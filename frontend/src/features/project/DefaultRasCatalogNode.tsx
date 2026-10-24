import type { CSSProperties, MouseEvent } from 'react';
import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import type { CatalogTreeNode } from './CatalogTreeSnapshot';
import { localizeProjectTreeSectionTitle, localizeProjectTreeSourceClass, localizeProjectTreeStatus } from './ProjectTreeModeSectionTitles';
import type { ProjectTreeModeName } from './ProjectTreeModeVM';
import { ExternalConnectionCatalogNode } from './ExternalConnectionCatalogNode';
import { ExternalFileConnectionCatalogNode } from './ExternalFileConnectionCatalogNode';
import { ResultCatalogNode } from './ResultCatalogNode';
import { LayerErrorIndicator } from './LayerErrorIndicator';

export function catalogNodeIcon(node: CatalogTreeNode): string {
  if (node.children?.length) return '▱';
  const value = `${node.sourceClass} ${node.name}`.toLowerCase();
  if (value.includes('geometry')) return '⌁';
  if (value.includes('terrain')) return '▧';
  if (value.includes('boundary')) return '⇢';
  if (value.includes('plan')) return '≋';
  if (value.includes('result')) return '◒';
  if (value.includes('classification')) return '▦';
  return '◇';
}

export function catalogNodeTypeLabel(language: Language, node: CatalogTreeNode): string {
  return localizeProjectTreeSourceClass(language, node.sourceClass);
}

export interface DefaultRasCatalogNodeProps {
  node: CatalogTreeNode;
  depth: number;
  language: Language;
  collapsedIds: Set<string>;
  selectedNodeId: string | null;
  selectedIds: Set<string>;
  mode: ProjectTreeModeName;
  onToggle: (node: CatalogTreeNode) => void;
  onSelect: (event: MouseEvent<HTMLButtonElement>, node: CatalogTreeNode) => void;
  onOpen: (node: CatalogTreeNode) => void;
  onContextMenu: (event: MouseEvent<HTMLButtonElement>, node: CatalogTreeNode) => void;
}

/** Recursive catalog row preserving select, expand, context-menu, and double-click events. */
export function DefaultRasCatalogNode({
  node,
  depth,
  language,
  collapsedIds,
  selectedNodeId,
  selectedIds,
  mode,
  onToggle,
  onSelect,
  onOpen,
  onContextMenu,
}: DefaultRasCatalogNodeProps) {
  const hasChildren = (node.children?.length ?? 0) > 0;
  const isGroup = !node.assetID && hasChildren;
  const collapsed = collapsedIds.has(node.id);
  const selected = selectedNodeId === node.id;
  const multiSelected = selectedIds.has(`${mode}:${node.id}`);
  const visibleName = node.localizeName ? localizeProjectTreeSectionTitle(language, node.name) : node.name;
  const icon = catalogNodeIcon(node);

  return (
    <>
      <div className={`project-tree-row ${selected ? 'is-selected' : ''} ${multiSelected ? 'is-multi-selected' : ''}`} style={{ '--tree-depth': depth } as CSSProperties}>
        <button className="project-tree-expander" type="button" aria-label={hasChildren ? (collapsed ? text(language, '展开', 'Expand') : text(language, '折叠', 'Collapse')) : undefined} disabled={!hasChildren} onClick={() => onToggle(node)}>{hasChildren ? (collapsed ? '›' : '⌄') : ''}</button>
        <button className="project-tree-node" type="button" aria-selected={selected} onClick={(event) => onSelect(event, node)} onDoubleClick={() => node.assetID && onOpen(node)} onContextMenu={(event) => onContextMenu(event, node)}>
          {node.sourceClass === 'ExternalFileConnectionCatalogNode'
            ? <ExternalFileConnectionCatalogNode node={node} icon={icon} />
            : node.sourceClass === 'ExternalConnectionCatalogNode'
              ? <ExternalConnectionCatalogNode node={node} icon={icon} />
            : <span className={`project-tree-node-icon ${isGroup ? 'is-group' : ''}`}>{icon}</span>}
          <span className="project-tree-node-name" title={node.name}>{visibleName}</span>
          {node.diagnosticStatus && <span className={`project-diagnostic-dot is-${node.diagnosticStatus.toLowerCase()}`} title={localizeProjectTreeStatus(language, node.diagnosticStatus, 'diagnostic')} />}
          {node.sourceClass === 'ObservableResult' && <ResultCatalogNode node={node} language={language} />}
        </button>
        {node.layerErrors && <LayerErrorIndicator language={language} errors={node.layerErrors} />}
        {node.assetID && <span className="project-tree-source-kind">{catalogNodeTypeLabel(language, node)}</span>}
      </div>
      {hasChildren && !collapsed && <div className="project-tree-children">{node.children?.map((child) => (
        <DefaultRasCatalogNode key={child.id} node={child} depth={depth + 1} language={language} collapsedIds={collapsedIds} selectedNodeId={selectedNodeId} selectedIds={selectedIds} mode={mode} onToggle={onToggle} onSelect={onSelect} onOpen={onOpen} onContextMenu={onContextMenu} />
      ))}</div>}
    </>
  );
}
