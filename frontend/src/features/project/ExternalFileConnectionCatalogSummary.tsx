import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import type { CatalogTreeNode } from './CatalogTreeSnapshot';
import { CatalogSummaryDetails } from './CatalogSummaryDetails';
import { RasCatalogSummary } from './RasCatalogSummary';

export function ExternalFileConnectionCatalogSummary({ language, node, icon, typeLabel }: {
  language: Language; node: CatalogTreeNode; icon: string; typeLabel: string;
}) {
  const properties = new Map((node.properties ?? []).map((property) => [property.id, property.value]));
  return (
    <RasCatalogSummary
      language={language}
      node={node}
      icon={icon}
      typeLabel={typeLabel}
      filePathOverride={properties.get('AbsolutePath') ?? null}
      showTimestamps={false}
      showDescription={false}
      showExtraProperties={false}
    >
      <CatalogSummaryDetails language={language} title={text(language, '连接信息', 'Connection information')} fields={[
        { id: 'type', label: text(language, '连接类型', 'Connection type'), value: properties.get('Type') },
        { id: 'relative-path', label: text(language, '相对路径', 'Relative path'), value: properties.get('RelativePath') },
      ]} />
    </RasCatalogSummary>
  );
}
