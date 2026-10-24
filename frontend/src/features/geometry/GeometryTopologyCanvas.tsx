import { useCallback, useEffect, useMemo, useRef, useState, type PointerEvent as ReactPointerEvent, type WheelEvent as ReactWheelEvent } from 'react';
import type {
  GeometryTopologyMap,
  GeometryTopologyMarqueeHitMode,
  GeometryTopologySelectionMode,
  GeometryTopologySelectionScope,
} from './geometryTypes';

export interface GeometryTopologyViewportCapture {
  viewport: { minX: number; minY: number; maxX: number; maxY: number };
  mapCellSize: number;
}
export interface GeometryTopologySpatialSelection {
  gesture: 'Click' | 'Extent';
  worldPoints: Array<{ x: number; y: number }>;
  mapCellSize: number;
  selectionScope?: GeometryTopologySelectionScope;
  selectionMode?: GeometryTopologySelectionMode;
  marqueeHitMode?: GeometryTopologyMarqueeHitMode;
}
export interface GeometryTopologyNodeMove {
  nodeFID: number;
  start: { x: number; y: number };
  end: { x: number; y: number };
  mapCellSize: number;
}
export interface GeometryTopologyCanvasProps {
  language: 'zh' | 'en';
  sourceKey: string;
  map: GeometryTopologyMap | null;
  selectedFIDByKind: { node: number[]; arc: number[]; region: number[] };
  canSelect: boolean;
  canMoveNodes: boolean;
  busy: boolean;
  mapCaptureReady: boolean;
  selectionScope?: GeometryTopologySelectionScope;
  selectionMode?: GeometryTopologySelectionMode;
  marqueeHitMode?: GeometryTopologyMarqueeHitMode;
  onViewportReady: (capture: GeometryTopologyViewportCapture) => void;
  onSpatialSelect: (event: GeometryTopologySpatialSelection) => void;
  onMoveNode: (event: GeometryTopologyNodeMove) => void;
}

interface CanvasSize { width: number; height: number }
interface Camera { centerX: number; centerY: number; worldUnitsPerPixel: number }
interface WorldViewport { minX: number; minY: number; maxX: number; maxY: number }
type DragState =
  | { pointerId: number; ownerKey: string; kind: 'pan'; startClientX: number; startClientY: number; camera: Camera }
  | { pointerId: number; ownerKey: string; kind: 'extent' | 'click'; startClientX: number; startClientY: number; startWorld: { x: number; y: number } }
  | { pointerId: number; ownerKey: string; kind: 'node'; startClientX: number; startClientY: number; startWorld: { x: number; y: number }; nodeFID: number };

const MIN_CANVAS_SIZE = 1;
const FIT_PADDING = 1.12;
const DRAG_THRESHOLD_PX = 3;
const ZOOM_FACTOR = 1.2;

/**
 * Displays only the current Host topology map. Camera transforms affect the
 * view only; all selection and node edits are sent to the owning Geometry hook.
 */
