import { useEffect, useRef, useState } from 'react';
import type { AssetReference, FeaturePageProps, WorkbenchContext } from '../../../shared/workbench';
import { errorDiagnostics, message, text } from '../../../shared/workbench';
import type { ModelSnapshot } from '../../editors/model';
import {
  assertModelSnapshot, hasUncapturedSessionReferences, isSha256, modelLoadRequest,
  modelSaveRequest, modelSourceState, requestIdentity, sourceOwnerKey, updatedModelContext,
} from '../coreImportWorkflow';
import type {
  TerrainInputAppendRequest, TerrainInputAppendResult, TerrainInputImportInspection,
  TerrainInputImportSelection, TerrainInputOperationResult,
} from '../coreImportWorkflow';
import { USGSTerrainImportDialog } from './USGSTerrainImportDialog';
import type {
  TerrainUSGSDownloadData, TerrainUSGSDownloadedFile, TerrainUSGSCleanupData,
  TerrainUSGSImportStatus, TerrainUSGSQueryData,
} from './usgsTypes';
import { validUSGSDownload, validUSGSQuery } from './usgsTypes';
import './usgs.css';

type Busy = 'query' | 'download' | 'inspect' | 'append' | 'save' | 'cleanup' | null;
interface QueryState { owner: string; snapshot: ModelSnapshot; result: TerrainUSGSQueryData }
interface ImportedRow { include: boolean; rounding: string }
interface LastReceipt { append: TerrainInputAppendResult; saved: ModelSnapshot }
interface PendingSave { owner: string; snapshot: ModelSnapshot; append: TerrainInputAppendResult; download: TerrainUSGSDownloadData }
interface PendingDownload { owner: string; data: TerrainUSGSDownloadData; inspection: TerrainInputImportInspection; sourceById: Map<string, TerrainUSGSDownloadedFile> }
interface CleanupRecord { owner: string; token: string; download: TerrainUSGSDownloadData; context: WorkbenchContext; asset: AssetReference; expected: ModelSnapshot; receipt?: TerrainInputAppendResult }

function hasProjectContext(context: FeaturePageProps['context']): boolean {
  return Boolean(context.projectSessionID && context.projectPath && context.projectXml !== null && isSha256(context.projectRevision));
}
function normalizedPath(value: string): string {
  return value.replaceAll('\\', '/').replace(/\/{2,}/g, '/');
}
function delay(milliseconds: number, signal: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    if (signal.aborted) { reject(new DOMException('Request cancelled.', 'AbortError')); return; }
    const timer = window.setTimeout(() => { signal.removeEventListener('abort', abort); resolve(); }, milliseconds);
    const abort = (): void => { window.clearTimeout(timer); reject(new DOMException('Request cancelled.', 'AbortError')); };
    signal.addEventListener('abort', abort, { once: true });
  });
}
function operationProgress(value: unknown): { fraction?: number; message?: string } {
  if (!value || typeof value !== 'object') return {};
  const item = value as Record<string, unknown>;
  const fraction = typeof item.fraction === 'number' && Number.isFinite(item.fraction)
    ? Math.max(0, Math.min(1, item.fraction)) : undefined;
  const messageValue = typeof item.message === 'string' ? item.message : typeof item.taskName === 'string' ? item.taskName : undefined;
  return { fraction, message: messageValue };
}
function rawDiagnostic(error: unknown): string {
  const details = errorDiagnostics(error);
  if (typeof details === 'string') return details;
  if (details !== undefined) {
    try { return JSON.stringify(details, null, 2); } catch { return String(details); }
  }
  return error instanceof Error ? error.message : String(error);
}
function importFailureOperations(operations: TerrainInputOperationResult[]): TerrainInputOperationResult[] {
  return operations.filter(item => item.status !== 'Staged' && item.status !== 'Committed');
}

