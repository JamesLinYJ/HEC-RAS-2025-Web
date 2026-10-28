import { useId, useMemo } from 'react';
import type { MapViewport, ViewSize } from '../mapViewport';
import { backgroundLabelLines, backgroundLabelPlacement, backgroundPathData, backgroundScreenPoint } from './sceneGeometry';
import { backgroundColorVisible } from './protocol';
import type { BackgroundMapFeature, BackgroundMapFrame, BackgroundMapLabel } from './types';

export interface BackgroundMapSceneProps {
  frame: BackgroundMapFrame | null;
  viewport: MapViewport;
  size: ViewSize;
}

function FeaturePath({ feature, viewport, size, id }: {
  feature: BackgroundMapFeature; viewport: MapViewport; size: ViewSize; id: string;
}) {
  const translation = feature.isTranslated ? { x: feature.translationX, y: feature.translationY } : { x: 0, y: 0 };
  const d = backgroundPathData(feature.parts, viewport, translation);
  const clip = backgroundPathData([{ points: feature.clipPolygon, isClosed: true }], viewport);
  const dash = feature.isDashed ? feature.dash.join(' ') : undefined;
  const hasGap = feature.geometryType === 'LineString' && feature.lineGapWidth > 0;
  const hasFill = feature.geometryType === 'Polygon' && backgroundColorVisible(feature.fillColor);
  return <g data-background-feature={feature.id} data-background-z={feature.zIndex} clipPath={`url(#${id}-clip)`}>
    <defs>
      <clipPath id={`${id}-clip`} clipPathUnits="userSpaceOnUse"><path d={clip} /></clipPath>
      {hasGap && <mask id={`${id}-gap`} maskUnits="userSpaceOnUse" maskContentUnits="userSpaceOnUse"
        x={0} y={0} width={size.width} height={size.height} style={{ maskType: 'luminance' }}>
        <path d={d} fill="none" stroke="white" strokeWidth={feature.strokeWidth} strokeDasharray={dash}
          strokeLinejoin="round" strokeLinecap="butt" />
        <path d={d} fill="none" stroke="black" strokeWidth={feature.lineGapWidth}
          strokeLinejoin="round" strokeLinecap="butt" />
      </mask>}
    </defs>
    {hasFill && <path d={d} fill={feature.fillColor!} fillOpacity={feature.fillOpacity} fillRule="evenodd" stroke="none" />}
    {backgroundColorVisible(feature.strokeColor) && feature.strokeWidth > 0 && <path d={d} fill="none"
      stroke={feature.strokeColor!} strokeOpacity={feature.strokeOpacity} strokeWidth={feature.strokeWidth}
      strokeLinejoin="round" strokeLinecap="butt" strokeDasharray={dash}
      mask={hasGap ? `url(#${id}-gap)` : undefined} />}
  </g>;
}

function Label({ label, viewport, id }: { label: BackgroundMapLabel; viewport: MapViewport; id: string }) {
  const placement = backgroundLabelPlacement(label, viewport);
  const family = label.fontFamily?.trim() || 'sans-serif';
  const lines = useMemo(() => {
    if (!label.point) return [];
    const context = document.createElement('canvas').getContext('2d');
    if (!context) return [label.text];
    context.font = `${label.italic ? 'italic ' : ''}${label.bold ? 'bold ' : ''}${label.fontSize}px ${family}`;
    return backgroundLabelLines(label, value => context.measureText(value).width);
  }, [label, family]);
  if (!placement) return null;
  const halo = backgroundColorVisible(label.haloColor) && label.haloWidth > 0;
  return <g data-background-label={label.id} data-background-z={label.zIndex}
    transform={placement.rotation ? `rotate(${placement.rotation} ${placement.anchor.x} ${placement.anchor.y})` : undefined}>
    {placement.path && <defs><path id={`${id}-line`} d={placement.path} /></defs>}
    <text fontFamily={family} fontSize={label.fontSize} fontWeight={label.bold ? 'bold' : 'normal'}
      fontStyle={label.italic ? 'italic' : 'normal'} fill={label.textColor ?? 'none'}
      stroke={halo ? label.haloColor! : 'none'} strokeWidth={halo ? label.haloWidth * 2 : 0}
      strokeLinejoin="round" paintOrder="stroke fill" textAnchor="middle" dominantBaseline="central"
      x={placement.path ? undefined : placement.anchor.x} y={placement.path ? undefined : placement.anchor.y}>
      {placement.path
        ? <textPath href={`#${id}-line`} startOffset="50%">{label.text}</textPath>
        : lines.map((line, index) => <tspan key={index} x={placement.anchor.x}
          dy={index === 0 ? -(lines.length - 1) * label.fontSize * 0.6 : label.fontSize * 1.2}>{line}</tspan>)}
    </text>
  </g>;
}

/** Insert as the first SVG child, beneath all project rasters, vectors and interactions. */
export function BackgroundMapScene({ frame, viewport, size }: BackgroundMapSceneProps) {
  const prefix = `background-${useId().replace(/[^a-zA-Z0-9_-]/g, '')}`;
  const ordered = useMemo(() => frame ? [
    ...frame.features.map((feature, index) => ({ kind: 'feature' as const, value: feature, index })),
    ...frame.labels.map((label, index) => ({ kind: 'label' as const, value: label, index })),
  ].sort((left, right) => left.value.zIndex - right.value.zIndex ||
    (left.kind === right.kind ? left.index - right.index : left.kind === 'feature' ? -1 : 1)) : [], [frame]);
  if (!frame) return null;
  const upperLeft = backgroundScreenPoint({ x: frame.viewport.minX, y: frame.viewport.maxY }, viewport);
  const lowerRight = backgroundScreenPoint({ x: frame.viewport.maxX, y: frame.viewport.minY }, viewport);
  const frameWidth = lowerRight.x - upperLeft.x, frameHeight = lowerRight.y - upperLeft.y;
  return <g className="map-background-scene" pointerEvents="none" aria-hidden="true" data-background-request={frame.requestID}
    clipPath={`url(#${prefix}-viewport)`}>
    <defs><clipPath id={`${prefix}-viewport`} clipPathUnits="userSpaceOnUse">
      <rect x={0} y={0} width={size.width} height={size.height} />
    </clipPath></defs>
    {backgroundColorVisible(frame.osmBackgroundColor) && <rect data-background-osm-color="true"
      x={upperLeft.x} y={upperLeft.y} width={frameWidth} height={frameHeight} fill={frame.osmBackgroundColor!} />}
    {frame.rasters.map(raster => {
      const top = backgroundScreenPoint({ x: raster.minX, y: raster.maxY }, viewport);
      const bottom = backgroundScreenPoint({ x: raster.maxX, y: raster.minY }, viewport);
      return <image key={raster.id} data-background-raster={raster.id} href={raster.imageSrc}
        x={top.x} y={top.y} width={bottom.x - top.x} height={bottom.y - top.y}
        opacity={raster.opacity} preserveAspectRatio="none" />;
    })}
    {ordered.map(item => item.kind === 'feature'
      ? <FeaturePath key={`feature:${item.value.id}`} feature={item.value} viewport={viewport} size={size} id={`${prefix}-f${item.index}`} />
      : <Label key={`label:${item.value.id}`} label={item.value} viewport={viewport} id={`${prefix}-l${item.index}`} />)}
  </g>;
}
