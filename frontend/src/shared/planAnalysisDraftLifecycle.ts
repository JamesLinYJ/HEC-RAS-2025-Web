import type { AssetReference, BackendGateway, BackendRequestOptions, WorkbenchContext } from './workbench';

export interface PlanDiscardTransition { revision: number; sourceOwner: string; targetOwner: string }
/** A root discard consumes only the old render's exact source-to-target transition. */
export function matchesPlanDiscardTransition(transition: PlanDiscardTransition | null,
  revision: number, sourceOwner: string, targetOwner: string): boolean {
  return !!transition && transition.revision === revision && transition.sourceOwner === sourceOwner
    && transition.targetOwner === targetOwner;
}

export interface PlanSaveRecovery<T> {
  ownerContextID: string;
  source: WorkbenchContext;
  asset: AssetReference;
  backend: BackendGateway;
  backendRevision: number;
  operationID: string | null;
  phase: 'saving' | 'uncertain' | 'resuming';
  attemptID?: string;
  receipt?: T;
}
export function capturePlanSaveRecovery<T>(ownerContextID: string, source: WorkbenchContext,
  backend: BackendGateway, backendRevision = 0): PlanSaveRecovery<T> {
  if (source.selectedAsset?.kind !== 'Plan') throw new Error('只能跟踪当前计算计划的保存。');
  const captured = structuredClone(source);
  return { ownerContextID, source: captured, asset: captured.selectedAsset!, backend, backendRevision,
    operationID: null, phase: 'saving' };
}
export function trackPlanSaveOperation<T>(capture: PlanSaveRecovery<T>, operationID: string): PlanSaveRecovery<T> {
  if (!operationID || (capture.operationID && capture.operationID !== operationID)) throw new Error('保存任务编号与原任务不一致。');
  return capture.operationID === operationID ? capture : { ...capture, operationID };
}
/** Never calls request/model.save. The original gateway and accepted operation identity are pinned. */
export async function resumeCapturedPlanSave<T>(capture: PlanSaveRecovery<T>, options?: BackendRequestOptions): Promise<T> {
  if (!capture.operationID) throw new Error('原保存请求没有返回任务编号，无法自动续查；不会重复提交。');
  if (capture.receipt !== undefined) return capture.receipt;
  if (!capture.backend.resumeOperation) throw new Error('原服务连接尚不支持继续核对任务，请保留当前草稿。');
  return capture.backend.resumeOperation<T>(capture.operationID, options);
}
export function isConfirmedPlanSaveTerminal(error: unknown): boolean {
  return !!error && typeof error === 'object' && 'terminalConfirmed' in error && error.terminalConfirmed === true;
}
