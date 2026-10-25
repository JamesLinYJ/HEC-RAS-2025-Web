import assert from 'node:assert/strict';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { build } from 'vite';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const temporary = await mkdtemp(path.join(tmpdir(), 'hecras-r2r-matrix-'));
let checks = 0;
const check = fn => { fn(); checks++; };
try {
  const entry = path.join(temporary, 'entry.ts');
  await writeFile(entry, `export * from ${JSON.stringify(path.join(root, 'frontend/src/features/tools/plan-analysis/r2rMatrixWorkflow.ts'))};\n`);
  await build({ configFile: false, root, logLevel: 'silent', build: { lib: { entry, formats: ['es'], fileName: () => 'matrix.mjs' },
    outDir: path.join(temporary, 'out'), emptyOutDir: true, minify: false, sourcemap: false } });
  const w = await import(pathToFileURL(path.join(temporary, 'out/matrix.mjs')).href);
  // Protocol fixtures model the Host DTOs. These checks do not emit HDF files or run the solver.
  const projectSHA = 'a'.repeat(64), planSHA = 'b'.repeat(64), inspectionID = 'c'.repeat(64);
  const context = { projectSessionID: 'session-a', projectPath: '/fixture/project.ras', projectXml: '<Project />',
    projectRevision: projectSHA, selectedAsset: null, coreLayerContext: null };
  const planPath = '/fixture/Plans/Plan CPU.h5';
  const draft = { assetPath: planPath, revision: planSHA, fields: { CoreCount: ' 4 ' }, sourceState: 'retained-source-state',
    editorID: 'editor-a', ownerContextID: 'model:plan-a', planTimeWindowEdits: [
      { id: '1', fieldID: 'TimeWindowMode', value: 'Plan' }, { id: '2', fieldID: 'InternalComputeDurationSeconds', value: '6' },
    ] };
  const capturing = w.captureR2RSource(context, 'draft-1', [draft]);
  draft.fields.CoreCount = '8'; context.projectXml = '<Changed />';
  const source = await capturing;
  check(() => assert.equal(source.capture.projectXml, '<Project />'));
  check(() => assert.equal(source.drafts[0].fields.CoreCount, ' 4 '));
  check(() => assert.match(source.capture.projectXmlFingerprint, /^[0-9A-F]{64}$/));
  const identity = { projectPath: source.capture.projectPath, projectRevision: projectSHA,
    projectXmlFingerprint: source.capture.projectXmlFingerprint };
  const properties = [
    { propertyName: 'SolverType', displayName: 'Processor', valueType: 'SolverType', inputHint: '', isClosedSet: true,
      initialValues: [{ value: 'CPU', label: 'CPU', selected: true, removable: false }, { value: 'GPU', label: 'GPU', selected: true, removable: false }] },
    { propertyName: 'ComputeTimestep', displayName: 'Compute timestep', valueType: 'TimeSpan', inputHint: 'duration', isClosedSet: false,
      initialValues: [{ value: '00:00:00.1000000', label: '0.1s', selected: true, removable: true }] },
  ];
  const rawOptions = { ...identity, plans: [{ planPath, revision: planSHA, name: 'Plan CPU' }], selectedPlanPath: planPath,
    outputDirectory: '/output', properties, rules: [{ name: 'Non-positive timestep', reason: 'The compute timestep must be greater than zero.', enabled: true }] };
  const options = w.parseR2ROptions(rawOptions, source, planPath);
  check(() => assert.deepEqual(options.properties.map(item => item.propertyName), ['SolverType', 'ComputeTimestep']));
  check(() => assert.deepEqual(options.properties[0].initialValues.map(item => item.value), ['CPU', 'GPU']));
  check(() => assert.equal(options.properties[1].initialValues[0].value, '00:00:00.1000000'));
  for (const patch of [
    { projectPath: '/other/project.ras' }, { projectRevision: planSHA }, { projectXmlFingerprint: planSHA },
    { plans: [rawOptions.plans[0], rawOptions.plans[0]] }, { selectedPlanPath: '/other/plan.h5' },
    { properties: [properties[0], properties[0]] }, { rules: [rawOptions.rules[0], rawOptions.rules[0]] },
    { properties: [{ ...properties[0], initialValues: [{ ...properties[0].initialValues[0], removable: true }] }] },
  ]) check(() => assert.throws(() => w.parseR2ROptions({ ...rawOptions, ...patch }, source, planPath)));
  let form = w.initialR2RForm(options);
  check(() => assert.equal(form.overwrite, false));
  check(() => assert.equal(form.writeManifest, true));
  form = w.addR2RAxis(form, properties[1]);
  check(() => assert.throws(() => w.addR2RAxis(form, properties[1])));
  form = { ...form, axes: [{ ...form.axes[0], newValue: ' 0.2s ' }] };
  const extended = w.addR2RValues(form.axes[0], properties[1]);
  check(() => assert.deepEqual(extended.values.map(item => item.value), ['00:00:00.1000000', ' 0.2s ']));
  check(() => assert.throws(() => w.addR2RValues({ ...extended, newValue: ' 0.2s ' }, properties[1])));
  check(() => assert.throws(() => w.addR2RValues({ propertyName: 'SolverType', values: properties[0].initialValues, newValue: 'Other' }, properties[0])));
  const stringAxis = w.addR2RValues({ propertyName: 'StringProperty', values: [], newValue: '  a,b;c  ' }, { ...properties[1], valueType: 'String' });
  check(() => assert.equal(stringAxis.values[0].value, '  a,b;c  '));
  form = { ...form, axes: [extended] };
  const request = w.makeR2RPreviewRequest(source, options, planPath, form);
  check(() => assert.deepEqual(request.axes[0].values, ['00:00:00.1000000', ' 0.2s ']));
  check(() => assert.deepEqual(request.planDraft.planTimeWindowEdits.map(item => item.id), ['1', '2']));
  check(() => assert.equal(request.planDraft.fields.CoreCount, ' 4 '));
  for (const patch of [
    { outputDirectory: 'relative/path' }, { outputDirectory: '' }, { axes: [extended, extended] },
    { axes: [{ ...extended, newValue: 'pending' }] }, { axes: [{ ...extended, values: extended.values.map(value => ({ ...value, selected: false })) }] },
    { enabledRuleNames: ['unsupported-rule'] }, { enabledRuleNames: ['Non-positive timestep', 'Non-positive timestep'] },
    { axes: [{ propertyName: 'SolverType', newValue: '', values: [{ value: 'NOT-IN-ENUM', label: 'invalid', selected: true, removable: false }] }] },
    { axes: [{ ...extended, values: Array.from({ length: 2049 }, (_, index) => ({ value: `${index}s`, label: `${index}s`, selected: true, removable: true })) }] },
  ]) check(() => assert.throws(() => w.makeR2RPreviewRequest(source, options, planPath, { ...form, ...patch })));
  check(() => assert.throws(() => w.makeR2RPreviewRequest({ ...source, drafts: [{ ...source.drafts[0], revision: projectSHA }] }, options, planPath, form)));
  const orderedForm = w.addR2RAxis(form, properties[0]);
  check(() => assert.deepEqual(w.makeR2RPreviewRequest(source, options, planPath, orderedForm).axes.map(axis => axis.propertyName), ['ComputeTimestep', 'SolverType']));
  const combinations = ['0.1s', '0.2s'].map((value, index) => ({ index: index + 1, label: value, filename: `Plan_Dt${index + 1}.r2r.h5`,
    isExcluded: false, isCollapsed: false, exclusionReason: '', assignments: [
      { propertyName: 'ComputeTimestep', displayName: 'Compute timestep', value, isRelevant: true, irrelevantReason: null },
    ] }));
  const rawPreview = { ...identity, planPath, planRevision: planSHA, inspectionID, outputDirectory: '/output', writeCount: 2,
    summary: '2 combinations · 2 files', hint: '', combinations };
  const preview = w.parseR2RPreview(rawPreview, source, request);
  check(() => assert.deepEqual(preview.combinations.map(row => row.filename), combinations.map(row => row.filename)));
  for (const patch of [
    { projectPath: '/other/project.ras' }, { planPath: '/other/plan.h5' }, { planRevision: projectSHA },
    { inspectionID: 'not-sha' }, { writeCount: 1 }, { outputDirectory: '/elsewhere' },
    { combinations: [{ ...combinations[0], index: 2 }, combinations[1]] },
    { combinations: [{ ...combinations[0], filename: '../bad.r2r.h5' }, combinations[1]] },
    { combinations: [combinations[0], { ...combinations[1], filename: combinations[0].filename }] },
    { combinations: [{ ...combinations[0], assignments: [{ ...combinations[0].assignments[0], propertyName: 'SolverType' }] }, combinations[1]] },
    { combinations: [{ ...combinations[0], isCollapsed: true }, combinations[1]] },
  ]) check(() => assert.throws(() => w.parseR2RPreview({ ...rawPreview, ...patch }, source, request)));
  const inspected = { source, request, preview, formKey: w.r2rFormKey(form) };
  check(() => assert.equal(w.isR2RInspectionCurrent(inspected, 'session-a', 'draft-1', planPath, form), true));
  for (const [session, key, selected, changedForm] of [
    ['session-b', 'draft-1', planPath, form], ['session-a', 'draft-2', planPath, form], ['session-a', 'draft-1', '/other/plan.h5', form],
    ['session-a', 'draft-1', planPath, { ...form, overwrite: true }], ['session-a', 'draft-1', planPath, { ...form, writeManifest: false }],
    ['session-a', 'draft-1', planPath, { ...form, axes: [{ ...extended, newValue: 'unfinished' }] }],
  ]) check(() => assert.equal(w.isR2RInspectionCurrent(inspected, session, key, selected, changedForm), false));
  const files = combinations.map(row => ({ filename: row.filename, status: 'Written', filePath: `/output/${row.filename}`, fileSizeBytes: 1200, message: null }));
  const rawReceipt = { ...identity, planPath, planRevision: planSHA, inspectionID, outputDirectory: '/output', state: 'completed',
    written: 2, skipped: 0, failed: 0, excluded: 0, wasCancelled: false, wasAborted: false, abortReason: null,
    manifestPath: '/output/Plan_matrix.csv', sourcesStillCurrent: true, files };
  check(() => assert.equal(w.parseR2REmitReceipt(rawReceipt, inspected).written, 2));
  for (const patch of [
    { projectPath: '/other/project.ras' }, { projectRevision: planSHA }, { projectXmlFingerprint: projectSHA },
    { planPath: '/other/plan.h5' }, { planRevision: projectSHA }, { inspectionID: projectSHA }, { outputDirectory: '/other' },
    { files: [files[0]] }, { files: [{ ...files[0], filename: files[1].filename }, files[1]] },
    { files: [{ ...files[0], filePath: '/other/Plan.r2r.h5' }, files[1]] },
    { files: [{ ...files[0], fileSizeBytes: 0 }, files[1]] },
    { files: [{ ...files[0], status: 'Excluded' }, files[1]] }, { written: 1 }, { skipped: 1 },
    { state: 'cancelled' }, { wasAborted: true }, { manifestPath: '/other/Plan_matrix.csv' },
  ]) check(() => assert.throws(() => w.parseR2REmitReceipt({ ...rawReceipt, ...patch }, inspected)));
  const partial = { ...rawReceipt, state: 'failed', written: 1, failed: 1, wasAborted: true,
    files: [files[0], { ...files[1], status: 'Failed', fileSizeBytes: 0, message: 'prepare failed' }] };
  check(() => assert.equal(w.parseR2REmitReceipt(partial, inspected).written, 1));
  const cancelled = { ...rawReceipt, state: 'cancelled', written: 1, wasCancelled: true,
    files: [files[0], { ...files[1], status: 'Cancelled', filePath: null, fileSizeBytes: 0, message: 'Cancelled' }] };
  check(() => assert.equal(w.parseR2REmitReceipt(cancelled, inspected).files[0].status, 'Written'));
  check(() => assert.equal(w.parseR2REmitReceipt({ ...rawReceipt, sourcesStillCurrent: false }, inspected).sourcesStillCurrent, false));
  check(() => assert.equal(w.r2rConfirmedTerminal({ terminalConfirmed: true, operationId: 'original-id' }), true));
  check(() => assert.equal(w.r2rConfirmedTerminal({ terminalConfirmed: false, operationId: 'original-id' }), false));
  check(() => assert.equal(w.r2rConfirmedTerminal(new DOMException('cancel requested', 'AbortError')), false));
  check(() => assert.equal(w.parseR2RProgress({ fraction: Infinity, values: { filename: 'a.r2r.h5', phase: 'writing' } }).fraction, undefined));
  check(() => assert.equal(w.sameR2RPath('/Case/Plan.h5', '/case/Plan.h5'), false));
  check(() => assert.equal(w.sameR2RPath('C:\\Project\\Plan.h5', 'c:/project/plan.h5'), true));
  console.log(`R2R matrix protocol: ${checks} checks passed (no Host emission, no solver).`);
} finally { await rm(temporary, { recursive: true, force: true }); }
