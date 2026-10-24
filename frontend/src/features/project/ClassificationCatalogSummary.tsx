import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import type { CatalogTreeNode } from './CatalogTreeSnapshot';
import { RasCatalogSummary } from './RasCatalogSummary';

export function ClassificationCatalogSummary({ language, node, icon, typeLabel }: {
  language: Language; node: CatalogTreeNode; icon: string; typeLabel: string;
}) {
  const inputs = node.children ?? [];
  return (
    <RasCatalogSummary language={language} node={node} icon={icon} typeLabel={typeLabel}>
      <section className="project-detail-properties">
        <h4>{text(language, '分类输入顺序', 'Classification input order')}</h4>
        {inputs.length > 0
          ? <ol>{inputs.map((input) => <li key={input.id}>{input.name}</li>)}</ol>
          : <div className="project-catalog-description"><p className="is-missing">{text(language, '目录未提供分类输入项。', 'The catalog did not provide classification inputs.')}</p></div>}
      </section>
    </RasCatalogSummary>
  );
}
