export type NormalizedTimeRange = [number, number];

export function indexRangeFromNormalized(range: NormalizedTimeRange, count: number): [number, number] {
  const last = Math.max(0, count - 1);
  const start = Math.max(0, Math.min(last, Math.round(range[0] * last)));
  const end = Math.max(start, Math.min(last, Math.round(range[1] * last)));
  return [start, end];
}

export function normalizedRangeFromIndexes(start: number, end: number, count: number): NormalizedTimeRange {
  const last = Math.max(0, count - 1);
  if (last === 0) return [0, 1];
  const firstIndex = Math.max(0, Math.min(last, Math.round(start)));
  const lastIndex = Math.max(firstIndex, Math.min(last, Math.round(end)));
  return [firstIndex / last, lastIndex / last];
}

export function hasCompleteTimeBounds(times: readonly string[]): boolean {
  return times.length > 0 && !!times[0]?.trim() && !!times[times.length - 1]?.trim();
}
