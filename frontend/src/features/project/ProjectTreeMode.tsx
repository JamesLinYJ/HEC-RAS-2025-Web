import { useEffect, useMemo, useState } from 'react';
import type { MouseEvent } from 'react';
import type { BackendGateway, FeaturePageProps, Language, WorkbenchContext } from '../../shared/workbench';
import type { CatalogTreeNode, CatalogTreeSnapshot } from './CatalogTreeSnapshot';
import type { GeometryBackupCommittedCallback } from './geometrybackupworkflow/geometryBackupWorkflow';
import { ProjectTreeModeReactView } from './ProjectTreeModeReactView';
import type { ProjectTreeContextMenu } from './ProjectTreeModeReactView';
import {
  countProjectTreeNodes,
  createProjectTreeModeVMState,
  filterProjectTree,
  flattenVisibleProjectTree,
  orderProjectTreeRoots,
  reconcileProjectTreeSnapshotState,
  rootsForProjectTreeCategory,
  selectProjectTreeNode,
  selectProjectTreeNodeForContextMenu,
  setProjectTreeExpanded,
  toggleProjectTreeNode,
} from './ProjectTreeModeVM';
import type { ProjectTreeAction, ProjectTreeCategory, ProjectTreeModeName } from './ProjectTreeModeVM';
import { ProjectBehaviors } from './behaviors/ProjectBehaviors';
import { sameProjectionPath } from './behaviors/projectProjectionExport';
import { text } from '../../shared/workbench';

export interface ProjectTreeModeProps {
  language: Language;
  backend: BackendGateway;
  context: WorkbenchContext;
  hasUnsavedDrafts?: boolean;
  onContextCommit?: FeaturePageProps['onContextCommit'];
  onGeometryBackupCommitted?: GeometryBackupCommittedCallback;
  mode: ProjectTreeModeName;
  projectSnapshot: CatalogTreeSnapshot | null;
  catalogSnapshot: CatalogTreeSnapshot | null;
  loading: boolean;
  error?: string;
  backendSupportsModelActions: boolean;
  pendingModelActionNodeId?: string | null;
  selectedAssetPath?: string;
  onModeChange: (mode: ProjectTreeModeName) => void;
  onSelect: (node: CatalogTreeNode | null) => void;
  onOpen: (node: CatalogTreeNode) => void;
  onPerformAction: (action: ProjectTreeAction, node: CatalogTreeNode) => void;
  onRetry: () => void;
  onExportProjectProjection?: () => void;
  projectionExportDisabledReason?: string;
}

