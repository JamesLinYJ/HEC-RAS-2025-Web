import type { Language } from '../../../shared/workbench';
import type { DssPairedData } from './types';
import { PairedDataDataTableWindow } from './PairedDataDataTableWindow';

interface PairedDataDataTableViewProps {
  data: DssPairedData;
  pathname: string;
  sourceRevision: string;
  language: Language;
}

const rowLimit = 100;

function sourceValue(value: number | null | undefined): string {
  return value === null || value === undefined || !Number.isFinite(value) ? 'NoData' : String(value);
}

export function PairedDataDataTableView({ data, pathname, sourceRevision, language }: PairedDataDataTableViewProps) {
  const zh = language === 'zh';
  const rows = Math.min(data.ordinates.length, ...data.curves.map((curve) => curve.values.length), rowLimit);
  const actualRows = Math.min(data.ordinates.length, ...data.curves.map((curve) => curve.values.length));

  return <section className="project-dss-paired-table" aria-label={zh ? 'DSS 配对数据表' : 'DSS paired-data table'}>
    <div className="project-dss-visual-heading">
      <strong>{zh ? '源配对数据' : 'Source paired data'}</strong>
      <span>{zh ? `自变量 ${data.independentType || '未提供'}（${data.independentUnits || '单位未提供'}） · 因变量 ${data.dependentType || '未提供'}（${data.dependentUnits || '单位未提供'}）` : `Independent ${data.independentType || 'not provided'} (${data.independentUnits || 'units not provided'}) · dependent ${data.dependentType || 'not provided'} (${data.dependentUnits || 'units not provided'})`}</span>
    </div>
    {data.curves.length === 0 && <p role="status">{zh ? 'Host 返回了配对坐标，但没有曲线值。' : 'The host returned paired ordinates without curve values.'}</p>}
    <div className="project-dss-table-scroll">
      <table>
        <thead><tr><th>{data.independentType || (zh ? '自变量' : 'Independent variable')} ({data.independentUnits || (zh ? '单位未提供' : 'units not provided')})</th>{data.curves.map((curve, index) => <th key={`${curve.label}:${index}`}>{curve.label || `${zh ? '曲线' : 'Curve'} ${index + 1}`} ({data.dependentUnits || (zh ? '单位未提供' : 'units not provided')})</th>)}</tr></thead>
        <tbody>{Array.from({ length: rows }, (_, row) => <tr key={`paired:${row}`}>
          <td>{sourceValue(data.ordinates[row])}</td>{data.curves.map((curve, index) => <td key={`${curve.label}:${index}`}>{sourceValue(curve.values[row])}</td>)}
        </tr>)}</tbody>
      </table>
    </div>
    {actualRows > rowLimit && <small className="project-dss-table-limit">{zh ? `仅显示前 ${rowLimit} 行；来源可对齐 ${actualRows} 行。` : `Showing the first ${rowLimit} of ${actualRows} aligned source rows.`}</small>}
    <PairedDataDataTableWindow data={data} pathname={pathname} sourceRevision={sourceRevision} language={language} />
  </section>;
}