export function GeometryTopologyCanvas({
  language,
  sourceKey,
  map,
  selectedFIDByKind,
  canSelect,
  canMoveNodes,
  busy,
  mapCaptureReady,
  selectionScope = 'All',
  selectionMode = 'Replace',
  marqueeHitMode = 'Intersects',
  onViewportReady,
  onSpatialSelect,
  onMoveNode,
}: GeometryTopologyCanvasProps) {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const svgRef = useRef<SVGSVGElement | null>(null);
  const dragRef = useRef<DragState | null>(null);
  const ownerKeyRef = useRef<string | null>(null);
  const latestViewportRef = useRef('');
  const [size, setSize] = useState<CanvasSize>({ width: 0, height: 0 });
  const [cameraState, setCameraState] = useState<Camera | null>(null);
  const [cameraOwnerKey, setCameraOwnerKey] = useState<string | null>(null);
  const camera = cameraOwnerKey === sourceKey ? cameraState : null;
  const [panMode, setPanMode] = useState(false);
  const [selectionBox, setSelectionBox] = useState<WorldViewport | null>(null);

  const extent = map?.extent ?? null;
  const extentKey = extent
    ? `${extent.minX},${extent.minY},${extent.maxX},${extent.maxY}`
    : 'no-extent';
  const viewport = useMemo(() => {
    if (!camera || size.width < MIN_CANVAS_SIZE || size.height < MIN_CANVAS_SIZE) return null;
    const measured = viewportFor(camera, size);
    return isValidViewport(measured) ? measured : null;
  }, [camera, size]);
  const mapCellSize = camera?.worldUnitsPerPixel ?? 0;

  const storeCamera = useCallback((next: Camera | null) => {
    setCameraState(next);
    setCameraOwnerKey(next ? sourceKey : null);
  }, [sourceKey]);

  useEffect(() => {
    const target = containerRef.current;
    if (!target) return;
    const measure = (observed?: ResizeObserverEntry) => {
      const rect = observed?.contentRect ?? target.getBoundingClientRect();
      const next = { width: Math.max(0, rect.width), height: Math.max(0, rect.height) };
      setSize((current) => current.width === next.width && current.height === next.height ? current : next);
    };
    measure();
    if (typeof ResizeObserver === 'undefined') {
      const handleResize = () => measure();
      window.addEventListener('resize', handleResize);
      return () => window.removeEventListener('resize', handleResize);
    }
    const observer = new ResizeObserver((entries) => {
      if (entries[0]) measure(entries[0]);
    });
    observer.observe(target);
    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    if (!extent || !isValidExtent(extent)) {
      if (ownerKeyRef.current !== sourceKey || camera !== null) {
        ownerKeyRef.current = sourceKey;
        storeCamera(null);
      }
      return;
    }
    const ownerChanged = ownerKeyRef.current !== sourceKey;
    if (ownerChanged) {
      ownerKeyRef.current = sourceKey;
      latestViewportRef.current = '';
      dragRef.current = null;
      setPanMode(false);
      setSelectionBox(null);
      storeCamera(size.width >= MIN_CANVAS_SIZE && size.height >= MIN_CANVAS_SIZE
        ? fitCamera(extent, size)
        : null);
      return;
    }
    if (!camera && size.width >= MIN_CANVAS_SIZE && size.height >= MIN_CANVAS_SIZE) {
      storeCamera(fitCamera(extent, size));
    }
  }, [sourceKey, extentKey, size.width, size.height, extent, camera, storeCamera]);

  useEffect(() => {
    if (!map || !extent || !viewport || !(mapCellSize > 0) || !Number.isFinite(mapCellSize)) return;
    const captureKey = [sourceKey, viewport.minX, viewport.minY, viewport.maxX, viewport.maxY, mapCellSize].join('|');
    if (latestViewportRef.current === captureKey) return;
    latestViewportRef.current = captureKey;
    onViewportReady({ viewport, mapCellSize });
  }, [sourceKey, map, extent, viewport, mapCellSize, onViewportReady]);

  const fit = useCallback(() => {
    if (!extent || !isValidExtent(extent) || size.width < MIN_CANVAS_SIZE || size.height < MIN_CANVAS_SIZE) return;
    storeCamera(fitCamera(extent, size));
  }, [extent, size, storeCamera]);

  function getWorldPoint(event: { clientX: number; clientY: number }): { x: number; y: number } | null {
    const svg = svgRef.current;
    const matrix = svg?.getScreenCTM();
    if (!svg || !matrix || !Number.isFinite(mapCellSize) || mapCellSize <= 0) return null;
    const point = new DOMPoint(event.clientX, event.clientY).matrixTransform(matrix.inverse());
    const world = { x: point.x, y: -point.y };
    return Number.isFinite(world.x) && Number.isFinite(world.y) ? world : null;
  }

  function getMapFeature(event: { target: EventTarget | null }) {
    if (!(event.target instanceof Element)) return null;
    const element = event.target.closest<SVGElement>('[data-topology-map-feature-id]');
    const id = element?.dataset.topologyMapFeatureId;
    if (!id || !map) return null;
    return map.features.find((feature) => feature.id === id) ?? null;
  }

  function onPointerDown(event: ReactPointerEvent<SVGSVGElement>) {
    if (!camera || !viewport || busy) return;
    const wantsPan = panMode || event.button === 1 || event.button === 2;
    if (!wantsPan && !mapCaptureReady) return;
    const world = getWorldPoint(event);
    if (!world) return;
    const feature = getMapFeature(event);
    const sourceNodePoint = feature?.featureKind === 'node' ? feature.paths.find((path) => path.length > 0)?.[0] : null;
    const hitFID = feature?.featureKind === 'node' && typeof feature.fid === 'number' && Number.isInteger(feature.fid)
      && sourceNodePoint && Number.isFinite(sourceNodePoint.x) && Number.isFinite(sourceNodePoint.y)
      ? feature.fid
      : null;
    let drag: DragState | null = null;
    if (wantsPan) {
      event.preventDefault();
      drag = { pointerId: event.pointerId, ownerKey: sourceKey, kind: 'pan', startClientX: event.clientX, startClientY: event.clientY, camera };
    } else if (hitFID !== null && canMoveNodes && mapCaptureReady && !event.shiftKey) {
      event.preventDefault();
      drag = { pointerId: event.pointerId, ownerKey: sourceKey, kind: 'node', startClientX: event.clientX, startClientY: event.clientY, startWorld: { x: sourceNodePoint!.x, y: sourceNodePoint!.y }, nodeFID: hitFID };
    } else if (canSelect && mapCaptureReady && (event.shiftKey || !feature)) {
      event.preventDefault();
      drag = { pointerId: event.pointerId, ownerKey: sourceKey, kind: 'extent', startClientX: event.clientX, startClientY: event.clientY, startWorld: world };
    } else if (feature) {
      drag = { pointerId: event.pointerId, ownerKey: sourceKey, kind: 'click', startClientX: event.clientX, startClientY: event.clientY, startWorld: world };
    } else {
      drag = { pointerId: event.pointerId, ownerKey: sourceKey, kind: 'click', startClientX: event.clientX, startClientY: event.clientY, startWorld: world };
    }
    if (!drag) return;
    dragRef.current = drag;
    setSelectionBox(null);
    event.currentTarget.setPointerCapture(event.pointerId);
  }

  function onPointerMove(event: ReactPointerEvent<SVGSVGElement>) {
    const drag = dragRef.current;
    if (!drag || drag.pointerId !== event.pointerId) return;
    if (drag.ownerKey !== sourceKey) {
      dragRef.current = null;
      setSelectionBox(null);
      return;
    }
    const deltaX = event.clientX - drag.startClientX;
    const deltaY = event.clientY - drag.startClientY;
    if (drag.kind === 'pan') {
      if (Math.hypot(deltaX, deltaY) < 1) return;
      const scale = drag.camera.worldUnitsPerPixel;
      const nextCamera = {
        ...drag.camera,
        centerX: drag.camera.centerX - deltaX * scale,
        centerY: drag.camera.centerY + deltaY * scale,
      };
      if (!isValidCamera(nextCamera)) return;
      storeCamera(nextCamera);
      dragRef.current = { ...drag, startClientX: event.clientX, startClientY: event.clientY, camera: nextCamera };
      return;
    }
    if (drag.kind === 'extent') {
      const point = getWorldPoint(event);
      if (!point) return;
      setSelectionBox(normalizedViewport(drag.startWorld, point));
    }
  }

  function onPointerUp(event: ReactPointerEvent<SVGSVGElement>) {
    const drag = dragRef.current;
    if (!drag || drag.pointerId !== event.pointerId) return;
    if (drag.ownerKey !== sourceKey) {
      dragRef.current = null;
      setSelectionBox(null);
      return;
    }
    dragRef.current = null;
    setSelectionBox(null);
    if (drag.kind === 'pan') return;
    const end = getWorldPoint(event);
    if (!end) return;
    const distance = Math.hypot(event.clientX - drag.startClientX, event.clientY - drag.startClientY);
    const moved = distance >= DRAG_THRESHOLD_PX;
    if (drag.kind === 'node') {
      if (moved && canMoveNodes && mapCaptureReady && !busy) {
        onMoveNode({ nodeFID: drag.nodeFID, start: drag.startWorld, end, mapCellSize });
      } else if (!moved && canSelect && mapCaptureReady && !busy) {
        sendSpatialSelection('Click', [end]);
      }
      return;
    }
    if (drag.kind === 'extent' && moved) {
      if (canSelect && mapCaptureReady && !busy) sendSpatialSelection('Extent', [drag.startWorld, end]);
      return;
    }
    if (canSelect && mapCaptureReady && !busy) sendSpatialSelection('Click', [end]);
  }

  function sendSpatialSelection(gesture: 'Click' | 'Extent', worldPoints: Array<{ x: number; y: number }>) {
    if (!mapCaptureReady || busy || !Number.isFinite(mapCellSize) || mapCellSize <= 0) return;
    onSpatialSelect({
      gesture,
      worldPoints,
      mapCellSize,
      selectionScope: gesture === 'Click' ? 'All' : selectionScope,
      selectionMode,
      ...(gesture === 'Extent' ? { marqueeHitMode } : {}),
    });
  }

  function onWheel(event: ReactWheelEvent<SVGSVGElement>) {
    if (!camera || !viewport || !svgRef.current) return;
    event.preventDefault();
    const rect = svgRef.current.getBoundingClientRect();
    if (rect.width <= 0 || rect.height <= 0) return;
    const localX = event.clientX - rect.left;
    const localY = event.clientY - rect.top;
    const anchor = {
      x: viewport.minX + localX * camera.worldUnitsPerPixel,
      y: viewport.maxY - localY * camera.worldUnitsPerPixel,
    };
    const factor = event.deltaY < 0 ? 1 / ZOOM_FACTOR : ZOOM_FACTOR;
    applyZoom(factor, anchor);
  }

  function applyZoom(factor: number, anchor?: { x: number; y: number }) {
    if (!camera || !Number.isFinite(factor) || factor <= 0) return;
    const base = extent && size.width > 0 && size.height > 0 ? fitCamera(extent, size).worldUnitsPerPixel : camera.worldUnitsPerPixel;
    const nextScale = clamp(camera.worldUnitsPerPixel * factor, base * 1e-5, base * 1e5);
    if (!(nextScale > 0) || !Number.isFinite(nextScale) || !Number.isFinite(base) || base <= 0) return;
    if (!anchor || size.width <= 0 || size.height <= 0) {
      storeCamera({ ...camera, worldUnitsPerPixel: nextScale });
      return;
    }
    const centerX = anchor.x - (anchor.x - camera.centerX) * (nextScale / camera.worldUnitsPerPixel);
    const centerY = anchor.y - (anchor.y - camera.centerY) * (nextScale / camera.worldUnitsPerPixel);
    const nextCamera = { centerX, centerY, worldUnitsPerPixel: nextScale };
    if (isValidCamera(nextCamera)) storeCamera(nextCamera);
  }

  const paths = useMemo(() => map?.features.map((feature) => ({
    feature,
    selected: isFeatureSelected(feature, selectedFIDByKind),
    path: feature.paths.map((points) => points.length > 0
      ? `M ${points.map((point) => `${point.x} ${-point.y}`).join(' L ')}${feature.geometryType === 'polygon' ? ' Z' : ''}`
      : '').filter(Boolean).join(' '),
  })) ?? [], [map, selectedFIDByKind]);

  const isEmptyMap = !map || !Array.isArray(map.features) || map.features.length === 0;
  const hasUsableExtent = !!extent && isValidExtent(extent) && Math.max(extent.maxX - extent.minX, extent.maxY - extent.minY) > 0;
  const viewBox = viewport && mapCellSize > 0 && hasUsableExtent
    ? `${viewport.minX} ${-viewport.maxY} ${viewport.maxX - viewport.minX} ${viewport.maxY - viewport.minY}`
    : undefined;
  const controlsDisabled = !camera || !viewport || !(mapCellSize > 0) || !hasUsableExtent;
  const statusText = camera && viewport && mapCellSize > 0
    ? language === 'zh'
      ? `当前视野 X ${formatCoordinate(viewport.minX)}–${formatCoordinate(viewport.maxX)}，Y ${formatCoordinate(viewport.minY)}–${formatCoordinate(viewport.maxY)}；${mapCellSize.toPrecision(5)} 源坐标单位/屏幕像素`
      : `Viewport X ${formatCoordinate(viewport.minX)}–${formatCoordinate(viewport.maxX)}, Y ${formatCoordinate(viewport.minY)}–${formatCoordinate(viewport.maxY)}; ${mapCellSize.toPrecision(5)} source units/pixel`
    : language === 'zh'
      ? '等待有效的 Host 拓扑范围，以测量当前视野和源坐标命中尺度。'
      : 'Waiting for a valid Host topology extent to measure the viewport and source-coordinate hit scale.';
  const captureStatus = mapCaptureReady
    ? ''
    : language === 'zh' ? ' · 当前地图拓扑上下文尚未就绪，选择和节点编辑已停用' : ' · The current map topology context is not ready; selection and node edits are disabled';

  return <section className="editor-table-card" data-testid="geometry-topology-map-panel" aria-label={language === 'zh' ? '几何拓扑图' : 'Geometry topology map'}>
    <div className="editor-terrain-table-heading" style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8, flexWrap: 'wrap' }}>
      <strong>{language === 'zh' ? '几何拓扑图' : 'Geometry topology map'}</strong>
      <div role="toolbar" aria-label={language === 'zh' ? '拓扑视图工具' : 'Topology view tools'} style={{ display: 'flex', gap: 6, alignItems: 'center' }}>
        <button type="button" className="editor-button" data-testid="geometry-topology-fit" disabled={controlsDisabled} onClick={fit}>{language === 'zh' ? '适合范围' : 'Fit'}</button>
        <button type="button" className="editor-button" data-testid="geometry-topology-zoom-in" disabled={controlsDisabled} aria-label={language === 'zh' ? '放大' : 'Zoom in'} onClick={() => applyZoom(1 / ZOOM_FACTOR)}>＋</button>
        <button type="button" className="editor-button" data-testid="geometry-topology-zoom-out" disabled={controlsDisabled} aria-label={language === 'zh' ? '缩小' : 'Zoom out'} onClick={() => applyZoom(ZOOM_FACTOR)}>−</button>
        <button type="button" className={`editor-button${panMode ? ' editor-button-primary' : ''}`} data-testid="geometry-topology-pan-toggle" aria-pressed={panMode} disabled={controlsDisabled} onClick={() => setPanMode((value) => !value)}>{language === 'zh' ? (panMode ? '平移中' : '平移') : (panMode ? 'Panning' : 'Pan')}</button>
      </div>
    </div>
    <div data-testid="geometry-topology-map-status" className="editor-field-muted" role="status" aria-live="polite" style={{ margin: '4px 0 8px' }}>
      {statusText}{captureStatus}{busy ? (language === 'zh' ? ' · 正在等待拓扑回执' : ' · Waiting for topology receipt') : ''}
    </div>
    <div
      ref={containerRef}
      data-testid="geometry-topology-canvas-container"
      style={{ position: 'relative', width: '100%', height: 460, minHeight: 280, overflow: 'hidden', border: '1px solid #dfe5ea', borderRadius: 6, background: '#f8fafb' }}
    >
      {viewBox && !isEmptyMap ? <svg
        ref={svgRef}
        viewBox={viewBox}
        preserveAspectRatio="none"
        role="img"
        aria-label={language === 'zh' ? '几何拓扑源坐标画布' : 'Geometry topology source-coordinate canvas'}
        data-testid="geometry-topology-canvas"
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerUp}
        onPointerCancel={() => { dragRef.current = null; setSelectionBox(null); }}
        onContextMenu={(event) => event.preventDefault()}
        onWheel={onWheel}
        style={{ display: 'block', width: '100%', height: '100%', touchAction: 'none', cursor: panMode ? 'grab' : canSelect && mapCaptureReady ? 'crosshair' : 'default', userSelect: 'none' }}
      >
        <rect x={viewport!.minX} y={-viewport!.maxY} width={viewport!.maxX - viewport!.minX} height={viewport!.maxY - viewport!.minY} fill="transparent" pointerEvents="all" />
        {paths.map(({ feature, selected, path }) => {
          const geometryType = feature.geometryType.toLowerCase();
          const labelPoint = labelCoordinate(feature);
          const featureColor = selected ? '#b42318' : feature.featureKind === 'arc' ? '#1769aa' : feature.featureKind === 'node' ? '#0f766e' : '#6d5db3';
          const fidLabel = feature.fid == null ? null : `${feature.featureKind === 'node' ? 'N' : feature.featureKind === 'arc' ? 'A' : 'R'} ${feature.fid}`;
          return <g key={feature.id} data-topology-map-feature-id={feature.id} data-feature-kind={feature.featureKind} data-fid={feature.fid ?? undefined}>
            {geometryType === 'point' ? feature.paths.map((points, index) => {
              const point = points[0];
              if (!point) return null;
              return <g key={`${feature.id}:point:${index}`}>
                <circle cx={point.x} cy={-point.y} r={mapCellSize * 4.2} fill={featureColor} stroke="#fff" strokeWidth={1.2} vectorEffect="non-scaling-stroke" />
                <circle cx={point.x} cy={-point.y} r={mapCellSize * 8} fill="transparent" pointerEvents="all" />
                {fidLabel && <text x={point.x + mapCellSize * 6} y={-point.y - mapCellSize * 5} fontSize={mapCellSize * 12} fill="#17212b" stroke="#f8fafb" strokeWidth={mapCellSize * 3} paintOrder="stroke" pointerEvents="none">{fidLabel}</text>}
                <title>{fidLabel ? `${fidLabel} · (${formatCoordinate(point.x)}, ${formatCoordinate(point.y)})` : `${feature.featureKind} · (${formatCoordinate(point.x)}, ${formatCoordinate(point.y)})`}</title>
              </g>;
            }) : <>
              <path d={path} fill={geometryType === 'polygon' ? `${featureColor}20` : 'none'} fillRule="evenodd" stroke={featureColor} strokeWidth={geometryType === 'polygon' ? 1.4 : 2.2} vectorEffect="non-scaling-stroke" pointerEvents={geometryType === 'polygon' ? 'visiblePainted' : 'none'} />
              {geometryType !== 'polygon' && <path d={path} fill="none" stroke="transparent" strokeWidth={12} vectorEffect="non-scaling-stroke" pointerEvents="stroke" />}
              {fidLabel && labelPoint && <text x={labelPoint.x + mapCellSize * 4} y={-labelPoint.y - mapCellSize * 4} fontSize={mapCellSize * 12} fill="#17212b" stroke="#f8fafb" strokeWidth={mapCellSize * 3} paintOrder="stroke" pointerEvents="none">{fidLabel}</text>}
              <title>{`${fidLabel ?? (feature.featureKind === 'region' ? (language === 'zh' ? '区域边界' : 'Region boundary') : feature.featureKind)}${labelPoint ? ` · (${formatCoordinate(labelPoint.x)}, ${formatCoordinate(labelPoint.y)})` : ''}`}</title>
            </>}
            <path d={path} fill="transparent" stroke="transparent" strokeWidth={geometryType === 'point' ? 0 : 12} vectorEffect="non-scaling-stroke" pointerEvents={geometryType === 'point' ? 'none' : geometryType === 'polygon' ? 'fill' : 'stroke'} />
          </g>;
        })}
        {selectionBox && <rect
          x={selectionBox.minX}
          y={-selectionBox.maxY}
          width={selectionBox.maxX - selectionBox.minX}
          height={selectionBox.maxY - selectionBox.minY}
          fill="#2563eb1c"
          stroke="#2563eb"
          strokeDasharray="5 4"
          strokeWidth={1.4}
          vectorEffect="non-scaling-stroke"
          pointerEvents="none"
          data-testid="geometry-topology-selection-box"
        />}
      </svg> : <div className="editor-empty-state" data-testid="geometry-topology-empty" style={{ padding: 20 }}>
        {isEmptyMap
          ? language === 'zh' ? '当前 Host 拓扑快照没有可绘制的真实要素。' : 'The current Host topology snapshot has no drawable features.'
          : !hasUsableExtent
            ? language === 'zh' ? 'Host 返回的要素范围没有空间跨度，无法从真实数据测量视野尺度；地图手势已停用。' : 'The Host extent has no spatial span, so a view scale cannot be measured from the source data; map gestures are disabled.'
            : language === 'zh' ? '正在测量拓扑画布尺寸…' : 'Measuring the topology canvas…'}
      </div>}
    </div>
    <small className="editor-field-muted" style={{ display: 'block', marginTop: 7 }}>
      {language === 'zh'
        ? '点按要素执行 Host 空间选择；拖动空白处或按 Shift 拖动框选，拖动节点提交 Host 节点移动。滚轮缩放；平移工具或中键拖动平移。所有坐标均为 Geometry 源坐标。'
        : 'Click for Host spatial selection; drag empty space or hold Shift to draw an extent, and drag a node for a Host node move. Wheel zooms; use Pan or the middle mouse button to pan. Coordinates remain in Geometry source space.'}
    </small>
  </section>;
}

