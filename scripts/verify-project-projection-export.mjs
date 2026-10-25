import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { build } from 'vite';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const temporary = await mkdtemp(path.join(tmpdir(), 'hecras-projection-export-'));
const checks = [];
try {
  const directory = path.join(root, 'frontend/src/features/project/behaviors');
  const entry = path.join(temporary, 'entry.ts');
  await writeFile(entry, ['projectProjectionExport', 'ProjectProjectionExportController', 'GenericProjectBehaviors', 'ProjectBehaviors', 'ProjectCatalogHeaderBehaviors']
    .map(file => `export * from ${JSON.stringify(path.join(directory, file + '.ts'))};`).join('\n'));
  await build({ configFile: false, root, logLevel: 'silent', build: { lib: { entry, formats: ['es'], fileName: () => 'projection.mjs' },
    outDir: path.join(temporary, 'built'), emptyOutDir: true, minify: false, sourcemap: false } });
  const api = await import(pathToFileURL(path.join(temporary, 'built/projection.mjs')).href);
  const sha = text => createHash('sha256').update(text).digest('hex');
  const context = () => ({ projectSessionID: 'session-A', projectPath: '/fixture/项目.ras', projectRevision: sha('project'),
    projectXml: '<Project><Projection Type="EPSG">32610</Projection></Project>\n', selectedAsset: { kind: 'Geometry', path: '/foreign/model.h5', revision: sha('geometry') },
    coreLayerContext: { projectSessionID: 'session-A', projectPath: '/fixture/项目.ras', projectRevision: sha('project'), identityToken: sha('Core A'), references: [], sessionHdfReferences: [] } });
  const source = () => { const ctx = context(); return { context: ctx, catalogSourceKey: api.projectProjectionSourceKey(ctx), projectionWKT: 'PROJCS["source"]' }; };
  const receipt = request => ({ projectPath: request.projectPath, projectRevision: request.expectedProjectRevision, outputPath: request.destinationPath.replace('.prj', ' (1).prj') });
  const file = outputPath => ({ sourcePath: outputPath, canonicalPath: outputPath, revision: sha('actual exported bytes'), length: 485 });
  const capture = api.captureProjectProjection(source(), 'operation-A');
  assert.equal(capture.request.destinationPath, '/fixture/Exports/Projection.prj');
  assert.equal(capture.request.projectPath, '/fixture/项目.ras');
  assert.equal(capture.request.projectXml, context().projectXml);
  assert.equal(capture.request.coreLayerContext.identityToken, sha('Core A'));
  const original = source(), frozen = api.captureProjectProjection(original, 'operation-B');
  original.context.coreLayerContext.identityToken = 'mutated'; original.context.projectXml += 'mutated';
  assert.equal(frozen.request.coreLayerContext.identityToken, sha('Core A')); assert.equal(frozen.request.projectXml, context().projectXml);
  assert.equal(api.projectProjectionDestination('C:\\工程\\项目.ras'), 'C:\\工程\\Exports\\Projection.prj');
  assert.equal(api.projectProjectionDestination('\\\\server\\share\\项目.ras'), '\\\\server\\share\\Exports\\Projection.prj');
  assert.equal(api.projectProjectionDestination('/project.ras'), '/Exports/Projection.prj');
  for (const mutate of [s => s.context.projectPath = null, s => s.context.projectPath = 'browser-file://local.ras', s => s.context.projectPath = 'relative.ras',
    s => s.context.projectSessionID = '', s => s.context.projectXml = '', s => s.context.projectRevision = 'bad',
    s => s.context.coreLayerContext.identityToken = 'bad', s => s.context.coreLayerContext.projectSessionID = 'wrong',
    s => s.catalogSourceKey = 'old', s => s.projectionWKT = null, s => s.projectionWKT = ' ']) {
    const candidate = source(); mutate(candidate); assert.ok(api.projectProjectionSourceIssue(candidate)); assert.throws(() => api.captureProjectProjection(candidate, 'test'));
  }
  const key = api.projectProjectionSourceKey(context());
  for (const mutate of [c => c.projectSessionID += 'B', c => c.projectXml += ' ', c => c.projectRevision = sha('B'), c => c.projectPath += 'B', c => c.coreLayerContext.identityToken = sha('B')]) {
    const candidate = context(); mutate(candidate); assert.notEqual(api.projectProjectionSourceKey(candidate), key);
  }
  const modelSelection = context(); modelSelection.selectedAsset.path = '/other.h5'; assert.equal(api.projectProjectionSourceKey(modelSelection), key);
  checks.push('Detached exact project/session/XML/Core capture; selected model never substitutes project; saved-source and projection gates; original Exports path on Unix/Windows/UNC.');

  const good = receipt(capture.request);
  assert.equal(api.parseProjectProjectionReceipt(good, capture).outputPath, good.outputPath);
  for (const patch of [{ projectPath: '/wrong.ras' }, { projectRevision: sha('wrong') }, { outputPath: '/foreign/Projection.prj' },
    { outputPath: '/fixture/Exports/../Exports/Projection.prj' }, { outputPath: '/fixture/Exports/Projection (1000).prj' },
    { outputPath: '/fixture/Exports/Projection (0).prj' }, { outputPath: '/fixture/Exports/Other.prj' }])
    assert.throws(() => api.parseProjectProjectionReceipt({ ...good, ...patch }, capture));
  assert.equal(api.parseProjectProjectionFile(file(good.outputPath), good).length, 485);
  for (const patch of [{ sourcePath: '/wrong.prj' }, { canonicalPath: '/wrong.prj' }, { revision: 'bad' }, { length: 0 }, { length: -1 }, { length: 1.5 }])
    assert.throws(() => api.parseProjectProjectionFile({ ...file(good.outputPath), ...patch }, good));
  assert.equal(api.ProjectBehaviors.AttachActions_Layer_ContextMenu({ sourceClass: 'ModelSectionContext' })[0], api.GenericProjectBehaviors.ExportProjection);
  assert.deepEqual(api.ProjectBehaviors.AttachActions_Layer_ContextMenu({ sourceClass: 'ObservableGeometry' }), []);
  assert.equal(api.ProjectCatalogHeaderBehaviors.GetContextMenuActions()[0], api.GenericProjectBehaviors.ExportProjection);
  checks.push('Strict unique destination, source revision and nonempty hashed-file receipts; original behavior registration stays project-specific.');

  const defer = () => { let resolve, reject; const promise = new Promise((yes, no) => { resolve = yes; reject = no; }); return { promise, resolve, reject }; };
  const until = async predicate => { for (let count = 0; count < 100; count++) { if (predicate()) return; await new Promise(resolve => setImmediate(resolve)); } throw new Error('Protocol test timeout'); };
  const environment = () => {
    const owners = new Set(), calls = [], resumes = []; let current = true;
    const controller = new api.ProjectProjectionExportController((owner, dirty) => dirty ? owners.add(owner) : owners.delete(owner));
    const backend = { supports: () => true, request: (method, parameters, options) => {
      const pending = defer(); calls.push({ method, parameters, options, pending });
      if (method === 'project.projection.export') options.onTransportState({ state: 'connected', operationId: 'original-http-id', recoveredAfterAttempt: 0 });
      return pending.promise;
    }, resumeOperation: (id, options) => { const pending = defer(); resumes.push({ id, options, pending }); return pending.promise; } };
    return { controller, backend, owners, calls, resumes, stillCurrent: () => current, changeSource: () => { current = false; } };
  };
  {
    const env = environment();
    await env.controller.start(source(), env.backend, env.stillCurrent, true);
    assert.equal(env.calls.length, 0);
    const unavailable = { ...env.backend, supports: () => false };
    await env.controller.start(source(), unavailable, env.stillCurrent, false); assert.equal(env.calls.length, 0);
    const unsafe = new api.ProjectProjectionExportController(undefined);
    await unsafe.start(source(), env.backend, env.stillCurrent, false); assert.equal(env.calls.length, 0);
  }
  {
    const env = environment(), done = env.controller.start(source(), env.backend, env.stillCurrent, false);
    assert.equal(env.owners.size, 1); assert.match([...env.owners][0], /^project\.projection-export:/);
    await env.controller.start(source(), env.backend, env.stillCurrent, false); assert.equal(env.calls.length, 1);
    env.calls[0].pending.reject(Object.assign(new Error('connection lost'), { terminalConfirmed: false, operationId: 'original-http-id' })); await done;
    assert.equal(env.controller.getSnapshot().task.phase, 'unknown'); assert.equal(env.owners.size, 1); assert.equal(env.controller.canResume(), true);
    const resumed = env.controller.resume(); assert.equal(env.resumes[0].id, 'original-http-id'); assert.equal(env.calls.length, 1);
    env.resumes[0].pending.resolve(receipt(env.calls[0].parameters)); await until(() => env.calls.length === 2);
    assert.equal(env.calls[1].method, 'workspace.file.inspect'); assert.equal(env.owners.size, 1);
    env.calls[1].pending.resolve(file(env.calls[1].parameters.sourcePath)); await resumed;
    assert.equal(env.controller.getSnapshot().task.phase, 'completed'); assert.equal(env.owners.size, 0);
    assert.equal(env.calls.filter(call => call.method === 'project.projection.export').length, 1);
  }
  checks.push('Dirty/capability/write-owner gates; synchronous ownership; duplicate prevention; uncertain HTTP status keeps protection and resumes only the original task.');
  {
    const env = environment(), done = env.controller.start(source(), env.backend, env.stillCurrent, false);
    env.calls[0].pending.resolve(receipt(env.calls[0].parameters)); await until(() => env.calls.length === 2);
    env.calls[1].pending.reject(new Error('readback interrupted')); await done;
    assert.equal(env.controller.getSnapshot().task.phase, 'readback-failed'); assert.equal(env.owners.size, 0);
    const retry = env.controller.retryReadback(); assert.equal(env.calls[2].method, 'workspace.file.inspect');
    env.calls[2].pending.resolve(file(env.calls[2].parameters.sourcePath)); await retry;
    assert.equal(env.controller.getSnapshot().task.phase, 'completed'); assert.equal(env.calls.filter(call => call.method === 'project.projection.export').length, 1);
  }
  for (const stage of ['receipt', 'readback']) {
    const env = environment(), done = env.controller.start(source(), env.backend, env.stillCurrent, false);
    if (stage === 'receipt') env.changeSource();
    env.calls[0].pending.resolve(receipt(env.calls[0].parameters));
    if (stage === 'readback') { await until(() => env.calls.length === 2); env.changeSource(); env.calls[1].pending.resolve(file(env.calls[1].parameters.sourcePath)); }
    await done; assert.equal(env.controller.getSnapshot().task.phase, 'failed'); assert.equal(env.controller.getSnapshot().task.receipt, undefined); assert.equal(env.owners.size, 0);
  }
  {
    const env = environment(), done = env.controller.start(source(), env.backend, env.stillCurrent, false);
    env.calls[0].pending.resolve({ ...receipt(env.calls[0].parameters), projectRevision: sha('other') }); await done;
    assert.equal(env.controller.getSnapshot().task.phase, 'failed'); assert.equal(env.calls.length, 1); assert.equal(env.owners.size, 0);
  }
  {
    const env = environment(), done = env.controller.start(source(), env.backend, env.stillCurrent, false);
    env.calls[0].pending.reject(Object.assign(new Error('server rejected'), { terminalConfirmed: true })); await done;
    assert.equal(env.controller.getSnapshot().task.phase, 'failed'); assert.equal(env.owners.size, 0);
  }
  {
    const env = environment(); env.backend.request = (_method, _parameters, _options) => Promise.reject(Object.assign(new Error('acceptance unknown'), { terminalConfirmed: false }));
    await env.controller.start(source(), env.backend, env.stillCurrent, false);
    assert.equal(env.controller.getSnapshot().task.phase, 'unknown'); assert.equal(env.controller.canResume(), false); assert.equal(env.owners.size, 1);
  }
  checks.push('Readback retries never re-export; stale project/session/XML/Core captures and wrong revisions cannot publish success; confirmed failure releases ownership, unknown acceptance retains it.');
  console.log(JSON.stringify({ passed: true, checks }, null, 2));
} finally { await rm(temporary, { recursive: true, force: true }); }
