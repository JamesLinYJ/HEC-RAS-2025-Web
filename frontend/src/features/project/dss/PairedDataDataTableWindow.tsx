import { useMemo, useState } from 'react';
import type { Language } from '../../../shared/workbench';
import type { DssPairedData } from './types';
import { copyCsv, downloadCsv, toCsv, type CsvValue } from './csv';
import { DssDataDialog } from './DssDataDialog';

interface PairedDataDataTableWindowProps {
  data: DssPairedData;
  pathname: string;
  sourceRevision: string;
  language: Language;
}

function displaySample(value: number | null | undefined, language: Language): string {
  if (value === undefined) return language === 'zh' ? '未提供' : 'Not provided';
  if (value === null || !Number.isFinite(value)) return 'NoData';
  return String(value);
}

export function PairedDataDataTableWindow({ data, pathname, sourceRevision, language }: PairedDataDataTableWindowProps) {
  const zh = language === 'zh';
  const [open, setOpen] = useState(false);
  const [copyState, setCopyState] = useState<'idle' | 'copied' | 'failed'>('idle');
  const rowCount = Math.max(data.ordinates.length, ...data.curves.map((curve) => curve.values.length), 0);
  const csvRows = useMemo(() => {
    const header = [
      'Source index',
      `${data.independentType || 'Independent'} (${data.independentUnits || ''})`,
      ...data.curves.map((curve, index) => `${curve.label || `Curve ${index + 1}`} (${data.dependentUnits || ''})`),
    ];
    const rows: CsvValue[][] = [
      ['Record path', pathname],
      ['Source SHA-256', sourceRevision],
      ['Independent type', data.independentType],
      ['Independent units', data.independentUnits],
      ['Dependent type', data.dependentType],
      ['Dependent units', data.dependentUnits],
      [],
      header,
    ];
    for (let index = 0; index < rowCount; index += 1) {
      rows.push([index + 1, data.ordinates[index], ...data.curves.map((curve) => curve.values[index])]);
    }
    return toCsv(rows);
  }, [data.curves, data.dependentType, data.dependentUnits, data.independentType, data.independentUnits, data.ordinates, pathname, rowCount, sourceRevision]);

  const copy = async (): Promise<void> => {
    try {
      await copyCsv(csvRows);
      setCopyState('copied');
    } catch {
      setCopyState('failed');
    }
  };

  const lengthsMatch = data.curves.every((curve) => curve.values.length === data.ordinates.length);

  return <>
    <button type="button" className="project-dss-window-trigger" data-testid="project-dss-paired-open-table-window" onClick={() => setOpen(true)}>
      {zh ? '完整配对数据' : 'Full paired data'}
    </button>
    <DssDataDialog open={open} onClose={() => setOpen(false)} title={zh ? '配对数据表' : 'Paired-data table'} label={zh ? '完整配对数据' : 'Full paired data'} closeLabel={zh ? '关闭窗口' : 'Close window'}>
      <div className="project-dss-window-source">
        <code>{pathname}</code>
        <small>{zh ? '源 SHA-256' : 'Source SHA-256'} · <code>{sourceRevision}</code></small>
        <span>{zh
          ? `自变量 ${data.independentType || '未提供'}（${data.independentUnits || '单位未提供'}） · 因变量 ${data.dependentType || '未提供'}（${data.dependentUnits || '单位未提供'}） · ${rowCount} 个源索引 · ${data.curves.length} 条实际曲线`
          : `Independent ${data.independentType || 'not provided'} (${data.independentUnits || 'units not provided'}) · dependent ${data.dependentType || 'not provided'} (${data.dependentUnits || 'units not provided'}) · ${rowCount} source indexes · ${data.curves.length} returned curves`}</span>
        {!lengthsMatch && <p role="status">{zh ? 'Host 返回的自变量和曲线数组长度不同。表格按源索引展示；缺失单元格留空，不补造数据。' : 'The host arrays have different lengths. Rows use source indexes; missing cells stay empty and are not synthesized.'}</p>}
        {data.curves.length === 0 && <p role="status">{zh ? 'Host 未返回曲线值；表格仍保留全部实际自变量。' : 'The host returned no curve values; all returned independent values remain available.'}</p>}
      </div>
      <div className="project-dss-window-actions">
        <button type="button" data-testid="project-dss-paired-export-csv" onClick={() => downloadCsv('dss-paired-data.csv', csvRows)}>{zh ? '下载完整 CSV' : 'Download full CSV'}</button>
        <button type="button" data-testid="project-dss-paired-copy-csv" onClick={() => void copy()}>{zh ? '复制完整 CSV' : 'Copy full CSV'}</button>
        {copyState !== 'idle' && <span role="status">{copyState === 'copied' ? (zh ? 'CSV 已复制。' : 'CSV copied.') : (zh ? '复制失败；浏览器未提供剪贴板权限。' : 'Copy failed; clipboard access is unavailable.')}</span>}
        <small>{zh ? 'NoData 在 CSV 中为空单元格；自变量及每条曲线均按 Host 数组索引保留。' : 'NoData is blank in CSV; the independent values and every returned curve keep their source indexes.'}</small>
      </div>
      <div className="project-dss-table-scroll project-dss-window-table-scroll">
        <table data-testid="project-dss-paired-full-table">
          <thead><tr><th>{zh ? '源索引' : 'Source index'}</th><th>{data.independentType || (zh ? '自变量' : 'Independent')} ({data.independentUnits || (zh ? '单位未提供' : 'units not provided')})</th>{data.curves.map((curve, index) => <th key={`${curve.label}:${index}`}>{curve.label || `${zh ? '曲线' : 'Curve'} ${index + 1}`} ({data.dependentUnits || (zh ? '单位未提供' : 'units not provided')})</th>)}</tr></thead>
          <tbody>{Array.from({ length: rowCount }, (_, index) => <tr key={`source:${index}`} data-source-index={index}>
            <td>{index + 1}</td><td className={data.ordinates[index] === null ? 'is-nodata' : undefined}>{displaySample(data.ordinates[index], language)}</td>
            {data.curves.map((curve, curveIndex) => <td key={`${curve.label}:${curveIndex}`} className={curve.values[index] === null ? 'is-nodata' : undefined}>{displaySample(curve.values[index], language)}</td>)}
          </tr>)}</tbody>
        </table>
      </div>
    </DssDataDialog>
  </>;
}
