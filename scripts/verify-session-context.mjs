import assert from 'node:assert/strict';
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
    }, formats: ['es'], fileName: (_format, name) => `${name}.mjs` },
    outDir: temporary, emptyOutDir: true, minify: false, sourcemap: false,
  } });
  const { withSessionReferenceContext, mergeSessionReferenceContext } = await import(pathToFileURL(path.join(temporary, 'context.mjs')).href);
  const { sessionBackend } = await import(pathToFileURL(path.join(temporary, 'gateway.mjs')).href);
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
  console.log(JSON.stringify({ state: 'passed', checks: 17, scope: 'session-reference-ownership-and-request-isolation' }));
} finally { await rm(temporary, { recursive: true, force: true }); }
