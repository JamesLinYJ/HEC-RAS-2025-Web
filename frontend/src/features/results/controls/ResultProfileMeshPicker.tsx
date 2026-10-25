import { useEffect, useRef, useState, type KeyboardEvent, type PointerEvent as ReactPointerEvent, type WheelEvent } from 'react';
import type { BackendGateway, Language } from '../../../shared/workbench';

interface ResultProfileMeshExtent {
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
}

interface ResultProfileMeshElement {
  elementIndex: number;
  paths: Array<Array<{ x: number; y: number }>>;
}

interface ResultProfileMeshResponse {
  resultPath: string;
  revision: string;
  flowAreaName: string;
  flowAreaIndex: number;
  elementKind: 'Cell' | 'Face';
  available: boolean;
  unavailableReason: string | null;
  sourceExtent: ResultProfileMeshExtent | null;
  requestedExtent: ResultProfileMeshExtent | null;
  totalElements: number;
  intersectingElements: number | null;
  returnedElements: number;
  elementLimit: number;
  vertexLimit: number;
  isComplete: boolean;
  requiresZoom: boolean;
  elements: ResultProfileMeshElement[];
}

interface ResultProfileMeshPickerProps {
  backend: BackendGateway;
  backendRevision?: number;
  language: Language;
  owner: string;
  resultPath: string;
  revision: string;
  flowAreaIndex: number | null;
  flowAreaName: string;
  elementCount: number;
  elementKind: 'Cell' | 'Face' | null;
  selectedElementIndex: number | null;
  ready: boolean;
  onSelectElement: (elementIndex: number) => void;
}

interface MeshPickerState {
  owner: string;
  viewport: ResultProfileMeshExtent | null;
  sourceExtent: ResultProfileMeshExtent | null;
  response: ResultProfileMeshResponse | null;
  responseKey: string | null;
  loading: boolean;
  error: string | null;
}

interface PendingRequest {
  controller: AbortController;
  generation: number;
  active: boolean;
}

interface DragState {
  pointerID: number;
  startX: number;
  startY: number;
  startWorld: { x: number; y: number };
  baseExtent: ResultProfileMeshExtent;
  moved: boolean;
  active: boolean;
}

const VIEW_WIDTH = 720;
const VIEW_HEIGHT = 360;
const VIEW_PADDING = 18;
const RESULT_PROFILE_MAP_LIMIT = 2500;

