import type { MapExtent, XY } from './mapTypes';
import { fitExtent } from './mapViewport';
import type { MapViewport, ViewSize } from './mapViewport';

export type NavigationTarget = { kind: 'point'; point: XY } | { kind: 'extent'; extent: MapExtent };

/** Input coordinates already use the canvas CRS; this never reprojects them. */
export function navigationViewport(target: NavigationTarget, size: ViewSize, fullExtent: MapExtent | null): MapViewport {
  if (target.kind === 'extent') {
    const { minX, minY, maxX, maxY } = target.extent;
    if (![minX, minY, maxX, maxY].every(Number.isFinite) || minX > maxX || minY > maxY) throw new RangeError('invalidExtent');
    if (minX !== maxX || minY !== maxY) return fitExtent(target.extent, size, 32);
    target = { kind: 'point', point: [minX, minY] };
  }
  const [x, y] = target.point;
  if (!Number.isFinite(x) || !Number.isFinite(y)) throw new RangeError('invalidPoint');
  // Source ZoomToPoint uses 0.0001 of the full-layer diagonal and a 1000-unit fallback.
  const diagonal = fullExtent ? Math.hypot(fullExtent.maxX - fullExtent.minX, fullExtent.maxY - fullExtent.minY) : Math.SQRT2 * 1000;
  const half = Math.max((diagonal > 0 && Number.isFinite(diagonal) ? diagonal : 1000) * 0.00005,
    Math.abs(x) * Number.EPSILON * 8, Math.abs(y) * Number.EPSILON * 8);
  return fitExtent({ minX: x - half, maxX: x + half, minY: y - half, maxY: y + half }, size, 32);
}
