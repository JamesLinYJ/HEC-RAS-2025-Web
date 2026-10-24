import { useMemo } from 'react';
import type { Language } from '../../shared/workbench';
import { worldToScreen } from './mapViewport';
import type { MapViewport, ViewSize } from './mapViewport';
import type { MapResultVelocityFieldCapture, MapResultVelocityVector } from './useMapVelocityField';

const MAX_VELOCITY_ARROWS = 1200;
const MAX_ARROW_LENGTH = 20;
const MIN_ARROW_SPACING = 24;

export interface MapVelocityArrowsProps {
  vectors: MapResultVelocityVector[];
  capture: MapResultVelocityFieldCapture | null;
  viewport: MapViewport;
  size: ViewSize;
  language: Language;
}

interface ScreenVelocityArrow {
  index: number;
  vector: MapResultVelocityVector;
  x: number;
  y: number;
  screenMagnitude: number;
}

interface ScreenVelocityArrowSelection {
  arrows: ScreenVelocityArrow[];
  maximumMagnitude: number;
}

function selectScreenArrows(
  vectors: MapResultVelocityVector[],
  viewport: MapViewport,
  size: ViewSize,
): ScreenVelocityArrowSelection {
  if (!Number.isFinite(size.width) || !Number.isFinite(size.height) ||
      size.width <= 0 || size.height <= 0 || !Number.isFinite(viewport.scale) || viewport.scale <= 0 ||
      !Number.isFinite(viewport.offsetX) || !Number.isFinite(viewport.offsetY)) {
    return { arrows: [], maximumMagnitude: 0 };
  }

  const visible: ScreenVelocityArrow[] = [];
  let maximumMagnitude = 0;
  vectors.forEach((vector, index) => {
    if (!Number.isFinite(vector.worldX) || !Number.isFinite(vector.worldY) ||
        !Number.isFinite(vector.vx) || !Number.isFinite(vector.vy) || !Number.isFinite(vector.magnitude) ||
        vector.magnitude < 0) return;
    const screenMagnitude = Math.hypot(vector.vx, vector.vy);
    if (!Number.isFinite(screenMagnitude) || screenMagnitude === 0) return;
    const [x, y] = worldToScreen([vector.worldX, vector.worldY], viewport);
    if (!Number.isFinite(x) || !Number.isFinite(y) || x < 0 || x > size.width || y < 0 || y > size.height) return;
    visible.push({ index, vector, x, y, screenMagnitude });
    maximumMagnitude = Math.max(maximumMagnitude, screenMagnitude);
  });

  if (visible.length === 0) return { arrows: visible, maximumMagnitude };

  let minX = Number.POSITIVE_INFINITY;
  let maxX = Number.NEGATIVE_INFINITY;
  let minY = Number.POSITIVE_INFINITY;
  let maxY = Number.NEGATIVE_INFINITY;
  for (const arrow of visible) {
    minX = Math.min(minX, arrow.x);
    maxX = Math.max(maxX, arrow.x);
    minY = Math.min(minY, arrow.y);
    maxY = Math.max(maxY, arrow.y);
  }
  const spanX = Math.max(maxX - minX, 1);
  const spanY = Math.max(maxY - minY, 1);
  let cellSize = Math.max(MIN_ARROW_SPACING, Math.sqrt(spanX * spanY / MAX_VELOCITY_ARROWS));

  const bucketByScreenCell = (sizeInPixels: number): ScreenVelocityArrow[] => {
    const cells = new Map<string, ScreenVelocityArrow>();
    for (const arrow of visible) {
      const column = Math.floor((arrow.x - minX) / sizeInPixels);
      const row = Math.floor((arrow.y - minY) / sizeInPixels);
      const key = `${column}:${row}`;
      if (!cells.has(key)) cells.set(key, arrow);
    }
    return [...cells.values()];
  };

  let selected = bucketByScreenCell(cellSize);
  while (selected.length > MAX_VELOCITY_ARROWS) {
    cellSize *= Math.max(1.05, Math.sqrt(selected.length / MAX_VELOCITY_ARROWS));
    selected = bucketByScreenCell(cellSize);
  }
  return { arrows: selected, maximumMagnitude };
}

export function MapVelocityArrows({
  vectors,
  capture,
  viewport,
  size,
  language,
}: MapVelocityArrowsProps) {
  const arrows = useMemo(
    () => capture ? selectScreenArrows(vectors, viewport, size) : { arrows: [], maximumMagnitude: 0 },
    [capture, vectors, viewport, size],
  );
  const profile = capture?.profileName ?? '';
  const profileIndex = capture?.profileIndex ?? '';
  const revision = capture?.revision ?? '';

  return (
    <g
      className="map-velocity-arrows"
      data-testid="map-velocity-arrows"
      data-profile={profile}
      data-profile-index={profileIndex}
      data-revision={revision}
      aria-label={language === 'zh' ? '速度矢量箭头' : 'Velocity vector arrows'}
      pointerEvents="none"
    >
      {capture && arrows.arrows.map(({ index, vector, x, y, screenMagnitude }) => {
        const length = arrows.maximumMagnitude > 0
          ? MAX_ARROW_LENGTH * screenMagnitude / arrows.maximumMagnitude
          : 0;
        const angle = Math.atan2(-vector.vy, vector.vx) * 180 / Math.PI;
        const headLength = Math.min(5, length * 0.42);
        const headWidth = headLength * 0.55;
        return (
          <g
            key={index}
            className="map-velocity-arrow"
            data-testid="map-velocity-arrow"
            data-world-x={vector.worldX}
            data-world-y={vector.worldY}
            data-vx={vector.vx}
            data-vy={vector.vy}
            data-magnitude={vector.magnitude}
            data-profile={profile}
            data-profile-index={profileIndex}
            data-revision={revision}
            transform={`translate(${x} ${y}) rotate(${angle})`}
            pointerEvents="none"
          >
            <path
              d={`M 0 0 L ${length} 0 M ${length - headLength} ${-headWidth} L ${length} 0 L ${length - headLength} ${headWidth}`}
              fill="none"
              stroke="#c43d3d"
              strokeWidth="1.6"
              strokeLinecap="round"
              strokeLinejoin="round"
              vectorEffect="non-scaling-stroke"
            />
          </g>
        );
      })}
    </g>
  );
}
