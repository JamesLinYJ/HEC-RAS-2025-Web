import type { MapExtent, XY } from './mapTypes';

export interface MapViewport {
  scale: number;
  offsetX: number;
  offsetY: number;
}

export interface ViewSize {
  width: number;
  height: number;
}

export const INITIAL_VIEWPORT: MapViewport = { scale: 1, offsetX: 0, offsetY: 0 };

export function worldToScreen(point: XY, viewport: MapViewport): XY {
  return [point[0] * viewport.scale + viewport.offsetX, -point[1] * viewport.scale + viewport.offsetY];
}

export function screenToWorld(point: XY, viewport: MapViewport): XY {
  return [(point[0] - viewport.offsetX) / viewport.scale, -(point[1] - viewport.offsetY) / viewport.scale];
}

export function fitExtent(extent: MapExtent, size: ViewSize, padding = 64): MapViewport {
  const width = Math.max(size.width, 1);
  const height = Math.max(size.height, 1);
  const rawSpanX = extent.maxX - extent.minX;
  const rawSpanY = extent.maxY - extent.minY;
  if (
    !Number.isFinite(extent.minX) || !Number.isFinite(extent.minY) ||
    !Number.isFinite(extent.maxX) || !Number.isFinite(extent.maxY) ||
    !Number.isFinite(rawSpanX) || !Number.isFinite(rawSpanY) ||
    rawSpanX < 0 || rawSpanY < 0
  ) {
    throw new RangeError('Map extent is outside the numeric range that can be displayed.');
  }
  let spanX = Math.max(rawSpanX, 1e-12);
  let spanY = Math.max(rawSpanY, 1e-12);
  const innerWidth = Math.max(width - 2 * padding, 1);
  const innerHeight = Math.max(height - 2 * padding, 1);
  const aspect = Math.max(innerWidth / innerHeight, 1e-12);
  if (spanX / spanY > aspect) spanY = spanX / aspect;
  else spanX = spanY * aspect;
  const scale = Math.min(innerWidth / spanX, innerHeight / spanY);
  const centerX = extent.minX + rawSpanX / 2;
  const centerY = extent.minY + rawSpanY / 2;
  const offsetX = width / 2 - centerX * scale;
  const offsetY = height / 2 + centerY * scale;
  const screenMinX = extent.minX * scale + offsetX;
  const screenMaxX = extent.maxX * scale + offsetX;
  const screenMinY = -extent.minY * scale + offsetY;
  const screenMaxY = -extent.maxY * scale + offsetY;
  if (
    !Number.isFinite(scale) || scale <= 0 || !Number.isFinite(offsetX) || !Number.isFinite(offsetY) ||
    !Number.isFinite(screenMinX) || !Number.isFinite(screenMaxX) ||
    !Number.isFinite(screenMinY) || !Number.isFinite(screenMaxY) ||
    (rawSpanX > 0 && screenMinX === screenMaxX) || (rawSpanY > 0 && screenMinY === screenMaxY)
  ) {
    throw new RangeError('Map extent is too extreme to render at the available numeric precision.');
  }
  return { scale, offsetX, offsetY };
}

export function panViewport(viewport: MapViewport, delta: XY): MapViewport {
  return { ...viewport, offsetX: viewport.offsetX + delta[0], offsetY: viewport.offsetY + delta[1] };
}

export function zoomAt(viewport: MapViewport, screenPoint: XY, factor: number): MapViewport {
  const worldPoint = screenToWorld(screenPoint, viewport);
  const scale = Math.max(1e-15, Math.min(1e15, viewport.scale * factor));
  return {
    scale,
    offsetX: screenPoint[0] - worldPoint[0] * scale,
    offsetY: screenPoint[1] + worldPoint[1] * scale,
  };
}
