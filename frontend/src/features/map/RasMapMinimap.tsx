import { useMemo, useRef } from 'react';
import type { KeyboardEvent as ReactKeyboardEvent, PointerEvent as ReactPointerEvent, WheelEvent as ReactWheelEvent } from 'react';
import { pathsInGeometry, pointsInGeometry, positionsInGeometry } from './geoJson';
import type { MapViewport } from './mapViewport';
import { zoomAt } from './mapViewport';
import type { GeoJsonLayer, MapImageLayer, MapExtent, XY } from './mapTypes';
import './mapMinimap.css';

const MINIMAP_WIDTH = 240;
const MINIMAP_HEIGHT = 154;
const MINIMAP_PADDING = 12;
const MINIMAP_COLORS = ['#2877c8', '#bc6540', '#477d64', '#a44b78', '#8467b5', '#a38232'];
const MIN_SCALE = 1e-15;
const MAX_SCALE = 1e15;

export interface RasMapMinimapProps {
  layers: GeoJsonLayer[];
  images: MapImageLayer[];
  viewport: MapViewport;
  size: { width: number; height: number };
  language: 'zh' | 'en';
  onViewportChange: (viewport: MapViewport) => void;
}

interface OverviewFrame {
  coordinateScale: number;
  centerX: number;
  centerY: number;
  scale: number;
}

interface MiniPoint {
  x: number;
  y: number;
}

interface MiniRect {
  x: number;
  y: number;
  width: number;
  height: number;
}

interface PointerDrag {
  pointerId: number;
  offsetX: number;
  offsetY: number;
}

function validExtent(extent: MapExtent): boolean {
  return Number.isFinite(extent.minX) && Number.isFinite(extent.minY)
    && Number.isFinite(extent.maxX) && Number.isFinite(extent.maxY)
    && extent.minX <= extent.maxX && extent.minY <= extent.maxY;
}

function validImageExtent(extent: MapExtent): boolean {
  return validExtent(extent) && extent.minX < extent.maxX && extent.minY < extent.maxY;
}

function createOverviewFrame(layers: readonly GeoJsonLayer[], images: readonly MapImageLayer[]): OverviewFrame | null {
  let minX = Infinity;
  let minY = Infinity;
  let maxX = -Infinity;
  let maxY = -Infinity;
  const include = (x: number, y: number): void => {
    if (!Number.isFinite(x) || !Number.isFinite(y)) return;
    minX = Math.min(minX, x);
    minY = Math.min(minY, y);
    maxX = Math.max(maxX, x);
    maxY = Math.max(maxY, y);
  };

  for (const layer of layers) {
    if (!layer.visible) continue;
    for (const feature of layer.features) {
      const positions = positionsInGeometry(feature.geometry);
      if (positions.length === 0 || positions.some(([x, y]) => !Number.isFinite(x) || !Number.isFinite(y))) continue;
      for (const [x, y] of positions) include(x, y);
    }
  }
  for (const image of images) {
    if (!image.imageSrc || !validImageExtent(image.extent)) continue;
    include(image.extent.minX, image.extent.minY);
    include(image.extent.maxX, image.extent.maxY);
  }
  if (![minX, minY, maxX, maxY].every(Number.isFinite)) return null;

  // Normalize before finding spans so even very large finite project coordinates
  // cannot overflow while the overview transform is being calculated.
  const coordinateScale = Math.max(Math.abs(minX), Math.abs(minY), Math.abs(maxX), Math.abs(maxY), 1);
  const normalizedMinX = minX / coordinateScale;
  const normalizedMinY = minY / coordinateScale;
  const normalizedMaxX = maxX / coordinateScale;
  const normalizedMaxY = maxY / coordinateScale;
  const centerX = normalizedMinX / 2 + normalizedMaxX / 2;
  const centerY = normalizedMinY / 2 + normalizedMaxY / 2;
  const spanX = Math.max(0, normalizedMaxX - normalizedMinX);
  const spanY = Math.max(0, normalizedMaxY - normalizedMinY);
  const majorSpan = Math.max(spanX, spanY, 1e-9);
  const displaySpanX = Math.max(spanX, majorSpan * 0.06);
  const displaySpanY = Math.max(spanY, majorSpan * 0.06);
  const innerWidth = MINIMAP_WIDTH - 2 * MINIMAP_PADDING;
  const innerHeight = MINIMAP_HEIGHT - 2 * MINIMAP_PADDING;
  const scale = Math.min(innerWidth / displaySpanX, innerHeight / displaySpanY);
  if (!Number.isFinite(scale) || scale <= 0 || !Number.isFinite(centerX) || !Number.isFinite(centerY)) return null;
  return { coordinateScale, centerX, centerY, scale };
}

