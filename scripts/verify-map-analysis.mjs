import assert from 'node:assert/strict';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { build } from 'vite';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const directory = await mkdtemp(path.join(tmpdir(), 'hecras-map-analysis-'));
const outputDirectory = path.join(directory, 'dist');
let checks = 0;
try {
  const sampleLineModule = path.join(root, 'frontend/src/features/map/analysis/mapSampleLineAnalysis.ts');
  const meshTimeSeriesModule = path.join(root, 'frontend/src/features/map/analysis/mapMeshTimeSeriesHelpers.ts');
  const entry = path.join(directory, 'verification-entry.ts');
  await writeFile(entry, [
    `export { computeMapSampleLineCoordinateSHA, mapSampleLineSeriesToPlot } from ${JSON.stringify(sampleLineModule)};`,
    `export { parseMeshTimeSeriesInspection, parseMeshTimeSeriesExecution } from ${JSON.stringify(meshTimeSeriesModule)};`,
  ].join('\n'), 'utf8');
  await build({ configFile: false, root, logLevel: 'silent', build: {
    lib: { entry,
      formats: ['es'], fileName: () => 'analysis.mjs' },
    outDir: outputDirectory, emptyOutDir: true, minify: false, sourcemap: false,
  } });
  const {
    computeMapSampleLineCoordinateSHA: digest,
    mapSampleLineSeriesToPlot: plot,
    parseMeshTimeSeriesInspection: parseInspection,
    parseMeshTimeSeriesExecution: parseExecution,
  } =
    await import(pathToFileURL(path.join(outputDirectory, 'analysis.mjs')).href);
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

  // Sanitized, representative MapAnalysisTypes capture and Host receipt shape.
  // Values and field names follow the real meshTimeSeries inspect/execute wire
  // contract; paths and owner tokens are fixtures, not machine-local data.
  const projectXmlSHA = 'a'.repeat(64);
  const resultRevision = 'result-revision-01';
  const resultPath = 'fixture/results/Plan CPU.hdf';
  const resultNode = {
    rootName: 'Focused Plans', nodeType: 'MultiResult', nodeName: 'Plan CPU', nodePath: [0, 0, 4],
    assetID: 'fixture/plans/Plan CPU.hdf', mapType: null, filename: null, filenameRelative: null,
  };
  const selectedResult = {
    resultPath, revision: resultRevision, resultName: 'Plan CPU', planName: 'Plan CPU',
    endTime: null, isComputing: false,
  };
  const capture = {
    ownerKey: 'fixture-owner',
    map: {
      projectPath: 'fixture/project.prj', projectXml: '<Project />', projectXmlSHA,
      mapID: 'fixture-map-id', mapIndex: 0, mapName: 'Fixture result map', mapType: 'Plan',
      expectedVisualizationRevision: 'visualization-revision-01',
      projectSessionID: 'fixture-session', coreLayerContext: null,
    },
    projectRevision: 'project-revision-01', projectXmlSHA,
    visualizationRevision: 'visualization-revision-01', contextToken: 'fixture-context-token',
    selectionOverrides: [{ node: resultNode, selectionIndex: 0, mode: 'Explicit', resultPath, resultRevision }],
    activeNode: {
      node: resultNode, nodeKind: 'MultiResult', name: 'Plan CPU',
      selections: [{ selectionIndex: 0, selectionMode: 'Explicit', selectedResult }],
      selectedMapType: 'Plan', profileIndex: 2,
    },
    treeRoots: [],
    slotZero: { selectionIndex: 0, selectionMode: 'Explicit', selectedResult },
    profileIndex: 2, profileTime: '2026-10-24T00:00:03.0000000',
    renderSnapshot: null, captureKey: 'fixture-capture-key',
  };
  const ownerEcho = {
    projectSessionID: capture.map.projectSessionID,
    projectCanonicalPath: capture.map.projectPath,
    projectRevision: capture.projectRevision,
    projectXmlSHA: capture.projectXmlSHA,
    visualizationRevision: capture.visualizationRevision,
    coreLayerContextIdentity: '',
    mapID: capture.map.mapID,
    mapIndex: capture.map.mapIndex,
    mapName: capture.map.mapName,
    mapType: capture.map.mapType,
    contextToken: capture.contextToken,
    originKind: 'Result', originPath: resultPath, originRevision: resultRevision,
    originMeshDraftSHA256: '', originTopologyDraftSHA256: '',
    originResultNode: resultNode, originSelectionIndex: 0,
  };
  const inspectionRaw = {
    ...ownerEcho, state: 'ready', message: null,
    flowAreas: [{ flowAreaIndex: 0, flowAreaID: 'flow-area-row:0', name: 'Solver Mesh',
      meshHash: -184749484, cellCount: 16, faceCount: 40, hasMesh: true }],
    matchingResultNames: ['Plan CPU'], skippedComputingResultCount: 0,
  };
  const inspection = parseInspection(inspectionRaw, capture);
  assert.equal(inspection.state, 'ready'); checks++;
  assert.deepEqual(inspection.flowAreas.map(({ flowAreaIndex, flowAreaID, meshHash, cellCount, faceCount }) =>
    ({ flowAreaIndex, flowAreaID, meshHash, cellCount, faceCount })),
  [{ flowAreaIndex: 0, flowAreaID: 'flow-area-row:0', meshHash: -184749484, cellCount: 16, faceCount: 40 }]); checks++;

  const rawTimes = [
    '2026-10-24T00:00:00.0000000',
    '2026-10-24T00:00:01.0000000',
    '2026-10-24T00:00:03.0000000',
  ];
  function executionReceipt({ elementKind, elementIndex, variable, units, values, operationID }) {
    return {
      ...ownerEcho, operationID, state: 'ready', message: null,
      flowAreaIndex: 0, flowAreaID: 'flow-area-row:0', flowAreaName: 'Solver Mesh',
      meshHash: -184749484, elementKind, elementIndex, variable, units,
      skippedComputingResultCount: 0,
      series: [{
        resultPath, resultRevision, resultName: 'Plan CPU', owners: [resultNode], selectionIndices: [0],
        flowAreaIndex: 0, flowAreaName: 'Solver Mesh', meshHash: -184749484,
        variable, times: rawTimes, values,
      }],
    };
  }
  const cellSelection = { flowArea: inspection.flowAreas[0], elementKind: 'Cell', elementIndex: 3, operationID: 'cell-operation' };
  const cellRaw = executionReceipt({ elementKind: 'Cell', elementIndex: 3, variable: 'Cell Depth', units: 'm',
    values: [1.25, 'NaN', null], operationID: cellSelection.operationID });
  const cellResult = parseExecution(cellRaw, capture, inspection, cellSelection);
  assert.equal(cellResult.state, 'ready'); checks++;
  assert.equal(cellResult.variable, 'Cell Depth'); checks++;
  assert.deepEqual(cellResult.entries[0].times, rawTimes); checks++;
  assert.deepEqual(cellResult.entries[0].values, [1.25, null, null]); checks++;

  const faceSelection = { flowArea: inspection.flowAreas[0], elementKind: 'Face', elementIndex: 7, operationID: 'face-operation' };
  const faceRaw = executionReceipt({ elementKind: 'Face', elementIndex: 7, variable: 'Face Velocity', units: 'm/s',
    values: [0.5, -0.25, 'Infinity'], operationID: faceSelection.operationID });
  const faceResult = parseExecution(faceRaw, capture, inspection, faceSelection);
  assert.equal(faceResult.state, 'ready'); checks++;
  assert.equal(faceResult.variable, 'Face Velocity'); checks++;
  assert.deepEqual(faceResult.entries[0].values, [0.5, -0.25, null]); checks++;

  function clone(value) { return structuredClone(value); }
  const badCellName = clone(cellRaw);
  badCellName.variable = 'Depth'; badCellName.series[0].variable = 'Depth';
  assert.throws(() => parseExecution(badCellName, capture, inspection, cellSelection)); checks++;
  const badCellSeriesName = clone(cellRaw); badCellSeriesName.series[0].variable = 'Depth';
  assert.throws(() => parseExecution(badCellSeriesName, capture, inspection, cellSelection)); checks++;
  const badFaceName = clone(faceRaw);
  badFaceName.variable = 'Velocity'; badFaceName.series[0].variable = 'Velocity';
  assert.throws(() => parseExecution(badFaceName, capture, inspection, faceSelection)); checks++;
  const badMesh = clone(cellRaw); badMesh.meshHash += 1;
  assert.throws(() => parseExecution(badMesh, capture, inspection, cellSelection)); checks++;
  const badSeriesMesh = clone(cellRaw); badSeriesMesh.series[0].meshHash += 1;
  assert.throws(() => parseExecution(badSeriesMesh, capture, inspection, cellSelection)); checks++;
  const badOriginPath = clone(cellRaw); badOriginPath.originPath = 'fixture/results/other.hdf';
  assert.throws(() => parseExecution(badOriginPath, capture, inspection, cellSelection)); checks++;
  const badOriginRevision = clone(cellRaw); badOriginRevision.originRevision = 'stale-result-revision';
  assert.throws(() => parseExecution(badOriginRevision, capture, inspection, cellSelection)); checks++;
  const badSeriesPath = clone(cellRaw); badSeriesPath.series[0].resultPath = 'fixture/results/other.hdf';
  assert.throws(() => parseExecution(badSeriesPath, capture, inspection, cellSelection)); checks++;
  const badSeriesRevision = clone(cellRaw); badSeriesRevision.series[0].resultRevision = 'stale-result-revision';
  assert.throws(() => parseExecution(badSeriesRevision, capture, inspection, cellSelection)); checks++;
  const staleInspection = clone(inspectionRaw); staleInspection.projectRevision = 'stale-project-revision';
  assert.throws(() => parseInspection(staleInspection, capture)); checks++;

  console.log(JSON.stringify({ state: 'passed', checks,
    scope: 'coordinate-wire-protocol;mesh-time-series-real-Host-shape-parser-regressions;NoData-and-raw-times;HTTP-and-DOM-not-covered',
    meshTimeSeriesHostWireRegression: true,
    actualCSharpHostVectors: Boolean(process.argv[2]) }));
} finally { await rm(directory, { recursive: true, force: true }); }
