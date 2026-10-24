import type { MouseEvent } from 'react';
import type { BackendGateway, Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import type { CatalogTreeNode, CatalogTreeSnapshot } from './CatalogTreeSnapshot';
import type { ProjectTreeAction, ProjectTreeCategory, ProjectTreeModeName } from './ProjectTreeModeVM';
import { projectTreeModelActionIdentity } from './ProjectTreeModelActions';
import { DefaultRasCatalogNode } from './DefaultRasCatalogNode';
import { RasCatalogInfoPane } from './RasCatalogInfoPane';
import { RasCatalogTreeMode } from './RasCatalogTreeMode';

export interface ProjectTreeContextMenu {
  x: number;
  y: number;
  node: CatalogTreeNode;
}

export interface RasCatalogProps {
  language: Language;
  backend: BackendGateway;
  mode: ProjectTreeModeName;
  snapshot: CatalogTreeSnapshot | null;
  loading: boolean;
  error?: string;
  backendSupportsModelActions: boolean;
  pendingModelActionNodeId?: string | null;
  selectedAssetPath?: string;
  category: ProjectTreeCategory;
  query: string;
  collapsedIds: Set<string>;
  selectedIds: Set<string>;
  selectedNode: CatalogTreeNode | null;
  visibleRoots: CatalogTreeNode[];
  nodeCount: number;
  contextMenu: ProjectTreeContextMenu | null;
  onModeChange: (mode: ProjectTreeModeName) => void;
  onCategoryChange: (category: ProjectTreeCategory) => void;
  onQueryChange: (query: string) => void;
  onExpandAll: (expand: boolean) => void;
  onToggle: (node: CatalogTreeNode) => void;
  onSelect: (event: MouseEvent<HTMLButtonElement>, node: CatalogTreeNode) => void;
  onOpen: (node: CatalogTreeNode) => void;
  onContextMenu: (event: MouseEvent<HTMLButtonElement>, node: CatalogTreeNode) => void;
  onContextAction: (action: ProjectTreeAction, node: CatalogTreeNode) => void;
  onCloseContextMenu: () => void;
  onRetry: () => void;
}

/** Catalog shell keeps the existing tree pane, context menu, inspector, and selected-asset readout together. */
export function RasCatalog({
  language,
  backend,
  mode,
  snapshot,
  loading,
  error,
  backendSupportsModelActions,
  pendingModelActionNodeId,
  selectedAssetPath,
  category,
  query,
  collapsedIds,
  selectedIds,
  selectedNode,
  visibleRoots,
  nodeCount,
  contextMenu,
  onModeChange,
  onCategoryChange,
  onQueryChange,
  onExpandAll,
  onToggle,
  onSelect,
  onOpen,
  onContextMenu,
  onContextAction,
  onCloseContextMenu,
  onRetry,
}: RasCatalogProps) {
  const selectedForMenu = contextMenu?.node;
  const duplicateIdentity = selectedForMenu ? projectTreeModelActionIdentity(selectedForMenu) : null;
  const copyPending = pendingModelActionNodeId !== null && pendingModelActionNodeId !== undefined;

  return (
    <section className="project-tree-workspace">
      <RasCatalogTreeMode language={language} mode={mode} category={category} query={query} onModeChange={onModeChange} onCategoryChange={onCategoryChange} onQueryChange={onQueryChange} onExpandAll={onExpandAll} />

      <div className="project-tree-main">
        <div className="project-tree-pane">
          <div className="project-tree-count">{snapshot ? `${nodeCount} ${text(language, '个对象', 'items')}` : text(language, '等待工程目录', 'Waiting for catalog')}</div>
          {loading ? <div className="project-tree-state"><span className="project-spinner" />{text(language, '正在读取 C# 工程目录…', 'Loading the C# project catalog…')}</div>
            : error ? <div className="project-tree-state project-tree-error"><strong>{text(language, '工程目录读取失败', 'Project catalog failed to load')}</strong><p>{error}</p><button type="button" className="project-button project-button-secondary" onClick={onRetry}>{text(language, '重试', 'Retry')}</button></div>
              : !snapshot ? <div className="project-tree-state"><span>◇</span><p>{text(language, '选择工程文件后，等待后端解析工程目录。', 'Select a project file, then wait for the backend to parse its catalog.')}</p></div>
                : visibleRoots.length === 0 ? <div className="project-tree-state"><span>◇</span><p>{text(language, query ? '没有符合筛选条件的对象。' : '此分组没有工程对象。', query ? 'No items match this filter.' : 'This category has no project items.')}</p></div>
                  : <div className="project-tree-scroll">{visibleRoots.map((node) => <DefaultRasCatalogNode key={node.id} node={node} depth={0} language={language} collapsedIds={collapsedIds} selectedNodeId={selectedNode?.id ?? null} selectedIds={selectedIds} mode={mode} onToggle={onToggle} onSelect={onSelect} onOpen={onOpen} onContextMenu={onContextMenu} />)}</div>}
          {selectedIds.size > 1 && <div className="project-selection-count">{selectedIds.size} {text(language, '项已选', 'selected')}</div>}
        </div>
        <aside className="project-tree-inspector"><RasCatalogInfoPane language={language} backend={backend} node={selectedNode} /></aside>
      </div>

      {selectedForMenu && contextMenu && <div className="project-node-context" style={{ left: Math.min(contextMenu.x, window.innerWidth - 205), top: Math.min(contextMenu.y, window.innerHeight - 105) }} onClick={(event) => event.stopPropagation()}>
        {selectedForMenu.assetID && <button type="button" onClick={() => { onOpen(selectedForMenu); onCloseContextMenu(); }}>{text(language, '打开对象', 'Open item')}</button>}
        {mode === 'project' && duplicateIdentity && <button
          type="button"
          data-testid="project-tree-copy-model"
          data-node-id={selectedForMenu.id}
          disabled={!backendSupportsModelActions || copyPending}
          title={!backendSupportsModelActions ? text(language, '当前 C# 服务尚未发布模型复制操作。', 'The current C# service has not published model copy operations.') : undefined}
          onClick={() => { onContextAction('duplicate', selectedForMenu); onCloseContextMenu(); }}
        >{copyPending && selectedForMenu.id === pendingModelActionNodeId
          ? text(language, '正在复制…', 'Copying…')
          : copyPending
            ? text(language, '另一模型正在复制…', 'Another model is being copied…')
            : text(language, '复制模型', 'Copy model')}</button>}
      </div>}
      {selectedAssetPath && <div className="project-tree-selected-identity"><span>{text(language, '当前资产身份', 'Selected asset identity')}</span><code>{selectedAssetPath}</code></div>}
    </section>
  );
}
