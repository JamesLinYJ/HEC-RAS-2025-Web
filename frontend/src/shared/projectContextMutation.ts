import type { WorkbenchContext } from './workbench';
import { sessionReferenceSourceIdentity } from './sessionReferences';

const sha256 = /^[\da-f]{64}$/i;
const identity = (source: WorkbenchContext): string => JSON.stringify([
  sessionReferenceSourceIdentity(source), source.selectedAsset?.locator ?? null,
]);
export interface ProjectContextLease { id: string; source: string }

export function captureProjectContextLease(current: WorkbenchContext, source: WorkbenchContext,
  draftOwner: string, dirtyOwners: ReadonlySet<string>): ProjectContextLease | null {
  if (!source.projectPath || !source.projectSessionID || !source.projectXml || !sha256.test(source.projectRevision ?? '') ||
      !draftOwner || identity(current) !== identity(source) || [...dirtyOwners].some(owner => owner !== draftOwner)) return null;
  return { id: crypto.randomUUID(), source: identity(source) };
}

export function matchesProjectContextCommit(lease: ProjectContextLease, current: WorkbenchContext,
  source: WorkbenchContext, committed: WorkbenchContext): boolean {
  return lease.source === identity(current) && lease.source === identity(source) &&
    committed.projectPath === source.projectPath && committed.projectSessionID === source.projectSessionID &&
    sha256.test(committed.projectRevision ?? '') && typeof committed.projectXml === 'string' && !!committed.projectXml &&
    committed.selectedAsset?.path === source.selectedAsset?.path &&
    committed.selectedAsset?.kind === source.selectedAsset?.kind &&
    committed.selectedAsset?.revision === source.selectedAsset?.revision;
}
