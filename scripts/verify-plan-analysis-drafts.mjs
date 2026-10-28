import assert from 'node:assert/strict';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { build } from 'vite';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const temporary = await mkdtemp(path.join(tmpdir(), 'hecras-plan-analysis-drafts-'));
try {
  // Bundle only the pure production helpers into a temporary directory, never product dist.
  await build({ configFile: false, root, logLevel: 'silent', build: {
    lib: { entry: {
      registry: path.join(root, 'frontend/src/shared/planAnalysisDrafts.ts'),
      capture: path.join(root, 'frontend/src/shared/planAnalysisDraftCapture.ts'),
      lifecycle: path.join(root, 'frontend/src/shared/planAnalysisDraftLifecycle.ts'),
    }, formats: ['es'], fileName: (_format, name) => `${name}.mjs` },
    outDir: temporary, emptyOutDir: true, minify: false, sourcemap: false,
  } });
  const registryModule = await import(pathToFileURL(path.join(temporary, 'registry.mjs')).href);
  const { capturePlanEditorDraft } = await import(pathToFileURL(path.join(temporary, 'capture.mjs')).href);
  const { capturePlanSaveRecovery, trackPlanSaveOperation, resumeCapturedPlanSave, isConfirmedPlanSaveTerminal, matchesPlanDiscardTransition }
    = await import(pathToFileURL(path.join(temporary, 'lifecycle.mjs')).href);
  const { capturePlanAnalysisSource, emptyPlanAnalysisDraftRegistry, publishPlanAnalysisDraft,
    clearPlanAnalysisDraftRegistry, resolveAnalysisDrafts, hasProtectedSourceWrite, matchesPlanSaveRecoveryAcknowledgement } = registryModule;
  let passed = 0;
  const test = (name, run) => { run(); passed++; console.log(`PASS ${name}`); };
  const backend = { supports: () => true, request: async () => { throw new Error('Pure test must not call a backend'); } };
  const project = { projectSessionID: 'session-A', projectPath: '/project/a.ras', projectRevision: 'A'.repeat(64),
    projectXml: '<Project id="a"/>', selectedAsset: { path: '/project/a.plan.hdf', kind: 'Plan', name: 'A', revision: 'B'.repeat(64) },
    coreLayerContext: { identityToken: 'C'.repeat(64), references: [], projectSessionID: 'session-A', projectPath: '/project/a.ras' } };
  const editorID = '00000000-0000-4000-8000-000000000001';
  const ownerContextID = 'exact-source-owner-A';
  const snapshot = { revision: project.selectedAsset.revision,
    fields: { value: '0.1', core: '8', mode: '1', start: '2026-10-16T00:00:00', end: '2026-10-16T00:00:04', derived: 'read-only' },
    sourceState: '{"PlanTimeWindowDraft":{"mode":1}}', readOnlyFields: ['derived'],
    planTimeWindowFieldIDs: { mode: 'mode', start: 'start', end: 'end' } };
  const baseline = { sourceState: snapshot.sourceState, fields: { ...snapshot.fields }, images: {}, rows: {}, samples: {}, sampleText: {} };
  const draft = structuredClone(baseline);
  draft.fields.value = ' 0.2500 ';
  draft.fields.core = '4';
  draft.fields.mode = '2';
  draft.fields.start = '2026-10-16T00:00:01';
  draft.fields.end = '2026-10-16T00:00:06';
  const input = { editorID, ownerContextID, assetPath: project.selectedAsset.path, snapshot, baseline, draft };
  const planDraft = capturePlanEditorDraft(input).planDraft;
  const publication = { editorID, ownerContextID, source: capturePlanAnalysisSource(project, backend, 1),
    assetPath: project.selectedAsset.path, revision: snapshot.revision, dirty: true, valid: true, busy: false,
    draftRevision: 1, status: 'draft', planDraft };
  const registry = publishPlanAnalysisDraft(emptyPlanAnalysisDraftRegistry(), editorID, publication);
  const owners = new Set([ownerContextID]);
  const required = [{ assetPath: publication.assetPath, revision: publication.revision }];
  const capture = { context: project, backend, backendRevision: 1, registry, hasUnsavedDrafts: true };
  const resolve = (overrides = {}, ownerSet = owners, paths = required) => resolveAnalysisDrafts({ ...capture, ...overrides }, ownerSet, paths);
  const registryWith = changes => publishPlanAnalysisDraft(emptyPlanAnalysisDraftRegistry(), editorID, { ...publication, ...changes });
  const codes = result => result.blockers.map(item => item.code);

  test('tool and project writes block navigation until confirmed, while ordinary settings remain discardable', () => {
    for (const owner of ['tools.stable-channel-write:save', 'project.projection-export:export', 'tools.project-archive:prepare',
      'editors.channel-v6-write:import'])
      assert.equal(hasProtectedSourceWrite(new Set([owner]), emptyPlanAnalysisDraftRegistry()), true, owner);
    for (const owner of ['tools.stable-channel-draft:form', 'tools.project-archive-settings:form'])
      assert.equal(hasProtectedSourceWrite(new Set([owner]), emptyPlanAnalysisDraftRegistry()), false, owner);
  });

  test('changed ordinary raw values and sourceState preserved; loaded derived fields omitted', () => {
    assert.deepEqual(planDraft.fields, { value: ' 0.2500 ', core: '4' });
    assert.equal(planDraft.sourceState, snapshot.sourceState);
    assert.equal(planDraft.ownerContextID, ownerContextID);
    assert.equal(planDraft.editorID, editorID);
  });
  test('time edits retain mode/start/end order and raw values, with stable ids', () => {
    assert.deepEqual(planDraft.planTimeWindowEdits.map(edit => edit.fieldID), ['mode', 'start', 'end']);
    assert.deepEqual(planDraft.planTimeWindowEdits, capturePlanEditorDraft(input).planDraft.planTimeWindowEdits);
    assert.equal(planDraft.planTimeWindowEdits[2].value, draft.fields.end);
  });
  test('unchanged time fields are not replayed', () => {
    const one = structuredClone(baseline); one.fields.end = '2026-10-16T00:00:07';
    assert.deepEqual(capturePlanEditorDraft({ ...input, draft: one }).planDraft.planTimeWindowEdits.map(item => item.fieldID), ['end']);
  });
  test('sourceState raw replacement and locator fallback match save semantics', () => {
    const changed = capturePlanEditorDraft({ ...input, draft: { ...draft, sourceState: ' exact draft state ' } });
    assert.equal(changed.planDraft.sourceState, ' exact draft state ');
    const fallback = capturePlanEditorDraft({ ...input, snapshot: { ...snapshot, sourceState: null },
      draft: { ...draft, sourceState: null }, sourceStateFallback: 'locator-state' });
    assert.equal(fallback.planDraft.sourceState, 'locator-state');
  });
  for (const key of ['images', 'rows', 'samples', 'sampleText']) test(`unsupported ${key} edits cannot silently disappear`, () => {
    assert.equal(capturePlanEditorDraft({ ...input, draft: { ...draft, [key]: { extra: [] } } }).planDraft, undefined);
  });
  test('read-only changes and missing loaded fields block capture', () => {
    assert.ok(capturePlanEditorDraft({ ...input, draft: { ...draft, fields: { ...draft.fields, derived: 'edited' } } }).unsupportedReason);
    const missing = { ...draft.fields }; delete missing.core;
    assert.ok(capturePlanEditorDraft({ ...input, draft: { ...draft, fields: missing } }).unsupportedReason);
  });
  test('immutable publication, deduplication, monotonic revision', () => {
    assert.equal(registry.revision, 1);
    assert.equal(registry.entries[0].captureRevision, 1);
    assert.equal(publishPlanAnalysisDraft(registry, editorID, publication), registry);
    assert.throws(() => { registry.entries[0].planDraft.fields.value = 'bad'; }, TypeError);
    const changed = publishPlanAnalysisDraft(registry, editorID, { ...publication, draftRevision: 2 });
    assert.equal(changed.revision, 2); assert.notEqual(changed.captureKey, registry.captureKey);
  });
  test('safe required Plan captures exactly its owner and does not mutate caller data', () => {
    const result = resolve(); assert.equal(result.ok, true); assert.equal(result.planDrafts.length, 1);
    assert.deepEqual([...result.capturedOwners], [ownerContextID]);
    result.planDrafts[0].fields.value = 'mutated copy';
    assert.equal(registry.entries[0].planDraft.fields.value, ' 0.2500 ');
  });
  test('two Plan/Result options backed by one Plan produce one draft', () => {
    const result = resolve({}, owners, [...required, ...required]);
    assert.equal(result.ok, true); assert.equal(result.planDrafts.length, 1);
  });
  test('different option revisions for same Plan are rejected', () => {
    assert.ok(codes(resolve({}, owners, [...required, { ...required[0], revision: 'D'.repeat(64) }])).includes('requiredRevisionConflict'));
  });
  test('a retained Plan remains owned after root asset selection changes', () => {
    const result = resolve({ context: { ...project, selectedAsset: { path: '/project/other.geometry.hdf', kind: 'Geometry', name: 'Other' } } });
    assert.equal(result.ok, true); assert.equal(result.captureKey, resolve().captureKey);
  });
  for (const key of ['projectSessionID', 'projectPath', 'projectRevision', 'projectXml', 'coreLayerContext']) test(`source ${key} change retires draft`, () => {
    const value = key === 'coreLayerContext' ? { identityToken: 'different' } : 'different';
    const result = resolve({ context: { ...project, [key]: value } });
    assert.equal(result.ok, false); assert.ok(codes(result).includes('sourceMismatch'));
    assert.notEqual(result.captureKey, resolve().captureKey);
  });
  test('backend instance and capability revision changes retire captured source', () => {
    assert.ok(codes(resolve({ backend: { ...backend } })).includes('sourceMismatch'));
    assert.ok(codes(resolve({ backendRevision: 2 })).includes('sourceMismatch'));
  });
  for (const status of ['invalid', 'busy', 'unavailable', 'unsupported', 'uncertain']) test(`${status} never falls back to saved Plan`, () => {
    const result = resolve({ registry: registryWith({ status, valid: false, busy: status === 'busy', planDraft: undefined }) });
    assert.equal(result.ok, false); assert.deepEqual(result.planDrafts, []); assert.ok(codes(result).includes(status));
  });
  test('duplicate Plan publishers and reused owner ids block capture', () => {
    const other = { ...publication, editorID: '00000000-0000-4000-8000-000000000002', ownerContextID: 'second-owner' };
    let duplicated = publishPlanAnalysisDraft(registry, other.editorID, other);
    assert.ok(codes(resolve({ registry: duplicated }, new Set([...owners, other.ownerContextID]))).includes('duplicatePlanOwner'));
    duplicated = publishPlanAnalysisDraft(registry, other.editorID, { ...other, assetPath: '/project/b.plan.hdf', ownerContextID });
    assert.ok(codes(resolve({ registry: duplicated })).includes('duplicateDirtyOwner'));
  });
  test('missing/mismatched Host owner and editor identities block capture', () => {
    for (const changes of [{ planDraft: undefined }, { planDraft: { ...planDraft, editorID: 'different' } },
      { planDraft: { ...planDraft, ownerContextID: 'different' } }, { planDraft: { ...planDraft, revision: 'different' } }])
      assert.ok(codes(resolve({ registry: registryWith(changes) })).includes('uncapturableOwner'));
  });
  test('missing registry, unrelated dirty owner and empty catalog requirements cannot hide dirty state', () => {
    assert.equal(resolve({ registry: undefined }).ok, false);
    assert.equal(resolve({}, new Set([...owners, 'unrelated-editor'])).ok, false);
    assert.equal(resolve({}, owners, []).ok, false);
  });
  test('unknown hasUnsavedDrafts without exact owners blocks analysis', () => {
    const empty = emptyPlanAnalysisDraftRegistry();
    assert.ok(codes(resolve({ registry: empty }, undefined)).includes('uncapturedDirtyOwner'));
    assert.ok(codes(resolveAnalysisDrafts({ ...capture, registry: empty }, undefined, required)).includes('unknownDirtyOwner'));
    assert.ok(codes(resolve({ registry: empty }, new Set())).includes('unknownDirtyOwner'));
  });
  test('publication before owner effect stays blocked', () => {
    assert.ok(codes(resolve({ hasUnsavedDrafts: false }, new Set())).includes('unregisteredDirtyOwner'));
  });
  test('own form/export exclusions stay stable without suppressing published Plan owners', () => {
    const own = 'tools.r2r-form:one';
    const existing = resolve();
    const form = resolve({ excludedDirtyOwners: new Set([own]) }, new Set([...owners, own]));
    assert.equal(form.ok, true); assert.equal(form.captureKey, existing.captureKey);
    const pending = 'tools.r2r-export:pending-one', accepted = 'tools.r2r-export:operation-one';
    assert.equal(resolve({ excludedDirtyOwners: new Set([pending]) }, new Set([...owners, pending])).captureKey,
      resolve({ excludedDirtyOwners: new Set([accepted]) }, new Set([...owners, accepted])).captureKey);
    assert.equal(resolve({ registry: registryWith({ valid: false, status: 'invalid' }), excludedDirtyOwners: owners }).ok, false);
    const clean = { ...capture, registry: emptyPlanAnalysisDraftRegistry(), hasUnsavedDrafts: false };
    assert.equal(resolveAnalysisDrafts(clean, new Set(), required).captureKey,
      resolveAnalysisDrafts({ ...clean, hasUnsavedDrafts: true, excludedDirtyOwners: new Set([own]) }, new Set([own]), required).captureKey);
  });
  test('root session transaction blocks independently from caller exclusions', () => {
    const result = resolve({ registry: { ...registry, sessionBusy: true }, excludedDirtyOwners: owners });
    assert.ok(codes(result).includes('sessionBusy'));
  });
  test('clean/discard/save replacement removes dirty payload and advances capture', () => {
    const clean = publishPlanAnalysisDraft(registry, editorID, { ...publication, dirty: false, status: 'clean', planDraft: undefined });
    const result = resolve({ registry: clean, hasUnsavedDrafts: false }, new Set());
    assert.equal(result.ok, true); assert.equal(result.planDrafts.length, 0);
    assert.notEqual(result.captureKey, resolve().captureKey);
    const discarded = clearPlanAnalysisDraftRegistry(clean, true);
    assert.equal(discarded.discardRevision, 1); assert.equal(discarded.entries.length, 0);
    assert.equal(publishPlanAnalysisDraft(clean, editorID, null).entries.length, 0);
  });
  test('active save protects user transitions but permits its exact source acknowledgement', () => {
    const saving = registryWith({ status: 'busy', valid: false, busy: true, writePending: true });
    assert.equal(hasProtectedSourceWrite(owners, saving), true);
    assert.equal(hasProtectedSourceWrite(owners, saving, project), false);
    for (const context of [{ ...project, projectXml: '<changed/>' }, { ...project, coreLayerContext: null },
      { ...project, selectedAsset: { ...project.selectedAsset, path: '/different.plan.hdf' } },
      { ...project, selectedAsset: { ...project.selectedAsset, revision: 'different' } }])
      assert.equal(hasProtectedSourceWrite(owners, saving, context), true);
  });
  test('unmounted active/unknown writes remain explicit uncertain blockers', () => {
    const saving = registryWith({ status: 'busy', valid: false, busy: true, writePending: true });
    const unmounted = publishPlanAnalysisDraft(saving, editorID, null);
    assert.equal(unmounted.entries[0].status, 'uncertain');
    assert.equal(unmounted.entries[0].planDraft, undefined);
    assert.equal(hasProtectedSourceWrite(new Set(), unmounted, project), true);
    assert.equal(resolve({ registry: unmounted }, new Set()).ok, false);
  });
  test('geometry and R2R pending/accepted export owners cannot be discarded', () => {
    for (const owner of ['geometry.mesh-export:uuid', 'tools.r2r-export:pending-uuid', 'tools.r2r-export:uuid', 'map.write:uuid', 'map.create:uuid', 'map.raster-write:uuid'])
      assert.equal(hasProtectedSourceWrite(new Set([owner]), emptyPlanAnalysisDraftRegistry()), true);
    assert.equal(hasProtectedSourceWrite(new Set(['tools.r2r-form:uuid']), emptyPlanAnalysisDraftRegistry()), false);
  });
  test('root discard consumes only its exact old-render transition', () => {
    let token = { revision: 4, sourceOwner: 'A', targetOwner: 'B' };
    assert.equal(matchesPlanDiscardTransition(token, 4, 'A', 'B'), true);
    for (const args of [[3, 'A', 'B'], [4, 'B', 'A'], [4, 'A', 'C']])
      assert.equal(matchesPlanDiscardTransition(token, ...args), false);
    // The editor consumes this token before the stale passive effect can recreate pending.
    if (matchesPlanDiscardTransition(token, 4, 'A', 'B')) token = null;
    assert.equal(matchesPlanDiscardTransition(token, 4, 'A', 'B'), false);
  });
  test('a resuming uncertain save keeps navigation blocked and only exact acknowledgement passes', () => {
    const recovering = registryWith({ status: 'uncertain', valid: false, busy: true, writePending: false,
      saveOperationID: 'accepted-save-1', recoveryAttemptID: 'resume-attempt-1' });
    const acknowledgement = { editorID, ownerContextID, operationID: 'accepted-save-1', attemptID: 'resume-attempt-1' };
    assert.equal(hasProtectedSourceWrite(owners, recovering), true);
    assert.equal(hasProtectedSourceWrite(owners, recovering, project), true);
    assert.equal(hasProtectedSourceWrite(owners, recovering, project, acknowledgement), false);
    assert.equal(matchesPlanSaveRecoveryAcknowledgement(recovering.entries[0], project, acknowledgement), true);
    for (const key of ['editorID', 'ownerContextID', 'operationID', 'attemptID'])
      assert.equal(hasProtectedSourceWrite(owners, recovering, project, { ...acknowledgement, [key]: 'wrong' }), true);
    const idle = registryWith({ status: 'uncertain', valid: false, busy: false,
      saveOperationID: 'accepted-save-1', recoveryAttemptID: 'resume-attempt-1' });
    assert.equal(hasProtectedSourceWrite(owners, idle, project, acknowledgement), true);
  });
  test('matching recovery cannot bypass another active export or uncertain editor', () => {
    const recovering = registryWith({ status: 'uncertain', valid: false, busy: true,
      saveOperationID: 'op', recoveryAttemptID: 'attempt' });
    const acknowledgement = { editorID, ownerContextID, operationID: 'op', attemptID: 'attempt' };
    assert.equal(hasProtectedSourceWrite(new Set([...owners, 'tools.r2r-export:other']), recovering, project, acknowledgement), true);
    const other = publishPlanAnalysisDraft(recovering, 'other', { ...publication, editorID: 'other', status: 'uncertain', valid: false });
    assert.equal(hasProtectedSourceWrite(owners, other, project, acknowledgement), true);
  });
  test('save recovery pins the original gateway/source and refuses a different HTTP task id', () => {
    const original = structuredClone(project);
    let captured = capturePlanSaveRecovery(ownerContextID, original, backend, 9);
    original.projectXml = '<different/>'; original.selectedAsset.path = '/different.hdf';
    assert.equal(captured.backend, backend); assert.equal(captured.backendRevision, 9);
    assert.equal(captured.source.projectXml, project.projectXml); assert.equal(captured.asset.path, project.selectedAsset.path);
    captured = trackPlanSaveOperation(captured, 'op-1');
    assert.equal(trackPlanSaveOperation(captured, 'op-1'), captured);
    assert.throws(() => trackPlanSaveOperation(captured, 'op-2'));
  });
  const asyncTest = async (name, run) => { await run(); passed++; console.log(`PASS ${name}`); };
  await asyncTest('resume follows one accepted task without posting another model.save', async () => {
    const calls = [];
    const gateway = { supports: () => true, request: async () => { throw new Error('Must not POST'); },
      resumeOperation: async (id, options) => { calls.push({ id, options }); return { revision: 'saved', fields: { raw: '0.3000' } }; } };
    const captured = trackPlanSaveOperation(capturePlanSaveRecovery(ownerContextID, project, gateway), 'http-save-1');
    const controller = new AbortController();
    const result = await resumeCapturedPlanSave(captured, { signal: controller.signal });
    assert.equal(result.fields.raw, '0.3000'); assert.equal(calls.length, 1);
    assert.equal(calls[0].id, 'http-save-1'); assert.equal(calls[0].options.signal, controller.signal);
    assert.equal(await resumeCapturedPlanSave({ ...captured, receipt: result }), result);
    assert.equal(calls.length, 1, 'An already verified terminal snapshot can be adopted without another network request.');
  });
  await asyncTest('unknown acceptance without id and unsupported original gateway never re-POST', async () => {
    const captured = capturePlanSaveRecovery(ownerContextID, project, backend);
    await assert.rejects(resumeCapturedPlanSave(captured), /任务编号/);
    await assert.rejects(resumeCapturedPlanSave(trackPlanSaveOperation(captured, 'http-save-2')), /原服务连接/);
  });
  await asyncTest('only explicit terminal confirmation releases a failed or canceled save', async () => {
    const terminal = Object.assign(new Error('cancelled'), { terminalConfirmed: true });
    const gateway = { ...backend, resumeOperation: async () => { throw terminal; } };
    const captured = trackPlanSaveOperation(capturePlanSaveRecovery(ownerContextID, project, gateway), 'op');
    await assert.rejects(resumeCapturedPlanSave(captured), error => error === terminal);
    assert.equal(isConfirmedPlanSaveTerminal(terminal), true);
    for (const error of [new Error('network'), { terminalConfirmed: false }, { operationId: 'op' }, null])
      assert.equal(isConfirmedPlanSaveTerminal(error), false);
  });
  console.log(`Plan analysis draft bridge: ${passed} pure checks passed (no Host/UI/OS clipboard acceptance claimed).`);
} finally { await rm(temporary, { recursive: true, force: true }); }
