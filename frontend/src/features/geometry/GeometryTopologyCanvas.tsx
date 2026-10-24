import { useCallback, useEffect, useMemo, useRef, useState, type KeyboardEvent as ReactKeyboardEvent, type MouseEvent as ReactMouseEvent, type PointerEvent as ReactPointerEvent, type WheelEvent as ReactWheelEvent } from 'react';
import type {
  GeometryTopologyArcPointsSnapshot,
  GeometryTopologyCoordinate,
  GeometryTopologyMapCommand,
  GeometryTopologyMap,
  GeometryTopologyMapTool,
  GeometryTopologyMarqueeHitMode,
  GeometryTopologySelectionMode,
  GeometryTopologySelectionScope,
} from './geometryTypes';
import {
  classifyNodeHitExtent,
  isArcPointsSnapshotForPath,
  isFiniteCoordinate,
  nearestArcFeatureHit,
  nearestArcSegmentHit,
  nearestArcVertexDistance,
  resolveUniqueSelectedArcPath,
  uniqueInternalArcPointHit,
} from './geometryMapHitTesting';
import { dedupeConsecutiveCoordinates, draftPathTolerance, toSvgPath } from './geometryMapDrawing';

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
  tool: GeometryTopologyMapTool;
  currentDraftIdentity: string | null;
  splitMode: 'Anywhere' | 'AtVertex';
  arcPoints: GeometryTopologyArcPointsSnapshot | null;
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
  onMapCommand: (command: GeometryTopologyMapCommand) => void | Promise<void>;
}

interface CanvasSize { width: number; height: number }
interface Camera { centerX: number; centerY: number; worldUnitsPerPixel: number }
interface WorldViewport { minX: number; minY: number; maxX: number; maxY: number }
type DragState =
  | { pointerId: number; ownerKey: string; kind: 'pan'; startClientX: number; startClientY: number; camera: Camera }
  | { pointerId: number; ownerKey: string; kind: 'extent' | 'click'; startClientX: number; startClientY: number; startWorld: { x: number; y: number } }
  | { pointerId: number; ownerKey: string; kind: 'node'; startClientX: number; startClientY: number; startWorld: { x: number; y: number }; nodeFID: number }
  | { pointerId: number; ownerKey: string; kind: 'arc-tool'; tool: Exclude<GeometryTopologyMapTool, 'select'>; startClientX: number; startClientY: number; startWorld: { x: number; y: number } }
  | { pointerId: number; ownerKey: string; kind: 'arc-point'; startClientX: number; startClientY: number; startWorld: GeometryTopologyCoordinate; arcFID: number; pointIndex: number; snapshotRevision: string };

interface ArcDraft {
  ownerKey: string;
  points: GeometryTopologyCoordinate[];
  startNodeFID?: number;
  startArcTarget?: { arcFID: number; sourceSegmentIndex: number; point: GeometryTopologyCoordinate };
}

interface ArcPointPreview {
  arcFID: number;
  pointIndex: number;
  start: GeometryTopologyCoordinate;
  end: GeometryTopologyCoordinate;
}

