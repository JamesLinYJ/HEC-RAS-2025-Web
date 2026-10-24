import type { BackendGateway, FeaturePageProps, Language, WorkbenchContext } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import type { CatalogTreeNode } from './CatalogTreeSnapshot';
import type { GeometryBackupCommittedCallback } from './geometrybackupworkflow/geometryBackupWorkflow';
import { CatalogSummaryDetails } from './CatalogSummaryDetails';
import { RasCatalogSummary } from './RasCatalogSummary';
import { GeometryBackupAction } from './geometrybackupworkflow/GeometryBackupAction';

export interface BackupCatalogSummaryProps {
  language: Language;
  backend: BackendGateway;
  context: WorkbenchContext;
  hasUnsavedDrafts?: boolean;
  onContextCommit?: FeaturePageProps['onContextCommit'];
  onGeometryBackupCommitted?: GeometryBackupCommittedCallback;
  node: CatalogTreeNode;
  icon: string;
  typeLabel: string;
}

export function BackupCatalogSummary({ language, backend, context, hasUnsavedDrafts, onContextCommit, onGeometryBackupCommitted, node, icon, typeLabel }: BackupCatalogSummaryProps) {
  const backup = node.backupSummary;
  const capability = backup?.canSaveAsNewGeometry;
  const restoreState = capability === undefined
    ? text(language, '未提供恢复状态', 'Restore status not provided')
    : capability
      ? text(language, '已通过目录恢复校验', 'Catalog restore checks passed')
      : text(language, '未通过目录恢复校验', 'Catalog restore checks did not pass');

  return (
    <RasCatalogSummary
      language={language}
      node={node}
      icon={icon}
      typeLabel={typeLabel}
      filePathOverride={backup?.filename}
      dateCreatedOverride={backup?.dateCreated}
      dateLastModifiedOverride={backup?.dateLastModified}
      showDescription={false}
      showExtraProperties={false}
    >
      <CatalogSummaryDetails language={language} title={text(language, '备份信息', 'Backup information')} fields={[
        { id: 'restore-check', label: text(language, '恢复校验', 'Restore check'), value: restoreState },
        { id: 'backup-revision', label: text(language, '备份 SHA-256', 'Backup SHA-256'), value: backup?.revision },
        { id: 'parent-geometry', label: text(language, '父级几何路径', 'Parent geometry path'), value: backup?.parentGeometryPath },
        { id: 'parent-geometry-revision', label: text(language, '父级几何 SHA-256', 'Parent Geometry SHA-256'), value: backup?.parentGeometryRevision },
        { id: 'project-revision', label: text(language, '工程 SHA-256', 'Project SHA-256'), value: backup?.projectRevision },
      ]} />
      <GeometryBackupAction
        language={language}
        backend={backend}
        context={context}
        hasUnsavedDrafts={hasUnsavedDrafts}
        onContextCommit={onContextCommit}
        onGeometryBackupCommitted={onGeometryBackupCommitted}
        node={node}
        action="restore"
      />
    </RasCatalogSummary>
  );
}
