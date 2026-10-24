import assert from 'node:assert/strict';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { build } from 'vite';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const directory = await mkdtemp(path.join(tmpdir(), 'hecras-map-analysis-'));
let checks = 0;
try {
  await build({ configFile: false, root, logLevel: 'silent', build: {
    lib: { entry: path.join(root, 'frontend/src/features/map/analysis/mapSampleLineAnalysis.ts'),
      formats: ['es'], fileName: () => 'analysis.mjs' },
    outDir: directory, emptyOutDir: true, minify: false, sourcemap: false,
  } });
  const { computeMapSampleLineCoordinateSHA: digest, mapSampleLineSeriesToPlot: plot } =
    await import(pathToFileURL(path.join(directory, 'analysis.mjs')).href);
  // Independent Python hashlib + struct.pack('>dd') protocol vectors. The
  // optional file is emitted by the actual retained C# Host hash implementation.
  const vectors = [
    { coordinates: [[1.25, -2.5], [3456789.125, 0.03125]], sha: 'E0F74729BD210CE79E5A488CDD68D363F609C734AF263FF4A70153115C54A795' },
    { coordinates: [[-0, 1], [1e20, -123.25]], sha: 'DC8B3BF6175ABCBDB4559E7F0B853FFAEE4EFEA7C63899ADA82640F8BE52082E' },
    { coordinates: [[3456789.125, 0.03125], [1.25, -2.5]], sha: '70C778A9717F55BFFC0075E3122C36240EB610209FA7DD6994B09CEAB9669469' },
  ];
  for (const vector of vectors) { assert.equal(await digest(vector.coordinates), vector.sha); checks++; }
  if (process.argv[2]) {
    const actual = JSON.parse(await readFile(path.resolve(process.argv[2]), 'utf8'));
    for (const vector of actual) {
      assert.equal(await digest(vector.coordinates.map(p => [p.x, p.y])), vector.sha);
      checks++;
    }
  }
  for (const invalid of [[], [[1, 2]], [[NaN, 1], [2, 3]], [[1, 2], [3, Infinity]], Array(10001).fill([0, 1])]) {
    await assert.rejects(digest(invalid)); checks++;
  }
  const original = { resultPath: '/工程/Result.h5', revision: 'a'.repeat(64), resultName: '真实结果',
    mode: 'Flow', quantity: 'Flow', units: 'm³/s', available: true, unavailableReason: null,
    points: [{ profileIndex: 0, time: '2026-10-24T00:00:00', value: 2.5 },
      { profileIndex: 1, time: '2026-10-24T00:00:01', value: -9999 },
      { profileIndex: 2, time: '2026-10-24T00:00:03', value: null },
      { profileIndex: 3, time: '2026-10-24T00:00:07', value: -1.25 }] };
  const before = JSON.stringify(original);
  const chart = plot(original, 'zh', 'source-part-0');
  assert.deepEqual(chart.dataset.times, original.points.map(p => p.time)); checks++;
  assert.deepEqual(chart.series.values, [2.5, null, null, -1.25]); checks++;
  assert.equal(chart.noDataCount, 2); checks++;
  assert.equal(chart.series.units, 'm³/s'); checks++;
  assert.equal(JSON.stringify(original), before); checks++;
  console.log(JSON.stringify({ state: 'passed', checks,
    scope: 'coordinate-wire-protocol-and-NoData-time-preservation; HTTP-and-DOM-not-covered',
    actualCSharpHostVectors: Boolean(process.argv[2]) }));
} finally { await rm(directory, { recursive: true, force: true }); }
