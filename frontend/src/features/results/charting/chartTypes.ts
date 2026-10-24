import type { ResultDataset, ResultSeries } from '../data';

export interface ChartSettings {
  grid: boolean;
  markers: boolean;
  step: boolean;
  yMin: string;
  yMax: string;
}

export const CHART_LAYOUT = {
  width: 960,
  height: 352,
  left: 74,
  right: 22,
  top: 24,
  bottom: 54,
} as const;

export const CHART_COLORS = ['#168c83', '#315fcd', '#db7b32', '#a04ec4', '#4b9270', '#d14a68', '#788394', '#b3962f'] as const;

export const CHART_PLOT_WIDTH = CHART_LAYOUT.width - CHART_LAYOUT.left - CHART_LAYOUT.right;
export const CHART_PLOT_HEIGHT = CHART_LAYOUT.height - CHART_LAYOUT.top - CHART_LAYOUT.bottom;

export type TimePrecision = 'minute' | 'second' | 'millisecond';

export interface ChartScale {
  start: number;
  end: number;
  min: number;
  max: number;
  points: number;
}

export function isFiniteValue(value: number | null | undefined): value is number {
  return value !== null && value !== undefined && Number.isFinite(value);
}

export function createChartScale(
  dataset: ResultDataset,
  series: ResultSeries[],
  range: [number, number],
  settings: Pick<ChartSettings, 'yMin' | 'yMax'>,
): ChartScale | null {
  if (dataset.times.length === 0) return null;
  const last = Math.max(0, dataset.times.length - 1);
  const start = Math.max(0, Math.min(last, Math.round(range[0] * last)));
  const end = Math.max(start, Math.min(last, Math.round(range[1] * last)));
  let autoMin = Number.POSITIVE_INFINITY;
  let autoMax = Number.NEGATIVE_INFINITY;
  for (const item of series) {
    for (const value of item.values.slice(start, end + 1)) {
      if (!isFiniteValue(value)) continue;
      autoMin = Math.min(autoMin, value);
      autoMax = Math.max(autoMax, value);
    }
  }
  if (!Number.isFinite(autoMin) || !Number.isFinite(autoMax)) return { start, end, min: 0, max: 1, points: dataset.times.length };

  const span = autoMax - autoMin;
  const padding = span === 0 ? Math.max(Math.abs(autoMax) * 0.05, 1) : span * 0.08;
  const requestedMin = settings.yMin.trim() === '' ? Number.NaN : Number(settings.yMin);
  const requestedMax = settings.yMax.trim() === '' ? Number.NaN : Number(settings.yMax);
  const min = Number.isFinite(requestedMin) ? requestedMin : autoMin - padding;
  const maxCandidate = Number.isFinite(requestedMax) ? requestedMax : autoMax + padding;
  const max = maxCandidate > min ? maxCandidate : min + 1;
  return { start, end, min, max, points: dataset.times.length };
}

export function xForIndex(index: number, scale: ChartScale): number {
  if (scale.end === scale.start) return CHART_LAYOUT.left + CHART_PLOT_WIDTH / 2;
  return CHART_LAYOUT.left + ((index - scale.start) / (scale.end - scale.start)) * CHART_PLOT_WIDTH;
}

export function yForValue(value: number, scale: ChartScale): number {
  return CHART_LAYOUT.top + ((scale.max - value) / (scale.max - scale.min)) * CHART_PLOT_HEIGHT;
}

export function formatTime(value: string, language: 'zh' | 'en', precision: TimePrecision): string {
  const date = new Date(value);
  if (!Number.isFinite(date.getTime())) return value.length > 18 ? value.slice(0, 18) : value;
  const locale = language === 'zh' ? 'zh-CN' : 'en-US';
  const includeSeconds = precision !== 'minute';
  const formatted = new Intl.DateTimeFormat(locale, {
    month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit',
    ...(includeSeconds ? { second: '2-digit' as const } : {}),
    hour12: false,
  }).format(date);
  return precision === 'millisecond' ? formatted + '.' + String(date.getMilliseconds()).padStart(3, '0') : formatted;
}

export function formatTimeTicks(values: string[], indexes: number[], language: 'zh' | 'en'): { labels: string[]; precision: TimePrecision } {
  const milliseconds = values.map((value) => new Date(value).getTime()).filter(Number.isFinite);
  const duration = milliseconds.length > 1 ? Math.max(...milliseconds) - Math.min(...milliseconds) : 0;
  let precision: TimePrecision = duration < 60_000 ? 'second' : 'minute';
  let labels = values.map((value) => formatTime(value, language, precision));
  if (new Set(labels).size !== labels.length && precision === 'minute') {
    precision = 'second';
    labels = values.map((value) => formatTime(value, language, precision));
  }
  if (new Set(labels).size !== labels.length) {
    precision = 'millisecond';
    labels = values.map((value) => formatTime(value, language, precision));
  }
  if (new Set(labels).size !== labels.length) {
    labels = labels.map((label, index) => `${label} · ${indexes[index] + 1}`);
  }
  return { labels, precision };
}

export function formatValue(value: number): string {
  if (!Number.isFinite(value)) return '—';
  return Number(value.toPrecision(8)).toString();
}

export function formatAxisTicks(values: number[]): string[] {
  for (let digits = 0; digits <= 12; digits++) {
    const labels = values.map((value) => {
      const fixed = value.toFixed(digits);
      return fixed.includes('.') ? fixed.replace(/0+$/, '').replace(/\.$/, '') : fixed;
    });
    if (new Set(labels).size === labels.length) return labels;
  }
  // Keep adjacent ticks distinguishable even when the visible range is smaller than 1e-12 or at a large magnitude.
  return values.map((value) => Number(value.toPrecision(17)).toString());
}
