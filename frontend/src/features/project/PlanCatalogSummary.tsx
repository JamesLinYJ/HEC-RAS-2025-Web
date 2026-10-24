import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import type { CatalogTreeNode } from './CatalogTreeSnapshot';
import { RasCatalogSummary } from './RasCatalogSummary';

const PROPERTY_IDS = new Set([
  'PlanAssociation/Geometry',
  'PlanAssociation/NValueMode',
  'PlanAssociation/Classification',
]);

export function PlanCatalogSummary({ language, node, icon, typeLabel }: {
  language: Language; node: CatalogTreeNode; icon: string; typeLabel: string;
}) {
  const properties = new Map((node.properties ?? []).map((property) => [property.id, property.value]));
  const missing = text(language, '未提供', 'Not provided');
  const association = (id: string) => {
    const value = properties.get(id);
    return value === undefined ? missing : value.trim() ? value : text(language, '无关联', 'Not associated');
  };
  return (
    <RasCatalogSummary language={language} node={node} icon={icon} typeLabel={typeLabel} consumedPropertyIds={PROPERTY_IDS}>
      <section className="project-detail-properties">
        <h4>{text(language, '关联对象', 'Associations')}</h4>
        <dl>
          <div><dt>{text(language, '关联几何', 'Associated geometry')}</dt><dd>{association('PlanAssociation/Geometry')}</dd></div>
          <div><dt>{text(language, '糙率模式', 'N-value mode')}</dt><dd>{association('PlanAssociation/NValueMode')}</dd></div>
          <div><dt>{text(language, '分类图层', 'Classification layer')}</dt><dd>{association('PlanAssociation/Classification')}</dd></div>
        </dl>
      </section>
    </RasCatalogSummary>
  );
}
