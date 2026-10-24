import { useEffect, useMemo, useRef, useState } from 'react';
import type { FeaturePageProps } from '../../shared/workbench';
import { message, text } from '../../shared/workbench';
import type { ModelSnapshot } from '../editors/model';
import {
  assertModelSnapshot, hasUncapturedSessionReferences, modelLoadRequest, modelSaveRequest,
  modelSourceState, requestIdentity, sourceOwnerKey, updatedModelContext,
} from './coreImportWorkflow';
import type {
  TerrainInputAppendRequest, TerrainInputAppendResult, TerrainInputImportInspection,
  TerrainInputImportSelection, TerrainInputOperationResult,
} from './coreImportWorkflow';
import { TerrainImporterDialog, type TerrainImportRowSettings } from './TerrainImporterDialog';
import './coreImportWorkflow.css';

type Props = FeaturePageProps;
interface ImportStatus {
  state: string;
  result?: TerrainInputAppendResult | null;
  operations: TerrainInputOperationResult[];
  resultUnavailableCode?: string | null;
  diagnostic?: string | null;
}
interface PendingSave {
  owner: string;
  snapshot: ModelSnapshot;
  append: TerrainInputAppendResult;
}

function delay(milliseconds: number, signal: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    if (signal.aborted) { reject(new DOMException('Request cancelled.', 'AbortError')); return; }
    const timer = window.setTimeout(() => { signal.removeEventListener('abort', abort); resolve(); }, milliseconds);
    const abort = (): void => { window.clearTimeout(timer); reject(new DOMException('Request cancelled.', 'AbortError')); };
    signal.addEventListener('abort', abort, { once: true });
  });
}

function modelContextReady(context: FeaturePageProps['context']): boolean {
  return Boolean(context.projectPath && context.projectXml !== null && context.projectSessionID);
}

