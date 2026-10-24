import type { ResultSeries } from '../data';
import { CHART_COLORS } from './chartTypes';

interface ChartLegendProps {
  series: ResultSeries[];
  language: 'zh' | 'en';
}

export function ChartLegend({ series, language }: ChartLegendProps) {
  return <div className="results-chart-legend" role="group" aria-label={language === 'zh' ? '图例' : 'Legend'}>
    {series.map((item, index) => <span key={item.id}><i style={{ backgroundColor: CHART_COLORS[index % CHART_COLORS.length] }} />{item.name}{item.units ? ' (' + item.units + ')' : ''}</span>)}
  </div>;
}