function normalizedPoint(point: XY, frame: OverviewFrame): MiniPoint | null {
  const x = point[0] / frame.coordinateScale;
  const y = point[1] / frame.coordinateScale;
  return Number.isFinite(x) && Number.isFinite(y) ? { x, y } : null;
}

function worldToMinimap(point: XY, frame: OverviewFrame): MiniPoint | null {
  const normalized = normalizedPoint(point, frame);
  if (!normalized) return null;
  const x = MINIMAP_WIDTH / 2 + (normalized.x - frame.centerX) * frame.scale;
  const y = MINIMAP_HEIGHT / 2 - (normalized.y - frame.centerY) * frame.scale;
  return Number.isFinite(x) && Number.isFinite(y) ? { x, y } : null;
}

function minimapToNormalized(point: MiniPoint, frame: OverviewFrame): MiniPoint | null {
  const x = frame.centerX + (point.x - MINIMAP_WIDTH / 2) / frame.scale;
  const y = frame.centerY - (point.y - MINIMAP_HEIGHT / 2) / frame.scale;
  return Number.isFinite(x) && Number.isFinite(y) ? { x, y } : null;
}

function minimapPointerPoint(event: ReactPointerEvent<SVGSVGElement> | ReactWheelEvent<SVGSVGElement>): MiniPoint | null {
  const rect = event.currentTarget.getBoundingClientRect();
  if (!Number.isFinite(rect.width) || !Number.isFinite(rect.height) || rect.width <= 0 || rect.height <= 0) return null;
  const x = (event.clientX - rect.left) * MINIMAP_WIDTH / rect.width;
  const y = (event.clientY - rect.top) * MINIMAP_HEIGHT / rect.height;
  return Number.isFinite(x) && Number.isFinite(y) ? { x, y } : null;
}

function currentViewportRect(viewport: MapViewport, size: { width: number; height: number }, frame: OverviewFrame): MiniRect | null {
  if (!Number.isFinite(viewport.scale) || viewport.scale <= 0
    || !Number.isFinite(viewport.offsetX) || !Number.isFinite(viewport.offsetY)
    || !Number.isFinite(size.width) || !Number.isFinite(size.height) || size.width <= 0 || size.height <= 0) return null;
  const left = -viewport.offsetX / viewport.scale;
  const right = (size.width - viewport.offsetX) / viewport.scale;
  const top = viewport.offsetY / viewport.scale;
  const bottom = (viewport.offsetY - size.height) / viewport.scale;
  const topLeft = worldToMinimap([left, top], frame);
  const bottomRight = worldToMinimap([right, bottom], frame);
  if (!topLeft || !bottomRight) return null;
  const rect = {
    x: Math.min(topLeft.x, bottomRight.x),
    y: Math.min(topLeft.y, bottomRight.y),
    width: Math.abs(bottomRight.x - topLeft.x),
    height: Math.abs(bottomRight.y - topLeft.y),
  };
  return Object.values(rect).every(Number.isFinite) ? rect : null;
}

function viewportCenterNormalized(viewport: MapViewport, size: { width: number; height: number }, frame: OverviewFrame): MiniPoint | null {
  if (!Number.isFinite(viewport.scale) || viewport.scale <= 0
    || !Number.isFinite(viewport.offsetX) || !Number.isFinite(viewport.offsetY)
    || !Number.isFinite(size.width) || !Number.isFinite(size.height)) return null;
  const worldX = (size.width / 2 - viewport.offsetX) / viewport.scale;
  const worldY = (viewport.offsetY - size.height / 2) / viewport.scale;
  return normalizedPoint([worldX, worldY], frame);
}

function viewportCenteredAt(
  normalizedCenter: MiniPoint,
  viewport: MapViewport,
  size: { width: number; height: number },
  frame: OverviewFrame,
): MapViewport | null {
  const worldX = normalizedCenter.x * frame.coordinateScale;
  const worldY = normalizedCenter.y * frame.coordinateScale;
  if (!Number.isFinite(worldX) || !Number.isFinite(worldY)) return null;
  const offsetX = size.width / 2 - worldX * viewport.scale;
  const offsetY = size.height / 2 + worldY * viewport.scale;
  return Number.isFinite(offsetX) && Number.isFinite(offsetY) ? { ...viewport, offsetX, offsetY } : null;
}

function isValidViewport(viewport: MapViewport): boolean {
  return Number.isFinite(viewport.scale) && viewport.scale > 0
    && Number.isFinite(viewport.offsetX) && Number.isFinite(viewport.offsetY);
}

function isInside(point: MiniPoint, rect: MiniRect): boolean {
  return point.x >= rect.x && point.x <= rect.x + rect.width
    && point.y >= rect.y && point.y <= rect.y + rect.height;
}

function clampedOpacity(value: number | undefined): number {
  return typeof value === 'number' && Number.isFinite(value) ? Math.max(0, Math.min(1, value)) : 1;
}

