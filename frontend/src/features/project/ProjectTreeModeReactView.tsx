import type { CSSProperties, MouseEvent, ReactNode } from 'react';
import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import type { CatalogTreeNode, CatalogTreeSnapshot } from './CatalogTreeSnapshot';
import { BackupCatalogSummary } from './BackupCatalogSummary';
import { BoundaryConditionCatalogSummary } from './BoundaryConditionCatalogSummary';
import { ClassificationCatalogSummary } from './ClassificationCatalogSummary';
import { ExternalFileConnectionCatalogSummary } from './ExternalFileConnectionCatalogSummary';
import { GeometryCatalogSummary } from './GeometryCatalogSummary';
import { NValueCatalogSummary } from './NValueCatalogSummary';
import { PlanCatalogSummary } from './PlanCatalogSummary';
import { ProjectCatalogSummary } from './ProjectCatalogSummary';
import { RasCatalogSummary } from './RasCatalogSummary';
import { ResultCatalogSummary } from './ResultCatalogSummary';
import { TerrainCatalogSummary } from './TerrainCatalogSummary';
import { localizeProjectTreeSectionTitle, localizeProjectTreeSourceClass, localizeProjectTreeStatus } from './ProjectTreeModeSectionTitles';
import type { ProjectTreeAction, ProjectTreeCategory, ProjectTreeModeName } from './ProjectTreeModeVM';
import { projectTreeModelActionIdentity } from './ProjectTreeModelActions';

export interface ProjectTreeContextMenu {
  x: number;
  y: number;
  node: CatalogTreeNode;
}

