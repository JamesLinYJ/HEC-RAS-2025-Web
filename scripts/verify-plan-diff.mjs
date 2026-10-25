import assert from 'node:assert/strict';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { createHash, webcrypto } from 'node:crypto';
import { build } from 'vite';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const directory = await mkdtemp(path.join(tmpdir(), 'hecras-plan-diff-'));
let checks = 0;
globalThis.crypto ??= webcrypto;
try {
  await build({ configFile: false, root, logLevel: 'silent', build: {
    lib: { entry: path.join(root, 'frontend/src/features/tools/plan-analysis/planDiffWorkflow.ts'),
      formats: ['es'], fileName: () => 'workflow.mjs' },
    outDir: directory, emptyOutDir: true, minify: false, sourcemap: false,
  } });
  const { capturePlanDiffProject, parsePlanDiffOptions, parsePlanDiffResult, planDiffSourceKey,
    planDiffComparisonKey, PlanDiffRequestGate } = await import(pathToFileURL(path.join(directory, 'workflow.mjs')).href);
  const equal = (actual, expected, explanation) => { assert.deepEqual(actual, expected, explanation); checks++; };
  const rejects = (operation, explanation) => { assert.throws(operation, undefined, explanation); checks++; };
  const hash = value => createHash('sha256').update(value, 'utf8').digest('hex').toUpperCase();
  const context = { projectSessionID: 'session-1', projectPath: '/work/A.ras', projectRevision: hash('project'),
    projectXml: '<Project> 中文\r\n</Project>', selectedAsset: { path: '/work/Plans/A.h5', kind: 'Plan', name: 'A', revision: hash('A') },
    coreLayerContext: { sessionID: 'core-1', sources: [{ path: '/external/A.h5', revision: 'external-revision' }] } };
  const capturePromise = capturePlanDiffProject(context);
  const expectedCore = structuredClone(context.coreLayerContext);
  context.coreLayerContext.sources[0].path = '/external/changed.h5';
  const capture = await capturePromise;
  equal(capture.projectXmlFingerprint, hash(context.projectXml), 'The exact Unicode/CRLF project XML is hashed.');
  equal(capture.coreLayerContext, expectedCore, 'The capture clones Core identity before the asynchronous hash.');
  for (const invalid of [{ projectSessionID: '' }, { projectPath: 'relative.ras' }, { projectPath: '/work/other.xml' },
    { projectRevision: 'not-a-sha' }, { projectXml: null }, { projectXml: '' }]) {
    await assert.rejects(capturePlanDiffProject({ ...context, ...invalid })); checks++;
  }
  const left = { id: 'plan|/work/Plans/A.h5', planPath: '/work/Plans/A.h5', planRevision: hash('A'),
    resultPath: null, resultRevision: null, displayName: '同名计划', headerName: '同名计划', isResult: false };
  const right = { ...left, id: 'plan|/work/Plans/B.h5', planPath: '/work/Plans/B.h5', planRevision: hash('B') };
  const result = { ...left, id: 'result|/work/Plans/A.h5|/work/Results/A.h5|0', resultPath: '/work/Results/A.h5',
    displayName: '      → A  (2026/10/25)', headerName: 'A  (2026/10/25)', isResult: true };
  const identity = { projectPath: capture.projectPath, projectRevision: capture.projectRevision,
    projectXmlFingerprint: capture.projectXmlFingerprint };
  const catalog = { ...identity, options: [left, result, right] };
  equal(parsePlanDiffOptions(catalog, capture).options, catalog.options, 'Original IDs, labels and Host ordering are preserved.');
  equal(parsePlanDiffOptions(catalog, capture).options[1].planPath, left.planPath,
    'A Result source retains its associated Plan path instead of substituting the Result HDF.');
  equal(parsePlanDiffOptions(catalog, capture).options[1].resultRevision, null,
    'The current Host returns a null Result revision; the client must not fabricate one.');
  equal(parsePlanDiffOptions({ ...catalog, options: [{ ...result, id: 'result|A|missing:unavailable|0', resultPath: null }] }, capture)
    .options[0].resultPath, null, 'An explicitly unavailable Result still compares the exact associated Plan supplied by the Host.');
  equal(parsePlanDiffOptions({ ...catalog, options: [] }, capture).options, [], 'An empty authoritative catalog remains empty.');
  for (const changed of [{ projectPath: '/work/B.ras' }, { projectRevision: hash('other') },
    { projectXmlFingerprint: hash('other XML') }]) {
    rejects(() => parsePlanDiffOptions({ ...catalog, ...changed }, capture), 'A mismatched project capture is rejected.');
  }
  for (const badOptions of [[left, left], [{ ...left, planRevision: 'bad' }], [{ ...left, resultPath: '/result.h5' }],
    [{ ...left, resultRevision: undefined }], [{ ...result, resultPath: undefined }], [left, { ...result, planRevision: hash('changed') }],
    [{ ...left, id: '' }], [{ ...left, planPath: 'relative.h5' }]]) {
    rejects(() => parsePlanDiffOptions({ ...catalog, options: badOptions }, capture), 'Ambiguous or incomplete original identities are rejected.');
  }
  const rows = [
    { key: 'Failure', leftValue: '<error: TargetInvocationException>', rightValue: '<null>', differs: true },
    { key: 'OriginalText', leftValue: ' 0001\t中文\n', rightValue: ' 0001\t中文\n', differs: false },
    { key: 'Precise', leftValue: '1,234567', rightValue: '1.234567', differs: true },
  ];
  const response = { ...identity, left, right, rows };
  equal(parsePlanDiffResult(response, capture, left, right).rows, rows,
    'Original error/null markers, whitespace and culture-specific value strings are not coerced.');
  for (const changed of [{ left: right, right: left }, { left: { ...left, id: right.id } },
    { right: { ...right, planRevision: hash('new B') } }, { right: { ...right, planPath: left.planPath } },
    { right: { ...right, isResult: true } }, { right: { ...right, headerName: 'unexpected' } },
    { projectXmlFingerprint: hash('later XML') }]) {
    rejects(() => parsePlanDiffResult({ ...response, ...changed }, capture, left, right),
      'Same display names never authorize another side, source, revision, kind or capture.');
  }
  for (const badRows of [[{ ...rows[0], leftValue: null }], [{ ...rows[0], differs: false }], [rows[0], rows[0]],
    [rows[1], rows[0]], [{ key: 'Missing', leftValue: '', differs: true }]]) {
    rejects(() => parsePlanDiffResult({ ...response, rows: badRows }, capture, left, right),
      'Malformed, duplicate, reordered or inconsistent rows cannot masquerade as a successful comparison.');
  }
  const sourceKey = planDiffSourceKey(context, 3);
  for (const changed of [{ projectSessionID: 'session-2' }, { projectPath: '/work/other.ras' },
    { projectRevision: hash('new project') }, { projectXml: `${context.projectXml}\n` },
    { coreLayerContext: expectedCore }, { selectedAsset: { ...context.selectedAsset, revision: hash('new A') } }]) {
    equal(planDiffSourceKey({ ...context, ...changed }, 3) === sourceKey, false,
      'Project session, source, XML, Core and selected asset revisions retire the old capture.');
  }
  equal(planDiffSourceKey(context, 4) === sourceKey, false, 'Backend capability revision is part of source scope.');
  const comparisonKey = planDiffComparisonKey(sourceKey, 'draft-1', left, right);
  for (const changedKey of [planDiffComparisonKey(sourceKey, 'draft-2', left, right),
    planDiffComparisonKey(sourceKey, 'draft-1', right, left),
    planDiffComparisonKey(sourceKey, 'draft-1', left, { ...right, planRevision: hash('later B') }),
    planDiffComparisonKey(sourceKey, 'draft-1', left, { ...result, resultRevision: hash('result') })]) {
    equal(changedKey === comparisonKey, false, 'Draft capture and both exact option identities retire in-flight results.');
  }
  const gate = new PlanDiffRequestGate();
  const first = gate.begin(comparisonKey);
  equal(gate.accepts(first, comparisonKey), true, 'The active request accepts its unchanged capture.');
  equal(gate.accepts(first, 'other-scope'), false, 'A scope update rejects completion before effect cleanup.');
  const second = gate.begin('other-scope');
  equal(first.controller.signal.aborted, true, 'A new request aborts the previous transport.');
  equal(gate.accepts(first, comparisonKey), false, 'Switching away and back cannot revive an earlier response.');
  gate.retire();
  equal(gate.accepts(second, 'other-scope'), false, 'Explicit cancellation rejects a terminal response.');
  const retried = gate.begin(comparisonKey);
  equal(gate.accepts(retried, comparisonKey), true, 'A fresh retry remains usable after cancellation or preparation failure.');
  console.log(JSON.stringify({ state: 'passed', checks,
    scope: 'PlanDiff capture/identity/row protocol and request retirement; no Host or browser acceptance claimed' }));
} finally { await rm(directory, { recursive: true, force: true }); }
