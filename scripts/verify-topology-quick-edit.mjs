import { spawn } from 'node:child_process';
import { access, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'vite';

// React/Host-contract fixture only: this does not claim real HDF persistence.
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const temporary = await mkdtemp(path.join(tmpdir(), 'hecras-quick-edit-'));
let browser, exited;
try {
  const chrome = process.env.HECRAS_CHROME ?? '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
  await access(chrome);
  const entry = path.join(temporary, 'entry.ts');
  const src = name => JSON.stringify(path.join(root, `frontend/src/features/geometry/${name}`));
  await writeFile(entry, `import { TopologyArcQuickEditPicker } from ${src('TopologyArcQuickEditPicker.tsx')};
import { TopologyRegionQuickEditPicker } from ${src('TopologyRegionQuickEditPicker.tsx')};
import { useGeometryWorkbench } from ${src('useGeometryWorkbench.ts')};
import { useGeometryTopologyMap } from ${src('useGeometryTopologyMap.ts')};
import { createElement, useState } from ${JSON.stringify(path.join(root, 'node_modules/react/index.js'))};
import { createRoot } from ${JSON.stringify(path.join(root, 'node_modules/react-dom/client.js'))};
export { TopologyArcQuickEditPicker, TopologyRegionQuickEditPicker, useGeometryWorkbench, useGeometryTopologyMap, createElement, useState, createRoot };`);
  await build({ configFile: false, root, logLevel: 'silent', define: { 'process.env.NODE_ENV': '"production"' }, build: {
    lib: { entry, formats: ['iife'], name: 'QuickEditFixture', fileName: () => 'fixture.js' },
    outDir: path.join(temporary, 'bundle'), minify: false,
  } });
  browser = spawn(chrome, ['--headless=new', '--no-first-run', '--no-default-browser-check', '--disable-background-networking',
    '--disable-component-update', '--disable-sync', '--disable-extensions', '--remote-debugging-pipe',
    `--user-data-dir=${path.join(temporary, 'profile')}`, 'about:blank'], { stdio: ['ignore', 'ignore', 'pipe', 'pipe', 'pipe'] });
  exited = new Promise(resolve => browser.once('exit', resolve));
  let sequence = 0, incoming = '', diagnostic = '';
  const pending = new Map();
  browser.stderr.on('data', bytes => { diagnostic += bytes.toString(); });
  const failPending = error => { for (const item of pending.values()) { clearTimeout(item.timeout); item.reject(error); } pending.clear(); };
  browser.on('error', failPending);
  browser.stdio[3].on('error', failPending);
  browser.on('exit', code => failPending(new Error(`Headless Chrome exited (${code}): ${diagnostic}`)));
  browser.stdio[4].on('data', bytes => {
    incoming += bytes.toString();
    let end;
    while ((end = incoming.indexOf('\0')) !== -1) {
      const response = JSON.parse(incoming.slice(0, end)); incoming = incoming.slice(end + 1);
      const item = pending.get(response.id);
      if (item) { pending.delete(response.id); clearTimeout(item.timeout); response.error ? item.reject(new Error(JSON.stringify(response.error))) : item.resolve(response.result); }
    }
  });
  const command = (method, params = {}, sessionId) => new Promise((resolve, reject) => {
    const id = ++sequence, timeout = setTimeout(() => { pending.delete(id); reject(new Error(`CDP timeout: ${method}`)); }, 30000);
    pending.set(id, { resolve, reject, timeout }); browser.stdio[3].write(`${JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) })}\0`);
  });
  const target = await command('Target.createTarget', { url: 'about:blank' });
  const session = await command('Target.attachToTarget', { targetId: target.targetId, flatten: true });
  const evaluate = async expression => {
    const response = await command('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true }, session.sessionId);
    if (response.exceptionDetails) throw new Error(response.exceptionDetails.exception?.description ?? response.exceptionDetails.text);
    return response.result.value;
  };
  await evaluate(`document.body.innerHTML = '<div id="root"></div>'`);
  await evaluate(await readFile(path.join(temporary, 'bundle/fixture.js'), 'utf8'));
  // These five field records are copied unchanged from a real Host topology.load receipt.
  const hostFields = JSON.parse(await readFile(path.join(root, 'scripts/fixtures/topology-quick-edit-host-fields.json'), 'utf8'));
  const result = await evaluate(`(${browserChecks.toString()})(QuickEditFixture, ${JSON.stringify(hostFields)})`);
  console.log(JSON.stringify({ passed: true, scope: 'browser React lifecycle and mocked topology Host contract only', checks: result }, null, 2));
  await command('Browser.close');
} finally {
  if (browser && browser.exitCode === null) browser.kill('SIGTERM');
  if (exited) await Promise.race([exited, new Promise(resolve => setTimeout(resolve, 3000))]);
  await rm(temporary, { recursive: true, force: true });
}

async function browserChecks(A, hostFields) {
  const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
  const check = (condition, reason) => { if (!condition) throw new Error(`${reason}\n${document.body.innerText}`); };
  const until = async (predicate, reason) => { for (let i = 0; i < 150; i++) { if (predicate()) return; await wait(15); } check(false, reason); };
  const clone = value => structuredClone(value), calls = [];
  const table = (kind, rows, editableFields) => ({ featureKind: kind, columns: [], rows, editableFields });
  const row = (kind, fid, values) => ({ id: `${kind}:${fid}`, fid, values });
  let serial = 0, release, hold = false, undoStack = [], redoStack = [];
  let state = {
    revision: 'AB12', draftState: 'draft-0', canEdit: true, isDirty: false, canUndo: false, canRedo: false,
    nodes: table('node', [], []),
    arcs: table('arc', [row('arc', 11, { SizeFunction: 'Constant', ConstantSize: '12', PointCount: '8' }),
      row('arc', 29, { SizeFunction: 'Constant', ConstantSize: '31', PointCount: '5' })],
    clone(hostFields.arcs)),
    regions: table('region', [row('region', 7, { PatchType: 'TrianglePaver', ConstantSize: '20' })],
      clone(hostFields.regions)),
    completion: { sourceRevision: 'AB12', draftIdentity: 'hash-0', isComplete: true, messages: [] }, map: null,
  };
  const stamp = (next) => {
    serial++; next.draftState = `draft-${serial}`; next.completion.draftIdentity = `hash-${serial}`;
    next.isDirty = true; next.canUndo = undoStack.length > 0; next.canRedo = redoStack.length > 0; state = next; return clone(state);
  };
  const mutate = (request) => {
    const mutation = request.mutation;
    if (mutation.operation === 'undo' || mutation.operation === 'redo') {
      check(mutation.featureKind === 'history' && mutation.featureIDs.length === 0 && mutation.arcFeatureIDs.length === 0 &&
        mutation.controlPointFeatureIDs.length === 0 && mutation.nodeFeatureIDs.length === 0, 'invalid Host history command');
      if (mutation.operation === 'undo') { const previous = undoStack.pop(); redoStack.push(clone(state)); return stamp(previous); }
      const next = redoStack.pop(); undoStack.push(clone(state)); return stamp(next);
    }
    undoStack.push(clone(state)); redoStack = [];
    const next = clone(state), rows = mutation.featureKind === 'arc' ? next.arcs.rows : next.regions.rows;
    const target = rows.find(item => item.fid === mutation.featureIDs[0]);
    check(target, 'fake Host received an unknown feature');
    if (mutation.operation === 'setField' || mutation.operation === 'setRegionInput')
      target.values[mutation.fieldID] = mutation.fieldID === 'ConstantSize' && mutation.value === '' ? 'NaN' : mutation.value;
    else if (mutation.operation === 'halfSize' || mutation.operation === 'doubleSize') {
      const factor = mutation.operation === 'halfSize' ? .5 : 2;
      target.values.ConstantSize = String(Number(target.values.ConstantSize) * factor);
    } else throw new Error(`Unexpected mutation ${mutation.operation}`);
    return stamp(next);
  };
  const cleanMesh = { revision: 'AB12', draftState: 'mesh-clean', topologyFingerprint: 'fixture-topology', canEdit: true,
    isDirty: false, flowAreas: [], regions: table('region', [], []), selectedRegionIDs: [], patchSizing: null,
    generationReport: null, prepareSummary: null };
  const cleanCulvert = { revision: 'AB12', draftState: 'culvert-clean', canEdit: true, isDirty: false,
    barrels: table('barrel', [], []), barrelProperties: table('barrelProperties', [], []),
    openingProperties: table('openingProperties', [], []), profiles: [] };
  const topologySignature = () => JSON.stringify([state.arcs.rows.map(item => item.values), state.regions.rows.map(item => item.values)]);
  let dirtyMesh, meshFingerprint, meshLoadOverride, failNextSave = false;
  const backend = { supports: method => ['model.load', 'model.save', 'topology.load', 'topology.edit',
    'geometry.mesh.load', 'geometry.mesh.edit', 'geometry.culverts.load'].includes(method),
    request(method, parameters) {
      calls.push({ method, parameters: clone(parameters) });
      if (method === 'model.load') return Promise.resolve({ revision: 'AB12', images: {}, sourceState: null });
      if (method === 'topology.load') return Promise.resolve(clone(state));
      if (method === 'topology.edit') {
        if (hold) { hold = false; return new Promise(resolve => { release = () => resolve(mutate(parameters)); }); }
        return Promise.resolve(mutate(parameters));
      }
      if (method === 'geometry.culverts.load') return Promise.resolve(clone(cleanCulvert));
      if (method === 'geometry.mesh.load') {
        if (parameters.draftState === 'mesh-dirty') return Promise.resolve(clone(dirtyMesh));
        return Promise.resolve(clone(parameters.draftState === 'mesh-clean' ? cleanMesh : meshLoadOverride ?? cleanMesh));
      }
      if (method === 'geometry.mesh.edit') {
        check(parameters.mutation.operation === 'prepare', 'unexpected mesh edit');
        meshFingerprint = topologySignature();
        dirtyMesh = { ...clone(cleanMesh), draftState: 'mesh-dirty', isDirty: true };
        return Promise.resolve(clone(dirtyMesh));
      }
      if (method === 'model.save') {
        if (failNextSave) { failNextSave = false; return Promise.reject(new Error('Injected parent save failure.')); }
        if (parameters.geometryMeshState === 'mesh-dirty' && meshFingerprint !== topologySignature())
          return Promise.reject(new Error('The source topology changed after this mesh draft was created.'));
        if (parameters.geometryMeshState === 'mesh-other-revision')
          return Promise.reject(new Error('The mesh draft belongs to a different source revision.'));
        return Promise.resolve({ revision: 'CD34', images: {}, sourceState: null });
      }
      throw new Error(`Unexpected ${method}`);
    } };
  const context = { projectSessionID: 'fixture-session', projectPath: '/fixture/project.ras', projectRevision: 'A', projectXml: null,
    selectedAsset: { kind: 'Geometry', path: '/fixture/geometry.h5', revision: 'AB12' } };
  let api;
  const h = A.createElement;
  const onContextChange = () => {};
  function Fixture() {
    const [kind, setKind] = A.useState('arc');
    const w = A.useGeometryWorkbench({ language: 'zh', context, backend, onContextChange });
    const map = A.useGeometryTopologyMap({ active: false, owner: w.ownerKey, backend, context, model: w.model, topology: w.topology,
      canEdit: !!w.topology?.canEdit, busy: false, onLoadArcPoints: w.loadArcPoints, onMapCommand: w.editTopologyMapCommand });
    api = { w, map, setKind };
    const ids = map.canvasProps.selectedFIDByKind[kind];
    const selected = (kind === 'arc' ? w.topology?.arcs.rows : w.topology?.regions.rows)?.filter(item => ids.includes(item.fid)) ?? [];
    const edit = async (item, change) => {
      const capture = map.captureQuickSelection(kind, item);
      if (!capture) throw new Error('stale selection');
      await w.quickEditTopology(kind, item, change, () => map.isQuickSelectionCurrent(capture), result => map.carryQuickSelection(capture, result));
    };
    const component = kind === 'arc' ? A.TopologyArcQuickEditPicker : A.TopologyRegionQuickEditPicker;
    return h(component, { language: 'zh', rows: selected, snapshot: w.topology, canEdit: true, busy: false, onEdit: edit });
  }
  A.createRoot(document.getElementById('root')).render(h(Fixture));
  await until(() => api?.w.topology && api.w.mesh && api.w.culverts && api.map.selectionSnapshotKey, 'fixture load');
  const select = (kind, ids) => {
    api.setKind(kind);
    const table = kind === 'arc' ? api.w.topology.arcs : api.w.topology.regions;
    api.map.onTableSelectionChange(kind, table.rows.filter(item => ids.includes(item.fid)));
  };
  const choice = label => document.querySelector(`select[aria-label="${label}"]`);
  const input = label => document.querySelector(`input[aria-label="${label}"]`);
  const changeChoice = (label, value) => { const element = choice(label); element.value = value; element.dispatchEvent(new Event('change', { bubbles: true })); };
  const changeText = (element, value) => {
    element.focus(); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(element, value);
    element.dispatchEvent(new Event('input', { bubbles: true }));
  };
  const commit = element => element.dispatchEvent(new KeyboardEvent('keydown', { bubbles: true, key: 'Enter' }));
  const edits = () => calls.filter(call => call.method === 'topology.edit');
  select('arc', [11, 29]);
  await until(() => document.body.innerText.includes('多个要素'), 'multi-selection message');
  check(!choice('尺寸函数'), 'multiple arcs must not produce a writable first-row editor');
  select('arc', [11]);
  await until(() => choice('尺寸函数') && !choice('尺寸函数').disabled, 'single arc editor');
  check([...choice('尺寸函数').options].map(option => option.value).join(',') === 'Constant,CountWithBias,Unset', 'choices must be exactly Host provided');
  const selectedArc = api.w.topology.arcs.rows[0], enumCapture = api.map.captureQuickSelection('arc', selectedArc);
  const beforeUnknown = edits().length;
  let rejectedUnknown = false;
  await api.w.quickEditTopology('arc', selectedArc, { operation: 'setField', fieldID: 'SizeFunction', value: 'not-a-host-choice' },
    () => api.map.isQuickSelectionCurrent(enumCapture), result => api.map.carryQuickSelection(enumCapture, result))
    .catch(() => { rejectedUnknown = true; });
  check(rejectedUnknown && edits().length === beforeUnknown, 'unknown enum value must not reach the Host');
  state.arcs.editableFields.find(field => field.id === 'SizeFunction').choices = [];
  api.w.reload();
  await until(() => api.w.topology?.arcs.editableFields.find(field => field.id === 'SizeFunction')?.choices.length === 0 && api.map.selectionSnapshotKey,
    'fixture reload with unavailable enum choices');
  select('arc', [11]);
  await until(() => choice('尺寸函数')?.disabled, 'missing Host choices must disable the enum control');
  const noChoicesArc = api.w.topology.arcs.rows[0], noChoicesCapture = api.map.captureQuickSelection('arc', noChoicesArc);
  let rejectedMissing = false;
  await api.w.quickEditTopology('arc', noChoicesArc, { operation: 'setField', fieldID: 'SizeFunction', value: 'Constant' },
    () => api.map.isQuickSelectionCurrent(noChoicesCapture), result => api.map.carryQuickSelection(noChoicesCapture, result))
    .catch(() => { rejectedMissing = true; });
  check(rejectedMissing && edits().length === beforeUnknown, 'an enum without Host choices must not be submitted');
  state.arcs.editableFields = clone(hostFields.arcs);
  api.w.reload();
  await until(() => api.w.topology?.arcs.editableFields.find(field => field.id === 'SizeFunction')?.choices.length && api.map.selectionSnapshotKey,
    'fixture reload with original Host choices');
  select('arc', [11]);
  await until(() => choice('尺寸函数') && !choice('尺寸函数').disabled, 'reselect source arc');
  changeChoice('尺寸函数', 'CountWithBias');
  await until(() => api.w.topology.arcs.rows[0].values.SizeFunction === 'CountWithBias' && !input('点数').closest('[hidden]'), 'choice and selection must survive edit receipt');
  check(api.map.canvasProps.selectedFIDByKind.arc[0] === 11, 'edit receipt lost source selection');
  const beforeInvalid = edits().length;
  changeText(input('点数'), '1e'); input('点数').blur();
  await wait(60);
  check(input('点数').value === '1e' && edits().length === beforeInvalid && input('点数').getAttribute('aria-invalid') === 'true', 'unfinished integer was lost or submitted');
  changeText(input('点数'), '24'); commit(input('点数'));
  await until(() => api.w.topology.arcs.rows[0].values.PointCount === '24', 'point count edit');
  check(edits().at(-1).parameters.mutation.featureIDs.join(',') === '11', 'point edit changed wrong FID');
  changeChoice('尺寸函数', 'Constant');
  await until(() => api.w.topology.arcs.rows[0].values.SizeFunction === 'Constant' && !input('固定尺寸').closest('[hidden]'), 'constant mode');
  changeText(input('固定尺寸'), ''); commit(input('固定尺寸'));
  await until(() => api.w.topology.arcs.rows[0].values.ConstantSize === 'NaN', 'blank arc size');
  check(edits().at(-1).parameters.mutation.value === '', 'blank arc size must use an empty string, not a fabricated value');
  select('region', [7]);
  await until(() => choice('网格类型') && !choice('网格类型').disabled, 'region editor');
  changeChoice('网格类型', 'Cartesian');
  await until(() => api.w.topology.regions.rows[0].values.PatchType === 'Cartesian', 'region patch type');
  changeText(input('固定尺寸'), ''); commit(input('固定尺寸'));
  await until(() => api.w.topology.regions.rows[0].values.ConstantSize === 'NaN', 'region size');
  check(edits().at(-1).parameters.mutation.operation === 'setRegionInput', 'region input used the wrong draft operation');
  select('arc', [11]);
  await until(() => choice('尺寸函数') && !choice('尺寸函数').disabled, 'arc reselection');
  hold = true;
  changeChoice('尺寸函数', 'Unset');
  await until(() => release, 'held edit');
  select('arc', [29]);
  await until(() => api.map.canvasProps.selectedFIDByKind.arc[0] === 29, 'switch selection while waiting');
  release();
  await until(() => api.w.topology.arcs.rows[0].values.SizeFunction === 'Unset', 'held receipt');
  await wait(60);
  check(!api.map.canvasProps.selectedFIDByKind.arc.includes(11), 'stale receipt restored previous FID');
  select('arc', [29]);
  await until(() => input('固定尺寸') && input('固定尺寸').value === '31', 'new FID must not inherit previous raw input');
  const old = api.w.topology.arcs.rows[1].values.ConstantSize;
  await api.w.undoTopology(); await until(() => api.w.topology.canRedo, 'undo receipt');
  await api.w.redoTopology(); await until(() => api.w.topology.arcs.rows[1].values.ConstantSize === old, 'redo receipt');
  const draftForSave = api.w.topology.draftState;
  await api.w.save();
  let saved = calls.findLast(call => call.method === 'model.save');
  check(saved?.parameters.geometryTopologyState === draftForSave && saved.parameters.geometryMeshState === null &&
    saved.parameters.geometryCulvertState === 'culvert-clean' && !calls.some(call => call.method === 'topology.save'),
  'parent save must carry topology and culverts but omit the unchanged mesh ledger');

  api.w.prepareMesh();
  await until(() => api.w.mesh?.isDirty && api.w.canUndoMesh, 'dirty mesh draft');
  select('arc', [29]);
  await until(() => input('固定尺寸') && input('固定尺寸').value === '31', 'arc selected before conflicting topology edit');
  changeText(input('固定尺寸'), '33'); commit(input('固定尺寸'));
  await until(() => api.w.topology.arcs.rows[1].values.ConstantSize === '33', 'topology changed after mesh draft');
  const conflictingTopology = api.w.topology.draftState, conflictingMesh = api.w.mesh.draftState;
  let conflictRejected = false;
  await api.w.save().catch(() => { conflictRejected = true; });
  await until(() => !api.w.busyOperation, 'failed save completion');
  saved = calls.findLast(call => call.method === 'model.save');
  check(conflictRejected && saved.parameters.geometryTopologyState === conflictingTopology &&
    saved.parameters.geometryMeshState === conflictingMesh && api.w.topology.draftState === conflictingTopology &&
    api.w.mesh.draftState === conflictingMesh && api.w.mesh.isDirty && api.w.canUndoMesh,
  'an incompatible dirty mesh must reach the Host and a failed save must retain both drafts and history');

  await api.w.undoTopology();
  await until(() => api.w.topology.arcs.rows[1].values.ConstantSize === '31', 'undo restores mesh-compatible topology');
  await api.w.save();
  saved = calls.findLast(call => call.method === 'model.save');
  check(saved.parameters.geometryTopologyState === api.w.topology.draftState && saved.parameters.geometryMeshState === 'mesh-dirty',
    'compatible dirty topology and mesh must be submitted together');
  await api.w.undoMesh();
  await until(() => api.w.mesh?.isDirty === false && api.w.canRedoMesh, 'mesh undo to clean with redo history');
  failNextSave = true;
  let cleanSaveRejected = false;
  await api.w.save().catch(() => { cleanSaveRejected = true; });
  await until(() => !api.w.busyOperation, 'failed save with clean mesh');
  check(cleanSaveRejected && api.w.mesh?.draftState === 'mesh-clean' && api.w.canRedoMesh && api.w.topology?.isDirty,
    'a failed parent save must retain the undone mesh and redo history');
  await api.w.save();
  saved = calls.findLast(call => call.method === 'model.save');
  check(saved.parameters.geometryMeshState === null && saved.parameters.geometryCulvertState === 'culvert-clean',
    'an undone clean mesh must not be replayed when saving another dirty child');

  meshLoadOverride = { ...clone(cleanMesh), draftState: 'mesh-unknown', isDirty: undefined };
  api.w.reload();
  await until(() => api.w.mesh?.draftState === 'mesh-unknown' && api.w.topology?.isDirty, 'unknown mesh dirty state');
  await api.w.save();
  check(calls.findLast(call => call.method === 'model.save').parameters.geometryMeshState === 'mesh-unknown',
    'unknown mesh dirty state must not be treated as clean');
  meshLoadOverride = { ...clone(cleanMesh), revision: 'OTHER', draftState: 'mesh-other-revision' };
  api.w.reload();
  await until(() => api.w.mesh?.draftState === 'mesh-other-revision' && api.w.topology?.isDirty, 'different mesh source revision');
  let sourceRejected = false;
  await api.w.save().catch(() => { sourceRejected = true; });
  check(sourceRejected && calls.findLast(call => call.method === 'model.save').parameters.geometryMeshState === 'mesh-other-revision',
    'a clean mesh from another revision must not be silently omitted');
  meshLoadOverride = { ...clone(cleanMesh), revision: 'OTHER', draftState: '' };
  api.w.reload();
  await until(() => api.w.mesh?.revision === 'OTHER' && api.w.mesh?.draftState === '' && api.w.topology?.isDirty, 'missing mesh ledger');
  const savesBeforeMissing = calls.filter(call => call.method === 'model.save').length;
  let missingRejected = false;
  await api.w.save().catch(() => { missingRejected = true; });
  check(missingRejected && calls.filter(call => call.method === 'model.save').length === savesBeforeMissing,
    'missing mesh ledger must block the save before dispatch');
  return ['single and multiple source selection', 'captured Host field records, unknown and missing enum choices', 'conditional arc input and unfinished raw input retained',
    'point count and blank arc/region size mutations', 'selection survives a matching receipt and stale selection does not return',
    'parent save: clean and undone mesh omitted; dirty mesh conflicts rejected without losing drafts/history',
    'parent save: compatible dirty mesh retained, unknown/mismatched mesh not omitted, missing ledger blocked'];
}
