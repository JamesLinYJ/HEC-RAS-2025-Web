import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react';
import type { Language } from '../../../shared/workbench';
import type { DssTimeSeriesData } from './types';
import { copyCsv, downloadCsv, toCsv, type CsvValue } from './csv';
import { DssDataDialog } from './DssDataDialog';

interface TimeSeriesDataTableWindowProps {
  data: DssTimeSeriesData;
  pathname: string;
  sourceRevision: string;
  language: Language;
}

function displaySample(value: number | null | undefined, language: Language): string {
  if (value === undefined) return language === 'zh' ? '未提供' : 'Not provided';
  if (value === null || !Number.isFinite(value)) return 'NoData';
  return String(value);
}

export function TimeSeriesDataTableWindow({ data, pathname, sourceRevision, language }: TimeSeriesDataTableWindowProps) {
  const zh = language === 'zh';
  const [open, setOpen] = useState(false);
  const [pageIndex, setPageIndex] = useState(0);
  const [pageSize, setPageSize] = useState(100);
  const [jumpTarget, setJumpTarget] = useState('1');
  const [jumpedIndex, setJumpedIndex] = useState<number | null>(null);
  const [copyState, setCopyState] = useState<'idle' | 'copied' | 'failed'>('idle');
  const tableScrollRef = useRef<HTMLDivElement>(null);
  const rowCount = Math.max(data.times.length, data.values.length);
  const pageCount = Math.max(1, Math.ceil(rowCount / pageSize));
  const safePageIndex = Math.min(pageIndex, pageCount - 1);
  const firstRow = safePageIndex * pageSize;
  const lastRowExclusive = Math.min(firstRow + pageSize, rowCount);
  useEffect(() => {
    if (jumpedIndex === null) return;
    const row = tableScrollRef.current?.querySelector<HTMLTableRowElement>(`[data-source-index="${jumpedIndex}"]`);
    row?.scrollIntoView({ block: 'center' });
  }, [jumpedIndex, safePageIndex]);
  const csvRows = useMemo(() => {
    const rows: CsvValue[][] = [
      ['Record path', pathname],
      ['Source SHA-256', sourceRevision],
      ['Data type', data.dataType],
      ['Units', data.units],
      [],
      ['Source index', 'Time', `Value (${data.units || ''})`],
    ];
    for (let index = 0; index < rowCount; index += 1) {
      rows.push([index + 1, data.times[index], data.values[index]]);
    }
    return toCsv(rows);
  }, [data.dataType, data.times, data.units, data.values, pathname, rowCount, sourceRevision]);

  const jumpToRow = (event: FormEvent<HTMLFormElement>): void => {
    event.preventDefault();
    const requested = Number.parseInt(jumpTarget, 10);
    if (!Number.isFinite(requested) || rowCount === 0) return;
    const targetRow = Math.min(Math.max(requested, 1), rowCount);
    setJumpTarget(String(targetRow));
    setPageIndex(Math.min(Math.floor((targetRow - 1) / pageSize), pageCount - 1));
    setJumpedIndex(targetRow - 1);
  };

  const copy = async (): Promise<void> => {
    try {
      await copyCsv(csvRows);
      setCopyState('copied');
    } catch {
      setCopyState('failed');
    }
  };

  return <>
    <button type="button" className="project-dss-window-trigger" data-testid="project-dss-timeseries-open-table-window" onClick={() => setOpen(true)}>
      {zh ? '完整时序数据' : 'Full time-series data'}
    </button>
    <DssDataDialog open={open} onClose={() => setOpen(false)} title={zh ? '时序数据表' : 'Time-series data table'} label={zh ? '完整时序数据' : 'Full time-series data'} closeLabel={zh ? '关闭窗口' : 'Close window'}>
      <div className="project-dss-window-source">
        <code>{pathname}</code>
        <small>{zh ? '源 SHA-256' : 'Source SHA-256'} · <code>{sourceRevision}</code></small>
        <span>{data.times.length === data.values.length
          ? (zh ? `${rowCount} 个原始样本 · ${data.dataType || '数据类型未提供'} · ${data.units || '单位未提供'}` : `${rowCount} source samples · ${data.dataType || 'data type not provided'} · ${data.units || 'units not provided'}`)
          : (zh ? `时刻数组 ${data.times.length} 项，数值数组 ${data.values.length} 项；按原索引查看，缺失单元格留空。` : `${data.times.length} timestamps and ${data.values.length} values; shown by source index with missing cells left blank.`)}</span>
      </div>
      <div className="project-dss-window-actions">
        <button type="button" data-testid="project-dss-timeseries-export-csv" onClick={() => downloadCsv('dss-time-series.csv', csvRows)}>{zh ? '下载完整 CSV' : 'Download full CSV'}</button>
        <button type="button" data-testid="project-dss-timeseries-copy-csv" onClick={() => void copy()}>{zh ? '复制完整 CSV' : 'Copy full CSV'}</button>
        {copyState !== 'idle' && <span role="status">{copyState === 'copied' ? (zh ? 'CSV 已复制。' : 'CSV copied.') : (zh ? '复制失败；浏览器未提供剪贴板权限。' : 'Copy failed; clipboard access is unavailable.')}</span>}
        <small>{zh ? 'CSV 对 NoData 保留空单元格。原始质量数组另行显示，不与时序样本作一一配对。' : 'NoData values remain blank in CSV. Raw quality values are shown separately and are not mapped onto samples.'}</small>
      </div>
      <div className="project-dss-window-pagination">
        <span data-testid="project-dss-timeseries-total-rows">{zh ? `共 ${rowCount} 行` : `${rowCount} rows`}</span>
        <label>{zh ? '每页' : 'Rows per page'}
          <select aria-label={zh ? '每页行数' : 'Rows per page'} value={pageSize} onChange={(event) => { setPageSize(Number(event.target.value)); setPageIndex(0); setJumpedIndex(null); }}>
            {[50, 100, 250, 500].map((size) => <option key={size} value={size}>{size}</option>)}
          </select>
        </label>
        <form onSubmit={jumpToRow}>
          <label htmlFor="project-dss-timeseries-jump-row">{zh ? '跳至行' : 'Go to row'}</label>
          <input id="project-dss-timeseries-jump-row" data-testid="project-dss-timeseries-jump-row" aria-label={zh ? '跳至时序行' : 'Go to time-series row'} type="number" min={1} max={Math.max(1, rowCount)} value={jumpTarget} onChange={(event) => setJumpTarget(event.target.value)} />
          <button type="submit">{zh ? '跳转' : 'Go'}</button>
        </form>
        <span aria-live="polite">{rowCount === 0 ? (zh ? '没有样本行' : 'No sample rows') : (zh ? `第 ${firstRow + 1}–${lastRowExclusive} 行 · 第 ${safePageIndex + 1}/${pageCount} 页` : `Rows ${firstRow + 1}–${lastRowExclusive} · page ${safePageIndex + 1}/${pageCount}`)}</span>
        <button type="button" aria-label={zh ? '上一页' : 'Previous page'} disabled={safePageIndex <= 0} onClick={() => setPageIndex(Math.max(0, safePageIndex - 1))}>{zh ? '上一页' : 'Previous'}</button>
        <button type="button" aria-label={zh ? '下一页' : 'Next page'} disabled={safePageIndex >= pageCount - 1} onClick={() => setPageIndex(Math.min(pageCount - 1, safePageIndex + 1))}>{zh ? '下一页' : 'Next'}</button>
      </div>
      <div ref={tableScrollRef} className="project-dss-table-scroll project-dss-window-table-scroll">
        <table data-testid="project-dss-timeseries-full-table">
          <thead><tr><th>{zh ? '源索引' : 'Source index'}</th><th>{zh ? '原始时刻' : 'Source time'}</th><th>{zh ? `值（${data.units || '单位未提供'}）` : `Value (${data.units || 'units not provided'})`}</th></tr></thead>
          <tbody>{Array.from({ length: lastRowExclusive - firstRow }, (_, offset) => {
            const index = firstRow + offset;
            return <tr key={`source:${index}`} data-source-index={index} aria-current={jumpedIndex === index ? 'true' : undefined} className={jumpedIndex === index ? 'is-jump-target' : undefined}>
              <td>{index + 1}</td><td>{data.times[index] ?? (zh ? '未提供' : 'Not provided')}</td>
              <td className={data.values[index] === null ? 'is-nodata' : undefined}>{displaySample(data.values[index], language)}</td>
            </tr>;
          })}</tbody>
        </table>
      </div>
      {data.qualities && <details className="project-dss-source-qualities project-dss-window-qualities">
        <summary>{zh ? `原始质量数组（${data.qualities.length} 项）` : `Raw quality array (${data.qualities.length})`}</summary>
        <pre>{JSON.stringify(data.qualities)}</pre>
        <small>{zh ? '保留 Host 返回顺序和长度，不推断质量位与样本的对应关系。' : 'The host array order and length are preserved; no quality-to-sample mapping is inferred.'}</small>
      </details>}
    </DssDataDialog>
  </>;
}
