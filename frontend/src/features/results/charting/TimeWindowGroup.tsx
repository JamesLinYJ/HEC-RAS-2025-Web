import { useMemo } from 'react';
import type { ResultDataset, ResultSeries } from '../data';
import { hasCompleteTimeBounds, indexRangeFromNormalized } from './timeWindowMath';
import type { NormalizedTimeRange } from './timeWindowMath';
import './timeControls.css';

interface TimeWindowGroupProps {
  times: string[];
  dataset: ResultDataset | null;
  series: ResultSeries[];
  range: NormalizedTimeRange;
  onRangeChange: (range: NormalizedTimeRange) => void;
  language: 'zh' | 'en';
}

export function TimeWindowGroup({ times, dataset, series, range, onRangeChange, language }: TimeWindowGroupProps) {
  const zh = language === 'zh';
  const [start, end] = indexRangeFromNormalized(range, times.length);
  const usableBounds = hasCompleteTimeBounds(times);
  const coverage = useMemo(() => {
    if (!dataset || series.length === 0) return null;
    let valid = 0;
    let noData = 0;
    for (const item of series) {
      for (let index = start; index <= end; index += 1) {
        if (Number.isFinite(item.values[index])) valid += 1;
        else noData += 1;
      }
    }
    return { valid, noData, total: valid + noData };
  }, [dataset, series, start, end]);

  return <details className="results-time-window-group">
    <summary>
      <span>{zh ? '时间范围摘要' : 'Time window summary'}</span>
      <span>{usableBounds ? `${times[start] ?? ''} — ${times[end] ?? ''}` : (zh ? '起止时刻不可用' : 'Time bounds unavailable')}</span>
      {usableBounds && <span>{(end - start + 1).toLocaleString(zh ? 'zh-CN' : 'en-US')} {zh ? '个时步' : 'time steps'}</span>}
    </summary>
    <div className="results-time-window-group-body">
      <p>{coverage
        ? (zh ? `当前可见序列：${coverage.valid.toLocaleString('zh-CN')} 个有效值，${coverage.noData.toLocaleString('zh-CN')} 个 NoData（共 ${coverage.total.toLocaleString('zh-CN')} 个序列样本）。` : `Visible series: ${coverage.valid.toLocaleString('en-US')} valid values and ${coverage.noData.toLocaleString('en-US')} NoData samples (${coverage.total.toLocaleString('en-US')} series samples).`)
        : !dataset
          ? (zh ? '尚无载入的序列值；摘要仅显示所选结果的原始时步范围。' : 'No series values are loaded; the summary shows only the selected result’s native time steps.')
          : (zh ? '当前没有可见序列，未统计序列样本。' : 'No series are currently visible, so sample coverage is not counted.')}</p>
      <button type="button" onClick={() => onRangeChange([0, 1])} disabled={!usableBounds || (start === 0 && end === times.length - 1)}>{zh ? '显示完整时间范围' : 'Show full time range'}</button>
      {dataset && <span>{dataset.source === 'backend-result' ? (zh ? '结果源只读' : 'Result source is read-only') : (zh ? '用户导入序列' : 'Imported user series')}</span>}
    </div>
  </details>;
}
