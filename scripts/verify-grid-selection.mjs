import assert from 'node:assert/strict';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { build } from 'vite';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const directory = await mkdtemp(path.join(tmpdir(), 'hecras-grid-selection-'));
let checks = 0;
try {
  await build({ configFile: false, root, logLevel: 'silent', build: {
    lib: { entry: path.join(root, 'frontend/src/controls/dataGridSelection.ts'), formats: ['es'], fileName: () => 'selection.mjs' },
    outDir: directory, emptyOutDir: true, minify: false, sourcemap: false,
  } });
  const { gridArrowDestination: destination, planGridCellEdits: plan, sameGridClipboardScope: sameScope } =
    await import(pathToFileURL(path.join(directory, 'selection.mjs')).href);
  const equal = (actual, expected, message) => { assert.deepEqual(actual, expected, message); checks++; };
  const from = { rowIndex: 52, columnIndex: 3 };
  equal(destination(from, 'ArrowUp', 121, 8, true), { rowIndex: 0, columnIndex: 3 }, 'Ctrl-Up reaches the first displayed row.');
  equal(destination(from, 'ArrowDown', 121, 8, true), { rowIndex: 120, columnIndex: 3 }, 'Ctrl-Down reaches the last displayed row across pages.');
  equal(destination(from, 'ArrowLeft', 121, 8, true), { rowIndex: 52, columnIndex: 0 }, 'Ctrl-Left reaches the first displayed column.');
  equal(destination(from, 'ArrowRight', 121, 8, true), { rowIndex: 52, columnIndex: 7 }, 'Ctrl-Right reaches the last displayed column.');
  equal(Math.floor(destination(from, 'ArrowDown', 121, 8, true).rowIndex / 50), 2, 'The focus destination is the third page.');
  equal(destination(from, 'ArrowDown', 121, 8, false), { rowIndex: 53, columnIndex: 3 }, 'An ordinary arrow moves one row.');
  equal(destination(from, 'ArrowLeft', 121, 8, false), { rowIndex: 52, columnIndex: 2 }, 'An ordinary arrow moves one column.');
  equal(destination({ rowIndex: 0, columnIndex: 0 }, 'ArrowUp', 1, 1, false), { rowIndex: 0, columnIndex: 0 }, 'Navigation clamps to a one-cell view.');
  equal(destination(from, 'ArrowDown', 0, 8, true), null, 'An empty view has no target.');
  equal(destination(from, 'ArrowUp', 20, 8, true), null, 'A filtered-away origin cannot address another source row.');
  equal(destination({ rowIndex: 0.5, columnIndex: 0 }, 'ArrowDown', 3, 1, true), null, 'Fractional positions cannot navigate.');

  const source = [{ id: 'source-9', value: 'first' }, { id: 'source-2', value: 'hidden' }, { id: 'source-40', value: '' }];
  const view = [source[2], source[0]];
  const columns = [{ id: 'Key', readOnly: true }, { id: 'Value' }, { id: 'Unsupported' }];
  const getID = row => row.id;
  const canEdit = (row, column) => row.id !== 'source-9' && column.id === 'Value';
  const edits = [
    { identity: { rowID: 'source-40', columnID: 'Value' }, value: '' },
    { identity: { rowID: 'source-9', columnID: 'Value' }, value: '' },
    { identity: { rowID: 'source-40', columnID: 'Key' }, value: '' },
    { identity: { rowID: 'source-40', columnID: 'Unsupported' }, value: '' },
    { identity: { rowID: 'source-2', columnID: 'Value' }, value: '' },
    { identity: { rowID: 'source-40', columnID: 'removed-column' }, value: '' },
  ];
  const cleared = plan(edits, view, columns, getID, canEdit);
  equal(cleared.accepted.map(edit => [edit.row.id, edit.column.id, edit.value]), [['source-40', 'Value', '']],
    'Clearing preserves the visible source ID and sends the empty string through the existing editor contract.');
  equal(cleared.skipped, 5, 'Read-only, adapter-rejected, hidden and removed cells are skipped.');
  equal(cleared.accepted[0].row === source[2], true, 'The callback receives the original row object, not a view ordinal.');
  equal(plan([...edits, edits[0]], view, columns, getID, canEdit).accepted.length, 1, 'Duplicate cell keys produce only one edit.');
  const firstVisible = view[destination({ rowIndex: 1, columnIndex: 1 }, 'ArrowUp', view.length, columns.length, true).rowIndex];
  equal(firstVisible.id, 'source-40', 'A sorted filtered boundary resolves to its source row ID, including empty values.');
  const multi = plan(view.map(row => ({ identity: { rowID: row.id, columnID: 'Value' }, value: ' 0001\t中文\n' })),
    view, columns, getID, () => true);
  const calls = [];
  for (const edit of multi.accepted) {
    view.reverse();
    calls.push([edit.row.id, edit.column.id, edit.value]);
  }
  equal(calls, [['source-40', 'Value', ' 0001\t中文\n'], ['source-9', 'Value', ' 0001\t中文\n']],
    'A callback reordering the view cannot redirect subsequent edits or normalize clipboard text.');

  const scope = { rows: source, columns, resetKey: 'source-A', viewKey: '40,9:page-0', selectionKey: '40:Value',
    interactionVersion: 4, adapters: [canEdit, getID] };
  equal(sameScope(scope, { ...scope, adapters: [...scope.adapters] }), true, 'An unchanged source/selection accepts its read.');
  for (const changed of [
    { rows: [...source] }, { columns: [...columns] }, { resetKey: 'source-B' },
    { viewKey: '9,40:page-0' }, { viewKey: '40,9:page-1' }, { selectionKey: '9:Value' },
    { interactionVersion: 6 }, { adapters: [() => true, getID] },
  ]) equal(sameScope(scope, { ...scope, ...changed }), false, 'Changed rows, owner, view, selection, generation or adapter rejects stale clipboard work.');
  console.log(JSON.stringify({ state: 'passed', checks,
    scope: 'display-boundaries-source-ID-edit-plans-and-clipboard-scope; DOM-focus-and-system-clipboard-not-covered' }));
} finally { await rm(directory, { recursive: true, force: true }); }
