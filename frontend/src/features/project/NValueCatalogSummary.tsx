import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import type { CatalogTreeNode } from './CatalogTreeSnapshot';
import { CatalogSummaryDetails } from './CatalogSummaryDetails';
import { RasCatalogSummary } from './RasCatalogSummary';

export function NValueCatalogSummary({ language, node, icon, typeLabel }: {
  language: Language; node: CatalogTreeNode; icon: string; typeLabel: string;
}) {
  const classification = node.children?.find((child) => child.sourceClass === 'ObservableClassificationLayer');
  return (
    <RasCatalogSummary language={language} node={node} icon={icon} typeLabel={typeLabel}>
      <CatalogSummaryDetails language={language} title={text(language, '糙率图层设置', 'N-value layer settings')} fields={[
        { id: 'mode', label: text(language, '糙率模式', 'N-value mode') },
        { id: 'classification', label: text(language, '分类图层', 'Classification layer'), value: classification?.name },
      ]} />
    </RasCatalogSummary>
  );
}
