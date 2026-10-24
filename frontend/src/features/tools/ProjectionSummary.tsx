import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';

export interface ProjectionAreaBounds {
  left: number;
  right: number;
  bottom: number;
  top: number;
}

export interface HostProjectionSummary {
  option: string;
  projectionName: string | null;
  validity: string | null;
  unitName: string | null;
  isVertical: boolean;
  isProjected: boolean;
  epsgCode: string | null;
  utmZone: string | null;
  areaOfUse: { geographic: ProjectionAreaBounds; projected: ProjectionAreaBounds } | null;
}

export function parseHostProjectionSummary(value: unknown): HostProjectionSummary | null {
  if (value === undefined || value === null) return null;
  const row = record(value, 'Host 投影摘要');
  if (typeof row.option !== 'string' || typeof row.isVertical !== 'boolean' || typeof row.isProjected !== 'boolean') {
    throw new Error('Host 投影摘要缺少选项或投影属性。');
  }
  const area = row.areaOfUse === undefined || row.areaOfUse === null
    ? null
    : parseArea(row.areaOfUse);
  return {
    option: row.option,
    projectionName: nullableString(row.projectionName, 'projectionName'),
    validity: nullableString(row.validity, 'validity'),
    unitName: nullableString(row.unitName, 'unitName'),
    isVertical: row.isVertical,
    isProjected: row.isProjected,
    epsgCode: nullableScalarString(row.epsgCode, 'epsgCode'),
    utmZone: nullableScalarString(row.utmZone, 'utmZone'),
    areaOfUse: area,
  };
}

export function ProjectionSummary({ language, title, summary }: {
  language: Language;
  title: string;
  summary: HostProjectionSummary | null | undefined;
}) {
  return <section className="gp-ui-projection-summary" aria-label={title}>
    <div className="gp-ui-panel-heading"><h4>{title}</h4><small>{summary ? `Host · ${summary.option}` : 'Host'}</small></div>
    {summary ? <>
      <dl className="gp-ui-projection-summary-fields">
        <div><dt>{text(language, '投影名称', 'Projection')}</dt><dd>{summary.projectionName || '—'}</dd></div>
        <div><dt>{text(language, '有效性', 'Validity')}</dt><dd>{summary.validity || '—'}</dd></div>
        <div><dt>{text(language, '单位', 'Units')}</dt><dd>{summary.unitName || '—'}</dd></div>
        <div><dt>EPSG</dt><dd>{summary.epsgCode || '—'}</dd></div>
        <div><dt>{text(language, 'UTM 分区', 'UTM zone')}</dt><dd>{summary.utmZone || '—'}</dd></div>
        <div><dt>{text(language, '投影坐标系', 'Projected')}</dt><dd>{yesNo(summary.isProjected, language)}</dd></div>
        <div><dt>{text(language, '垂直坐标系', 'Vertical')}</dt><dd>{yesNo(summary.isVertical, language)}</dd></div>
      </dl>
      {summary.areaOfUse && <details className="gp-ui-projection-area">
        <summary>{text(language, '适用范围（Host）', 'Area of use (Host)')}</summary>
        <div><strong>{text(language, '地理坐标', 'Geographic')}</strong><span>{formatBounds(summary.areaOfUse.geographic)}</span></div>
        <div><strong>{text(language, '投影坐标', 'Projected')}</strong><span>{formatBounds(summary.areaOfUse.projected)}</span></div>
      </details>}
    </> : <p className="gp-ui-hint">{text(language, 'Host 尚未返回已解析的投影摘要；此处不会根据文本推断 CRS。', 'The Host has not returned an evaluated projection summary. No CRS is inferred from the text.')}</p>}
  </section>;
}

function parseArea(value: unknown): HostProjectionSummary['areaOfUse'] {
  const row = record(value, 'Host 投影适用范围');
  return { geographic: parseBounds(row.geographic), projected: parseBounds(row.projected) };
}

function parseBounds(value: unknown): ProjectionAreaBounds {
  const row = record(value, 'Host 投影范围');
  const bounds = { left: numberField(row.left), right: numberField(row.right), bottom: numberField(row.bottom), top: numberField(row.top) };
  if (bounds.right < bounds.left || bounds.top < bounds.bottom) throw new Error('Host 投影范围边界顺序无效。');
  return bounds;
}

function record(value: unknown, description: string): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) throw new Error(`${description}不是有效对象。`);
  return value as Record<string, unknown>;
}

function nullableString(value: unknown, field: string): string | null {
  if (value === undefined || value === null) return null;
  if (typeof value !== 'string') throw new Error(`Host 投影摘要中的 ${field} 无效。`);
  return value;
}

function nullableScalarString(value: unknown, field: string): string | null {
  if (value === undefined || value === null) return null;
  if (typeof value === 'string' || typeof value === 'number') return String(value);
  throw new Error(`Host 投影摘要中的 ${field} 无效。`);
}

function numberField(value: unknown): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) throw new Error('Host 投影范围含无效坐标。');
  return value;
}

function yesNo(value: boolean, language: Language): string {
  return language === 'zh' ? value ? '是' : '否' : value ? 'Yes' : 'No';
}

function formatBounds(bounds: ProjectionAreaBounds): string {
  return `L ${bounds.left} · R ${bounds.right} · B ${bounds.bottom} · T ${bounds.top}`;
}
