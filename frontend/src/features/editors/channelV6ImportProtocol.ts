import { BackendOperationUnavailableError } from '../../shared/workbench';
import { isWorkspaceFileInspection } from '../project/QuickProjectImportWorkflow';
import type {
  ChannelV6ImportCapture, ChannelV6ImportCleanup, ChannelV6ImportCompleted, ChannelV6ImportDiscarded,
  ChannelV6ImportFile, ChannelV6ImportInspection, ChannelV6ImportProgress,
} from './channelV6ImportTypes';

export const channelV6ImportMethods = {
  inspect: 'workspace.file.inspect',
  import: 'model.channelV6GeometryImport',
  discard: 'model.channelV6GeometryImportDiscard',
} as const;

function record(value: unknown): value is Record<string, unknown> {
  return !!value && typeof value === 'object' && !Array.isArray(value);
}
function sha256(value: unknown): value is string { return typeof value === 'string' && /^[A-Fa-f0-9]{64}$/.test(value); }
function token(value: unknown): value is string { return typeof value === 'string' && /^[a-f0-9]{32}$/.test(value); }
function stringRecord(value: unknown): value is Record<string, string> {
  return record(value) && Object.values(value).every(item => typeof item === 'string');
}

/** Raw form text is retained; only surrounding whitespace is ignored when submitting. */
export function channelV6SourcePathIssue(raw: string): string | null {
  const path = raw.trim();
  if (!path) return '请输入 Version 6 几何 HDF 文件的完整路径。';
  if (/\0|\r|\n/.test(path) || !(/^(?:\/|[A-Za-z]:[\\/]|\\\\[^\\/]+[\\/][^\\/]+)/.test(path)))
    return '请输入 C# 服务所在计算机可访问的绝对路径。';
  if (!/\.hdf$/i.test(path)) return '请选择扩展名为 .hdf 的 Version 6 几何文件。';
  return null;
}

export function parseChannelV6ImportFile(value: unknown, rawPath: string): ChannelV6ImportFile {
  const issue = channelV6SourcePathIssue(rawPath);
  if (issue) throw new Error(issue);
  if (!isWorkspaceFileInspection(value, rawPath.trim()) || !/\.hdf$/i.test(value.canonicalPath)
      || value.length <= 0 || !/^[A-F0-9]{64}$/.test(value.revision))
    throw new Error('来源检查回执缺少匹配的 HDF 路径、非空文件长度或真实 SHA-256。');
  return { sourcePath: value.sourcePath, canonicalPath: value.canonicalPath, revision: value.revision, length: value.length };
}

export function validateChannelV6ImportCapture(capture: ChannelV6ImportCapture, inputId: string): void {
  if (!capture.owner || !capture.draftKey || capture.inputId !== inputId || !inputId
      || !capture.backend || !capture.model || !capture.model.assetPath || !sha256(capture.model.revision)
      || !sha256(capture.expectedDraftIdentity) || !Array.isArray(capture.inputIds)
      || capture.inputIds.some(id => typeof id !== 'string' || !id)
      || new Set(capture.inputIds).size !== capture.inputIds.length || !capture.inputIds.includes(inputId)
      || (capture.existingDraftToken !== null && !token(capture.existingDraftToken)))
    throw new Error('父地形没有返回可核验的河道、草稿或来源身份，请重新读取当前输入。');
  if (!capture.backend.supports(channelV6ImportMethods.inspect)
      || !capture.backend.supports(channelV6ImportMethods.import)
      || !capture.backend.supports(channelV6ImportMethods.discard))
    throw new Error('当前 C# 服务缺少 Version 6 来源检查、导入或精确暂存清理能力。');
}

export function channelV6CaptureKey(capture: ChannelV6ImportCapture): string {
  return JSON.stringify([capture.owner, capture.draftKey, capture.inputId, capture.inputIds,
    capture.expectedDraftIdentity, capture.existingDraftToken, capture.model]);
}

export function channelV6ImportRequest(inspection: ChannelV6ImportInspection) {
  validateChannelV6ImportCapture(inspection.capture, inspection.capture.inputId);
  return {
    model: structuredClone(inspection.capture.model),
    inputId: inspection.capture.inputId,
    sourcePath: inspection.file.canonicalPath,
    sourceSHA256: inspection.file.revision,
    expectedDraftIdentity: inspection.capture.expectedDraftIdentity,
  };
}

