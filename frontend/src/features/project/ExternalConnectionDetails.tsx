import type { BackendGateway, Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import type { CatalogTreeNode } from './CatalogTreeSnapshot';
import { CatalogSummaryDetails } from './CatalogSummaryDetails';
import { DssExternalConnectionPreview } from './DssExternalConnectionPreview';
import { RasCatalogSummary } from './RasCatalogSummary';

export function ExternalConnectionDetails({ language, backend, node, icon, typeLabel }: {
  language: Language;
  backend: BackendGateway;
  node: CatalogTreeNode;
  icon: string;
  typeLabel: string;
}) {
  const properties = new Map((node.properties ?? []).map((property) => [property.id, property.value]));
  const absolutePath = properties.get('AbsolutePath') ?? '';
  const isDssFile = node.sourceClass === 'ExternalFileConnectionCatalogNode' && absolutePath.toLowerCase().endsWith('.dss');
  return (
    <RasCatalogSummary
      language={language}
      node={node}
      icon={icon}
      typeLabel={typeLabel}
      filePathOverride={absolutePath || null}
      showTimestamps={false}
      showDescription={false}
      showExtraProperties={false}
    >
      <CatalogSummaryDetails language={language} title={text(language, '连接信息', 'Connection details')} fields={[
        { id: 'type', label: text(language, '连接类型', 'Connection type'), value: properties.get('Type') },
        { id: 'description', label: text(language, '说明', 'Description'), value: properties.get('Description') },
        { id: 'relative-path', label: text(language, '相对路径', 'Relative path'), value: properties.get('RelativePath') },
        { id: 'absolute-path', label: text(language, '绝对路径', 'Absolute path'), value: absolutePath },
        { id: 'search-pattern', label: text(language, '文件名模式', 'Filename pattern'), value: properties.get('SearchPattern') },
        { id: 'recurse', label: text(language, '包含子目录', 'Include subfolders'), value: properties.get('Recurse') },
      ]} />
      {isDssFile && <DssExternalConnectionPreview language={language} backend={backend} path={absolutePath} />}
    </RasCatalogSummary>
  );
}
