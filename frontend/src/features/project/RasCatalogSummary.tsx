import type { ReactNode } from 'react';
import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import type { CatalogTreeNode } from './CatalogTreeSnapshot';
import { CatalogFilePathControl } from './CatalogFilePathControl';
import { RasCatalogSummaryHeader } from './RasCatalogSummaryHeader';

const COMMON_PROPERTY_IDS = new Set(['Name', 'Filename', 'DateCreated', 'DateLastModified', 'Description']);

function readLayerError(error: unknown): { code: string; severity: string; message: string } | null {
  if (error === null || typeof error !== 'object') return null;
  const record = error as Record<string, unknown>;
  const stringValue = (key: string): string => typeof record[key] === 'string' ? record[key] as string : '';
  return {
    code: stringValue('code'),
    severity: stringValue('severity'),
    message: stringValue('evaluatedMessage'),
  };
}

function propertyLabel(language: Language, id: string, sourceLabel: string): string {
  if (language !== 'zh') return sourceLabel;
  const labels: Record<string, string> = {
    Name: '名称',
    Filename: '文件路径',
    DateCreated: '创建时间',
    DateLastModified: '修改时间',
    Description: '说明',
    Kind: '类型',
    'PlanAssociation/Geometry': '关联几何',
    'PlanAssociation/NValueMode': '糙率模式',
    'PlanAssociation/Classification': '分类图层',
    'BoundaryCondition/StartTime': '开始时间',
    'BoundaryCondition/EndTime': '结束时间',
    'TerrainInput/SourceClass': '来源类型',
    'TerrainInput/Index': '来源序号',
  };
  if (labels[id]) return labels[id];
  return /[\u3400-\u9fff]/.test(sourceLabel) ? sourceLabel : '其他属性';
}

function layerErrorSeverity(language: Language, sourceSeverity: string): string {
  if (language !== 'zh') return sourceSeverity;
  const labels: Record<string, string> = { error: '错误', warning: '警告', info: '信息' };
  return labels[sourceSeverity.toLowerCase()] ?? '诊断';
}

export interface RasCatalogSummaryProps {
  language: Language;
  node: CatalogTreeNode;
  icon: string;
  typeLabel: string;
  consumedPropertyIds?: ReadonlySet<string>;
  filePathOverride?: string | null;
  dateCreatedOverride?: string | null;
  dateLastModifiedOverride?: string | null;
  showTimestamps?: boolean;
  showDescription?: boolean;
  showExtraProperties?: boolean;
  children?: ReactNode;
}

/** Common source metadata shared by the original catalog summary controls. */
export function RasCatalogSummary({
  language,
  node,
  icon,
  typeLabel,
  consumedPropertyIds,
  filePathOverride,
  dateCreatedOverride,
  dateLastModifiedOverride,
  showTimestamps = true,
  showDescription = true,
  showExtraProperties = true,
  children,
}: RasCatalogSummaryProps) {
  const summary = node.catalogSummary;
  const propertyValue = (id: string) => node.properties?.find((property) => property.id === id)?.value;
  const filePath = filePathOverride !== undefined ? filePathOverride : summary?.filename || node.backupSummary?.filename || node.locator?.filename ||
    node.locator?.filenameRelative || propertyValue('Filename') || null;
  const dateCreated = dateCreatedOverride !== undefined ? dateCreatedOverride : summary?.dateCreated || node.backupSummary?.dateCreated || propertyValue('DateCreated') || null;
  const dateLastModified = dateLastModifiedOverride !== undefined ? dateLastModifiedOverride : summary?.dateLastModified || node.backupSummary?.dateLastModified || propertyValue('DateLastModified') || null;
  const description = summary?.description || propertyValue('Description') || null;
  const layerErrors = (node.layerErrors ?? []).map(readLayerError).filter((value): value is NonNullable<typeof value> => value !== null);
  const extraProperties = showExtraProperties
    ? (node.properties ?? []).filter((property) => !COMMON_PROPERTY_IDS.has(property.id) && !consumedPropertyIds?.has(property.id))
    : [];
  const title = summary?.name || node.name;

  return (
    <div className="project-tree-detail">
      <RasCatalogSummaryHeader language={language} icon={icon} typeLabel={typeLabel} title={title} />
      <CatalogFilePathControl language={language} filePath={filePath} dateCreated={dateCreated} dateLastModified={dateLastModified} showTimestamps={showTimestamps} />
      {showDescription && <section className="project-catalog-description">
        <h4>{text(language, '说明', 'Description')}</h4>
        <p className={!description ? 'is-missing' : ''}>{description || text(language, '未提供', 'Not provided')}</p>
      </section>}
      {children}
      {extraProperties.length > 0 && <section className="project-detail-properties">
        <h4>{text(language, '属性', 'Properties')}</h4>
        <dl>{extraProperties.map((property) => <div key={property.id}><dt>{propertyLabel(language, property.id, property.label)}</dt><dd>{property.value || text(language, '未提供', 'Not provided')}</dd></div>)}</dl>
      </section>}
      {layerErrors.length > 0 && <section className="project-catalog-layer-errors">
        <h4>{text(language, '图层错误', 'Layer errors')}</h4>
        <ul>{layerErrors.map((error, index) => <li key={`${error.code}:${index}`}>
          {(error.severity || error.code) && <strong>{[error.severity ? layerErrorSeverity(language, error.severity) : '', error.code].filter(Boolean).join(' · ')}</strong>}
          <p>{error.message || text(language, '未提供错误说明', 'Error message not provided')}</p>
        </li>)}</ul>
      </section>}
      {node.assetID && <div className="project-detail-path"><span>{text(language, '资产身份', 'Asset identity')}</span><code>{node.assetID}</code></div>}
      {node.locator && <details className="project-detail-technical"><summary>{text(language, '定位器', 'Locator')}</summary><pre>{JSON.stringify(node.locator, null, 2)}</pre></details>}
    </div>
  );
}
