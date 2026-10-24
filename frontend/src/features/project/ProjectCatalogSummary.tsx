import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import type { CatalogTreeNode } from './CatalogTreeSnapshot';
import { CatalogSummaryDetails } from './CatalogSummaryDetails';
import { RasCatalogSummary } from './RasCatalogSummary';

const PROPERTY_IDS = new Set([
  'ProjectCount/Terrains', 'ProjectCount/Geometries', 'ProjectCount/Plans',
  'ProjectCount/Results', 'ProjectCount/BoundaryConditions', 'ProjectCount/NValues',
  'ProjectCount/Classifications', 'Project/Units', 'Project/ProjectionName', 'Project/ProjectionWKT',
]);

export function ProjectCatalogSummary({ language, node, icon, typeLabel }: {
  language: Language; node: CatalogTreeNode; icon: string; typeLabel: string;
}) {
  const properties = new Map((node.properties ?? []).map((property) => [property.id, property.value]));
  const wkt = properties.get('Project/ProjectionWKT');
  return (
    <RasCatalogSummary language={language} node={node} icon={icon} typeLabel={typeLabel}
      consumedPropertyIds={PROPERTY_IDS} showTimestamps={false} showExtraProperties={false}>
      <CatalogSummaryDetails language={language} title={text(language, '工程信息', 'Project information')} fields={[
        { id: 'units', label: text(language, '单位制', 'Units'), value: properties.get('Project/Units') },
        { id: 'projection-name', label: text(language, '投影', 'Projection'), value: properties.get('Project/ProjectionName') },
        { id: 'terrain-count', label: text(language, '地形数量', 'Terrains'), value: properties.get('ProjectCount/Terrains') },
        { id: 'geometry-count', label: text(language, '几何数量', 'Geometries'), value: properties.get('ProjectCount/Geometries') },
        { id: 'plan-count', label: text(language, '计划数量', 'Plans'), value: properties.get('ProjectCount/Plans') },
        { id: 'result-count', label: text(language, '结果数量', 'Results'), value: properties.get('ProjectCount/Results') },
        { id: 'boundary-count', label: text(language, '边界条件数量', 'Boundary conditions'), value: properties.get('ProjectCount/BoundaryConditions') },
        { id: 'nvalue-count', label: text(language, '糙率图层数量', 'N-value layers'), value: properties.get('ProjectCount/NValues') },
        { id: 'classification-count', label: text(language, '分类图层数量', 'Classification layers'), value: properties.get('ProjectCount/Classifications') },
      ]} />
      <details className="project-detail-technical">
        <summary>{text(language, '投影 WKT', 'Projection WKT')}</summary>
        <pre>{wkt?.trim() ? wkt : text(language, '未提供', 'Not provided')}</pre>
      </details>
    </RasCatalogSummary>
  );
}