export default GeometryTopologyCanvas;

function isValidExtent(extent: NonNullable<GeometryTopologyMap['extent']>): boolean {
  const spanX = extent.maxX - extent.minX;
  const spanY = extent.maxY - extent.minY;
  return [extent.minX, extent.minY, extent.maxX, extent.maxY, spanX, spanY].every(Number.isFinite)
    && extent.minX <= extent.maxX && extent.minY <= extent.maxY;
}

function fitCamera(extent: NonNullable<GeometryTopologyMap['extent']>, size: CanvasSize): Camera {
  const spanX = Math.max(0, extent.maxX - extent.minX);
  const spanY = Math.max(0, extent.maxY - extent.minY);
  const scaleX = spanX > 0 ? spanX / size.width : 0;
  const scaleY = spanY > 0 ? spanY / size.height : 0;
  const scale = Math.max(scaleX, scaleY) * FIT_PADDING;
  return {
    centerX: extent.minX + spanX / 2,
    centerY: extent.minY + spanY / 2,
    worldUnitsPerPixel: scale,
  };
}

function viewportFor(camera: Camera, size: CanvasSize): WorldViewport {
  const halfWidth = size.width * camera.worldUnitsPerPixel / 2;
  const halfHeight = size.height * camera.worldUnitsPerPixel / 2;
  return {
    minX: camera.centerX - halfWidth,
    maxX: camera.centerX + halfWidth,
    minY: camera.centerY - halfHeight,
    maxY: camera.centerY + halfHeight,
  };
}

