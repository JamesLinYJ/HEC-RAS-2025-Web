import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import type { CatalogTreeNode } from './CatalogTreeSnapshot';
import { RasCatalogSummary } from './RasCatalogSummary';

const PROPERTY_IDS = new Set(['BoundaryCondition/StartTime', 'BoundaryCondition/EndTime']);

export function BoundaryConditionCatalogSummary({ language, node, icon, typeLabel }: {
  language: Language; node: CatalogTreeNode; icon: string; typeLabel: string;
}) {
  const properties = new Map((node.properties ?? []).map((property) => [property.id, property.value]));
  const missing = text(language, '未提供', 'Not provided');
  return (
    <RasCatalogSummary language={language} node={node} icon={icon} typeLabel={typeLabel} consumedPropertyIds={PROPERTY_IDS}>
      <section className="project-detail-properties">
        <h4>{text(language, '时间范围', 'Time range')}</h4>
        <dl>
          <div><dt>{text(language, '开始时间', 'Start time')}</dt><dd>{properties.get('BoundaryCondition/StartTime') || missing}</dd></div>
          <div><dt>{text(language, '结束时间', 'End time')}</dt><dd>{properties.get('BoundaryCondition/EndTime') || missing}</dd></div>
        </dl>
      </section>
    </RasCatalogSummary>
  );
}
