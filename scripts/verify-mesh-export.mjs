import assert from 'node:assert/strict';
import { createHash, randomUUID } from 'node:crypto';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { build } from 'vite';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const temporary = await mkdtemp(path.join(tmpdir(), 'hecras-mesh-export-'));
const checks = [];
try {
  const data = path.join(root, 'frontend/src/features/geometry/meshShapefileExport.ts');
  const controller = path.join(root, 'frontend/src/features/geometry/MeshShapefileExportController.ts');
  const entry = path.join(temporary, 'entry.ts');
  await writeFile(entry, `export * from ${JSON.stringify(data)};\nexport * from ${JSON.stringify(controller)};\n`);
  await build({ configFile: false, root, logLevel: 'silent', build: {
    lib: { entry, formats: ['es'], fileName: () => 'mesh-export.mjs' },
    outDir: path.join(temporary, 'built'), emptyOutDir: true, minify: false, sourcemap: false,
  } });
  const { captureMeshExport, hashMeshExportCapture, parseMeshExportReceipt, meshExportSourceKey,
    meshExportSourceIssue, validateMeshExportPath, MeshShapefileExportController } = await import(pathToFileURL(path.join(temporary, 'built/mesh-export.mjs')).href);
  const sha = text => createHash('sha256').update(text).digest('hex').toUpperCase();
  const revision = sha('real-source-identity-test');
  const projectRevision = sha('project-source-identity-test');
  const source = () => {
    const context = { projectSessionID: 'project-session-A', projectPath: '/source/project.ras', projectXml: '<Project>原文</Project>',
      projectRevision, selectedAsset: { kind: 'Geometry', path: '/source/Geometry.h5', name: '几何', revision },
      coreLayerContext: { projectSessionID: 'project-session-A', projectPath: '/source/project.ras', identityToken: 'core-A' } };
    return { context, model: { assetPath: context.selectedAsset.path, kind: 'Geometry', projectPath: context.projectPath,
      projectXml: context.projectXml, coreLayerContext: structuredClone(context.coreLayerContext) }, localOnly: false,
      mesh: { revision, draftState: '网格 opaque draft\n  ', topologyFingerprint: 'topology-A', flowAreas: [
        { id: 'flow-area-row:4', rowIndex: 4, name: '真实名称', hasMesh: true, cellCount: 16, faceCount: 40 },
        { id: 'flow-area-row:8', rowIndex: 8, name: '同名不充当身份', hasMesh: false, cellCount: 0, faceCount: 0 } ] },
      topology: { revision: revision.toLowerCase(), draftState: '拓扑 opaque draft\r\n' } };
  };
  const form = { flowAreaID: 'flow-area-row:4', exportKind: 'CellCenters', outputPath: '/exports/单元中心.shp' };
  const validReceipt = request => ({ operationID: request.operationID, state: 'completed', canonicalAssetPath: request.model.assetPath,
    sourceRevision: request.revision, flowAreaID: request.flowAreaID, flowAreaName: request.expectedFlowAreaName, exportKind: request.exportKind,
    meshDraftSHA256: request.meshDraftSHA256, topologyDraftSHA256: request.topologyDraftSHA256,
    outputPath: request.outputPath, featureCount: 16, projectionWKT: null,
    artifacts: ['.shx', '.dbf', '.shp'].map((ext, index) => ({ path: request.outputPath.slice(0, -4) + ext, sizeBytes: 100 + index, sha256: sha(ext) })) });
  const original = source();
  const captured = await hashMeshExportCapture(captureMeshExport(original, form, randomUUID()));
  assert.equal(captured.request.draftState, '网格 opaque draft\n  ');
  assert.equal(captured.request.topologyDraftState, '拓扑 opaque draft\r\n');
  assert.equal(captured.request.meshDraftSHA256, sha(captured.request.draftState));
  assert.equal(captured.request.topologyDraftSHA256, sha(captured.request.topologyDraftState));
  assert.equal(captured.request.flowAreaID, 'flow-area-row:4');
  original.model.coreLayerContext.identityToken = 'modified'; original.context.selectedAsset.path = '/wrong.h5';
  assert.equal(captured.request.model.coreLayerContext.identityToken, 'core-A');
  assert.equal(captured.context.selectedAsset.path, '/source/Geometry.h5');
  checks.push('Synchronous detached source capture preserves session/Core, source row ID, and exact Unicode draft bytes/hashes.');

  const baseline = source(), key = meshExportSourceKey(baseline);
  for (const change of [s => { s.context.projectSessionID = 'B'; }, s => { s.context.projectRevision = sha('B'); },
    s => { s.context.projectXml += ' '; }, s => { s.context.coreLayerContext.identityToken = 'B'; },
    s => { s.model.assetPath = '/other.h5'; }, s => { s.mesh.revision = sha('B'); }, s => { s.mesh.draftState += ' '; },
    s => { s.topology.draftState += ' '; }, s => { s.mesh.flowAreas[0].name = '新名字'; }]) {
    const changed = structuredClone(baseline); change(changed); assert.notEqual(meshExportSourceKey(changed), key);
  }
  for (const change of [s => { s.context.projectSessionID = ''; }, s => { s.model.assetPath = '/wrong.h5'; },
    s => { s.model.coreLayerContext.identityToken = 'other'; }, s => { s.topology.revision = sha('other'); },
    s => { s.mesh.draftState = ''; }, s => { s.topology = undefined; }, s => { s.localOnly = true; },
    s => { s.mesh.flowAreas.push(structuredClone(s.mesh.flowAreas[0])); }]) {
    const changed = source(); change(changed); assert.ok(meshExportSourceIssue(changed, form.flowAreaID));
  }
  assert.ok(meshExportSourceIssue(source(), 'flow-area-row:8'));
  assert.ok(meshExportSourceIssue(source(), '真实名称'));
  for (const output of ['relative.shp', '/exports/../a.shp', '/exports/./a.shp', '/exports//a.shp', ' /exports/a.shp', '/exports/a.shp\n']) assert.throws(() => validateMeshExportPath(output));
  for (const output of ['/exports/a.shp', '/exports/a.SHP', 'C:\\exports\\a.shp', '\\\\server\\share\\a.shp']) assert.equal(validateMeshExportPath(output), output);
  checks.push('Source changes invalidate preparation; incomplete/mixed/local-only sources and ambiguous rows cannot export; target is an explicit absolute .shp path.');

  const good = validReceipt(captured.request);
  assert.equal(parseMeshExportReceipt(good, captured).artifacts.length, 3);
  const upperCapture = await hashMeshExportCapture(captureMeshExport(source(), { ...form, outputPath: '/exports/Upper.SHP' }, randomUUID()));
  assert.equal(parseMeshExportReceipt(validReceipt(upperCapture.request), upperCapture).outputPath, '/exports/Upper.SHP');
  const projected = structuredClone(good); projected.projectionWKT = 'PROJCS[source]'; projected.artifacts.push({ path: form.outputPath.slice(0, -4) + '.prj', sizeBytes: 45, sha256: sha('prj') });
  assert.equal(parseMeshExportReceipt(projected, captured).artifacts.length, 4);
  for (const [field, value] of Object.entries({ operationID: randomUUID(), canonicalAssetPath: '/other.h5', sourceRevision: sha('other'),
    flowAreaID: 'flow-area-row:0', flowAreaName: 'other', exportKind: 'PerimeterFaces', meshDraftSHA256: sha('other'),
    topologyDraftSHA256: sha('other'), outputPath: '/wrong/a.shp', featureCount: 15, state: 'queued' })) {
    assert.throws(() => parseMeshExportReceipt({ ...good, [field]: value }, captured), field);
  }
  for (const change of [r => { r.artifacts.pop(); }, r => { r.artifacts.push(r.artifacts[0]); }, r => { r.artifacts[0].path = '/foreign/file.shx'; },
    r => { r.artifacts[0].sha256 = 'bad'; }, r => { r.artifacts[0].sizeBytes = -1; }, r => { r.projectionWKT = 'WKT'; },
    r => { r.artifacts.push({ path: form.outputPath.slice(0, -4) + '.prj', sizeBytes: 5, sha256: sha('invented') }); }]) {
    const bad = structuredClone(good); change(bad); assert.throws(() => parseMeshExportReceipt(bad, captured));
  }
  const cancelled = { ...good, state: 'cancelled', outputPath: null, featureCount: 0, artifacts: [] };
  assert.equal(parseMeshExportReceipt(cancelled, captured).state, 'cancelled');
  assert.throws(() => parseMeshExportReceipt({ ...cancelled, artifacts: good.artifacts }, captured));
  const perimeterCapture = await hashMeshExportCapture(captureMeshExport(source(), { ...form, exportKind: 'PerimeterFaces' }, randomUUID()));
  assert.equal(parseMeshExportReceipt(validReceipt(perimeterCapture.request), perimeterCapture).featureCount, 16);
  assert.throws(() => parseMeshExportReceipt({ ...validReceipt(perimeterCapture.request), featureCount: 41 }, perimeterCapture));
  checks.push('Only matching terminal receipts with a complete unique same-destination file family and real projection/count semantics are accepted.');

  const deferred = () => { let resolve, reject; const promise = new Promise((yes, no) => { resolve = yes; reject = no; }); return { promise, resolve, reject }; };
  const until = async predicate => { const deadline = Date.now() + 3000; while (!predicate()) { if (Date.now() > deadline) throw new Error('Protocol test timed out'); await new Promise(resolve => setImmediate(resolve)); } };
  const environment = (hash) => {
    const owners = new Set(), events = [], calls = [], resumes = [];
    const instance = new MeshShapefileExportController((owner, active) => { events.push([owner, active]); active ? owners.add(owner) : owners.delete(owner); }, hash);
    const backend = { supports: () => true, request: (method, parameters, options) => {
      const pending = deferred(); calls.push({ method, parameters, options, pending });
      options.onTransportState({ state: 'connected', operationId: 'http-original', recoveredAfterAttempt: 0 }); return pending.promise;
    }, resumeOperation: (operationID, options) => { const pending = deferred(); resumes.push({ operationID, options, pending });
      options.onTransportState({ state: 'connected', operationId: operationID, recoveredAfterAttempt: 0 }); return pending.promise; } };
    return { instance, backend, owners, events, calls, resumes };
  };
  {
    const unsafe = new MeshShapefileExportController(undefined), env = environment();
    await unsafe.start(source(), form, env.backend, () => true);
    assert.equal(env.calls.length, 0); assert.equal(unsafe.getSnapshot().tasks.length, 0);
    assert.match(unsafe.getSnapshot().validationError.message, /写入保护/);
    checks.push('A controller without workspace write protection refuses to submit an export.');
  }
  {
    const env = environment(), liveSource = source();
    const done = env.instance.start(liveSource, form, env.backend, () => true);
    assert.equal(env.owners.size, 1, 'Root is locked before hashing yields.');
    await until(() => env.calls.length === 1);
    liveSource.context.projectSessionID = 'new-project'; liveSource.mesh.draftState = 'new draft';
    await env.instance.start(source(), form, env.backend, () => true); assert.equal(env.calls.length, 1);
    env.instance.cancel(); assert.equal(env.calls[0].options.signal.aborted, true);
    assert.equal(env.instance.getSnapshot().tasks[0].phase, 'cancelling'); assert.equal(env.owners.size, 1);
    env.calls[0].options.onTransportState({ state: 'cancelSent', operationId: 'http-original', accepted: true });
    assert.equal(env.owners.size, 1, 'Cancellation delivery is not a terminal receipt.');
    env.calls[0].pending.resolve(validReceipt(env.calls[0].parameters)); await done;
    const task = env.instance.getSnapshot().tasks[0];
    assert.equal(task.phase, 'completed', 'A completed publication wins a cancellation race.');
    assert.equal(task.capture.context.projectSessionID, 'project-session-A'); assert.equal(task.capture.request.draftState, '网格 opaque draft\n  ');
    assert.equal(env.owners.size, 0); assert.equal(env.instance.pending, false);
    checks.push('Task/source survives navigation mutations; duplicate export is blocked; cancel delivery retains dirty ownership; completed receipt wins cancel race.');
  }
  {
    const env = environment(), done = env.instance.start(source(), form, env.backend, () => true);
    await until(() => env.calls.length === 1);
    env.calls[0].pending.reject(Object.assign(new Error('link lost'), { terminalConfirmed: false, operationId: 'http-original' })); await done;
    assert.equal(env.instance.getSnapshot().tasks[0].phase, 'unknown'); assert.equal(env.owners.size, 1);
    await env.instance.start(source(), form, env.backend, () => true); assert.equal(env.calls.length, 1);
    const resumed = env.instance.resume(); await until(() => env.resumes.length === 1);
    assert.equal(env.resumes[0].operationID, 'http-original'); assert.equal(env.resumes[0].options.signal.aborted, false);
    env.calls[0].options.onProgress({ taskName: 'late old event', fraction: 0.9 });
    assert.equal(env.instance.getSnapshot().tasks[0].progress, undefined, 'Late events from the retired tracking attempt are ignored.');
    env.resumes[0].pending.resolve(validReceipt(env.calls[0].parameters)); await resumed;
    assert.equal(env.instance.getSnapshot().tasks[0].phase, 'completed'); assert.equal(env.owners.size, 0); assert.equal(env.calls.length, 1);
    checks.push('Unconfirmed task persists and resumes with GET-only gateway tracking of the same HTTP ID, without another export POST or stale progress.');
  }
  {
    const env = environment(), done = env.instance.start(source(), form, env.backend, () => true);
    await until(() => env.calls.length === 1);
    env.calls[0].pending.resolve({ ...validReceipt(env.calls[0].parameters), flowAreaID: 'late-other-flow-area' }); await done;
    assert.equal(env.instance.getSnapshot().tasks[0].phase, 'unknown'); assert.equal(env.owners.size, 1);
    const resumed = env.instance.resume(); await until(() => env.resumes.length === 1);
    env.resumes[0].pending.reject(Object.assign(new Error('cancelled'), { name: 'AbortError', terminalConfirmed: true, operationId: 'http-original' })); await resumed;
    assert.equal(env.instance.getSnapshot().tasks[0].phase, 'cancelled'); assert.equal(env.owners.size, 0);
    checks.push('Wrong-source terminal payload stays unresolved; resumed confirmed cancellation releases the owner without requiring a new abort.');
  }
  {
    const env = environment(); env.backend.request = () => Promise.reject(Object.assign(new Error('acceptance uncertain'), { terminalConfirmed: false }));
    await env.instance.start(source(), form, env.backend, () => true);
    assert.equal(env.instance.getSnapshot().tasks[0].phase, 'unknown'); assert.equal(env.instance.canResume(), false); assert.equal(env.owners.size, 1);
    await env.instance.resume(); assert.equal(env.resumes.length, 0);
    checks.push('Unknown acceptance without an HTTP ID cannot invent an ID, replay the write, or release dirty ownership.');
  }
  {
    const gate = deferred(), env = environment(capture => gate.promise.then(() => hashMeshExportCapture(capture)));
    const done = env.instance.start(source(), form, env.backend, () => true); env.instance.cancel(); gate.resolve(); await done;
    assert.equal(env.calls.length, 0); assert.equal(env.instance.getSnapshot().tasks[0].phase, 'cancelled'); assert.equal(env.owners.size, 0);
    const changed = environment(); await changed.instance.start(source(), form, changed.backend, () => false);
    assert.equal(changed.calls.length, 0); assert.equal(changed.instance.getSnapshot().tasks[0].phase, 'failed'); assert.equal(changed.owners.size, 0);
    checks.push('Preparation cancellation or source change retires only a request that was never sent.');
  }
  {
    const env = environment(), done = env.instance.start(source(), form, env.backend, () => true); await until(() => env.calls.length === 1);
    env.calls[0].pending.reject(Object.assign(new Error('Existing family cannot be overwritten.'), { terminalConfirmed: true, operationId: 'http-original', code: 'sourceFailure' })); await done;
    assert.equal(env.instance.getSnapshot().tasks[0].phase, 'failed'); assert.equal(env.owners.size, 0);
    const next = env.instance.start(source(), { ...form, outputPath: '/exports/new.shp' }, env.backend, () => true); await until(() => env.calls.length === 2);
    assert.notEqual(env.calls[1].parameters.operationID, env.calls[0].parameters.operationID);
    env.calls[1].pending.resolve(validReceipt(env.calls[1].parameters)); await next;
    assert.deepEqual(env.instance.getSnapshot().tasks.map(task => task.phase), ['failed', 'completed']);
    checks.push('Confirmed failure permits a fresh operation while preserving the previous captured-source record.');
  }
  console.log(JSON.stringify({ state: 'passed', evidence: 'Component/protocol regression only; real Host/UI acceptance is separate.', checks }, null, 2));
} finally { await rm(temporary, { recursive: true, force: true }); }
