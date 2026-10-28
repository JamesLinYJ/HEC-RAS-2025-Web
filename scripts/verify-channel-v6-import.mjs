import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { build } from 'vite';

// Execute only the protocol/controller modules. This test does not build the product or start a Host.
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const temporary = await mkdtemp(path.join(tmpdir(), 'hecras-channel-v6-protocol-'));
const checks = [];
try {
  await build({ configFile: false, root, logLevel: 'silent', build: {
    lib: { entry: {
      protocol: path.join(root, 'frontend/src/features/editors/channelV6ImportProtocol.ts'),
      controller: path.join(root, 'frontend/src/features/editors/channelV6ImportController.ts'),
    }, formats: ['es'], fileName: (_format, name) => `${name}.mjs` },
    outDir: temporary, emptyOutDir: true, minify: false, sourcemap: false,
  } });
  const protocol = await import(pathToFileURL(path.join(temporary, 'protocol.mjs')).href);
  const { ChannelV6ImportController } = await import(pathToFileURL(path.join(temporary, 'controller.mjs')).href);
  const sha = value => createHash('sha256').update(value).digest('hex').toUpperCase();
  const defer = () => { let resolve, reject; const promise = new Promise((yes, no) => { resolve = yes; reject = no; }); return { promise, resolve, reject }; };
  const until = async predicate => {
    for (let attempt = 0; attempt < 100; attempt++) { if (predicate()) return; await new Promise(resolve => setImmediate(resolve)); }
    throw new Error('Protocol test timeout.');
  };
  const file = sourcePath => ({ sourcePath: sourcePath.trim(), canonicalPath: '/fixture/实际河道.g01.hdf', revision: sha('real source'), length: 2048 });
  const rawPath = '  /fixture/河道.g01.hdf  ';
  const tokenA = 'a'.repeat(32), tokenB = 'b'.repeat(32), payloadID = 'c'.repeat(32);

  function environment(options = {}) {
    const calls = [], resumes = [], accepted = [], released = [];
    let current = true, lease = false, sequence = 0;
    const backend = {
      supports: method => !(options.unsupported ?? []).includes(method),
      request(method, parameters, requestOptions) {
        const pending = defer(), id = `http-${++sequence}`;
        const call = { method, parameters, options: requestOptions, pending, id };
        calls.push(call);
        if (method === protocol.channelV6ImportMethods.inspect) {
          if (!options.deferredInspection) pending.resolve(file(parameters.sourcePath));
        } else requestOptions.onTransportState?.({ state: 'connected', operationId: id, recoveredAfterAttempt: 0 });
        return pending.promise;
      },
      resumeOperation(id, requestOptions) {
        assert.equal(requestOptions.signal?.aborted ?? false, false, 'A resumed task must establish its cancellation pump before abort.');
        const pending = defer(); resumes.push({ id, options: requestOptions, pending });
        requestOptions.onTransportState?.({ state: 'connected', operationId: id, recoveredAfterAttempt: 0 });
        return pending.promise;
      },
    };
    const capture = {
      owner: 'parent:session-A:terrain-A', draftKey: 'session-A:backend-A:draft-17', backend,
      model: { assetPath: '/fixture/terrain.h5', revision: sha('terrain'), projectPath: '/fixture/project.ras',
        projectXml: '<Project />', projectRevision: sha('project'), projectSessionID: 'session-A', sourceState: 'original-source-state',
        fields: { Description: '尚未保存的说明', 'TerrainLayersEditor/SelectedInputWithMap': 'channel-A' },
        images: { Images: [{ id: 'image-1', name: 'keep', data: 'raw-image' }] },
        rows: { OtherTable: [{ id: 'row-1', values: { Value: ' 001.20e1 ' } }] }, samples: {},
        changedCells: { OtherTable: [{ rowID: 'row-1', columnID: 'Value' }] }, tableSelections: {} },
      inputId: 'channel-A', inputIds: ['raster-A', 'channel-A'], inputName: '空河道', expectedDraftIdentity: sha('request draft'),
      existingDraftToken: options.existingDraftToken ?? null,
    };
    const controller = new ChannelV6ImportController({
      capture: async (inputId, signal) => {
        if (signal.aborted || inputId !== capture.inputId || options.noCapture) return null;
        return capture;
      },
      isCurrent: candidate => current && candidate === capture,
      acquire: candidate => {
        if (!current || lease || candidate !== capture || options.acquireRejected) return false;
        lease = true; return true;
      },
      accept: (candidate, receipt) => {
        assert.equal(candidate, capture);
        if (options.adoptionRejected) return false;
        accepted.push(receipt); return true;
      },
      release: candidate => { assert.equal(candidate, capture); lease = false; released.push(candidate); if (options.releaseThrows) throw new Error('release observer failed'); },
    });
    return { controller, backend, capture, calls, resumes, accepted, released,
      changeSource: () => { current = false; }, lease: () => lease,
      imports: () => calls.filter(call => call.method === protocol.channelV6ImportMethods.import),
      cleanups: () => calls.filter(call => call.method === protocol.channelV6ImportMethods.discard),
    };
  }
  const prepare = async env => {
    env.controller.setSourcePath(env.capture.owner, env.capture.inputId, rawPath);
    await env.controller.inspect(env.capture.inputId, rawPath);
    assert.ok(env.controller.getSnapshot().inspection);
    assert.equal(env.controller.sourcePath(env.capture.owner, env.capture.inputId), rawPath);
  };
  const completed = env => ({ state: 'completed', sourceRevision: sha('real source'),
    cleanup: { draftToken: env.capture.existingDraftToken ?? tokenA, payloadId: payloadID, migratedSHA256: sha('migrated file') },
    snapshot: { revision: sha('terrain'), sourceState: 'imported-source-state', canEdit: true,
      fields: { Description: '尚未保存的说明' }, images: {}, fieldGroups: {}, availableOperations: [],
      terrain: { inputs: [{ id: 'raster-A' }, { id: 'channel-A', schema: 'ChannelModTerrainInputProperties' }],
        selectedId: 'channel-A', importDraftToken: env.capture.existingDraftToken ?? tokenA,
        rows: { 'ChannelModEditor/Features': [{ id: 'xs-1', values: {} }, { id: 'xs-2', values: {} }] } } },
    topology: { inputId: 'channel-A', sourceRevision: sha('terrain'), draftIdentity: sha('topology draft'),
      features: [{ id: 'arc-1', kind: 'arc', points: [{ x: 0, y: 10 }, { x: 100, y: 10 }] }],
      selectedFeatureIDs: [], canUndo: true, canRedo: false },
  });
  const discardReceipt = (id = payloadID) => ({ discarded: true, payloadId: id, draftDirectoryRemoved: false });
  const confirmCleanup = async env => {
    await until(() => env.cleanups().length > 0);
    env.cleanups().at(-1).pending.resolve(discardReceipt());
  };

  for (const source of ['/fixture/river.hdf', ' C:\\工程\\river.HDF ', '\\\\server\\share\\river.hdf'])
    assert.equal(protocol.channelV6SourcePathIssue(source), null);
  for (const source of ['', 'river.hdf', '/fixture/river.h5', 'browser-file://river.hdf', '/fixture/a\nfile.hdf'])
    assert.ok(protocol.channelV6SourcePathIssue(source));
  assert.equal(protocol.parseChannelV6ImportFile(file(rawPath), rawPath).revision, sha('real source'));
  for (const patch of [{ sourcePath: '/wrong.hdf' }, { canonicalPath: 'relative.hdf' }, { canonicalPath: '/wrong.h5' },
    { revision: 'manual SHA' }, { length: 0 }, { length: -1 }, { length: 1.5 }])
    assert.throws(() => protocol.parseChannelV6ImportFile({ ...file(rawPath), ...patch }, rawPath));
  checks.push('Absolute .hdf path and exact file receipt validation; SHA-256 comes from Host inspection; raw input remains separate from canonical path.');

  {
    const env = environment(); await prepare(env);
    const inspection = env.controller.getSnapshot().inspection;
    const request = protocol.channelV6ImportRequest(inspection);
    assert.equal(request.sourcePath, '/fixture/实际河道.g01.hdf'); assert.equal(request.sourceSHA256, sha('real source'));
    assert.equal(request.expectedDraftIdentity, env.capture.expectedDraftIdentity);
    assert.deepEqual(request.model, env.capture.model); assert.notEqual(request.model, env.capture.model);
    assert.equal(request.model.rows.OtherTable[0].values.Value, ' 001.20e1 ');
    assert.equal(protocol.parseChannelV6ImportCompleted(completed(env), inspection).state, 'completed');
    for (const modify of [r => r.sourceRevision = sha('wrong'), r => r.snapshot.revision = sha('wrong'),
      r => r.snapshot.terrain.selectedId = 'other', r => r.snapshot.terrain.inputs.reverse(),
      r => r.cleanup.draftToken = tokenB, r => r.cleanup.payloadId = '../other',
      r => r.topology.inputId = 'other', r => r.topology.sourceRevision = sha('other'),
      r => r.topology.features.push(r.topology.features[0]), r => r.topology.features[0].points[0].x = NaN,
      r => r.topology.selectedFeatureIDs = ['missing'], r => r.snapshot.sourceState = '']) {
      const receipt = completed(env); modify(receipt); assert.throws(() => protocol.parseChannelV6ImportCompleted(receipt, inspection));
    }
    assert.throws(() => protocol.parseChannelV6ImportDiscarded(discardReceipt('other'), completed(env).cleanup));
    assert.equal(protocol.isChannelV6ImportCancelled({ state: 'cancelled' }), true);
    assert.equal(protocol.isChannelV6ImportCancelled({ state: 'cancelled', cleanup: completed(env).cleanup }), false);
  }
  checks.push('Full parent request including raw unsaved rows/images is detached; source/input/revision/topology/cleanup receipts are checked before adoption.');

  for (const options of [{ noCapture: true }, { unsupported: [protocol.channelV6ImportMethods.discard] }]) {
    const env = environment(options); await env.controller.inspect(env.capture.inputId, rawPath);
    assert.equal(env.controller.getSnapshot().inspection, null); assert.equal(env.imports().length, 0);
  }
  for (const change of [env => env.changeSource(), env => env.capture.draftKey += ':new',
    env => env.capture.model.fields.Description += '编辑', env => env.capture.inputId = 'other',
    env => env.capture.backend = { ...env.backend }]) {
    const env = environment(); await prepare(env); change(env); await env.controller.start();
    assert.equal(env.imports().length, 0); assert.equal(env.lease(), false);
  }
  {
    const env = environment({ deferredInspection: true }); const inspected = env.controller.inspect(env.capture.inputId, rawPath);
    await until(() => env.calls.length === 1); env.changeSource(); env.calls[0].pending.resolve(file(rawPath)); await inspected;
    assert.equal(env.controller.getSnapshot().inspection, null);
  }
  {
    const env = environment({ deferredInspection: true }); const inspected = env.controller.inspect(env.capture.inputId, rawPath);
    await until(() => env.calls.length === 1); env.controller.invalidateScope(); env.calls[0].pending.resolve(file(rawPath)); await inspected;
    assert.equal(env.controller.getSnapshot().inspection, null); assert.equal(env.controller.getSnapshot().inspecting, false);
  }
  checks.push('Missing cleanup capability, changed source/session/backend/input/draft and cancelled or late inspection cannot submit a write.');

  for (const releaseThrows of [false, true]) {
    const env = environment({ releaseThrows }); await prepare(env); const done = env.controller.start();
    assert.equal(env.lease(), true); await env.controller.start(); assert.equal(env.imports().length, 1);
    env.imports()[0].options.onProgress({ fraction: .72, taskName: 'Preparing imported Channel geometry' });
    assert.equal(env.controller.getSnapshot().task.progress.fraction, .72);
    env.imports()[0].pending.resolve(completed(env)); await done;
    assert.equal(env.controller.getSnapshot().task.phase, 'completed'); assert.equal(env.accepted.length, 1);
    assert.equal(env.cleanups().length, 0); assert.equal(env.released.length, 1); assert.equal(env.lease(), false);
    assert.equal(env.controller.sourcePath(env.capture.owner, env.capture.inputId), rawPath);
    assert.equal(env.controller.sourcePath('other-session', env.capture.inputId), '');
  }
  checks.push('Import owns one synchronous parent lease; duplicate submit is blocked; adopted payload is never cleaned by this controller, even if release observer throws.');

  {
    const env = environment(); await prepare(env); const done = env.controller.start(), call = env.imports()[0];
    call.pending.reject(Object.assign(new Error('network lost'), { operationId: call.id, terminalConfirmed: false })); await done;
    assert.equal(env.controller.getSnapshot().task.phase, 'unknown'); assert.equal(env.controller.pending, true); assert.equal(env.lease(), true);
    await env.controller.start(); assert.equal(env.imports().length, 1);
    const resumed = env.controller.resume(); assert.equal(env.resumes[0].id, call.id);
    env.resumes[0].pending.resolve(completed(env)); await resumed;
    assert.equal(env.controller.getSnapshot().task.phase, 'completed'); assert.equal(env.imports().length, 1);
  }
  {
    const env = environment(); await prepare(env);
    env.backend.request = (method, parameters, options) => {
      const pending = defer(); env.calls.push({ method, parameters, options, pending }); return pending.promise;
    };
    const done = env.controller.start(); env.imports()[0].pending.reject(Object.assign(new Error('acceptance unconfirmed'), { terminalConfirmed: false })); await done;
    assert.equal(env.controller.getSnapshot().task.httpOperationID, null); assert.equal(env.controller.canResume(), false);
    assert.equal(env.controller.pending, true); await env.controller.start(); assert.equal(env.imports().length, 1);
  }
  checks.push('Unknown import retains the original backend/HTTP ID and parent lease; resume never reposts; unknown acceptance without ID cannot be duplicated.');

  {
    const env = environment(); await prepare(env); const done = env.controller.start(); env.controller.cancel();
    assert.equal(env.imports()[0].options.signal.aborted, true); assert.equal(env.lease(), true);
    env.imports()[0].pending.resolve({ state: 'cancelled' }); await done;
    assert.equal(env.controller.getSnapshot().task.phase, 'cancelled'); assert.equal(env.cleanups().length, 0); assert.equal(env.lease(), false);
  }
  {
    const env = environment(); await prepare(env); const done = env.controller.start(), call = env.imports()[0];
    call.pending.reject(Object.assign(new Error('poll lost'), { terminalConfirmed: false, operationId: call.id })); await done;
    env.controller.cancel(); assert.equal(env.resumes.length, 1); assert.equal(env.resumes[0].id, call.id);
    assert.equal(env.resumes[0].options.signal.aborted, true);
    env.resumes[0].pending.resolve({ state: 'cancelled' }); await until(() => !env.controller.pending);
    assert.equal(env.controller.getSnapshot().task.phase, 'cancelled'); assert.equal(env.imports().length, 1);
  }
  {
    const env = environment({ existingDraftToken: tokenB }); await prepare(env); const done = env.controller.start();
    env.controller.cancel(); env.imports()[0].pending.resolve(completed(env)); await confirmCleanup(env); await done;
    assert.equal(env.controller.getSnapshot().task.phase, 'cancelled'); assert.equal(env.accepted.length, 0);
    assert.equal(env.cleanups()[0].parameters.cleanup.draftToken, tokenB);
    assert.equal(env.cleanups()[0].parameters.cleanup.payloadId, payloadID);
    assert.equal(env.calls.some(call => call.method === 'model.terrainInputImportDiscard'), false);
  }
  checks.push('Cancellation waits for a terminal result; cancelled unknown tasks establish original polling before abort; completed-after-cancel cleans only the returned payload, including shared tokens.');

  for (const rejection of ['source', 'parent', 'topology']) {
    const env = environment({ adoptionRejected: rejection === 'parent' }); await prepare(env); const done = env.controller.start();
    const receipt = completed(env);
    if (rejection === 'source') env.changeSource();
    if (rejection === 'topology') receipt.topology.features[0].points[0].x = NaN;
    env.imports()[0].pending.resolve(receipt); await until(() => env.cleanups().length === 1);
    assert.equal(env.lease(), true); assert.equal(env.accepted.length, 0);
    env.cleanups()[0].pending.resolve(discardReceipt()); await done;
    assert.equal(env.controller.getSnapshot().task.phase, rejection === 'topology' ? 'failed' : 'not-adopted');
    assert.equal(env.lease(), false);
  }
  {
    const env = environment(); await prepare(env); const done = env.controller.start();
    const receipt = completed(env); env.changeSource();
    env.capture.model.assetPath = '/foreign/terrain.h5'; env.capture.model.revision = sha('foreign');
    env.capture.backend = { supports: () => true, request: () => { throw new Error('Wrong backend used'); } };
    env.imports()[0].pending.resolve(receipt); await confirmCleanup(env); await done;
    assert.equal(env.cleanups()[0].parameters.model.assetPath, '/fixture/terrain.h5');
    assert.equal(env.cleanups()[0].parameters.model.revision, sha('terrain'));
  }
  checks.push('Stale source, parent rejection and malformed topology clean exact owned payload before releasing; cleanup retains detached original model and backend despite later capture mutation.');

  {
    const env = environment({ adoptionRejected: true }); await prepare(env); const done = env.controller.start();
    env.imports()[0].pending.resolve(completed(env)); await until(() => env.cleanups().length === 1);
    const firstCleanup = env.cleanups()[0]; firstCleanup.pending.reject(Object.assign(new Error('confirmed cleanup failure'), { terminalConfirmed: true, operationId: firstCleanup.id }));
    await done; assert.equal(env.controller.getSnapshot().task.phase, 'cleanup-failed'); assert.equal(env.lease(), true);
    const retry = env.controller.retryCleanup(); assert.equal(env.cleanups().length, 2);
    const secondCleanup = env.cleanups()[1]; assert.notEqual(secondCleanup.id, firstCleanup.id);
    secondCleanup.pending.reject(Object.assign(new Error('cleanup polling lost'), { terminalConfirmed: false, operationId: secondCleanup.id })); await retry;
    assert.equal(env.controller.getSnapshot().task.phase, 'cleanup-unknown');
    assert.equal(env.controller.getSnapshot().task.cleanupOperationID, secondCleanup.id);
    const resumed = env.controller.retryCleanup(); assert.equal(env.resumes[0].id, secondCleanup.id);
    assert.equal(env.cleanups().length, 2); env.resumes[0].pending.resolve(discardReceipt()); await resumed;
    assert.equal(env.controller.getSnapshot().task.phase, 'not-adopted'); assert.equal(env.lease(), false);
  }
  {
    const env = environment({ adoptionRejected: true }); await prepare(env); const done = env.controller.start();
    const receipt = completed(env); delete receipt.cleanup; env.imports()[0].pending.resolve(receipt); await done;
    assert.equal(env.controller.getSnapshot().task.phase, 'cleanup-failed'); assert.equal(env.controller.pending, true);
    assert.equal(env.cleanups().length, 0); assert.equal(env.controller.canRetryCleanup(), false);
    await env.controller.start(); assert.equal(env.imports().length, 1);
  }
  {
    const env = environment(); await prepare(env); const done = env.controller.start(); env.controller.dispose();
    assert.equal(env.imports()[0].options.signal.aborted, true); assert.equal(env.lease(), true);
    env.imports()[0].pending.resolve(completed(env)); await confirmCleanup(env); await done;
    assert.equal(env.controller.getSnapshot().task.phase, 'cancelled'); assert.equal(env.lease(), false);
  }
  checks.push('Cleanup A terminal failure then B unknown resumes B only; missing cleanup identity keeps ownership without blind deletion; disposal cancels but tracks and cleans before release.');

  console.log(JSON.stringify({ passed: true, checks }, null, 2));
} finally { await rm(temporary, { recursive: true, force: true }); }
