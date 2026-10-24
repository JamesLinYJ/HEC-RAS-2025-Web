import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import type { CatalogTreeNode } from './CatalogTreeSnapshot';
import { CatalogSummaryDetails } from './CatalogSummaryDetails';
import { RasCatalogSummary } from './RasCatalogSummary';

export function ResultCatalogSummary({ language, node, icon, typeLabel }: {
  language: Language; node: CatalogTreeNode; icon: string; typeLabel: string;
}) {
  const children = node.children ?? [];
  const plan = children.find((child) => child.sourceClass === 'ObservablePlan');
  const geometryAssociation = plan?.properties?.find((property) => property.id === 'PlanAssociation/Geometry');
  const geometryName = geometryAssociation
    ? geometryAssociation.value.trim() ? geometryAssociation.value : text(language, '无关联', 'Not associated')
    : undefined;
  const boundaryCondition = children.find((child) => child.sourceClass === 'ObservableBoundaryCondition');

  return (
    <RasCatalogSummary language={language} node={node} icon={icon} typeLabel={typeLabel}
      dateCreatedOverride={plan?.catalogSummary?.dateCreated ?? null}
      dateLastModifiedOverride={plan?.catalogSummary?.dateLastModified ?? null}
      showDescription={false} showExtraProperties={false}>
      <CatalogSummaryDetails language={language} title={text(language, '结果关联', 'Result associations')} fields={[
        { id: 'geometry', label: text(language, '几何', 'Geometry'), value: geometryName },
        { id: 'boundary-condition', label: text(language, '边界条件', 'Boundary condition'), value: boundaryCondition?.name ?? text(language, '无关联', 'Not associated') },
      ]} />
    </RasCatalogSummary>
  );
}
