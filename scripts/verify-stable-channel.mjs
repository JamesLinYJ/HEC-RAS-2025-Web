import assert from 'node:assert/strict';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { build } from 'vite';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const temporary = await mkdtemp(path.join(tmpdir(), 'hecras-stable-channel-'));
try {
  await build({ configFile: false, root, logLevel: 'silent', build: {
    lib: { entry: path.join(root, 'frontend/src/features/tools/stable-channel/stableChannelWorkflow.ts'), formats: ['es'], fileName: () => 'workflow.mjs' },
    outDir: temporary, emptyOutDir: true, minify: false, sourcemap: false,
  } });
  const m = await import(pathToFileURL(path.join(temporary, 'workflow.mjs')).href);
  const A = 'A'.repeat(64), B = 'B'.repeat(64), C = 'C'.repeat(64);
  const context = { projectSessionID: 'session-A', projectPath: '/project/a.ras', projectRevision: A,
    projectXml: '<Project/>', selectedAsset: null, coreLayerContext: { identityToken: 'core-A' } };
  const part = { d: 0.005, angleOfRepose: 45, roughness: .03, specificGravity: 2.65, shieldsParameter: .047, equation: 'Manning' };
  const state = { depth: 2, discharge: 100, slope: .001, leftBankSideSlope: 2, rightBankSideSlope: 2,
    temperature: 70, width: 60, solutionMethod: 'Lane', leftBank: { ...part }, bed: { ...part }, rightBank: { ...part } };
  const source = { sourceID: C, projectPath: context.projectPath, projectRevision: A, designPath: '/project/a.scd.h5', designRevision: B, inMemory: false };
  const backend = { supports: () => true, request: async () => { throw Error('No real Host is used in pure checks'); } };
  const capture = { context, contextKey: m.channelContextKey(context), backend, backendRevision: 1, source };
  let passed = 0;
  const test = async (name, action) => { await action(); passed++; console.log(`PASS ${name}`); };

  await test('all six exact-two combinations accepted; zero/one/three/duplicate targets rejected', () => {
    for (let i = 0; i < m.solveTargets.length; i++) for (let j = i + 1; j < m.solveTargets.length; j++) {
      assert.equal(m.channelComputeInputError(m.channelDraftFromState(state, [m.solveTargets[i], m.solveTargets[j]])), null);
    }
    for (const targets of [[], ['Depth'], ['Depth', 'Width', 'Slope'], ['Depth', 'Depth']])
      assert.match(m.channelComputeInputError(m.channelDraftFromState(state, targets)), /恰好两个/);
  });
  await test('raw draft preserves whitespace, partial exponent, inactive Shields values and independent part equations', () => {
    const draft = m.channelDraftFromState(state, ['Depth', 'Width']);
    draft.discharge = ' 1.000e2 '; draft.leftBank.shieldsParameter = ' 0.0100 '; draft.rightBank.equation = 'Strickler';
    const before = m.channelDraftKey(draft);
    assert.equal(m.channelStateFromDraft(draft).discharge, 100);
    assert.equal(m.channelDraftKey(draft), before);
    draft.slope = '1e';
    assert.throws(() => m.channelStateFromDraft(draft));
    assert.equal(draft.slope, '1e');
    assert.equal(draft.leftBank.shieldsParameter, ' 0.0100 ');
    assert.equal(draft.bed.equation, 'Manning');
  });
  await test('temperature is Fahrenheit 40–120 and conditional required inputs follow source method', () => {
    const draft = m.channelDraftFromState(state, ['Depth', 'Width']);
    draft.temperature = '20'; assert.match(m.channelComputeInputError(draft), /40–120/);
    draft.temperature = '40'; assert.equal(m.channelComputeInputError(draft), null);
    draft.temperature = '120'; assert.equal(m.channelComputeInputError(draft), null);
    draft.leftBank.shieldsParameter = ''; assert.equal(m.channelComputeInputError(draft), null);
    draft.solutionMethod = 'UserDefinedMobility'; assert.match(m.channelComputeInputError(draft), /已知参数/);
  });
  await test('server defaults are preserved without invented inputs; null remains an empty raw value', () => {
    const server = structuredClone(state); server.temperature = 0; server.depth = null;
    const draft = m.channelDraftFromState(server);
    assert.equal(draft.temperature, '0'); assert.equal(draft.depth, ''); assert.deepEqual(draft.solveFor, []);
  });
  await test('readback rejects unsaved project XML and disk revisions before an open can ignore them', () => {
    const readback = { success: true, projectPath: context.projectPath, projectXml: context.projectXml, revision: A };
    m.assertChannelProjectReadback(readback, context);
    assert.throws(() => m.assertChannelProjectReadback(readback, { ...context, projectXml: '<Changed/>' }), /未保存/);
    assert.throws(() => m.assertChannelProjectReadback({ ...readback, revision: B }, context), /修订/);
  });
  await test('open binds request, project SHA, design SHA and exact temporary source identity', () => {
    const response = { requestID: 'open-1', source, state };
    assert.deepEqual(m.parseChannelOpen(response, 'open-1', context, 'memory-1').state, state);
    for (const response2 of [{ ...response, requestID: 'other' }, { ...response, source: { ...source, projectRevision: B } },
      { ...response, source: { ...source, designRevision: 'missing' } }]) assert.throws(() => m.parseChannelOpen(response2, 'open-1', context, 'memory-1'));
    const temporary = { ...source, inMemory: true, sourceID: 'memory-1', designPath: null, designRevision: null };
    m.parseChannelOpen({ ...response, source: temporary }, 'open-1', context, 'memory-1');
    assert.throws(() => m.parseChannelOpen({ ...response, source: temporary }, 'open-1', context, 'different-memory'));
  });
  await test('stale session, XML, Core binding and gateway cannot receive computed state or save snapshot', () => {
    assert.equal(m.channelCaptureCurrent(capture, context, backend, 1), true);
    for (const patch of [{ projectSessionID: 'session-B' }, { projectXml: '<Changed/>' }, { projectRevision: B }, { coreLayerContext: { identityToken: 'core-B' } }])
      assert.equal(m.channelCaptureCurrent(capture, { ...context, ...patch }, backend, 1), false);
    assert.equal(m.channelCaptureCurrent(capture, context, { ...backend }, 1), false);
    assert.equal(m.channelCaptureCurrent(capture, context, backend, 2), false);
  });
  await test('computed responses must match request/source/targets and finite solved outputs', () => {
    const response = { requestID: 'compute-1', sourceID: C, state, solveFor: ['Depth', 'Width'], computed: true, errorCode: null };
    m.parseChannelCompute(response, 'compute-1', source, response.solveFor);
    for (const patch of [{ requestID: 'stale' }, { sourceID: A }, { solveFor: ['Width', 'Slope'] }, { state: { ...state, depth: null } }, { errorCode: 'solverFailed' }])
      assert.throws(() => m.parseChannelCompute({ ...response, ...patch }, 'compute-1', source, response.solveFor));
  });
  await test('merge only updates chosen outputs, preserving raw known and hidden parameters', () => {
    const draft = m.channelDraftFromState(state, ['Depth', 'ParticleSize']);
    draft.discharge = ' 1e2 '; draft.width = '60.000'; draft.bed.shieldsParameter = ' 0.0470 ';
    const solved = { ...state, depth: 7, width: 111, discharge: 88, bed: { ...part, d: 12 } };
    const merged = m.mergeChannelComputed(draft, solved);
    assert.equal(merged.depth, '7'); assert.equal(merged.bed.d, '12'); assert.equal(merged.width, '60.000');
    assert.equal(merged.discharge, ' 1e2 '); assert.equal(merged.bed.shieldsParameter, ' 0.0470 ');
  });
  await test('save receipt must match request, source, path, revision and entire submitted hydraulic state', () => {
    const pending = { capture, request: { requestID: 'save-1', source, state }, draft: m.channelDraftFromState(state), operationID: null };
    const response = { requestID: 'save-1', sourceID: C, designPath: source.designPath, designRevision: A, state };
    m.parseChannelSave(response, pending);
    for (const patch of [{ requestID: 'other' }, { sourceID: A }, { designPath: '/other.scd.h5' }, { designRevision: 'bad' }, { state: { ...state, discharge: 999 } }])
      assert.throws(() => m.parseChannelSave({ ...response, ...patch }, pending));
  });
  await test('unknown save resumes the original gateway HTTP ID and never reposts', async () => {
    let posts = 0, resumes = 0;
    const gateway = { supports: () => true, request: async () => { posts++; }, resumeOperation: async id => { assert.equal(id, 'http-original'); resumes++; return { terminal: true }; } };
    const pending = { capture: { ...capture, backend: gateway }, request: { requestID: 'payload-request', source, state }, draft: m.channelDraftFromState(state), operationID: null };
    await assert.rejects(() => m.resumeChannelSave(pending));
    m.captureChannelOperation(pending, 'http-original');
    assert.throws(() => m.captureChannelOperation(pending, 'http-other'));
    assert.deepEqual(await m.resumeChannelSave(pending), { terminal: true });
    assert.equal(posts, 0); assert.equal(resumes, 1);
    assert.equal(m.channelTerminalConfirmed({ terminalConfirmed: false }), false);
    assert.equal(m.channelTerminalConfirmed(new Error('network failed')), false);
    assert.equal(m.channelTerminalConfirmed({ terminalConfirmed: true }), true);
  });
  console.log(`Stable Channel pure checks: ${passed} passed. This does not constitute Host or browser acceptance.`);
} finally { await rm(temporary, { recursive: true, force: true }); }
