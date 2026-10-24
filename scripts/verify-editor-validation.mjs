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
        entry: path.join(root, 'frontend/src/features/editors/SchemaFieldRenderer.tsx'),
        formats: ['es'],
        fileName: () => 'editor-validation.mjs',
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
  console.log(JSON.stringify({ state: 'passed', checks: 4, scope: 'editor-schema-owner-validation' }));
} finally {
  await rm(temporary, { recursive: true, force: true });
}
