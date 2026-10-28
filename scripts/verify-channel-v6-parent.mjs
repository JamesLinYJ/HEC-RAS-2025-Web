import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { build } from 'vite';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const temporary = await mkdtemp(path.join(tmpdir(), 'hecras-channel-v6-parent-'));
try {
  const entry = path.join(temporary, 'entry.ts');
  await writeFile(entry, `export { ChannelV6ParentDraftRegistry, channelV6ParentModel, channelV6ParentDraftIdentity } from ${JSON.stringify(path.join(root, 'frontend/src/features/editors/EditorWorkbench.tsx'))};\n`
    + `export * from ${JSON.stringify(path.join(root, 'frontend/src/features/editors/channelV6ParentSave.ts'))};\n`);
  await build({ configFile: false, root, logLevel: 'silent', build: {
    lib: { entry, formats: ['es'], fileName: () => 'parent.mjs' }, outDir: path.join(temporary, 'bundle'),
    minify: false, sourcemap: false,
  } });
  const { ChannelV6ParentDraftRegistry, channelV6ParentModel, channelV6ParentDraftIdentity,
    captureChannelV6ParentSave, trackChannelV6ParentSave, acknowledgeChannelV6ParentSave,
    channelV6ParentSaveCurrent, channelV6ParentSaveFailureConfirmed, resumeChannelV6ParentSave }
    = await import(pathToFileURL(path.join(temporary, 'bundle/parent.mjs')).href);
  let passed = 0;
  const test = async (name, run) => { await run(); passed++; console.log(`PASS ${name}`); };
  const defer = () => { let resolve, reject; const promise = new Promise((yes, no) => { resolve = yes; reject = no; }); return { promise, resolve, reject }; };
  const sha = value => createHash('sha256').update(value).digest('hex').toUpperCase();
  const backend = { supports: () => true, request: async () => { throw new Error('Unexpected pure-test backend request'); } };
  const fixture = (gateway = backend, owner = 'terrain-owner-A') => {
    const snapshot = { revision: 'A'.repeat(64), canEdit: true, sourceState: '{"parent":"baseline"}',
      fields: { name: 'before', derived: 'read-only', 'TerrainLayersEditor/SelectedInputWithMap': 'channel-A' },
      images: {}, fieldGroups: {}, availableOperations: ['model.save'], readOnlyFields: ['derived'],
      terrain: { inputs: [{ id: 'channel-A', scope: 'scope-A', name: 'Empty Channel', type: 'Channel', schema: 'ChannelModTerrainInputProperties' },
        { id: 'raster-B', scope: 'scope-B', name: 'Raster', type: 'Raster', schema: 'RasterTerrainInputProperties' }],
        selectedId: 'channel-A', rows: { 'ChannelModEditor/Features': [] }, tables: {}, importDraftToken: null } };
    const draft = { sourceState: '{"parent":"raw draft"}', fields: { ...snapshot.fields, name: ' exact unsaved name ', derived: 'ignore' },
      images: { picture: [{ id: 'image-A', data: 'data:image/png;base64,AA==' }] }, rows: { 'ChannelModEditor/Features': [] },
      samples: {}, sampleText: {}, changedCells: {}, terrainView: { ...snapshot.terrain,
        tables: { table: { requestedSelectionRowIDs: ['row-2'], selectedRowIDs: ['row-1'], columns: [] } } } };
    const context = { projectSessionID: 'session-A', projectPath: '/private/project.ras', projectXml: '<Project/>', projectRevision: 'B'.repeat(64),
      selectedAsset: { path: '/private/terrain.h5', kind: 'Terrain', name: 'Terrain', revision: snapshot.revision, locator: { sourceId: 'terrain-A' } },
      coreLayerContext: { identityToken: 'core-A', projectSessionID: 'session-A', projectPath: '/private/project.ras', references: [{ path: '/private/terrain.h5', revision: snapshot.revision }] } };
    return { owner, context, backend: gateway, backendRevision: 4, generation: 2, draftGeneration: 7, snapshot, draft };
  };
  const capture = async (registry, state) => {
    const model = channelV6ParentModel(state);
    return registry.capture(state, 'channel-A', model, await channelV6ParentDraftIdentity('channel-A', model));
  };
  const receipt = (state, token = 'c'.repeat(32), payload = 'd'.repeat(32)) => ({ state: 'completed', sourceRevision: 'E'.repeat(64),
    snapshot: { ...state.snapshot, sourceState: '{"parent":"imported"}', fields: { ...state.draft.fields },
      terrain: { ...state.draft.terrainView, importDraftToken: token,
        rows: { 'ChannelModEditor/Features': [{ id: 'feature-A', values: { Name: 'Cross section' } }] } } },
    topology: { inputId: 'channel-A', sourceRevision: state.snapshot.revision, draftIdentity: 'F'.repeat(64),
      features: [{ id: 'feature-A', kind: 'channel', points: [{ x: 0, y: 0 }, { x: 1, y: 1 }] }], selectedFeatureIDs: [], canUndo: true, canRedo: false },
    cleanup: { draftToken: token, payloadId: payload, migratedSHA256: '9'.repeat(64) } });
  const adopted = async (gateway = backend) => {
    const registry = new ChannelV6ParentDraftRegistry(), state = fixture(gateway), captured = await capture(registry, state);
    assert.equal(registry.acquire(captured, state), true);
    const result = receipt(state), next = registry.accept(captured, state, result);
    assert.ok(next); registry.release(captured);
    return { registry, state, captured, result, next };
  };

  await test('captures full parent save data and exact selected input without replaying read-only fields', () => {
    const state = fixture(), model = channelV6ParentModel(state);
    assert.deepEqual(model.fields, { name: ' exact unsaved name ', 'TerrainLayersEditor/SelectedInputWithMap': 'channel-A' });
    assert.deepEqual(model.images, state.draft.images); assert.deepEqual(model.rows, state.draft.rows);
    assert.deepEqual(model.tableSelections, { table: ['row-2'] }); assert.deepEqual(model.coreLayerContext, state.context.coreLayerContext);
    assert.equal(model.sourceState, state.draft.sourceState); assert.equal(model.projectXml, state.context.projectXml);
    state.draft.fields.name = 'later'; state.draft.images.picture[0].data = 'later';
    assert.equal(model.fields.name, ' exact unsaved name '); assert.notEqual(model.images.picture[0].data, 'later');
  });
  await test('Host parent source-state identity is independently SHA256 checked', async () => {
    const model = channelV6ParentModel(fixture());
    assert.equal(await channelV6ParentDraftIdentity('channel-A', model), sha('channel-A\n' + model.revision + '\n' + model.sourceState));
    assert.notEqual(await channelV6ParentDraftIdentity('channel-A', model), 'F'.repeat(64));
  });
  await test('exact lease rejects a duplicate acquisition and unrelated release cannot unlock it', async () => {
    const registry = new ChannelV6ParentDraftRegistry(), state = fixture(), one = await capture(registry, state), two = await capture(registry, state);
    assert.equal(registry.acquire(one, state), true); registry.release(two); assert.equal(registry.acquire(two, state), false);
    registry.release(one); assert.equal(registry.acquire(two, state), true);
  });
  for (const dimension of ['owner', 'context', 'backend', 'backendRevision', 'generation', 'draftGeneration', 'snapshot', 'draft']) {
    await test(`late import cannot replace a parent changed in ${dimension}`, async () => {
      const registry = new ChannelV6ParentDraftRegistry(), original = fixture(), captured = await capture(registry, original);
      assert.equal(registry.acquire(captured, original), true);
      const delayed = defer(); let current = original;
      const outcome = delayed.promise.then(value => registry.accept(captured, current, value));
      const replacements = { owner: 'other-owner', context: { ...original.context, projectSessionID: 'new-session' }, backend: { ...backend },
        backendRevision: 5, generation: 3, draftGeneration: 8, snapshot: { ...original.snapshot }, draft: { ...original.draft } };
      current = { ...original, [dimension]: replacements[dimension] }; delayed.resolve(receipt(original));
      assert.equal(await outcome, null); assert.equal(registry.hasOwned(original.owner), false);
      assert.equal(current[dimension], replacements[dimension]);
    });
  }
  await test('same-value edit ABA still invalidates the old capture through monotonic draft generation', async () => {
    const registry = new ChannelV6ParentDraftRegistry(), state = fixture(), captured = await capture(registry, state);
    assert.equal(registry.isCurrent(captured, { ...state, draftGeneration: state.draftGeneration + 2 }), false);
    assert.equal(registry.isCurrent(captured, { ...state, context: { ...state.context, coreLayerContext: { ...state.context.coreLayerContext, references: [] } } }), false);
  });
  await test('selected input changes and tampered captured save request block adoption', async () => {
    const registry = new ChannelV6ParentDraftRegistry(), state = fixture(), captured = await capture(registry, state);
    assert.throws(() => registry.capture(state, 'raster-B', captured.model, captured.expectedDraftIdentity));
    captured.model.fields.name = 'mutated'; assert.equal(registry.isCurrent(captured, state), false);
  });
  await test('parent adoption preserves unsaved images and leaves baseline unchanged', async () => {
    const { state, next, registry } = await adopted();
    assert.equal(next.sourceState, '{"parent":"imported"}'); assert.deepEqual(next.images, state.draft.images);
    assert.equal(next.fields.name, state.draft.fields.name); assert.equal(next.terrainView.importDraftToken, 'c'.repeat(32));
    assert.equal(state.snapshot.sourceState, '{"parent":"baseline"}'); assert.equal(state.snapshot.terrain.importDraftToken, null);
    assert.equal(registry.hasOwned(state.owner), true); assert.deepEqual(registry.retired(), []);
  });
  await test('wrong revision/input/token receipt never becomes a parent-owned payload', async () => {
    for (const wrong of ['revision', 'input', 'token']) {
      const registry = new ChannelV6ParentDraftRegistry(), state = fixture(), captured = await capture(registry, state);
      registry.acquire(captured, state); const result = receipt(state);
      if (wrong === 'revision') result.snapshot.revision = '0'.repeat(64);
      if (wrong === 'input') result.topology.inputId = 'raster-B';
      if (wrong === 'token') result.cleanup.draftToken = 'b'.repeat(32);
      assert.throws(() => registry.accept(captured, state, result)); assert.equal(registry.hasOwned(state.owner), false);
    }
  });
  await test('Undo/Redo and newer imports retain all accepted payloads sharing the same token', async () => {
    const { registry, state, next } = await adopted();
    // The Host's Undo projection has empty features but keeps the replayable sourceState/token.
    const undone = { ...state, draft: { ...next, sourceState: '{"parent":"undo retains redo payload"}',
      terrainView: { ...next.terrainView, rows: { 'ChannelModEditor/Features': [] } } }, draftGeneration: 9 };
    const second = await capture(registry, undone); registry.acquire(second, undone);
    assert.ok(registry.accept(second, undone, receipt(undone, 'c'.repeat(32), 'e'.repeat(32)))); registry.release(second);
    assert.deepEqual(registry.retired(), []); assert.equal(registry.hasOwned(state.owner), true);
    assert.deepEqual(registry.retire('other-owner'), []);
    const retired = registry.retire(state.owner);
    assert.deepEqual(retired.map(item => item.cleanup.payloadId).sort(), ['d'.repeat(32), 'e'.repeat(32)]);
    assert.ok(retired.every(item => item.cleanup.draftToken === 'c'.repeat(32))); assert.deepEqual(registry.retire(state.owner), []);
  });
  await test('retired payload cleanup is exact, once at a time, and accepts idempotent missing after save', async () => {
    const pending = defer(), calls = [];
    const gateway = { ...backend, request: async (method, parameters) => { calls.push({ method, parameters }); return pending.promise; } };
    const { registry, state } = await adopted(gateway); const [payload] = registry.retire(state.owner);
    const first = registry.cleanup(payload); await registry.cleanup(payload);
    assert.equal(calls.length, 1); assert.equal(calls[0].method, 'model.channelV6GeometryImportDiscard');
    assert.deepEqual(calls[0].parameters.cleanup, payload.cleanup); assert.equal(calls[0].parameters.model.assetPath, '/private/terrain.h5');
    pending.resolve({ discarded: true, payloadId: payload.cleanup.payloadId, draftDirectoryRemoved: false }); await first;
    assert.equal(registry.hasPayload(payload), false); await registry.cleanup(payload); assert.equal(calls.length, 1);
  });
  await test('unknown cleanup keeps original gateway and HTTP ID and resumes without another POST', async () => {
    const calls = [], id = '1'.repeat(32);
    const gateway = { ...backend,
      request: async (method, params, options) => { calls.push(['POST', method]); options.onTransportState({ state: 'connected', operationId: id, recoveredAfterAttempt: 0 }); throw { terminalConfirmed: false, operationId: id }; },
      resumeOperation: async resumed => { calls.push(['GET', resumed]); return { discarded: true, payloadId: 'd'.repeat(32), draftDirectoryRemoved: true }; } };
    const { registry, state } = await adopted(gateway), [payload] = registry.retire(state.owner);
    await registry.cleanup(payload); assert.equal(payload.phase, 'unknown'); assert.equal(payload.operationID, id);
    await registry.cleanup(payload); assert.deepEqual(calls, [['POST', 'model.channelV6GeometryImportDiscard'], ['GET', id]]);
    assert.equal(registry.hasPayload(payload), false);
  });
  await test('unknown cleanup without HTTP ID retains ownership and never retries POST', async () => {
    let calls = 0; const gateway = { ...backend, request: async () => { calls++; throw { terminalConfirmed: false }; } };
    const { registry, state } = await adopted(gateway), [payload] = registry.retire(state.owner);
    await registry.cleanup(payload); await registry.cleanup(payload);
    assert.equal(calls, 1); assert.equal(payload.phase, 'unknown'); assert.equal(registry.hasPayload(payload), true);
  });
  const saveID = '1'.repeat(32), saveLease = 'root-save-lease-A';
  const saveCapture = state => captureChannelV6ParentSave(state, saveLease, channelV6ParentModel(state));
  const savedSnapshot = state => ({ ...state.snapshot, revision: '7'.repeat(64), sourceState: null,
    terrain: { ...state.draft.terrainView, importDraftToken: null } });
  const savedContext = (state, saved) => ({ ...state.context, selectedAsset: { ...state.context.selectedAsset, revision: saved.revision } });
  await test('save captures the complete immutable request while retaining the original gateway and draft references', () => {
    const state = fixture(), save = saveCapture(state);
    assert.equal(save.current.backend, state.backend); assert.equal(save.current.draft, state.draft);
    assert.equal(save.current.snapshot, state.snapshot); assert.notEqual(save.current.context, state.context);
    state.context.coreLayerContext.references[0].revision = '0'.repeat(64); state.draft.images.picture[0].data = 'changed';
    assert.equal(save.current.context.coreLayerContext.references[0].revision, 'A'.repeat(64));
    assert.equal(save.request.images.picture[0].data, 'data:image/png;base64,AA==');
    assert.equal(channelV6ParentSaveCurrent(save, state), false);
  });
  await test('capability refresh and replacement gateway preserve a save bound to the same exact parent draft', () => {
    const state = fixture(), save = saveCapture(state);
    assert.equal(channelV6ParentSaveCurrent(save, { ...state, backendRevision: 99, backend: { ...backend } }), true);
    for (const [dimension, replacement] of Object.entries({ owner: 'other', context: { ...state.context, projectSessionID: 'other' },
      generation: state.generation + 1, draftGeneration: state.draftGeneration + 1,
      snapshot: { ...state.snapshot }, draft: { ...state.draft } }))
      assert.equal(channelV6ParentSaveCurrent(save, { ...state, [dimension]: replacement }), false, dimension);
    assert.equal(channelV6ParentSaveCurrent(save, { ...state, context: { ...state.context,
      selectedAsset: { ...state.context.selectedAsset, revision: '0'.repeat(64) } } }), false);
  });
  for (const mode of ['reject', 'throw']) await test(`root task tracking ${mode} keeps the original HTTP ID without releasing payload ownership`, async () => {
    const { registry, state } = await adopted();
    const save = trackChannelV6ParentSave(saveCapture(state), saveID, () => {
      if (mode === 'throw') throw new Error('root track exception'); return false;
    });
    assert.equal(save.operationID, saveID); assert.equal(save.operationAcknowledged, false); assert.ok(save.error);
    assert.equal(registry.hasOwned(state.owner), true); assert.deepEqual(registry.retired(), []);
    const retry = trackChannelV6ParentSave(save, saveID, () => true);
    assert.equal(retry.operationID, saveID); assert.equal(retry.operationAcknowledged, true); assert.equal(retry.error, undefined);
  });
  await test('a second HTTP ID cannot replace the original task or commit its receipt', async () => {
    const state = fixture(), first = trackChannelV6ParentSave(saveCapture(state), saveID, () => true);
    const conflict = trackChannelV6ParentSave(first, '2'.repeat(32), () => { throw new Error('must not acknowledge another ID'); });
    assert.equal(conflict.operationID, saveID); assert.equal(conflict.operationConflict, true);
    const saved = savedSnapshot(state); let commits = 0;
    assert.equal(acknowledgeChannelV6ParentSave({ ...conflict, receipt: saved }, state, savedContext(state, saved),
      () => true, () => { commits++; return true; }).accepted, false);
    assert.equal(commits, 0); await assert.rejects(resumeChannelV6ParentSave(conflict));
  });
  await test('invalid or missing IDs cannot cause a recovery GET or a duplicate POST', async () => {
    let calls = 0; const state = fixture({ ...backend, request: async () => { calls++; }, resumeOperation: async () => { calls++; } });
    const initial = saveCapture(state);
    const invalid = trackChannelV6ParentSave(initial, '../other-task', () => { calls++; return true; });
    assert.equal(invalid.operationID, null); await assert.rejects(resumeChannelV6ParentSave(invalid));
    await assert.rejects(resumeChannelV6ParentSave(initial)); assert.equal(calls, 0);
  });
  for (const mode of ['reject', 'throw']) await test(`root commit ${mode} retains the cached successful receipt, draft, lease and Undo payloads`, async () => {
    let requests = 0;
    const { registry, state } = await adopted({ ...backend, request: async () => { requests++; throw new Error('no POST'); },
      resumeOperation: async () => { requests++; throw new Error('cached receipt needs no GET'); } });
    const saved = savedSnapshot(state), tracking = { ...trackChannelV6ParentSave(saveCapture(state), saveID, () => true), receipt: saved };
    const before = structuredClone(state.draft);
    const rejected = acknowledgeChannelV6ParentSave(tracking, state, savedContext(state, saved), () => true,
      () => { if (mode === 'throw') throw new Error('root commit exception'); return false; });
    assert.equal(rejected.accepted, false); assert.equal(rejected.save.receipt, saved); assert.equal(rejected.save.leaseID, saveLease);
    assert.deepEqual(state.draft, before); assert.equal(registry.hasOwned(state.owner), true); assert.deepEqual(registry.retired(), []);
    assert.equal(await resumeChannelV6ParentSave(rejected.save), saved); assert.equal(requests, 0);
    assert.equal(channelV6ParentSaveFailureConfirmed(rejected.save, { terminalConfirmed: true, operationId: saveID }, 'resume'), false);
  });
  await test('root task rejection blocks a successful receipt from reaching commit', () => {
    const state = fixture(), saved = savedSnapshot(state); let commits = 0;
    const tracking = { ...trackChannelV6ParentSave(saveCapture(state), saveID, () => true), receipt: saved };
    const rejected = acknowledgeChannelV6ParentSave(tracking, state, savedContext(state, saved), () => false,
      () => { commits++; return true; });
    assert.equal(rejected.accepted, false); assert.equal(rejected.save.receipt, saved); assert.equal(commits, 0);
  });
  await test('unknown save resumes only the original gateway and ID, then retires payloads after exact root acknowledgement', async () => {
    const calls = [], state = fixture({ ...backend, request: async () => { calls.push('unexpected POST'); },
      resumeOperation: async id => { calls.push(['original GET', id]); return savedSnapshot(state); } });
    const registry = new ChannelV6ParentDraftRegistry(), captured = await capture(registry, state);
    registry.acquire(captured, state); state.draft = registry.accept(captured, state, receipt(state)); registry.release(captured);
    let save = { ...trackChannelV6ParentSave(saveCapture(state), saveID, () => true), phase: 'unknown' };
    const replacement = { ...state, backendRevision: state.backendRevision + 1,
      backend: { ...backend, resumeOperation: async () => { calls.push('wrong GET'); } } };
    const result = await resumeChannelV6ParentSave(save); save = { ...save, receipt: result };
    assert.equal(registry.hasOwned(state.owner), true); assert.deepEqual(registry.retired(), []);
    const ack = acknowledgeChannelV6ParentSave(save, replacement, savedContext(state, result), (lease, id) => {
      assert.equal(lease, saveLease); assert.equal(id, saveID); return true;
    }, (source, committed, lease, id) => {
      assert.deepEqual(source, state.context); assert.equal(committed.selectedAsset.revision, result.revision);
      assert.equal(lease, saveLease); assert.equal(id, saveID);
      assert.equal(registry.hasOwned(state.owner), true); assert.deepEqual(registry.retired(), []); return true;
    });
    assert.equal(ack.accepted, true); assert.equal(registry.retire(state.owner).length, 1);
    assert.deepEqual(calls, [['original GET', saveID]]);
  });
  await test('only a matching original-task failure or explicit submission rejection can release the save lease', () => {
    const initial = saveCapture(fixture()), tracked = trackChannelV6ParentSave(initial, saveID, () => true);
    assert.equal(channelV6ParentSaveFailureConfirmed(tracked, { terminalConfirmed: true, operationId: saveID }, 'resume'), true);
    for (const error of [{ terminalConfirmed: false, operationId: saveID }, { terminalConfirmed: true, operationId: '2'.repeat(32) },
      { terminalConfirmed: true }, { code: 'INVALID_REQUEST' }, { code: 'CLIENT_DISPOSED' },
      { operation: 'GET /operations/' + saveID, status: 404 }])
      assert.equal(channelV6ParentSaveFailureConfirmed(tracked, error, 'resume'), false);
    assert.equal(channelV6ParentSaveFailureConfirmed(initial, { code: 'INVALID_REQUEST' }, 'resume'), false);
    assert.equal(channelV6ParentSaveFailureConfirmed(initial, { code: 'INVALID_REQUEST', operation: 'model.save' }, 'submit'), true);
    assert.equal(channelV6ParentSaveFailureConfirmed(initial, { operation: 'POST /operations', status: 403 }, 'submit'), true);
    assert.equal(channelV6ParentSaveFailureConfirmed(initial, { operation: 'POST /operations', status: 408 }, 'submit'), false);
  });
  console.log(`Channel V6 parent glue: ${passed} pure checks passed; no real Host or UI acceptance claimed.`);
} finally { await rm(temporary, { recursive: true, force: true }); }