interface PendingArcClick {
  ownerKey: string;
  world: GeometryTopologyCoordinate;
  clientX: number;
  clientY: number;
  pointerUpTime: number;
}

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
  tool,
  currentDraftIdentity,
  splitMode = 'Anywhere',
  arcPoints,
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
  onMapCommand,
}: GeometryTopologyCanvasProps) {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const svgRef = useRef<SVGSVGElement | null>(null);
  const dragRef = useRef<DragState | null>(null);
  const ownerKeyRef = useRef<string | null>(null);
  const pendingCommandRef = useRef(false);
  const arcDraftRef = useRef<ArcDraft | null>(null);
  const pendingArcClickRef = useRef<PendingArcClick | null>(null);
  const latestViewportRef = useRef('');
  const [size, setSize] = useState<CanvasSize>({ width: 0, height: 0 });
  const [cameraState, setCameraState] = useState<Camera | null>(null);
  const [cameraOwnerKey, setCameraOwnerKey] = useState<string | null>(null);
  const camera = cameraOwnerKey === sourceKey ? cameraState : null;
  const [panMode, setPanMode] = useState(false);
  const [selectionBox, setSelectionBox] = useState<WorldViewport | null>(null);
  const [arcDraft, setArcDraftState] = useState<ArcDraft | null>(null);
  const [arcPointPreview, setArcPointPreview] = useState<ArcPointPreview | null>(null);
  const [toolNotice, setToolNotice] = useState('');

  const extent = map?.extent ?? null;
  const extentKey = extent
    ? `${extent.minX},${extent.minY},${extent.maxX},${extent.maxY}`
    : 'no-extent';
  const selectedArcFIDs = useMemo(() => [...new Set(selectedFIDByKind.arc
    .filter((fid) => Number.isSafeInteger(fid) && fid >= 0))].sort((left, right) => left - right), [selectedFIDByKind.arc]);
  const interactionKey = JSON.stringify([sourceKey, tool, splitMode, currentDraftIdentity, selectedArcFIDs, mapCaptureReady, extentKey]);
  const activeArcPath = arcPoints
    ? resolveUniqueSelectedArcPath(map, selectedArcFIDs, arcPoints.arcFID)
    : null;
  const arcPointsValid = isArcPointsSnapshotForPath(arcPoints, activeArcPath, currentDraftIdentity);
  const viewport = useMemo(() => {
    if (!camera || size.width < MIN_CANVAS_SIZE || size.height < MIN_CANVAS_SIZE) return null;
    const measured = viewportFor(camera, size);
    return isValidViewport(measured) ? measured : null;
  }, [camera, size]);
  const mapCellSize = camera?.worldUnitsPerPixel ?? 0;

  const setArcDraft = useCallback((next: ArcDraft | null) => {
    arcDraftRef.current = next;
    setArcDraftState(next);
  }, []);

  const storeCamera = useCallback((next: Camera | null) => {
    setCameraState(next);
    setCameraOwnerKey(next ? sourceKey : null);
  }, [sourceKey]);

  useEffect(() => {
    dragRef.current = null;
    pendingArcClickRef.current = null;
    setSelectionBox(null);
    setArcPointPreview(null);
    setArcDraft(null);
    setToolNotice('');
  }, [interactionKey, setArcDraft]);

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

  const canEditMap = canMoveNodes && mapCaptureReady && !busy && !pendingCommandRef.current
    && Number.isFinite(mapCellSize) && mapCellSize > 0;

  function submitMapCommand(
    command: GeometryTopologyMapCommand,
    onAccepted?: () => void,
    onRejected?: () => void,
  ): boolean {
    if (!canEditMap || pendingCommandRef.current) return false;
    pendingCommandRef.current = true;
    setToolNotice('');
    try {
      Promise.resolve(onMapCommand(command))
        .then(() => onAccepted?.())
        .catch(() => onRejected?.())
        .finally(() => { pendingCommandRef.current = false; });
      return true;
    } catch {
      pendingCommandRef.current = false;
      onRejected?.();
      return false;
    }
  }

  function appendArcDraftPoint(query: GeometryTopologyCoordinate): void {
    if (!canEditMap || pendingCommandRef.current) return;
    const current = arcDraftRef.current?.ownerKey === interactionKey ? arcDraftRef.current : null;
    if (current?.points.length) {
      const last = current.points[current.points.length - 1];
      if (Math.hypot(last.x - query.x, last.y - query.y) <= draftPathTolerance(mapCellSize)) return;
      const next = { ...current, points: [...current.points, query] };
      setArcDraft(next);
      setToolNotice('');
      return;
    }

    // Match the Host's source hit extent and capture a stable node FID before it
    // can fall back to the spatial index's first candidate.
    const nodeHit = classifyNodeHitExtent(map, query, mapCellSize * 6);
    if (nodeHit.kind === 'ambiguous') {
      setToolNotice(language === 'zh' ? '起点范围内有多个源节点，无法确定连接对象；请移开后再画。' : 'Several source nodes fall in the start hit window; move away to choose an unambiguous start.');
      return;
    }
    if (nodeHit.kind === 'unique') {
      setArcDraft({ ownerKey: interactionKey, points: [nodeHit.point], startNodeFID: nodeHit.nodeFID });
      setToolNotice('');
      return;
    }
    const arcHit = nearestArcSegmentHit(map, query, mapCellSize * 5);
    if (arcHit) {
      if (splitMode === 'AtVertex' && arcHit.feature.paths[0].length <= 2) {
        setToolNotice(language === 'zh'
          ? '此弧段没有内部顶点，不能按“在顶点”方式连接起点；请切换到“任意位置”或选择其他弧段。'
          : 'This arc has no internal vertex for an AtVertex start; switch to Anywhere or choose another arc.');
        return;
      }
      setArcDraft({
        ownerKey: interactionKey,
        points: [arcHit.point],
        startArcTarget: { arcFID: arcHit.arcFID, sourceSegmentIndex: arcHit.sourceSegmentIndex, point: arcHit.point },
      });
      setToolNotice('');
      return;
    }
    setArcDraft({ ownerKey: interactionKey, points: [query] });
    setToolNotice('');
  }

  function cancelArcDraft(): void {
    if (pendingCommandRef.current) return;
    pendingArcClickRef.current = null;
    setArcDraft(null);
    setToolNotice(language === 'zh' ? '已取消尚未提交的弧段绘制。' : 'The unsubmitted arc drawing was cancelled.');
  }

  function commitArcDraft(): void {
    const current = arcDraftRef.current;
    if (!current || current.ownerKey !== interactionKey) return;
    const coordinates = dedupeConsecutiveCoordinates(current.points, draftPathTolerance(mapCellSize));
    if (coordinates.length < 2) {
      setToolNotice(language === 'zh' ? '至少需要两个不同的源坐标点才能提交弧段。' : 'At least two distinct source-coordinate points are required to submit an arc.');
      return;
    }
    const endNodeHit = classifyNodeHitExtent(map, coordinates[coordinates.length - 1], mapCellSize * 6);
    if (endNodeHit.kind === 'ambiguous') {
      setToolNotice(language === 'zh' ? '终点范围内有多个源节点，服务端无法确认连接对象；绘制预览已保留。' : 'Several source nodes fall in the end hit window; the drawing preview is retained until the endpoint is unambiguous.');
      return;
    }
    const command: GeometryTopologyMapCommand = {
      action: 'addArc',
      featureKind: 'arc',
      featureIDs: [],
      coordinates,
      mapCellSize,
      ...(current.startNodeFID !== undefined ? { startNodeFID: current.startNodeFID } : {}),
      ...(current.startArcTarget ? { startArcTarget: current.startArcTarget, splitMode } : {}),
    };
    submitMapCommand(
      command,
      () => { if (arcDraftRef.current === current) setArcDraft(null); },
      () => setToolNotice(language === 'zh' ? '服务未确认弧段写入；临时预览仍保留，可检查诊断后重试。' : 'The service did not confirm the arc edit; the preview remains available for retry after reviewing the diagnostic.'),
    );
  }

  function handleArcToolClick(activeTool: Exclude<GeometryTopologyMapTool, 'select'>, query: GeometryTopologyCoordinate): void {
    if (!canEditMap || pendingCommandRef.current || !isFiniteCoordinate(query)) return;
    if (activeTool === 'addArc') {
      appendArcDraftPoint(query);
      return;
    }
    if (activeTool === 'moveArcPoint') return;

    if (activeTool === 'splitArc') {
      const hit = nearestArcFeatureHit(map, query, mapCellSize * 5);
      const hitPath = hit?.feature.paths[0] ?? null;
      if (!hit || !hitPath) {
        setToolNotice(language === 'zh' ? '未命中唯一的真实弧段线段；请靠近弧段内部再试。' : 'No unique source arc segment was hit; click closer to an arc interior.');
        return;
      }
      if (splitMode === 'AtVertex' && hitPath.length <= 2) {
        setToolNotice(language === 'zh' ? '这条弧段没有内部顶点，无法使用“在顶点”模式分割。' : 'This arc has no internal vertex, so AtVertex mode cannot split it.');
        return;
      }
      const first = hitPath[0];
      const last = hitPath[hitPath.length - 1];
      const atEndpoint = (query.x === first.x && query.y === first.y) || (query.x === last.x && query.y === last.y);
      if (splitMode === 'Anywhere' && atEndpoint) {
        setToolNotice(language === 'zh' ? '分割点不能与弧段端点重合。' : 'An arc cannot be split at its endpoint.');
        return;
      }
      submitMapCommand({
        action: 'splitArc', featureKind: 'arc', featureIDs: [], arcFID: hit.arcFID,
        coordinates: [query], mapCellSize, splitMode,
      });
      return;
    }

    const selectedArc = arcPointsValid && arcPoints
      ? resolveUniqueSelectedArcPath(map, selectedArcFIDs, arcPoints.arcFID)
      : null;
    if (!selectedArc || !arcPoints) {
      setToolNotice(language === 'zh' ? '请先在表格中唯一选中一条弧段，并等待其源点快照就绪。' : 'Select exactly one arc in the table and wait for its source-point snapshot.');
      return;
    }

    if (activeTool === 'deleteArcPoint') {
      const hit = uniqueInternalArcPointHit(arcPoints, query, mapCellSize * 8);
      if (!hit) {
        setToolNotice(language === 'zh' ? '未命中唯一内部点；弧段首尾端点不可删除。' : 'No unique internal point was hit; arc endpoints cannot be deleted.');
        return;
      }
      submitMapCommand({
        action: 'deleteArcPoint', featureKind: 'arc', featureIDs: [], arcFID: hit.arcFID,
        pointIndex: hit.pointIndex, mapCellSize,
      });
      return;
    }

    if (activeTool === 'insertArcPoint') {
      const hit = nearestArcSegmentHit(map, query, mapCellSize * 16, selectedArc.feature.fid ?? undefined);
      if (!hit || hit.arcFID !== selectedArc.feature.fid) {
        setToolNotice(language === 'zh' ? '请在当前唯一选中弧段的真实线段附近插入点。' : 'Click near a source segment on the uniquely selected arc.');
        return;
      }
      if (nearestArcVertexDistance(selectedArc.path, query) < mapCellSize * 8) {
        setToolNotice(language === 'zh' ? '插入位置太靠近现有顶点；请点在线段内部。' : 'The insertion point is too close to an existing vertex; click inside a segment.');
        return;
      }
      submitMapCommand({
        action: 'insertArcPoint', featureKind: 'arc', featureIDs: [], arcFID: hit.arcFID,
        sourceSegmentIndex: hit.sourceSegmentIndex, coordinates: [query], mapCellSize,
      });
    }
  }

  function finishPointerGesture(event: ReactPointerEvent<SVGSVGElement>): void {
    const drag = dragRef.current;
    if (!drag || drag.pointerId !== event.pointerId) return;
    if (drag.ownerKey !== interactionKey) {
      dragRef.current = null;
      setSelectionBox(null);
      setArcPointPreview(null);
      return;
    }
    dragRef.current = null;
    setSelectionBox(null);
    if (drag.kind === 'pan') return;
    const end = getWorldPoint(event);
    if (!end) return;
    const distance = Math.hypot(event.clientX - drag.startClientX, event.clientY - drag.startClientY);
    const moved = distance >= DRAG_THRESHOLD_PX;
    if (drag.kind === 'arc-tool') {
      if (!moved && !busy) {
        if (drag.tool === 'addArc') {
          // Defer adding the point until click.detail is known. The second
          // pointerup of a double-click has detail=2 and must not add a short tail.
          pendingArcClickRef.current = {
            ownerKey: interactionKey,
            world: end,
            clientX: event.clientX,
            clientY: event.clientY,
            pointerUpTime: event.timeStamp,
          };
        } else {
          handleArcToolClick(drag.tool, end);
        }
      }
      return;
    }
    if (drag.kind === 'arc-point') {
      setArcPointPreview(null);
      if (!moved || busy || !arcPointsValid || !arcPoints || arcPoints.revision !== drag.snapshotRevision || arcPoints.arcFID !== drag.arcFID) return;
      submitMapCommand({
        action: 'moveArcPoint', featureKind: 'arc', featureIDs: [], arcFID: drag.arcFID,
        pointIndex: drag.pointIndex, coordinates: [end], mapCellSize,
      });
      return;
    }
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
    pendingArcClickRef.current = null;
    if (!camera || !viewport || busy || pendingCommandRef.current) return;
    const wantsPan = panMode || event.button === 1 || event.button === 2;
    if (!wantsPan && !mapCaptureReady) return;
    const world = getWorldPoint(event);
    if (!world) return;
    const feature = getMapFeature(event);
    if (wantsPan) {
      event.preventDefault();
      dragRef.current = { pointerId: event.pointerId, ownerKey: interactionKey, kind: 'pan', startClientX: event.clientX, startClientY: event.clientY, camera };
      setSelectionBox(null);
      setArcPointPreview(null);
      event.currentTarget.setPointerCapture(event.pointerId);
      return;
    }
    if (tool !== 'select') {
      if (!canEditMap) return;
      if (tool === 'addArc') event.currentTarget.focus();
      if (tool === 'moveArcPoint') {
        const selectedArc = arcPointsValid && arcPoints
          ? resolveUniqueSelectedArcPath(map, selectedArcFIDs, arcPoints.arcFID)
          : null;
        const hit = arcPointsValid && arcPoints ? uniqueInternalArcPointHit(arcPoints, world, mapCellSize * 8) : null;
        if (!selectedArc || !arcPoints || !hit || hit.arcFID !== selectedArc.feature.fid) {
          setToolNotice(language === 'zh' ? '请拖动当前唯一选中弧段的内部点；首尾端点不可移动。' : 'Drag an internal point on the uniquely selected arc; endpoints cannot move.');
          return;
        }
        event.preventDefault();
        dragRef.current = {
          pointerId: event.pointerId, ownerKey: interactionKey, kind: 'arc-point',
          startClientX: event.clientX, startClientY: event.clientY, startWorld: hit.point,
          arcFID: hit.arcFID, pointIndex: hit.pointIndex, snapshotRevision: arcPoints.revision,
        };
        setArcPointPreview({ arcFID: hit.arcFID, pointIndex: hit.pointIndex, start: hit.point, end: hit.point });
      } else {
        event.preventDefault();
        dragRef.current = {
          pointerId: event.pointerId, ownerKey: interactionKey, kind: 'arc-tool', tool,
          startClientX: event.clientX, startClientY: event.clientY, startWorld: world,
        };
      }
      setSelectionBox(null);
      event.currentTarget.setPointerCapture(event.pointerId);
      return;
    }
    const sourceNodePoint = feature?.featureKind === 'node' ? feature.paths.find((path) => path.length > 0)?.[0] : null;
    const hitFID = feature?.featureKind === 'node' && typeof feature.fid === 'number' && Number.isInteger(feature.fid)
      && sourceNodePoint && Number.isFinite(sourceNodePoint.x) && Number.isFinite(sourceNodePoint.y)
      ? feature.fid
      : null;
    let drag: DragState | null = null;
    if (hitFID !== null && canMoveNodes && mapCaptureReady && !event.shiftKey) {
      event.preventDefault();
      drag = { pointerId: event.pointerId, ownerKey: interactionKey, kind: 'node', startClientX: event.clientX, startClientY: event.clientY, startWorld: { x: sourceNodePoint!.x, y: sourceNodePoint!.y }, nodeFID: hitFID };
    } else if (canSelect && mapCaptureReady && (event.shiftKey || !feature)) {
      event.preventDefault();
      drag = { pointerId: event.pointerId, ownerKey: interactionKey, kind: 'extent', startClientX: event.clientX, startClientY: event.clientY, startWorld: world };
    } else if (feature) {
      drag = { pointerId: event.pointerId, ownerKey: interactionKey, kind: 'click', startClientX: event.clientX, startClientY: event.clientY, startWorld: world };
    } else {
      drag = { pointerId: event.pointerId, ownerKey: interactionKey, kind: 'click', startClientX: event.clientX, startClientY: event.clientY, startWorld: world };
    }
    if (!drag) return;
    dragRef.current = drag;
    setSelectionBox(null);
    event.currentTarget.setPointerCapture(event.pointerId);
  }

  function onPointerMove(event: ReactPointerEvent<SVGSVGElement>) {
    const drag = dragRef.current;
    if (!drag || drag.pointerId !== event.pointerId) return;
    if (drag.ownerKey !== interactionKey) {
      dragRef.current = null;
      setSelectionBox(null);
      setArcPointPreview(null);
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
    } else if (drag.kind === 'arc-point') {
      if (!arcPointsValid || !arcPoints || arcPoints.revision !== drag.snapshotRevision || arcPoints.arcFID !== drag.arcFID) {
        dragRef.current = null;
        setArcPointPreview(null);
        setToolNotice(language === 'zh' ? '弧段点源快照已变化，已取消这次拖动。' : 'The source point snapshot changed; this drag was cancelled.');
        return;
      }
      const point = getWorldPoint(event);
      if (point) setArcPointPreview({ arcFID: drag.arcFID, pointIndex: drag.pointIndex, start: drag.startWorld, end: point });
    }
  }

  function onPointerUp(event: ReactPointerEvent<SVGSVGElement>) {
    finishPointerGesture(event);
  }

  function onCanvasClick(event: ReactMouseEvent<SVGSVGElement>) {
    const pending = pendingArcClickRef.current;
    pendingArcClickRef.current = null;
    if (tool !== 'addArc' || event.detail !== 1 || !pending || pending.ownerKey !== interactionKey) return;
    if (Math.hypot(event.clientX - pending.clientX, event.clientY - pending.clientY) > 4) return;
    if (event.timeStamp < pending.pointerUpTime || event.timeStamp - pending.pointerUpTime > 1000) return;
    appendArcDraftPoint(pending.world);
  }

  function onDoubleClick(event: ReactMouseEvent<SVGSVGElement>) {
    pendingArcClickRef.current = null;
    if (tool !== 'addArc' || busy || !mapCaptureReady) return;
    event.preventDefault();
    // The first click already added its point. The second click is intentionally ignored,
    // so the double-click gesture only commits the existing source-coordinate path.
    commitArcDraft();
  }

  function onCanvasKeyDown(event: ReactKeyboardEvent<SVGSVGElement>) {
    if (event.repeat || tool !== 'addArc' || pendingCommandRef.current) return;
    if (event.key === 'Escape') {
      event.preventDefault();
      cancelArcDraft();
    } else if (event.key === 'Enter') {
      event.preventDefault();
      commitArcDraft();
    }
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
    dragRef.current = null;
    setSelectionBox(null);
    setArcPointPreview(null);
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
    path: feature.paths.map((points) => toSvgPath(points, feature.geometryType === 'polygon')).filter(Boolean).join(' '),
  })) ?? [], [map, selectedFIDByKind]);

  const shownArcDraft = arcDraft?.ownerKey === interactionKey && tool === 'addArc' ? arcDraft : null;
  const shownArcDraftPath = shownArcDraft ? toSvgPath(shownArcDraft.points) : '';
  const showInternalArcPoints = tool === 'moveArcPoint' || tool === 'insertArcPoint' || tool === 'deleteArcPoint'
    || (tool === 'splitArc' && splitMode === 'AtVertex');
  const shownInternalArcPoints = showInternalArcPoints && arcPointsValid && arcPoints && activeArcPath
    ? arcPoints.rows.filter((row) => row.pointKind === 'internal' && Number.isSafeInteger(row.internalPointIndex))
    : [];
  const activeToolLabel = language === 'zh'
    ? ({ select: '选择', addArc: '绘制弧段', moveArcPoint: '移动内点', insertArcPoint: '插入点', deleteArcPoint: '删除点', splitArc: '分割弧段' } as const)[tool]
    : ({ select: 'Select', addArc: 'Draw arc', moveArcPoint: 'Move internal point', insertArcPoint: 'Insert point', deleteArcPoint: 'Delete point', splitArc: 'Split arc' } as const)[tool];

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
    : language === 'zh' ? ' · 当前地图拓扑上下文尚未就绪，地图选择和编辑已停用' : ' · The current map topology context is not ready; map selection and editing are disabled';
  const toolInstruction = language === 'zh'
    ? tool === 'addArc' ? '绘制弧段：逐点点击，按 Enter 或双击提交，按 Escape 取消。预览尚未写入几何草稿。'
      : tool === 'moveArcPoint' ? '移动内点：拖动当前唯一选中弧段的内部点；首尾端点不可移动。'
        : tool === 'insertArcPoint' ? '插入点：点击当前唯一选中弧段的线段内部；端点及现有顶点附近不会插入。'
          : tool === 'deleteArcPoint' ? '删除点：点击当前唯一选中弧段的内部点；首尾端点不可删除。'
            : tool === 'splitArc' ? `分割弧段：点击真实弧段线段；当前模式为${splitMode === 'AtVertex' ? '在内部顶点' : '任意位置'}。`
              : '点按要素执行 Host 空间选择；拖动空白处框选，拖动节点提交 Host 节点移动。'
    : tool === 'addArc' ? 'Draw an arc: click vertices, press Enter or double-click to submit, or Escape to cancel. The preview is not in the geometry draft.'
      : tool === 'moveArcPoint' ? 'Move internal point: drag a point on the uniquely selected arc; endpoints cannot move.'
        : tool === 'insertArcPoint' ? 'Insert point: click inside a segment of the uniquely selected arc; endpoints and nearby vertices are excluded.'
          : tool === 'deleteArcPoint' ? 'Delete point: click an internal point on the uniquely selected arc; endpoints cannot be deleted.'
            : tool === 'splitArc' ? `Split arc: click a real arc segment; mode is ${splitMode === 'AtVertex' ? 'at internal vertex' : 'anywhere'}.`
              : 'Click for Host spatial selection; drag empty space to select an extent, or drag a node for a Host node move.';
  const toolDisabledReason = tool !== 'select' && !canEditMap
    ? language === 'zh' ? ' · 需要有效地图捕获、可编辑草稿和正的源坐标比例。' : ' · A valid map capture, editable draft, and positive source-coordinate scale are required.'
    : '';

  return <section className="editor-table-card" data-testid="geometry-topology-map-panel" aria-label={language === 'zh' ? '几何拓扑图' : 'Geometry topology map'}>
    <div className="editor-terrain-table-heading" style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8, flexWrap: 'wrap' }}>
      <strong>{language === 'zh' ? `几何拓扑图 · ${activeToolLabel}` : `Geometry topology map · ${activeToolLabel}`}</strong>
      <div role="toolbar" aria-label={language === 'zh' ? '拓扑视图工具' : 'Topology view tools'} style={{ display: 'flex', gap: 6, alignItems: 'center' }}>
        <button type="button" className="editor-button" data-testid="geometry-topology-fit" disabled={controlsDisabled} onClick={fit}>{language === 'zh' ? '适合范围' : 'Fit'}</button>
        <button type="button" className="editor-button" data-testid="geometry-topology-zoom-in" disabled={controlsDisabled} aria-label={language === 'zh' ? '放大' : 'Zoom in'} onClick={() => applyZoom(1 / ZOOM_FACTOR)}>＋</button>
        <button type="button" className="editor-button" data-testid="geometry-topology-zoom-out" disabled={controlsDisabled} aria-label={language === 'zh' ? '缩小' : 'Zoom out'} onClick={() => applyZoom(ZOOM_FACTOR)}>−</button>
        <button type="button" className={`editor-button${panMode ? ' editor-button-primary' : ''}`} data-testid="geometry-topology-pan-toggle" aria-pressed={panMode} disabled={controlsDisabled} onClick={() => setPanMode((value) => !value)}>{language === 'zh' ? (panMode ? '平移中' : '平移') : (panMode ? 'Panning' : 'Pan')}</button>
      </div>
    </div>
    <div data-testid="geometry-topology-map-status" className="editor-field-muted" role="status" aria-live="polite" style={{ margin: '4px 0 8px' }}>
      {statusText}{captureStatus}{busy ? (language === 'zh' ? ' · 正在等待拓扑回执' : ' · Waiting for topology receipt') : ''}
      {toolDisabledReason}
      <div data-testid="geometry-topology-tool-instruction">{toolInstruction}</div>
      {toolNotice && <div data-testid="geometry-topology-tool-notice" role="alert">{toolNotice}</div>}
      {shownArcDraft && <div data-testid="geometry-topology-arc-draft-status">{language === 'zh'
        ? pendingCommandRef.current
          ? `正在提交弧段预览，${shownArcDraft.points.length} 个源坐标点；等待 Host 确认。`
          : `弧段绘制预览，${shownArcDraft.points.length} 个源坐标点；尚未提交。`
        : pendingCommandRef.current
          ? `Submitting arc preview with ${shownArcDraft.points.length} source-coordinate points; waiting for Host confirmation.`
          : `Arc drawing preview, ${shownArcDraft.points.length} source-coordinate points; not submitted.`}</div>}
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
        data-active-tool={tool}
        tabIndex={0}
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerUp}
        onPointerCancel={() => { dragRef.current = null; pendingArcClickRef.current = null; setSelectionBox(null); setArcPointPreview(null); setArcDraft(null); }}
        onClick={onCanvasClick}
        onDoubleClick={onDoubleClick}
        onKeyDown={onCanvasKeyDown}
        onContextMenu={(event) => event.preventDefault()}
        onWheel={onWheel}
        style={{ display: 'block', width: '100%', height: '100%', touchAction: 'none', cursor: panMode ? 'grab' : tool === 'select' ? canSelect && mapCaptureReady ? 'crosshair' : 'default' : canEditMap ? 'crosshair' : 'not-allowed', userSelect: 'none', outlineOffset: -2 }}
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
        {shownInternalArcPoints.map((row) => <circle
          key={`arc-${arcPoints!.arcFID}-internal-${row.internalPointIndex}`}
          cx={row.x}
          cy={-row.y}
          r={mapCellSize * 4.5}
          fill={tool === 'moveArcPoint' ? '#f59e0b' : tool === 'deleteArcPoint' ? '#dc2626' : '#fbbf24'}
          stroke="#fff"
          strokeWidth={1.5}
          vectorEffect="non-scaling-stroke"
          pointerEvents="none"
          data-topology-internal-point-index={row.internalPointIndex ?? undefined}
          aria-hidden="true"
        />)}
        {arcPointPreview && arcPointsValid && arcPoints?.revision === (dragRef.current?.kind === 'arc-point' ? dragRef.current.snapshotRevision : undefined)
          && <g data-testid="geometry-topology-arc-point-preview" pointerEvents="none" aria-hidden="true">
          <line x1={arcPointPreview.start.x} y1={-arcPointPreview.start.y} x2={arcPointPreview.end.x} y2={-arcPointPreview.end.y}
            stroke="#f59e0b" strokeDasharray="4 3" strokeWidth={2} vectorEffect="non-scaling-stroke" />
          <circle cx={arcPointPreview.end.x} cy={-arcPointPreview.end.y} r={mapCellSize * 5.5}
            fill="#f59e0b" stroke="#fff" strokeWidth={1.5} vectorEffect="non-scaling-stroke" />
        </g>}
        {shownArcDraft && shownArcDraftPath && <g data-testid="geometry-topology-arc-draft" aria-label={language === 'zh' ? '尚未提交的弧段绘制预览' : 'Unsubmitted arc drawing preview'} pointerEvents="none">
          <path d={shownArcDraftPath} fill="none" stroke="#d97706" strokeWidth={2.5} strokeDasharray="6 4" vectorEffect="non-scaling-stroke" />
          {shownArcDraft.points.map((point, index) => <circle key={`arc-draft-point-${index}`} cx={point.x} cy={-point.y} r={mapCellSize * 3.5}
            fill={index === 0 ? '#b42318' : '#d97706'} stroke="#fff" strokeWidth={1.2} vectorEffect="non-scaling-stroke" />)}
        </g>}
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
      {toolInstruction}{' '}{language === 'zh'
        ? '滚轮缩放；平移工具或中键拖动平移。所有坐标均为 Geometry 源坐标。'
        : 'Wheel zooms; use Pan or the middle mouse button to pan. Coordinates remain in Geometry source space.'}
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
