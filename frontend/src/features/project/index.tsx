import { ProjectExplorerControl } from './ProjectExplorerControl';
import type { ProjectExplorerProps } from './ProjectExplorerControl';

export type ProjectFeaturePageProps = ProjectExplorerProps;

export default function ProjectFeaturePage(props: ProjectFeaturePageProps) {
  return <ProjectExplorerControl {...props} />;
}

export { ProjectExplorerControl } from './ProjectExplorerControl';
export type { PendingProjectCommand, ProjectCommandKind, ProjectExplorerProps } from './ProjectExplorerControl';
export { NewProjectPage } from './NewProjectPage';
export { ProjectExplorerMainPageTiled } from './ProjectExplorerMainPageTiled';
export { ProjectTreeMode } from './ProjectTreeMode';
export { ProjectTreeModeReactView } from './ProjectTreeModeReactView';
export { RasCatalogSummary } from './RasCatalogSummary';
export { RasCatalogSummaryHeader } from './RasCatalogSummaryHeader';
export { CatalogFilePathControl } from './CatalogFilePathControl';
export { BackupCatalogSummary } from './BackupCatalogSummary';
export { ClassificationCatalogSummary } from './ClassificationCatalogSummary';
export { ExternalFileConnectionCatalogSummary } from './ExternalFileConnectionCatalogSummary';
export { NValueCatalogSummary } from './NValueCatalogSummary';
export { ProjectCatalogSummary } from './ProjectCatalogSummary';
export { ResultCatalogSummary } from './ResultCatalogSummary';
export { GeometryCatalogSummary } from './GeometryCatalogSummary';
export { TerrainCatalogSummary } from './TerrainCatalogSummary';
export { BoundaryConditionCatalogSummary } from './BoundaryConditionCatalogSummary';
export { PlanCatalogSummary } from './PlanCatalogSummary';
export { ProjectTreeModeSectionTitles, localizeProjectTreeSectionTitle } from './ProjectTreeModeSectionTitles';
export type { ProjectTreeModeProps } from './ProjectTreeMode';
export type { ProjectTreeAction, ProjectTreeCategory, ProjectTreeModeName, ProjectTreeModeVMState, ProjectTreeRootDefinition } from './ProjectTreeModeVM';
export type { CatalogTreeNode, CatalogTreeSnapshot, CatalogTreeRequest } from './CatalogTreeSnapshot';
