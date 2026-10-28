import { BackendOperationUnavailableError, type BackendGateway, type BackendRequestOptions, type FeaturePageProps, type WorkbenchContext } from '../../shared/workbench';
import { planAnalysisValueKey } from '../../shared/planAnalysisDrafts';
import type { ModelDraft, ModelSnapshot } from './model';
import type { ChannelV6ImportModel } from './channelV6ImportTypes';

export interface ChannelV6ParentSource {
  owner: string;
  context: WorkbenchContext;
  backend: BackendGateway;
  backendRevision: number;
  generation: number;
  draftGeneration: number;
  snapshot: ModelSnapshot;
  draft: ModelDraft;
}

export interface ChannelV6ParentSave {
  current: ChannelV6ParentSource;
  sourceKey: string;
  leaseID: string;
  operationID: string | null;
  operationAcknowledged: boolean;
  operationConflict?: boolean;
  phase: 'saving' | 'unknown' | 'resuming';
  request: ChannelV6ImportModel;
  receipt?: ModelSnapshot;
  error?: unknown;
}

// A reconnect can replace the gateway or its capabilities without replacing the parent draft.
// Recovery still uses current.backend captured below, never the replacement gateway.
function parentSaveSourceKey(current: ChannelV6ParentSource): string {
  return planAnalysisValueKey([current.owner, current.context, current.generation, current.draftGeneration]);
}

export function captureChannelV6ParentSave(current: ChannelV6ParentSource, leaseID: string,
  request: ChannelV6ImportModel): ChannelV6ParentSave {
  return { current: { ...current, context: structuredClone(current.context) }, sourceKey: parentSaveSourceKey(current),
    leaseID, operationID: null, operationAcknowledged: false, phase: 'saving', request: structuredClone(request) };
}

export function channelV6ParentSaveCurrent(save: ChannelV6ParentSave, current: ChannelV6ParentSource | null): boolean {
  return !!current && current.snapshot === save.current.snapshot && current.draft === save.current.draft
    && save.sourceKey === parentSaveSourceKey(current);
}

/** Record the first real HTTP ID even if the root cannot yet acknowledge it. Never replace it. */
export function trackChannelV6ParentSave(save: ChannelV6ParentSave, operationID: string,
  acknowledge: NonNullable<FeaturePageProps['onTrackModelSave']>): ChannelV6ParentSave {
  if (save.operationConflict) return save;
  if (!/^[a-f\d]{32}$/.test(operationID))
    return { ...save, operationAcknowledged: false, error: new Error('地形保存返回了无效任务编号，原事务保护继续保留。') };
  if (save.operationID && save.operationID !== operationID)
    return { ...save, operationAcknowledged: false, operationConflict: true,
      error: new Error('地形保存返回了不同任务编号；不会替换原任务或采用其回执。') };
  const tracked = { ...save, operationID, operationAcknowledged: false };
  try {
    if (!acknowledge(save.leaseID, operationID))
      return { ...tracked, error: new Error('根事务尚未接纳原地形保存任务编号，父草稿继续保留。') };
    return { ...tracked, operationAcknowledged: true, error: undefined };
  } catch (error) { return { ...tracked, error }; }
}

/** This function cannot mutate parent draft or payload ownership before the root's acknowledgement. */
export function acknowledgeChannelV6ParentSave(save: ChannelV6ParentSave, current: ChannelV6ParentSource | null,
  committed: WorkbenchContext, acknowledgeOperation: NonNullable<FeaturePageProps['onTrackModelSave']>,
  acknowledgeCommit: NonNullable<FeaturePageProps['onCommitModelSave']>): { save: ChannelV6ParentSave; accepted: boolean } {
  if (!save.receipt || !save.operationID || save.operationConflict || !channelV6ParentSaveCurrent(save, current))
    return { save, accepted: false };
  const tracked = trackChannelV6ParentSave(save, save.operationID, acknowledgeOperation);
  if (!tracked.operationAcknowledged) return { save: tracked, accepted: false };
  try {
    if (!acknowledgeCommit(tracked.current.context, committed, tracked.leaseID, tracked.operationID!))
      return { save: { ...tracked, error: new Error('根事务尚未接纳原地形保存回执，父草稿和暂存文件继续保留。') }, accepted: false };
    return { save: tracked, accepted: true };
  } catch (error) { return { save: { ...tracked, error }, accepted: false }; }
}

/** A failed GET, malformed ID, or local acknowledgement exception is not the write's terminal state. */
export function channelV6ParentSaveFailureConfirmed(save: ChannelV6ParentSave, error: unknown, phase: 'submit' | 'resume'): boolean {
  if (save.receipt || save.operationConflict) return false;
  if (!error || typeof error !== 'object') return false;
  const failure = error as { terminalConfirmed?: unknown; operationId?: unknown; code?: unknown; operation?: unknown; status?: unknown };
  if (failure.terminalConfirmed === true)
    return !!save.operationID && failure.operationId === save.operationID;
  if (phase !== 'submit' || save.operationID || failure.operationId || failure.terminalConfirmed === false) return false;
  return error instanceof BackendOperationUnavailableError || failure.code === 'CLIENT_DISPOSED'
    || (failure.code === 'INVALID_REQUEST' && failure.operation === 'model.save')
    || (failure.operation === 'POST /operations' && typeof failure.status === 'number'
      && failure.status >= 400 && failure.status < 500 && failure.status !== 408);
}

export async function resumeChannelV6ParentSave(save: ChannelV6ParentSave, options?: BackendRequestOptions): Promise<ModelSnapshot> {
  if (!save.operationID || !/^[a-f\d]{32}$/.test(save.operationID)) throw new Error('原地形保存没有有效任务编号，不会重复提交。');
  if (save.operationConflict) throw new Error('地形保存任务编号有冲突，父草稿和暂存文件继续保留。');
  if (save.receipt) return save.receipt;
  if (!save.current.backend.resumeOperation) throw new Error('原服务不支持续查；父草稿与暂存文件继续保留。');
  return save.current.backend.resumeOperation<ModelSnapshot>(save.operationID, options);
}
