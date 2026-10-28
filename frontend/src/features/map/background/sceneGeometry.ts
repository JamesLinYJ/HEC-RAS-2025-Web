import { worldToScreen } from '../mapViewport';
import type { MapViewport } from '../mapViewport';
import type { BackgroundMapCoordinate, BackgroundMapLabel, BackgroundMapPathPart } from './types';

export function backgroundScreenPoint(point: BackgroundMapCoordinate, viewport: MapViewport): BackgroundMapCoordinate {
  const [x, y] = worldToScreen([point.x, point.y], viewport);
  return { x, y };
}

export function backgroundPathData(parts: BackgroundMapPathPart[], viewport: MapViewport,
  translation: { x: number; y: number } = { x: 0, y: 0 }): string {
  return parts.map(part => part.points.map((point, index) => {
    const screen = backgroundScreenPoint(point, viewport);
    return `${index === 0 ? 'M' : 'L'} ${screen.x + translation.x} ${screen.y + translation.y}`;
  }).join(' ') + (part.isClosed ? ' Z' : '')).join(' ');
}

export interface BackgroundLabelPlacement {
  anchor: BackgroundMapCoordinate;
  path: string | null;
  pathLength: number;
  rotation: number;
}

/** Arc-length midpoint and readable path direction, not a mean of unevenly spaced vertices. */
export function backgroundLabelPlacement(label: BackgroundMapLabel, viewport: MapViewport): BackgroundLabelPlacement | null {
  if (label.point) return { anchor: backgroundScreenPoint(label.point, viewport), path: null,
    pathLength: 0, rotation: label.rotation };
  const points = label.line.map(point => backgroundScreenPoint(point, viewport));
  if (points.length < 2) return null;
  const lengths = points.slice(1).map((point, index) => Math.hypot(point.x - points[index].x, point.y - points[index].y));
  const total = lengths.reduce((sum, length) => sum + length, 0);
  if (!Number.isFinite(total) || total <= 0) return null;
  let remaining = total / 2;
  let middle = 0;
  while (middle < lengths.length - 1 && remaining > lengths[middle]) remaining -= lengths[middle++];
  const start = points[middle], end = points[middle + 1];
  const fraction = lengths[middle] > 0 ? remaining / lengths[middle] : 0;
  const anchor = { x: start.x + (end.x - start.x) * fraction, y: start.y + (end.y - start.y) * fraction };
  // SVG text follows its path. Reverse an upside-down path without changing
  // its midpoint or the Host's explicit additional rotation.
  if (end.x < start.x || end.x === start.x && end.y < start.y) points.reverse();
  return { anchor, path: points.map((point, index) => `${index === 0 ? 'M' : 'L'} ${point.x} ${point.y}`).join(' '),
    pathLength: total, rotation: label.rotation };
}

/** Point-label wrapping uses the browser's actual current font metrics. The contract width is in em. */
export function backgroundLabelLines(label: BackgroundMapLabel, measure: (text: string) => number): string[] {
  if (label.maximumWidth <= 0 || label.maximumWidth === 2_147_483_647)
    return label.text.split(/\r?\n/);
  const limit = label.maximumWidth * label.fontSize;
  const lines: string[] = [];
  for (const paragraph of label.text.split(/\r?\n/)) {
    let line = '';
    // Preserve graphemes when wrapping CJK text or a long unbroken name.
    const segments = typeof Intl.Segmenter === 'function'
      ? Array.from(new Intl.Segmenter(undefined, { granularity: 'grapheme' }).segment(paragraph), item => item.segment)
      : Array.from(paragraph);
    let lastSpace = -1;
    for (const segment of segments) {
      if (line && measure(line + segment) > limit) {
        if (lastSpace > 0) {
          lines.push(line.slice(0, lastSpace));
          line = line.slice(lastSpace + 1) + segment;
        } else {
          lines.push(line);
          line = segment.trimStart();
        }
        lastSpace = line.lastIndexOf(' ');
      } else {
        line += segment;
        if (segment === ' ') lastSpace = line.length - 1;
      }
    }
    lines.push(line.trimEnd());
  }
  return lines;
}