function emptyState(owner: string): MeshPickerState {
  return { owner, viewport: null, sourceExtent: null, response: null, responseKey: null, loading: false, error: null };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function isSha256(value: unknown): value is string {
  return typeof value === 'string' && /^[a-f\d]{64}$/i.test(value);
}

function samePath(left: string, right: string): boolean {
  const normalize = (value: string): string => {
    const path = value.trim().replaceAll('\\', '/').replace(/\/+$/, '') || '/';
    return /^[a-z]:\//i.test(path) ? path.toLowerCase() : path;
  };
  return normalize(left) === normalize(right);
}

function validExtent(value: unknown): value is ResultProfileMeshExtent {
  if (!isRecord(value)) return false;
  if (!(typeof value.minX === 'number' && Number.isFinite(value.minX)
    && typeof value.minY === 'number' && Number.isFinite(value.minY)
    && typeof value.maxX === 'number' && Number.isFinite(value.maxX)
    && typeof value.maxY === 'number' && Number.isFinite(value.maxY)
    && value.maxX > value.minX && value.maxY > value.minY)) return false;
  return Number.isFinite(value.maxX - value.minX) && Number.isFinite(value.maxY - value.minY);
}

function parseMapResponse(
  value: unknown,
  expected: {
    resultPath: string;
    revision: string;
    flowAreaIndex: number;
    flowAreaName: string;
    elementKind: 'Cell' | 'Face';
    elementCount: number;
    requestedExtent: ResultProfileMeshExtent | null;
  },
): ResultProfileMeshResponse {
  if (!isRecord(value)) {
    throw new Error('网格几何接口返回的数据结构无效。');
  }
  const resultPath = value.resultPath;
  const revision = value.revision;
  const flowAreaName = value.flowAreaName;
  const flowAreaIndex = value.flowAreaIndex;
  const elementKind = value.elementKind;
  const available = value.available;
  const unavailableReason = value.unavailableReason;
  const totalElements = value.totalElements;
  const intersectingElements = value.intersectingElements;
  const returnedElements = value.returnedElements;
  const elementLimit = value.elementLimit;
  const vertexLimit = value.vertexLimit;
  const isComplete = value.isComplete;
  const requiresZoom = value.requiresZoom;
  const rawElements = value.elements;
  if (typeof resultPath !== 'string' || !isSha256(revision)
    || typeof flowAreaName !== 'string' || typeof flowAreaIndex !== 'number' || !Number.isInteger(flowAreaIndex) || flowAreaIndex < 0
    || (elementKind !== 'Cell' && elementKind !== 'Face')
    || typeof available !== 'boolean' || (unavailableReason !== null && typeof unavailableReason !== 'string')
    || typeof totalElements !== 'number' || !Number.isInteger(totalElements) || totalElements < 0
    || (intersectingElements !== null && (typeof intersectingElements !== 'number' || !Number.isInteger(intersectingElements) || intersectingElements < 0))
    || typeof returnedElements !== 'number' || !Number.isInteger(returnedElements) || returnedElements < 0
    || typeof elementLimit !== 'number' || !Number.isInteger(elementLimit) || elementLimit < 1 || elementLimit > 5000
    || typeof vertexLimit !== 'number' || !Number.isInteger(vertexLimit) || vertexLimit < 1 || vertexLimit > 120_000
    || typeof isComplete !== 'boolean' || typeof requiresZoom !== 'boolean'
    || !Array.isArray(rawElements)) {
    throw new Error('网格几何接口返回的数据结构无效。');
  }
  if (returnedElements > totalElements || (intersectingElements !== null && intersectingElements > totalElements)
    || isComplete === requiresZoom) {
    throw new Error('网格几何接口返回的计数或完整性状态互相矛盾。');
  }
  if (!samePath(resultPath, expected.resultPath) || revision.toUpperCase() !== expected.revision.toUpperCase()
    || flowAreaIndex !== expected.flowAreaIndex || flowAreaName !== expected.flowAreaName
    || elementKind !== expected.elementKind || totalElements !== expected.elementCount) {
    throw new Error('网格几何响应与当前 Result、SHA、流区、元素类型或目录元素数不一致。');
  }
  const sourceExtentValue = value.sourceExtent;
  const requestedExtentValue = value.requestedExtent;
  if (sourceExtentValue !== null && !validExtent(sourceExtentValue)) throw new Error('网格几何接口返回了无效的源范围。');
  if (requestedExtentValue !== null && !validExtent(requestedExtentValue)) throw new Error('网格几何接口返回了无效的请求范围。');
  if (!available) {
    if (returnedElements !== 0 || rawElements.length !== 0) throw new Error('不可用网格响应不应包含元素几何。');
    return {
      resultPath,
      revision,
      flowAreaName,
      flowAreaIndex,
      elementKind,
      available: false,
      unavailableReason,
      sourceExtent: null,
      requestedExtent: null,
      totalElements,
      intersectingElements,
      returnedElements: 0,
      elementLimit,
      vertexLimit,
      isComplete,
      requiresZoom: false,
      elements: [],
    };
  }
  if (!validExtent(sourceExtentValue) || !validExtent(requestedExtentValue)) {
    throw new Error('可用的网格几何缺少有限的源范围或请求范围。');
  }
  const expectedRequestedExtent = expected.requestedExtent === null
    ? sourceExtentValue
    : intersectExtents(sourceExtentValue, expected.requestedExtent) ?? expected.requestedExtent;
  if (!sameExtent(requestedExtentValue, expectedRequestedExtent)) {
    throw new Error('网格几何响应范围与本次请求视口不一致。');
  }
  if (rawElements.length > elementLimit || rawElements.length > 5000) {
    throw new Error('网格元素回执超过 Host 单次读取上限。');
  }
  if (!isComplete && !requiresZoom) throw new Error('不完整网格响应没有提供需要缩放的状态。');

  let vertexCount = 0;
  const elements: ResultProfileMeshElement[] = rawElements.map((rawElement) => {
    if (!isRecord(rawElement) || typeof rawElement.elementIndex !== 'number' || !Number.isInteger(rawElement.elementIndex) || !Array.isArray(rawElement.paths)) {
      throw new Error('网格几何包含无效的元素编号或路径。');
    }
    const elementIndex = rawElement.elementIndex;
    if (elementIndex < 0 || elementIndex >= totalElements) throw new Error('网格几何元素编号超出 Result 范围。');
    const paths = rawElement.paths.map((rawPath) => {
      if (!Array.isArray(rawPath) || rawPath.length < (expected.elementKind === 'Cell' ? 3 : 2)
        || rawPath.length > 120_000 || vertexCount + rawPath.length > vertexLimit) {
        throw new Error('网格几何包含无效或不完整的路径。');
      }
      vertexCount += rawPath.length;
      return rawPath.map((rawPoint) => {
        if (!isRecord(rawPoint) || typeof rawPoint.x !== 'number' || !Number.isFinite(rawPoint.x)
          || typeof rawPoint.y !== 'number' || !Number.isFinite(rawPoint.y)) {
          throw new Error('网格几何包含非有限坐标。');
        }
        return { x: rawPoint.x, y: rawPoint.y };
      });
    });
    if (paths.length === 0) throw new Error('网格元素没有可绘制路径。');
    return { elementIndex, paths };
  });
  const seen = new Set<number>();
  for (const element of elements) {
    if (seen.has(element.elementIndex)) throw new Error('网格几何包含重复元素编号。');
    seen.add(element.elementIndex);
  }
  if (elements.length !== returnedElements || elements.length > elementLimit || vertexCount > vertexLimit) {
    throw new Error('返回网格元素或顶点计数超出 Host 回执上限。');
  }
  if (intersectingElements !== null && elements.length > intersectingElements) throw new Error('返回元素数大于相交元素计数。');
  if ((!isComplete || requiresZoom) && elements.length !== 0) {
    throw new Error('网格服务返回了不完整几何；为避免误选，已拒绝显示。');
  }
  if (isComplete && requiresZoom) throw new Error('网格服务同时标记完整与需要缩放，回执状态冲突。');

  return {
    resultPath,
    revision,
    flowAreaName,
    flowAreaIndex,
    elementKind,
    available: true,
    unavailableReason,
    sourceExtent: sourceExtentValue,
    requestedExtent: requestedExtentValue,
    totalElements,
    intersectingElements,
    returnedElements,
    elementLimit,
    vertexLimit,
    isComplete,
    requiresZoom,
    elements,
  };
}

function intersectExtents(left: ResultProfileMeshExtent, right: ResultProfileMeshExtent): ResultProfileMeshExtent | null {
  const extent = {
    minX: Math.max(left.minX, right.minX),
    minY: Math.max(left.minY, right.minY),
    maxX: Math.min(left.maxX, right.maxX),
    maxY: Math.min(left.maxY, right.maxY),
  };
  return validExtent(extent) ? extent : null;
}

function sameExtent(left: unknown, right: ResultProfileMeshExtent): boolean {
  if (!validExtent(left)) return false;
  const close = (actual: number, expected: number): boolean =>
    Math.abs(actual - expected) <= Number.EPSILON * Math.max(1, Math.abs(actual), Math.abs(expected)) * 64;
  return close(left.minX, right.minX) && close(left.minY, right.minY)
    && close(left.maxX, right.maxX) && close(left.maxY, right.maxY);
}

function projection(extent: ResultProfileMeshExtent): { scale: number; offsetX: number; offsetY: number } {
  const width = extent.maxX - extent.minX;
  const height = extent.maxY - extent.minY;
  if (!Number.isFinite(width) || !Number.isFinite(height) || width <= 0 || height <= 0) {
    return { scale: 1, offsetX: VIEW_PADDING, offsetY: VIEW_PADDING };
  }
  const scale = Math.min((VIEW_WIDTH - VIEW_PADDING * 2) / width, (VIEW_HEIGHT - VIEW_PADDING * 2) / height);
  if (!Number.isFinite(scale) || scale <= 0) return { scale: 1, offsetX: VIEW_PADDING, offsetY: VIEW_PADDING };
  return {
    scale,
    offsetX: (VIEW_WIDTH - width * scale) / 2,
    offsetY: (VIEW_HEIGHT - height * scale) / 2,
  };
}

function svgPoint(svg: SVGSVGElement, clientX: number, clientY: number): { x: number; y: number } | null {
  const matrix = svg.getScreenCTM();
  if (!matrix) return null;
  try {
    const point = svg.createSVGPoint();
    point.x = clientX;
    point.y = clientY;
    const local = point.matrixTransform(matrix.inverse());
    return Number.isFinite(local.x) && Number.isFinite(local.y) ? { x: local.x, y: local.y } : null;
  } catch {
    return null;
  }
}

function worldPoint(svg: SVGSVGElement, clientX: number, clientY: number, extent: ResultProfileMeshExtent): { x: number; y: number } | null {
  const local = svgPoint(svg, clientX, clientY);
  if (!local) return null;
  const transform = projection(extent);
  const result = {
    x: extent.minX + (local.x - transform.offsetX) / transform.scale,
    y: extent.minY + (VIEW_HEIGHT - transform.offsetY - local.y) / transform.scale,
  };
  return Number.isFinite(result.x) && Number.isFinite(result.y) ? result : null;
}

function zoomAt(extent: ResultProfileMeshExtent, factor: number, anchor: { x: number; y: number }): ResultProfileMeshExtent | null {
  const next = {
    minX: anchor.x + (extent.minX - anchor.x) * factor,
    minY: anchor.y + (extent.minY - anchor.y) * factor,
    maxX: anchor.x + (extent.maxX - anchor.x) * factor,
    maxY: anchor.y + (extent.maxY - anchor.y) * factor,
  };
  return validExtent(next) ? next : null;
}

function localized(language: Language, zh: string, en: string): string {
  return language === 'zh' ? zh : en;
}

export function ResultProfileMeshPicker({
  backend,
  backendRevision,
  language,
  owner,
  resultPath,
  revision,
  flowAreaIndex,
  flowAreaName,
  elementCount,
  elementKind,
  selectedElementIndex,
  ready,
  onSelectElement,
}: ResultProfileMeshPickerProps) {
  const zh = language === 'zh';
  const [state, setState] = useState<MeshPickerState>(() => emptyState(owner));
  const requestGenerationRef = useRef(0);
  const pendingRequestRef = useRef<PendingRequest | null>(null);
  const requestKeyRef = useRef('');
  const dragRef = useRef<DragState | null>(null);
  const suppressPointerClickRef = useRef(false);
  const active = state.owner === owner ? state : emptyState(owner);
  const extentKey = active.viewport ? JSON.stringify(active.viewport) : '';
  const requestKey = JSON.stringify([owner, resultPath, revision, flowAreaIndex, flowAreaName, elementCount, elementKind, extentKey, backendRevision ?? null]);
  requestKeyRef.current = requestKey;
  const currentResponse = active.responseKey === requestKey ? active.response : null;
  const drawExtent = active.viewport ?? currentResponse?.requestedExtent ?? active.sourceExtent;

  useEffect(() => {
    pendingRequestRef.current?.controller.abort();
    if (pendingRequestRef.current) pendingRequestRef.current.active = false;
    pendingRequestRef.current = null;
    const generation = ++requestGenerationRef.current;
    if (!ready || !resultPath || !isSha256(revision) || flowAreaIndex === null || elementKind === null) {
      setState((current) => current.owner === owner
        ? { ...current, response: null, responseKey: null, loading: false, error: null }
        : emptyState(owner));
      return;
    }
    if (!backend.supports('result.profileMap')) {
      setState((current) => ({ ...emptyState(owner), viewport: current.owner === owner ? current.viewport : null,
        sourceExtent: current.owner === owner ? current.sourceExtent : null,
        error: localized(language, '当前 C# 服务未发布 Result 网格读取能力。', 'The C# service does not advertise Result mesh geometry reading.') }));
      return;
    }

    const controller = new AbortController();
    const pending: PendingRequest = { controller, generation, active: true };
    pendingRequestRef.current = pending;
    const isCurrent = (): boolean => pending.active && !controller.signal.aborted
      && requestGenerationRef.current === generation && pendingRequestRef.current === pending
      && requestKeyRef.current === requestKey;
    setState((current) => ({
      ...emptyState(owner),
      viewport: current.owner === owner ? current.viewport : null,
      sourceExtent: current.owner === owner ? current.sourceExtent : null,
      response: null,
      responseKey: null,
      loading: true,
    }));

    const requestedExtent = active.viewport;
    const timer = setTimeout(() => {
      void (async (): Promise<void> => {
        try {
          const raw = await backend.request<unknown>('result.profileMap', {
            resultPath,
            revision,
            flowAreaIndex,
            elementKind,
            ...(requestedExtent ? { extent: requestedExtent } : {}),
            limit: RESULT_PROFILE_MAP_LIMIT,
          }, { signal: controller.signal });
          if (!isCurrent()) return;
          const response = parseMapResponse(raw, {
            resultPath,
            revision,
            flowAreaIndex,
            flowAreaName,
            elementKind,
            elementCount,
            requestedExtent,
          });
          if (!response.available) {
            const detail = response.unavailableReason;
            setState((current) => current.owner === owner ? {
              ...current,
              response,
              responseKey: requestKey,
              loading: false,
              error: detail ? localized(language, `当前 Result 没有可用的二维网格：${detail}`, `The selected Result has no available 2D mesh: ${detail}`)
                : localized(language, '当前 Result 没有可用的二维网格。', 'The selected Result has no available 2D mesh.'),
            } : current);
            return;
          }
          setState((current) => current.owner === owner ? {
            ...current,
            response,
            responseKey: requestKey,
            sourceExtent: response.sourceExtent,
            loading: false,
            error: null,
          } : current);
        } catch (error) {
          if (isCurrent()) {
            const detail = error instanceof Error ? error.message : String(error);
            setState((current) => current.owner === owner ? { ...current, response: null, responseKey: null, loading: false,
              error: localized(language, `读取网格几何失败：${detail}`, `Failed to read mesh geometry: ${detail}`) } : current);
          }
        } finally {
          if (pendingRequestRef.current === pending && requestGenerationRef.current === generation) {
            pending.active = false;
            pendingRequestRef.current = null;
          }
        }
      })();
    }, 120);

    return () => {
      clearTimeout(timer);
      pending.active = false;
      controller.abort();
      if (pendingRequestRef.current === pending) pendingRequestRef.current = null;
      if (requestGenerationRef.current === generation) requestGenerationRef.current += 1;
    };
  }, [active.viewport, backend, backendRevision, elementCount, elementKind, flowAreaIndex, flowAreaName, language, owner, ready, requestKey, resultPath, revision]);

  const setViewport = (viewport: ResultProfileMeshExtent | null): void => {
    setState((current) => {
      if (current.owner !== owner) return current;
      if (current.viewport === null && viewport === null) return current;
      if (current.viewport && viewport && sameExtent(current.viewport, viewport)) return current;
      return { ...current, viewport, response: null, responseKey: null, loading: false, error: null };
    });
  };

  const zoom = (factor: number, anchor?: { x: number; y: number }): void => {
    if (!drawExtent) return;
    const center = anchor ?? { x: drawExtent.minX + (drawExtent.maxX - drawExtent.minX) / 2, y: drawExtent.minY + (drawExtent.maxY - drawExtent.minY) / 2 };
    const next = zoomAt(drawExtent, factor, center);
    if (next) setViewport(next);
  };

  const handleWheel = (event: WheelEvent<SVGSVGElement>): void => {
    if (!drawExtent) return;
    event.preventDefault();
    const anchor = worldPoint(event.currentTarget, event.clientX, event.clientY, drawExtent);
    zoom(event.deltaY < 0 ? 0.78 : 1 / 0.78, anchor ?? undefined);
  };

  const handlePointerDown = (event: ReactPointerEvent<SVGSVGElement>): void => {
    if (!drawExtent || event.button !== 0) return;
    const startWorld = worldPoint(event.currentTarget, event.clientX, event.clientY, drawExtent);
    if (!startWorld) return;
    dragRef.current = {
      pointerID: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      startWorld,
      baseExtent: drawExtent,
      moved: false,
      active: true,
    };
  };

  const handlePointerMove = (event: ReactPointerEvent<SVGSVGElement>): void => {
    const drag = dragRef.current;
    if (!drag || !drag.active || drag.pointerID !== event.pointerId) return;
    if (!drag.moved && Math.hypot(event.clientX - drag.startX, event.clientY - drag.startY) < 4) return;
    if (!drag.moved) {
      drag.moved = true;
      event.currentTarget.setPointerCapture(event.pointerId);
    }
    const currentWorld = worldPoint(event.currentTarget, event.clientX, event.clientY, drag.baseExtent);
    if (!currentWorld) return;
    const next = {
      minX: drag.baseExtent.minX + drag.startWorld.x - currentWorld.x,
      minY: drag.baseExtent.minY + drag.startWorld.y - currentWorld.y,
      maxX: drag.baseExtent.maxX + drag.startWorld.x - currentWorld.x,
      maxY: drag.baseExtent.maxY + drag.startWorld.y - currentWorld.y,
    };
    if (validExtent(next)) setViewport(next);
  };

  const handlePointerUp = (event: ReactPointerEvent<SVGSVGElement>): void => {
    const drag = dragRef.current;
    if (!drag || drag.pointerID !== event.pointerId) return;
    drag.active = false;
    if (drag.moved) {
      suppressPointerClickRef.current = true;
      window.setTimeout(() => { suppressPointerClickRef.current = false; }, 0);
    }
    dragRef.current = null;
    if (event.currentTarget.hasPointerCapture(event.pointerId)) event.currentTarget.releasePointerCapture(event.pointerId);
  };

  const handleElementClick = (elementIndex: number): void => {
    if (suppressPointerClickRef.current) {
      suppressPointerClickRef.current = false;
      return;
    }
    onSelectElement(elementIndex);
  };

  const handleElementKeyDown = (event: KeyboardEvent<SVGGElement>, elementIndex: number): void => {
    if (event.key !== 'Enter' && event.key !== ' ') return;
    event.preventDefault();
    onSelectElement(elementIndex);
  };

  const mapStatus = (): string => {
    if (!ready) return localized(language, '选择已确认的计算结果后读取内嵌网格。', 'Select a verified Result to read its embedded mesh.');
    if (!backend.supports('result.profileMap')) return localized(language, '当前 C# 服务未发布 Result 网格读取能力。', 'The C# service does not advertise Result mesh geometry reading.');
    if (active.loading) return localized(language, '正在读取网格几何…', 'Reading mesh geometry…');
    if (active.error) return active.error;
    if (currentResponse?.requiresZoom || currentResponse?.isComplete === false) {
      return localized(language, '当前范围的网格几何超过单次读取上限。请放大或拖动地图后重试。', 'Mesh geometry for this extent exceeds the per-request limit. Zoom in or pan, then retry.');
    }
    if (!currentResponse) return localized(language, '尚未读取网格几何。', 'Mesh geometry has not been read.');
    if (currentResponse.returnedElements === 0) return localized(language, '当前范围没有可显示的网格元素。', 'No mesh elements are visible in this extent.');
    const noun = currentResponse.elementKind === 'Cell' ? (zh ? '单元' : 'cells') : (zh ? '面' : 'faces');
    return zh
      ? `显示 ${currentResponse.returnedElements.toLocaleString('zh-CN')} 个${noun}，共 ${currentResponse.totalElements.toLocaleString('zh-CN')} 个。点击元素载入时间序列。`
      : `Showing ${currentResponse.returnedElements.toLocaleString('en-US')} ${noun} of ${currentResponse.totalElements.toLocaleString('en-US')}. Select an element to load its time series.`;
  };

  const renderPath = (path: Array<{ x: number; y: number }>): string => {
    if (!drawExtent) return '';
    const transform = projection(drawExtent);
    const points = path.map((point) => ({
      x: transform.offsetX + (point.x - drawExtent.minX) * transform.scale,
      y: VIEW_HEIGHT - transform.offsetY - (point.y - drawExtent.minY) * transform.scale,
    }));
    return points.map((point, index) => `${index === 0 ? 'M' : 'L'}${point.x.toFixed(3)},${point.y.toFixed(3)}`).join(' ')
      + (elementKind === 'Cell' ? ' Z' : '');
  };

  const canInteract = ready && !!drawExtent && !active.loading && !active.error
    && !!currentResponse?.available && currentResponse.isComplete && !currentResponse.requiresZoom;
  const visibleElements = canInteract && currentResponse ? currentResponse.elements : [];
  const kindLabel = elementKind === 'Face' ? localized(language, '面', 'Face') : localized(language, '单元', 'Cell');

  return <section className="results-mesh-picker" aria-labelledby="results-mesh-picker-title" data-testid="result-profile-mesh-picker">
    <div className="results-mesh-picker-heading">
      <div><h3 id="results-mesh-picker-title">{localized(language, '二维网格点选', '2D mesh selection')}</h3>
        <span>{flowAreaName || localized(language, '未选择流区', 'No flow area selected')}{elementKind ? ` · ${kindLabel}` : ''}</span></div>
      <div className="results-mesh-picker-actions">
        <button type="button" className="results-secondary-button" aria-label={localized(language, '放大地图', 'Zoom in')}
          title={localized(language, '缩小显示范围', 'Zoom in')} data-testid="result-profile-mesh-zoom-in"
          disabled={!drawExtent || active.loading} onClick={() => zoom(0.65)}>＋</button>
        <button type="button" className="results-secondary-button" aria-label={localized(language, '缩小地图', 'Zoom out')}
          title={localized(language, '扩大显示范围', 'Zoom out')} data-testid="result-profile-mesh-zoom-out"
          disabled={!drawExtent || active.loading} onClick={() => zoom(1 / 0.65)}>−</button>
        <button type="button" className="results-secondary-button" data-testid="result-profile-mesh-fit"
          disabled={!active.sourceExtent || active.loading} onClick={() => setViewport(null)}>{localized(language, '适合全图', 'Fit')}</button>
      </div>
    </div>
    <p className={'results-mesh-picker-status' + (active.error ? ' is-error' : '')} role={active.error ? 'alert' : 'status'}
      data-testid="result-profile-mesh-status">{mapStatus()}</p>
    <div className="results-mesh-map-frame">
      <svg className={'results-mesh-map' + (canInteract ? ' is-ready' : '')} viewBox={`0 0 ${VIEW_WIDTH} ${VIEW_HEIGHT}`}
        preserveAspectRatio="xMidYMid meet" role="group" aria-label={localized(language, '结果内嵌二维网格', 'Result embedded 2D mesh')}
        data-testid="result-profile-mesh-map" onWheel={handleWheel} onPointerDown={handlePointerDown}
        onPointerMove={handlePointerMove} onPointerUp={handlePointerUp} onPointerCancel={handlePointerUp}>
        {visibleElements.map((element) => {
          const selected = selectedElementIndex === element.elementIndex;
          return <g key={`${elementKind}:${element.elementIndex}`}
            className={'results-mesh-element' + (selected ? ' is-selected' : '')}
            role="button" tabIndex={0} aria-pressed={selected}
            aria-label={`${kindLabel} ${element.elementIndex}`}
            data-element-index={element.elementIndex}
            data-testid={`result-profile-mesh-element-${elementKind?.toLowerCase()}-${element.elementIndex}`}
            onClick={() => handleElementClick(element.elementIndex)}
            onKeyDown={(event) => handleElementKeyDown(event, element.elementIndex)}>
            {element.paths.map((path, pathIndex) => <path key={pathIndex} d={renderPath(path)}
              className={elementKind === 'Cell' ? 'results-mesh-cell' : 'results-mesh-face'}
              vectorEffect="non-scaling-stroke" />)}
          </g>;
        })}
      </svg>
    </div>
  </section>;
}
