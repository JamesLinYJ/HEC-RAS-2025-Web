import { useEffect, useMemo, useRef, useState } from 'react';
import type { AssetReference, BackendGateway, BackendTransportState, Language, WorkbenchContext } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import type { ModelSnapshot } from '../editors/model';
import {
  assertModelSnapshot,
  exactCoreLayerContext,
  hasUncapturedSessionReferences,
  isSha256,
  modelLoadRequest,
  modelSaveRequest,
  modelSourceState,
  type WorkspaceFileInspection,
} from '../tools/coreImportWorkflow';
import { isAbsoluteServerPath } from './ProjectTypes';
import { isSameServerPath } from './ExternalConnectionWorkflow';
import { isProjectTreeSHA256 } from './ProjectTreeModelActions';
import type { CatalogTreeNode } from './CatalogTreeSnapshot';

interface TerrainRasterExportReceipt {
  state: string;
  outputPath: string | null;
  sourcePath: string;
  sourceRevision: string;
  cleanupWarning?: string | null;
}

interface ExportProgress {
  fraction?: number;
  taskName?: string;
  message?: string;
}

type ExportPhase = 'preparing' | 'exporting' | 'verifying' | 'completed' | 'cancelled' | 'unconfirmed' | 'failed' | 'unverified';

interface ExportViewState {
  owner: string;
  phase: ExportPhase;
  message?: string;
  progress?: ExportProgress;
  cancelRequested?: boolean;
  transport?: BackendTransportState;
  operationId?: string;
  outputPath?: string;
  cleanupWarning?: string | null;
  receipt?: {
    outputPath: string;
    outputRevision: string;
    outputLength: number;
    sourcePath: string;
    sourceRevision: string;
  };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function parseInspection(value: unknown, requestedPath: string): WorkspaceFileInspection | null {
  if (!isRecord(value) || typeof value.sourcePath !== 'string' ||
      !isSameServerPath(value.sourcePath, requestedPath) || typeof value.canonicalPath !== 'string' ||
      !isAbsoluteServerPath(value.canonicalPath) || !isProjectTreeSHA256(value.revision) ||
      !Number.isSafeInteger(value.length) || (value.length as number) <= 0) return null;
  return {
    sourcePath: value.sourcePath,
    canonicalPath: value.canonicalPath,
    revision: value.revision,
    length: value.length as number,
  };
}

function parseExportReceipt(value: unknown): TerrainRasterExportReceipt | null {
  if (!isRecord(value) || typeof value.state !== 'string' ||
      !(typeof value.outputPath === 'string' || value.outputPath === null) ||
      typeof value.sourcePath !== 'string' || typeof value.sourceRevision !== 'string' ||
      (value.cleanupWarning !== undefined && value.cleanupWarning !== null && typeof value.cleanupWarning !== 'string')) return null;
  return {
    state: value.state,
    outputPath: value.outputPath,
    sourcePath: value.sourcePath,
    sourceRevision: value.sourceRevision,
    cleanupWarning: typeof value.cleanupWarning === 'string' ? value.cleanupWarning : null,
  };
}

function progressFrom(value: unknown): ExportProgress | undefined {
  if (!isRecord(value)) return undefined;
  const fraction = typeof value.fraction === 'number' && Number.isFinite(value.fraction)
    ? Math.max(0, Math.min(1, value.fraction)) : undefined;
  const taskName = typeof value.taskName === 'string' ? value.taskName : undefined;
  const message = typeof value.message === 'string' ? value.message : undefined;
  if (fraction === undefined && taskName === undefined && message === undefined) return undefined;
  return { fraction, taskName, message };
}

function isAbortError(value: unknown): boolean {
  return isRecord(value) && value.name === 'AbortError';
}

function errorText(error: unknown, language: Language): string {
  if (error instanceof Error && error.message) return error.message;
  return text(language, '操作失败，未能确认导出结果。', 'The operation failed and its export result could not be confirmed.');
}

function currentOwner(context: WorkbenchContext, node: CatalogTreeNode): string {
  const core = exactCoreLayerContext(context, requestAsset(context, node));
  return JSON.stringify([
    context.projectSessionID,
    context.projectPath,
    context.projectRevision,
    context.projectXml,
    core?.identityToken ?? core?.IdentityToken ?? null,
    node.id,
    node.sourceClass,
    node.assetID ?? null,
    context.selectedAsset?.path ?? null,
    context.selectedAsset?.kind ?? null,
    context.selectedAsset?.revision ?? null,
    modelSourceState(context.selectedAsset),
    modelSourceState(requestAsset(context, node)),
  ]);
}

function requestAsset(context: WorkbenchContext, node: CatalogTreeNode): AssetReference {
  const selected = context.selectedAsset;
  const selectedMatches = selected?.kind === 'Terrain' && !!node.assetID && isSameServerPath(selected.path, node.assetID);
  return {
    ...(selectedMatches && selected ? selected : {}),
    path: node.assetID ?? '',
    kind: 'Terrain',
    name: node.name,
    locator: selectedMatches ? selected?.locator : node.locator ?? undefined,
  };
}

function requireGuid(value: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value);
}