/** Draws a local overview of the data already displayed by RasProjectMap. */
export function RasMapMinimap({ layers, images, viewport, size, language, onViewportChange }: RasMapMinimapProps) {
  const frame = useMemo(() => createOverviewFrame(layers, images), [layers, images]);
  const dragRef = useRef<PointerDrag | null>(null);
  const zh = language === 'zh';
  const viewportRect = frame ? currentViewportRect(viewport, size, frame) : null;
  const canNavigate = !!frame && isValidViewport(viewport)
    && Number.isFinite(size.width) && size.width > 0
    && Number.isFinite(size.height) && size.height > 0;

  const centerCameraAt = (point: MiniPoint): void => {
    if (!frame || !canNavigate) return;
    const normalized = minimapToNormalized(point, frame);
    if (!normalized) return;
    const next = viewportCenteredAt(normalized, viewport, size, frame);
    if (next) onViewportChange(next);
  };

  const onPointerDown = (event: ReactPointerEvent<SVGSVGElement>): void => {
    if (event.button !== 0 || !frame || !canNavigate) return;
    const point = minimapPointerPoint(event);
    if (!point) return;
    event.preventDefault();
    event.stopPropagation();
    const wasInsideViewport = viewportRect ? isInside(point, viewportRect) : false;
    const center = viewportCenterNormalized(viewport, size, frame);
    const normalized = minimapToNormalized(point, frame);
    if (!normalized) return;
    const offsetX = wasInsideViewport && center ? normalized.x - center.x : 0;
    const offsetY = wasInsideViewport && center ? normalized.y - center.y : 0;
    dragRef.current = { pointerId: event.pointerId, offsetX, offsetY };
    try { event.currentTarget.setPointerCapture(event.pointerId); } catch { /* Pointer capture may be unavailable in embedded renderers. */ }
    if (!wasInsideViewport) centerCameraAt(point);
  };

  const onPointerMove = (event: ReactPointerEvent<SVGSVGElement>): void => {
    const drag = dragRef.current;
    if (!drag || drag.pointerId !== event.pointerId || !frame || !canNavigate) return;
    const point = minimapPointerPoint(event);
    const normalized = point ? minimapToNormalized(point, frame) : null;
    if (!normalized) return;
    event.preventDefault();
    event.stopPropagation();
    const next = viewportCenteredAt({ x: normalized.x - drag.offsetX, y: normalized.y - drag.offsetY }, viewport, size, frame);
    if (next) onViewportChange(next);
  };

  const endPointerDrag = (event: ReactPointerEvent<SVGSVGElement>): void => {
    const drag = dragRef.current;
    if (!drag || drag.pointerId !== event.pointerId) return;
    event.stopPropagation();
    dragRef.current = null;
    try {
      if (event.currentTarget.hasPointerCapture(event.pointerId)) event.currentTarget.releasePointerCapture(event.pointerId);
    } catch { /* Pointer capture may already have been released by the browser. */ }
  };

  const onWheel = (event: ReactWheelEvent<SVGSVGElement>): void => {
    if (!frame || !canNavigate || !Number.isFinite(event.deltaY) || event.deltaY === 0) return;
    const point = minimapPointerPoint(event);
    const normalized = point ? minimapToNormalized(point, frame) : null;
    if (!normalized) return;
    const worldX = normalized.x * frame.coordinateScale;
    const worldY = normalized.y * frame.coordinateScale;
    if (!Number.isFinite(worldX) || !Number.isFinite(worldY)) return;
    event.preventDefault();
    event.stopPropagation();
    const exponent = Math.max(-400, Math.min(400, event.deltaY / 120));
    const factor = Math.pow(0.9, exponent);
    const scale = Math.max(MIN_SCALE, Math.min(MAX_SCALE, viewport.scale * factor));
    const scaleDelta = scale - viewport.scale;
    const next = {
      scale,
      offsetX: viewport.offsetX - worldX * scaleDelta,
      offsetY: viewport.offsetY + worldY * scaleDelta,
    };
    if (isValidViewport(next)) onViewportChange(next);
  };

  const onKeyDown = (event: ReactKeyboardEvent<SVGSVGElement>): void => {
    if (!canNavigate) return;
    const horizontalStep = size.width * 0.1;
    const verticalStep = size.height * 0.1;
    let next: MapViewport | null = null;
    if (event.key === 'ArrowLeft') next = { ...viewport, offsetX: viewport.offsetX + horizontalStep };
    else if (event.key === 'ArrowRight') next = { ...viewport, offsetX: viewport.offsetX - horizontalStep };
    else if (event.key === 'ArrowUp') next = { ...viewport, offsetY: viewport.offsetY + verticalStep };
    else if (event.key === 'ArrowDown') next = { ...viewport, offsetY: viewport.offsetY - verticalStep };
    else if (event.key === '+' || event.key === '=' || event.key === 'Add') next = zoomAt(viewport, [size.width / 2, size.height / 2], 1 / 0.9);
    else if (event.key === '-' || event.key === 'Subtract') next = zoomAt(viewport, [size.width / 2, size.height / 2], 0.9);
    else if (event.key === 'Home' && frame) {
      const centered = viewportCenteredAt({ x: frame.centerX, y: frame.centerY }, viewport, size, frame);
      next = centered;
    }
    if (!next || !isValidViewport(next)) return;
    event.preventDefault();
    event.stopPropagation();
    onViewportChange(next);
  };

  const title = zh ? '地图概览' : 'Map overview';
  const hint = zh
    ? '点击定位 · 拖动视口框 · 滚轮缩放 · 方向键移动 · +/- 缩放'
    : 'Click to locate · drag the viewport · wheel to zoom · arrow keys pan · +/- zoom';

  return <section className="ras-map-minimap" aria-label={title}>
    <div className="ras-map-minimap__heading">{title}</div>
    {frame ? <svg
      className="ras-map-minimap__canvas"
      width={MINIMAP_WIDTH}
      height={MINIMAP_HEIGHT}
      viewBox={`0 0 ${MINIMAP_WIDTH} ${MINIMAP_HEIGHT}`}
      preserveAspectRatio="none"
      role="application"
      tabIndex={0}
      aria-label={`${title}. ${hint}`}
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={endPointerDrag}
      onPointerCancel={endPointerDrag}
      onLostPointerCapture={() => { dragRef.current = null; }}
      onWheel={onWheel}
      onKeyDown={onKeyDown}
    >
      <rect className="ras-map-minimap__surface" x="0" y="0" width={MINIMAP_WIDTH} height={MINIMAP_HEIGHT} />
      {images.map((image) => {
        if (!image.imageSrc || !validImageExtent(image.extent)) return null;
        const topLeft = worldToMinimap([image.extent.minX, image.extent.maxY], frame);
        const bottomRight = worldToMinimap([image.extent.maxX, image.extent.minY], frame);
        if (!topLeft || !bottomRight) return null;
        return <image
          key={image.id}
          className="ras-map-minimap__image"
          href={image.imageSrc}
          x={Math.min(topLeft.x, bottomRight.x)}
          y={Math.min(topLeft.y, bottomRight.y)}
          width={Math.abs(bottomRight.x - topLeft.x)}
          height={Math.abs(bottomRight.y - topLeft.y)}
          opacity={clampedOpacity(image.opacity)}
          preserveAspectRatio="none"
          style={{ imageRendering: image.sampling === 'nearest' ? 'pixelated' : 'auto' }}
          pointerEvents="none"
        ><title>{image.name}</title></image>;
      })}
      {layers.filter((layer) => layer.visible).map((layer, layerIndex) => {
        const color = MINIMAP_COLORS[layerIndex % MINIMAP_COLORS.length];
        return <g key={layer.id} opacity={clampedOpacity(layer.opacity)}>
          {layer.features.map((feature) => {
            const positions = positionsInGeometry(feature.geometry);
            if (positions.length === 0 || positions.some(([x, y]) => !Number.isFinite(x) || !Number.isFinite(y))) return null;
            const project = (point: XY): XY => {
              const projected = worldToMinimap(point, frame);
              return projected ? [projected.x, projected.y] : [Number.NaN, Number.NaN];
            };
            const paths = pathsInGeometry(feature.geometry, project);
            const points = pointsInGeometry(feature.geometry).map(project);
            return <g key={feature.id} className="ras-map-minimap__feature">
              {paths.filter((path) => path.d).map((path, index) => <path
                key={`path-${index}`}
                d={path.d}
                fill={path.fill ? `${color}30` : 'none'}
                fillRule="evenodd"
                stroke={color}
                strokeWidth={1.2}
                strokeLinejoin="round"
                vectorEffect="non-scaling-stroke"
              />)}
              {points.map(([x, y], index) => <circle key={`point-${index}`} cx={x} cy={y} r={2} fill="#fff" stroke={color} strokeWidth={1.1} vectorEffect="non-scaling-stroke" />)}
              <title>{feature.name}</title>
            </g>;
          })}
        </g>;
      })}
      {viewportRect && <rect
        className="ras-map-minimap__viewport"
        x={viewportRect.x}
        y={viewportRect.y}
        width={viewportRect.width}
        height={viewportRect.height}
        vectorEffect="non-scaling-stroke"
        pointerEvents="none"
      />}
    </svg> : <div className="ras-map-minimap__empty">
      {zh ? '当前没有可概览的可见几何或有效影像范围。' : 'No visible geometry or valid image extent is available.'}
    </div>}
    <p className="ras-map-minimap__hint">{hint}</p>
  </section>;
}

export default RasMapMinimap;
