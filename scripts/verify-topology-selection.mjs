import assert from 'node:assert/strict';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { build } from 'vite';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const temporary = await mkdtemp(path.join(tmpdir(), 'hecras-topology-selection-'));
try {
  await build({ configFile: false, root, logLevel: 'silent', build: {
    lib: { entry: path.join(root, 'frontend/src/features/geometry/geometryTopologySelection.ts'), formats: ['es'], fileName: () => 'selection.mjs' },
    outDir: temporary, emptyOutDir: true, minify: false, sourcemap: false,
  } });
  const {
    emptyTopologySelection, topologySelectionSnapshotKey, topologyTableSelection, topologySelectionGeometry,
    resolveTopologyDiagnosticTarget, topologySelectionCamera, topologyTableSelectedRowIDs,
  } = await import(pathToFileURL(path.join(temporary, 'selection.mjs')).href);
  const table = (featureKind, rows) => ({ featureKind, rows, columns: [], editableFields: [] });
  const row = (id, fid) => ({ id, fid, values: {} });
  const feature = (id, featureKind, fid, geometryType, selectionIDs, paths) => ({ id, featureKind, fid, geometryType, selectionIDs, paths });
  const point = (x, y) => ({ x, y });
  const target = { featureKind: 'arc', fid: 19, tableRowID: 'source-arc-19', mapFeatureIDs: ['actual-line-19'] };
  const controlPointTarget = { featureKind: 'controlPoint', fid: 43, tableRowID: 'source-region-43', mapFeatureIDs: ['actual-boundary', 'actual-control-point-43'] };
  const snapshot = {
    revision: 'AB12', draftState: 'opaque-draft-A', canEdit: true, isDirty: false,
    nodes: table('node', [row('source-node-7', 7)]),
    arcs: table('arc', [row('source-arc-19', 19)]),
    regions: table('region', [row('source-region-43', 43), row('source-region-91', 91)]),
    map: { extent: { minX: -20, minY: -10, maxX: 80, maxY: 90 }, features: [
      feature('actual-node-7', 'node', 7, 'point', ['source-node-7'], [[point(2, 3)]]),
      feature('actual-line-19', 'arc', 19, 'line', ['source-arc-19'], [[point(1, 4), point(-5, 12), point(9, 2)]]),
      // A completed region shared by two source control points has no invented polygon FID.
      feature('actual-boundary', 'region', null, 'polygon', ['source-region-43', 'source-region-91'], [
        [point(-3, -2), point(8, -2), point(8, 9), point(-3, 9), point(-3, -2)],
        [point(1, 1), point(2, 1), point(2, 2), point(1, 1)],
      ]),
      feature('actual-control-point-43', 'region', 43, 'point', ['source-region-43'], [[point(4, 4)]]),
      feature('actual-control-point-91', 'region', 91, 'point', ['source-region-91'], [[point(5, 5)]]),
    ] },
    completion: { sourceRevision: 'ab12', draftIdentity: 'draft-hash-A', isComplete: false,
      messages: [{ id: 'diagnostic-A', code: 'ArcsIntersect', severity: 'Error', text: 'Original diagnostic text', targets: [target] },
        { id: 'diagnostic-region', code: 'RegionError', severity: 'Error', text: 'Original region diagnostic', targets: [controlPointTarget] }] },
  };
  const original = JSON.stringify(snapshot);
  const sourceKey = topologySelectionSnapshotKey('project-session/asset-A', snapshot);
  assert.ok(sourceKey, 'Case differences in Host SHA spelling do not invalidate the same source revision.');
  for (const changed of [
    { ...snapshot, revision: 'other' },
    { ...snapshot, completion: { ...snapshot.completion, sourceRevision: 'other' } },
    { ...snapshot, completion: null },
    { ...snapshot, completion: { ...snapshot.completion, draftIdentity: '' } },
  ]) assert.equal(topologySelectionSnapshotKey('project-session/asset-A', changed), null);
  assert.notEqual(sourceKey, topologySelectionSnapshotKey('other-session/asset-A', snapshot));
  assert.notEqual(sourceKey, topologySelectionSnapshotKey('project-session/asset-B', snapshot));
  assert.notEqual(sourceKey, topologySelectionSnapshotKey('project-session/asset-A', { ...snapshot, draftState: 'opaque-draft-B' }));
  assert.notEqual(sourceKey, topologySelectionSnapshotKey('project-session/asset-A', {
    ...snapshot, completion: { ...snapshot.completion, draftIdentity: 'draft-hash-B' },
  }));

  assert.deepEqual(topologyTableSelection(snapshot, 'arc', [snapshot.arcs.rows[0], snapshot.arcs.rows[0],
    row('local-19', 19), row('source-arc-19', 0), row('local-1', -1), row('local-huge', 2 ** 53)]),
  { node: [], arc: [19], region: [] }, 'Only the actual unique source row and FID may select; row order and local labels are not identities.');
  assert.deepEqual(topologyTableSelection({ ...snapshot, arcs: table('arc', [...snapshot.arcs.rows, row('duplicate', 19)]) }, 'arc', snapshot.arcs.rows),
    emptyTopologySelection(), 'An ambiguous FID is not selectable.');
  const localRows = [...snapshot.arcs.rows, row('local-L', -1)];
  const localOnly = { scopeKey: sourceKey, version: 10, ids: ['local-L'] };
  assert.deepEqual(topologyTableSelectedRowIDs('arc', localRows, snapshot, [], sourceKey, 10, localOnly), ['local-L']);
  assert.deepEqual(topologyTableSelectedRowIDs('arc', localRows, snapshot, [19], sourceKey, 11, localOnly), ['source-arc-19']);
  assert.deepEqual(topologyTableSelectedRowIDs('arc', localRows, snapshot, [], sourceKey, 12, localOnly), [],
    'A local row must not reappear when a later map selection returns to the same empty FID set.');
  assert.deepEqual(topologyTableSelectedRowIDs('arc', localRows, snapshot, [], sourceKey, 11, localOnly), [],
    'Map Replace with an already empty source selection must still invalidate local-only rows.');
  assert.deepEqual(topologyTableSelectedRowIDs('arc', localRows, snapshot, [19], sourceKey, 13,
    { scopeKey: sourceKey, version: 13, ids: ['source-arc-19', 'local-L'] }), ['source-arc-19', 'local-L'],
  'A table submission records the accepted generation and retains its own mixed local/source selection.');
  assert.deepEqual(topologyTableSelectedRowIDs('arc', localRows, snapshot, [], 'new-source-or-draft', 10, localOnly), []);
  const arcSelection = { node: [], arc: [19], region: [] };
  assert.deepEqual(topologySelectionGeometry(snapshot, arcSelection), {
    featureIDs: ['actual-line-19'], extent: { minX: -5, minY: 2, maxX: 9, maxY: 12 },
  }, 'The full real polyline, including its bend, determines the bounds.');
  assert.deepEqual(topologySelectionGeometry(snapshot, { node: [], arc: [], region: [43] }), {
    featureIDs: ['actual-boundary', 'actual-control-point-43'], extent: { minX: -3, minY: -2, maxX: 8, maxY: 9 },
  }, 'A null-FID region polygon is resolved through exact source row selectionIDs.');
  assert.equal(topologySelectionGeometry(snapshot, { node: [], arc: [0], region: [] }).extent, null);
  assert.equal(topologySelectionGeometry({ ...snapshot, map: null }, arcSelection).extent, null);
  const badCoordinates = structuredClone(snapshot);
  badCoordinates.map.features.find((item) => item.id === 'actual-line-19').paths[0][1].x = NaN;
  assert.equal(topologySelectionGeometry(badCoordinates, arcSelection).extent, null, 'Do not fit a partial geometry after dropping a bad coordinate.');
  const missingCoordinates = structuredClone(snapshot);
  missingCoordinates.map.features.find((item) => item.id === 'actual-line-19').paths = [];
  assert.equal(topologySelectionGeometry(missingCoordinates, arcSelection).extent, null);

  assert.deepEqual(resolveTopologyDiagnosticTarget(snapshot, 'diagnostic-A', target)?.selection, arcSelection);
  assert.deepEqual(resolveTopologyDiagnosticTarget(snapshot, 'diagnostic-region', controlPointTarget)?.selection,
    { node: [], arc: [], region: [43] }, 'Host controlPoint targets select the Regions source FID, never a completed-region ordinal.');
  assert.equal(resolveTopologyDiagnosticTarget(snapshot, 'previous-diagnostic', target), null);
  for (const changed of [
    { ...target, fid: 0 }, { ...target, tableRowID: 'source-arc-0' },
    { ...target, mapFeatureIDs: ['actual-node-7'] }, { ...target, mapFeatureIDs: [] },
  ]) assert.equal(resolveTopologyDiagnosticTarget(snapshot, 'diagnostic-A', changed), null, 'A target must belong to the current diagnostic message.');
  const mismatchedTargetSnapshot = structuredClone(snapshot);
  mismatchedTargetSnapshot.completion.messages[0].targets[0].tableRowID = 'source-node-7';
  assert.equal(resolveTopologyDiagnosticTarget(mismatchedTargetSnapshot, 'diagnostic-A', mismatchedTargetSnapshot.completion.messages[0].targets[0]), null);
  assert.equal(resolveTopologyDiagnosticTarget(badCoordinates, 'diagnostic-A', target), null);
  assert.equal(resolveTopologyDiagnosticTarget({ ...snapshot, completion: { ...snapshot.completion, sourceRevision: 'stale' } }, 'diagnostic-A', target), null);

  const arcBounds = topologySelectionGeometry(snapshot, arcSelection).extent;
  const fitted = topologySelectionCamera(arcBounds, snapshot.map.extent, { width: 1000, height: 500 });
  assert.deepEqual(fitted, { centerX: 2, centerY: 7, worldUnitsPerPixel: 0.025 });
  const nodeBounds = topologySelectionGeometry(snapshot, { node: [7], arc: [], region: [] }).extent;
  assert.deepEqual(nodeBounds, { minX: 2, minY: 3, maxX: 2, maxY: 3 });
  assert.deepEqual(topologySelectionCamera(nodeBounds, snapshot.map.extent, { width: 1000, height: 500 }),
    { centerX: 2, centerY: 3, worldUnitsPerPixel: 0.01 }, 'Point camera scale is derived from the current real topology extent.');
  assert.equal(topologySelectionCamera(nodeBounds, null, { width: 1000, height: 500 }), null);
  assert.equal(topologySelectionCamera(nodeBounds, nodeBounds, { width: 1000, height: 500 }), null);
  assert.equal(topologySelectionCamera(arcBounds, snapshot.map.extent, { width: 0, height: 500 }), null);
  assert.equal(topologySelectionCamera({ ...arcBounds, maxX: Infinity }, snapshot.map.extent, { width: 1000, height: 500 }), null);
  assert.equal(JSON.stringify(snapshot), original, 'Selection, diagnostics, and camera calculations must not mutate the Host snapshot.');
  console.log('PASS topology selection source identity, current diagnostic targets, exact geometry bounds, and finite camera checks (component logic only).');
} finally {
  await rm(temporary, { recursive: true, force: true });
}
