import type { Language } from '../../../shared/workbench';
import type { ResultDataset, ResultSeries } from '../data';
import { DynamicTimeSeriesProfilePlot } from './DynamicTimeSeriesProfilePlot';
import { TimeSeriesPayloadChart } from './TimeSeriesPayloadChart';
import type { ChartSettings } from './TimeSeriesPayloadChart';

interface MapResultsChartProps {
  dataset: ResultDataset | null;
  series: ResultSeries[];
  selectedIndex: number;
  onSelectIndex: (index: number) => void;
  range: [number, number];
  onRangeChange: (range: [number, number]) => void;
  settings: ChartSettings;
  onSettingsChange: (settings: ChartSettings) => void;
  language: Language;
}

/** Dispatches actual HDF Result series to the dynamic profile plot and imported files to the general chart. */
export function MapResultsChart(props: MapResultsChartProps) {
  if (props.dataset?.source === 'backend-result') {
    return <DynamicTimeSeriesProfilePlot {...props} dataset={props.dataset} />;
  }
  return <TimeSeriesPayloadChart {...props} />;
}
