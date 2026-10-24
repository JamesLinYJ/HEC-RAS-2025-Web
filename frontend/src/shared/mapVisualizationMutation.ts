import type { MapVisualizationCommitReceipt, MapVisualizationTarget, WorkbenchContext } from './workbench';
import { sessionReferenceSourceIdentity } from './sessionReferences';

const sha256 = /^[\da-f]{64}$/i;
const sourceIdentity = (source: WorkbenchContext): string => JSON.stringify([
  sessionReferenceSourceIdentity(source), source.selectedAsset?.locator ?? null,
]);
const targetIdentity = (target: MapVisualizationTarget): string => JSON.stringify([
  target.mapID, target.mapIndex, target.name, target.mapType, target.visualizationRevision?.toUpperCase(),
]);

export interface MapVisualizationLease {
  id: string;
  source: string;
  target: string;
}

/** The tool's parameter draft may remain open; other workspace drafts cannot be bypassed. */
export function captureMapVisualizationLease(current: WorkbenchContext, source: WorkbenchContext,
  target: MapVisualizationTarget, draftOwner: string, dirtyOwners: ReadonlySet<string>): MapVisualizationLease | null {
  if (!source.projectPath || !source.projectSessionID || !source.projectXml ||
      !sha256.test(source.projectRevision ?? '') || !draftOwner ||
      sourceIdentity(source) !== sourceIdentity(current) ||
      [...dirtyOwners].some(owner => owner !== draftOwner) ||
      !target.mapID || !Number.isInteger(target.mapIndex) || target.mapIndex < 0 ||
      !target.name.trim() || !target.mapType.trim() || !sha256.test(target.visualizationRevision ?? '')) return null;
  return { id: crypto.randomUUID(), source: sourceIdentity(source), target: targetIdentity(target) };
}

/** Only the acknowledgement for the reserved source, map and preceding .vis revision is accepted. */
export function matchesMapVisualizationReceipt(lease: MapVisualizationLease, current: WorkbenchContext,
  source: WorkbenchContext, target: MapVisualizationTarget, receipt: MapVisualizationCommitReceipt): boolean {
  return lease.source === sourceIdentity(current) && lease.source === sourceIdentity(source) &&
    lease.target === targetIdentity(target) && receipt.mapIndex === target.mapIndex &&
    receipt.mapName === target.name && receipt.mapType === target.mapType &&
    sha256.test(receipt.visualizationRevision) &&
    typeof receipt.previousVisualizationRevision === 'string' &&
    receipt.previousVisualizationRevision.toUpperCase() === target.visualizationRevision?.toUpperCase();
}
