import type { WorkbenchContext } from './workbench';

const sha256 = /^[a-f\d]{64}$/i;
const operationID = /^[a-f\d]{32}$/;
const sourceKey = (value: WorkbenchContext): string => JSON.stringify(value);

/** A source-bound terrain save survives transport loss without retiring its import payloads. */
export interface ModelSaveLease {
  id: string;
  sourceKey: string;
  sourceRevision: string;
  writeOwner: string;
  operationID: string | null;
}

export function captureModelSaveLease(current: WorkbenchContext, source: WorkbenchContext,
  draftOwner: string, writeOwner: string, dirtyOwners: ReadonlySet<string>,
  sourceRevision: string | null | undefined = source.selectedAsset?.revision): ModelSaveLease | null {
  if (!draftOwner || !writeOwner.startsWith('editors.channel-v6-write:') || draftOwner === writeOwner
    || !source.projectSessionID || !source.projectPath || !source.projectXml
    || !sha256.test(source.projectRevision ?? '') || source.selectedAsset?.kind !== 'Terrain'
    || !source.selectedAsset.path || typeof sourceRevision !== 'string' || !sha256.test(sourceRevision)
    || (source.selectedAsset.revision != null && source.selectedAsset.revision.toUpperCase() !== sourceRevision?.toUpperCase())
    || sourceKey(current) !== sourceKey(source) || [...dirtyOwners].some(owner => owner !== draftOwner)) return null;
  return { id: crypto.randomUUID(), sourceKey: sourceKey(source), sourceRevision, writeOwner, operationID: null };
}

export function trackModelSaveOperation(lease: ModelSaveLease, id: string): ModelSaveLease | null {
  if (!operationID.test(id) || (lease.operationID !== null && lease.operationID !== id)) return null;
  return lease.operationID === id ? lease : { ...lease, operationID: id };
}

export function matchesModelSaveCommit(lease: ModelSaveLease, current: WorkbenchContext,
  source: WorkbenchContext, committed: WorkbenchContext, id: string): boolean {
  return !!lease.operationID && lease.operationID === id
    && lease.sourceKey === sourceKey(current) && lease.sourceKey === sourceKey(source)
    && committed.projectSessionID === source.projectSessionID && committed.projectPath === source.projectPath
    && committed.projectRevision === source.projectRevision && committed.projectXml === source.projectXml
    && committed.selectedAsset?.path === source.selectedAsset?.path
    && committed.selectedAsset?.kind === source.selectedAsset?.kind
    && committed.selectedAsset?.name === source.selectedAsset?.name
    && sha256.test(committed.selectedAsset?.revision ?? '');
}
