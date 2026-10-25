import type { MapDocumentIndexChange, MapVisualizationAcknowledgement, MapVisualizationTarget, WorkbenchContext } from './workbench';
import { sessionReferenceSourceIdentity } from './sessionReferences';

const sha256 = /^[\da-f]{64}$/i;
const sourceIdentity = (source: WorkbenchContext): string => JSON.stringify([
  sessionReferenceSourceIdentity(source), source.selectedAsset?.locator ?? null,
]);
const targetIdentity = (target: MapVisualizationTarget): string => JSON.stringify([
  target.mapID, target.mapIndex, target.name, target.mapType, target.visualizationRevision?.toUpperCase(),
  target.documentMutation?.action === 'rename' ? ['rename', target.documentMutation.newName] :
    target.documentMutation?.action === 'delete' ? ['delete', target.documentMutation.mapIndexChanges.map(change =>
      [change.previousMapIndex, change.mapIndex, change.mapName, change.mapType])] : null,
]);

const validName = (name: string): boolean => typeof name === 'string' && name === name.trim() &&
  name.length > 0 && name.length <= 128 && !/[\u0000-\u001f\u007f-\u009f]/.test(name);

function validIndexChanges(changes: MapDocumentIndexChange[], removedIndex: number): boolean {
  return Array.isArray(changes) && removedIndex <= changes.length && changes.every((change, index) =>
    change != null && change.mapIndex === index &&
    change.previousMapIndex === (index < removedIndex ? index : index + 1) &&
    typeof change.mapName === 'string' && change.mapName.trim().length > 0 &&
    typeof change.mapType === 'string' && change.mapType.trim().length > 0);
}

function validDocumentMutation(target: MapVisualizationTarget): boolean {
  const mutation = target.documentMutation;
  if (!mutation) return true;
  if (mutation.action === 'rename') return validName(mutation.newName);
  return mutation.action === 'delete' && validIndexChanges(mutation.mapIndexChanges, target.mapIndex);
}

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
      !target.name.trim() || !target.mapType.trim() || !sha256.test(target.visualizationRevision ?? '') ||
      !validDocumentMutation(target)) return null;
  return { id: crypto.randomUUID(), source: sourceIdentity(source), target: targetIdentity(target) };
}

/** Only the acknowledgement for the reserved source, map and preceding .vis revision is accepted. */
export function matchesMapVisualizationReceipt(lease: MapVisualizationLease, current: WorkbenchContext,
  source: WorkbenchContext, target: MapVisualizationTarget, acknowledgement: MapVisualizationAcknowledgement): boolean {
  if (!validDocumentMutation(target) || lease.source !== sourceIdentity(current) || lease.source !== sourceIdentity(source) ||
      lease.target !== targetIdentity(target) || !acknowledgement || typeof acknowledgement !== 'object') return false;
  const receipt = 'documentAction' in acknowledgement ? acknowledgement.receipt : acknowledgement;
  if (!receipt || !sha256.test(receipt.visualizationRevision) ||
      typeof receipt.previousVisualizationRevision !== 'string' ||
      receipt.previousVisualizationRevision.toUpperCase() !== target.visualizationRevision?.toUpperCase()) return false;

  if ('documentAction' in acknowledgement) {
    if (acknowledgement.documentAction === 'rename' && target.documentMutation?.action === 'rename') {
      const renamed = acknowledgement.receipt;
      return renamed.mapIndex === target.mapIndex && renamed.mapType === target.mapType &&
        renamed.mapName === target.documentMutation.newName &&
        (renamed.mapName === target.name || renamed.visualizationRevision.toUpperCase() !== target.visualizationRevision?.toUpperCase());
    }
    if (acknowledgement.documentAction === 'delete' && target.documentMutation?.action === 'delete') {
      const removed = acknowledgement.receipt;
      const expectedChanges = target.documentMutation.mapIndexChanges;
      return removed.removedMapIndex === target.mapIndex && removed.removedMapName === target.name &&
        removed.removedMapType === target.mapType && validIndexChanges(removed.mapIndexChanges, target.mapIndex) &&
        removed.visualizationRevision.toUpperCase() !== target.visualizationRevision?.toUpperCase() &&
        removed.mapIndexChanges.length === expectedChanges.length &&
        removed.mapIndexChanges.every((change, index) => {
          const expected = expectedChanges[index];
          return expected != null && change.previousMapIndex === expected.previousMapIndex && change.mapIndex === expected.mapIndex &&
            change.mapName === expected.mapName && change.mapType === expected.mapType;
        });
    }
    return false;
  }
  return !target.documentMutation && acknowledgement.mapIndex === target.mapIndex &&
    acknowledgement.mapName === target.name && acknowledgement.mapType === target.mapType;
}
