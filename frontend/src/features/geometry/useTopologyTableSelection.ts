import { useEffect, useMemo, useState } from 'react';
import type { GeometryRow, GeometryTopologySnapshot } from './geometryTypes';
import type { GeometryTopologyMapWorkspace } from './useGeometryTopologyMap';
import { topologyTableSelectedRowIDs, type TopologyLocalTableSelection, type TopologySelectionKind } from './geometryTopologySelection';

/** Source selections are controlled by the shared snapshot; local rows stay table-only. */
export function useTopologyTableSelection(kind: TopologySelectionKind, rows: GeometryRow[], snapshot: GeometryTopologySnapshot | undefined,
  workspace: GeometryTopologyMapWorkspace | undefined, onSelectionChange: ((rows: GeometryRow[]) => number | void) | undefined) {
  const scopeKey = workspace?.selectionSnapshotKey ?? JSON.stringify([snapshot?.revision, snapshot?.draftState, snapshot?.completion?.draftIdentity]);
  const [entry, setEntry] = useState<TopologyLocalTableSelection | null>(null);
  const selectedFIDs = workspace?.canvasProps.selectedFIDByKind[kind] ?? [];
  const version = workspace?.selectionVersion ?? null;
  const selectedIDs = useMemo(() => version !== null
    ? topologyTableSelectedRowIDs(kind, rows, snapshot, selectedFIDs, scopeKey, version, entry)
    : rows.filter((row) => entry?.scopeKey === scopeKey && entry.ids.includes(row.id)).map((row) => row.id),
  [kind, rows, snapshot, selectedFIDs, scopeKey, version, entry]);

  useEffect(() => { setEntry((current) => current?.scopeKey === scopeKey && current.version === version ? current : null); }, [scopeKey, version]);

  const selectRows = (next: GeometryRow[]): void => {
    const submittedVersion = onSelectionChange?.(next);
    setEntry({ scopeKey, ids: next.map((row) => row.id), version: typeof submittedVersion === 'number' ? submittedVersion : version });
  };
  return { selectedIDs, selectedRows: rows.filter((row) => selectedIDs.includes(row.id)), selectRows };
}
