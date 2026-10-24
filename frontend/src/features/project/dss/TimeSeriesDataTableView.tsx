import type { Language } from '../../../shared/workbench';
import type { DssTimeSeriesData } from './types';
import { TimeSeriesDataTableWindow } from './TimeSeriesDataTableWindow';

interface TimeSeriesDataTableViewProps {
  data: DssTimeSeriesData;
  pathname: string;
  sourceRevision: string;
  language: Language;
}

const rowLimit = 100;

function sourceValue(value: number | null | undefined, language: Language): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return language === 'zh' ? 'NoData' : 'NoData';
  return String(value);
}

export function TimeSeriesDataTableView({ data, pathname, sourceRevision, language }: TimeSeriesDataTableViewProps) {
  const zh = language === 'zh';
  const rowCount = Math.min(data.times.length, data.values.length);
  const visibleCount = Math.min(rowCount, rowLimit);

  return <section className="project-dss-time-series-table" aria-label={zh ? 'DSS 时序数据表' : 'DSS time-series data table'}>
    <div className="project-dss-visual-heading">
      <strong>{zh ? '源时序数据' : 'Source time-series data'}</strong>
      <span>{zh ? `${rowCount} 个时刻 · ${data.units || '单位未提供'} · ${data.dataType || '数据类型未提供'}` : `${rowCount} timestamps · ${data.units || 'units not provided'} · ${data.dataType || 'data type not provided'}`}</span>
    </div>
    <div className="project-dss-table-scroll">
      <table>
        <thead><tr><th>{zh ? '原始时刻' : 'Source time'}</th><th>{zh ? `值（${data.units || '单位未提供'}）` : `Value (${data.units || 'units not provided'})`}</th></tr></thead>
        <tbody>{Array.from({ length: visibleCount }, (_, index) => <tr key={`${data.times[index] ?? 'missing-time'}:${index}`}>
          <td>{data.times[index] ?? '—'}</td><td className={data.values[index] === null || data.values[index] === undefined ? 'is-nodata' : undefined}>{sourceValue(data.values[index], language)}</td>
        </tr>)}</tbody>
      </table>
    </div>
    {rowCount > rowLimit && <small className="project-dss-table-limit">{zh ? `仅显示前 ${rowLimit} 项；来源共 ${rowCount} 项。` : `Showing the first ${rowLimit} of ${rowCount} source rows.`}</small>}
    {data.qualities && <details className="project-dss-source-qualities">
      <summary>{zh ? `原始质量数组（${data.qualities.length} 项）` : `Raw quality array (${data.qualities.length})`}</summary>
      <pre>{JSON.stringify(data.qualities)}</pre>
      <small>{zh ? '保留 Host 返回的原始数组；未假设其质量位与单个样本一一对应。' : 'The host array is preserved as returned; no one-to-one sample mapping is assumed.'}</small>
    </details>}
    <TimeSeriesDataTableWindow data={data} pathname={pathname} sourceRevision={sourceRevision} language={language} />
  </section>;
}