/** A cleanup capability is bound to this result's terrain token and the captured input list. */
export function parseChannelV6ImportCleanup(value: unknown, inspection: ChannelV6ImportInspection): ChannelV6ImportCleanup {
  if (!record(value) || value.state !== 'completed' || value.sourceRevision !== inspection.file.revision
      || !record(value.snapshot) || value.snapshot.revision !== inspection.capture.model.revision
      || !record(value.snapshot.terrain) || value.snapshot.terrain.selectedId !== inspection.capture.inputId
      || !Array.isArray(value.snapshot.terrain.inputs)
      || JSON.stringify(value.snapshot.terrain.inputs.map(input => record(input) ? input.id : null)) !== JSON.stringify(inspection.capture.inputIds)
      || !record(value.cleanup) || !token(value.cleanup.draftToken) || !token(value.cleanup.payloadId)
      || !sha256(value.cleanup.migratedSHA256)
      || value.cleanup.draftToken !== value.snapshot.terrain.importDraftToken
      || (inspection.capture.existingDraftToken !== null && value.cleanup.draftToken !== inspection.capture.existingDraftToken))
    throw new Error('导入回执没有返回归属于此次河道草稿的精确暂存清理身份。');
  return { draftToken: value.cleanup.draftToken, payloadId: value.cleanup.payloadId,
    migratedSHA256: value.cleanup.migratedSHA256 };
}

export function parseChannelV6ImportCompleted(value: unknown, inspection: ChannelV6ImportInspection): ChannelV6ImportCompleted {
  parseChannelV6ImportCleanup(value, inspection);
  if (!record(value) || !record(value.snapshot) || !record(value.topology)) throw new Error('河道导入没有返回完整地形和拓扑回执。');
  const snapshot = value.snapshot, topology = value.topology;
  if (typeof snapshot.sourceState !== 'string' || !snapshot.sourceState || snapshot.canEdit !== true
      || !stringRecord(snapshot.fields) || !record(snapshot.images) || !record(snapshot.fieldGroups)
      || !Array.isArray(snapshot.availableOperations)
      || topology.inputId !== inspection.capture.inputId || topology.sourceRevision !== inspection.capture.model.revision
      || !sha256(topology.draftIdentity) || !Array.isArray(topology.features) || topology.features.length === 0
      || !Array.isArray(topology.selectedFeatureIDs) || typeof topology.canUndo !== 'boolean' || typeof topology.canRedo !== 'boolean')
    throw new Error('导入返回的草稿或拓扑不匹配当前河道及地形修订。');
  const ids = new Set<string>();
  for (const feature of topology.features) {
    if (!record(feature) || typeof feature.id !== 'string' || !feature.id || ids.has(feature.id)
        || typeof feature.kind !== 'string' || !feature.kind || !Array.isArray(feature.points)
        || feature.points.some(point => !record(point) || typeof point.x !== 'number' || !Number.isFinite(point.x)
          || typeof point.y !== 'number' || !Number.isFinite(point.y)))
      throw new Error('导入拓扑包含重复要素身份或无效坐标。');
    ids.add(feature.id);
  }
  if (topology.selectedFeatureIDs.some(id => typeof id !== 'string' || !ids.has(id))
      || new Set(topology.selectedFeatureIDs).size !== topology.selectedFeatureIDs.length)
    throw new Error('导入拓扑的所选要素身份无效。');
  return value as unknown as ChannelV6ImportCompleted;
}

export function isChannelV6ImportCancelled(value: unknown): boolean {
  return record(value) && value.state === 'cancelled' && value.cleanup == null && value.snapshot == null && value.topology == null;
}

export function parseChannelV6ImportDiscarded(value: unknown, cleanup: ChannelV6ImportCleanup): ChannelV6ImportDiscarded {
  if (!record(value) || value.discarded !== true || value.payloadId !== cleanup.payloadId || typeof value.draftDirectoryRemoved !== 'boolean')
    throw new Error('服务没有确认本次河道暂存文件已清理，清理归属将继续保留。');
  return value as unknown as ChannelV6ImportDiscarded;
}

export function channelV6TerminalConfirmed(error: unknown): boolean {
  if (error instanceof BackendOperationUnavailableError) return true;
  if (!record(error)) return false;
  return error.terminalConfirmed === true || (error.terminalConfirmed !== false
    && (error.code === 'INVALID_REQUEST' || (error.operation === 'POST /operations'
      && typeof error.status === 'number' && error.status >= 400 && error.status < 500 && error.status !== 408)));
}

export function channelV6OperationID(error: unknown): string | null {
  return record(error) && typeof error.operationId === 'string' && error.operationId.length > 0 ? error.operationId : null;
}

export function parseChannelV6ImportProgress(value: unknown): ChannelV6ImportProgress | null {
  if (!record(value)) return null;
  const fraction = typeof value.fraction === 'number' && Number.isFinite(value.fraction)
    ? Math.min(1, Math.max(0, value.fraction)) : undefined;
  const taskName = typeof value.taskName === 'string' ? value.taskName : undefined;
  const message = typeof value.message === 'string' ? value.message : undefined;
  return fraction !== undefined || taskName || message ? { fraction, taskName, message } : null;
}
