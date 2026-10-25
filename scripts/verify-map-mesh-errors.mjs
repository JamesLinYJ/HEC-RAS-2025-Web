import assert from 'node:assert/strict';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { build } from 'vite';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const temporary = await mkdtemp(path.join(tmpdir(), 'hecras-map-mesh-errors-'));
let checks = 0;
const check = (callback) => { callback(); checks++; };
try {
  const entry = path.join(temporary, 'entry.ts');
  await writeFile(entry, `export * from ${JSON.stringify(path.join(root,
    'frontend/src/features/map/mesh-errors/mapMeshErrorsWorkflow.ts'))};\n`);
  await build({ configFile: false, root, logLevel: 'silent', build: {
    lib: { entry, formats: ['es'], fileName: () => 'mesh-errors.mjs' }, outDir: path.join(temporary, 'out'),
    emptyOutDir: true, minify: false, sourcemap: false,
  } });
  const workflow = await import(pathToFileURL(path.join(temporary, 'out/mesh-errors.mjs')).href);
  const { mapMeshErrorCandidate: candidate, captureMapMeshErrors: capture, parseMapMeshErrorSnapshot: parse,
    meshErrorWriteReceipt: receipt, normalizeMeshThreshold: normalize, meshErrorZoomExtent: zoom,
    sameMaterializedMeshLocator: sameLocator } = workflow;
  // Sanitized representations of the actual Host contract. These are protocol
  // tests; they are not real HDF scans, writes or browser acceptance.
  const projectSHA = 'a'.repeat(64), xmlSHA = 'b'.repeat(64), visSHA = 'c'.repeat(64), nextVis = 'd'.repeat(64);
  const sourceSHA = 'e'.repeat(64), token = 'f'.repeat(64);
  const context = { projectPath: '/fixture/project.ras', projectXml: '<Project />', projectRevision: projectSHA,
    projectSessionID: '00000000-0000-4000-8000-000000000001', selectedAsset: null };
  const geometry = { rootName: 'Geometries', nodeType: 'Geometry', nodeName: 'Geometry',
    nodePath: [0, 0], assetID: '/fixture/Geometry.h5' };
  const flowLocator = { rootName: 'Geometries', nodeType: 'FlowAreaLayer', nodeName: 'Computational Mesh',
    nodePath: [0, 0, 1], parentGeometryNodePath: [0, 0], parentGeometryNodeName: 'Geometry',
    parentGeometryAssetID: '/fixture/Geometry.h5' };
  const flow = { id: 'flow-0', name: 'Computational Mesh', sourceClass: 'FlowAreaLayerVisualContext', locator: flowLocator };
  const roots = [{ id: 'geometry-0', name: 'Geometry', sourceClass: 'GeometryNodeVM', locator: geometry, children: [flow] }];
  const document = { id: '00000000-0000-4000-8000-000000000002', mapID: '00000000-0000-4000-8000-000000000002',
    mapIndex: 1, name: '地图', mapType: 'Geometry', visualizationRevision: visSHA, roots };
  const map = { projectPath: context.projectPath, projectXml: context.projectXml, projectXmlSHA: xmlSHA,
    projectSessionID: context.projectSessionID, mapID: document.mapID, mapIndex: 1, mapName: '地图',
    mapType: 'Geometry', expectedVisualizationRevision: visSHA };
  const catalog = { projectPath: context.projectPath, projectRevision: projectSHA, projectXmlSHA: xmlSHA,
    visualizationRevision: visSHA, mapID: document.mapID, mapIndex: 1, mapName: '地图', mapType: 'Geometry',
    contextToken: token, activeNodes: [], treeRoots: roots, treeSelectionOverrides: [] };
  const selected = candidate(context, document, catalog, map, flow.id);
  check(() => assert.ok(selected));
  for (const change of [
    { context: { ...context, projectSessionID: 'other-session' } },
    { document: { ...document, mapIndex: 0 } },
    { catalog: { ...catalog, visualizationRevision: nextVis } },
    { map: { ...map, projectXmlSHA: nextVis } },
    { node: 'another-flow' },
    { catalog: { ...catalog, treeRoots: [roots[0], roots[0]] } },
    { catalog: { ...catalog, treeRoots: [{ ...roots[0], locator: { ...geometry, assetID: '/other/Geometry.h5' } }] } },
  ]) check(() => assert.equal(candidate(change.context ?? context, change.document ?? document, change.catalog ?? catalog,
    change.map ?? map, change.node ?? flow.id), null));
  const calls = [];
  const gateway = { supports: () => true, request: async (method, params) => {
    calls.push({ method, params });
    return { sourcePath: params.sourcePath, canonicalPath: params.sourcePath, revision: sourceSHA, length: 1024 };
  } };
  const captured = await capture(gateway, selected, new AbortController().signal);
  check(() => assert.deepEqual(calls.map(call => call.method), ['workspace.file.inspect']));
  check(() => assert.equal(captured.request.sourceTreeCapture, true));
  check(() => assert.equal(captured.request.resultOwner, null));
  check(() => assert.equal(captured.request.contextToken, token));
  const issue = { id: 'mesh:0:node:6', meshIndex: 0, nodeIndex: 6, faceCount: 4, node: { x: 1, y: 1 },
    cellsMinX: 0, cellsMinY: 0, cellsMaxX: 2, cellsMaxY: 2,
    cells: [{ cellIndex: 0, coordinates: [{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 1, y: 1 }] }] };
  const value = { projectSessionID: context.projectSessionID, mapWorkflowIdentity: captured.request.mapWorkflowIdentity,
    contextToken: token, projectCanonicalPath: context.projectPath, projectRevision: projectSHA, projectXmlSHA: xmlSHA,
    visualizationRevision: visSHA, previousVisualizationRevision: visSHA, mapID: document.mapID, mapIndex: 1,
    mapName: '地图', mapType: 'Geometry', flowAreaNode: flowLocator, sourcePath: geometry.assetID, sourceRevision: sourceSHA,
    checkMeshErrors: true, maxFacesPerNode: 3, status: 'available', diagnostic: null,
    issues: [issue], totalIssueCount: 1, omittedIssueCount: 0 };
  check(() => assert.equal(parse(value, captured, 'load').issues[0].nodeIndex, 6));
  for (const patch of [
    { projectSessionID: 'another-session' }, { mapWorkflowIdentity: 'stale' }, { contextToken: '0'.repeat(64) },
    { projectCanonicalPath: '/other/project.ras' }, { projectRevision: nextVis }, { projectXmlSHA: nextVis },
    { mapID: context.projectSessionID }, { mapIndex: 2 }, { mapName: 'other' }, { mapType: 'Plan' },
    { sourcePath: '/other/Geometry.h5' }, { sourceRevision: nextVis }, { visualizationRevision: nextVis },
    { previousVisualizationRevision: nextVis }, { flowAreaNode: { ...flowLocator, nodePath: [0, 1, 1] } },
    { flowAreaNode: { ...flowLocator, parentGeometryAssetID: '/other/Geometry.h5' } },
    { maxFacesPerNode: 2 }, { maxFacesPerNode: 3.5 }, { maxFacesPerNode: 65 },
    { totalIssueCount: 2 }, { omittedIssueCount: -1 }, { issues: [issue, issue], totalIssueCount: 2 },
    { status: 'draftSettings' }, { status: 'disabled' }, { status: 'partial' },
    { status: 'savedReadbackUnavailable' }, { checkMeshErrors: false },
  ]) check(() => assert.throws(() => parse({ ...value, ...patch }, captured, 'load')));
  for (const patch of [
    { faceCount: 3 }, { nodeIndex: -1 }, { node: { x: Infinity, y: 1 } }, { cellsMaxX: -1 },
    { cellsMinY: null }, { cells: [{ cellIndex: 0, coordinates: [{ x: 0, y: NaN }] }] },
  ]) check(() => assert.throws(() => parse({ ...value, issues: [{ ...issue, ...patch }] }, captured, 'load')));
  const written = parse({ ...value, visualizationRevision: nextVis }, captured, 'set', { enabled: true, threshold: 2 });
  check(() => assert.deepEqual(receipt(written), { mapIndex: 1, mapName: '地图', mapType: 'Geometry',
    previousVisualizationRevision: visSHA, visualizationRevision: nextVis }));
  check(() => assert.throws(() => parse({ ...value, visualizationRevision: nextVis }, captured, 'set', { enabled: true, threshold: 8 })));
  const unavailable = { ...value, visualizationRevision: nextVis, status: 'savedReadbackUnavailable', issues: [], totalIssueCount: 0 };
  check(() => assert.equal(parse(unavailable, captured, 'set').status, 'savedReadbackUnavailable'));
  check(() => assert.throws(() => parse({ ...unavailable, visualizationRevision: visSHA }, captured, 'load')));
  check(() => assert.throws(() => parse({ ...unavailable, issues: [issue], totalIssueCount: 1 }, captured, 'set')));
  const implicit = { ...flowLocator, nodePath: null, parentGeometryNodePath: null };
  check(() => assert.equal(sameLocator(implicit, flowLocator), true));
  check(() => assert.equal(sameLocator(flowLocator, implicit), false));
  check(() => assert.equal(sameLocator(implicit, { ...flowLocator, nodeName: 'Another mesh' }), false));
  for (const [input, expected] of [[2, 3], [3.49, 3], [3.5, 4], [8, 8], [64.5, 64], [-10, 3]]) {
    check(() => assert.equal(normalize(input), expected));
  }
  check(() => assert.throws(() => normalize(NaN)));
  check(() => assert.deepEqual(zoom(issue), { minX: -1, minY: -1, maxX: 3, maxY: 3 }));
  check(() => assert.equal(zoom({ ...issue, cellsMinX: null, cellsMinY: null, cellsMaxX: null, cellsMaxY: null }), null));
  check(() => assert.equal(zoom({ ...issue, cellsMinX: -Number.MAX_VALUE, cellsMaxX: Number.MAX_VALUE }), null));
  check(() => assert.equal(zoom({ ...issue, cellsMinX: 1, cellsMaxX: 1 }), null));

  // MultiResult slot zero means the exact selected source, not the first result
  // in a plan's choices. The tree override must pin the same path and SHA.
  const resultNode = { rootName: 'Results', nodeType: 'MultiResult', nodeName: 'Plan', nodePath: [1, 0], assetID: '/fixture/Plan.h5' };
  const selectedPath = '/fixture/Results/second-result.hdf';
  const resultFields = { parentResultNodeType: 'MultiResult', parentResultNodeName: 'Plan', parentResultNodePath: [1, 0],
    parentResultAssetID: selectedPath, parentResultRevision: sourceSHA };
  const resultGeometry = { ...geometry, ...resultFields, rootName: 'Results', nodePath: [1, 0, 0], assetID: selectedPath };
  const resultFlow = { ...flow, locator: { ...flowLocator, ...resultFields, rootName: 'Results', nodePath: [1, 0, 0, 1],
    parentGeometryNodePath: [1, 0, 0], parentGeometryAssetID: selectedPath } };
  const resultCatalog = { ...catalog, treeRoots: [{ id: 'multi', name: 'Plan', sourceClass: 'MultiResultNodeVM', locator: resultNode,
    children: [{ ...roots[0], locator: resultGeometry, children: [resultFlow] }] }],
    treeSelectionOverrides: [{ planName: 'Plan', planAssetID: '/fixture/Plan.h5', selectedResultPath: selectedPath,
      selectedResultRevision: sourceSHA }] };
  const selectedResult = candidate(context, document, resultCatalog, map, flow.id);
  check(() => assert.ok(selectedResult));
  const resultCapture = await capture(gateway, selectedResult, new AbortController().signal);
  check(() => assert.equal(resultCapture.request.resultOwner.selectionIndex, 0));
  check(() => assert.equal(resultCapture.request.resultOwner.selectedResultPath, selectedPath));
  check(() => assert.deepEqual(resultCapture.request.resultOwner.selectionOverrides,
    [{ node: resultNode, selectionIndex: 0, mode: 'Explicit', resultPath: selectedPath, resultRevision: sourceSHA }]));
  check(() => assert.equal(candidate(context, document, { ...resultCatalog, treeSelectionOverrides: [] }, map, flow.id), null));
  check(() => assert.equal(candidate(context, document, { ...resultCatalog, treeSelectionOverrides: [
    { ...resultCatalog.treeSelectionOverrides[0], selectedResultPath: '/fixture/Results/first-result.hdf' },
  ] }, map, flow.id), null));
  console.log(`Map mesh-error protocol verification passed (${checks} checks). Real Host/UI acceptance is separate.`);
} finally {
  await rm(temporary, { recursive: true, force: true });
}
