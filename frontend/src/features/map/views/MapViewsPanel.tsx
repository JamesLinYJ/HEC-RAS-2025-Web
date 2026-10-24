import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { FeaturePageProps, WorkbenchContext } from '../../../shared/workbench';
import { errorDiagnostics, message } from '../../../shared/workbench';
import type { MapDocumentTab, MapExtent } from '../mapTypes';
import { ViewEditor } from './ViewEditor';
import { ViewViewer } from './ViewViewer';
import {
  commitMapViewAction,
  inspectMapViews,
  MAP_VIEWS_COMMIT,
  MAP_VIEWS_INSPECT,
} from './ViewManager';
import type { MapViewsCommitReceipt, MapViewsInspection } from './ViewManager';
import {
  isValidViewExtent,
  mapExtentFromViewExtent,
  normalizeShortcut,
  shortcutConflict,
  shortcutFromKeyboardEvent,
  viewExtentFromMapExtent,
} from './View';
import type { SavedMapView, ViewOwner } from './View';
import './views.css';

export interface MapViewsPanelProps extends FeaturePageProps {
  activeDocument: MapDocumentTab | null;
  /** The current .vis revision from the map catalog; falls back to the active document. */
  visualizationRevision?: string | null;
  /** The live camera extent, supplied by the map owner. */
  currentExtent: MapExtent | null;
  onNavigateToExtent: (extent: MapExtent) => void;
  /** Parent accepts the commit only while this project/map owner is still active. */
  onVisualizationCommit: (source: WorkbenchContext, receipt: MapViewsCommitReceipt) => boolean;
  onCommitStateChange?: (busy: boolean) => void;
  /** Attach the native shortcuts to the map canvas, not the document or window. */
  shortcutScope?: HTMLElement | null;
  shortcutScopeRef?: { readonly current: HTMLElement | null };
  /** Optional source context included in the Host's optimistic identity checks. */
  coreLayerContext?: unknown;
}

function stableValue(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(stableValue);
  if (!value || typeof value !== 'object') return value;
  return Object.fromEntries(Object.keys(value as Record<string, unknown>).sort().map((key) => [key, stableValue((value as Record<string, unknown>)[key])]));
}

function extentLabel(extent: MapExtent | null, zh: boolean): string {
  if (!extent || !Number.isFinite(extent.minX) || !Number.isFinite(extent.minY) ||
      !Number.isFinite(extent.maxX) || !Number.isFinite(extent.maxY)) return zh ? '当前范围不可用' : 'Current extent unavailable';
  const number = (value: number) => Number(value.toPrecision(9)).toString();
  return `${zh ? '左' : 'L'} ${number(extent.minX)} · ${zh ? '下' : 'B'} ${number(extent.minY)} · ${zh ? '右' : 'R'} ${number(extent.maxX)} · ${zh ? '上' : 'T'} ${number(extent.maxY)}`;
}

function editableTarget(target: EventTarget | null): boolean {
  if (!(target instanceof Element)) return true;
  return Boolean(target.closest('input,textarea,select,button,[contenteditable="true"],[role="textbox"]'));
}

function diagnosticsLabel(error: unknown): string {
  try {
    const diagnostics = errorDiagnostics(error);
    const payload = error instanceof Error
      ? diagnostics === error
        ? { name: error.name, message: error.message, stack: error.stack }
        : { diagnostics, error: error.message, name: error.name }
      : diagnostics;
    return JSON.stringify(payload, null, 2);
  }
  catch { return String(error); }
}

function primaryErrorMessage(error: unknown, language: 'zh' | 'en'): string {
  const raw = message(error, language);
  return language === 'zh' && !/[\u3400-\u9fff]/.test(raw)
    ? '地图视图操作未完成，请刷新列表并检查工程状态。'
    : raw;
}

function hasValidMapExtent(extent: MapExtent | null): extent is MapExtent {
  return Boolean(extent && Number.isFinite(extent.minX) && Number.isFinite(extent.minY) &&
    Number.isFinite(extent.maxX) && Number.isFinite(extent.maxY) && extent.minX < extent.maxX && extent.minY < extent.maxY);
}

