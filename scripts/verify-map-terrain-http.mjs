import assert from 'node:assert/strict';
import { createHash, randomUUID } from 'node:crypto';
import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';

const [baseUrl, manifestPath, reportPath] = process.argv.slice(2);
if (!baseUrl || !manifestPath || !reportPath) {
  throw new Error('Usage: node scripts/verify-map-terrain-http.mjs <loopback-server-url> <terrain-association-manifest.json> <report.json>');
}
const origin = new URL(baseUrl);
assert.ok(['http:', 'https:'].includes(origin.protocol) &&
  ['127.0.0.1', 'localhost', '[::1]'].includes(origin.hostname),
'Use a loopback C# server that can read the local fixture files.');
const resolvedManifestPath = path.resolve(manifestPath);
const destination = path.resolve(reportPath);
const manifest = JSON.parse(await readFile(resolvedManifestPath, 'utf8'));
assert.equal(manifest.schema, 'hecras-map-terrain-association-e2e/v1');
const report = { state: 'running', operations: [], checks: [] };
const sha = data => createHash('sha256').update(data).digest('hex').toUpperCase();
const bytes = async file => readFile(file);

async function operation(method, parameters, expectedErrorCode) {
  const accepted = await fetch(new URL('/api/operations', origin), {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ method, parameters }),
  });
  assert.equal(accepted.status, 202, `${method} must be accepted by the C# server.`);
  const { id } = await accepted.json();
  let cursor = 0;
  for (const deadline = Date.now() + 180_000; Date.now() < deadline;) {
    const response = await fetch(new URL(`/api/operations/${id}?after=${cursor}`, origin));
    assert.equal(response.status, 200);
    const result = await response.json();
    cursor = result.cursor;
    if (!result.hasMore && ['completed', 'failed', 'cancelled'].includes(result.state)) {
      report.operations.push({ method, id, state: result.state, error: result.error });
      if (expectedErrorCode) {
        assert.equal(result.state, 'failed');
        assert.equal(result.error?.code, expectedErrorCode);
        return result;
      }
      assert.equal(result.state, 'completed', JSON.stringify(result.error));
      return result.result;
    }
    await new Promise(resolve => setTimeout(resolve, 50));
  }
  throw new Error(`Unconfirmed operation ${id} (${method}).`);
}

const sameLocator = (actual, expected) => actual?.rootName === expected?.rootName &&
  actual?.nodeType === expected?.nodeType && actual?.nodeName === expected?.nodeName &&
  actual?.assetID === expected?.assetID &&
  JSON.stringify(actual?.nodePath ?? []) === JSON.stringify(expected?.nodePath ?? []) &&
  actual?.parentGeometryAssetID === expected?.parentGeometryAssetID &&
  JSON.stringify(actual?.parentGeometryNodePath ?? []) === JSON.stringify(expected?.parentGeometryNodePath ?? []) &&
  actual?.parentPlanAssetID === expected?.parentPlanAssetID &&
  JSON.stringify(actual?.parentPlanNodePath ?? []) === JSON.stringify(expected?.parentPlanNodePath ?? []) &&
  actual?.parentResultNodeType === expected?.parentResultNodeType &&
  actual?.parentResultAssetID === expected?.parentResultAssetID &&
  JSON.stringify(actual?.parentResultNodePath ?? []) === JSON.stringify(expected?.parentResultNodePath ?? []) &&
  actual?.parentResultRevision === expected?.parentResultRevision;
