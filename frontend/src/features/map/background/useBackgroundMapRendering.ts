import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { message, text } from '../../../shared/workbench';
import type { BackendGateway } from '../../../shared/workbench';
import { rgbaFrameImage } from '../mapImages';
import {
  BackgroundMapRequestGate, backgroundColorVisible, backgroundMapHasProject, backgroundMapHasSource,
  backgroundMapSettings, backgroundMapSourceKey, backgroundMapViewportFromCamera, parseBackgroundMapResult,
  readBackgroundMapProjection,
} from './protocol';
import type {
  BackgroundMapFrame, BackgroundMapRenderingState, BackgroundMapRenderingStatus, BackgroundMapRenderRequest,
  BackgroundMapSourceStatus, UseBackgroundMapRenderingOptions,
} from './types';

interface RenderState {
  backend: BackendGateway | null;
  sourceKey: string;
  viewportKey: string;
  status: BackgroundMapRenderingStatus;
  frame: BackgroundMapFrame | null;
  message: string | null;
  sources: BackgroundMapSourceStatus[];
  projectionResolved: boolean;
}

const EMPTY_STATE: RenderState = { backend: null, sourceKey: '', viewportKey: '', status: 'disabled',
  frame: null, message: null, sources: [], projectionResolved: false };

/** Real Host frames only; source changes hide synchronously, and camera changes cancel/debounce reads. */
export function useBackgroundMapRendering({ backend, backendRevision = 0, context, document, camera, size,
  settings: vmSettings, coordinatesAreProject, language }: UseBackgroundMapRenderingOptions): BackgroundMapRenderingState {
  const settingsRead = useMemo(() => {
    try { return { settings: backgroundMapSettings(vmSettings), error: null }; }
    catch (error) { return { settings: null, error }; }
  }, [vmSettings]);
  const { settings } = settingsRead;
  const projectionRead = useMemo(() => readBackgroundMapProjection(context.projectXml), [context.projectXml]);
  const viewportRead = useMemo(() => {
    try { return { viewport: backgroundMapViewportFromCamera(camera, size), error: null }; }
    catch (error) { return { viewport: null, error }; }
  }, [camera.scale, camera.offsetX, camera.offsetY, size.width, size.height]);
  const { viewport } = viewportRead;
  const sourceKey = backgroundMapSourceKey(context, document, settings, coordinatesAreProject, backendRevision);
  const viewportKey = JSON.stringify([viewport, camera.scale]);
  const rendererConnected = backend.supports('map.background.render');
  const projectionEvaluatorConnected = backend.supports('projection.evaluate');
  const sourceEnabled = Boolean(settings && backgroundMapHasSource(settings));
  const projectAvailable = backgroundMapHasProject(context, document);
  const [rendered, setRendered] = useState<RenderState>(EMPTY_STATE);
  const [refreshEpoch, setRefreshEpoch] = useState(0);
  const gate = useRef(new BackgroundMapRequestGate());
  const controller = useRef<AbortController | null>(null);
  const currentIdentity = useRef({ backend, sourceKey, viewportKey });
  // This changes during render, before an effect cleanup can run, so an old
  // promise cannot publish into the frame between a source switch and commit.
  currentIdentity.current = { backend, sourceKey, viewportKey };
  const projectionCache = useRef<{ backend: BackendGateway; sourceKey: string; valid: boolean } | null>(null);

  const blockedMessage = !settings ? message(settingsRead.error, language)
    : !projectAvailable ? text(language, '请先打开真实工程并选择已保存的地图，才能加载底图。', 'Open a project and select a saved map before loading background maps.')
    : !coordinatesAreProject ? text(language, '当前画布使用导入图层的源坐标，底图仅能叠加到工程坐标画布。', 'The canvas is using source coordinates. Background maps require the project coordinate space.')
    : !projectionRead.projection ? projectionRead.issue === 'local'
      ? text(language, '当前工程为 Local 局部坐标，无法定位在线底图；请先设置有效的工程投影。', 'The project uses Local coordinates. Set a projected project CRS to locate online background maps.')
      : text(language, '工程 XML 中缺少可识别的有效 Projection 定义，无法定位底图。', 'The project XML has no usable Projection definition for background maps.')
    : !rendererConnected ? text(language, '当前 C# 服务未提供底图绘制操作。', 'The C# service does not provide background-map rendering.')
    : !projectionEvaluatorConnected ? text(language, '当前 C# 服务无法核验工程投影，底图暂不可用。', 'The C# service cannot validate the project projection; background maps are unavailable.')
    : !viewport ? message(viewportRead.error, language) : null;

  useEffect(() => {
    const requestController = new AbortController();
    controller.current = requestController;
    const capture = gate.current.begin(backend, sourceKey, viewportKey);
    const current = () => gate.current.accepts(capture, currentIdentity.current, requestController.signal);
    const base = { backend, sourceKey, viewportKey, frame: null, sources: [], message: null, projectionResolved: false };
    if (!sourceEnabled || blockedMessage || !settings || !viewport || !projectionRead.projection || !document) {
      setRendered({ ...base, status: sourceEnabled || settingsRead.error ? 'blocked' : 'disabled',
        message: blockedMessage, projectionResolved: false });
      return () => { gate.current.invalidate(); requestController.abort(); };
    }
    const projection = projectionRead.projection;
    const cachedProjection = projectionCache.current;
    const isProjectionValidated = cachedProjection?.backend === backend && cachedProjection.sourceKey === sourceKey && cachedProjection.valid;
    setRendered(previous => ({ ...base, status: 'loading', projectionResolved: Boolean(isProjectionValidated),
      // Keep a correctly georeferenced image while panning, but never across a
      // source, XML, map revision, capability epoch or connection change.
      frame: previous.backend === backend && previous.sourceKey === sourceKey ? previous.frame : null,
      sources: previous.backend === backend && previous.sourceKey === sourceKey ? previous.sources : [] }));
    const timer = setTimeout(() => {
      void (async () => {
        try {
          if (!current()) return;
          if (!isProjectionValidated) {
            const raw = await backend.request<unknown>('projection.evaluate', { type: projection.type, value: projection.value },
              { signal: requestController.signal });
            if (!current()) return;
            const evaluation = raw as { isValid?: unknown; isProjected?: unknown; error?: unknown } | null;
            if (!evaluation || evaluation.isValid !== true || evaluation.isProjected !== true) {
              const detail = typeof evaluation?.error === 'string' && evaluation.error.trim() ? ` ${evaluation.error}` : '';
              setRendered({ ...base, status: 'blocked', message: text(language,
                '工程 Projection 不是经 C# 核验的有效投影坐标系，不能加载底图。',
                'The project Projection is not a valid projected CRS confirmed by C#; background maps cannot be loaded.') + detail });
              return;
            }
            projectionCache.current = { backend, sourceKey, valid: true };
          }
          if (!current()) return;
          const request: BackgroundMapRenderRequest = { requestID: crypto.randomUUID(), mapID: document.mapID,
            projectSessionID: context.projectSessionID, projectionType: projection.type, projectionValue: projection.value,
            viewport, worldUnitsPerPixel: 1 / camera.scale, settings };
          const raw = await backend.request<unknown>('map.background.render', request, { signal: requestController.signal });
          if (!current()) return;
          const result = parseBackgroundMapResult(raw, request);
          // Satellite follows ImageRenderLayer.Splat (straight RGB+alpha). The
          // relief recolorer produces black RGB, identical for either alpha
          // convention. Do not unpremultiply these Host bytes a second time.
          const rasters = result.rasters.map(({ rgba, ...raster }) => ({ ...raster,
            imageSrc: rgbaFrameImage(rgba, raster.width, raster.height) }));
          const hasContent = rasters.length > 0 || backgroundColorVisible(result.osmBackgroundColor) ||
            result.features.some(feature => backgroundColorVisible(feature.fillColor) && feature.fillOpacity > 0 ||
              backgroundColorVisible(feature.strokeColor) && feature.strokeOpacity > 0 && feature.strokeWidth > 0) ||
            result.labels.some(label => backgroundColorVisible(label.textColor) || backgroundColorVisible(label.haloColor) && label.haloWidth > 0);
          const enabledSources = result.sources.filter(source => source.state !== 'disabled');
          const failed = enabledSources.filter(source => source.state !== 'available');
          const status: BackgroundMapRenderingStatus = hasContent ? failed.length ? 'partial' : 'ready'
            : failed.some(source => source.state === 'failed') ? 'error' : 'blocked';
          const statusMessage = hasContent ? failed.length ? text(language,
            '部分底图来源未能绘制，请查看来源状态。', 'Some background sources could not be rendered. See the source status.') : null
            : enabledSources.every(source => source.state === 'outsideCoverage') ? text(language,
              '当前地图范围超出已启用底图来源的覆盖范围。', 'The current viewport is outside the enabled background sources’ coverage.')
              : failed.some(source => source.state === 'failed') ? text(language,
                '底图来源加载失败，当前没有可显示的底图。', 'Background sources failed to load; no background is displayed.')
                : text(language, '服务已返回，但当前范围内没有可绘制的底图内容。', 'The service returned no drawable background content for this viewport.');
          if (current()) setRendered({ backend, sourceKey, viewportKey, status, message: statusMessage,
            frame: hasContent ? { ...result, rasters, sourceKey } : null, sources: result.sources, projectionResolved: true });
        } catch (error) {
          if (current()) setRendered({ ...base, status: 'error', message: message(error, language),
            projectionResolved: projectionCache.current?.backend === backend && projectionCache.current.sourceKey === sourceKey });
        }
      })();
    }, 150);
    return () => { clearTimeout(timer); gate.current.invalidate(); requestController.abort(); };
  // Scalar identities deliberately own the lifecycle. Unrelated tree selection
  // and freshly allocated props must not restart background requests.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [backend, sourceKey, viewportKey, rendererConnected, projectionEvaluatorConnected, sourceEnabled,
    blockedMessage, refreshEpoch, language]);

  const refresh = useCallback(() => setRefreshEpoch(value => value + 1), []);
  const cancel = useCallback(() => {
    gate.current.invalidate();
    controller.current?.abort();
    const identity = currentIdentity.current;
    setRendered({ ...EMPTY_STATE, ...identity, status: 'cancelled', message: text(language,
      '已取消当前底图请求；移动地图或重新加载会发起新请求。',
      'The current background request was cancelled. Moving the map or reloading starts a new request.') });
  }, [language]);
  const belongsToSource = rendered.backend === backend && rendered.sourceKey === sourceKey;
  const belongsToViewport = belongsToSource && rendered.viewportKey === viewportKey;
  const immediatelyBlocked = sourceEnabled && blockedMessage !== null;
  const visible = belongsToSource && sourceEnabled && !blockedMessage;
  return { frame: visible ? rendered.frame : null,
    status: !sourceEnabled && !settingsRead.error ? 'disabled' : immediatelyBlocked || settingsRead.error ? 'blocked'
      : belongsToViewport ? rendered.status : 'loading',
    message: immediatelyBlocked || settingsRead.error ? blockedMessage : belongsToViewport ? rendered.message : null,
    sources: visible ? rendered.sources : [],
    projectionResolved: Boolean(projectionRead.projection && visible && rendered.projectionResolved),
    rendererConnected, refresh, cancel };
}
