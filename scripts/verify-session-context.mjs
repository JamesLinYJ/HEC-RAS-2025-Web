import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { build } from 'vite';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const temporary = await mkdtemp(path.join(tmpdir(), 'hecras-session-context-'));
try {
  await build({ configFile: false, root, logLevel: 'silent', build: {
    lib: { entry: {
      context: path.join(root, 'frontend/src/shared/sessionReferences.ts'),
      gateway: path.join(root, 'frontend/src/platform/SessionBackend.ts'),
      mapMutation: path.join(root, 'frontend/src/shared/mapVisualizationMutation.ts'),
      projectMutation: path.join(root, 'frontend/src/shared/projectContextMutation.ts'),
      externalConnection: path.join(root, 'frontend/src/features/project/ExternalConnectionWorkflow.ts'),
    }, formats: ['es'], fileName: (_format, name) => `${name}.mjs` },
    outDir: temporary, emptyOutDir: true, minify: false, sourcemap: false,
  } });
  const { withSessionReferenceContext, mergeSessionReferenceContext } = await import(pathToFileURL(path.join(temporary, 'context.mjs')).href);
  const { sessionBackend } = await import(pathToFileURL(path.join(temporary, 'gateway.mjs')).href);
  const { captureMapVisualizationLease, matchesMapVisualizationReceipt } = await import(pathToFileURL(path.join(temporary, 'mapMutation.mjs')).href);
  const { captureProjectContextLease, matchesProjectContextCommit } = await import(pathToFileURL(path.join(temporary, 'projectMutation.mjs')).href);
  const { ExternalConnectionCommittedContextError, inspectAndCommitExternalConnection } =
    await import(pathToFileURL(path.join(temporary, 'externalConnection.mjs')).href);
  const reference = { referenceID: 'retained-source', projectSessionID: 'session-a', projectPath: '/project.ras',
    kind: 'terrain', sourcePath: '/external.h5', canonicalSourcePath: '/external.h5',
    sourceRevision: 'b'.repeat(64), name: '外部地形', sessionOnly: true };
  const source = { projectSessionID: 'session-a', projectPath: '/project.ras', projectRevision: 'a'.repeat(64), projectXml: '<Project/>',
    selectedAsset: { path: '/external.h5', kind: 'Terrain', name: '外部地形', revision: reference.sourceRevision } };
  const core = { projectSessionID: source.projectSessionID, projectPath: source.projectPath, projectRevision: source.projectRevision,
    references: [], sessionHdfReferences: [reference], identityToken: 'c'.repeat(64) };
  const active = withSessionReferenceContext(source, core);
  assert.equal(active.selectedAsset.locator.coreLayerContext, core, 'The selected asset and session must share one acknowledged capture.');
  assert.equal(mergeSessionReferenceContext({ ...source, selectedAsset: null }, active).coreLayerContext, core,
    'Changing selection must not lose external references.');
  assert.throws(() => withSessionReferenceContext(source, { ...core, projectSessionID: 'another-session' }),
    'A capture from another project session must be rejected.');
  assert.throws(() => mergeSessionReferenceContext({ ...active, selectedAsset: { ...active.selectedAsset, revision: 'd'.repeat(64) } }, active, true),
    'An external HDF save cannot reuse its old source SHA.');
  const rebased = { ...core, identityToken: 'e'.repeat(64), sessionHdfReferences: [{ ...reference, sourceRevision: 'd'.repeat(64) }] };
  const saved = { ...active, selectedAsset: { ...active.selectedAsset, revision: 'd'.repeat(64), locator: { coreLayerContext: rebased } } };
  assert.equal(mergeSessionReferenceContext(saved, active, true).coreLayerContext, rebased,
    'A rebased Host receipt in the selected locator must replace an inherited old global capture.');
  const projectSaved = { ...active, projectRevision: 'f'.repeat(64), projectXml: '<Project saved="true"/>' };
  assert.throws(() => mergeSessionReferenceContext(projectSaved, active, true),
    'A changed project SHA cannot retain the previous project capability.');
  const projectCore = { ...core, projectRevision: projectSaved.projectRevision, identityToken: '1'.repeat(64) };
  assert.equal(mergeSessionReferenceContext(withSessionReferenceContext(projectSaved, projectCore), active, true).coreLayerContext,
    projectCore, 'A project-save receipt must replace both the global and selected-locator capability.');
  const calls = [];
  const transport = { supports: () => true, request: async (method, parameters, options) => { calls.push({ method, parameters, options }); return parameters; } };
  const gateway = sessionBackend(transport, active);
  const parameters = { map: { projectPath: source.projectPath }, model: { projectPath: source.projectPath }, source: { projectPath: source.projectPath } };
  const options = { signal: new AbortController().signal };
  await gateway.request('map.layer.inspect', parameters, options);
  assert.equal(calls.at(-1).parameters.map.coreLayerContext, core);
  assert.equal(calls.at(-1).parameters.model.coreLayerContext, core);
  assert.equal(calls.at(-1).parameters.source.coreLayerContext, undefined, 'Arbitrary source payloads must not be traversed.');
  assert.equal(calls.at(-1).options, options);
  assert.equal(parameters.map.coreLayerContext, undefined, 'Request augmentation must not mutate the captured caller DTO.');
  await gateway.request('model.load', { projectPath: '/other.ras' });
  assert.equal(calls.at(-1).parameters.coreLayerContext, undefined, 'A different project cannot inherit this session.');
  await gateway.request('project.open', { projectPath: source.projectPath });
  assert.equal(calls.at(-1).parameters.coreLayerContext, undefined, 'Opening the project starts a new session.');
  const explicit = { identityToken: 'old-inspection' };
  await gateway.request('model.load', { projectPath: source.projectPath, coreLayerContext: explicit });
  assert.equal(calls.at(-1).parameters.coreLayerContext, explicit, 'A captured request must never be silently upgraded to another identity.');
  const nextGateway = sessionBackend(transport, withSessionReferenceContext(saved, rebased));
  await gateway.request('model.load', { projectPath: source.projectPath });
  assert.equal(calls.at(-1).parameters.coreLayerContext, core, 'A late request retains its original immutable capability.');
  await nextGateway.request('model.load', { projectPath: source.projectPath });
  assert.equal(calls.at(-1).parameters.coreLayerContext, rebased);
  const target = { mapID: 'map-one', mapIndex: 0, name: '计算地图', mapType: 'Plan', visualizationRevision: 'a'.repeat(64) };
  const toolDraft = new Set(['tools.geoprocessing']);
  const lease = captureMapVisualizationLease(active, active, target, 'tools.geoprocessing', toolDraft);
  assert.ok(lease, 'The originating tool may keep its parameters while reserving the map.');
  assert.equal(captureMapVisualizationLease(active, active, target, 'tools.geoprocessing', new Set([...toolDraft, 'geometry:unsaved'])), null,
    'An unrelated model draft must prevent reserving the map.');
  assert.equal(captureMapVisualizationLease(active, { ...active, projectRevision: 'f'.repeat(64) }, target, 'tools.geoprocessing', toolDraft), null,
    'A stale project revision must not obtain a lease.');
  assert.equal(captureMapVisualizationLease(active, { ...active, projectSessionID: 'other-session' }, target, 'tools.geoprocessing', toolDraft), null,
    'A different session must not obtain a lease for this project.');
  assert.equal(captureMapVisualizationLease(active, active, { ...target, visualizationRevision: null }, 'tools.geoprocessing', toolDraft), null,
    'An unsaved map has no visualization transaction to reserve.');
  const mapReceipt = { mapIndex: 0, mapName: target.name, mapType: target.mapType,
    previousVisualizationRevision: target.visualizationRevision.toUpperCase(), visualizationRevision: 'b'.repeat(64) };
  assert.equal(matchesMapVisualizationReceipt(lease, active, active, target, mapReceipt), true,
    'The exact acknowledged map receipt is accepted without losing the source context.');
  assert.equal(matchesMapVisualizationReceipt(lease, active, active, target, { ...mapReceipt, mapIndex: 1 }), false,
    'A receipt for another map cannot advance this map capture.');
  assert.equal(matchesMapVisualizationReceipt(lease, active, active, target, { ...mapReceipt, previousVisualizationRevision: 'c'.repeat(64) }), false,
    'A receipt for a later or earlier visualization revision cannot be adopted.');
  assert.equal(matchesMapVisualizationReceipt(lease, active, active, target, { ...mapReceipt, visualizationRevision: 'unknown' }), false,
    'A result without a real visualization revision cannot advance saved-map state.');
  assert.equal(matchesMapVisualizationReceipt(lease, active, active, { ...target, name: '另一张地图' }, mapReceipt), false,
    'The captured map may not be silently replaced by name.');
  assert.equal(matchesMapVisualizationReceipt(lease, saved, active, target, mapReceipt), false,
    'A late receipt cannot overwrite a context whose selected model has since been saved.');
  assert.equal(matchesMapVisualizationReceipt(lease, active, { ...active, selectedAsset: { ...active.selectedAsset,
    locator: { ...active.selectedAsset.locator, changedOwner: true } } }, target, mapReceipt), false,
    'Even a same-path selection cannot substitute another source locator after reservation.');
  const changedTarget = { ...target, mapID: 'another-map' };
  assert.equal(matchesMapVisualizationReceipt(lease, active, active, changedTarget, mapReceipt), false,
    'Map IDs remain distinct even when names and indices match.');
  const projectLease = captureProjectContextLease(active, active, 'bc-draft', new Set(['bc-draft']));
  assert.ok(projectLease, 'A project-only connection change may retain its originating boundary draft.');
  assert.equal(captureProjectContextLease(active, active, 'bc-draft', new Set(['bc-draft', 'other-model'])), null,
    'A project connection cannot bypass another editor draft.');
  assert.equal(captureProjectContextLease(active, { ...active, projectRevision: '9'.repeat(64) }, 'bc-draft', new Set()), null,
    'A stale project cannot reserve a context transition.');
  const connectionProject = { ...active, projectRevision: '2'.repeat(64), projectXml: '<Project><Connection/></Project>' };
  const connectionCore = { ...core, projectRevision: connectionProject.projectRevision, identityToken: '3'.repeat(64) };
  const connectionContext = withSessionReferenceContext(connectionProject, connectionCore);
  assert.equal(matchesProjectContextCommit(projectLease, active, active, connectionContext), true,
    'An acknowledged project rebase preserves the selected model source SHA.');
  assert.equal(matchesProjectContextCommit(projectLease, active, active, { ...connectionContext,
    selectedAsset: { ...connectionContext.selectedAsset, revision: '4'.repeat(64) } }), false,
    'A project-only receipt cannot silently save or replace the model HDF.');
  assert.equal(matchesProjectContextCommit(projectLease, active, active, { ...connectionContext,
    selectedAsset: { ...connectionContext.selectedAsset, path: '/another-model.h5' } }), false,
    'A project connection cannot substitute another selected asset.');
  assert.equal(matchesProjectContextCommit(projectLease, active, active, { ...connectionContext, projectSessionID: 'other-session' }), false,
    'A connection receipt cannot cross project sessions.');
  assert.equal(matchesProjectContextCommit(projectLease, connectionContext, active, connectionContext), false,
    'A late receipt cannot overwrite a context that has already changed.');
  assert.equal(matchesProjectContextCommit(projectLease, active, active, { ...connectionContext, projectPath: '/other.ras' }), false,
    'A project connection receipt must name the reserved project file.');
  assert.equal(mergeSessionReferenceContext(connectionContext, active, true).coreLayerContext, connectionCore,
    'The acknowledged project context must replace the previous registry capability.');
  assert.equal(connectionContext.selectedAsset.revision, active.selectedAsset.revision,
    'Rebasing a project connection leaves the source model untouched.');

  const projectXmlSHA = (xml) => createHash('sha256').update(xml, 'utf8').digest('hex').toUpperCase();
  const externalProjectXml = '<Project><ExternalConnections /></Project>';
  const externalProjectRevision = 'a'.repeat(64);
  const externalSessionID = '3ce87651-3f74-4c44-8e2b-2f8cf8ccf782';
  const externalProjectPath = '/fixture/ExternalConnection.ras';
  const externalAsset = {
    path: '/fixture/Boundary Conditions.hdf', kind: 'Boundary Conditions', name: 'Boundary Conditions', revision: 'b'.repeat(64),
    locator: { nodeID: 'bc-node', coreLayerContext: null },
  };
  const externalCore = {
    projectSessionID: externalSessionID, projectPath: externalProjectPath, projectRevision: externalProjectRevision,
    references: [], sessionHdfReferences: [], identityToken: 'c'.repeat(64),
  };
  externalAsset.locator.coreLayerContext = externalCore;
  const externalSource = {
    projectSessionID: externalSessionID, projectPath: externalProjectPath, projectXml: externalProjectXml,
    projectRevision: externalProjectRevision, selectedAsset: externalAsset, coreLayerContext: externalCore,
  };
  const externalValues = {
    action: 'add', kind: 'file', name: 'Rainfall Source', path: '/fixture/rainfall.hdf', description: 'fixture source',
  };
  const sourceReferencesSHA256 = 'd'.repeat(64);
  const externalInspection = {
    ...externalValues,
    canCommit: true,
    projectPath: externalProjectPath,
    projectRevision: externalProjectRevision,
    projectXmlSHA: projectXmlSHA(externalProjectXml),
    projectSessionID: externalSessionID,
    canonicalPath: '/fixture/rainfall.hdf',
    sourceSHA256: 'e'.repeat(64),
    sourceExists: true,
    sourceFileCount: 1,
    sourceReferencesSHA256,
    normalizedName: 'Rainfall Source',
  };
  const committedXml = '<Project><ExternalConnections><File Name="Rainfall Source" /></ExternalConnections></Project>';
  const committedRevision = 'f'.repeat(64);
  const rebasedCore = {
    ...externalCore, projectRevision: committedRevision, identityToken: '1'.repeat(64),
  };
  const makeExternalReceipt = (coreLayerContext = rebasedCore) => ({
    success: true,
    action: 'add',
    kind: 'file',
    project: {
      success: true,
      projectPath: externalProjectPath,
      projectXml: committedXml,
      revision: committedRevision,
      projectXmlSHA: projectXmlSHA(committedXml),
      projectSessionID: externalSessionID,
      coreLayerContext,
    },
    previousProjectRevision: externalProjectRevision,
    before: null,
    after: {
      name: 'Rainfall Source', type: 'File', path: '/fixture/rainfall.hdf',
      description: 'fixture source', connectionSHA256: '2'.repeat(64),
      sourcePath: '/fixture/rainfall.hdf', sourceSHA256: externalInspection.sourceSHA256,
      sourceExists: true,
    },
    sourceCapture: {
      path: '/fixture/rainfall.hdf', sha256: externalInspection.sourceSHA256, exists: true, fileCount: 1,
    },
    previousSourceReferencesSHA256: sourceReferencesSHA256,
    sourceReferencesSHA256: '3'.repeat(64),
  });
  const externalMethods = () => ({ supports: () => true });
  let addedChecks = 0;
  const verify = (condition, description) => {
    assert.ok(condition, description);
    addedChecks += 1;
  };
  const verifyRejects = async (promise, predicate, description) => {
    await assert.rejects(promise, predicate, description);
    addedChecks += 1;
  };

  const postReceiptController = new AbortController();
  let postReceiptSourceCurrent = true;
  const validReceipt = makeExternalReceipt();
  const validCalls = [];
  const validGateway = {
    ...externalMethods(),
    request: async (method, parameters, options) => {
      validCalls.push({ method, parameters, options });
      if (method === 'project.connection.inspect') return externalInspection;
      if (method === 'project.connection.commit') {
        postReceiptSourceCurrent = false;
        postReceiptController.abort();
        return validReceipt;
      }
      throw new Error(`Unexpected operation ${method}`);
    },
  };
  const validCommit = await inspectAndCommitExternalConnection(validGateway, externalSource, externalValues,
    postReceiptController.signal, () => postReceiptSourceCurrent);
  verify(validCalls.map(({ method }) => method).join(',') === 'project.connection.inspect,project.connection.commit',
    'The valid mock uses the inspect-then-commit contract exactly once.');
  verify(validCommit.receipt === validReceipt,
    'A valid acknowledged commit receipt is returned even when the signal and current-owner predicate change after POST.');
  verify(postReceiptController.signal.aborted && !postReceiptSourceCurrent,
    'The post-commit test changes both request cancellation and source currentness before the receipt is consumed.');
  verify(validCommit.committed.coreLayerContext === rebasedCore,
    'The committed project context adopts the validated rebased core capability.');
  verify(validCommit.committed.selectedAsset.locator.coreLayerContext === rebasedCore,
    'The selected asset locator adopts the same validated rebased core capability.');
  verify(validCommit.source === externalSource && validCommit.source.coreLayerContext === externalCore &&
    validCommit.source.selectedAsset.locator.coreLayerContext === externalCore,
  'Returning the new context does not mutate the captured source or its locator.');

  const missingCoreReceipt = makeExternalReceipt(null);
  const missingCoreGateway = {
    ...externalMethods(),
    request: async (method) => method === 'project.connection.inspect' ? externalInspection : missingCoreReceipt,
  };
  await verifyRejects(inspectAndCommitExternalConnection(missingCoreGateway, externalSource, externalValues),
    (error) => error instanceof ExternalConnectionCommittedContextError && error.receipt === missingCoreReceipt,
    'A committed receipt missing the previously captured core capability is surfaced as a committed-context error carrying that receipt.');

  const staleReturnedCore = { ...rebasedCore, projectRevision: externalProjectRevision };
  const staleCoreReceipt = makeExternalReceipt(staleReturnedCore);
  const staleReturnedCoreGateway = {
    ...externalMethods(),
    request: async (method) => method === 'project.connection.inspect' ? externalInspection : staleCoreReceipt,
  };
  await verifyRejects(inspectAndCommitExternalConnection(staleReturnedCoreGateway, externalSource, externalValues),
    (error) => error instanceof ExternalConnectionCommittedContextError && error.receipt === staleCoreReceipt,
    'A committed receipt with a core capability bound to the old project revision is surfaced with the receipt for reconciliation.');

  const staleSourceCore = { ...externalCore, projectRevision: '9'.repeat(64) };
  const staleSource = {
    ...externalSource,
    coreLayerContext: staleSourceCore,
    selectedAsset: { ...externalAsset, locator: { ...externalAsset.locator, coreLayerContext: staleSourceCore } },
  };
  const staleSourceCalls = [];
  const staleSourceGateway = {
    ...externalMethods(),
    request: async (method, parameters) => { staleSourceCalls.push({ method, parameters }); return externalInspection; },
  };
  await verifyRejects(inspectAndCommitExternalConnection(staleSourceGateway, staleSource, externalValues),
    /外部来源身份与工程修订不匹配/,
    'A source core capture bound to an old project revision is rejected before any project operation is posted.');
  verify(staleSourceCalls.length === 0,
    'A stale source core capture produces no inspect or commit POST.');

  console.log(JSON.stringify({ state: 'passed', checks: 41 + addedChecks,
    scope: 'session-reference-project-context-and-external-connection-ownership' }));
} finally { await rm(temporary, { recursive: true, force: true }); }