function isValidViewport(viewport: WorldViewport): boolean {
  return [viewport.minX, viewport.minY, viewport.maxX, viewport.maxY].every(Number.isFinite)
    && viewport.minX <= viewport.maxX && viewport.minY <= viewport.maxY;
}

function isValidCamera(camera: Camera): boolean {
  return [camera.centerX, camera.centerY, camera.worldUnitsPerPixel].every(Number.isFinite)
    && camera.worldUnitsPerPixel >= 0;
}

function normalizedViewport(first: { x: number; y: number }, second: { x: number; y: number }): WorldViewport {
  return {
    minX: Math.min(first.x, second.x),
    minY: Math.min(first.y, second.y),
    maxX: Math.max(first.x, second.x),
    maxY: Math.max(first.y, second.y),
  };
}

function isFeatureSelected(
  feature: GeometryTopologyMap['features'][number],
  selected: GeometryTopologyCanvasProps['selectedFIDByKind'],
): boolean {
  const kind = feature.featureKind === 'node' ? 'node' : feature.featureKind === 'arc' ? 'arc' : feature.featureKind === 'region' ? 'region' : null;
  if (!kind) return false;
  const ids = selected[kind];
  if (feature.fid != null && ids.includes(feature.fid)) return true;
  if (!feature.selectionIDs?.length || ids.length === 0) return false;
  const selectedRows = new Set(ids.map((fid) => `${kind}:${fid}`));
  return feature.selectionIDs.some((id) => selectedRows.has(id));
}

function labelCoordinate(feature: GeometryTopologyMap['features'][number]): { x: number; y: number } | null {
  const firstPath = feature.paths.find((path) => path.length > 0);
  return firstPath?.[Math.floor(firstPath.length / 2)] ?? null;
}

function clamp(value: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, value));
}

function formatCoordinate(value: number): string {
  return Number.isFinite(value) ? Number(value.toPrecision(7)).toString() : '—';
}
