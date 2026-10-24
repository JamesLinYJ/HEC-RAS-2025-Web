import type { MouseEvent as ReactMouseEvent, PointerEvent as ReactPointerEvent, WheelEvent as ReactWheelEvent } from 'react';
import { extentOfPositions, isKnownCrs, pathsInGeometry, pointsInGeometry, positionsInGeometry } from './geoJson';
import { mapCRS } from './mapText';
import { worldToScreen } from './mapViewport';
import type { MapViewport } from './mapViewport';
import type { GeoFeature, GeoJsonLayer, MapImageLayer, XY } from './mapTypes';
import type { MapDisplaySettings } from './MapSettings';
import type { MapToolID } from './MapToolsVM';
import { MapVelocityArrows } from './MapVelocityArrows';
import type { MapVelocityFieldState } from './useMapVelocityField';
import { ToolTipAnnotation } from './ToolTipAnnotation';
import type { MapHoverTooltip } from './useMapHoverIdentify';
import { RasMapMinimap } from './RasMapMinimap';

export interface RasProjectMapProps {
  layers: GeoJsonLayer[];
  images?: MapImageLayer[];
  velocityField?: MapVelocityFieldState;
  viewport: MapViewport;
  size: { width: number; height: number };
  activeTool: MapToolID;
  selectedFeature: { layerID: string; featureID: string } | null;
  draftPoints: XY[];
  settings: MapDisplaySettings;
  onPointerDown: (event: ReactPointerEvent<SVGSVGElement>) => void;
  onPointerMove: (event: ReactPointerEvent<SVGSVGElement>) => void;
  onPointerLeave?: () => void;
  onPointerUp: (event: ReactPointerEvent<SVGSVGElement>) => void;
  onClick: (event: ReactPointerEvent<SVGSVGElement>) => void;
  onDoubleClick: (event: ReactPointerEvent<SVGSVGElement>) => void;
  onWheel: (event: ReactWheelEvent<SVGSVGElement>) => void;
  onFeatureSelect: (layer: GeoJsonLayer, feature: GeoFeature) => void;
  onFeatureContext: (layer: GeoJsonLayer, feature: GeoFeature, event: ReactMouseEvent<SVGGElement>) => void;
  language: 'zh' | 'en';
  hoverTooltip?: MapHoverTooltip | null;
  onViewportChange?: (viewport: MapViewport) => void;
}

function niceGridStep(unitsPerPixel: number, width: number): number {
  const desired = unitsPerPixel * width / 9;
  if (!Number.isFinite(desired) || desired <= 0) return 1;
  const power = Math.pow(10, Math.floor(Math.log10(desired)));
  const fraction = desired / power;
  return (fraction < 1.5 ? 1 : fraction < 3.5 ? 2 : fraction < 7.5 ? 5 : 10) * power;
}

function gridValues(min: number, max: number, step: number): number[] {
  const first = Math.ceil(min / step) * step;
  const count = Math.min(100, Math.max(0, Math.floor((max - first) / step) + 1));
  return Array.from({ length: count }, (_, index) => first + index * step);
}

function featureColor(layerIndex: number): string {
  return ['#2877c8', '#bc6540', '#477d64', '#a44b78', '#8467b5', '#a38232'][layerIndex % 6];
}

function FeatureShape({
  layer,
  feature,
  color,
  viewport,
  selected,
  showLabel,
  activeTool,
  onSelect,
  onContext,
}: {
  layer: GeoJsonLayer;
  feature: GeoFeature;
  color: string;
  viewport: MapViewport;
  selected: boolean;
  showLabel: boolean;
  activeTool: MapToolID;
  onSelect: () => void;
  onContext: (event: ReactMouseEvent<SVGGElement>) => void;
}) {
  const project = (point: XY): XY => worldToScreen(point, viewport);
  const points = pointsInGeometry(feature.geometry);
  const positions = positionsInGeometry(feature.geometry);
  const extent = extentOfPositions(positions);
  const labelPosition = extent
    ? project([(extent.minX + extent.maxX) / 2, (extent.minY + extent.maxY) / 2])
    : null;
  const paths = pathsInGeometry(feature.geometry, project);
  const isPolygon = paths.some((path) => path.fill);
  return (
    <g
      className={'map-feature' + (selected ? ' is-selected' : '')}
      data-feature-id={feature.id}
      data-layer-id={layer.id}
      onClick={(event) => {
        if (activeTool !== 'select') return;
        event.stopPropagation();
        onSelect();
      }}
      onContextMenu={(event) => {
        event.preventDefault();
        event.stopPropagation();
        onContext(event);
      }}
    >
      {paths.filter((path) => path.d).map((path, index) => (
        <path
          key={'path-' + index}
          d={path.d}
          fill={path.fill ? color + '30' : 'none'}
          fillRule="evenodd"
          stroke={selected ? '#143f68' : color}
          strokeWidth={selected ? 2.4 : path.fill ? 1.7 : 2}
          strokeLinejoin="round"
          vectorEffect="non-scaling-stroke"
          className="map-feature-path"
        />
      ))}
      {points.map((point, index) => {
        const [x, y] = project(point);
        return (
          <circle
            key={'point-' + index}
            cx={x}
            cy={y}
            r={selected ? 5.8 : 4.2}
            fill="#fff"
            stroke={selected ? '#143f68' : color}
            strokeWidth={selected ? 2.7 : 2}
            vectorEffect="non-scaling-stroke"
            className="map-feature-point"
          />
        );
      })}
      {showLabel && selected && labelPosition && (
        <text className="map-feature-label" x={labelPosition[0] + 8} y={labelPosition[1] - 8}>
          {feature.name}
        </text>
      )}
      {isPolygon && <title>{feature.name}</title>}
    </g>
  );
}

