import { walkCatalogTree } from './CatalogTreeSnapshot';
import type { CatalogTreeNode, CatalogTreeSnapshot } from './CatalogTreeSnapshot';
import { ProjectTreeModeSectionTitles } from './ProjectTreeModeSectionTitles';

export type ProjectTreeModeName = 'project' | 'catalog';
export type ProjectTreeCategory = 'all' | 'geometries' | 'terrains' | 'boundaries' | 'plans' | 'results';
export type ProjectTreeAction = 'duplicate';

/** Root node responsibilities mirrored from ProjectTreeModeVM.InitializeRootNodesInternal. */
export interface ProjectTreeRootDefinition {
  viewModelProperty: 'PlanRootNode' | 'ResultsRootNode' | 'GeometryRootNode' | 'TerrainRootNode' |
    'ClassificationRootNode' | 'MapLayersRootNode' | 'ProfileLinesRootNode' | 'BoundaryConditionsRootNode';
  title: string;
  childrenIgnoreSortOrder?: boolean;
}

export const PROJECT_TREE_ROOT_DEFINITIONS: readonly ProjectTreeRootDefinition[] = [
  { viewModelProperty: 'PlanRootNode', title: ProjectTreeModeSectionTitles.Plans },
  { viewModelProperty: 'ResultsRootNode', title: ProjectTreeModeSectionTitles.Results },
  { viewModelProperty: 'GeometryRootNode', title: ProjectTreeModeSectionTitles.Geometries },
  { viewModelProperty: 'TerrainRootNode', title: ProjectTreeModeSectionTitles.Terrains },
  { viewModelProperty: 'ClassificationRootNode', title: ProjectTreeModeSectionTitles.SurfaceLayers, childrenIgnoreSortOrder: true },
  { viewModelProperty: 'MapLayersRootNode', title: ProjectTreeModeSectionTitles.MapLayers, childrenIgnoreSortOrder: true },
  { viewModelProperty: 'ProfileLinesRootNode', title: ProjectTreeModeSectionTitles.Features },
  { viewModelProperty: 'BoundaryConditionsRootNode', title: ProjectTreeModeSectionTitles.BoundaryConditions },
];

const rootOrder = new Map(PROJECT_TREE_ROOT_DEFINITIONS.map(({ title }, index) => [title, index]));
const categoryBySection: Partial<Record<string, Exclude<ProjectTreeCategory, 'all'>>> = {
  [ProjectTreeModeSectionTitles.Geometries]: 'geometries',
  [ProjectTreeModeSectionTitles.Terrains]: 'terrains',
  [ProjectTreeModeSectionTitles.BoundaryConditions]: 'boundaries',
  [ProjectTreeModeSectionTitles.Plans]: 'plans',
  [ProjectTreeModeSectionTitles.Results]: 'results',
};

export interface ProjectTreeModeVMState {
  category: ProjectTreeCategory;
  query: string;
  collapsedIds: Set<string>;
  selectedIds: Set<string>;
  selectedNode: CatalogTreeNode | null;
  selectionAnchorId?: string;
}

export interface SelectionModifiers {
  shiftKey: boolean;
  ctrlKey: boolean;
  metaKey: boolean;
}

export function createProjectTreeModeVMState(): ProjectTreeModeVMState {
  return {
    category: 'all',
    query: '',
    collapsedIds: new Set(),
    selectedIds: new Set(),
    selectedNode: null,
  };
}

/** The C# VM creates its eight specialized roots first, then appends base roots. */
export function orderProjectTreeRoots(nodes: CatalogTreeNode[]): CatalogTreeNode[] {
  return nodes
    .map((node, originalIndex) => ({ node, originalIndex, order: rootOrder.get(node.name) }))
    .sort((left, right) => {
      if (left.order === undefined && right.order === undefined) return left.originalIndex - right.originalIndex;
      if (left.order === undefined) return 1;
      if (right.order === undefined) return -1;
      return left.order - right.order;
    })
    .map(({ node }) => node);
}

export function rootsForProjectTreeCategory(nodes: CatalogTreeNode[], category: ProjectTreeCategory): CatalogTreeNode[] {
  return category === 'all' ? nodes : nodes.filter((node) => categoryBySection[node.name] === category);
}

export function filterProjectTree(nodes: CatalogTreeNode[], query: string): CatalogTreeNode[] {
  if (!query) return nodes;
  const needle = query.toLocaleLowerCase();
  return nodes.flatMap((node) => {
    const children = filterProjectTree(node.children ?? [], query);
    const matches = `${node.name} ${node.sourceClass} ${node.assetID ?? ''}`.toLocaleLowerCase().includes(needle);
    return matches || children.length ? [{ ...node, children: children.length ? children : node.children }] : [];
  });
}

export function countProjectTreeNodes(nodes: CatalogTreeNode[]): number {
  return nodes.reduce((total, node) => total + 1 + countProjectTreeNodes(node.children ?? []), 0);
}

