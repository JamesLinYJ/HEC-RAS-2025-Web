import type { Language } from '../../shared/workbench';
import type { CatalogTreeNode } from './CatalogTreeSnapshot';
import { localizeProjectTreeStatus } from './ProjectTreeModeSectionTitles';

/** Result row state published only when CatalogTreeService has a stable result revision. */
export function ResultCatalogNode({ node, language }: { node: CatalogTreeNode; language: Language }) {
  if (!node.resultComputeState) return null;
  const state = localizeProjectTreeStatus(language, node.resultComputeState, 'compute');
  const title = node.resultComputeFailureReason || state;
  return <span className={`project-result-state is-${node.resultComputeState.toLowerCase()}`} aria-label={state} title={title}>{state}</span>;
}
