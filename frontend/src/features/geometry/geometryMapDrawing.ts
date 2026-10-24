import type { GeometryTopologyCoordinate } from './geometryTypes';
import { isFiniteCoordinate } from './geometryMapHitTesting';

export function toSvgPath(points: readonly GeometryTopologyCoordinate[], close = false): string {
  if (points.length === 0 || !points.every(isFiniteCoordinate)) return '';
  const path = `M ${points.map((point) => `${point.x} ${-point.y}`).join(' L ')}`;
  return close && points.length >= 3 ? `${path} Z` : path;
}

export function dedupeConsecutiveCoordinates(
  points: readonly GeometryTopologyCoordinate[],
  tolerance: number,
): GeometryTopologyCoordinate[] {
  const epsilon = Number.isFinite(tolerance) && tolerance > 0 ? tolerance : 0;
  const result: GeometryTopologyCoordinate[] = [];
  for (const point of points) {
    if (!isFiniteCoordinate(point)) continue;
    const previous = result[result.length - 1];
    if (previous && Math.hypot(previous.x - point.x, previous.y - point.y) <= epsilon) continue;
    result.push({ x: point.x, y: point.y });
  }
  return result;
}

export function draftPathTolerance(mapCellSize: number): number {
  if (!Number.isFinite(mapCellSize) || mapCellSize <= 0) return 0;
  // Pointer-up and browser double-click can report the same endpoint twice. This tolerance
  // removes only sub-pixel duplicates from the local drawing preview and command payload.
  return mapCellSize * 0.2;
}
