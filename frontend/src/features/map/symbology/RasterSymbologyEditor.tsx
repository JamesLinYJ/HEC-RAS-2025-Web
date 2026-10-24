import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { BackendGateway, Language, WorkbenchContext } from '../../../shared/workbench';
import { errorDiagnostics, message } from '../../../shared/workbench';
import { ColorInfosEditor } from './ColorInfosEditor';
import { ColorPaletteList } from './ColorPaletteList';
import { ColorPalettePicker } from './ColorPalettePicker';
import { InterpolatedSurfaceFillEditor } from './InterpolatedSurfaceFillEditor';
import { RasterSymbologySettingsGroup } from './RasterSymbologySettingsGroup';
import {
  advanceRasterSymbologyIdentity,
  assertRasterSymbologyOwner,
  cloneRasterSymbologyDraft,
  isValidRasterSymbologyDraft,
  makeRasterSymbologyRequest,
  parseRasterMapSourceIdentity,
  parseRasterSymbologyApply,
  parseRasterSymbologyDraft,
  parseRasterSymbologyInspect,
  parseRasterSymbologyPalettes,
  parseRasterSymbologySnapshot,
  parseRasterHistogram,
  rasterSymbologyContextKey,
  rasterSymbologyMethods,
  rasterSymbologyOwnerKey,
  validateRasterViewport,
} from './rasterSymbologyWorkflow';
import type {
  RasterMapSourceIdentity,
  RasterMapViewExtent,
  RasterSymbologyDraft,
  RasterSymbologyPalette,
  RasterSymbologySnapshot,
} from './rasterSymbologyWorkflow';
import './symbology.css';

export interface RasterSymbologyEditorProps {
  backend: BackendGateway;
  context: WorkbenchContext;
  language: Language;
  /** The exact Host RasterMapSourceIdentity from the active validated raster layer. */
  source: Record<string, unknown>;
  /** The actual result viewport; omitted when the parent cannot provide one. */
  viewport?: RasterMapViewExtent;
  onVisualizationCommitted: (previousRevision: string, nextRevision: string) => void;
  onAppliedLegendChange?: (legend: AppliedRasterLegend | null) => void;
  onDirtyStateChange?: (owner: string, dirty: boolean) => void;
  backendRevision?: number;
}

export interface AppliedRasterLegend {
  source: RasterMapSourceIdentity;
  draft: RasterSymbologyDraft;
  values: number[] | null;
}

type Phase = 'loading' | 'ready' | 'failed';
interface ActiveRequest {
  id: number;
  ownerKey: string;
  contextKey: string;
  sourceVisualizationRevision: string;
  propVisualizationRevision: string;
  controller: AbortController;
}
interface RequestEnvelope<T> { value: T; request: ActiveRequest; source: RasterMapSourceIdentity }
interface EditorError { error: unknown }

