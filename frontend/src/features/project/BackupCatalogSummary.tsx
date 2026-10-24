import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import type { CatalogTreeNode } from './CatalogTreeSnapshot';
import { CatalogSummaryDetails } from './CatalogSummaryDetails';
import { RasCatalogSummary } from './RasCatalogSummary';

export function BackupCatalogSummary({ language, node, icon, typeLabel }: {
  language: Language; node: CatalogTreeNode; icon: string; typeLabel: string;
}) {
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
        { id: 'parent-geometry', label: text(language, '父级几何路径', 'Parent geometry path'), value: backup?.parentGeometryPath },
      ]} />
    </RasCatalogSummary>
  );
}
