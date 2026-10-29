import type {
  GeometryRow, GeometryTopologyCompletionMessage, GeometryTopologyExtent,
  GeometryTopologyMapFeature, GeometryTopologySnapshot,
} from './geometryTypes';

export type TopologySelectionKind = 'node' | 'arc' | 'region';
export type TopologySelection = Record<TopologySelectionKind, number[]>;
export type TopologyDiagnosticTarget = NonNullable<GeometryTopologyCompletionMessage['targets']>[number];
export type TopologyDiagnosticLocator = (messageID: string, target: TopologyDiagnosticTarget) => void;

export interface TopologySelectionFocus {
  snapshotKey: string;
  requestID: number;
  extent: GeometryTopologyExtent;
}

export interface TopologyLocalTableSelection {
  scopeKey: string;
  version: number | null;
  ids: string[];
}

/** A quick editor owns exactly one source feature in one map selection generation. */
export interface TopologyQuickSelectionCapture {
  snapshotKey: string;
  version: number;
  kind: 'arc' | 'region';
  fid: number;
  rowID: string;
  revision: string;
}

export function captureTopologyQuickSelection(snapshot: GeometryTopologySnapshot | undefined, kind: 'arc' | 'region', row: GeometryRow,
  snapshotKey: string | null, version: number, selection: TopologySelection): TopologyQuickSelectionCapture | null {
  if (!snapshot || !snapshotKey || topologySourceRows(snapshot, kind, [row]).length !== 1 || row.fid == null ||
      selection[kind].length !== 1 || selection[kind][0] !== row.fid ||
      selection.node.length !== 0 || selection[kind === 'arc' ? 'region' : 'arc'].length !== 0) return null;
  return { snapshotKey, version, kind, fid: row.fid, rowID: row.id, revision: snapshot.revision };
}

export function canCarryTopologyQuickSelection(capture: TopologyQuickSelectionCapture, snapshotKey: string | null, version: number,
  selection: TopologySelection, result: GeometryTopologySnapshot): boolean {
  if (capture.snapshotKey !== snapshotKey || capture.version !== version || result.revision !== capture.revision ||
      !result.completion?.draftIdentity || result.completion.sourceRevision.toUpperCase() !== result.revision.toUpperCase() ||
      selection[capture.kind].length !== 1 || selection[capture.kind][0] !== capture.fid || selection.node.length !== 0 ||
      selection[capture.kind === 'arc' ? 'region' : 'arc'].length !== 0) return false;
  const matches = topologyRows(result, capture.kind).filter((row) => row.fid === capture.fid && row.id === capture.rowID);
  return matches.length === 1 && topologySourceRows(result, capture.kind, matches).length === 1;
}

/** React selection identity, separate from the Host's opaque draft and source contracts. */
export function topologySelectionSnapshotKey(owner: string, snapshot?: GeometryTopologySnapshot): string | null {
  const completion = snapshot?.completion;
  if (!snapshot?.revision || !completion?.draftIdentity ||
      completion.sourceRevision.toUpperCase() !== snapshot.revision.toUpperCase()) return null;
  return JSON.stringify([owner, snapshot.revision, snapshot.draftState, completion.sourceRevision, completion.draftIdentity]);
}

export function emptyTopologySelection(): TopologySelection {
  return { node: [], arc: [], region: [] };
}

export function topologyDiagnosticKind(featureKind: string): TopologySelectionKind | null {
  const kind = featureKind.toLowerCase();
  // Host RegionIndex targets resolve completed regions to their source control points.
  return kind === 'controlpoint' ? 'region' : kind === 'node' || kind === 'arc' || kind === 'region' ? kind : null;
}

export function topologyRows(snapshot: GeometryTopologySnapshot, kind: TopologySelectionKind): GeometryRow[] {
  return (kind === 'node' ? snapshot.nodes : kind === 'arc' ? snapshot.arcs : snapshot.regions).rows;
}

function sourceRow(snapshot: GeometryTopologySnapshot, kind: TopologySelectionKind, fid: number): GeometryRow | null {
  if (!Number.isSafeInteger(fid) || fid < 0) return null;
  const matches = topologyRows(snapshot, kind).filter((row) => row.fid === fid);
  const row = matches.length === 1 ? matches[0]! : null;
  return row && topologyRows(snapshot, kind).filter((item) => item.id === row.id).length === 1 ? row : null;
}

export function topologySourceRows(snapshot: GeometryTopologySnapshot, kind: TopologySelectionKind, rows: GeometryRow[]): GeometryRow[] {
  const byFID = new Map<number, GeometryRow | null>();
  const rowIDs = new Map<string, number>();
  for (const row of topologyRows(snapshot, kind)) {
    rowIDs.set(row.id, (rowIDs.get(row.id) ?? 0) + 1);
    if (row.fid != null && Number.isSafeInteger(row.fid) && row.fid >= 0) {
      byFID.set(row.fid, byFID.has(row.fid) ? null : row);
    }
  }
  return rows.filter((row) => row.fid != null && byFID.get(row.fid)?.id === row.id && rowIDs.get(row.id) === 1);
}

/** Never interpret a table index, diagnostic string, or locally added row as a source FID. */
export function topologyTableSelection(snapshot: GeometryTopologySnapshot, kind: TopologySelectionKind, rows: GeometryRow[]): TopologySelection {
  const selection = emptyTopologySelection();
  selection[kind] = [...new Set(topologySourceRows(snapshot, kind, rows).map((row) => row.fid!))];
  return selection;
}