export function RasterSymbologyEditor({ backend, context, language, source, viewport, onVisualizationCommitted,
  onAppliedLegendChange, onDirtyStateChange, backendRevision }: RasterSymbologyEditorProps) {
  const zh = language === 'zh';
  const sourceParse = useMemo(() => {
    try {
      const identity = parseRasterMapSourceIdentity(source);
      if (identity.mapLayerType !== 'SingleBandRasterMapLayer') {
        throw new Error('连续栅格符号化仅支持 SingleBandRasterMapLayer。');
      }
      return { identity, error: null as unknown };
    } catch (error) {
      return { identity: null, error };
    }
  }, [source]);
  const identity = sourceParse.identity;
  const ownerKey = identity ? rasterSymbologyOwnerKey(identity) : '';
  const contextKey = rasterSymbologyContextKey(context);
  const viewportValue = validateRasterViewport(viewport);
  const dirtyOwner = identity ? `map.raster-symbology:${identity.id}` : 'map.raster-symbology';

  const [phase, setPhase] = useState<Phase>('loading');
  const [busy, setBusy] = useState(false);
  const [snapshot, setSnapshot] = useState<RasterSymbologySnapshot | null>(null);
  const [baseline, setBaseline] = useState<RasterSymbologySnapshot | null>(null);
  const [editorError, setEditorError] = useState<EditorError | null>(null);
  const [success, setSuccess] = useState('');
  const [paletteName, setPaletteName] = useState('');
  const [deletePaletteID, setDeletePaletteID] = useState<string | null>(null);
  const [requestLabel, setRequestLabel] = useState('');

  const identityRef = useRef<RasterMapSourceIdentity | null>(null);
  const ownerKeyRef = useRef('');
  const contextKeyRef = useRef('');
  const appliedLegendRef = useRef<AppliedRasterLegend | null>(null);
  const appliedLegendCallbackRef = useRef(onAppliedLegendChange);
  appliedLegendCallbackRef.current = onAppliedLegendChange;
  const activeRequestRef = useRef<ActiveRequest | null>(null);
  const requestGenerationRef = useRef(0);
  const mountedRef = useRef(false);
  const propsRef = useRef({ ownerKey, contextKey, visualizationRevision: identity?.visualizationRevision ?? '' });
  const pendingLocalCommitRef = useRef<{ ownerKey: string; parentRevisionAtCommit: string; previousRevision: string; nextRevision: string } | null>(null);
  propsRef.current = { ownerKey, contextKey, visualizationRevision: identity?.visualizationRevision ?? '' };

  const publishAppliedLegend = useCallback((legend: AppliedRasterLegend | null) => {
    appliedLegendRef.current = legend ? cloneAppliedRasterLegend(legend) : null;
    appliedLegendCallbackRef.current?.(legend ? cloneAppliedRasterLegend(legend) : null);
  }, []);
  const preserveAppliedLegendForCurrentSource = useCallback(() => {
    const legend = appliedLegendRef.current;
    const currentSource = identityRef.current;
    if (!legend || !currentSource) return;
    publishAppliedLegend({ ...legend, source: cloneRasterMapSourceIdentity(currentSource) });
  }, [publishAppliedLegend]);

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
      requestGenerationRef.current++;
      activeRequestRef.current?.controller.abort();
      activeRequestRef.current = null;
      publishAppliedLegend(null);
    };
  }, [publishAppliedLegend]);

  const requestRevisionStillOwned = useCallback((request: ActiveRequest): boolean => {
    const currentPropsRevision = propsRef.current.visualizationRevision;
    if (sameRevision(currentPropsRevision, request.sourceVisualizationRevision)) return true;
    const pending = pendingLocalCommitRef.current;
    return pending?.ownerKey === request.ownerKey
      && sameRevision(pending.nextRevision, request.sourceVisualizationRevision)
      && (sameRevision(pending.parentRevisionAtCommit, currentPropsRevision)
        || sameRevision(pending.previousRevision, currentPropsRevision))
      && (sameRevision(request.propVisualizationRevision, currentPropsRevision)
        || sameRevision(request.propVisualizationRevision, pending.parentRevisionAtCommit)
        || sameRevision(request.propVisualizationRevision, pending.previousRevision));
  }, []);

  const isCurrentRequest = useCallback((request: ActiveRequest): boolean => mountedRef.current
    && activeRequestRef.current === request
    && request.id === requestGenerationRef.current
    && propsRef.current.ownerKey === request.ownerKey
    && propsRef.current.contextKey === request.contextKey
    && requestRevisionStillOwned(request), [requestRevisionStillOwned]);

  const isResponseCurrent = useCallback((envelope: RequestEnvelope<unknown>): boolean => mountedRef.current
    && envelope.request.id === requestGenerationRef.current
    && propsRef.current.ownerKey === envelope.request.ownerKey
    && propsRef.current.contextKey === envelope.request.contextKey
    && requestRevisionStillOwned(envelope.request)
    && ownerKeyRef.current === envelope.request.ownerKey
    && contextKeyRef.current === envelope.request.contextKey
    && identityRef.current !== null
    && rasterSymbologyOwnerKey(identityRef.current) === envelope.request.ownerKey
    && identityRef.current.visualizationRevision === envelope.source.visualizationRevision
    && identityRef.current.sourceRevision === envelope.source.sourceRevision, [requestRevisionStillOwned]);

  const performRequest = useCallback(async <T,>(method: string, parameters: unknown,
    parse: (value: unknown) => T, label: string): Promise<RequestEnvelope<T> | undefined> => {
    const requestSource = identityRef.current;
    const requestOwnerKey = ownerKeyRef.current;
    const requestContextKey = contextKeyRef.current;
    if (!requestSource || !requestOwnerKey || !requestContextKey
      || requestOwnerKey !== propsRef.current.ownerKey || requestContextKey !== propsRef.current.contextKey) {
      return undefined;
    }
    const requestPropVisualizationRevision = propsRef.current.visualizationRevision;
    const pendingCommit = pendingLocalCommitRef.current;
    const propOwnsRequestSource = sameRevision(requestPropVisualizationRevision, requestSource.visualizationRevision)
      || pendingCommit?.ownerKey === requestOwnerKey
        && (sameRevision(pendingCommit.parentRevisionAtCommit, requestPropVisualizationRevision)
          || sameRevision(pendingCommit.previousRevision, requestPropVisualizationRevision))
        && sameRevision(pendingCommit.nextRevision, requestSource.visualizationRevision);
    if (!propOwnsRequestSource) return undefined;
    if (!backend.supports(method)) {
      setEditorError({ error: new Error(`C# Host 尚未发布 ${method}。`) });
      return undefined;
    }

    activeRequestRef.current?.controller.abort();
    const request: ActiveRequest = {
      id: ++requestGenerationRef.current,
      ownerKey: requestOwnerKey,
      contextKey: requestContextKey,
      sourceVisualizationRevision: requestSource.visualizationRevision,
      propVisualizationRevision: requestPropVisualizationRevision,
      controller: new AbortController(),
    };
    activeRequestRef.current = request;
    const writeOwner = [rasterSymbologyMethods.apply, rasterSymbologyMethods.savePalette, rasterSymbologyMethods.deletePalette]
      .some(writeMethod => writeMethod === method) ? `map.raster-write:${contextKey}:${ownerKey}:${request.id}` : null;
    if (writeOwner) onDirtyStateChange?.(writeOwner, true);
    setBusy(true);
    setRequestLabel(label);
    setEditorError(null);
    setSuccess('');
    try {
      const result = await backend.request(method, parameters, { signal: request.controller.signal });
      if (!isCurrentRequest(request)) return undefined;
      return { value: parse(result), request, source: requestSource };
    } catch (error) {
      if (isCurrentRequest(request)) setEditorError({ error });
      return undefined;
    } finally {
      if (writeOwner) onDirtyStateChange?.(writeOwner, false);
      if (isCurrentRequest(request)) {
        activeRequestRef.current = null;
        setBusy(false);
        setRequestLabel('');
      }
    }
  }, [backend, isCurrentRequest, contextKey, ownerKey, onDirtyStateChange]);

  const contextCanInspect = Boolean(context.projectPath && context.projectXml && context.projectSessionID);
  const sourceRevisionKey = identity ? `${ownerKey}:${identity.visualizationRevision}:${identity.sourceRevision}` : '';

  useEffect(() => {
    const nextIdentity = sourceParse.identity;
    const nextOwnerKey = nextIdentity ? rasterSymbologyOwnerKey(nextIdentity) : '';
    const nextContextKey = rasterSymbologyContextKey(context);
    const currentSource = identityRef.current;
    const sameOwner = nextOwnerKey !== '' && nextOwnerKey === ownerKeyRef.current
      && nextContextKey === contextKeyRef.current;
    const pending = pendingLocalCommitRef.current;
    if (sameOwner && pending?.ownerKey === nextOwnerKey
      && currentSource?.visualizationRevision && sameRevision(currentSource.visualizationRevision, pending.nextRevision)
      && [pending.parentRevisionAtCommit, pending.previousRevision, pending.nextRevision]
        .some(revision => sameRevision(nextIdentity?.visualizationRevision ?? '', revision))) {
      if (sameRevision(nextIdentity?.visualizationRevision ?? '', pending.nextRevision)) pendingLocalCommitRef.current = null;
      return;
    }
    // The parent can echo the revision just acknowledged by this editor. Keep the
    // accepted local source in that case rather than discarding the working draft.
    if (sameOwner && currentSource?.visualizationRevision && nextIdentity?.visualizationRevision
      && sameRevision(currentSource.visualizationRevision, nextIdentity.visualizationRevision)
      && currentSource?.sourceRevision === nextIdentity?.sourceRevision) return;

    publishAppliedLegend(null);
    requestGenerationRef.current++;
    activeRequestRef.current?.controller.abort();
    activeRequestRef.current = null;
    identityRef.current = nextIdentity;
    ownerKeyRef.current = nextOwnerKey;
    contextKeyRef.current = nextContextKey;
    setSnapshot(null);
    setBaseline(null);
    setBusy(false);
    setRequestLabel('');
    setSuccess('');
    setEditorError(null);
    setDeletePaletteID(null);
    setPaletteName('');

    if (!nextIdentity || sourceParse.error || !contextCanInspect || !context.projectPath
      || nextIdentity.projectPath !== context.projectPath) {
      setPhase('failed');
      setEditorError({ error: sourceParse.error ?? new Error(zh
        ? '当前工程上下文与栅格图层身份不完整或不匹配。'
        : 'The active project context does not match the raster source identity.') });
      return;
    }
    setPhase('loading');
    const parameters = makeRasterSymbologyRequest(nextIdentity, context);
    void performRequest(rasterSymbologyMethods.inspect, parameters, parseRasterSymbologyInspect,
      zh ? '读取栅格符号' : 'Reading raster symbology').then(result => {
      if (!result) {
        if (identityRef.current && rasterSymbologyOwnerKey(identityRef.current) === nextOwnerKey
          && identityRef.current.visualizationRevision === nextIdentity.visualizationRevision
          && contextKeyRef.current === nextContextKey && !activeRequestRef.current) setPhase('failed');
        return;
      }
      if (!isResponseCurrent(result)) return;
      try {
        assertRasterSymbologyOwner(result.value.previousVisualizationRevision, result.value.sourceRevision,
          nextIdentity, 'inspect');
        if (result.value.visualizationRevision.toUpperCase() !== nextIdentity.visualizationRevision.toUpperCase()
          || result.value.raster.id !== nextIdentity.id
          || result.value.raster.sourceMapID !== nextIdentity.sourceMapID) {
          throw new Error('Host 的读取结果与当前栅格图层或可视化修订不匹配。');
        }
        setSnapshot(result.value.snapshot);
        setBaseline(result.value.snapshot);
        publishAppliedLegend(makeAppliedRasterLegend(nextIdentity, result.value.snapshot));
        setPhase('ready');
      } catch (error) {
        setEditorError({ error });
        setPhase('failed');
      }
    });
  }, [sourceRevisionKey, contextCanInspect, context.projectPath, context.projectXml, context.projectSessionID,
    context.projectRevision, context.selectedAsset, sourceParse, context, performRequest, isResponseCurrent, zh, backendRevision, publishAppliedLegend]);

  const dirty = Boolean(snapshot && baseline && snapshotStateKey(snapshot) !== snapshotStateKey(baseline));
  useEffect(() => {
    onDirtyStateChange?.(dirtyOwner, dirty);
    return () => onDirtyStateChange?.(dirtyOwner, false);
  }, [dirty, dirtyOwner, onDirtyStateChange]);

  const reportActionError = (error: unknown) => {
    if (ownerKeyRef.current === ownerKey && contextKeyRef.current === contextKey) setEditorError({ error });
  };

  const updateSnapshot = (update: (current: RasterSymbologySnapshot) => RasterSymbologySnapshot) => {
    setSnapshot(current => current ? update(current) : current);
    setSuccess('');
  };

  const onDraftChange = (patch: Partial<RasterSymbologyDraft>) => {
    updateSnapshot(current => {
      const draft = { ...current.draft, ...patch };
      if (patch.valueSource === 'Linear' && draft.stops.length > 1) {
        const stops = draft.stops.map(stop => ({ ...stop }));
        const first = stops[0].value;
        const last = stops[stops.length - 1].value;
        for (let index = 1; index < stops.length - 1; index++) {
          stops[index] = { ...stops[index], value: first + (last - first) * index / (stops.length - 1) };
        }
        draft.stops = stops;
      }
      return { ...current, draft };
    });
  };

  const onPlotOptionChange = (propertyName: string, value: string) => {
    updateSnapshot(current => ({
      ...current,
      plotOptions: current.plotOptions.map(option => option.propertyName === propertyName
        ? { ...option, value }
        : option),
    }));
  };

  const onStopChange = (id: string, patch: { color?: string; value?: number }) => {
    updateSnapshot(current => {
      const stops = current.draft.stops.map(stop => stop.id === id ? { ...stop, ...patch } : stop);
      if (patch.value !== undefined && current.draft.valueSource === 'Linear') {
        const first = stops[0]?.value;
        const last = stops[stops.length - 1]?.value;
        if (Number.isFinite(first) && Number.isFinite(last) && stops.length > 1) {
          for (let index = 1; index < stops.length - 1; index++) {
            stops[index] = { ...stops[index], value: first + (last - first) * index / (stops.length - 1) };
          }
        }
      }
      return { ...current, draft: { ...current.draft, isEditing: true, stops } };
    });
  };

  const onAddBelow = (id: string) => {
    updateSnapshot(current => {
      if (current.draft.stops.length >= 1024) return current;
      const index = current.draft.stops.findIndex(stop => stop.id === id);
      if (index < 0) return current;
      const stops = current.draft.stops.map(stop => ({ ...stop }));
      const left = stops[index];
      const right = stops[index + 1];
      let value: number;
      if (right) value = left.value + (right.value - left.value) / 2;
      else value = left.value + Math.max(1, Math.abs(left.value) * 0.01);
      const added = { id: createGuid(), color: left.color, value };
      stops.splice(index + 1, 0, added);
      if (current.draft.valueSource === 'Linear' && stops.length > 1) {
        const first = stops[0].value;
        const last = stops[stops.length - 1].value;
        for (let row = 1; row < stops.length - 1; row++) {
          stops[row] = { ...stops[row], value: first + (last - first) * row / (stops.length - 1) };
        }
      }
      return { ...current, draft: { ...current.draft, isEditing: true, stops } };
    });
  };

  const onDeleteStop = (id: string) => {
    updateSnapshot(current => {
      if (current.draft.stops.length <= 2) return current;
      const stops = current.draft.stops.filter(stop => stop.id !== id);
      if (stops.length === current.draft.stops.length) return current;
      if (current.draft.valueSource === 'Linear' && stops.length > 1) {
        const first = stops[0].value;
        const last = stops[stops.length - 1].value;
        for (let row = 1; row < stops.length - 1; row++) {
          stops[row] = { ...stops[row], value: first + (last - first) * row / (stops.length - 1) };
        }
      }
      return { ...current, draft: { ...current.draft, isEditing: true, stops } };
    });
  };

  const requestCurrentSource = () => {
    const currentSource = identityRef.current;
    if (!currentSource || rasterSymbologyOwnerKey(currentSource) !== ownerKeyRef.current) {
      throw new Error('当前栅格图层身份已更改，请重新打开符号编辑器。');
    }
    return currentSource;
  };

  const onSelectPalette = async (paletteID: string) => {
    if (!snapshot) return;
    let currentSource: RasterMapSourceIdentity;
    try { currentSource = requestCurrentSource(); } catch (error) { reportActionError(error); return; }
    const draft = { ...cloneRasterSymbologyDraft(snapshot.draft), isEditing: true };
    const response = await performRequest(rasterSymbologyMethods.selectPalette, {
      ...makeRasterSymbologyRequest(currentSource, context), paletteID, draft,
    }, parseRasterSymbologyDraft, zh ? '选择颜色带' : 'Selecting palette');
    if (!response || !isResponseCurrent(response)) return;
    try {
      if (!isValidRasterSymbologyDraft(response.value.draft)) throw new Error('Host 返回的颜色带断点无法用于当前符号草稿。');
      updateSnapshot(current => ({ ...current, draft: response.value.draft }));
    } catch (error) { reportActionError(error); }
  };

  const onUpdateHistogram = async () => {
    if (!snapshot) return;
    const currentSource = requestCurrentSource();
    const forView = snapshot.draft.updateHistogramWithView;
    if (forView && !viewportValue) {
      reportActionError(new Error(zh ? '没有来自父级的有效视口，无法计算当前视图直方图。' : 'A current viewport was not provided.'));
      return;
    }
    const response = await performRequest(rasterSymbologyMethods.histogram, {
      ...makeRasterSymbologyRequest(currentSource, context),
      viewport: forView ? viewportValue : null,
      count: snapshot.draft.stops.length,
      forView,
    }, parseRasterHistogram, zh ? '计算栅格直方图' : 'Computing raster histogram');
    if (!response || !isResponseCurrent(response)) return;
    try {
      const values = response.value;
      if (values.length !== snapshot.draft.stops.length) throw new Error('Host 返回的直方图数量与当前颜色断点数量不一致。');
      updateSnapshot(current => ({ ...current, draft: {
        ...current.draft,
        isEditing: true,
        stops: current.draft.stops.map((stop, index) => ({ ...stop, value: values[index] })),
      } }));
    } catch (error) { reportActionError(error); }
  };

  const onReset = async () => {
    const currentSource = requestCurrentSource();
    const response = await performRequest(rasterSymbologyMethods.reset,
      makeRasterSymbologyRequest(currentSource, context), parseRasterSymbologySnapshot,
      zh ? '读取 Host 默认符号' : 'Reading Host defaults');
    if (!response || !isResponseCurrent(response)) return;
    const result = response.value;
    try {
      assertRasterSymbologyOwner(result.previousVisualizationRevision, result.sourceRevision, currentSource, 'reset');
      if (result.visualizationRevision.toUpperCase() !== currentSource.visualizationRevision.toUpperCase()) {
        throw new Error('Host 的默认符号快照与当前可视化修订不匹配。');
      }
      updateSnapshot(() => result.snapshot);
    } catch (error) { reportActionError(error); }
  };

  const onApply = async () => {
    if (!snapshot || !isValidRasterSymbologyDraft(snapshot.draft)) return;
    const currentSource = requestCurrentSource();
    const submittedSnapshot = cloneSnapshot(snapshot);
    const response = await performRequest(rasterSymbologyMethods.apply, {
      ...makeRasterSymbologyRequest(currentSource, context),
      draft: cloneRasterSymbologyDraft(snapshot.draft),
      plotOptions: Object.fromEntries(snapshot.plotOptions.map(option => [option.propertyName, option.value])),
    }, parseRasterSymbologyApply, zh ? '应用栅格符号' : 'Applying raster symbology');
    if (!response || !isResponseCurrent(response)) return;
    const result = response.value;
    try {
      assertRasterRasterPayloadOwner(result.raster, currentSource);
      identityRef.current = advanceRasterSymbologyIdentity(currentSource, result, 'apply');
      pendingLocalCommitRef.current = { ownerKey, parentRevisionAtCommit: propsRef.current.visualizationRevision,
        previousRevision: result.previousVisualizationRevision, nextRevision: result.visualizationRevision };
      setSnapshot(submittedSnapshot);
      setBaseline(submittedSnapshot);
      setSuccess(zh ? `符号已写入 .vis（${result.visualizationRevision}）` : `Symbology saved to .vis (${result.visualizationRevision})`);
      try { onVisualizationCommitted(result.previousVisualizationRevision, result.visualizationRevision); }
      catch (error) { setEditorError({ error }); }
      const appliedSource = identityRef.current;
      if (appliedSource) {
        const refreshed = await performRequest(rasterSymbologyMethods.inspect,
          makeRasterSymbologyRequest(appliedSource, context), parseRasterSymbologyInspect,
          zh ? '确认已应用图例' : 'Confirming applied legend');
        if (!refreshed || !isResponseCurrent(refreshed)) {
          if (identityRef.current?.visualizationRevision === appliedSource.visualizationRevision
            && ownerKeyRef.current === ownerKey && contextKeyRef.current === contextKey) {
            publishAppliedLegend(null);
          }
          return;
        }
        try {
          assertRasterSymbologyOwner(refreshed.value.previousVisualizationRevision, refreshed.value.sourceRevision,
            appliedSource, 'inspect');
          if (!sameRevision(refreshed.value.visualizationRevision, appliedSource.visualizationRevision)
            || refreshed.value.raster.id !== appliedSource.id
            || refreshed.value.raster.sourceMapID !== appliedSource.sourceMapID) {
            throw new Error('Host 的已应用图例与当前栅格来源或可视化修订不匹配。');
          }
          publishAppliedLegend(makeAppliedRasterLegend(appliedSource, refreshed.value.snapshot));
        } catch (error) {
          publishAppliedLegend(null);
          reportActionError(error);
        }
      }
    } catch (error) { reportActionError(error); }
  };

  const onSavePalette = async () => {
    if (!snapshot || !isValidRasterSymbologyDraft(snapshot.draft)) return;
    const name = paletteName.trim();
    if (!isValidPaletteName(name)) return;
    const currentSource = requestCurrentSource();
    const response = await performRequest(rasterSymbologyMethods.savePalette, {
      ...makeRasterSymbologyRequest(currentSource, context),
      draft: cloneRasterSymbologyDraft(snapshot.draft),
      name,
    }, parseRasterSymbologyPalettes, zh ? '保存自定义颜色带' : 'Saving custom palette');
    if (!response || !isResponseCurrent(response)) return;
    const result = response.value;
    try {
      const saved = result.palettes.find(palette => palette.isCustom && palette.name.toLocaleLowerCase() === name.toLocaleLowerCase());
      if (!saved) throw new Error('Host 保存回执中没有找到所请求的自定义颜色带。');
      identityRef.current = advanceRasterSymbologyIdentity(currentSource, result, 'savePalette');
      pendingLocalCommitRef.current = { ownerKey, parentRevisionAtCommit: propsRef.current.visualizationRevision,
        previousRevision: result.previousVisualizationRevision, nextRevision: result.visualizationRevision };
      preserveAppliedLegendForCurrentSource();
      setSnapshot(current => current ? ({ ...current, palettes: result.palettes,
        draft: { ...current.draft, paletteID: saved.id, name: saved.name } }) : current);
      setPaletteName('');
      setSuccess(zh ? `自定义颜色带“${saved.name}”已保存。应用符号仍需单独提交。` : `Custom palette “${saved.name}” was saved. Apply the symbology separately.`);
      try { onVisualizationCommitted(result.previousVisualizationRevision, result.visualizationRevision); }
      catch (error) { setEditorError({ error }); }
    } catch (error) { reportActionError(error); }
  };

  const onDeletePalette = async (palette: RasterSymbologyPalette) => {
    if (deletePaletteID !== palette.id) { setDeletePaletteID(palette.id); return; }
    if (!palette.isCustom) return;
    const currentSource = requestCurrentSource();
    const response = await performRequest(rasterSymbologyMethods.deletePalette, {
      ...makeRasterSymbologyRequest(currentSource, context), paletteID: palette.id,
    }, parseRasterSymbologyPalettes, zh ? '删除自定义颜色带' : 'Deleting custom palette');
    if (!response || !isResponseCurrent(response)) return;
    const result = response.value;
    try {
      identityRef.current = advanceRasterSymbologyIdentity(currentSource, result, 'deletePalette');
      pendingLocalCommitRef.current = { ownerKey, parentRevisionAtCommit: propsRef.current.visualizationRevision,
        previousRevision: result.previousVisualizationRevision, nextRevision: result.visualizationRevision };
      preserveAppliedLegendForCurrentSource();
      setSnapshot(current => current ? ({ ...current, palettes: result.palettes,
        draft: current.draft.paletteID === palette.id ? { ...current.draft, paletteID: null } : current.draft }) : current);
      setDeletePaletteID(null);
      setSuccess(zh ? `自定义颜色带“${palette.name}”已删除。` : `Custom palette “${palette.name}” was deleted.`);
      try { onVisualizationCommitted(result.previousVisualizationRevision, result.visualizationRevision); }
      catch (error) { setEditorError({ error }); }
    } catch (error) { reportActionError(error); }
  };

  const draftValid = Boolean(snapshot && isValidRasterSymbologyDraft(snapshot.draft));
  const nameValid = isValidPaletteName(paletteName.trim());
  const diagnosticText = editorError ? formatDiagnostics(errorDiagnostics(editorError.error)) : '';

  return (
    <section className="raster-symbology-editor" aria-label={zh ? '栅格符号' : 'Raster symbology'}>
      <header className="raster-symbology-header">
        <div>
          <p className="raster-symbology-eyebrow">{zh ? '栅格图层' : 'Raster layer'}</p>
          <h3>{identity?.nodeName || (zh ? '栅格符号设置' : 'Raster symbology')}</h3>
          {identity && <p className="raster-symbology-source">{identity.filenameRelative || identity.filename || identity.sourcePath}</p>}
        </div>
        <div className="raster-symbology-header-status" role="status" aria-live="polite">
          {phase === 'loading' ? (zh ? '读取中…' : 'Loading…') : phase === 'ready' ? (dirty ? (zh ? '有未应用的更改' : 'Unapplied changes') : (zh ? '已同步' : 'Synchronized')) : (zh ? '不可用' : 'Unavailable')}
        </div>
      </header>

      {phase === 'loading' && <div className="raster-symbology-loading" role="status">{requestLabel || (zh ? '正在读取 Host 栅格符号…' : 'Reading raster symbology from the Host…')}</div>}
      {phase === 'failed' && !editorError && <p className="raster-symbology-error">{zh ? '无法读取此栅格图层。' : 'This raster layer could not be read.'}</p>}
      {editorError && <div className="raster-symbology-error-panel" role="alert">
        <p>{message(editorError.error, language)}</p>
        <details><summary>{zh ? 'Host 诊断' : 'Host diagnostics'}</summary><pre>{diagnosticText}</pre></details>
      </div>}
      {success && <p className="raster-symbology-success" role="status">{success}</p>}

      {phase === 'ready' && snapshot && <>
        {busy && <div className="raster-symbology-busy" role="status">{requestLabel || (zh ? '正在等待 Host…' : 'Waiting for the Host…')}</div>}
        <ColorPalettePicker language={language} palettes={snapshot.palettes} selectedId={snapshot.draft.paletteID}
          disabled={busy} onSelect={onSelectPalette} />
        <InterpolatedSurfaceFillEditor language={language} name={snapshot.draft.name} stops={snapshot.draft.stops}
          grayscale={snapshot.draft.grayscale} />
        <RasterSymbologySettingsGroup language={language} draft={snapshot.draft} plotOptions={snapshot.plotOptions}
          disabled={busy} onDraftChange={onDraftChange} onPlotOptionChange={onPlotOptionChange} onReset={onReset} />
        <ColorInfosEditor language={language} draft={snapshot.draft} viewport={viewportValue}
          disabled={busy || !snapshot.draft.isEditing} onValueSourceChange={value => onDraftChange({ valueSource: value })}
          onStopChange={onStopChange} onAddBelow={onAddBelow} onDelete={onDeleteStop}
          onUpdateHistogram={onUpdateHistogram} onViewHistogramChange={enabled => onDraftChange({ updateHistogramWithView: enabled })} />
        <section className="raster-symbology-palettes">
          <div className="raster-symbology-section-title"><h4>{zh ? '颜色带库' : 'Palette library'}</h4><span>{snapshot.palettes.length}</span></div>
          <ColorPaletteList language={language} palettes={snapshot.palettes} selectedId={snapshot.draft.paletteID}
            disabled={busy} deletingId={deletePaletteID} onSelect={onSelectPalette} onDelete={onDeletePalette}
            onCancelDelete={() => setDeletePaletteID(null)} />
          <div className="raster-symbology-save-palette">
            <label className="raster-symbology-field">
              <span>{zh ? '保存为自定义颜色带' : 'Save as a custom palette'}</span>
              <input value={paletteName} maxLength={128} onChange={event => setPaletteName(event.target.value)} disabled={busy}
                placeholder={zh ? '颜色带名称' : 'Palette name'} />
            </label>
            <button type="button" className="raster-symbology-button" onClick={onSavePalette}
              disabled={busy || !draftValid || !nameValid}>
              {zh ? '保存颜色带' : 'Save palette'}
            </button>
            {snapshot.palettes.some(palette => palette.isCustom && palette.name.toLocaleLowerCase() === paletteName.trim().toLocaleLowerCase())
              && <p className="raster-symbology-note">{zh ? '同名自定义颜色带会被 Host 替换。' : 'The Host replaces a saved custom palette with the same name.'}</p>}
          </div>
        </section>
        <div className="raster-symbology-actions">
          <span className={draftValid ? 'is-valid' : 'is-invalid'}>{draftValid
            ? (zh ? '颜色断点有效' : 'Color stops are valid')
            : (zh ? '修正颜色或严格递增的分级值后才能提交' : 'Correct colors and strictly increasing values before applying')}</span>
          <button type="button" className="raster-symbology-apply" onClick={onApply}
            data-testid="raster-symbology-apply"
            disabled={busy || !dirty || !draftValid}>
            {zh ? '应用到 .vis' : 'Apply to .vis'}
          </button>
        </div>
      </>}
    </section>
  );
}