export interface TerrainRasterExportPanelProps {
  language: Language;
  backend: BackendGateway;
  context: WorkbenchContext;
  node: CatalogTreeNode;
  hasUnsavedDrafts?: boolean;
}

/** Exports only the captured, saved Terrain model to a Host-selected server-side TIFF path. */
export function TerrainRasterExportPanel({ language, backend, context, node, hasUnsavedDrafts }: TerrainRasterExportPanelProps) {
  const owner = useMemo(() => currentOwner(context, node), [context, node]);
  const ownerRef = useRef(owner);
  ownerRef.current = owner;
  const generation = useRef(0);
  const active = useRef<{ owner: string; controller: AbortController } | null>(null);
  const transportOperationID = useRef<string | null>(null);
  const [cellSize, setCellSize] = useState('5');
  const [viewState, setViewState] = useState<ExportViewState | null>(null);
  const currentState = viewState?.owner === owner ? viewState : null;
  const isBusy = currentState?.phase === 'preparing' || currentState?.phase === 'exporting' || currentState?.phase === 'verifying';
  const parsedCellSize = Number(cellSize.trim());

  useEffect(() => () => {
    if (active.current?.owner === owner) {
      generation.current++;
      active.current.controller.abort('Terrain catalog selection changed.');
      active.current = null;
    }
  }, [owner]);

  const blockedReason = useMemo(() => {
    if (node.sourceClass !== 'ObservableTerrain' || !node.assetID || !isAbsoluteServerPath(node.assetID)) {
      return text(language, '当前目录项没有提供有效的 Terrain 服务器路径。', 'The selected catalog entry has no valid server-side Terrain path.');
    }
    if (!context.projectPath || !isAbsoluteServerPath(context.projectPath) || !context.projectXml ||
        !isProjectTreeSHA256(context.projectRevision) || !requireGuid(context.projectSessionID)) {
      return text(language, '需要已打开且具有有效修订的工程会话。', 'An open project session with a valid revision is required.');
    }
    if (hasUnsavedDrafts !== false) {
      return hasUnsavedDrafts
        ? text(language, '存在未保存的编辑草稿；请先保存或处理草稿，再导出已保存的地形。', 'Unsaved editor drafts exist. Save or resolve them before exporting the saved Terrain.')
        : text(language, '当前目录未提供草稿状态，无法确认导出源是已保存版本。', 'The catalog has not provided draft status, so the saved Terrain source cannot be confirmed.');
    }
    const asset = requestAsset(context, node);
    if (hasUncapturedSessionReferences(context, asset)) {
      return text(language, '当前工程包含尚未捕获的会话专属引用；请重新加载工程后再导出。', 'The project has uncaptured session-only references. Reload the project before exporting.');
    }
    if (context.coreLayerContext != null && !exactCoreLayerContext(context, asset)) {
      return text(language, '当前核心图层身份与工程会话不匹配。', 'The captured core-layer identity does not match the project session.');
    }
    if (!backend.supports('workspace.file.inspect') || !backend.supports('model.load') || !backend.supports('terrain.raster.export')) {
      return text(language, '当前服务未同时提供文件检查、Terrain 读取和栅格导出能力。', 'The active service has not advertised file inspection, Terrain loading, and raster export.');
    }
    if (!Number.isFinite(parsedCellSize) || parsedCellSize <= 0) {
      return text(language, '像元尺寸必须是大于 0 的有限数值。', 'Cell size must be a positive finite number.');
    }
    return null;
  }, [backend, context, hasUnsavedDrafts, language, node, parsedCellSize]);

  const execute = async (): Promise<void> => {
    if (blockedReason || isBusy || !node.assetID || !context.projectPath || !context.projectXml) return;
    const asset = requestAsset(context, node);
    const runOwner = owner;
    const runGeneration = ++generation.current;
    const controller = new AbortController();
    active.current?.controller.abort('A newer Terrain export replaced this operation.');
    active.current = { owner: runOwner, controller };
    transportOperationID.current = null;
    let exportSubmitted = false;
    let terminalResultReceived = false;
    let completedReceipt: TerrainRasterExportReceipt | null = null;
    const isCurrent = (): boolean => runGeneration === generation.current && ownerRef.current === runOwner;
    const update = (next: Partial<ExportViewState>): void => {
      if (!isCurrent()) return;
      setViewState((previous) => ({ ...(previous?.owner === runOwner ? previous : { owner: runOwner, phase: 'preparing' as const }), ...next }));
    };

    setViewState({ owner: runOwner, phase: 'preparing' });
    try {
      if (hasUnsavedDrafts !== false) throw new Error(blockedReason ?? text(language, '存在未保存的编辑草稿。', 'Unsaved editor drafts exist.'));
      const [terrainCaptureRaw, projectCaptureRaw] = await Promise.all([
        backend.request<unknown>('workspace.file.inspect', { sourcePath: asset.path }, { signal: controller.signal }),
        backend.request<unknown>('workspace.file.inspect', { sourcePath: context.projectPath }, { signal: controller.signal }),
      ]);
      if (!isCurrent()) return;
      const terrainCapture = parseInspection(terrainCaptureRaw, asset.path);
      const projectCapture = parseInspection(projectCaptureRaw, context.projectPath);
      if (!terrainCapture || !projectCapture) {
        throw new Error(text(language, '服务没有返回匹配所选 Terrain 与工程的有效规范路径、SHA-256 和文件长度。', 'The service did not return matching canonical paths, SHA-256 values, and file lengths for the selected Terrain and project.'));
      }
      if (projectCapture.revision.toUpperCase() !== context.projectRevision!.toUpperCase()) {
        throw new Error(text(language, '工程文件修订与当前目录会话不一致；请刷新工程目录后重试。', 'The project-file revision does not match the active catalog session. Refresh the project catalog and retry.'));
      }
      if (asset.revision && (!isSha256(asset.revision) || asset.revision.toUpperCase() !== terrainCapture.revision.toUpperCase())) {
        throw new Error(text(language, '目录中的 Terrain 修订与服务器文件摘要不一致；请刷新目录后重试。', 'The Terrain revision in the catalog does not match the server file digest. Refresh the catalog and retry.'));
      }

      const snapshotRaw = await backend.request<ModelSnapshot>('model.load', modelLoadRequest(context, asset), { signal: controller.signal });
      if (!isCurrent()) return;
      assertModelSnapshot(snapshotRaw);
      if (!snapshotRaw.terrain || !isSha256(snapshotRaw.revision) ||
          snapshotRaw.revision.toUpperCase() !== terrainCapture.revision.toUpperCase()) {
        throw new Error(text(language, 'Terrain 快照与刚才捕获的源文件 SHA-256 不一致，已停止导出。', 'The Terrain snapshot does not match the captured source-file SHA-256. Export was stopped.'));
      }

      const [terrainBeforeExportRaw, projectBeforeExportRaw] = await Promise.all([
        backend.request<unknown>('workspace.file.inspect', { sourcePath: asset.path }, { signal: controller.signal }),
        backend.request<unknown>('workspace.file.inspect', { sourcePath: context.projectPath }, { signal: controller.signal }),
      ]);
      if (!isCurrent()) return;
      const terrainBeforeExport = parseInspection(terrainBeforeExportRaw, asset.path);
      const projectBeforeExport = parseInspection(projectBeforeExportRaw, context.projectPath);
      if (!terrainBeforeExport || !projectBeforeExport ||
          terrainBeforeExport.revision.toUpperCase() !== terrainCapture.revision.toUpperCase() ||
          !isSameServerPath(terrainBeforeExport.canonicalPath, terrainCapture.canonicalPath) ||
          projectBeforeExport.revision.toUpperCase() !== projectCapture.revision.toUpperCase() ||
          !isSameServerPath(projectBeforeExport.canonicalPath, projectCapture.canonicalPath)) {
        throw new Error(text(language, '工程或 Terrain 在导出前发生变化；没有启动栅格导出。', 'The project or Terrain changed before export. Raster export was not started.'));
      }

      const model = modelSaveRequest(context, asset, snapshotRaw);
      update({ phase: 'exporting', progress: undefined, cancelRequested: false, message: undefined });
      exportSubmitted = true;
      const resultRaw = await backend.request<unknown>('terrain.raster.export', { model, cellSize: parsedCellSize }, {
        signal: controller.signal,
        onProgress: (raw) => {
          if (!isCurrent()) return;
          const progress = progressFrom(raw);
          if (progress) update({ progress });
        },
        onTransportState: (transport) => {
          if (!isCurrent()) return;
          transportOperationID.current = transport.operationId;
          update({ transport, operationId: transport.operationId,
            cancelRequested: controller.signal.aborted || transport.state === 'cancelRequested' || transport.state === 'cancelRetrying' || transport.state === 'cancelSent' });
        },
      });
      terminalResultReceived = true;
      if (!isCurrent()) return;
      const receipt = parseExportReceipt(resultRaw);
      if (!receipt || !isProjectTreeSHA256(receipt.sourceRevision) ||
          receipt.sourceRevision.toUpperCase() !== terrainCapture.revision.toUpperCase() ||
          !isSameServerPath(receipt.sourcePath, terrainCapture.canonicalPath)) {
        throw new Error(text(language, 'Terrain 栅格导出回执与当前源路径或 SHA-256 不匹配，结果未验证。', 'The raster export receipt does not match the captured source path or SHA-256. The result is unverified.'));
      }
      completedReceipt = receipt;
      if (receipt.state === 'cancelled') {
        if (receipt.outputPath && (!isAbsoluteServerPath(receipt.outputPath) ||
            isSameServerPath(receipt.outputPath, terrainCapture.canonicalPath) ||
            isSameServerPath(receipt.outputPath, projectCapture.canonicalPath))) {
          throw new Error(text(language, '取消回执中的部分输出路径无效或指向源文件；未将其显示为成功。', 'The partial-output path in the cancellation receipt is invalid or points to a source file. It was not shown as a success.'));
        }
        update({ phase: 'cancelled', outputPath: receipt.outputPath ?? undefined,
          cleanupWarning: receipt.cleanupWarning, message: text(language, '服务已确认导出取消。', 'The service confirmed that the export was cancelled.') });
        return;
      }
      if (receipt.state !== 'completed' || !receipt.outputPath || !isAbsoluteServerPath(receipt.outputPath) ||
          !/\.tif{1,2}$/i.test(receipt.outputPath) ||
          isSameServerPath(receipt.outputPath, terrainCapture.canonicalPath) ||
          isSameServerPath(receipt.outputPath, projectCapture.canonicalPath)) {
        throw new Error(text(language, '服务没有返回有效、独立于源文件的 TIFF 完成回执。', 'The service did not return a valid completed TIFF receipt separate from the source files.'));
      }

      update({ phase: 'verifying', outputPath: receipt.outputPath, progress: undefined,
        message: text(language, '导出已结束，正在回读输出 TIFF 和复核源文件。', 'Export finished. Reading the output TIFF and rechecking the source files.') });
      const [outputCaptureRaw, terrainAfterRaw, projectAfterRaw] = await Promise.all([
        backend.request<unknown>('workspace.file.inspect', { sourcePath: receipt.outputPath }, { signal: controller.signal }),
        backend.request<unknown>('workspace.file.inspect', { sourcePath: asset.path }, { signal: controller.signal }),
        backend.request<unknown>('workspace.file.inspect', { sourcePath: context.projectPath }, { signal: controller.signal }),
      ]);
      if (!isCurrent()) return;
      const outputCapture = parseInspection(outputCaptureRaw, receipt.outputPath);
      const terrainAfter = parseInspection(terrainAfterRaw, asset.path);
      const projectAfter = parseInspection(projectAfterRaw, context.projectPath);
      if (!outputCapture || !terrainAfter || !projectAfter ||
          !isSameServerPath(outputCapture.canonicalPath, receipt.outputPath) ||
          terrainAfter.revision.toUpperCase() !== terrainCapture.revision.toUpperCase() ||
          !isSameServerPath(terrainAfter.canonicalPath, terrainCapture.canonicalPath) ||
          projectAfter.revision.toUpperCase() !== projectCapture.revision.toUpperCase() ||
          !isSameServerPath(projectAfter.canonicalPath, projectCapture.canonicalPath) ||
          isSameServerPath(outputCapture.canonicalPath, terrainCapture.canonicalPath) ||
          isSameServerPath(outputCapture.canonicalPath, projectCapture.canonicalPath)) {
        throw new Error(text(language, '输出文件未通过回读，或源 Terrain/工程在导出期间发生变化；不能确认导出成功。', 'Output read-back failed, or the source Terrain/project changed during export. Export success cannot be confirmed.'));
      }
      update({ phase: 'completed', message: text(language, '栅格导出已完成并通过文件回读；原 Terrain 与工程 SHA-256 保持不变。', 'Raster export completed and passed file read-back; the source Terrain and project SHA-256 values are unchanged.'),
        receipt: {
          outputPath: outputCapture.canonicalPath,
          outputRevision: outputCapture.revision,
          outputLength: outputCapture.length,
          sourcePath: terrainAfter.canonicalPath,
          sourceRevision: terrainAfter.revision,
        } });
    } catch (error) {
      if (!isCurrent()) return;
      if (isAbortError(error) && controller.signal.aborted) {
        if (!exportSubmitted) {
          update({ phase: 'cancelled', message: text(language, '已在导出启动前取消；没有创建输出文件。', 'Cancelled before export started. No output file was created.') });
        } else if (transportOperationID.current) {
          update({ phase: 'cancelled', operationId: transportOperationID.current,
            message: text(language, '服务已确认操作进入取消终态。', 'The service confirmed the operation reached its cancelled terminal state.') });
        } else {
          update({ phase: 'unconfirmed', message: text(language, '取消请求后的操作终态未能确认；请检查服务端目录中的输出文件后再判断。', 'The operation terminal state could not be confirmed after cancellation. Check the server-side output directory before deciding.') });
        }
      } else if (completedReceipt?.state === 'completed' || terminalResultReceived) {
        update({ phase: 'unverified', outputPath: completedReceipt?.outputPath ?? undefined,
          message: text(language, '导出操作已返回，但源文件或输出文件回读未通过校验；请勿将此结果视为成功。', 'The export operation returned, but source/output read-back verification failed. Do not treat it as a success.'),
          cleanupWarning: errorText(error, language) });
      } else if (exportSubmitted && (!isRecord(error) || error.terminalConfirmed !== true)) {
        update({ phase: 'unconfirmed', message: errorText(error, language) });
      } else {
        update({ phase: 'failed', message: errorText(error, language) });
      }
    } finally {
      if (runGeneration === generation.current && active.current?.controller === controller) active.current = null;
    }
  };

  const cancel = (): void => {
    const running = active.current;
    if (!running || running.owner !== owner || running.controller.signal.aborted || currentState?.phase === 'verifying') return;
    setViewState((previous) => previous?.owner === owner ? { ...previous, cancelRequested: true,
      message: text(language, '已请求取消，正在等待服务确认最终状态。', 'Cancellation requested; waiting for the service terminal state.') } : previous);
    running.controller.abort('User requested Terrain raster export cancellation.');
  };

  return <section className="project-detail-properties" aria-label={text(language, 'Terrain 栅格导出', 'Terrain raster export')}
    data-testid="project-terrain-raster-export">
    <h4>{text(language, '栅格导出', 'Raster export')}</h4>
    <p>{text(language, '将当前已保存的 Terrain 导出为带概览层的 GeoTIFF。输出目录和唯一文件名由服务根据工程设置确定；此操作不会改写 Terrain 源文件。',
      'Export the saved Terrain as a GeoTIFF with overview levels. The service selects the project export directory and a unique filename; this operation does not rewrite the Terrain source.')}</p>
    <label style={{ display: 'grid', gridTemplateColumns: 'minmax(90px, .7fr) minmax(90px, 1fr)', gap: '6px 10px', alignItems: 'center', maxWidth: 360 }}>
      <span>{text(language, '像元尺寸', 'Cell size')}</span>
      <input type="number" min="0" step="any" value={cellSize} disabled={isBusy}
        data-testid="project-terrain-raster-export-cell-size"
        aria-label={text(language, '像元尺寸', 'Cell size')}
        onChange={(event) => setCellSize(event.currentTarget.value)} />
    </label>
    {blockedReason && <div className="project-backend-note" role="note"><span>i</span>{blockedReason}</div>}
    <div className="project-footer-actions" style={{ marginTop: 10 }}>
      <button type="button" className="project-button project-button-primary" data-testid="project-terrain-raster-export-start"
        disabled={!!blockedReason || isBusy} onClick={() => void execute()}>
        {currentState?.phase === 'preparing' ? text(language, '正在捕获源文件…', 'Capturing source files…')
          : currentState?.phase === 'exporting' ? text(language, '正在导出…', 'Exporting…')
            : currentState?.phase === 'verifying' ? text(language, '正在核验…', 'Verifying…')
              : text(language, '导出 GeoTIFF', 'Export GeoTIFF')}
      </button>
      {isBusy && currentState?.phase !== 'verifying' && <button type="button" className="project-button project-button-secondary"
        data-testid="project-terrain-raster-export-cancel" onClick={cancel} disabled={!!currentState.cancelRequested}>
        {currentState.cancelRequested ? text(language, '等待取消确认…', 'Waiting for cancellation…') : text(language, '取消', 'Cancel')}
      </button>}
    </div>
    {isBusy && currentState?.phase === 'exporting' && currentState.progress && <div role="progressbar"
      aria-valuemin={0} aria-valuemax={1} aria-valuenow={currentState.progress.fraction}
      style={{ marginTop: 10, color: '#7f9aa3', fontSize: 11 }}>
      {currentState.progress.taskName || currentState.progress.message || text(language, '正在生成 TIFF…', 'Writing TIFF…')}
      {currentState.progress.fraction !== undefined && ` · ${Math.round(currentState.progress.fraction * 100)}%`}
    </div>}
    {currentState?.message && <p role={currentState.phase === 'failed' || currentState.phase === 'unconfirmed' || currentState.phase === 'unverified' ? 'alert' : 'status'}
      className={currentState.phase === 'failed' || currentState.phase === 'unconfirmed' || currentState.phase === 'unverified' ? 'project-inline-error' : undefined}>
      {currentState.message}
    </p>}
    {currentState?.transport && currentState.phase === 'exporting' && <small role="status" style={{ display: 'block', overflowWrap: 'anywhere' }}>
      {currentState.transport.state === 'reconnecting' ? text(language, `正在重连导出操作 ${currentState.transport.operationId}…`, `Reconnecting to export operation ${currentState.transport.operationId}…`)
        : currentState.transport.state === 'cancelSent' ? text(language, '取消请求已送达，仍在等待最终状态。', 'Cancellation was delivered; waiting for the terminal state.')
          : currentState.transport.state === 'cancelFailed' ? text(language, '取消请求未能送达，仍在等待最终状态。', 'Cancellation delivery failed; still waiting for the terminal state.')
            : text(language, '正在等待服务确认导出状态。', 'Waiting for the service terminal state.')}
    </small>}
    {currentState?.phase === 'completed' && currentState.receipt && <dl className="project-detail-properties" data-testid="project-terrain-raster-export-receipt">
      <div><dt>{text(language, '输出路径（服务器）', 'Output path (server)')}</dt><dd><code style={{ overflowWrap: 'anywhere' }}>{currentState.receipt.outputPath}</code></dd></div>
      <div><dt>{text(language, '输出 SHA-256', 'Output SHA-256')}</dt><dd><code>{currentState.receipt.outputRevision}</code></dd></div>
      <div><dt>{text(language, '输出长度', 'Output length')}</dt><dd>{currentState.receipt.outputLength} {text(language, '字节', 'bytes')}</dd></div>
      <div><dt>{text(language, '源 Terrain SHA-256', 'Source Terrain SHA-256')}</dt><dd><code>{currentState.receipt.sourceRevision}</code></dd></div>
      <div><dt>{text(language, '源 Terrain 路径', 'Source Terrain path')}</dt><dd><code style={{ overflowWrap: 'anywhere' }}>{currentState.receipt.sourcePath}</code></dd></div>
    </dl>}
    {currentState?.phase === 'cancelled' && currentState.outputPath && <p className="project-inline-error" role="status">
      {text(language, '服务报告存在未完成输出，请在服务器端处理该部分文件：', 'The service reported a partial output. Handle this incomplete file on the server:')} <code>{currentState.outputPath}</code>
    </p>}
    {currentState?.cleanupWarning && <details className="project-detail-technical"><summary>{text(language, '清理/核验信息', 'Cleanup/verification details')}</summary><pre>{currentState.cleanupWarning}</pre></details>}
  </section>;
}