export function topologyTableSelectedRowIDs(kind: TopologySelectionKind, rows: GeometryRow[], snapshot: GeometryTopologySnapshot | undefined,
  selectedFIDs: number[], scopeKey: string, selectionVersion: number, local: TopologyLocalTableSelection | null): string[] {
  const sourceIDs = new Set(snapshot ? topologySourceRows(snapshot, kind, rows).map((row) => row.id) : []);
  const currentLocal = local?.scopeKey === scopeKey && local.version === selectionVersion ? new Set(local.ids) : new Set<string>();
  const selected = new Set(selectedFIDs);
  return rows.filter((row) => sourceIDs.has(row.id) ? selected.has(row.fid!) : currentLocal.has(row.id)).map((row) => row.id);
}

function rowMapFeatures(snapshot: GeometryTopologySnapshot, kind: TopologySelectionKind, row: GeometryRow): GeometryTopologyMapFeature[] {
  return snapshot.map?.features.filter((feature) => feature.featureKind.toLowerCase() === kind &&
    (feature.fid === row.fid || (feature.fid === null && feature.selectionIDs.includes(row.id)))) ?? [];
}

export function topologySelectionGeometry(snapshot: GeometryTopologySnapshot | undefined, selection: TopologySelection): {
  featureIDs: string[];
  extent: GeometryTopologyExtent | null;
} {
  if (!snapshot) return { featureIDs: [], extent: null };
  const features = new Map<string, GeometryTopologyMapFeature>();
  let complete = true;
  for (const kind of ['node', 'arc', 'region'] as const) {
    for (const fid of selection[kind]) {
      const row = sourceRow(snapshot, kind, fid);
      const matches = row ? rowMapFeatures(snapshot, kind, row) : [];
      if (!matches.length) complete = false;
      for (const feature of matches) {
        if (features.has(feature.id) && features.get(feature.id) !== feature) complete = false;
        features.set(feature.id, feature);
      }
    }
  }
  let extent: GeometryTopologyExtent | null = null;
  for (const feature of features.values()) {
    let hasPoint = false;
    for (const path of feature.paths) for (const point of path) {
      if (!Number.isFinite(point.x) || !Number.isFinite(point.y)) { complete = false; continue; }
      hasPoint = true;
      extent = extent ? {
        minX: Math.min(extent.minX, point.x), minY: Math.min(extent.minY, point.y),
        maxX: Math.max(extent.maxX, point.x), maxY: Math.max(extent.maxY, point.y),
      } : { minX: point.x, minY: point.y, maxX: point.x, maxY: point.y };
    }
    if (!hasPoint) complete = false;
  }
  if (extent && ![extent.maxX - extent.minX, extent.maxY - extent.minY].every(Number.isFinite)) complete = false;
  return { featureIDs: [...features.keys()], extent: complete ? extent : null };
}

/** A diagnostic may address a completed region index; only the Host-resolved source target is selectable. */
export function resolveTopologyDiagnosticTarget(snapshot: GeometryTopologySnapshot | undefined, messageID: string, target: TopologyDiagnosticTarget): {
  kind: TopologySelectionKind;
  row: GeometryRow;
  selection: TopologySelection;
  extent: GeometryTopologyExtent;
} | null {
  if (!snapshot || !topologySelectionSnapshotKey('', snapshot)) return null;
  const kind = topologyDiagnosticKind(target.featureKind);
  if (!kind) return null;
  const message = snapshot.completion?.messages.find((item) => item.id === messageID);
  const retained = message?.targets?.some((item) => item.featureKind === target.featureKind && item.fid === target.fid &&
    item.tableRowID === target.tableRowID && item.mapFeatureIDs.length === target.mapFeatureIDs.length &&
    item.mapFeatureIDs.every((id, index) => id === target.mapFeatureIDs[index]));
  const row = sourceRow(snapshot, kind, target.fid);
  if (!retained || !row || row.id !== target.tableRowID || !target.mapFeatureIDs.length) return null;
  const selection = topologyTableSelection(snapshot, kind, [row]);
  const geometry = topologySelectionGeometry(snapshot, selection);
  if (!geometry.extent || target.mapFeatureIDs.some((id) => !geometry.featureIDs.includes(id))) return null;
  return { kind, row, selection, extent: geometry.extent };
}

/** Camera padding is presentation only; selected bounds above remain exact source coordinates. */
export function topologySelectionCamera(extent: GeometryTopologyExtent, sourceExtent: GeometryTopologyExtent | null,
  size: { width: number; height: number }): { centerX: number; centerY: number; worldUnitsPerPixel: number } | null {
  if (![size.width, size.height].every((value) => Number.isFinite(value) && value > 0)) return null;
  const spans = (bounds: GeometryTopologyExtent): [number, number] => [bounds.maxX - bounds.minX, bounds.maxY - bounds.minY];
  const [width, height] = spans(extent);
  if (![extent.minX, extent.minY, extent.maxX, extent.maxY, width, height].every(Number.isFinite) || width < 0 || height < 0) return null;
  let scale = Math.max(width / size.width, height / size.height) * 1.25;
  if (scale === 0 && sourceExtent) {
    const [sourceWidth, sourceHeight] = spans(sourceExtent);
    // Source TopologyVisualContextBehaviors uses a fraction of the topology extent for a point.
    if ([sourceWidth, sourceHeight].every((value) => Number.isFinite(value) && value >= 0)) {
      scale = Math.max(sourceWidth / size.width, sourceHeight / size.height) / 20;
    }
  }
  if (!Number.isFinite(scale) || scale <= 0) return null;
  return { centerX: extent.minX + width / 2, centerY: extent.minY + height / 2, worldUnitsPerPixel: scale };
}