function snapshotStateKey(snapshot: RasterSymbologySnapshot): string {
  return JSON.stringify({
    draft: {
      paletteID: snapshot.draft.paletteID,
      name: snapshot.draft.name,
      stops: snapshot.draft.stops,
      valueSource: snapshot.draft.valueSource,
      grayscale: snapshot.draft.grayscale,
      alpha: snapshot.draft.alpha,
      isSurfaceFillEnabled: snapshot.draft.isSurfaceFillEnabled,
      smoothingRadius: snapshot.draft.smoothingRadius,
      resampleCellSizeRatio: snapshot.draft.resampleCellSizeRatio,
      hillshade: snapshot.draft.hillshade,
      contours: snapshot.draft.contours,
    },
    plotOptions: snapshot.plotOptions.map(option => [option.propertyName, option.value]),
  });
}

function cloneSnapshot(snapshot: RasterSymbologySnapshot): RasterSymbologySnapshot {
  return {
    ...snapshot,
    draft: cloneRasterSymbologyDraft(snapshot.draft),
    palettes: snapshot.palettes.map(palette => ({ ...palette, colors: [...palette.colors], values: palette.values ? [...palette.values] : null })),
    surfaceFillValues: snapshot.surfaceFillValues ? [...snapshot.surfaceFillValues] : null,
    plotOptions: snapshot.plotOptions.map(option => ({ ...option, enumValues: option.enumValues.map(choice => ({ ...choice })) })),
  };
}