export function MapViewsPanel({
  language,
  context,
  backend,
  activeDocument,
  visualizationRevision,
  currentExtent,
  onNavigateToExtent,
  onVisualizationCommit,
  onCommitStateChange,
  hasUnsavedDrafts = false,
  shortcutScope,
  shortcutScopeRef,
  coreLayerContext,
}: MapViewsPanelProps) {
  const zh = language === 'zh';
  const effectiveVisualizationRevision = visualizationRevision !== undefined
    ? visualizationRevision
    : activeDocument?.visualizationRevision ?? null;
  const viewContext = useMemo<WorkbenchContext>(() => ({
    projectSessionID: context.projectSessionID,
    projectPath: context.projectPath,
    projectXml: context.projectXml,
    projectRevision: context.projectRevision,
    selectedAsset: null,
  }), [context.projectPath, context.projectXml, context.projectRevision, context.projectSessionID]);

  const owner = useMemo<ViewOwner | null>(() => {
    if (!activeDocument || !context.projectPath || !context.projectRevision || !context.projectSessionID || context.projectXml === null) return null;
    return {
      projectPath: context.projectPath,
      projectRevision: context.projectRevision,
      projectSessionID: context.projectSessionID,
      visualizationRevision: effectiveVisualizationRevision,
      mapID: activeDocument.mapID,
      mapIndex: activeDocument.mapIndex,
      mapName: activeDocument.name,
      mapType: activeDocument.mapType,
      ...(coreLayerContext !== undefined ? { coreLayerContext } : {}),
    };
  }, [activeDocument?.mapID, activeDocument?.mapIndex, activeDocument?.name, activeDocument?.mapType,
    context.projectPath, context.projectRevision, context.projectSessionID, context.projectXml,
    effectiveVisualizationRevision, coreLayerContext]);

  const ownerKey = useMemo(() => owner ? JSON.stringify(stableValue([
    owner.projectPath, owner.projectRevision, context.projectXml, owner.projectSessionID,
    owner.visualizationRevision, owner.mapID, owner.mapIndex, owner.mapName, owner.mapType, owner.coreLayerContext,
  ])) : '', [owner, context.projectXml]);

  const [inspection, setInspection] = useState<MapViewsInspection | null>(null);
  const [selectedName, setSelectedName] = useState<string | null>(null);
  const [createName, setCreateName] = useState('');
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    onCommitStateChange?.(busy);
    return () => onCommitStateChange?.(false);
  }, [busy, onCommitStateChange]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [status, setStatus] = useState('');
  const [pendingParentRevision, setPendingParentRevision] = useState<string | null>(null);
  const controllerRef = useRef<AbortController | undefined>(undefined);
  const generationRef = useRef(0);
  const ownerKeyRef = useRef(ownerKey);
  const createNameRef = useRef<HTMLInputElement | null>(null);
  ownerKeyRef.current = ownerKey;

  const selectedMatches = inspection?.views.filter((view) => view.name === selectedName) ?? [];
  const selectedAmbiguous = selectedMatches.length > 1;
  const selectedView = selectedMatches.length === 1 ? selectedMatches[0] : null;
  const sourceAvailable = Boolean(owner && context.projectPath && context.projectRevision && context.projectXml !== null && activeDocument);
  const operationAvailable = backend.supports(MAP_VIEWS_INSPECT) && backend.supports(MAP_VIEWS_COMMIT);
  const awaitingOwnerRefresh = Boolean(pendingParentRevision && pendingParentRevision.toUpperCase() !== (effectiveVisualizationRevision ?? '').toUpperCase());
  const inspectionOutOfDate = Boolean(inspection &&
    (inspection.visualizationRevision ?? '').toUpperCase() !== (effectiveVisualizationRevision ?? '').toUpperCase());
  const draftBlock = hasUnsavedDrafts;
  const writeDisabled = busy || loading || !sourceAvailable || !operationAvailable || draftBlock || awaitingOwnerRefresh || inspectionOutOfDate || !inspection;
  const currentExtentIsValid = hasValidMapExtent(currentExtent);
  const duplicateCreateName = Boolean(createName.trim() && inspection?.views.some((view) => view.name.toLowerCase() === createName.trim().toLowerCase()));
  const createDisabled = writeDisabled || !createName.trim() || duplicateCreateName || !currentExtentIsValid;
  const disabledReason = draftBlock
    ? (zh ? '请先保存或还原未保存的工程/地图更改。' : 'Save or discard unsaved project or map changes first.')
    : awaitingOwnerRefresh
      ? (zh ? '视图已提交，正在等待地图目录刷新到新修订。' : 'The view was committed; waiting for the map catalog to refresh.')
      : inspectionOutOfDate
        ? (zh ? '视图列表修订与地图目录不一致，请刷新地图目录后重试。' : 'The saved-view list differs from the map catalog revision. Refresh the map catalog and try again.')
      : !operationAvailable
        ? (zh ? '工程服务尚未发布地图视图写入操作。' : 'The project service has not advertised saved-map-view operations.')
        : !sourceAvailable
          ? (zh ? '请先打开一个已保存工程及地图。' : 'Open a saved project and map first.')
          : undefined;

  const runList = useCallback(async (signal?: AbortSignal, generation?: number) => {
    if (!owner) return;
    const requestGeneration = generation ?? ++generationRef.current;
    setLoading(true);
    setError(null);
    try {
      const next = await inspectMapViews(backend, viewContext, 'list', undefined, { signal }, owner.coreLayerContext);
      if (requestGeneration !== generationRef.current || ownerKeyRef.current !== ownerKey) return;
      setInspection(next);
      setSelectedName((previous) => previous && next.views.some((view) => view.name === previous) ? previous : null);
    } catch (nextError) {
      if (requestGeneration !== generationRef.current || signal?.aborted || ownerKeyRef.current !== ownerKey) return;
      setError(nextError);
      setInspection(null);
    } finally {
      if (requestGeneration === generationRef.current && ownerKeyRef.current === ownerKey) setLoading(false);
    }
  }, [backend, viewContext, owner, ownerKey, zh]);

  useEffect(() => {
    const generation = ++generationRef.current;
    controllerRef.current?.abort();
    controllerRef.current = undefined;
    setInspection(null);
    setSelectedName(null);
    setCreateName('');
    setBusy(false);
    setLoading(false);
    setError(null);
    setStatus('');
    setPendingParentRevision(null);
    if (!owner) return undefined;
    const controller = new AbortController();
    controllerRef.current = controller;
    void runList(controller.signal, generation);
    return () => {
      controller.abort();
      if (controllerRef.current === controller) controllerRef.current = undefined;
    };
  }, [ownerKey, runList, owner]);

  const submit = useCallback(async (intent: Parameters<typeof commitMapViewAction>[3]) => {
    if (!owner || !inspection || writeDisabled) return;
    setError(null);
    setStatus('');
    setBusy(true);
    controllerRef.current?.abort();
    const controller = new AbortController();
    controllerRef.current = controller;
    const generation = ++generationRef.current;
    try {
      const receipt = await commitMapViewAction(backend, viewContext, owner, intent, inspection, { signal: controller.signal });
      if (generation !== generationRef.current || ownerKeyRef.current !== ownerKey) return;
      const accepted = onVisualizationCommit(context, receipt);
      if (!accepted) {
        setPendingParentRevision(receipt.visualizationRevision);
        throw new Error(zh
          ? '服务已提交更改，但当前地图容器未接纳该修订。请刷新地图目录后核对。'
          : 'The service committed the change, but the active map owner did not accept the revision. Refresh the map catalog to verify it.');
      }
      const updated: MapViewsInspection = {
        ...inspection,
        canCommit: true,
        visualizationRevision: receipt.visualizationRevision,
        viewsSHA256: receipt.viewsSHA256,
        views: receipt.views,
        target: receipt.after,
      };
      setInspection(updated);
      if (intent.action === 'delete') setSelectedName(null);
      else if (intent.action === 'create') setSelectedName(receipt.after?.name ?? null);
      else if (intent.action === 'rename') setSelectedName(receipt.after?.name ?? null);
      else if (intent.targetName) setSelectedName(intent.targetName);
      setPendingParentRevision(receipt.visualizationRevision.toUpperCase() === (effectiveVisualizationRevision ?? '').toUpperCase()
        ? null : receipt.visualizationRevision);
      setStatus(intent.action === 'create'
        ? (zh ? '地图视图已保存。' : 'The map view was saved.')
        : intent.action === 'rename'
          ? (zh ? '视图名称已更新。' : 'The view name was updated.')
          : intent.action === 'resetExtent'
            ? (zh ? '视图范围已更新为当前地图范围。' : 'The view now uses the current map extent.')
            : intent.action === 'delete'
              ? (zh ? '地图视图已删除。' : 'The map view was deleted.')
              : (zh ? '快捷键已更新。' : 'The shortcut was updated.'));
      if (intent.action === 'create') setCreateName('');
    } catch (nextError) {
      if (generation !== generationRef.current || controller.signal.aborted) return;
      setError(nextError);
      // A rejected parent callback is after the Host commit; keep controls locked until readback.
      if (nextError instanceof Error && /服务已提交更改|service committed the change/i.test(nextError.message)) {
        setStatus(zh ? '请刷新目录确认服务端视图状态。' : 'Refresh the catalog to verify the saved view state.');
      }
    } finally {
      if (generation === generationRef.current && ownerKeyRef.current === ownerKey) setBusy(false);
    }
  }, [backend, context, viewContext, owner, inspection, writeDisabled, ownerKey, onVisualizationCommit, zh, effectiveVisualizationRevision]);

  const saveCurrent = useCallback(() => {
    const name = createName.trim();
    if (!name) {
      setError(new Error(zh ? '请输入地图视图名称。' : 'Enter a name for the map view.'));
      createNameRef.current?.focus();
      return;
    }
    if (!currentExtentIsValid || !currentExtent) {
      setError(new Error(zh ? '当前地图没有可保存的有效范围。' : 'The current map has no valid extent to save.'));
      return;
    }
    if (duplicateCreateName) {
      setError(new Error(zh ? '已存在同名视图，请换一个名称。' : 'A view with that name already exists. Choose another name.'));
      createNameRef.current?.focus();
      return;
    }
    void submit({ action: 'create', name, extent: viewExtentFromMapExtent(currentExtent) });
  }, [createName, currentExtent, currentExtentIsValid, duplicateCreateName, submit, zh]);

  const navigate = useCallback((view: SavedMapView) => {
    if (!isValidViewExtent(view.extent)) return;
    onNavigateToExtent(mapExtentFromViewExtent(view.extent));
    setSelectedName(view.name);
    setStatus(zh ? `已定位到“${view.name}”。` : `Moved to “${view.name}”.`);
  }, [onNavigateToExtent, zh]);

  useEffect(() => {
    const scope = shortcutScopeRef?.current ?? shortcutScope ?? null;
    if (!scope) return undefined;
    const handleShortcut = (event: KeyboardEvent) => {
      if (editableTarget(event.target)) return;
      const target = event.target;
      if (!(target instanceof Node) || !scope.contains(target)) return;
      if (event.repeat || event.isComposing || event.key === 'Process') return;
      const shortcut = shortcutFromKeyboardEvent(event);
      if (!shortcut) return;
      if (shortcut === 'Shift+V') {
        if (createDisabled) return;
        event.preventDefault();
        saveCurrent();
        return;
      }
      const view = inspection?.views.find((candidate) => candidate.shortcut !== 'None' && normalizeShortcut(candidate.shortcut) === shortcut);
      if (view) {
        event.preventDefault();
        navigate(view);
      }
    };
    scope.addEventListener('keydown', handleShortcut);
    return () => scope.removeEventListener('keydown', handleShortcut);
  }, [shortcutScope, shortcutScopeRef, inspection?.views, createDisabled, saveCurrent, navigate]);

  const refresh = () => {
    controllerRef.current?.abort();
    const generation = ++generationRef.current;
    const controller = new AbortController();
    controllerRef.current = controller;
    void runList(controller.signal, generation);
  };

  const commitRename = (name: string) => {
    if (!selectedView) return;
    if (name.trim().toLowerCase() !== selectedView.name.toLowerCase() && inspection?.views.some((view) => view.name.toLowerCase() === name.trim().toLowerCase())) {
      setError(new Error(zh ? '已存在同名视图。' : 'A view with that name already exists.'));
      return;
    }
    void submit({ action: 'rename', targetName: selectedView.name, name });
  };

  const commitShortcut = (shortcut: string) => {
    if (!selectedView || !inspection) return;
    const conflict = shortcutConflict(shortcut, inspection.views, selectedView.name);
    if (conflict) { setError(new Error(conflict)); return; }
    void submit({ action: 'shortcut', targetName: selectedView.name, shortcut });
  };

  const commitReset = (extent: MapExtent) => {
    if (!selectedView || !hasValidMapExtent(extent)) return;
    void submit({ action: 'resetExtent', targetName: selectedView.name, extent: viewExtentFromMapExtent(extent) });
  };

  const details = error ? diagnosticsLabel(error) : '';
  const title = activeDocument ? `${activeDocument.name} · ${activeDocument.mapType}` : (zh ? '未选择地图' : 'No map selected');

  return (
    <section className="map-views-panel" data-testid="map-views-panel" aria-label={zh ? '地图已保存视图' : 'Saved map views'}>
      <header className="map-views-header">
        <div>
          <h2>{zh ? '地图视图' : 'Map views'}</h2>
          <p data-testid="map-views-owner">{title}</p>
        </div>
        <button type="button" data-testid="map-view-refresh" disabled={loading || busy || !sourceAvailable || !operationAvailable}
          onClick={refresh}>{loading ? (zh ? '读取中…' : 'Loading…') : (zh ? '刷新' : 'Refresh')}</button>
      </header>

      <div className="map-views-camera" data-testid="map-view-camera-extent" aria-live="polite">
        <span>{zh ? '当前地图范围' : 'Current map extent'}</span>
        <output>{extentLabel(currentExtent, zh)}</output>
      </div>

      <div className="map-views-create">
        <label htmlFor="map-view-name">{zh ? '保存当前范围为' : 'Save current extent as'}</label>
        <div className="map-views-create-row">
          <input id="map-view-name" ref={createNameRef} data-testid="map-view-name" value={createName} maxLength={120}
            disabled={writeDisabled} onChange={(event) => { setCreateName(event.target.value); setError(null); }}
            onKeyDown={(event) => { if (event.key === 'Enter') { event.preventDefault(); saveCurrent(); } }} />
          <button type="button" data-testid="map-view-save" disabled={createDisabled} title={disabledReason}
            onClick={saveCurrent}>{zh ? '保存视图' : 'Save view'}</button>
        </div>
        {duplicateCreateName && <small className="map-views-inline-error">{zh ? '名称已存在。' : 'That name already exists.'}</small>}
      </div>

      <div className="map-views-content">
        <div className="map-views-list-pane" aria-busy={loading}>
          {loading && <p className="map-views-loading">{zh ? '正在读取工程视图…' : 'Reading project views…'}</p>}
          {!loading && inspection && <ViewViewer language={language} views={inspection.views} selectedName={selectedName}
            disabled={busy} onSelect={(view) => setSelectedName(view.name)} onNavigate={navigate} />}
          {!loading && !inspection && !error && <p className="map-views-loading">{disabledReason ?? (zh ? '正在准备地图视图…' : 'Preparing map views…')}</p>}
        </div>
        {selectedAmbiguous
          ? <p className="map-view-editor-empty">{zh ? '存在同名视图，服务无法安全区分目标；请先在工程文件中消除重名。' : 'The service cannot safely distinguish these duplicate names. Remove the duplicate in the project file first.'}</p>
          : <ViewEditor language={language} view={selectedView} currentExtent={currentExtent}
            disabled={writeDisabled} disabledReason={disabledReason} onRename={commitRename} onResetExtent={commitReset}
            onDelete={() => { if (selectedView) void submit({ action: 'delete', targetName: selectedView.name }); }}
            onShortcut={commitShortcut} />}
      </div>

      {Boolean(error) && <div className="map-views-error" role="alert">
        <p>{primaryErrorMessage(error, language)}</p>
        <details><summary>{zh ? '诊断详情' : 'Diagnostic details'}</summary><pre>{details}</pre></details>
      </div>}
      {status && <p className="map-views-status" role="status" aria-live="polite">{status}</p>}
      {busy && <p className="map-views-status" role="status">{zh ? '正在提交地图视图更改…' : 'Committing map view changes…'}</p>}
      {draftBlock && <p className="map-views-note">{disabledReason}</p>}
      {pendingParentRevision && <p className="map-views-note" data-testid="map-view-awaiting-refresh">
        {awaitingOwnerRefresh
          ? (zh ? '地图目录尚未确认新修订，写入操作已暂停。' : 'Writes are paused until the map catalog confirms the new revision.')
          : (zh ? '已收到地图目录新修订。' : 'The map catalog has published the new revision.')}
      </p>}
    </section>
  );
}