const flatten = roots => roots.flatMap(node => [node, ...flatten(node.children ?? [])]);
const mapRequestAt = revision => ({ ...manifest.mapRequest, expectedVisualizationRevision: revision });
const layerRequest = ({ map, geometryNodes, planNodes, resultOwners, viewport, width, height, contextToken }) => ({
  map,
  geometryNodes,
  associationNodes: [manifest.terrain.locator],
  viewport,
  width,
  height,
  planNodes,
  resultOwners,
  ...(contextToken ? { contextToken } : {}),
});
const validateFrame = (layer, { width, height, visible = true }) => {
  assert.ok(sameLocator(layer.locator, manifest.terrain.locator), 'The association response must retain the exact Terrain locator.');
  assert.equal(layer.kind, 'Terrain');
  assert.equal(layer.assetPath, manifest.terrain.sourcePath);
  assert.equal(layer.revision.toUpperCase(), manifest.terrain.sourceRevision.toUpperCase());
  assert.equal(layer.width, width);
  assert.equal(layer.height, height);
  assert.equal(layer.isChecked, visible);
  assert.equal(layer.effectiveVisible, visible);
  if (visible) {
    assert.equal(layer.status, 'available');
    assert.equal(Buffer.from(layer.rgba, 'base64').byteLength, width * height * 4);
    assert.ok(layer.legend.length > 0);
  } else {
    assert.equal(layer.status, 'hidden');
    assert.equal(layer.rgba, '');
  }
};

