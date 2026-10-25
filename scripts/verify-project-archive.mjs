import assert from 'node:assert/strict';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { build } from 'vite';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const temporary = await mkdtemp(path.join(tmpdir(), 'hecras-project-archive-'));
let checks = 0;
const check = fn => { fn(); checks++; };
const deferred = () => { let resolve, reject; const promise = new Promise((a, b) => { resolve = a; reject = b; }); return { promise, resolve, reject }; };
try {
  const entry = path.join(temporary, 'entry.ts');
  await writeFile(entry, `export * from ${JSON.stringify(path.join(root, 'frontend/src/features/tools/archive/projectArchiveWorkflow.ts'))};\n`);
  await build({ configFile: false, root, logLevel: 'silent', build: { lib: { entry, formats: ['es'], fileName: () => 'archive.mjs' },
    outDir: path.join(temporary, 'out'), emptyOutDir: true, minify: false, sourcemap: false } });
  const w = await import(pathToFileURL(path.join(temporary, 'out/archive.mjs')).href);
  // Protocol fixtures only: these checks do not create ZIP files or run a real Host.
  const sourceSHA = 'a'.repeat(64), archivedSHA = 'b'.repeat(64), inventorySHA = 'c'.repeat(64);
  const token = '01234567-89ab-cdef-0123-456789abcdef';
  const context = { projectSessionID: 'session-1', projectPath: '/fixture/model.ras', projectRevision: sourceSHA,
    projectXml: '<Project />', selectedAsset: null, coreLayerContext: null };
  const opened = { success: true, projectPath: context.projectPath, revision: sourceSHA, projectXml: context.projectXml };
  const receipt = { state: 'completed', cleanupToken: token, archivePath: `/private/${token}.zip`, projectPath: context.projectPath,
    sourceProjectSHA256: sourceSHA, projectSHA256: archivedSHA, inventorySHA256: inventorySHA, entryCount: 3, includeResults: false };
  const gateway = { supports: () => true, request: async () => receipt };
  const source = w.captureProjectArchiveSource(context, gateway, 1);
  const request = { projectPath: context.projectPath, expectedProjectSHA256: sourceSHA, includeResults: false };
  check(() => assert.notEqual(source.context, context));
  check(() => assert.equal(w.isProjectArchiveSourceCurrent(source, { ...context, selectedAsset: { path: '/other.h5' } }, gateway, 1), true));
  for (const patch of [{ projectSessionID: 'session-2' }, { projectPath: '/other.ras' }, { projectXml: '<Changed />' },
    { projectRevision: archivedSHA }, { coreLayerContext: { identityToken: sourceSHA } }])
    check(() => assert.equal(w.isProjectArchiveSourceCurrent(source, { ...context, ...patch }, gateway, 1), false));
  check(() => assert.equal(w.isProjectArchiveSourceCurrent(source, context, { ...gateway }, 1), false));
  check(() => assert.equal(w.isProjectArchiveSourceCurrent(source, context, gateway, 2), false));
  for (const patch of [{ projectPath: null }, { projectRevision: '' }, { projectXml: null }, { projectPath: 'relative.ras' }])
    check(() => assert.throws(() => w.captureProjectArchiveSource({ ...context, ...patch }, gateway)));
  check(() => assert.equal(w.parseProjectArchiveReceipt(receipt, { request }).projectSHA256, archivedSHA));
  check(() => assert.notEqual(w.parseProjectArchiveReceipt(receipt, { request }).projectSHA256, sourceSHA));
  const cancelled = { ...receipt, state: 'cancelled', projectSHA256: '', inventorySHA256: '', entryCount: 0 };
  check(() => assert.equal(w.parseProjectArchiveReceipt(cancelled, { request }).state, 'cancelled'));
  for (const patch of [{ state: 'running' }, { cleanupToken: '' }, { cleanupToken: '../bad' }, { cleanupToken: token.toUpperCase() },
    { projectPath: '/other.ras' }, { sourceProjectSHA256: archivedSHA }, { includeResults: true }, { entryCount: 0 },
    { projectSHA256: '' }, { inventorySHA256: '' }, { entryCount: 1.5 }, { archivePath: 'relative.zip' }])
    check(() => assert.throws(() => w.parseProjectArchiveReceipt({ ...receipt, ...patch }, { request })));
  check(() => w.validateProjectArchivePreflight(opened, source));
  for (const patch of [{ projectXml: '<Unsaved />' }, { revision: archivedSHA }, { projectPath: '/different.ras' }, { success: false }])
    check(() => assert.throws(() => w.validateProjectArchivePreflight({ ...opened, ...patch }, source)));
  const props = { context, backend: gateway, onDirtyStateChange() {}, dirtyOwners: new Set(), hasUnsavedDrafts: false };
  check(() => assert.equal(w.projectArchiveBlocker(props, 'settings'), null));
  check(() => assert.equal(w.projectArchiveBlocker({ ...props, dirtyOwners: new Set(['settings']), hasUnsavedDrafts: true }, 'settings'), null));
  for (const patch of [{ dirtyOwners: new Set(['editor']), hasUnsavedDrafts: true }, { dirtyOwners: undefined, hasUnsavedDrafts: true },
    { onDirtyStateChange: undefined }, { backend: { ...gateway, supports: () => false } },
    { planAnalysisDrafts: { entries: [{ dirty: true }] } }])
    check(() => assert.ok(w.projectArchiveBlocker({ ...props, ...patch }, 'settings')));
  check(() => assert.ok(w.projectArchiveCoreBlocker({ ...context, coreLayerContext: { references: [] } })));
  const core = { projectSessionID: context.projectSessionID, projectPath: context.projectPath, projectRevision: sourceSHA,
    identityToken: inventorySHA, references: [], sessionHdfReferences: [] };
  check(() => assert.equal(w.projectArchiveCoreBlocker({ ...context, coreLayerContext: core }), null));
  const ref = { referenceID: 'source-1', projectSessionID: context.projectSessionID, projectPath: context.projectPath,
    kind: 'terrain', sourcePath: '/external.h5', canonicalSourcePath: '/external.h5', sourceRevision: archivedSHA, name: 'Terrain', sessionOnly: true };
  check(() => assert.ok(w.projectArchiveCoreBlocker({ ...context, coreLayerContext: { ...core, sessionHdfReferences: [ref] } })));
  check(() => assert.equal(w.parseProjectArchiveProgress({ fraction: 0.4, message: 'Copying' }).fraction, 0.4));
  check(() => assert.equal(w.parseProjectArchiveProgress({ fraction: Number.NaN }).fraction, undefined));
  check(() => assert.equal(w.parseProjectArchiveProgress({ fraction: 42 }).fraction, undefined));
  check(() => assert.equal(w.projectArchiveTerminalConfirmed(new Error('network failed')), false));

  // Write ownership is visible before preflight, and remains until the completed terminal receipt.
  const calls = [], owners = new Set(), snapshots = [], preflight = deferred(), prepare = deferred();
  const backend = { supports: () => true, request(method, parameters, options) {
    calls.push({ method, parameters, options });
    assert.equal(owners.size, 1);
    return method === 'project.open' ? preflight.promise : prepare.promise;
  } };
  const flow = new w.ProjectArchiveWorkflow(run => snapshots.push(run));
  const register = (owner, dirty) => dirty ? owners.add(owner) : owners.delete(owner);
  const captured = w.captureProjectArchiveSource(context, backend);
  const task = flow.prepare(captured, false, register, () => true);
  check(() => assert.equal(flow.protected, true));
  check(() => assert.equal(owners.size, 1));
  check(() => assert.deepEqual(calls.map(call => call.method), ['project.open']));
  preflight.resolve(opened); await new Promise(resolve => setImmediate(resolve));
  check(() => assert.deepEqual(calls.map(call => call.method), ['project.open', 'developer.projectArchive.prepare']));
  calls[1].options.onProgress({ fraction: 0.3, message: 'Preparing' });
  calls[1].options.onProgress({ taskName: 'Zip' });
  check(() => assert.equal(flow.run.progress.fraction, 0.3));
  flow.cancel();
  check(() => assert.equal(calls[1].options.signal.aborted, true));
  check(() => assert.equal(flow.protected, true));
  // Cancel can race completion. A real completed receipt remains downloadable/retained.
  prepare.resolve(receipt); await task;
  check(() => assert.equal(flow.run.receipt.state, 'completed'));
  check(() => assert.equal(flow.run.cleanup, 'retained'));
  check(() => assert.equal(flow.protected, false));
  check(() => assert.equal(flow.canPrepare, false));
  check(() => assert.equal(owners.size, 0));
  await flow.prepare(captured, false, register, () => true);
  check(() => assert.equal(calls.length, 2));

  // Cleanup uses only the returned capability; failed outcomes retain it and retry is idempotent.
  let cleanAttempt = 0;
  backend.request = async (method, parameters) => {
    assert.equal(method, 'developer.projectArchive.cleanup'); assert.deepEqual(parameters, { cleanupToken: token }); cleanAttempt++;
    if (cleanAttempt === 1) return { cleaned: false };
    if (cleanAttempt === 2) throw new Error('response lost');
    return { cleaned: true };
  };
  await flow.cleanup(); check(() => assert.equal(flow.run.cleanup, 'failed'));
  check(() => assert.equal(flow.canPrepare, false));
  await flow.cleanup(); check(() => assert.equal(flow.run.cleanup, 'failed'));
  check(() => assert.equal(flow.run.receipt.cleanupToken, token));
  await flow.cleanup(); check(() => assert.equal(flow.run.cleanup, 'cleaned'));
  check(() => assert.equal(flow.canPrepare, true));
  await flow.cleanup(); check(() => assert.equal(cleanAttempt, 3));

  // A lost prepare response cannot be retried as POST. Resume follows only the captured backend and ID.
  const observed = [], unknownOwners = new Set();
  const unknownGateway = { supports: () => true, async request(method, parameters, options) {
    observed.push(method);
    if (method === 'project.open') return opened;
    options.onTransportState({ state: 'connected', operationId: 'original-operation', recoveredAfterAttempt: 0 });
    throw Object.assign(new Error('lost response'), { operationId: 'original-operation' });
  }, async resumeOperation(operationID, options) {
    observed.push(`resume:${operationID}`);
    options.onTransportState({ state: 'connected', operationId: 'wrong-operation', recoveredAfterAttempt: 0 });
    return cancelled;
  } };
  const unknown = new w.ProjectArchiveWorkflow(() => {}), unknownSource = w.captureProjectArchiveSource(context, unknownGateway);
  await unknown.prepare(unknownSource, false, (owner, dirty) => dirty ? unknownOwners.add(owner) : unknownOwners.delete(owner), () => true);
  check(() => assert.equal(unknown.run.phase, 'unknown'));
  check(() => assert.equal(unknown.protected, true));
  await unknown.prepare(unknownSource, false, () => {}, () => true);
  check(() => assert.equal(observed.length, 2));
  await unknown.resume();
  check(() => assert.deepEqual(observed, ['project.open', 'developer.projectArchive.prepare', 'resume:original-operation']));
  check(() => assert.equal(unknown.run.operationID, 'original-operation'));
  check(() => assert.equal(unknown.run.receipt.state, 'cancelled'));
  check(() => assert.equal(unknownOwners.size, 0));

  // Source drift during preflight and a cancelled preflight do not dispatch a write.
  for (const kind of ['changed', 'xml', 'cancel']) {
    const gate = deferred(), writes = [], held = new Set();
    const b = { supports: () => true, request(method) { writes.push(method); return gate.promise; } };
    const f = new w.ProjectArchiveWorkflow(() => {});
    const pending = f.prepare(w.captureProjectArchiveSource(context, b), false,
      (owner, dirty) => dirty ? held.add(owner) : held.delete(owner), () => kind !== 'changed');
    if (kind === 'cancel') f.cancel();
    gate.resolve(kind === 'xml' ? { ...opened, projectXml: '<Changed />' } : opened); await pending;
    check(() => assert.deepEqual(writes, ['project.open']));
    check(() => assert.equal(f.run.phase, 'terminal'));
    check(() => assert.equal(held.size, 0));
    check(() => assert.equal(f.run.receipt, null));
  }
  // Wrong-project terminal receipt stays protected, with no token eligible for cleanup.
  const wrong = new w.ProjectArchiveWorkflow(() => {});
  const wrongBackend = { supports: () => true, request: async method => method === 'project.open' ? opened : { ...receipt, projectPath: '/elsewhere.ras' } };
  await wrong.prepare(w.captureProjectArchiveSource(context, wrongBackend), false, () => {}, () => true);
  check(() => assert.equal(wrong.run.phase, 'unverified'));
  check(() => assert.equal(wrong.protected, true));
  check(() => assert.equal(wrong.run.receipt, null));
  process.stdout.write(`Project archive protocol/workflow checks passed: ${checks}. Real ZIP/download acceptance is separate.\n`);
} finally { await rm(temporary, { recursive: true, force: true }); }
