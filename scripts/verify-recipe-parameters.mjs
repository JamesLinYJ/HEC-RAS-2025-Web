import assert from 'node:assert/strict';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { build } from 'vite';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const temporary = await mkdtemp(path.join(tmpdir(), 'hecras-recipe-parameters-'));
try {
  await build({ configFile: false, root, logLevel: 'silent', build: {
    lib: { entry: path.join(root, 'frontend/src/features/geometry/RecipeBuilder.tsx'), formats: ['es'], fileName: () => 'recipe.mjs' },
    outDir: temporary, emptyOutDir: true, minify: false, sourcemap: false,
  } });
  const { serializeSteps, stepFromSource } = await import(pathToFileURL(path.join(temporary, 'recipe.mjs')).href);
  const source = { name: 'RemoveNodes', parameters: { threshold: '0.2', count: '7', perimeter: 'false', deltaCost: '-0.45' } };
  const draft = stepFromSource(source, 'first');
  draft.parameters.threshold = '0.35';
  assert.equal(serializeSteps([draft]), 'RemoveNodes(threshold=0.35, count=7, perimeter=false, deltaCost=-0.45)',
    'Editing a visible parameter must preserve nondefault hidden algorithm parameters.');
  assert.equal(source.parameters.threshold, '0.2', 'A draft must not mutate its acknowledged source snapshot.');
  const orthogonal = stepFromSource({ name: 'Orthogonalize', parameters: { count: '3', smoothmethod: '2', useArea: 'false', nudgeTriangles: 'true' } }, 'second');
  const optimization = stepFromSource({ name: 'OptTopology', parameters: { threshold: '0.9', count: '13' } }, 'third');
  assert.equal(serializeSteps([optimization, orthogonal]), 'OptTopology(threshold=0.9, count=13)\nOrthogonalize(count=3, smoothMethod=2, useArea=false, nudgeTriangles=true)',
    'Reordering steps must retain hidden count and geometry flags while honoring source parameter spelling.');
  const unknown = stepFromSource({ name: 'FutureAlgorithm', parameters: { retainedFlag: 'false', retainedCost: '2.4' } }, 'fourth');
  assert.equal(serializeSteps([unknown]), 'FutureAlgorithm(retainedFlag=false, retainedCost=2.4)',
    'An unknown step must retain the complete parameters returned by the Host.');
  console.log(JSON.stringify({ state: 'passed', checks: 4, scope: 'recipe-hidden-algorithm-parameter-preservation' }));
} finally {
  await rm(temporary, { recursive: true, force: true });
}