function rawError(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

export function TerrainImporter({ language, context, backend, onContextChange, onContextCommit, onNavigate, hasUnsavedDrafts }: Props) {
  const asset = context.selectedAsset?.kind === 'Terrain' ? context.selectedAsset : null;
  const owner = sourceOwnerKey(context);
  const ownerRef = useRef(owner); ownerRef.current = owner;
  const generation = useRef(0);
  const controller = useRef<AbortController | null>(null);
  const appendRef = useRef<{ owner: string; request: TerrainInputAppendRequest } | null>(null);
  const acceptedOwner = useRef<string | null>(null);
  const [paths, setPaths] = useState('');
  const [snapshot, setSnapshot] = useState<ModelSnapshot>();
  const [inspection, setInspection] = useState<TerrainInputImportInspection>();
  const [settings, setSettings] = useState<Record<string, TerrainImportRowSettings>>({});
  const [stitchTiles, setStitchTiles] = useState(true);
  const [pendingSave, setPendingSave] = useState<PendingSave>();
  const [appendReceipt, setAppendReceipt] = useState<TerrainInputAppendResult>();
  const [savedRevision, setSavedRevision] = useState<string>();
  const [status, setStatus] = useState<ImportStatus>();
  const [busy, setBusy] = useState<'inspect' | 'append' | 'save' | null>(null);
  const [error, setError] = useState<string>();
  const [rawDiagnostic, setRawDiagnostic] = useState<string>();
  const [cancelMessage, setCancelMessage] = useState<string>();

  const supportsLoad = backend.supports('model.load');
  const supportsInspect = backend.supports('model.terrainInputImportInspect');
  const supportsAppend = backend.supports('model.terrainInputAppend');
  const supportsStatus = backend.supports('model.terrainInputImportStatus');
  const supportsCancel = backend.supports('model.terrainInputImportCancel');
  const supportsSave = backend.supports('model.save');
  const projectReady = modelContextReady(context);
  const sessionReferencesReady = !hasUncapturedSessionReferences(context, asset);
  const filePaths = useMemo(() => [...new Set(paths.split(/\r?\n/).map(value => value.trim()).filter(Boolean))], [paths]);
  const canInspect = Boolean(asset && context.projectPath && context.projectXml !== null && sessionReferencesReady && filePaths.length > 0 && supportsLoad && supportsInspect && !hasUnsavedDrafts);
  const canAppend = Boolean(inspection && snapshot && projectReady && sessionReferencesReady && supportsAppend && supportsSave && !hasUnsavedDrafts && busy === null);

  useEffect(() => {
    if (acceptedOwner.current === owner) { acceptedOwner.current = null; return; }
    controller.current?.abort();
    generation.current += 1;
    appendRef.current = null;
    setSnapshot(undefined);
    setInspection(undefined);
    setSettings({});
    setPendingSave(undefined);
    setAppendReceipt(undefined);
    setSavedRevision(undefined);
    setStatus(undefined);
    setBusy(null);
    setError(undefined);
    setRawDiagnostic(undefined);
    setCancelMessage(undefined);
  }, [owner]);

  useEffect(() => () => {
    generation.current += 1;
    controller.current?.abort();
  }, []);

  const isCurrent = (id: number, signal: AbortSignal): boolean =>
    generation.current === id && ownerRef.current === owner && !signal.aborted;

  const begin = (): { id: number; signal: AbortSignal } => {
    controller.current?.abort();
    const next = new AbortController();
    controller.current = next;
    const id = ++generation.current;
    return { id, signal: next.signal };
  };

  const loadCurrentTerrain = async (signal: AbortSignal): Promise<ModelSnapshot> => {
    if (!asset || !context.projectPath || context.projectXml === null) {
      throw new Error(text(language, '请从当前工程选择一个 Terrain 地形模型。', 'Select a Terrain model from the current project.'));
    }
    if (!backend.supports('model.load')) throw new Error(text(language, 'C# 服务未发布 model.load。', 'The C# server has not advertised model.load.'));
    const loaded = await backend.request<ModelSnapshot>('model.load', modelLoadRequest(context, asset), { signal });
    assertModelSnapshot(loaded);
    if (!loaded.terrain) throw new Error(text(language, '服务端载入的模型没有 Terrain 输入快照。', 'The loaded model has no Terrain input snapshot.'));
    return loaded;
  };

  const inspect = async (): Promise<void> => {
    if (!canInspect) return;
    const request = begin();
    setBusy('inspect'); setError(undefined); setRawDiagnostic(undefined); setCancelMessage(undefined);
    setInspection(undefined); setAppendReceipt(undefined); setSavedRevision(undefined); setStatus(undefined);
    try {
      const loaded = await loadCurrentTerrain(request.signal);
      if (!isCurrent(request.id, request.signal)) return;
      const model = modelSaveRequest(context, asset!, loaded);
      const result = await backend.request<TerrainInputImportInspection>('model.terrainInputImportInspect', {
        model,
        filePaths,
      }, { signal: request.signal });
      if (!isCurrent(request.id, request.signal)) return;
      if (!result || !Array.isArray(result.sources) || result.sources.length === 0 ||
          !Array.isArray(result.roundingOptions) || !Array.isArray(result.unitConversionOptions) ||
          !result.roundingOptions.length || !result.unitConversionOptions.length ||
          result.sources.some(source => !source || typeof source.id !== 'string' || typeof source.path !== 'string' ||
            typeof source.revision !== 'string' || typeof source.isValid !== 'boolean')) {
        throw new Error(text(language, '地形检查接口返回了不完整的来源或设置选项。', 'Terrain inspection returned incomplete sources or settings.'));
      }
      setSnapshot(loaded);
      setInspection(result);
      setStitchTiles(result.stitchTilesDefault);
      setSettings(Object.fromEntries(result.sources.map(source => [source.id, {
        include: source.isValid,
        rounding: result.roundingOptions[0]!,
        unitConversion: result.unitConversionOptions.includes('Default') ? 'Default' : result.unitConversionOptions[0]!,
        customScaleFactor: '',
      }])));
    } catch (caught) {
      if (request.signal.aborted) setCancelMessage(text(language, '地形检查已取消。', 'Terrain inspection was cancelled.'));
      else if (isCurrent(request.id, request.signal)) { setError(message(caught, language)); setRawDiagnostic(rawError(caught)); }
    } finally {
      if (generation.current === request.id) setBusy(null);
    }
  };

  const importAndSave = async (): Promise<void> => {
    if (!canAppend || !inspection || !snapshot || !asset) return;
    const requestState = begin();
    setBusy('append'); setError(undefined); setRawDiagnostic(undefined); setCancelMessage(undefined);
    setStatus(undefined); setAppendReceipt(undefined); setSavedRevision(undefined); setPendingSave(undefined);
    try {
      if (!modelContextReady(context)) throw new Error(text(language, '当前工程路径、XML、会话或修订已变化，请重新检查。', 'The active project path, XML, session, or revision changed. Inspect again.'));
      if (hasUncapturedSessionReferences(context, asset)) throw new Error(text(language, '该资产依赖会话外部引用，但未携带匹配的核心图层捕获；请从当前地图树重新选择。', 'This asset depends on session-only references without an exact core-layer capture. Select it again from the active map tree.'));
      if (snapshot.revision !== asset.revision && asset.revision) throw new Error(text(language, '地形文件在检查后已发生修订变化，请重新载入并检查。', 'The terrain file revision changed after inspection. Reload and inspect again.'));
      const selections: TerrainInputImportSelection[] = inspection.sources.flatMap(source => {
        const setting = settings[source.id];
        if (!source.isValid || !setting?.include) return [];
        const selection: TerrainInputImportSelection = {
          path: source.path,
          sourceRevision: source.revision,
          rounding: setting.rounding,
          unitConversion: setting.unitConversion,
          sourceId: source.id,
          ...(setting.unitConversion === 'Custom' ? { customScaleFactor: Number(setting.customScaleFactor) } : {}),
        };
        return [selection];
      });
      if (selections.length === 0) throw new Error(text(language, '至少选择一个有效地形来源。', 'Select at least one valid terrain source.'));
      if (selections.some(selection => selection.unitConversion === 'Custom' &&
        (!Number.isFinite(selection.customScaleFactor) || Number(selection.customScaleFactor) <= 0))) {
        throw new Error(text(language, '自定义换算比例必须为有限正数。', 'Custom conversion factors must be finite positive numbers.'));
      }
      const draftToken = snapshot.terrain?.importDraftToken || requestIdentity();
      const appendRequest: TerrainInputAppendRequest = {
        model: modelSaveRequest(context, asset, snapshot),
        files: selections,
        stitchTiles,
        draftToken,
        batchToken: requestIdentity(),
      };
      appendRef.current = { owner, request: appendRequest };
      const appendPromise = backend.request<TerrainInputAppendResult>('model.terrainInputAppend', appendRequest, {
        signal: requestState.signal,
        onProgress: value => {
          if (isCurrent(requestState.id, requestState.signal)) {
            const raw = value && typeof value === 'object' ? value as Record<string, unknown> : {};
            setStatus(current => ({ state: 'Running', operations: current?.operations ?? [], diagnostic: typeof raw.message === 'string' ? raw.message : undefined }));
          }
        },
      });
      let appendFinished = false;
      void appendPromise.then(() => { appendFinished = true; }, () => { appendFinished = true; });
      while (!appendFinished && supportsStatus && !requestState.signal.aborted) {
        await delay(850, requestState.signal);
        if (appendFinished || !isCurrent(requestState.id, requestState.signal)) break;
        try {
          const polled = await backend.request<ImportStatus>('model.terrainInputImportStatus', { request: appendRequest }, { signal: requestState.signal });
          if (!isCurrent(requestState.id, requestState.signal)) return;
          if (polled && typeof polled.state === 'string' && Array.isArray(polled.operations)) setStatus(polled);
        } catch (statusError) {
          if (requestState.signal.aborted) throw statusError;
          if (isCurrent(requestState.id, requestState.signal)) setRawDiagnostic(rawError(statusError));
        }
      }
      const result = await appendPromise;
      if (!isCurrent(requestState.id, requestState.signal)) return;
      if (!result || result.draftToken !== appendRequest.draftToken || result.batchToken !== appendRequest.batchToken ||
          !Array.isArray(result.operations) || !result.snapshot) {
        throw new Error(text(language, '地形批次回执与请求 token 不匹配。', 'The terrain batch receipt does not match its request tokens.'));
      }
      assertModelSnapshot(result.snapshot);
      if (result.snapshot.revision !== snapshot.revision || result.snapshot.terrain?.importDraftToken !== draftToken) {
        throw new Error(text(language, '服务端地形快照没有保留来源修订或导入草稿 token。', 'The returned terrain snapshot did not preserve the source revision or import draft token.'));
      }
      setAppendReceipt(result);
      setSnapshot(result.snapshot);
      const staged = result.operations.some(operation => operation.status === 'Staged');
      if (!staged) {
        setStatus({ state: result.batchStatus, result, operations: result.operations });
        setError(text(language, '没有地形转换结果进入待保存状态。请检查逐项诊断。', 'No terrain conversion reached the staged state. Review the per-source diagnostics.'));
        return;
      }
      if (!backend.supports('model.save')) {
        setPendingSave({ owner, snapshot: result.snapshot, append: result });
        setError(text(language, '地形已转换并暂存，但当前 C# 服务未发布 model.save；尚未确认写入地形 HDF。', 'Terrain conversion was staged, but model.save is unavailable; the terrain HDF has not been confirmed as written.'));
        return;
      }
      setBusy('save');
      const saveRequest = modelSaveRequest(context, asset, result.snapshot);
      const saved = await backend.request<ModelSnapshot>('model.save', saveRequest, { signal: requestState.signal });
      if (!isCurrent(requestState.id, requestState.signal)) return;
      await acceptSavedReceipt(saved, result, saveRequest, requestState.id, requestState.signal);
    } catch (caught) {
      if (requestState.signal.aborted) setCancelMessage(text(language,
        '已请求取消。请以服务端最终状态及回执为准；未收到保存回执时不视为导入完成。',
        'Cancellation was requested. Check the server’s terminal state and receipt; without a save receipt, the import is not complete.'));
      else if (isCurrent(requestState.id, requestState.signal)) { setError(message(caught, language)); setRawDiagnostic(rawError(caught)); }
    } finally {
      if (generation.current === requestState.id) setBusy(null);
    }
  };

  const acceptSavedReceipt = async (value: unknown, append: TerrainInputAppendResult,
    saveRequest: Record<string, unknown>, requestID: number, signal: AbortSignal): Promise<void> => {
    assertModelSnapshot(value);
    const saved = value as ModelSnapshot;
    if (!saved.terrain || saved.revision === append.snapshot.revision || !saved.revision.trim()) {
      throw new Error(text(language, '地形保存回执未确认新的地形模型修订。', 'The save receipt did not confirm a new terrain model revision.'));
    }
    if (!isCurrent(requestID, signal) || !asset) return;
    if (saveRequest.assetPath !== asset.path || saveRequest.kind !== asset.kind ||
        saveRequest.projectPath !== context.projectPath || saveRequest.projectXml !== context.projectXml ||
        saveRequest.revision !== append.snapshot.revision ||
        saveRequest.sourceState !== append.snapshot.sourceState) {
      throw new Error(text(language,
        '地形保存请求的工程、模型路径、暂存修订或来源状态与当前暂存批次不匹配。',
        'The terrain save request project, model path, staged revision, or source state does not match the staged batch.'));
    }
    const nextContext = updatedModelContext(context, asset, saved);
    const accepted = onContextCommit ? onContextCommit(context, nextContext) : true;
    if (!accepted) {
      setError(text(language, '服务端已返回保存回执，但当前工作台拒绝了过期上下文；请重新载入地形模型。', 'The server returned a save receipt, but the workbench rejected the stale context. Reload the terrain model.'));
      return;
    }
    acceptedOwner.current = sourceOwnerKey(nextContext);
    if (!onContextCommit) onContextChange(nextContext);
    setSavedRevision(saved.revision);
    setSnapshot(saved);
    setPendingSave(undefined);
    setStatus({ state: append.batchStatus, result: append, operations: append.operations });
    setAppendReceipt(append);
    appendRef.current = null;
    setError(undefined);
    setRawDiagnostic(undefined);
  };

  const retrySave = async (): Promise<void> => {
    if (!pendingSave || pendingSave.owner !== owner || !asset || busy !== null) return;
    const requestState = begin();
    setBusy('save'); setError(undefined); setRawDiagnostic(undefined);
    try {
      if (!modelContextReady(context) || hasUncapturedSessionReferences(context, asset)) throw new Error(text(language, '当前工程会话或外部引用捕获已变化，不能继续保存暂存地形。', 'The project session or external-reference capture changed; staged terrain cannot be saved.'));
      const saveRequest = modelSaveRequest(context, asset, pendingSave.snapshot);
      const saved = await backend.request<ModelSnapshot>('model.save', saveRequest, { signal: requestState.signal });
      if (!isCurrent(requestState.id, requestState.signal)) return;
      await acceptSavedReceipt(saved, pendingSave.append, saveRequest, requestState.id, requestState.signal);
    } catch (caught) {
      if (requestState.signal.aborted) setCancelMessage(text(language, '保存请求已取消；地形仍处于待确认状态。', 'The save request was cancelled; terrain remains unconfirmed.'));
      else if (isCurrent(requestState.id, requestState.signal)) { setError(message(caught, language)); setRawDiagnostic(rawError(caught)); }
    } finally {
      if (generation.current === requestState.id) setBusy(null);
    }
  };

  const cancelBatch = async (): Promise<void> => {
    const active = appendRef.current;
    if (!active || active.owner !== owner || !supportsCancel || !asset || !snapshot) return;
    try {
      const model = {
        ...modelLoadRequest(context, asset),
        sourceState: snapshot.sourceState ?? modelSourceState(asset) ?? null,
      };
      const result = await backend.request<{ accepted: boolean; state: string; operationID?: string | null }>(
        'model.terrainInputImportCancel', {
          model,
          draftToken: active.request.draftToken,
          batchToken: active.request.batchToken,
        },
      );
      setCancelMessage(result?.accepted
        ? text(language, `服务端已接受取消请求（${result.state}）。正在等待批次最终回执。`, `The server accepted cancellation (${result.state}). Waiting for the batch receipt.`)
        : text(language, `服务端未接受取消请求（${result?.state ?? 'unknown'}）。`, `The server did not accept cancellation (${result?.state ?? 'unknown'}).`));
    } catch (caught) {
      setCancelMessage(`${text(language, '取消状态未确认。', 'Cancellation status is unconfirmed.')} ${rawError(caught)}`);
    }
  };

  const updateSettings = (sourceID: string, patch: Partial<TerrainImportRowSettings>): void => {
    setSettings(current => current[sourceID] ? { ...current, [sourceID]: { ...current[sourceID]!, ...patch } } : current);
  };

  const requestReady = modelContextReady(context);

  return (
    <section className="ci-panel" aria-labelledby="terrain-importer-title">
      <header className="ci-import-heading">
        <div><span className="ci-kicker">TerrainImporter · TerrainImporterDialog</span><h3 id="terrain-importer-title">{text(language, '地形栅格导入', 'Terrain raster import')}</h3></div>
        <span className={`tools-runtime-pill ${supportsInspect && supportsAppend ? 'is-connected' : 'is-unavailable'}`}>
          {supportsInspect && supportsAppend ? text(language, 'C# 操作已发布', 'C# operations advertised') : text(language, 'C# 操作未就绪', 'C# operations unavailable')}
        </span>
      </header>
      {!asset ? <div className="ci-warning" role="status">{text(language, '请从工程浏览器选择 Terrain 地形模型后再打开此导入器。', 'Select a Terrain model in the project browser to use this importer.')}</div> : <>
        <dl className="ci-details"><div><dt>{text(language, '地形模型', 'Terrain model')}</dt><dd>{asset.name}</dd></div><div><dt>{text(language, '模型路径', 'Model path')}</dt><dd>{asset.path}</dd></div><div><dt>{text(language, '模型修订', 'Model revision')}</dt><dd>{snapshot?.revision ?? asset.revision ?? '—'}</dd></div></dl>
        <label className="ci-field"><span>{text(language, '服务端可见输入路径（每行一个 .tif/.tiff 或 Terrain .h5）', 'Server-visible input paths (one .tif/.tiff or Terrain .h5 per line)')}</span>
          <textarea value={paths} disabled={busy !== null} spellCheck={false} onChange={event => { setPaths(event.target.value); setInspection(undefined); setAppendReceipt(undefined); setPendingSave(undefined); }} placeholder="/data/elevation.tif" /></label>
        <p className="ci-help">{text(language, '当前 Host 操作接收文件系统路径。Web 本地文件没有上传接入；所填路径由 C# 服务端读取。', 'The Host operation accepts file-system paths. Local browser files are not uploaded; the C# server reads the paths entered here.')}</p>
        {hasUnsavedDrafts && <div className="ci-warning" role="status">{text(language, '请先保存或丢弃其他编辑器草稿，再检查或导入地形。', 'Save or discard other editor drafts before inspecting or importing terrain.')}</div>}
        {!requestReady && <div className="ci-warning" role="status">{text(language, '需要当前工程路径、XML 快照、会话与工程 SHA。', 'The current project path, XML snapshot, session, and project SHA are required.')}</div>}
        {!sessionReferencesReady && <div className="ci-warning" role="status">{text(language,
          '该 Terrain 资产携带会话外部引用，但当前上下文没有精确匹配的捕获。请从当前地图树重新选择资产。',
          'This Terrain asset carries session-only references without an exact matching capture. Select the asset again from the active map tree.')}</div>}
        <div className="ci-actions">
          <button type="button" className="ci-button ci-primary" disabled={!canInspect || busy !== null} onClick={() => void inspect()}>{busy === 'inspect' ? text(language, '载入并检查中…', 'Loading and inspecting…') : text(language, '载入并检查来源', 'Load and inspect sources')}</button>
          {busy === 'append' && supportsCancel && <button type="button" className="ci-button ci-danger" onClick={() => void cancelBatch()}>{text(language, '请求取消本批次', 'Cancel this batch')}</button>}
          {pendingSave && <button type="button" className="ci-button ci-primary" disabled={busy !== null || !projectReady || !sessionReferencesReady} onClick={() => void retrySave()}>{busy === 'save' ? text(language, '保存中…', 'Saving…') : text(language, '重试保存暂存地形', 'Retry saving staged terrain')}</button>}
          {savedRevision && <button type="button" className="ci-button" onClick={() => onNavigate('editors')}>{text(language, '查看地形模型', 'Open terrain model')}</button>}
        </div>
        {busy && <div className="ci-warning" role="status">{busy === 'inspect' ? text(language, '正在从 C# 服务读取当前 Terrain 与来源属性。', 'Reading the current Terrain and source properties from C#.') : busy === 'append' ? text(language, '转换任务正在服务端运行；状态轮询使用同一批次 token。', 'Conversion is running on the server; polling uses the same batch token.') : text(language, '正在把暂存地形写入原模型并等待保存回执。', 'Publishing staged terrain into the model and waiting for the save receipt.')}</div>}
        {error && <div className="ci-error" role="alert"><strong>{error}</strong>{rawDiagnostic && <details><summary>{text(language, '原始诊断', 'Raw diagnostic')}</summary><pre>{rawDiagnostic}</pre></details>}</div>}
        {cancelMessage && <div className="ci-warning" role="status">{cancelMessage}</div>}
        {inspection && <TerrainImporterDialog language={language} inspection={inspection} settings={settings} stitchTiles={stitchTiles}
          busy={busy !== null} canImport={canAppend} onSettingsChange={updateSettings} onStitchTilesChange={setStitchTiles} onImport={() => void importAndSave()} />}
        {status && <section className="ci-result" aria-live="polite">
          <div className="ci-result-heading"><span className={status.state === 'Running' ? 'ci-state-dot ci-running' : 'ci-state-dot'} /><strong>{text(language, `服务端批次状态：${status.state}`, `Server batch state: ${status.state}`)}</strong><span className="ci-chip">{appendReceipt?.mode ?? '—'}</span></div>
          {status.diagnostic && <pre>{status.diagnostic}</pre>}
          <ul className="ci-operation-list">{status.operations.map(operation => <li key={operation.operationID}>
            <strong>{operation.status}</strong><span>{operation.outputName || operation.sourcePaths.join(' · ')}</span>
            {operation.diagnostic && <pre>{operation.diagnostic}</pre>}
          </li>)}</ul>
          {savedRevision && <div className="ci-success"><strong>{text(language, '地形已由保存回执确认', 'Terrain confirmed by save receipt')}</strong><div>{text(language, '新模型 SHA-256', 'New model SHA-256')}: <code>{savedRevision}</code></div><p>{text(language, '转换批次和保存回执均来自 C# 服务。', 'Both the conversion batch and save receipt came from C#.')}</p></div>}
        </section>}
        {rawDiagnostic && !error && <details className="ci-raw"><summary>{text(language, '最近的原始诊断', 'Latest raw diagnostic')}</summary><pre>{rawDiagnostic}</pre></details>}
      </>}
    </section>
  );
}
