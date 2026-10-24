import type { Language } from '../../../shared/workbench';
import type { ResultSeries } from '../data';

interface ScrubbedDataDisplayProps {
  time: string | null;
  index: number;
  series: ResultSeries[];
  language: Language;
}

/** Readout counterpart to the source ScrubbedDataDisplay, driven by the chart's selected sample. */
export function ScrubbedDataDisplay({ time, index, series, language }: ScrubbedDataDisplayProps) {
  if (time === null || series.length === 0) return null;
  const zh = language === 'zh';
  return <section className="results-scrubbed-data" data-testid="results-scrubbed-data" aria-live="polite">
    <div className="results-scrubbed-heading">
      <span>{zh ? '选中时步' : 'Selected sample'}</span>
      <time>{time}</time>
      <small>{zh ? `第 ${index + 1} 个原始样本` : `Raw sample ${index + 1}`}</small>
    </div>
    <div className="results-scrubbed-values">
      {series.map((item) => {
        const value = item.values[index];
        return <div key={item.id} className="results-scrubbed-value" title={`${item.name}${item.units ? ` (${item.units})` : ''}`}>
          <span>{item.name}</span>
          <strong>{value === null || value === undefined ? (zh ? '无数据' : 'NoData') : String(value)}</strong>
          {item.units && <small>{item.units}</small>}
        </div>;
      })}
    </div>
  </section>;
}