export function flattenVisibleProjectTree(nodes: CatalogTreeNode[], collapsed: Set<string>): CatalogTreeNode[] {
  const result: CatalogTreeNode[] = [];
  const visit = (items: CatalogTreeNode[]): void => {
    for (const node of items) {
      result.push(node);
      if (node.children?.length && !collapsed.has(node.id)) visit(node.children);
    }
  };
  visit(nodes);
  return result;
}

export function resetProjectTreeSnapshotState(state: ProjectTreeModeVMState): ProjectTreeModeVMState {
  return { ...state, collapsedIds: new Set(), selectedIds: new Set(), selectedNode: null };
}

/** Rebinds local tree state to a confirmed snapshot without changing the active workbench asset. */
export function reconcileProjectTreeSnapshotState(
  state: ProjectTreeModeVMState,
  mode: ProjectTreeModeName,
  snapshot: CatalogTreeSnapshot,
  selectedAssetPath?: string,
): ProjectTreeModeVMState {
  const nodes = walkCatalogTree(snapshot.roots);
  const nodeIds = new Set(nodes.map((node) => node.id));
  const selectedNode = selectedAssetPath
    ? nodes.find((node) => node.assetID === selectedAssetPath) ?? null
    : null;
  const modePrefix = `${mode}:`;
  const selectedIds = new Set([...state.selectedIds].filter((key) => {
    if (!key.startsWith(modePrefix)) return true;
    return selectedNode !== null && nodeIds.has(key.slice(modePrefix.length));
  }));
  if (selectedNode) selectedIds.add(`${mode}:${selectedNode.id}`);

  let selectionAnchorId = state.selectionAnchorId;
  if (selectionAnchorId?.startsWith(modePrefix)) {
    const anchorNodeId = selectionAnchorId.slice(modePrefix.length);
    if (!selectedNode || !nodeIds.has(anchorNodeId)) selectionAnchorId = undefined;
  }

  return {
    ...state,
    collapsedIds: new Set([...state.collapsedIds].filter((nodeId) => nodeIds.has(nodeId))),
    selectedIds,
    selectedNode,
    selectionAnchorId,
  };
}

export function toggleProjectTreeNode(state: ProjectTreeModeVMState, nodeId: string): ProjectTreeModeVMState {
  const collapsedIds = new Set(state.collapsedIds);
  if (collapsedIds.has(nodeId)) collapsedIds.delete(nodeId);
  else collapsedIds.add(nodeId);
  return { ...state, collapsedIds };
}

export function setProjectTreeExpanded(state: ProjectTreeModeVMState, nodes: CatalogTreeNode[], expand: boolean): ProjectTreeModeVMState {
  if (expand) return { ...state, collapsedIds: new Set() };
  const collapsedIds = new Set<string>();
  const visit = (items: CatalogTreeNode[]): void => {
    for (const node of items) {
      if (node.children?.length) {
        collapsedIds.add(node.id);
        visit(node.children);
      }
    }
  };
  visit(nodes);
  return { ...state, collapsedIds };
}

export function selectProjectTreeNode(
  state: ProjectTreeModeVMState,
  mode: ProjectTreeModeName,
  node: CatalogTreeNode,
  visibleNodes: CatalogTreeNode[],
  modifiers: SelectionModifiers,
): ProjectTreeModeVMState {
  const nodeKey = `${mode}:${node.id}`;
  if (modifiers.shiftKey && state.selectionAnchorId) {
    const start = visibleNodes.findIndex((item) => `${mode}:${item.id}` === state.selectionAnchorId);
    const end = visibleNodes.findIndex((item) => item.id === node.id);
    if (start >= 0 && end >= 0) {
      const selectedIds = new Set(visibleNodes.slice(Math.min(start, end), Math.max(start, end) + 1).map((item) => `${mode}:${item.id}`));
      return { ...state, selectedNode: node, selectedIds };
    }
  }
  if (modifiers.metaKey || modifiers.ctrlKey) {
    const selectedIds = new Set(state.selectedIds);
    if (selectedIds.has(nodeKey)) selectedIds.delete(nodeKey);
    else selectedIds.add(nodeKey);
    return { ...state, selectedNode: node, selectedIds, selectionAnchorId: nodeKey };
  }
  return { ...state, selectedNode: node, selectedIds: new Set([nodeKey]), selectionAnchorId: nodeKey };
}

export function selectProjectTreeNodeForContextMenu(
  state: ProjectTreeModeVMState,
  mode: ProjectTreeModeName,
  node: CatalogTreeNode,
): ProjectTreeModeVMState {
  const nodeKey = `${mode}:${node.id}`;
  return { ...state, selectedNode: node, selectedIds: new Set([nodeKey]), selectionAnchorId: nodeKey };
}
