import type { ResultDataset, ResultSeries } from '../data';
import { ChartView } from '../charting/ChartView';
import type { ChartSettings } from '../charting/chartTypes';

export type { ChartSettings } from '../charting/chartTypes';

interface TimeSeriesPayloadChartProps {
  dataset: ResultDataset | null;
  series: ResultSeries[];
  selectedIndex: number;
  onSelectIndex: (index: number) => void;
  range: [number, number];
  onRangeChange: (range: [number, number]) => void;
  settings: ChartSettings;
  onSettingsChange: (settings: ChartSettings) => void;
  language: 'zh' | 'en';
}

export function TimeSeriesPayloadChart({ dataset, series, selectedIndex, onSelectIndex, range, onRangeChange, settings, onSettingsChange, language }: TimeSeriesPayloadChartProps) {
  return <ChartView
    dataset={dataset}
    series={series}
    selectedIndex={selectedIndex}
    onSelectIndex={onSelectIndex}
    range={range}
    onRangeChange={onRangeChange}
    settings={settings}
    onSettingsChange={onSettingsChange}
    language={language}
  />;
}