try {
  const visualizationPath = manifest.mapRequest.projectPath.replace(/\.ras$/i, '.vis');
  const projectBefore = await bytes(manifest.mapRequest.projectPath);
  const terrainBefore = await bytes(manifest.terrain.sourcePath);
  const resultPaths = manifest.originalCSharpHdfReference.resultSources.map(source => source.path);
  const resultsBefore = await Promise.all(resultPaths.map(bytes));
  let visualizationRevision = sha(await bytes(visualizationPath));
  assert.equal(visualizationRevision, manifest.mapRequest.expectedVisualizationRevision);

  const completeOwners = manifest.owners;
  const initialAssociation = await operation('map.associations.load', layerRequest({
    map: mapRequestAt(visualizationRevision),
    geometryNodes: completeOwners.geometryNodes,
    planNodes: completeOwners.planNodes,
    resultOwners: completeOwners.resultOwners,
    viewport: manifest.viewport,
    width: manifest.width,
    height: manifest.height,
  }));
  assert.equal(initialAssociation.visualizationRevision, visualizationRevision);
  const initialLayer = initialAssociation.layers.find(layer => sameLocator(layer.locator, manifest.terrain.locator));
  assert.ok(initialLayer);
  validateFrame(initialLayer, { width: manifest.width, height: manifest.height });
  const actualRgba = Buffer.from(initialLayer.rgba, 'base64');
  assert.equal(sha(actualRgba), manifest.renderReference.rgbaSHA256);
  const wireBytes = Buffer.byteLength(JSON.stringify(initialAssociation), 'utf8');
  assert.ok(wireBytes > 8 * 1024 * 1024, 'The real RGBA Terrain response must exceed the former 8 MiB line limit.');
  assert.ok(manifest.width * manifest.height <= 2_097_152, 'This frame stays within the retained renderer’s per-layer pixel bound.');
  report.render = { wireBytes, rgbaBytes: actualRgba.byteLength,
    rgbaSHA256: sha(actualRgba), width: initialLayer.width, height: initialLayer.height,
    sourcePath: initialLayer.assetPath, revision: initialLayer.revision,
    owners: { geometryNodes: initialLayer.geometryNodes, planNodes: initialLayer.planNodes,
      resultOwners: initialLayer.resultOwners } };
  report.checks.push('A real map.associations.load rendered the selected Terrain source as RGBA above 8 MiB, preserving exact source, revision and owner identities.');

  const measurementRequest = {
    map: mapRequestAt(visualizationRevision),
    terrainNode: initialLayer.locator,
    expectedTerrainPath: initialLayer.assetPath,
    expectedTerrainRevision: initialLayer.revision,
    expectedDependencies: initialLayer.dependencies.filter(dependency =>
      ['source-hdf', 'terrain-input-raster'].includes(dependency.role)),
    endpoints: [manifest.knownWorldPoint, manifest.noDataWorldPoint],
    resultOwners: initialLayer.resultOwners,
  };
  const measurement = await operation('map.measureTerrainElevations', measurementRequest);
  assert.equal(measurement.contextToken, manifest.contextToken);
  assert.deepEqual(measurement.sourceDependencies, measurementRequest.expectedDependencies);
  assert.ok(Math.abs(measurement.elevations[0] - manifest.expectedTerrainValue) <= manifest.terrainTolerance);
  assert.equal(measurement.elevations[1], null, 'Finite Terrain NoData must not become an elevation or slope.');
  await operation('map.measureTerrainElevations', { ...measurementRequest,
    expectedTerrainRevision: '0'.repeat(64) }, 'sourceChanged');
  report.terrainMeasurement = measurement;
  report.checks.push('Terrain measurement matches the direct source sample, returns null for the actual NoData point, and rejects an expired source revision.');

  const point = manifest.knownWorldPoint;
  const hoverBase = {
    map: mapRequestAt(visualizationRevision),
    contextToken: manifest.contextToken,
    selectionOverrides: manifest.selectionOverrides,
    point,
    probePoint: { x: point.x + 0.001, y: point.y },
    worldUnitsPerScreenPoint: 0.001,
    profileSelections: [],
    layers: [{ node: manifest.terrain.locator, kind: 'terrain',
      sourcePath: manifest.terrain.sourcePath, sourceRevision: manifest.terrain.sourceRevision }],
  };
  const hover = await operation('map.hover.identify', { ...hoverBase, requestID: randomUUID() });
  const terrainEntries = hover.entries.filter(entry => sameLocator(entry.node, manifest.terrain.locator));
  assert.equal(terrainEntries.length, 1);
  assert.equal(terrainEntries[0].kind, 'terrain');
  assert.equal(terrainEntries[0].sourcePath, manifest.terrain.sourcePath);
  assert.equal(terrainEntries[0].sourceRevision.toUpperCase(), manifest.terrain.sourceRevision.toUpperCase());
  assert.ok(Math.abs(Number(terrainEntries[0].value) - manifest.expectedTerrainValue) <= manifest.terrainTolerance,
    JSON.stringify({ actual: terrainEntries[0], directSourceSample: manifest.expectedTerrainValue }));
  report.terrainSample = { point, value: terrainEntries[0].value,
    directSourceSample: manifest.expectedTerrainValue, tolerance: manifest.terrainTolerance };
  report.checks.push('Hover sampled the actual Terrain at a known world coordinate and matched a direct C# source sample, independently of RGBA pixels.');

  const noData = await operation('map.hover.identify', {
    ...hoverBase, requestID: randomUUID(), point: manifest.noDataWorldPoint,
    probePoint: { x: manifest.noDataWorldPoint.x + 0.001, y: manifest.noDataWorldPoint.y },
  });
  assert.equal(noData.entries.filter(entry => sameLocator(entry.node, manifest.terrain.locator)).length, 0,
    'Outside-raster Terrain must remain NoData, not display a zero.');
  const staleSource = structuredClone(hoverBase);
  staleSource.requestID = randomUUID();
  staleSource.layers[0].sourceRevision = '0'.repeat(64);
  await operation('map.hover.identify', staleSource, 'sourceChanged');
  report.checks.push('Outside-raster NoData remains empty, and a stale Terrain source revision is rejected.');

  const zoomViewport = manifest.renderReference.viewportChanged.viewport;
  const viewportMismatch = await operation('map.associations.load', layerRequest({
    map: mapRequestAt(visualizationRevision),
    geometryNodes: completeOwners.geometryNodes,
    planNodes: completeOwners.planNodes,
    resultOwners: completeOwners.resultOwners,
    viewport: zoomViewport,
    width: manifest.renderReference.viewportChanged.width,
    height: manifest.renderReference.viewportChanged.height,
    contextToken: initialAssociation.contextToken,
  }), 'sourceChanged');
  assert.equal(viewportMismatch.state, 'failed');
  const zoomAssociation = await operation('map.associations.load', layerRequest({
    map: mapRequestAt(visualizationRevision),
    geometryNodes: completeOwners.geometryNodes,
    planNodes: completeOwners.planNodes,
    resultOwners: completeOwners.resultOwners,
    viewport: zoomViewport,
    width: manifest.renderReference.viewportChanged.width,
    height: manifest.renderReference.viewportChanged.height,
  }));
  const zoomLayer = zoomAssociation.layers.find(layer => sameLocator(layer.locator, manifest.terrain.locator));
  assert.ok(zoomLayer);
  validateFrame(zoomLayer, { width: manifest.renderReference.viewportChanged.width,
    height: manifest.renderReference.viewportChanged.height });
  assert.notEqual(zoomAssociation.contextToken, initialAssociation.contextToken);
  assert.equal(sha(Buffer.from(zoomLayer.rgba, 'base64')),
    manifest.renderReference.viewportChanged.rgbaSHA256);
  report.checks.push('The viewport changes the association context token; reusing the old token fails while the new viewport returns a verified frame.');

  const terrainGeometry = completeOwners.geometryNodes.find(node => node.nodeType === 'Geometry' && !node.parentResultNodeType);
  assert.ok(terrainGeometry, 'The static Terrain must retain its Plan Geometry owner.');
  const resultOwner = completeOwners.resultOwners.find(owner => owner.resultNode.nodeType === 'MultiResult');
  assert.ok(resultOwner, 'The fixture must retain the exact selected MultiResult owner.');
  const performVisibility = async (locator, checked) => {
    const receipt = await operation('map.tree.perform', {
      map: mapRequestAt(visualizationRevision), action: 'visibility', node: locator, checked,
    });
    assert.equal(receipt.previousVisualizationRevision, visualizationRevision);
    visualizationRevision = receipt.visualizationRevision;
    assert.equal(sha(await bytes(visualizationPath)), visualizationRevision);
    return receipt;
  };
  const visibilityReceipts = [];
  visibilityReceipts.push(await performVisibility(resultOwner.resultNode, false));
  let tree = await operation('map.tree.load', mapRequestAt(visualizationRevision));
  let treeNodes = flatten(tree.roots);
  const hiddenResult = treeNodes.find(node => sameLocator(node.locator, resultOwner.resultNode));
  const visibleTerrain = treeNodes.find(node => sameLocator(node.locator, manifest.terrain.locator));
  assert.ok(hiddenResult);
  assert.equal(hiddenResult.checked, 'unchecked');
  assert.ok(visibleTerrain, 'Hiding the Result must retain the independent Plan Terrain row.');
  assert.equal(visibleTerrain.checked, 'checked');
  const terrainOnly = await operation('map.associations.load', layerRequest({
    map: mapRequestAt(visualizationRevision),
    geometryNodes: [terrainGeometry],
    planNodes: completeOwners.planNodes,
    resultOwners: [],
    viewport: manifest.viewport,
    width: 256,
    height: 256,
  }));
  const terrainOnlyLayer = terrainOnly.layers.find(layer => sameLocator(layer.locator, manifest.terrain.locator));
  assert.ok(terrainOnlyLayer);
  validateFrame(terrainOnlyLayer, { width: 256, height: 256 });
  report.terrainOnly = { resultHidden: true, resultNode: resultOwner.resultNode,
    terrainStatus: terrainOnlyLayer.status, terrainSourcePath: terrainOnlyLayer.assetPath };
  visibilityReceipts.push(await performVisibility(resultOwner.resultNode, true));
  report.checks.push('With the selected Result hidden, the separate Plan Terrain remains checked, retained in the tree and rendered on its own.');

  // Exercise layer hide, persisted membership, reload, and show on the copied
  // fixture. Leave it visible so browser/Electron acceptance starts ready.
  visibilityReceipts.push(await performVisibility(manifest.terrain.locator, false));
  tree = await operation('map.tree.load', mapRequestAt(visualizationRevision));
  treeNodes = flatten(tree.roots);
  const retainedHiddenTerrain = treeNodes.find(node => sameLocator(node.locator, manifest.terrain.locator));
  assert.ok(retainedHiddenTerrain);
  assert.equal(retainedHiddenTerrain.checked, 'unchecked');
  const hiddenAssociation = await operation('map.associations.load', layerRequest({
    map: mapRequestAt(visualizationRevision), geometryNodes: [terrainGeometry],
    planNodes: completeOwners.planNodes, resultOwners: [], viewport: manifest.viewport,
    width: 64, height: 64,
  }));
  const hiddenLayer = hiddenAssociation.layers.find(layer => sameLocator(layer.locator, manifest.terrain.locator));
  assert.ok(hiddenLayer);
  validateFrame(hiddenLayer, { width: 64, height: 64, visible: false });
  visibilityReceipts.push(await performVisibility(manifest.terrain.locator, true));
  tree = await operation('map.tree.load', mapRequestAt(visualizationRevision));
  const retainedShownTerrain = flatten(tree.roots).find(node => sameLocator(node.locator, manifest.terrain.locator));
  assert.ok(retainedShownTerrain);
  assert.equal(retainedShownTerrain.checked, 'checked');
  const restoredAssociation = await operation('map.associations.load', layerRequest({
    map: mapRequestAt(visualizationRevision), geometryNodes: [terrainGeometry],
    planNodes: completeOwners.planNodes, resultOwners: [], viewport: manifest.viewport,
    width: 256, height: 256,
  }));
  const restoredLayer = restoredAssociation.layers.find(layer => sameLocator(layer.locator, manifest.terrain.locator));
  assert.ok(restoredLayer);
  validateFrame(restoredLayer, { width: 256, height: 256 });
  assert.equal(visibilityReceipts.at(-1).visualizationRevision, visualizationRevision);

  const resultContext = await operation('map.resultSources', {
    map: mapRequestAt(visualizationRevision), selectionOverrides: manifest.selectionOverrides,
  });
  assert.equal(resultContext.visualizationRevision, visualizationRevision);
  manifest.mapRequest.expectedVisualizationRevision = visualizationRevision;
  manifest.contextToken = resultContext.contextToken;
  manifest.owners.resultOwners = manifest.owners.resultOwners.map(owner => ({
    ...owner, contextToken: resultContext.contextToken,
  }));
  manifest.terrain.isChecked = true;
  manifest.terrain.effectiveVisible = true;
  manifest.visibilityReceipts = visibilityReceipts.map(receipt => ({
    previousVisualizationRevision: receipt.previousVisualizationRevision,
    visualizationRevision: receipt.visualizationRevision,
    persistedVisualizationSHA256: receipt.visualizationRevision,
  }));
  await writeFile(resolvedManifestPath, JSON.stringify(manifest, null, 2));

  assert.deepEqual(await bytes(manifest.mapRequest.projectPath), projectBefore);
  assert.deepEqual(await bytes(manifest.terrain.sourcePath), terrainBefore);
  for (let index = 0; index < resultPaths.length; index++)
    assert.deepEqual(await bytes(resultPaths[index]), resultsBefore[index]);
  report.visualizationRevision = visualizationRevision;
  report.visibilityReceipts = visibilityReceipts.map(receipt => ({
    previousVisualizationRevision: receipt.previousVisualizationRevision,
    visualizationRevision: receipt.visualizationRevision,
  }));
  report.checks.push('Terrain hide/show and Result visibility transactions retained exact tree membership, matched each .vis receipt to disk, and restored Terrain visible for desktop/browser acceptance.');
  report.checks.push('The source project, Terrain raster and both direct-HDF Results remained byte-for-byte unchanged.');
  report.state = 'passed';
} catch (error) {
  report.state = 'failed';
  report.error = error.stack;
  process.exitCode = 1;
}

await writeFile(destination, JSON.stringify(report, null, 2));
console.log(JSON.stringify({ state: report.state, checks: report.checks.length,
  render: report.render, terrainSample: report.terrainSample,
  terrainOnly: report.terrainOnly, visualizationRevision: report.visualizationRevision,
  error: report.error }));
