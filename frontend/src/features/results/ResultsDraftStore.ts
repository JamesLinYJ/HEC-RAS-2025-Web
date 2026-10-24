import type { ResultDataset } from './data';

export type ResultCellDrafts = Record<string, Record<string, string>>;

export interface ResultsDraftSnapshot {
  dataset: ResultDataset | null;
  originalDataset: ResultDataset | null;
  dirty: boolean;
  cellDrafts: ResultCellDrafts;
  source: 'result' | 'user';
}

const snapshots = new Map<string, ResultsDraftSnapshot>();

function cloneSnapshot(snapshot: ResultsDraftSnapshot): ResultsDraftSnapshot {
  return structuredClone(snapshot);
}

/** Keeps local user-data edits alive while the Results page is unmounted during feature navigation. */
export function readResultsDraft(key: string): ResultsDraftSnapshot | null {
  const snapshot = snapshots.get(key);
  return snapshot ? cloneSnapshot(snapshot) : null;
}

export function writeResultsDraft(key: string, snapshot: ResultsDraftSnapshot): void {
  snapshots.set(key, cloneSnapshot(snapshot));
}

export function deleteResultsDraft(key: string): void {
  snapshots.delete(key);
}