function CoordinateGrid({ viewport, size }: { viewport: MapViewport; size: { width: number; height: number } }) {
  if (viewport.scale <= 0 || size.width <= 0 || size.height <= 0) return null;
  const worldLeft = -viewport.offsetX / viewport.scale;
  const worldRight = (size.width - viewport.offsetX) / viewport.scale;
  const worldTop = (viewport.offsetY - 0) / viewport.scale;
  const worldBottom = (viewport.offsetY - size.height) / viewport.scale;
  const step = niceGridStep(1 / viewport.scale, size.width);
  const xs = gridValues(worldLeft, worldRight, step);
  const ys = gridValues(worldBottom, worldTop, step);
  const axisColor = '#8294a3';
  const origin = worldToScreen([0, 0], viewport);
  return (
    <g className="map-coordinate-grid" pointerEvents="none">
      {origin[0] >= 0 && origin[0] <= size.width && <line className="map-coordinate-axis" stroke={axisColor} x1={origin[0]} x2={origin[0]} y1={0} y2={size.height} />}
      {origin[1] >= 0 && origin[1] <= size.height && <line className="map-coordinate-axis" stroke={axisColor} x1={0} x2={size.width} y1={origin[1]} y2={origin[1]} />}
      {xs.map((x) => {
        const position = worldToScreen([x, 0], viewport)[0];
        return <g key={'x-' + x}><line x1={position} x2={position} y1={0} y2={size.height} /><text x={position + 4} y={size.height - 8}>{Number(x.toPrecision(5))}</text></g>;
      })}
      {ys.map((y) => {
        const position = worldToScreen([0, y], viewport)[1];
        return <g key={'y-' + y}><line x1={0} x2={size.width} y1={position} y2={position} /><text x={6} y={position - 4}>{Number(y.toPrecision(5))}</text></g>;
      })}
    </g>
  );
}

