import type { ReactNode } from 'react';
import type { BackendGateway, Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import type { CatalogTreeNode } from './CatalogTreeSnapshot';
import { BackupCatalogSummary } from './BackupCatalogSummary';
import { BoundaryConditionCatalogSummary } from './BoundaryConditionCatalogSummary';
import { ClassificationCatalogSummary } from './ClassificationCatalogSummary';
import { ExternalConnectionDetails } from './ExternalConnectionDetails';
import { GeometryCatalogSummary } from './GeometryCatalogSummary';
import { NValueCatalogSummary } from './NValueCatalogSummary';
import { PlanCatalogSummary } from './PlanCatalogSummary';
import { ProjectCatalogSummary } from './ProjectCatalogSummary';
import { RasCatalogSummary } from './RasCatalogSummary';
import { ResultCatalogSummary } from './ResultCatalogSummary';
import { TerrainCatalogSummary } from './TerrainCatalogSummary';
import { catalogNodeIcon, catalogNodeTypeLabel } from './DefaultRasCatalogNode';
import { localizeProjectTreeStatus } from './ProjectTreeModeSectionTitles';

export interface RasCatalogInfoPaneProps {
  language: Language;
  backend: BackendGateway;
  node: CatalogTreeNode | null;
}

/** Selected-node detail dispatch and runtime diagnostics from RasCatalogInfoPane. */
export function RasCatalogInfoPane({ language, backend, node }: RasCatalogInfoPaneProps) {
  if (!node) return <div className="project-tree-detail-empty"><span>◇</span><p>{text(language, '选择工程对象查看目录信息。', 'Select a project item to inspect its catalog information.')}</p></div>;

  const summaryProps = { language, node, icon: catalogNodeIcon(node), typeLabel: catalogNodeTypeLabel(language, node) };
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
    case 'ExternalFileConnectionCatalogNode':
    case 'ExternalConnectionCatalogNode': summary = <ExternalConnectionDetails {...summaryProps} backend={backend} />; break;
    default: summary = <RasCatalogSummary {...summaryProps} />;
  }

  return (
    <>
      {summary}
      <section className="project-tree-detail-runtime">
        <dl>
          <div><dt>{text(language, '对象类型', 'Source class')}</dt><dd>{catalogNodeTypeLabel(language, node)}</dd></div>
          {node.diagnosticStatus && <div><dt>{text(language, '诊断状态', 'Diagnostic status')}</dt><dd>{localizeProjectTreeStatus(language, node.diagnosticStatus, 'diagnostic')}</dd></div>}
          {node.resultComputeState && <div><dt>{text(language, '计算状态', 'Compute state')}</dt><dd>{localizeProjectTreeStatus(language, node.resultComputeState, 'compute')}</dd></div>}
        </dl>
        {node.diagnosticReadError && <div className="project-inline-error">{node.diagnosticReadError}</div>}
        {node.resultComputeFailureReason && <div className="project-inline-error">{node.resultComputeFailureReason}</div>}
      </section>
    </>
  );
}
