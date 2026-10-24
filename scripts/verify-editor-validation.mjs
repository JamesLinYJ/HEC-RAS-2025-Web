import assert from 'node:assert/strict';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { build } from 'vite';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const temporary = await mkdtemp(path.join(tmpdir(), 'hecras-editor-validation-'));
try {
  await build({
    configFile: false,
    root,
    logLevel: 'silent',
    build: {
      lib: {
        entry: {
          'editor-validation': path.join(root, 'frontend/src/features/editors/SchemaFieldRenderer.tsx'),
          'terrain-input-type': path.join(root, 'frontend/src/features/editors/terrainTopologyTypes.ts'),
          'connection-rebase': path.join(root, 'frontend/src/features/editors/payloadExternalConnectionWorkflow.ts'),
          'model-draft': path.join(root, 'frontend/src/features/editors/modelDraft.ts'),
        },
        formats: ['es'],
        fileName: (_format, entryName) => `${entryName}.mjs`,
      },
      outDir: temporary,
      emptyOutDir: true,
      minify: false,
      sourcemap: false,
    },
  });
  const { validateModelDraft } = await import(pathToFileURL(path.join(temporary, 'editor-validation.mjs')).href);
  const schemas = [
    {
      id: 'FunctionalTerrainInputProperties',
      fields: [{ id: 'Context_MinX', scope: 'FunctionalTerrainInputProperties', kind: 'number', sourceType: 'double', title: 'Min X' }],
    },
    {
      id: 'ChannelModTerrainInputProperties',
      fields: [{ id: 'Context_Name', kind: 'text', sourceType: 'string', title: 'Name', readOnly: true }],
    },
  ];
  const snapshot = { revision: '0'.repeat(64), fields: {}, images: {}, canEdit: true, fieldGroups: {}, availableOperations: [] };
  const channel = 'TerrainInputs/new-channel/ChannelModTerrainInputProperties/Context_MinX';
  const functional = 'TerrainInputs/new-surface/FunctionalTerrainInputProperties/Context_MinX';
  const draft = (fields) => ({ sourceState: null, fields, images: {}, rows: {}, samples: {}, sampleText: {}, changedCells: {} });
  assert.deepEqual(validateModelDraft(draft({ [channel]: 'NaN' }), schemas, snapshot, 'zh'), [],
    'An undefined derived channel extent must not inherit the validation of a different editor with a same-named property.');
  assert.deepEqual(validateModelDraft(draft({ [functional]: 'NaN' }), schemas, snapshot, 'zh').map((error) => error.fieldID), [functional],
    'An invalid editable functional-surface extent must still block saving.');
  assert.deepEqual(validateModelDraft(draft({ [channel]: 'NaN', [functional]: '12.5' }), schemas, snapshot, 'zh'), [],
    'Repeated editor owner prefixes must retain the correct schema binding.');
  assert.deepEqual(validateModelDraft(draft({ [functional]: 'NaN' }), schemas,
    { ...snapshot, readOnlyFields: [functional] }, 'zh'), [],
    'An explicitly read-only Host field must remain outside editable validation.');
  const { isPolygonTerrainInputType } = await import(pathToFileURL(path.join(temporary, 'terrain-input-type.mjs')).href);
  assert.equal(isPolygonTerrainInputType('Polygon'), true, 'The real Host TypeName must enable both the polygon editor and its parent topology transaction.');
  assert.equal(isPolygonTerrainInputType('ChannelModTerrainInput'), false, 'Channel terrain input must not be routed into polygon topology.');
  const { rebaseBoundaryExternalConnection } = await import(pathToFileURL(path.join(temporary, 'connection-rebase.mjs')).href);
  const { createModelDraft, isModelDraftDirty } = await import(pathToFileURL(path.join(temporary, 'model-draft.mjs')).href);
  const line = { id: 'line-a', scope: 'Boundary/line-a', name: 'Boundary', activeHydraulicPayloadId: 'payload-a',
    payloads: [{ id: 'payload-a', scope: 'Boundary/line-a/Payload/payload-a' }],
    newPayload: { fields: {}, constantUnits: {}, choices: { 'NewPayloadPage/ViewModel_SelectedExternalConnection': [] } } };
  const boundarySnapshot = { ...snapshot, sourceState: 'captured-boundary-draft', fields: { 'payload-value': '1' },
    boundary: { lines: [line], series: {}, selectedLineId: 'line-a', selectedPayloadId: 'payload-a' } };
  const boundaryDraft = createModelDraft(boundarySnapshot);
  boundaryDraft.fields['payload-value'] = '12.50';
  boundaryDraft.rows = { userTable: [{ id: 'row-a', values: { value: '3.00' } }] };
  boundaryDraft.samples = { userSeries: [{ id: 'sample-a', x: 1, y: 2 }] };
  boundaryDraft.sampleText = { userSeries: { 'sample-a': { x: '1.000', y: '2.000' } } };
  boundaryDraft.changedCells = { userTable: [{ rowID: 'row-a', columnID: 'value' }] };
  const refreshed = { ...boundarySnapshot, fields: { 'payload-value': '12.5' },
    coreLayerContext: { identityToken: 'new-project-context' },
    boundary: { ...boundarySnapshot.boundary, lines: [{ ...line, activeHydraulicPayloadId: null,
      newPayload: { ...line.newPayload, choices: { 'NewPayloadPage/ViewModel_SelectedExternalConnection': [{ id: 'New connection', title: 'New connection' }] } } }] } };
  const rebased = rebaseBoundaryExternalConnection(boundarySnapshot, boundaryDraft, refreshed, 'line-a', 'New connection');
  assert.equal(rebased.snapshot.fields, boundarySnapshot.fields, 'A project connection must not turn unsaved fields into a persisted baseline.');
  assert.equal(rebased.draft.fields, boundaryDraft.fields, 'Refresh must preserve user-entered numeric text, not its normalized projection.');
  assert.equal(rebased.draft.rows, boundaryDraft.rows, 'The complete unsaved table must survive project rebase.');
  assert.equal(rebased.draft.samples, boundaryDraft.samples, 'The complete unsaved samples must survive project rebase.');
  assert.equal(rebased.draft.sampleText, boundaryDraft.sampleText, 'The sample text draft must survive project rebase.');
  assert.equal(rebased.draft.changedCells, boundaryDraft.changedCells, 'Existing cell edit identities must survive project rebase.');
  assert.equal(rebased.draft.sourceState, boundaryDraft.sourceState, 'Refresh must preserve the structural draft journal.');
  assert.equal(isModelDraftDirty(rebased.snapshot, rebased.draft), true, 'The parent must still require a save after connection creation.');
  assert.equal(rebased.connectionName, 'New connection', 'The exact acknowledged connection is returned for selection.');
  assert.throws(() => rebaseBoundaryExternalConnection(boundarySnapshot, boundaryDraft, { ...refreshed, revision: 'f'.repeat(64) }, 'line-a', 'New connection'),
    'A refresh from another HDF revision must be rejected.');
  assert.throws(() => rebaseBoundaryExternalConnection(boundarySnapshot, boundaryDraft, refreshed, 'line-a', 'missing'),
    'A connection cannot be inserted into choices that the Host did not return.');
  assert.throws(() => rebaseBoundaryExternalConnection(boundarySnapshot, boundaryDraft, { ...refreshed,
    boundary: { ...refreshed.boundary, lines: [{ ...refreshed.boundary.lines[0], payloads: [{ id: 'other', scope: 'other' }] }] } }, 'line-a', 'New connection'),
    'A project-only refresh cannot replace payload identities.');
  console.log(JSON.stringify({ state: 'passed', checks: 18, scope: 'editor-validation-and-dirty-boundary-connection-rebase' }));
} finally {
  await rm(temporary, { recursive: true, force: true });
}