export function USGSTerrainImportControl(props: FeaturePageProps) {
  const { language, context, backend, onContextChange, onContextCommit, hasUnsavedDrafts } = props;
  const zh = language === 'zh';
  const asset = context.selectedAsset?.kind === 'Terrain' ? context.selectedAsset : null;
  const owner = sourceOwnerKey(context);
  const ownerRef = useRef(owner); ownerRef.current = owner;
  const generation = useRef(0);
  const controller = useRef<AbortController | null>(null);
  const appendRef = useRef<{ owner: string; request: TerrainInputAppendRequest; snapshot: ModelSnapshot } | null>(null);
  const acceptedOwner = useRef<string | null>(null);
  const [inputId, setInputId] = useState('');
  const [query, setQuery] = useState<QueryState>();
  const [selectedIds, setSelectedIds] = useState<Set<string>>(() => new Set());
  const [importingIds, setImportingIds] = useState<Set<string>>(() => new Set());
  const [pendingDownload, setPendingDownload] = useState<PendingDownload>();
  const [lastDownload, setLastDownload] = useState<TerrainUSGSDownloadData>();
  const [rowSettings, setRowSettings] = useState<Record<string, ImportedRow>>({});
  const [stitchTiles, setStitchTiles] = useState(true);
  const [lastSnapshot, setLastSnapshot] = useState<ModelSnapshot>();
  const [lastReceipt, setLastReceipt] = useState<LastReceipt>();
  const [pendingSave, setPendingSave] = useState<PendingSave>();
  const [cleanupRecord, setCleanupRecord] = useState<CleanupRecord>();
  const [status, setStatus] = useState<TerrainUSGSImportStatus>();
  const [busy, setBusy] = useState<Busy>(null);
  const [progress, setProgress] = useState<{ fraction?: number; message?: string }>();
  const [error, setError] = useState<string>();
  const [diagnostic, setDiagnostic] = useState<string>();
  const [notice, setNotice] = useState<string>();
  const [cleanupToken, setCleanupToken] = useState<string>();

  const capabilities = {
    load: backend.supports('model.load'),
    query: backend.supports('model.terrainUSGSQuery'),
    download: backend.supports('model.terrainUSGSDownload'),
    cleanup: backend.supports('model.terrainUSGSCleanup'),
    inspect: backend.supports('model.terrainInputImportInspect'),
    append: backend.supports('model.terrainInputAppend'),
    status: backend.supports('model.terrainInputImportStatus'),
    cancel: backend.supports('model.terrainInputImportCancel'),
    save: backend.supports('model.save'),
  };
  const projectReady = hasProjectContext(context);
  const refsReady = !hasUncapturedSessionReferences(context, asset);
  const queryIsCurrent = Boolean(query && query.owner === owner && asset &&
    (!asset.revision || query.snapshot.revision === asset.revision));
  const pendingIsCurrent = Boolean(pendingDownload && pendingDownload.owner === owner);
  const canQuery = Boolean(asset && projectReady && refsReady && !hasUnsavedDrafts && !cleanupToken && capabilities.load && capabilities.query && busy === null);
  const canDownload = Boolean(queryIsCurrent && !hasUnsavedDrafts && !cleanupToken && capabilities.download && capabilities.inspect && busy === null && selectedIds.size > 0);
  const canImport = Boolean(pendingIsCurrent && !pendingSave && !hasUnsavedDrafts && capabilities.append && capabilities.save && busy === null &&
    Object.entries(rowSettings).some(([id, value]) => value.include && pendingDownload?.inspection.sources.some(source => source.id === id && source.isValid)));
  const availableInputs = query?.snapshot.terrain?.inputs ?? [];
  const visibleQuery = queryIsCurrent ? query : undefined;
  useEffect(() => {
    if (acceptedOwner.current === owner) { acceptedOwner.current = null; return; }
    controller.current?.abort();
    generation.current += 1;
    appendRef.current = null;
    setQuery(undefined); setSelectedIds(new Set()); setPendingDownload(undefined); setRowSettings({});
    setLastSnapshot(undefined); setLastReceipt(undefined); setPendingSave(undefined); setStatus(undefined); setBusy(null); setProgress(undefined);
    setError(undefined); setDiagnostic(undefined); setNotice(undefined);
    setInputId(''); setImportingIds(new Set());
  }, [owner]);
  useEffect(() => () => { generation.current += 1; controller.current?.abort(); }, []);

  const isCurrent = (id: number, signal: AbortSignal): boolean => generation.current === id && ownerRef.current === owner && !signal.aborted;
  const begin = (): { id: number; signal: AbortSignal } => {
    controller.current?.abort();
    const next = new AbortController(); controller.current = next;
    return { id: ++generation.current, signal: next.signal };
  };
  const loadTerrain = async (signal: AbortSignal): Promise<ModelSnapshot> => {
    if (!asset) throw new Error(text(language, '请从当前工程选择一个 Terrain 地形模型。', 'Select a Terrain model from the active project.'));
    return loadTerrainFor(context, asset, signal);
  };
  const loadTerrainFor = async (sourceContext: WorkbenchContext, sourceAsset: AssetReference, signal: AbortSignal): Promise<ModelSnapshot> => {
    if (!hasProjectContext(sourceContext)) throw new Error(text(language, '请打开工程并提供有效的 SHA-256 修订与会话。', 'Open a project with a valid SHA-256 revision and session.'));
    if (hasUncapturedSessionReferences(sourceContext, sourceAsset)) throw new Error(text(language, '当前模型包含未捕获的会话外部引用，请从当前地图树重新选择。', 'The model has uncaptured session-only references. Select it again from the active map tree.'));
    const loaded = await backend.request<ModelSnapshot>('model.load', modelLoadRequest(sourceContext, sourceAsset), { signal });
    assertModelSnapshot(loaded);
    if (!loaded.terrain) throw new Error(text(language, 'C# 服务没有返回 Terrain 输入快照。', 'The C# server returned no Terrain input snapshot.'));
    if (!isSha256(loaded.revision) || (sourceAsset.revision && loaded.revision !== sourceAsset.revision)) {
      throw new Error(text(language, '模型修订已变化或不是有效 SHA-256；请重新载入当前模型。', 'The model revision changed or is not a valid SHA-256. Reload the current model.'));
    }
    return loaded;
  };

  const runQuery = async (): Promise<void> => {
    if (!canQuery || !asset) return;
    const request = begin(); setBusy('query'); setProgress(undefined); setError(undefined); setDiagnostic(undefined); setNotice(undefined);
    setQuery(undefined); setPendingDownload(undefined); setLastDownload(undefined); setLastReceipt(undefined); setPendingSave(undefined); setStatus(undefined); setCleanupToken(undefined);
    try {
      const snapshot = await loadTerrain(request.signal);
      if (!isCurrent(request.id, request.signal)) return;
      const model = modelSaveRequest(context, asset, snapshot);
      const chosenInputId = inputId || snapshot.terrain?.selectedId || undefined;
      const result = await backend.request<TerrainUSGSQueryData>('model.terrainUSGSQuery', {
        model, ...(chosenInputId ? { inputId: chosenInputId } : {}),
      }, { signal: request.signal });
      if (!isCurrent(request.id, request.signal)) return;
      if (!validUSGSQuery(result) || (chosenInputId && result.inputId !== chosenInputId)) {
        throw new Error(text(language, 'USGS 查询没有返回匹配当前 Terrain 输入的有效产品和范围。', 'The USGS query did not return valid products and an extent for the selected Terrain input.'));
      }
      setQuery({ owner, snapshot, result }); setLastSnapshot(snapshot);
      setSelectedIds(new Set());
      setNotice(result.products.length === 0
        ? text(language, '查询已完成，但该范围没有可下载的 USGS 地形产品。', 'The query completed, but no downloadable USGS terrain products cover this extent.')
        : text(language, `查询完成：服务端返回 ${result.products.length} 项可用产品。`, `Query complete: the server returned ${result.products.length} available products.`));
      if (result.diagnostic) setDiagnostic(result.diagnostic);
    } catch (caught) {
      if (request.signal.aborted) setNotice(text(language, '查询已取消或响应过期；未更新产品列表。', 'The query was cancelled or became stale; the product list was not updated.'));
      else if (isCurrent(request.id, request.signal)) { setError(message(caught, language)); setDiagnostic(rawDiagnostic(caught)); }
    } finally { if (generation.current === request.id) setBusy(null); }
  };

  const cancelQueryOrDownload = (): void => {
    if (busy !== 'query' && busy !== 'download' && busy !== 'inspect') return;
    controller.current?.abort();
    setNotice(text(language, '已请求取消；请以 Host 最终状态为准。若下载已返回清理令牌，令牌会保留供安全清理。', 'Cancellation was requested; use the Host terminal state. Any returned cleanup token is retained for safe cleanup.'));
  };

  const toggleProduct = (id: string): void => setSelectedIds(current => {
    const next = new Set(current); if (next.has(id)) next.delete(id); else next.add(id); return next;
  });
  const selectVisible = (ids: string[], selected: boolean): void => setSelectedIds(current => {
    const next = new Set(current); for (const id of ids) selected ? next.add(id) : next.delete(id); return next;
  });

  const downloadAndInspect = async (): Promise<void> => {
    if (!canDownload || !asset || !query) return;
    const request = begin(); setBusy('download'); setProgress(undefined); setError(undefined); setDiagnostic(undefined); setNotice(undefined);
    setPendingDownload(undefined); setRowSettings({}); setLastReceipt(undefined); setPendingSave(undefined); setStatus(undefined); setCleanupToken(undefined);
    const selectedProducts = query.result.products.filter(product => selectedIds.has(product.id));
    setImportingIds(new Set(selectedProducts.map(product => product.id)));
    try {
      const fresh = await loadTerrain(request.signal);
      if (!isCurrent(request.id, request.signal)) return;
      if (fresh.revision !== query.snapshot.revision) throw new Error(text(language, '查询后模型内容已变化，请重新查询产品。', 'The model changed after the query. Query products again.'));
      const model = modelSaveRequest(context, asset, fresh);
      const downloaded = await backend.request<TerrainUSGSDownloadData>('model.terrainUSGSDownload', {
        model, productIds: selectedProducts.map(item => item.id), ...(query.result.inputId ? { inputId: query.result.inputId } : {}),
      }, { signal: request.signal, onProgress: value => { if (isCurrent(request.id, request.signal)) setProgress(operationProgress(value)); } });
      if (!isCurrent(request.id, request.signal)) return;
      if (downloaded && typeof downloaded.state === 'string' && downloaded.state.toLowerCase().includes('cancel')) {
        setImportingIds(new Set());
        setNotice(text(language, 'USGS 下载已由 Host 取消，未进入 Terrain 导入。', 'The Host cancelled the USGS download; no Terrain import was started.'));
        return;
      }
      if (downloaded && typeof downloaded.cleanupToken === 'string' && downloaded.cleanupToken) setCleanupToken(downloaded.cleanupToken);
      if (validUSGSDownload(downloaded)) {
        setLastDownload(downloaded);
        setCleanupRecord({ owner, token: downloaded.cleanupToken!, download: downloaded, context, asset, expected: fresh });
      }
      if (!validUSGSDownload(downloaded) || downloaded.sourceRevision !== fresh.revision ||
          downloaded.assetPath !== asset.path && normalizedPath(downloaded.assetPath) !== normalizedPath(asset.path) ||
          normalizedPath(downloaded.projectPath) !== normalizedPath(context.projectPath ?? '') ||
          (query.result.inputId != null && downloaded.inputId !== query.result.inputId) ||
          downloaded.files.length !== selectedProducts.length ||
          selectedProducts.some(product => !downloaded.files.some(file => file.productId === product.id))) {
        throw new Error(text(language, 'USGS 下载回执与当前模型修订或所选产品不一致；保留 Host 临时清理令牌。', 'The USGS download receipt does not match the model revision or selected products. The Host cleanup token is retained.'));
      }
      setBusy('inspect'); setProgress(undefined);
      const inspected = await backend.request<TerrainInputImportInspection>('model.terrainInputImportInspect', {
        model, filePaths: downloaded.files.map(file => file.filePath),
      }, { signal: request.signal });
      if (!isCurrent(request.id, request.signal)) return;
      if (!inspected || !Array.isArray(inspected.sources) || !inspected.sources.length ||
          !Array.isArray(inspected.roundingOptions) || !inspected.roundingOptions.length ||
          !Array.isArray(inspected.unitConversionOptions) || !inspected.unitConversionOptions.includes('Default')) {
        throw new Error(text(language, 'Terrain 检查没有返回有效来源或服务端导入选项。', 'Terrain inspection returned no valid sources or server import options.'));
      }
      const fileMap = new Map<string, TerrainUSGSDownloadedFile>();
      const rows: Record<string, ImportedRow> = {};
      for (const file of downloaded.files) {
        const source = inspected.sources.find(item => normalizedPath(item.path) === normalizedPath(file.filePath));
        if (!source || source.revision !== file.sourceRevision) {
          throw new Error(text(language, `下载来源修订与检查回执不匹配：${file.filePath}`, `The inspected source revision does not match the download receipt: ${file.filePath}`));
        }
        if (!Number.isFinite(file.defaultScaleFactor) || file.defaultScaleFactor <= 0 || !Number.isFinite(file.defaultScaleFactor * 1)) {
          throw new Error(text(language, `USGS 来源返回了无效的高程比例：${file.title}`, `The USGS source returned an invalid elevation scale: ${file.title}`));
        }
        fileMap.set(source.id, file);
        rows[source.id] = { include: source.isValid, rounding: inspected.roundingOptions.includes(file.defaultRounding) ? file.defaultRounding : inspected.roundingOptions[0]! };
      }
      if (inspected.sources.some(source => !fileMap.has(source.id))) {
        throw new Error(text(language, '检查回执包含不属于本次 USGS 下载的额外来源。', 'Inspection returned an extra source outside this USGS download.'));
      }
      setPendingDownload({ owner, data: downloaded, inspection: inspected, sourceById: fileMap });
      setRowSettings(rows); setStitchTiles(downloaded.stitchTiles); setLastSnapshot(fresh); setImportingIds(new Set());
      setNotice(text(language, '下载文件已由 Host 验证。核对来源、舍入与拼接设置后开始导入。', 'The Host validated the downloaded files. Review source, rounding, and stitching settings before importing.'));
      setBusy(null);
    } catch (caught) {
      setImportingIds(new Set());
      if (request.signal.aborted) setNotice(text(language, '已请求取消。请等待 Host 的最终操作状态；未收到回执时不视为下载完成。', 'Cancellation was requested. Wait for the Host terminal state; without a receipt the download is not considered complete.'));
      else if (isCurrent(request.id, request.signal)) { setError(message(caught, language)); setDiagnostic(rawDiagnostic(caught)); }
    } finally { if (generation.current === request.id) setBusy(null); }
  };

  const updateRow = (id: string, patch: Partial<ImportedRow>): void => setRowSettings(current => current[id] ? { ...current, [id]: { ...current[id]!, ...patch } } : current);

  const publishStaged = async (requestState: { id: number; signal: AbortSignal }, staged: PendingSave): Promise<void> => {
    if (!asset || staged.owner !== owner) throw new Error(text(language, '暂存批次不再属于当前模型。', 'The staged batch no longer belongs to the active model.'));
    setBusy('save');
    const saveRequest = modelSaveRequest(context, asset, staged.snapshot);
    if (saveRequest.assetPath !== asset.path || saveRequest.kind !== asset.kind || saveRequest.projectPath !== context.projectPath ||
        saveRequest.projectXml !== context.projectXml || saveRequest.revision !== staged.snapshot.revision ||
        typeof staged.snapshot.sourceState !== 'string' || staged.snapshot.sourceState.length === 0 ||
        saveRequest.sourceState !== staged.snapshot.sourceState) {
      throw new Error(text(language, '保存请求未捕获本批次的原模型修订和暂存来源状态。', 'The save request did not capture this batch’s original model revision and staged source state.'));
    }
    const saved = await backend.request<ModelSnapshot>('model.save', saveRequest, { signal: requestState.signal });
    if (!isCurrent(requestState.id, requestState.signal)) return;
    assertModelSnapshot(saved);
    if (!saved.terrain || !isSha256(saved.revision) || saved.revision === staged.snapshot.revision) {
      throw new Error(text(language, '保存回执没有确认新的 Terrain 模型修订。', 'The save receipt did not confirm a new Terrain model revision.'));
    }
    const stagedOutputIds = staged.append.operations.filter(item => item.status === 'Staged' && item.outputInputID).map(item => item.outputInputID!);
    const savedIds = new Set(saved.terrain.inputs.map(item => item.id));
    const outputSnapshotConfirmed = stagedOutputIds.length > 0 && stagedOutputIds.every(id => savedIds.has(id));
    const nextContext = updatedModelContext(context, asset, saved);
    const accepted = onContextCommit ? onContextCommit(context, nextContext) : true;
    if (!accepted) {
      setError(text(language, '服务端已保存，但工作台拒绝了过期上下文更新；请重新载入 Terrain。', 'The server saved the model, but the workbench rejected the stale context update. Reload Terrain.'));
      return;
    }
    acceptedOwner.current = sourceOwnerKey(nextContext);
    if (!onContextCommit) onContextChange(nextContext);
    setLastSnapshot(saved); setLastReceipt({ append: staged.append, saved }); setPendingSave(undefined); setQuery(undefined); setPendingDownload(undefined);
    setImportingIds(new Set());
    setNotice(outputSnapshotConfirmed
      ? text(language, '保存回执和输出 Terrain 输入 ID 已由服务端确认。', 'The save receipt and output Terrain input IDs are confirmed by the server.')
      : text(language, '保存回执确认了新模型修订，但输出输入 ID 未能与保存快照逐项匹配；清理令牌将保留。', 'The save receipt confirmed a new model revision, but output input IDs did not all match the saved snapshot. The cleanup token is retained.'));
    setCleanupToken(staged.download.cleanupToken ?? undefined);
    setCleanupRecord(current => current && current.token === staged.download.cleanupToken
      ? { ...current, owner: sourceOwnerKey(nextContext), context: nextContext,
        asset: nextContext.selectedAsset ?? asset, expected: saved, receipt: staged.append }
      : current);
    if (importFailureOperations(staged.append.operations).length) setError(text(language, '部分产品没有暂存；已保存成功项，详细失败状态见批次列表。', 'Some products were not staged. Successful items were saved; see the batch list for per-item failures.'));
  };

  const retrySave = async (): Promise<void> => {
    if (!pendingSave || pendingSave.owner !== owner || busy !== null || !asset || hasUnsavedDrafts) return;
    const request = begin(); setBusy('save'); setError(undefined); setDiagnostic(undefined);
    try {
      if (!projectReady || hasUncapturedSessionReferences(context, asset) ||
          (asset.revision && asset.revision !== pendingSave.snapshot.revision)) {
        throw new Error(text(language, '当前模型修订或工程会话与暂存保存请求不匹配；请重新载入后核对。', 'The active model revision or project session does not match the staged save request. Reload and review it.'));
      }
      await publishStaged(request, pendingSave);
    } catch (caught) {
      if (request.signal.aborted) setNotice(text(language, '保存请求已取消；暂存结果尚未由保存回执确认。', 'The save request was cancelled; staged results are not confirmed by a save receipt.'));
      else if (isCurrent(request.id, request.signal)) { setError(message(caught, language)); setDiagnostic(rawDiagnostic(caught)); }
    } finally { if (generation.current === request.id) setBusy(null); }
  };

  const runImport = async (): Promise<void> => {
    if (!canImport || !asset || !pendingDownload || !lastSnapshot) return;
    const requestState = begin(); setBusy('append'); setProgress(undefined); setError(undefined); setDiagnostic(undefined); setNotice(undefined); setStatus(undefined);
    try {
      if (pendingDownload.owner !== owner || !projectReady || hasUncapturedSessionReferences(context, asset)) {
        throw new Error(text(language, '工程、模型或会话来源已变化，需重新查询 USGS。', 'The project, model, or session owner changed. Query USGS again.'));
      }
      const fresh = await loadTerrain(requestState.signal);
      if (!isCurrent(requestState.id, requestState.signal)) return;
      if (fresh.revision !== pendingDownload.data.sourceRevision) throw new Error(text(language, '下载文件所属模型修订已变化，不能继续暂存。', 'The model revision owning these downloaded files changed; staging is blocked.'));
      const files: TerrainInputImportSelection[] = pendingDownload.inspection.sources.flatMap(source => {
        const setting = rowSettings[source.id]; const downloadFile = pendingDownload.sourceById.get(source.id);
        if (!source.isValid || !setting?.include) return [];
        if (!downloadFile || source.revision !== downloadFile.sourceRevision) throw new Error(text(language, `地形来源 ${source.path} 已变化，请重新下载。`, `Terrain source ${source.path} changed. Download it again.`));
        return [{ path: source.path, sourceRevision: source.revision, sourceId: source.id,
          rounding: setting.rounding, unitConversion: 'Default', actualScaleFactor: downloadFile.defaultScaleFactor }];
      });
      if (!files.length) throw new Error(text(language, '至少选择一个有效的 USGS 地形来源。', 'Select at least one valid USGS terrain source.'));
      setImportingIds(new Set(files.flatMap(file => {
        const sourceId = file.sourceId;
        const productId = sourceId ? pendingDownload.sourceById.get(sourceId)?.productId : undefined;
        return productId ? [productId] : [];
      })));
      const draftToken = fresh.terrain?.importDraftToken || requestIdentity();
      const appendRequest: TerrainInputAppendRequest = {
        model: modelSaveRequest(context, asset, fresh), files, stitchTiles,
        draftToken, batchToken: requestIdentity(),
      };
      appendRef.current = { owner, request: appendRequest, snapshot: fresh };
      const appendPromise = backend.request<TerrainInputAppendResult>('model.terrainInputAppend', appendRequest, {
        signal: requestState.signal,
        onProgress: value => { if (isCurrent(requestState.id, requestState.signal)) setProgress(operationProgress(value)); },
      });
      let finished = false; void appendPromise.then(() => { finished = true; }, () => { finished = true; });
      while (!finished && capabilities.status && !requestState.signal.aborted) {
        await delay(850, requestState.signal);
        if (finished || !isCurrent(requestState.id, requestState.signal)) break;
        try {
          const polled = await backend.request<TerrainUSGSImportStatus>('model.terrainInputImportStatus', { request: appendRequest }, { signal: requestState.signal });
          if (!isCurrent(requestState.id, requestState.signal)) return;
          if (polled && typeof polled.state === 'string' && Array.isArray(polled.operations)) setStatus(polled);
      } catch (caught) { if (requestState.signal.aborted) throw caught; if (isCurrent(requestState.id, requestState.signal)) setDiagnostic(rawDiagnostic(caught)); }
      }
      const appended = await appendPromise;
      if (!isCurrent(requestState.id, requestState.signal)) return;
      if (!appended || appended.draftToken !== appendRequest.draftToken || appended.batchToken !== appendRequest.batchToken ||
          !Array.isArray(appended.operations) || !appended.snapshot) {
        throw new Error(text(language, '地形暂存回执与请求 token 不匹配。', 'The terrain staging receipt does not match its request tokens.'));
      }
      assertModelSnapshot(appended.snapshot);
      if (appended.snapshot.revision !== fresh.revision || appended.snapshot.terrain?.importDraftToken !== draftToken ||
          typeof appended.snapshot.sourceState !== 'string' || appended.snapshot.sourceState.length === 0) {
        throw new Error(text(language, '地形暂存快照未保留原模型修订、实际来源状态或导入草稿 token。', 'The staged terrain receipt did not preserve the original model revision, actual source state, or import draft token.'));
      }
      setLastSnapshot(appended.snapshot); setStatus({ state: appended.batchStatus, result: appended, operations: appended.operations });
      if (!appended.operations.some(item => item.status === 'Staged')) {
        throw new Error(text(language, '服务端没有将任何转换结果暂存；请检查逐来源诊断。', 'The server staged no terrain conversions. Review per-source diagnostics.'));
      }
      const staged = { owner, snapshot: appended.snapshot, append: appended, download: pendingDownload.data };
      setPendingSave(staged);
      await publishStaged(requestState, staged);
    } catch (caught) {
      if (requestState.signal.aborted) setNotice(text(language, '已请求取消。请以服务端最终批次状态和保存回执为准；没有保存回执不算导入完成。', 'Cancellation was requested. Use the terminal batch state and save receipt; without a save receipt the import is not complete.'));
      else if (isCurrent(requestState.id, requestState.signal)) { setError(message(caught, language)); setDiagnostic(rawDiagnostic(caught)); }
    } finally { appendRef.current = null; setImportingIds(new Set()); if (generation.current === requestState.id) setBusy(null); }
  };

  const cancelBatch = async (): Promise<void> => {
    const active = appendRef.current;
    if (!active || active.owner !== owner || !asset || !capabilities.cancel) return;
    try {
      const request = active.request;
      const result = await backend.request<{ accepted: boolean; state: string; operationID?: string | null }>('model.terrainInputImportCancel', {
        model: { ...modelLoadRequest(context, asset), sourceState: request.model.sourceState ?? null },
        draftToken: request.draftToken, batchToken: request.batchToken,
      });
      setNotice(result?.accepted
        ? text(language, `服务端已接受取消请求（${result.state}），正在等待最终回执。`, `The server accepted cancellation (${result.state}); waiting for the terminal receipt.`)
        : text(language, `服务端未接受取消请求（${result?.state ?? '未知'}）。`, `The server did not accept cancellation (${result?.state ?? 'unknown'}).`));
    } catch (caught) { setNotice(`${text(language, '取消状态未确认。', 'Cancellation is unconfirmed.')} ${message(caught, language)}`); }
  };

  const safeToCleanup = (snapshot: ModelSnapshot, download: TerrainUSGSDownloadData, receipt?: TerrainInputAppendResult): boolean => {
    const paths = new Set(download.files.flatMap(file => [file.filePath, file.previewFilePath]
      .filter((path): path is string => Boolean(path)).map(normalizedPath)));
    const savedInputPaths = snapshot.terrain?.inputs.map(input => input.filename).filter((path): path is string => Boolean(path)) ?? [];
    if (savedInputPaths.some(path => paths.has(normalizedPath(path)))) return false;
    if (receipt) {
      const stagedIds = receipt.operations.filter(item => item.status === 'Staged' && item.outputInputID).map(item => item.outputInputID!);
      const savedIds = new Set(snapshot.terrain?.inputs.map(input => input.id) ?? []);
      return stagedIds.length > 0 && stagedIds.every(id => savedIds.has(id));
    }
    return true;
  };

  const cleanupOwnedFiles = async (): Promise<void> => {
    const record = cleanupRecord;
    if (!cleanupToken || !record || record.token !== cleanupToken || !capabilities.cleanup || !capabilities.load || busy !== null || hasUnsavedDrafts || pendingSave) return;
    if (record.owner !== owner) {
      setNotice(text(language, '清理令牌属于另一个 Terrain 模型。切回原模型后再执行安全清理。', 'The cleanup token belongs to another Terrain model. Return to its original model before cleanup.'));
      return;
    }
    if (!safeToCleanup(record.expected, record.download, record.receipt)) {
      setNotice(text(language, '当前保存快照无法证明不引用 USGS 临时文件；已保留清理令牌。', 'The saved snapshot cannot prove that USGS temporary files are unreferenced. The cleanup token is retained.'));
      return;
    }
    const request = begin(); setBusy('cleanup'); setError(undefined); setDiagnostic(undefined);
    try {
      const fresh = await loadTerrainFor(record.context, record.asset, request.signal);
      if (!isCurrent(request.id, request.signal)) return;
      if (fresh.revision !== record.expected.revision || !safeToCleanup(fresh, record.download, record.receipt)) {
        throw new Error(text(language, 'Terrain 在安全检查后发生变化；未调用 Host 清理。', 'Terrain changed after the safety check; Host cleanup was not called.'));
      }
      const cleanupModel = { ...modelLoadRequest(record.context, record.asset), sourceState: fresh.sourceState ?? modelSourceState(record.asset) ?? null };
      const result = await backend.request<TerrainUSGSCleanupData>('model.terrainUSGSCleanup', { model: cleanupModel, cleanupToken: record.token }, { signal: request.signal });
      if (!isCurrent(request.id, request.signal)) return;
      if (result?.removed) { setCleanupToken(undefined); setCleanupRecord(undefined); setLastDownload(undefined); setPendingDownload(undefined); setNotice(text(language, 'Host 已按清理令牌移除了自有临时文件。', 'The Host removed its owned temporary files using the cleanup token.')); }
      else setNotice(result?.warning || text(language, 'Host 未确认清理完成；令牌已保留，可稍后重试。', 'The Host did not confirm cleanup; the token is retained for retry.'));
    } catch (caught) {
      if (isCurrent(request.id, request.signal)) { setError(message(caught, language)); setDiagnostic(rawDiagnostic(caught)); }
    } finally { if (generation.current === request.id) setBusy(null); }
  };

  const hasStagedImport = Boolean(status?.result?.operations.some(item => item.status === 'Staged') || lastReceipt?.append.operations.some(item => item.status === 'Staged'));
  return <section className="usgs-page" aria-labelledby="usgs-page-title">
    <header className="usgs-page-heading"><div><span className="usgs-eyebrow">USGS · Terrain</span><h2 id="usgs-page-title">{zh ? 'USGS 地形导入' : 'USGS terrain import'}</h2>
      <p>{zh ? '从当前 Terrain 范围查询 USGS 高程产品，并通过 C# Host 导入、暂存与保存。' : 'Query USGS elevation products for the active Terrain extent and import them through the C# Host.'}</p></div>
      <span className={`usgs-connection ${capabilities.query && capabilities.download && capabilities.inspect && capabilities.append && capabilities.save ? 'is-ready' : ''}`}>
        {capabilities.query && capabilities.download && capabilities.inspect && capabilities.append && capabilities.save ? (zh ? 'C# 工作流可用' : 'C# workflow available') : (zh ? 'C# 工作流未完整发布' : 'C# workflow incomplete')}
      </span></header>
    {!asset ? <div className="usgs-callout">{zh ? '请从当前工程选择 Terrain 地形模型。' : 'Select a Terrain model from the active project.'}</div> : <>
      <dl className="usgs-model-card"><div><dt>{zh ? '模型' : 'Model'}</dt><dd>{asset.name}</dd></div><div><dt>{zh ? '模型路径' : 'Model path'}</dt><dd><code>{asset.path}</code></dd></div>
        <div><dt>{zh ? '模型修订' : 'Model revision'}</dt><dd><code>{lastSnapshot?.revision ?? asset.revision ?? '—'}</code></dd></div>
        <div><dt>{zh ? '工程修订' : 'Project revision'}</dt><dd><code>{context.projectRevision ?? '—'}</code></dd></div></dl>
      {(!projectReady || !refsReady) && <div className="usgs-callout is-warning">{!projectReady ? (zh ? '需要当前工程路径、XML 快照、SHA-256 修订和会话。' : 'A project path, XML snapshot, SHA-256 revision, and session are required.') : (zh ? '资产含未捕获的会话外部引用，请从活动地图树重新选择。' : 'This asset has uncaptured session-only references. Select it again from the active map tree.')}</div>}
      {hasUnsavedDrafts && <div className="usgs-callout is-warning">{zh ? '先保存或丢弃其他编辑器草稿，再查询或导入。' : 'Save or discard other editor drafts before querying or importing.'}</div>}
      <div className="usgs-query-row"><label><span>{zh ? '查询范围使用的 Terrain 输入' : 'Terrain input used for query extent'}</span><select value={inputId || query?.snapshot.terrain?.selectedId || ''} disabled={busy !== null || Boolean(pendingSave)} onChange={event => { setInputId(event.target.value); setQuery(undefined); setPendingDownload(undefined); setSelectedIds(new Set()); }}>
        <option value="">{zh ? '使用当前选中输入 / 模型范围' : 'Use selected input / model extent'}</option>{availableInputs.map(input => <option key={input.id} value={input.id}>{input.name} · {input.id}</option>)}
      </select></label><button type="button" className="usgs-primary" disabled={!canQuery} onClick={() => void runQuery()}>
        {busy === 'query' ? (zh ? '正在查询 USGS…' : 'Querying USGS…') : (zh ? '查询 USGS 产品' : 'Query USGS products')}</button>
        {(busy === 'query' || busy === 'download' || busy === 'inspect') && <button type="button" className="usgs-danger" onClick={cancelQueryOrDownload}>{zh ? '请求取消' : 'Request cancellation'}</button>}
        {busy === 'append' && capabilities.cancel && <button type="button" className="usgs-danger" onClick={() => void cancelBatch()}>{zh ? '请求取消导入批次' : 'Cancel import batch'}</button>}
      </div>
      {busy && <div className="usgs-progress" role="status"><strong>{busy === 'query' ? (zh ? '查询中' : 'Querying') : busy === 'download' ? (zh ? '下载中' : 'Downloading') : busy === 'inspect' ? (zh ? '校验文件中' : 'Inspecting files') : busy === 'append' ? (zh ? '服务端转换/暂存中' : 'Converting and staging') : busy === 'save' ? (zh ? '保存中' : 'Saving') : (zh ? 'Host 正在清理自有文件' : 'Host cleanup running')}</strong>
        {progress?.message && <span>{progress.message}</span>}{progress?.fraction != null && <progress max={1} value={progress.fraction} />}</div>}
      {visibleQuery && <USGSTerrainImportDialog language={language} query={visibleQuery.result} selectedIds={selectedIds} importingIds={importingIds} busy={busy !== null}
        onToggle={toggleProduct} onSelectVisible={selectVisible} onDownloadAndImport={() => void downloadAndInspect()} />}
      {pendingDownload && pendingIsCurrent && <section className="usgs-review" aria-labelledby="usgs-review-heading"><header><div><span className="usgs-eyebrow">Host inspection</span><h3 id="usgs-review-heading">{zh ? '导入设置与来源复核' : 'Review import sources and settings'}</h3></div>
        <label className="usgs-stitch"><input type="checkbox" checked={stitchTiles} disabled={busy !== null} onChange={event => setStitchTiles(event.target.checked)} />{zh ? '拼接相邻地形瓦片' : 'Stitch adjacent terrain tiles'}</label></header>
        <p>{zh ? '高程比例使用 USGS 下载回执中的真实值；舍入选项来自 Terrain 检查服务。' : 'Elevation scale uses the value returned by the USGS downloader; rounding choices come from Terrain inspection.'}</p>
        <div className="usgs-source-review">{pendingDownload.inspection.sources.map(source => {
          const setting = rowSettings[source.id]; const file = pendingDownload.sourceById.get(source.id);
          return <article key={source.id} className="usgs-source-row"><label className="usgs-source-enable"><input type="checkbox" checked={Boolean(setting?.include)} disabled={!source.isValid || busy !== null} onChange={event => updateRow(source.id, { include: event.target.checked })} />
            <span><strong>{source.name}</strong><small>{source.path}</small></span></label>
            <span className={source.isValid ? 'usgs-valid' : 'usgs-invalid'}>{source.isValid ? (zh ? '有效 DEM' : 'Valid DEM') : (source.error || (zh ? '无效 DEM' : 'Invalid DEM'))}</span>
            <span>{zh ? '尺寸' : 'Size'}：{source.width ?? '—'} × {source.height ?? '—'}</span><span>{zh ? '波段' : 'Bands'}：{source.bandCount ?? '—'}</span>
            <span>{zh ? 'USGS 高程比例' : 'USGS elevation scale'}：<code>{file?.defaultScaleFactor ?? '—'}</code></span>
            <label><span>{zh ? '舍入' : 'Rounding'}</span><select disabled={!source.isValid || !setting?.include || busy !== null} value={setting?.rounding ?? ''} onChange={event => updateRow(source.id, { rounding: event.target.value })}>
              {pendingDownload.inspection.roundingOptions.map(option => <option key={option} value={option}>{option}</option>)}
            </select></label>
          </article>;
        })}</div>
        <footer><span>{pendingSave && pendingSave.owner === owner
          ? (zh ? '批次已暂存，等待保存回执' : 'Batch staged; awaiting save receipt')
          : (zh ? `待导入 ${Object.values(rowSettings).filter(item => item.include).length} 个来源` : `${Object.values(rowSettings).filter(item => item.include).length} sources selected`)}</span>
          {pendingSave && pendingSave.owner === owner
            ? <button type="button" className="usgs-primary" disabled={busy !== null || hasUnsavedDrafts} onClick={() => void retrySave()}>{busy === 'save' ? (zh ? '保存重试中…' : 'Retrying save…') : (zh ? '重试保存已暂存地形' : 'Retry saving staged terrain')}</button>
            : <button type="button" className="usgs-primary" disabled={!canImport} onClick={() => void runImport()}>{busy === 'append' || busy === 'save' ? (zh ? '导入并保存中…' : 'Importing and saving…') : (zh ? '导入并保存到 Terrain' : 'Import and save to Terrain')}</button>}</footer>
      </section>}
      {status && <section className="usgs-batch-status"><h3>{zh ? `服务端批次：${status.state}` : `Server batch: ${status.state}`}</h3>{status.diagnostic && <details><summary>{zh ? '批次诊断' : 'Batch diagnostic'}</summary><pre>{status.diagnostic}</pre></details>}
        <ul>{status.operations.map(operation => <li key={operation.operationID}><strong>{operation.status}</strong><span>{operation.outputName || operation.sourcePaths.join(' · ')}</span>{operation.diagnostic && <details><summary>{zh ? '来源诊断' : 'Source diagnostic'}</summary><pre>{operation.diagnostic}</pre></details>}</li>)}</ul>
        {lastReceipt && <div className="usgs-receipt"><strong>{zh ? '保存回执已确认' : 'Save receipt confirmed'}</strong><p>{zh ? '新 Terrain 修订' : 'New Terrain revision'}：<code>{lastReceipt.saved.revision}</code></p>{hasStagedImport && <p>{zh ? '转换状态和保存修订均来自 C# 服务。' : 'Conversion state and saved revision came from the C# server.'}</p>}</div>}
      </section>}
      {cleanupToken && <section className="usgs-cleanup"><p>{cleanupRecord?.owner !== owner
        ? (zh ? '清理令牌属于另一个 Terrain 模型。切回原模型以保留并完成清理。' : 'The cleanup token belongs to another Terrain model. Return to that model to complete cleanup.')
        : (zh ? 'Host 临时来源文件仍由清理令牌持有。只有确认保存模型未引用这些路径后，才调用 Host 清理。' : 'Host temporary source files are held by a cleanup token. Cleanup is called only after verifying that the saved model does not reference those paths.')}</p>
        <button type="button" disabled={busy !== null || !capabilities.cleanup || !capabilities.load || hasUnsavedDrafts || Boolean(pendingSave) || !lastDownload || cleanupRecord?.owner !== owner} onClick={() => void cleanupOwnedFiles()}>{busy === 'cleanup' ? (zh ? '清理中…' : 'Cleaning…') : (zh ? '安全检查后清理临时文件' : 'Verify and clean temporary files')}</button></section>}
      {notice && <div className="usgs-callout" role="status">{notice}</div>}
      {error && <div className="usgs-callout is-error" role="alert"><strong>{error}</strong></div>}
      {diagnostic && <details className="usgs-diagnostic"><summary>{zh ? '服务端原始诊断' : 'Raw server diagnostic'}</summary><pre>{diagnostic}</pre></details>}
      {!capabilities.download && <div className="usgs-callout is-warning">{zh ? 'C# 服务未发布 USGS 下载操作；下载按钮不可用。' : 'The C# server has not advertised USGS download; the action is unavailable.'}</div>}
    </>}
  </section>;
}