/** React counterpart to the source ProjectTreeMode view; VM transitions stay in ProjectTreeModeVM.ts. */
export function ProjectTreeMode({
  language,
  backend,
  context,
  hasUnsavedDrafts,
  onContextCommit,
  onGeometryBackupCommitted,
  mode,
  projectSnapshot,
  catalogSnapshot,
  loading,
  error,
  backendSupportsModelActions,
  pendingModelActionNodeId,
  selectedAssetPath,
  onModeChange,
  onSelect,
  onOpen,
  onPerformAction,
  onRetry,
  onExportProjectProjection,
  projectionExportDisabledReason,
}: ProjectTreeModeProps) {
  const [statesByMode, setStatesByMode] = useState(() => ({
    project: createProjectTreeModeVMState(),
    catalog: createProjectTreeModeVMState(),
  }));
  const [contextMenu, setContextMenu] = useState<ProjectTreeContextMenu | null>(null);
  const snapshot = mode === 'project' ? projectSnapshot : catalogSnapshot;
  const state = statesByMode[mode];
  const roots = useMemo(() => orderProjectTreeRoots(snapshot?.roots ?? []), [snapshot]);
  const categoryRoots = useMemo(() => rootsForProjectTreeCategory(roots, state.category), [roots, state.category]);
  const visibleRoots = useMemo(() => filterProjectTree(categoryRoots, state.query.trim()), [categoryRoots, state.query]);
  const flattened = useMemo(() => flattenVisibleProjectTree(visibleRoots, state.collapsedIds), [visibleRoots, state.collapsedIds]);
  const nodeCount = useMemo(() => countProjectTreeNodes(visibleRoots), [visibleRoots]);

  useEffect(() => {
    setContextMenu(null);
  }, [mode, snapshot]);

  useEffect(() => {
    if (!snapshot) return;
    setStatesByMode((current) => ({
      ...current,
      [mode]: reconcileProjectTreeSnapshotState(current[mode], mode, snapshot, selectedAssetPath),
    }));
  }, [mode, snapshot, selectedAssetPath]);

  useEffect(() => {
    if (!contextMenu) return;
    const close = (): void => setContextMenu(null);
    window.addEventListener('click', close);
    window.addEventListener('scroll', close, true);
    return () => {
      window.removeEventListener('click', close);
      window.removeEventListener('scroll', close, true);
    };
  }, [contextMenu]);

  const selectNode = (event: MouseEvent<HTMLButtonElement>, node: CatalogTreeNode): void => {
    setStatesByMode((current) => ({ ...current, [mode]: selectProjectTreeNode(current[mode], mode, node, flattened, {
      shiftKey: event.shiftKey,
      ctrlKey: event.ctrlKey,
      metaKey: event.metaKey,
    }) }));
    onSelect(node);
  };

  const openContextMenu = (event: MouseEvent<HTMLButtonElement>, node: CatalogTreeNode): void => {
    event.preventDefault();
    setStatesByMode((current) => ({ ...current, [mode]: selectProjectTreeNodeForContextMenu(current[mode], mode, node) }));
    setContextMenu({ x: event.clientX, y: event.clientY, node });
  };

  const handleContextAction = (action: ProjectTreeAction, node: CatalogTreeNode): void => {
    onPerformAction(action, node);
  };

  const selectedProject = snapshot ? state.selectedNode : null;
  const projectActions = ProjectBehaviors.AttachActions_Layer_ContextMenu(selectedProject);
  const selectedProjectMatches = !!context.projectPath && !!selectedProject?.catalogSummary?.filename &&
    sameProjectionPath(selectedProject.catalogSummary.filename, context.projectPath);

  return (
    <>
    {projectActions.length > 0 && onExportProjectProjection && <div className="project-toolbar-actions" data-testid="project-selected-project-actions">
      <span>{selectedProject?.localizeName ? text(language, '工程', 'Project') : selectedProject?.name}</span>
      <button type="button" className="project-button project-button-secondary" data-testid="project-selected-projection-export"
        disabled={!selectedProjectMatches || Boolean(projectionExportDisabledReason)}
        title={!selectedProjectMatches ? '所选工程与当前工程不一致，请重新选择当前工程。' : projectionExportDisabledReason}
        onClick={onExportProjectProjection}>{text(language, projectActions[0].name, 'Export projection')}</button>
    </div>}
    <ProjectTreeModeReactView
      language={language}
      backend={backend}
      context={context}
      hasUnsavedDrafts={hasUnsavedDrafts}
      onContextCommit={onContextCommit}
      onGeometryBackupCommitted={onGeometryBackupCommitted}
      mode={mode}
      snapshot={snapshot}
      loading={loading}
      error={error}
      backendSupportsModelActions={backendSupportsModelActions}
      pendingModelActionNodeId={pendingModelActionNodeId}
      selectedAssetPath={selectedAssetPath}
      category={state.category}
      query={state.query}
      collapsedIds={state.collapsedIds}
      selectedIds={state.selectedIds}
      selectedNode={snapshot ? state.selectedNode : null}
      visibleRoots={visibleRoots}
      nodeCount={nodeCount}
      contextMenu={contextMenu}
      onModeChange={onModeChange}
      onCategoryChange={(category: ProjectTreeCategory) => setStatesByMode((current) => ({ ...current, [mode]: { ...current[mode], category } }))}
      onQueryChange={(query) => setStatesByMode((current) => ({ ...current, [mode]: { ...current[mode], query } }))}
      onExpandAll={(expand) => setStatesByMode((current) => ({ ...current, [mode]: setProjectTreeExpanded(current[mode], visibleRoots, expand) }))}
      onToggle={(node) => setStatesByMode((current) => ({ ...current, [mode]: toggleProjectTreeNode(current[mode], node.id) }))}
      onSelect={selectNode}
      onOpen={onOpen}
      onContextMenu={openContextMenu}
      onContextAction={handleContextAction}
      onCloseContextMenu={() => setContextMenu(null)}
      onRetry={onRetry}
    />
    </>
  );
}
