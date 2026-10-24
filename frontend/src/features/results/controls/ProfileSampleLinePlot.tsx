import { useState } from 'react';
import type { Language } from '../../../shared/workbench';
import type { ResultDataset, ResultSeries } from '../data';
import { TimeSeriesPayloadChart } from './TimeSeriesPayloadChart';
import { ScrubbedDataDisplay } from './ScrubbedDataDisplay';
import type { ChartSettings } from '../charting/chartTypes';

interface ProfileSampleLinePlotProps {
  dataset: ResultDataset;
  series: ResultSeries[];
  language: Language;
}

const defaultSettings: ChartSettings = { grid: true, markers: false, step: false, yMin: '', yMax: '' };

/** Time plot returned by Result.ComputeFlowTimeSeriesAsync for an explicitly entered sample line. */
export function ProfileSampleLinePlot({ dataset, series, language }: ProfileSampleLinePlotProps) {
  const [selectedIndex, setSelectedIndex] = useState(0);
  const [range, setRange] = useState<[number, number]>([0, 1]);
  const [settings, setSettings] = useState<ChartSettings>(defaultSettings);
  const activeIndex = Math.max(0, Math.min(selectedIndex, Math.max(0, dataset.times.length - 1)));
  return <div className="results-profile-sample-plot" data-testid="result-profile-sample-line-plot">
    <TimeSeriesPayloadChart
      dataset={dataset}
      series={series}
      selectedIndex={activeIndex}
      onSelectIndex={setSelectedIndex}
      range={range}
      onRangeChange={setRange}
      settings={settings}
      onSettingsChange={setSettings}
      language={language}
    />
    <ScrubbedDataDisplay time={dataset.times[activeIndex] ?? null} index={activeIndex} series={series} language={language} />
  </div>;
}