interface ProjectTreeModeReactViewProps {
  language: Language;
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

function nodeIcon(node: CatalogTreeNode): string {
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

function sourceName(language: Language, node: CatalogTreeNode): string {
  return localizeProjectTreeSourceClass(language, node.sourceClass);
}

function NodeRow({
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
}: {
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
}) {
  const hasChildren = (node.children?.length ?? 0) > 0;
  const isGroup = !node.assetID && hasChildren;
  const collapsed = collapsedIds.has(node.id);
  const selected = selectedNodeId === node.id;
  const multiSelected = selectedIds.has(`${mode}:${node.id}`);
  const visibleName = node.localizeName ? localizeProjectTreeSectionTitle(language, node.name) : node.name;
  return (
    <>
      <div className={`project-tree-row ${selected ? 'is-selected' : ''} ${multiSelected ? 'is-multi-selected' : ''}`} style={{ '--tree-depth': depth } as CSSProperties}>
        <button className="project-tree-expander" type="button" aria-label={hasChildren ? (collapsed ? text(language, '展开', 'Expand') : text(language, '折叠', 'Collapse')) : undefined} disabled={!hasChildren} onClick={() => onToggle(node)}>{hasChildren ? (collapsed ? '›' : '⌄') : ''}</button>
        <button className="project-tree-node" type="button" aria-selected={selected} onClick={(event) => onSelect(event, node)} onDoubleClick={() => node.assetID && onOpen(node)} onContextMenu={(event) => onContextMenu(event, node)}>
          <span className={`project-tree-node-icon ${isGroup ? 'is-group' : ''}`}>{nodeIcon(node)}</span>
          <span className="project-tree-node-name" title={node.name}>{visibleName}</span>
          {node.diagnosticStatus && <span className={`project-diagnostic-dot is-${node.diagnosticStatus.toLowerCase()}`} title={localizeProjectTreeStatus(language, node.diagnosticStatus, 'diagnostic')} />}
          {node.resultComputeState && <span className={`project-result-state is-${node.resultComputeState.toLowerCase()}`}>{localizeProjectTreeStatus(language, node.resultComputeState, 'compute')}</span>}
        </button>
        {node.assetID && <span className="project-tree-source-kind">{sourceName(language, node)}</span>}
      </div>
      {hasChildren && !collapsed && <div className="project-tree-children">{node.children?.map((child) => (
        <NodeRow key={child.id} node={child} depth={depth + 1} language={language} collapsedIds={collapsedIds} selectedNodeId={selectedNodeId} selectedIds={selectedIds} mode={mode} onToggle={onToggle} onSelect={onSelect} onOpen={onOpen} onContextMenu={onContextMenu} />
      ))}</div>}
    </>
  );
}

function NodeDetail({ language, node }: { language: Language; node: CatalogTreeNode | null }) {
  if (!node) return <div className="project-tree-detail-empty"><span>◇</span><p>{text(language, '选择工程对象查看目录信息。', 'Select a project item to inspect its catalog information.')}</p></div>;
  const summaryProps = { language, node, icon: nodeIcon(node), typeLabel: sourceName(language, node) };
  let summary: ReactNode;
  switch (node.sourceClass) {
    case 'ModelSectionContext': summary = <ProjectCatalogSummary {...summaryProps} typeLabel={text(language, '工程', 'Project')} />; break;
    case 'BackupNodeVM': summary = <BackupCatalogSummary {...summaryProps} />; break;
    case 'ObservableGeometry': summary = <GeometryCatalogSummary {...summaryProps} />; break;
    case 'ObservableTerrain': summary = <TerrainCatalogSummary {...summaryProps} />; break;
    case 'ObservableBoundaryCondition': summary = <BoundaryConditionCatalogSummary {...summaryProps} />; break;
    case 'ObservablePlan': summary = <PlanCatalogSummary {...summaryProps} />; break;
    case 'ObservableResult': summary = <ResultCatalogSummary {...summaryProps} />; break;
    case 'ObservableNValueLayer': summary = <NValueCatalogSummary {...summaryProps} />; break;
    case 'ObservableClassificationLayer': summary = <ClassificationCatalogSummary {...summaryProps} />; break;
    case 'ExternalFileConnectionCatalogNode': summary = <ExternalFileConnectionCatalogSummary {...summaryProps} />; break;
    default: summary = <RasCatalogSummary {...summaryProps} />;
  }
  return (
    <>
      {summary}
      <section className="project-tree-detail-runtime">
        <dl>
          <div><dt>{text(language, '对象类型', 'Source class')}</dt><dd>{sourceName(language, node)}</dd></div>
          {node.diagnosticStatus && <div><dt>{text(language, '诊断状态', 'Diagnostic status')}</dt><dd>{localizeProjectTreeStatus(language, node.diagnosticStatus, 'diagnostic')}</dd></div>}
          {node.resultComputeState && <div><dt>{text(language, '计算状态', 'Compute state')}</dt><dd>{localizeProjectTreeStatus(language, node.resultComputeState, 'compute')}</dd></div>}
        </dl>
        {node.diagnosticReadError && <div className="project-inline-error">{node.diagnosticReadError}</div>}
        {node.resultComputeFailureReason && <div className="project-inline-error">{node.resultComputeFailureReason}</div>}
      </section>
    </>
  );
}

export function ProjectTreeModeReactView({
  language,
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
}: ProjectTreeModeReactViewProps) {
  const selectedForMenu = contextMenu?.node;
  const duplicateIdentity = selectedForMenu ? projectTreeModelActionIdentity(selectedForMenu) : null;
  const copyPending = pendingModelActionNodeId !== null && pendingModelActionNodeId !== undefined;
  return (
    <section className="project-tree-workspace">
      <header className="project-tree-toolbar">
        <div className="project-tree-mode-switch" role="tablist" aria-label={text(language, '工程树模式', 'Project tree mode')}>
          <button type="button" role="tab" aria-selected={mode === 'project'} className={mode === 'project' ? 'is-active' : ''} onClick={() => onModeChange('project')}>{text(language, '工程树', 'Project tree')}</button>
          <button type="button" role="tab" aria-selected={mode === 'catalog'} className={mode === 'catalog' ? 'is-active' : ''} onClick={() => onModeChange('catalog')}>{text(language, '目录', 'Catalog')}</button>
        </div>
        <div className="project-tree-search"><span>⌕</span><input aria-label={text(language, '筛选工程对象', 'Filter project items')} value={query} onChange={(event) => onQueryChange(event.currentTarget.value)} placeholder={text(language, '筛选…', 'Filter…')} /><button type="button" aria-label={text(language, '清除筛选', 'Clear filter')} onClick={() => onQueryChange('')} disabled={!query}>×</button></div>
        <button type="button" className="project-tree-tool-button" onClick={() => onExpandAll(true)}>{text(language, '全部展开', 'Expand all')}</button>
        <button type="button" className="project-tree-tool-button" onClick={() => onExpandAll(false)}>{text(language, '全部折叠', 'Collapse all')}</button>
      </header>

      <nav className="project-tree-categories" aria-label={text(language, '工程分组', 'Project categories')}>
        {([
          ['all', text(language, '全部', 'All')],
          ['geometries', text(language, '几何', 'Geometries')],
          ['terrains', text(language, '地形', 'Terrains')],
          ['boundaries', text(language, '边界', 'Boundaries')],
          ['plans', text(language, '计划', 'Plans')],
          ['results', text(language, '结果', 'Results')],
        ] as const).map(([key, label]) => <button type="button" key={key} className={category === key ? 'is-active' : ''} onClick={() => onCategoryChange(key)}>{label}</button>)}
      </nav>

      <div className="project-tree-main">
        <div className="project-tree-pane">
          <div className="project-tree-count">{snapshot ? `${nodeCount} ${text(language, '个对象', 'items')}` : text(language, '等待工程目录', 'Waiting for catalog')}</div>
          {loading ? <div className="project-tree-state"><span className="project-spinner" />{text(language, '正在读取 C# 工程目录…', 'Loading the C# project catalog…')}</div>
            : error ? <div className="project-tree-state project-tree-error"><strong>{text(language, '工程目录读取失败', 'Project catalog failed to load')}</strong><p>{error}</p><button type="button" className="project-button project-button-secondary" onClick={onRetry}>{text(language, '重试', 'Retry')}</button></div>
              : !snapshot ? <div className="project-tree-state"><span>◇</span><p>{text(language, '选择工程文件后，等待后端解析工程目录。', 'Select a project file, then wait for the backend to parse its catalog.')}</p></div>
                : visibleRoots.length === 0 ? <div className="project-tree-state"><span>◇</span><p>{text(language, query ? '没有符合筛选条件的对象。' : '此分组没有工程对象。', query ? 'No items match this filter.' : 'This category has no project items.')}</p></div>
                  : <div className="project-tree-scroll">{visibleRoots.map((node) => <NodeRow key={node.id} node={node} depth={0} language={language} collapsedIds={collapsedIds} selectedNodeId={selectedNode?.id ?? null} selectedIds={selectedIds} mode={mode} onToggle={onToggle} onSelect={onSelect} onOpen={onOpen} onContextMenu={onContextMenu} />)}</div>}
          {selectedIds.size > 1 && <div className="project-selection-count">{selectedIds.size} {text(language, '项已选', 'selected')}</div>}
        </div>
        <aside className="project-tree-inspector"><NodeDetail language={language} node={selectedNode} /></aside>
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