export function RasProjectMap({
  layers,
  images = [],
  velocityField,
  viewport,
  size,
  activeTool,
  selectedFeature,
  draftPoints,
  settings,
  onPointerDown,
  onPointerMove,
  onPointerLeave,
  onPointerUp,
  onClick,
  onDoubleClick,
  onWheel,
  onFeatureSelect,
  onFeatureContext,
  language,
  hoverTooltip = null,
  onViewportChange,
}: RasProjectMapProps) {
  const visibleLayers = layers.filter((layer) => layer.visible);
  const coordinateKey = (layer: GeoJsonLayer) => layer.coordinateSpace === 'project' ? 'project' : 'source:' + layer.crs;
  const uniqueCRS = Array.from(new Set(visibleLayers.map(coordinateKey)));
  const activeLayer = visibleLayers[visibleLayers.length - 1];
  const activeCRS = activeLayer ? coordinateKey(activeLayer) : null;
  const compatibleLayers = uniqueCRS.length <= 1 ? visibleLayers : visibleLayers.filter((layer) => coordinateKey(layer) === activeCRS);
  const visibleImages = activeCRS === null || activeCRS === 'project' ? images : [];
  const crsMismatch = uniqueCRS.length > 1;
  const note = language === 'zh'
    ? {
      empty: '导入 GeoJSON 后，地图将在此处绘制其真实坐标和几何。',
      instruction: '拖动以平移 · 滚轮缩放 · 选择工具点击要素',
      crsMismatch: '图层坐标系不同。为避免错误叠加，当前只绘制最上方可见图层。',
      unknownProjection: '坐标系未知，按源坐标绘制；未执行投影转换。',
    }
    : {
      empty: 'Import GeoJSON to draw its actual coordinates and geometry here.',
      instruction: 'Drag to pan · scroll to zoom · use Select to choose a feature',
      crsMismatch: 'Visible layers use different coordinate systems. Only the top visible layer is drawn to avoid a false overlay.',
      unknownProjection: 'The coordinate system is unknown. Source coordinates are shown without projection conversion.',
    };
  const showUnknownCRS = compatibleLayers.some((layer) => layer.coordinateSpace !== 'project' && !isKnownCrs(layer.crs));
  const draftScreen = draftPoints.map((point) => worldToScreen(point, viewport));
  const draftPath = draftScreen.map(([x, y], index) => (index === 0 ? 'M ' : 'L ') + x + ' ' + y).join(' ');

  return (
    <div className="map-canvas-wrap">
      <div className="map-canvas-badges">
        <span className="map-canvas-crs">{mapCRS(language, compatibleLayers[compatibleLayers.length - 1]?.crs ?? (language === 'zh' ? '无坐标系' : 'No CRS'))}</span>
        <span className="map-canvas-coordinate-tag">{activeCRS === 'project' || visibleImages.length > 0 ? (language === 'zh' ? '工程坐标' : 'Project coordinates') : (language === 'zh' ? '源坐标' : 'Source coordinates')}</span>
      </div>
      <svg
        className={'map-canvas' + (activeTool === 'pan' ? ' is-panning' : activeTool.startsWith('draw') || activeTool.startsWith('measure') ? ' is-drawing' : ' is-selecting')}
        width={size.width}
        height={size.height}
        viewBox={'0 0 ' + Math.max(size.width, 1) + ' ' + Math.max(size.height, 1)}
        role="application"
        tabIndex={0}
        aria-label={language === 'zh' ? '地图视图' : 'Map view'}
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerLeave={onPointerLeave}
        onPointerUp={onPointerUp}
        onPointerCancel={onPointerUp}
        onClick={onClick}
        onDoubleClick={onDoubleClick}
        onWheel={onWheel}
      >
        <rect className="map-canvas-surface" x="0" y="0" width={size.width} height={size.height} />
        {visibleImages.map((image) => {
          const [x, y] = worldToScreen([image.extent.minX, image.extent.maxY], viewport);
          return <image key={image.id} data-testid="map-backend-image" data-source-kind={image.sourceKind}
            data-source-revision={image.sourceRevision} data-tree-node-id={image.treeNodeID}
            data-association-context={image.associationContextToken}
            data-source-path={image.sourcePath} data-profile-index={image.profileIndex}
            data-extent-min-x={image.extent.minX} data-extent-min-y={image.extent.minY}
            data-extent-max-x={image.extent.maxX} data-extent-max-y={image.extent.maxY}
            href={image.imageSrc} x={x} y={y}
            width={(image.extent.maxX - image.extent.minX) * viewport.scale}
            height={(image.extent.maxY - image.extent.minY) * viewport.scale}
            style={{ imageRendering: image.sampling === 'nearest' ? 'pixelated' : 'auto' }}
            opacity={image.opacity ?? 1} preserveAspectRatio="none" pointerEvents="none"><title>{image.name}</title></image>;
        })}
        {settings.IsMapOverlayVisible && settings.showCoordinateGrid && <CoordinateGrid viewport={viewport} size={size} />}
        {compatibleLayers.map((layer, layerIndex) => (
          <g key={layer.id} opacity={layer.opacity}>
            {layer.features.map((feature) => (
              <FeatureShape
                key={feature.id}
                layer={layer}
                feature={feature}
                color={featureColor(layerIndex)}
                viewport={viewport}
                selected={selectedFeature?.layerID === layer.id && selectedFeature.featureID === feature.id}
                showLabel={settings.showSelectionLabels}
                activeTool={activeTool}
                onSelect={() => onFeatureSelect(layer, feature)}
                onContext={(event) => onFeatureContext(layer, feature, event)}
              />
            ))}
          </g>
        ))}
        {settings.IsMapOverlayVisible && settings.showVelocityArrows && velocityField &&
          (activeCRS === null || activeCRS === 'project') &&
          <MapVelocityArrows vectors={velocityField.vectors} capture={velocityField.capture} viewport={viewport} size={size} language={language} />}
        {settings.IsMapOverlayVisible && draftPoints.length > 0 && (
          <g className="map-draft-overlay" pointerEvents="none">
            {draftPath && <path d={draftPath + (activeTool === 'drawPolygon' && draftPoints.length >= 3 ? ' Z' : '')} />}
            {draftScreen.map(([x, y], index) => <circle key={index} cx={x} cy={y} r="4.5" />)}
          </g>
        )}
      </svg>
      <ToolTipAnnotation tooltip={hoverTooltip} language={language} />
      {settings.IsMapOverlayVisible && onViewportChange && (compatibleLayers.length > 0 || visibleImages.length > 0) &&
        <RasMapMinimap layers={compatibleLayers} images={visibleImages} viewport={viewport} size={size}
          language={language} onViewportChange={onViewportChange} />}
      {compatibleLayers.length === 0 && visibleImages.length === 0 && (
        <div className="map-empty-state">
          <span className="map-empty-glyph" aria-hidden="true"><i /><i /><i /></span>
          <strong>{language === 'zh' ? '地图画布' : 'Map canvas'}</strong>
          <p>{note.empty}</p>
          <small>{note.instruction}</small>
        </div>
      )}
      {(crsMismatch || showUnknownCRS) && (
        <div className="map-projection-notice" role="status">
          <span aria-hidden="true">ⓘ</span>
          {crsMismatch ? note.crsMismatch : note.unknownProjection}
        </div>
      )}
    </div>
  );
}
