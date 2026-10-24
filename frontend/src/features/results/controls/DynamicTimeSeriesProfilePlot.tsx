import type { Language } from '../../../shared/workbench';
import type { ResultDataset, ResultSeries } from '../data';
import { TimeSeriesPayloadChart } from './TimeSeriesPayloadChart';
import type { ChartSettings } from './TimeSeriesPayloadChart';

interface DynamicTimeSeriesProfilePlotProps {
  dataset: ResultDataset;
  series: ResultSeries[];
  selectedIndex: number;
  onSelectIndex: (index: number) => void;
  range: [number, number];
  onRangeChange: (range: [number, number]) => void;
  settings: ChartSettings;
  onSettingsChange: (settings: ChartSettings) => void;
  language: Language;
}

/** Plots the actual Result.profile values for the selected mesh element across native output times. */
export function DynamicTimeSeriesProfilePlot(props: DynamicTimeSeriesProfilePlotProps) {
  const { dataset, series, language } = props;
  const zh = language === 'zh';

  return <figure className="results-dynamic-profile-plot">
    <figcaption>
      <span>{zh ? '动态结果时间序列' : 'Dynamic Result time series'}</span>
      <strong>{dataset.name}</strong>
      <small>{zh ? '数据来自已选结果文件的真实 HDF 读取' : 'Values read from the selected Result HDF file'} · {series.length} {zh ? '条载入序列' : 'loaded series'}</small>
    </figcaption>
    <TimeSeriesPayloadChart {...props} />
  </figure>;
}