function makeAppliedRasterLegend(source: RasterMapSourceIdentity,
  snapshot: RasterSymbologySnapshot): AppliedRasterLegend | null {
  const draft = cloneRasterSymbologyDraft(snapshot.draft);
  const values = snapshot.surfaceFillValues;
  if (!values || values.length < 2 || values.length !== draft.stops.length
    || !isValidRasterSymbologyDraft(draft)
    || values.some((value, index) => !Number.isFinite(value)
      || value !== draft.stops[index]?.value
      || index > 0 && value <= values[index - 1])) return null;
  return {
    source: cloneRasterMapSourceIdentity(source),
    draft,
    values: [...values],
  };
}

function cloneAppliedRasterLegend(legend: AppliedRasterLegend): AppliedRasterLegend {
  return {
    source: cloneRasterMapSourceIdentity(legend.source),
    draft: cloneRasterSymbologyDraft(legend.draft),
    values: legend.values ? [...legend.values] : null,
  };
}

function cloneRasterMapSourceIdentity(source: RasterMapSourceIdentity): RasterMapSourceIdentity {
  return { ...source, nodePath: [...source.nodePath] };
}

function assertRasterRasterPayloadOwner(raster: { id: string; sourceMapID: string | null }, source: RasterMapSourceIdentity): void {
  if (raster.id !== source.id || raster.sourceMapID !== source.sourceMapID) {
    throw new Error('Host 的栅格渲染回执与当前栅格来源不匹配。');
  }
}

function isValidPaletteName(value: string): boolean {
  return value.length > 0 && value.length <= 128 && !value.startsWith('__') && !/[\u0000-\u001f\u007f]/.test(value);
}

function createGuid(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') return crypto.randomUUID();
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, token => {
    const value = Math.floor(Math.random() * 16);
    return (token === 'x' ? value : (value & 0x3) | 0x8).toString(16);
  });
}

function sameRevision(left: string, right: string): boolean {
  return left.toLocaleUpperCase('en-US') === right.toLocaleUpperCase('en-US');
}

function formatDiagnostics(value: unknown): string {
  try { return JSON.stringify(value, null, 2) ?? String(value); }
  catch { return String(value); }
}
