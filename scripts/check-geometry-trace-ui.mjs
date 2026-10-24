import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { checkGeometryMapUI, validateGeometryMapManifest } from './check-geometry-map-ui.mjs';

const observerName = '__hecGeometryMapObserver';
const sha = bytes => createHash('sha256').update(bytes).digest('hex').toUpperCase();
const sameHash = (left, right) => String(left).toUpperCase() === String(right).toUpperCase();
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));

function accepted(items, method, predicate = () => true) {
  return items.filter(item => item.kind === 'accepted' && item.operationMethod === method && predicate(item.parameters));
}

function terminal(items, operationID) {
  return items.findLast(item => item.kind === 'terminal' && item.operationId === operationID && item.hasMore === false) ?? null;
}

async function observerItems(evaluate) {
  return await evaluate(`window.${observerName}?.items ?? []`);
}

async function waitOperation({ evaluate, method, startIndex = 0, predicate = () => true, timeoutMs = 150_000 }) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const items = await observerItems(evaluate);
    const matches = accepted(items.slice(startIndex), method, predicate);
    for (const entry of matches.reverse()) {
      const result = terminal(items, entry.operationId);
      if (!result) continue;
      assert.equal(result.state, 'completed', `${method} failed: ${JSON.stringify(result.error)}`);
      return { entry, accepted: { id: entry.operationId }, terminal: result };
    }
    await delay(100);
  }
  throw new Error(`Timed out waiting for ${method} to reach a terminal receipt.`);
}

function coordinateDistance(left, right) {
  return Math.hypot(left.x - right.x, left.y - right.y);
}

function distanceToSegment(point, start, end) {
  const dx = end.x - start.x;
  const dy = end.y - start.y;
  const lengthSquared = dx * dx + dy * dy;
  const ratio = lengthSquared === 0 ? 0 : Math.max(0, Math.min(1,
    ((point.x - start.x) * dx + (point.y - start.y) * dy) / lengthSquared));
  return coordinateDistance(point, { x: start.x + ratio * dx, y: start.y + ratio * dy });
}

function sourceFeatures(snapshot, kind) {
  return (snapshot?.map?.features ?? []).filter(feature => feature.featureKind === kind && feature.fid != null);
}

function connectedWaypointTriple(snapshot, mapCellSize) {
  const extent = snapshot?.map?.extent;
  assert.ok(extent && [extent.minX, extent.minY, extent.maxX, extent.maxY].every(Number.isFinite),
    'The current topology receipt omitted a finite map extent.');
  const nodes = sourceFeatures(snapshot, 'node').flatMap(feature => {
    const point = feature.paths?.[0]?.[0];
    return point && Number.isFinite(point.x) && Number.isFinite(point.y)
      ? [{ fid: feature.fid, point }] : [];
  });
  assert.ok(nodes.length >= 2, 'The current topology receipt has fewer than two stable source nodes.');
  const arcs = sourceFeatures(snapshot, 'arc').flatMap(feature => (feature.paths ?? []).flatMap(points =>
    points.slice(0, -1).map((point, index) => [point, points[index + 1]])))
    .filter(pair => pair[0] && pair[1]);
  const pairs = [];
  for (let left = 0; left < nodes.length; left++) {
    for (let right = left + 1; right < nodes.length; right++) {
      const distance = coordinateDistance(nodes[left].point, nodes[right].point);
      if (distance > mapCellSize * 30) pairs.push({ start: nodes[left], end: nodes[right], distance });
    }
  }
  pairs.sort((a, b) => b.distance - a.distance || a.start.fid - b.start.fid || a.end.fid - b.end.fid);
  for (const pair of pairs) {
    const dx = pair.end.point.x - pair.start.point.x;
    const dy = pair.end.point.y - pair.start.point.y;
    const offset = Math.max(pair.distance * 0.16, mapCellSize * 32);
    const candidates = [1, -1].map(sign => ({
      x: (pair.start.point.x + pair.end.point.x) / 2 + sign * (-dy / pair.distance) * offset,
      y: (pair.start.point.y + pair.end.point.y) / 2 + sign * (dx / pair.distance) * offset,
    }));
    for (const middle of candidates) {
      if (middle.x <= extent.minX || middle.x >= extent.maxX || middle.y <= extent.minY || middle.y >= extent.maxY) continue;
      const nodeGap = Math.min(...nodes.map(node => coordinateDistance(middle, node.point)));
      const arcGap = arcs.length ? Math.min(...arcs.map(([start, end]) => distanceToSegment(middle, start, end))) : Infinity;
      if (nodeGap > mapCellSize * 18 && arcGap > mapCellSize * 18) {
        return { startNodeFID: pair.start.fid, endNodeFID: pair.end.fid,
          points: [pair.start.point, middle, pair.end.point], middleClearance: { nodeGap, arcGap } };
      }
    }
  }
  throw new Error('Could not find a safe three-waypoint Terrain trace connected to two source nodes.');
}

function longestSourceArcSegment(snapshot, allowedFIDs, mapCellSize) {
  const candidates = [];
  for (const feature of sourceFeatures(snapshot, 'arc')) {
    if (!allowedFIDs.has(feature.fid) || feature.geometryType !== 'line' || feature.paths?.length !== 1) continue;
    const points = feature.paths[0];
    if (!points || points.length < 2) continue;
    for (let segmentIndex = 0; segmentIndex < points.length - 1; segmentIndex++) {
      const start = points[segmentIndex];
      const end = points[segmentIndex + 1];
      const pixels = coordinateDistance(start, end) / mapCellSize;
      if (pixels >= 60) candidates.push({ arcFID: feature.fid, segmentIndex, start, end, pixels, path: points });
    }
  }
  candidates.sort((a, b) => b.pixels - a.pixels || a.arcFID - b.arcFID || a.segmentIndex - b.segmentIndex);
  assert.ok(candidates.length, 'The saved Geometry has no sufficiently long source-arc segment for three DeepConnect anchors.');
  return candidates[0];
}

function closePoint(actual, expected, message, tolerance) {
  assert.ok(actual && Number.isFinite(actual.x) && Number.isFinite(actual.y), `${message}: missing coordinate.`);
  assert.ok(Math.abs(actual.x - expected.x) <= tolerance && Math.abs(actual.y - expected.y) <= tolerance,
    `${message}: actual=${JSON.stringify(actual)} expected=${JSON.stringify(expected)} tolerance=${tolerance}`);
}

export function buildGeometryTraceWorldScreenExpression(point) {
  return `(()=>{const svg=document.querySelector('[data-testid="geometry-topology-canvas"]');` +
    `if(!svg)throw Error('Missing topology canvas');const matrix=svg.getScreenCTM();if(!matrix)throw Error('Missing SVG screen matrix');` +
    `const rect=svg.getBoundingClientRect();const projected=new DOMPoint(${point.x},${-point.y}).matrixTransform(matrix);` +
    `const x=Math.round(projected.x),y=Math.round(projected.y);const world=new DOMPoint(x,y).matrixTransform(matrix.inverse());` +
    `const view=svg.viewBox.baseVal;return {x,y,world:{x:world.x,y:-world.y},` +
    `mapCellSize:{x:view.width/rect.width,y:view.height/rect.height},rect:{left:rect.left,top:rect.top,right:rect.right,bottom:rect.bottom,width:rect.width,height:rect.height}};})()`;
}

async function screenPointForWorld(evaluate, point) {
  return await evaluate(buildGeometryTraceWorldScreenExpression(point));
}

async function dispatchClick(command, point, clickCount = 1) {
  await command('Input.dispatchMouseEvent', { type: 'mousePressed', x: point.x, y: point.y,
    button: 'left', buttons: 1, clickCount });
  await command('Input.dispatchMouseEvent', { type: 'mouseReleased', x: point.x, y: point.y,
    button: 'left', buttons: 0, clickCount });
}

async function clickTestId(evaluate, waitFor, selector, label) {
  await waitFor(`Boolean(document.querySelector(${JSON.stringify(selector)})&&!document.querySelector(${JSON.stringify(selector)}).disabled)`,
    `${label} enabled`, 60_000);
  await evaluate(`(()=>{const button=document.querySelector(${JSON.stringify(selector)});if(!button||button.disabled)throw Error(${JSON.stringify('Unavailable ' + label)});button.click()})()`);
}

async function setSelectValue(evaluate, selector, value) {
  await evaluate(`(()=>{const select=document.querySelector(${JSON.stringify(selector)});` +
    `if(!(select instanceof HTMLSelectElement))throw Error('Missing select ${selector}');` +
    `Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype,'value').set.call(select,${JSON.stringify(value)});` +
    `select.dispatchEvent(new Event('change',{bubbles:true}));})()`);
}

async function pressEnter(command, evaluate) {
  await evaluate('document.querySelector("[data-testid=geometry-topology-canvas]")?.focus()');
  await command('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Enter', code: 'Enter', windowsVirtualKeyCode: 13 });
  await command('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Enter', code: 'Enter', windowsVirtualKeyCode: 13 });
}

function mapCommand(parameters) {
  return parameters?.mutation?.mapCommand ?? null;
}

function assertPreviewOwner(preview, currentDraft, mapCapture, geometryRevision, action) {
  const request = preview.entry.parameters ?? {};
  const result = preview.terminal.result ?? {};
  const owner = request.mapRequestContext;
  assert.equal(request.revision, currentDraft.revision);
  assert.equal(request.draftState, currentDraft.draftState);
  assert.equal(owner?.mapID, mapCapture.mapID);
  assert.equal(owner?.contextToken, mapCapture.contextToken);
  assert.equal(owner?.geometryRevision, geometryRevision);
  assert.equal(owner?.expectedDraftIdentity, currentDraft.completion?.draftIdentity);
  assert.equal(result.requestID, owner?.requestID);
  assert.equal(result.mapID, mapCapture.mapID);
  assert.equal(result.contextToken, mapCapture.contextToken);
  assert.equal(result.geometryRevision, geometryRevision);
  assert.equal(result.revision, currentDraft.revision);
  assert.equal(result.draftIdentity, currentDraft.completion?.draftIdentity);
  assert.equal(result.action, action);
  assert.ok(/^[0-9A-F]{64}$/i.test(result.pathSHA256 ?? ''), 'Path preview omitted its content hash.');
  assert.ok(Array.isArray(result.coordinates) && result.coordinates.length >= 2, 'Path preview returned no source path.');
  return result;
}

export function buildGeometryTraceRenderedPathExpression() {
  return `(()=>{const element=document.querySelector('[data-testid="geometry-topology-path-preview"]');` +
    `if(!element)return null;return {action:element.dataset.action,requestID:element.dataset.requestId,` +
    `sourceRevision:element.dataset.sourceRevision,draftIdentity:element.dataset.draftIdentity,mapID:element.dataset.mapId,` +
    `contextToken:element.dataset.contextToken,pathSHA256:element.dataset.pathSha256,` +
    `sourceCoordinates:JSON.parse(element.dataset.sourceCoordinates??'null'),` +
    `points:(element.getAttribute('points')??'').trim().split(/\\s+/).filter(Boolean).map(pair=>{const [x,y]=pair.split(',').map(Number);return {x,y:-y}})};})()`;
}

async function assertRenderedPath(evaluate, preview) {
  const rendered = await evaluate(buildGeometryTraceRenderedPathExpression());
  assert.ok(rendered, 'The current Host path preview was not rendered as a source-coordinate polyline.');
  assert.equal(rendered.action, preview.action);
  assert.equal(rendered.requestID, preview.requestID);
  assert.equal(rendered.sourceRevision, preview.sourceRevision);
  assert.equal(rendered.draftIdentity, preview.draftIdentity);
  assert.equal(rendered.mapID, preview.mapID);
  assert.equal(rendered.contextToken, preview.contextToken);
  assert.equal(rendered.pathSHA256, preview.pathSHA256);
  assert.deepEqual(rendered.sourceCoordinates, preview.coordinates);
  assert.equal(rendered.points.length, preview.coordinates.length, 'Rendered path vertex count differs from the Host preview.');
  rendered.points.forEach((point, index) => closePoint(point, preview.coordinates[index],
    `Rendered SVG path vertex ${index} differs from the Host path`, 1e-9));
  return rendered;
}

async function clickWorldPoint({ evaluate, waitFor, command, point, report, label }) {
  await evaluate('(()=>{const canvas=document.querySelector("[data-testid=geometry-topology-canvas]");if(!canvas)throw Error("Missing topology canvas");canvas.scrollIntoView({block:"center",inline:"nearest"});})()');
  await waitFor('(()=>{const rect=document.querySelector("[data-testid=geometry-topology-canvas]")?.getBoundingClientRect();return !!rect&&rect.top>=0&&rect.bottom<=window.innerHeight})()',
    `${label} canvas is visible after layout changes`, 30_000);
  const projected = await screenPointForWorld(evaluate, point);
  assert.ok(projected.x >= projected.rect.left && projected.x <= projected.rect.right &&
    projected.y >= projected.rect.top && projected.y <= projected.rect.bottom,
  `${label} no longer projects inside the visible canvas after its latest layout change.`);
  await dispatchClick(command, projected);
  report.push({ label, screen: { x: projected.x, y: projected.y }, pointerWorld: projected.world,
    mapCellSize: projected.mapCellSize, canvasRect: projected.rect });
  return projected;
}

function pathForFeature(snapshot, featureKind, fid) {
  const feature = snapshot?.map?.features?.find(item => item.featureKind === featureKind && item.fid === fid);
  return feature?.geometryType === 'line' && feature.paths?.length === 1 ? feature.paths[0] : null;
}

function connectionArcFID(preview, receipt, label) {
  const fid = preview?.connectionArcFID;
  assert.ok(Number.isSafeInteger(fid) && fid >= 0, `${label} preview omitted the Host-selected connection Arc FID.`);
  assert.ok((receipt?.arcs?.rows ?? []).some(row => row.fid === fid),
    `${label} draft does not contain the exact connection Arc identified by its Host preview.`);
  return fid;
}

function assertPathIncluded(actualPath, expectedPath, tolerance, label) {
  assert.ok(Array.isArray(actualPath) && Array.isArray(expectedPath) && expectedPath.length >= 2,
    `${label} is missing its source-coordinate path.`);
  let matched = 0;
  for (const point of actualPath) {
    if (matched < expectedPath.length && Math.abs(point.x - expectedPath[matched].x) <= tolerance &&
        Math.abs(point.y - expectedPath[matched].y) <= tolerance) matched++;
  }
  assert.equal(matched, expectedPath.length, `${label} does not preserve every preview vertex in source order.`);
}

function traceOperationSummary(items) {
  const byID = new Map(items.filter(item => item.kind === 'accepted').map(item => [item.operationId, item]));
  return items.flatMap(item => {
    if (item.kind !== 'terminal') return [];
    const request = byID.get(item.operationId);
    if (!request || !['topology.mapCommand.preview', 'topology.edit'].includes(request.operationMethod)) return [];
    const command = mapCommand(request.parameters);
    const result = item.result ?? {};
    return [{ operationMethod: request.operationMethod, operationID: item.operationId, state: item.state,
      action: command?.action ?? null, expectedPathSHA256: command?.expectedPathSHA256 ?? null,
      pathSHA256: result.pathSHA256 ?? null, pathCoordinateCount: result.coordinates?.length ?? null,
      anchorCount: command?.deepConnectSource?.anchors?.length ?? null,
      mapID: request.parameters?.mapRequestContext?.mapID ?? result.mapRequestContext?.mapID ?? null,
      contextToken: request.parameters?.mapRequestContext?.contextToken ?? result.mapRequestContext?.contextToken ?? null,
      draftIdentity: result.draftIdentity ?? result.completion?.draftIdentity ?? null,
      errorCode: item.error?.code ?? null }];
  });
}

async function saveScreenshot(command, output, name) {
  if (!output) return;
  const screenshot = await command('Page.captureScreenshot', { format: 'png' });
  await writeFile(path.join(output, name), Buffer.from(screenshot.data, 'base64'));
}

/** Optional source-backed SmartAdd/DeepConnect multi-anchor acceptance. */
export async function checkGeometryTraceUI({ manifest, output, evaluate, waitFor, command, navigate, report }) {
  const fixture = await validateGeometryMapManifest(manifest);
  const initialProjectSHA256 = sha(await readFile(fixture.projectPath));
  const initialGeometrySHA256 = sha(await readFile(fixture.geometryPath));
  const checks = [];
  const clicks = [];
  report.geometryTrace = { state: 'running', manifestSchema: manifest.schema,
    projectRevision: initialProjectSHA256, geometryRevision: initialGeometrySHA256, checks };
  try {
    // Reuse the already accepted source-backed project/map workflow as setup;
    // the additional trace stage starts from its current unsaved topology draft.
    await checkGeometryMapUI({ manifest, output, evaluate, waitFor, command, navigate, report });
    const items = await observerItems(evaluate);
    const mapGeometryLoad = accepted(items, 'map.geometry.load').findLast(item =>
      item.parameters?.map?.mapIndex === manifest.map.mapIndex &&
      item.parameters?.map?.mapName === manifest.map.mapName && item.parameters?.map?.mapType === manifest.map.mapType);
    assert.ok(mapGeometryLoad, 'The shared geometry setup did not capture the saved map owner.');
    const mapCaptureTerminal = terminal(items, mapGeometryLoad.operationId);
    assert.ok(mapCaptureTerminal?.result?.contextToken, 'The saved map capture omitted its context token.');
    const mapCapture = { mapID: mapGeometryLoad.parameters.map.mapID,
      contextToken: mapCaptureTerminal.result.contextToken,
      geometryRevision: mapCaptureTerminal.result.geometryRevision };
    assert.equal(mapCapture.mapID, mapCaptureTerminal.result.mapID);
    assert.ok(sameHash(mapCapture.geometryRevision, initialGeometrySHA256));

    const topologyLoadEntry = accepted(items, 'topology.load').findLast(item =>
      String(item.parameters?.model?.assetPath ?? '').includes(path.basename(fixture.geometryPath)));
    assert.ok(topologyLoadEntry, 'The shared geometry setup omitted the initial topology receipt.');
    const topologyLoad = terminal(items, topologyLoadEntry.operationId)?.result;
    assert.ok(topologyLoad?.completion?.draftIdentity, 'The initial topology receipt omitted draft identity.');
    const setupArcEntry = accepted(items, 'topology.edit').findLast(item => mapCommand(item.parameters)?.action === 'addArc');
    assert.ok(setupArcEntry, 'The shared geometry setup did not leave its accepted ordinary arc draft.');
    const initialDraft = terminal(items, setupArcEntry.operationId)?.result;
    assert.ok(initialDraft?.completion?.draftIdentity && initialDraft.isDirty,
      'The setup arc edit did not return the current unsaved topology draft.');
    const geometryRevision = initialDraft.revision;
    assert.ok(sameHash(geometryRevision, initialGeometrySHA256));
    await waitFor('Boolean(document.querySelector("[data-testid=geometry-topology-canvas]")&&document.querySelector("[data-testid=geometry-topology-map-workspace]"))',
      'the shared Geometry canvas remains mounted');

    // SmartAdd: three actual waypoints connected to two stable source nodes,
    // with a read-only Host path preview before exactly one draft mutation.
    await clickTestId(evaluate, waitFor, '[data-testid="geometry-map-tool-smartAdd"]', 'SmartAdd tool');
    await waitFor('Boolean(document.querySelector("[data-testid=geometry-topology-smart-add-controls]"))', 'SmartAdd settings');
    await setSelectValue(evaluate, '[data-testid="smart-add-field-algorithm"]', 'HighGround');
    await clickTestId(evaluate, waitFor, '[data-testid="geometry-topology-smart-add-apply"]', 'apply SmartAdd settings');
    await waitFor('document.querySelector("[data-testid=geometry-topology-canvas]")?.dataset.activeTool==="smartAdd"', 'SmartAdd map input active');
    await waitFor('document.querySelector("[data-testid=geometry-topology-preview-status]")?.textContent.includes("路径预览")',
      'SmartAdd path preview capability status');

    const viewport = await evaluate('(()=>{const svg=document.querySelector("[data-testid=geometry-topology-canvas]");const r=svg.getBoundingClientRect();const v=svg.viewBox.baseVal;return {mapCellSize:v.width/r.width,rect:{left:r.left,top:r.top,right:r.right,bottom:r.bottom,width:r.width,height:r.height}}})()');
    const smartWaypoints = connectedWaypointTriple(initialDraft, viewport.mapCellSize);
    report.geometryTrace.smartAdd = { startNodeFID: smartWaypoints.startNodeFID, endNodeFID: smartWaypoints.endNodeFID,
      plannedPoints: smartWaypoints.points, middleClearance: smartWaypoints.middleClearance, clicks: [] };
    const smartStart = (await observerItems(evaluate)).length;
    for (let index = 0; index < smartWaypoints.points.length; index++) {
      const clicked = await clickWorldPoint({ evaluate, waitFor, command, point: smartWaypoints.points[index],
        report: report.geometryTrace.smartAdd.clicks, label: `SmartAdd waypoint ${index + 1}` });
      clicks.push({ tool: 'smartAdd', index, world: clicked.world, screen: { x: clicked.x, y: clicked.y },
        mapCellSize: clicked.mapCellSize });
      await waitFor(`document.querySelectorAll('[data-testid="geometry-topology-smart-add-waypoints"] [data-waypoint-index]').length>=${index + 1}`,
        `SmartAdd waypoint ${index + 1} captured`, 30_000);
    }
    await waitFor('Boolean(document.querySelector("[data-testid=geometry-topology-path-preview][data-action=smartAdd]"))',
      'rendered SmartAdd Host path preview', 90_000);
    const smartPreview = await waitOperation({ evaluate, method: 'topology.mapCommand.preview', startIndex: smartStart,
      predicate: parameters => mapCommand(parameters)?.action === 'smartAdd' && mapCommand(parameters)?.coordinates?.length === 3 });
    const smartPath = assertPreviewOwner(smartPreview, initialDraft, mapCapture, geometryRevision, 'smartAdd');
    const smartCommandPreview = mapCommand(smartPreview.entry.parameters);
    assert.equal(smartCommandPreview?.coordinates?.length, 3, 'SmartAdd preview request omitted one of the real waypoints.');
    assert.equal(smartCommandPreview?.startNodeFID, smartWaypoints.startNodeFID,
      'SmartAdd did not preserve the captured source start node.');
    assert.equal(smartCommandPreview?.smartAddSource?.options?.algorithm, 'HighGround');
    assert.ok(sameHash(smartCommandPreview?.smartAddSource?.terrainCapture?.sourceRevision,
      initialDraft.smartAddTerrainCapture?.sourceRevision));
    assert.ok(sameHash(smartPath.sourceRevision, initialDraft.smartAddTerrainCapture?.sourceRevision),
      'SmartAdd preview receipt does not identify the same captured Terrain revision as its request.');
    for (let index = 0; index < 3; index++) {
      const toleranceX = clicks[index].mapCellSize?.x ?? viewport.mapCellSize;
      const toleranceY = clicks[index].mapCellSize?.y ?? viewport.mapCellSize;
      closePoint(smartCommandPreview.coordinates[index], clicks[index].world,
        `SmartAdd waypoint ${index + 1} differs from its integer-pixel pointer target`, Math.max(toleranceX, toleranceY));
    }
    const renderedSmartPreview = await assertRenderedPath(evaluate, smartPath);
    report.geometryTrace.smartAdd.preview = { operationID: smartPreview.entry.operationId,
      pathSHA256: smartPath.pathSHA256, sourceRevision: smartPath.sourceRevision,
      draftIdentity: smartPath.draftIdentity, mapID: smartPath.mapID, contextToken: smartPath.contextToken,
      coordinateCount: smartPath.coordinates.length, renderedVertexCount: renderedSmartPreview.points.length };
    await saveScreenshot(command, output, 'geometry-trace-smart-add-preview.png');

    const smartEditStart = (await observerItems(evaluate)).length;
    await pressEnter(command, evaluate);
    const smartFinalPreview = await waitOperation({ evaluate, method: 'topology.mapCommand.preview', startIndex: smartEditStart,
      predicate: parameters => mapCommand(parameters)?.action === 'smartAdd' &&
        mapCommand(parameters)?.coordinates?.length === 3 && parameters?.mapRequestContext?.expectedDraftIdentity === initialDraft.completion.draftIdentity });
    const smartFinalPath = assertPreviewOwner(smartFinalPreview, initialDraft, mapCapture, geometryRevision, 'smartAdd');
    assert.equal(smartFinalPath.pathSHA256, smartPath.pathSHA256,
      'SmartAdd final preview differs from the last rendered path preview.');
    const smartEdit = await waitOperation({ evaluate, method: 'topology.edit', startIndex: smartEditStart,
      predicate: parameters => mapCommand(parameters)?.action === 'smartAdd' &&
        sameHash(mapCommand(parameters)?.expectedPathSHA256, smartFinalPath.pathSHA256) });
    const smartReceipt = smartEdit.terminal.result;
    assert.equal(accepted((await observerItems(evaluate)).slice(smartEditStart), 'topology.edit',
      parameters => mapCommand(parameters)?.action === 'smartAdd').length, 1,
    'SmartAdd Enter must submit one topology mutation.');
    assert.equal(smartEdit.entry.parameters.mapRequestContext?.expectedDraftIdentity, initialDraft.completion.draftIdentity);
    assert.equal(smartReceipt?.mapRequestContext?.mapID, mapCapture.mapID);
    assert.equal(smartReceipt?.mapRequestContext?.contextToken, mapCapture.contextToken);
    assert.equal(smartReceipt?.mapRequestContext?.expectedDraftIdentity, initialDraft.completion.draftIdentity);
    assert.equal(smartReceipt?.isDirty, true);
    assert.notEqual(smartReceipt?.completion?.draftIdentity, initialDraft.completion.draftIdentity);
    assert.ok(sameHash(smartReceipt?.revision, geometryRevision));
    const smartArcFID = connectionArcFID(smartPath, smartReceipt, 'SmartAdd');
    const smartArcPath = pathForFeature(smartReceipt, 'arc', smartArcFID);
    assert.equal(smartArcPath?.length, smartPath.coordinates.length,
      'The SmartAdd draft Arc vertex count differs from the exact Host preview path.');
    smartPath.coordinates.forEach((point, index) => closePoint(smartArcPath[index], point,
      `SmartAdd result Arc vertex ${index} differs from the Host preview`, Math.max(viewport.mapCellSize, 1e-9)));
    const smartArcRow = smartReceipt.arcs.rows.find(row => row.fid === smartArcFID);
    const startNodeIndex = initialDraft.nodes.rows.findIndex(row => row.fid === smartWaypoints.startNodeFID);
    const endNodeIndex = initialDraft.nodes.rows.findIndex(row => row.fid === smartWaypoints.endNodeFID);
    assert.ok(startNodeIndex >= 0 && endNodeIndex >= 0, 'SmartAdd connected-node FIDs are absent from the parent topology rows.');
    assert.equal(smartArcRow?.startNodeID, startNodeIndex,
      'The SmartAdd result Arc is not connected to the captured source start node.');
    assert.equal(smartArcRow?.endNodeID, endNodeIndex,
      'The SmartAdd result Arc is not connected to the captured source end node.');
    const startNodePoint = sourceFeatures(initialDraft, 'node').find(feature => feature.fid === smartWaypoints.startNodeFID)?.paths?.[0]?.[0];
    const endNodePoint = sourceFeatures(initialDraft, 'node').find(feature => feature.fid === smartWaypoints.endNodeFID)?.paths?.[0]?.[0];
    closePoint(smartArcPath[0], startNodePoint, 'The new SmartAdd Arc does not start at its captured node', 1e-9);
    closePoint(smartArcPath.at(-1), endNodePoint, 'The new SmartAdd Arc does not end at its captured node', 1e-9);
    report.geometryTrace.smartAdd.completion = { operationID: smartEdit.entry.operationId,
      newArcFID: smartArcFID, vertexCount: smartArcPath.length, startNodeID: smartArcRow.startNodeID,
      endNodeID: smartArcRow.endNodeID, previousDraftIdentity: initialDraft.completion.draftIdentity,
      nextDraftIdentity: smartReceipt.completion.draftIdentity, dirty: smartReceipt.isDirty };
    checks.push('three real SmartAdd waypoints were previewed by the Host and Enter committed exactly one connected Arc with the same path hash into the current unsaved draft');

    // DeepConnect: capture three ordered anchors on one original persisted
    // topology arc; its preview and one double-click-confirmed mutation share one draft.
    const originalArcFIDs = new Set((topologyLoad.arcs?.rows ?? []).map(row => row.fid));
    const segment = longestSourceArcSegment(smartReceipt, originalArcFIDs, viewport.mapCellSize);
    await clickTestId(evaluate, waitFor, '[data-testid="geometry-map-tool-deepConnect"]', 'DeepConnect tool');
    await waitFor('document.querySelector("[data-testid=geometry-topology-canvas]")?.dataset.activeTool==="deepConnect"', 'DeepConnect map input active');
    const fractions = [0.2, 0.5, 0.8];
    const anchorsWorld = fractions.map(fraction => ({
      x: segment.start.x + (segment.end.x - segment.start.x) * fraction,
      y: segment.start.y + (segment.end.y - segment.start.y) * fraction,
    }));
    const deepStart = (await observerItems(evaluate)).length;
    report.geometryTrace.deepConnect = { sourceFID: segment.arcFID, sourceSegmentIndex: segment.segmentIndex,
      sourcePath: segment.path, requestedFractions: fractions, clicks: [] };
    for (let index = 0; index < anchorsWorld.length; index++) {
      const clicked = await clickWorldPoint({ evaluate, waitFor, command, point: anchorsWorld[index],
        report: report.geometryTrace.deepConnect.clicks, label: `DeepConnect source anchor ${index + 1}` });
      clicks.push({ tool: 'deepConnect', index, world: clicked.world, screen: { x: clicked.x, y: clicked.y } });
      await waitFor(`document.querySelectorAll('[data-testid="geometry-topology-deep-connect-anchors"] [data-anchor-index]').length>=${index + 1}`,
        `DeepConnect source anchor ${index + 1} captured`, 30_000);
    }
    await waitFor('Boolean(document.querySelector("[data-testid=geometry-topology-path-preview][data-action=deepConnect]"))',
      'rendered DeepConnect Host path preview', 90_000);
    const deepPreview = await waitOperation({ evaluate, method: 'topology.mapCommand.preview', startIndex: deepStart,
      predicate: parameters => mapCommand(parameters)?.action === 'deepConnect' &&
        mapCommand(parameters)?.deepConnectSource?.anchors?.length === 3 });
    const deepPath = assertPreviewOwner(deepPreview, smartReceipt, mapCapture, geometryRevision, 'deepConnect');
    const deepCommandPreview = mapCommand(deepPreview.entry.parameters);
    const deepSource = deepCommandPreview?.deepConnectSource;
    assert.equal(deepSource?.sourceKind, 'topology');
    assert.equal(deepSource?.featureFID, segment.arcFID);
    assert.equal(deepSource?.sourceLocator, `arc:${segment.arcFID}`);
    assert.equal(deepSource?.sourceRevision, geometryRevision);
    assert.ok(sameHash(deepPath.sourceRevision, geometryRevision),
      'Topology DeepConnect preview receipt does not identify the captured source Geometry revision.');
    assert.deepEqual(deepSource?.points, segment.path);
    assert.equal(deepSource?.anchors?.length, 3);
    assert.deepEqual(deepSource.anchors.map(anchor => anchor.segmentIndex), [segment.segmentIndex, segment.segmentIndex, segment.segmentIndex]);
    deepSource.anchors.forEach((anchor, index) => assert.ok(Math.abs(anchor.fraction - fractions[index]) <= 0.02,
      `DeepConnect anchor ${index + 1} does not match its rendered same-source segment target.`));
    assert.ok(deepPath.coordinates.length >= 3, 'The resolved DeepConnect path omitted its three ordered anchors.');
    const projectFraction = point => {
      const dx = segment.end.x - segment.start.x;
      const dy = segment.end.y - segment.start.y;
      return ((point.x - segment.start.x) * dx + (point.y - segment.start.y) * dy) / (dx * dx + dy * dy);
    };
    const anchorTolerance = Math.max(...report.geometryTrace.deepConnect.clicks.map(click =>
      Math.max(click.mapCellSize.x, click.mapCellSize.y))) / coordinateDistance(segment.start, segment.end);
    deepSource.anchors.forEach((anchor, index) => assert.ok(Math.abs(anchor.fraction - projectFraction(clicks.filter(click => click.tool === 'deepConnect')[index].world)) <= anchorTolerance + 1e-9,
      `DeepConnect anchor ${index + 1} differs from the integer-pixel source-segment hit by more than one pixel.`));
    const expectedAnchorPoints = deepSource.anchors.map(anchor => ({
      x: segment.start.x + (segment.end.x - segment.start.x) * anchor.fraction,
      y: segment.start.y + (segment.end.y - segment.start.y) * anchor.fraction,
    }));
    expectedAnchorPoints.forEach((point, index) => closePoint(deepPath.coordinates[index], point,
      `DeepConnect resolved path omits ordered source anchor ${index + 1}`, 1e-9));
    const renderedDeepPreview = await assertRenderedPath(evaluate, deepPath);
    report.geometryTrace.deepConnect.preview = { operationID: deepPreview.entry.operationId,
      pathSHA256: deepPath.pathSHA256, sourceRevision: deepPath.sourceRevision,
      draftIdentity: deepPath.draftIdentity, sourceFID: deepSource.featureFID,
      anchorCount: deepSource.anchors.length, coordinateCount: deepPath.coordinates.length,
      renderedVertexCount: renderedDeepPreview.points.length };
    await saveScreenshot(command, output, 'geometry-trace-deep-connect-preview.png');

    const deepEditStart = (await observerItems(evaluate)).length;
    const lastAnchorScreen = await screenPointForWorld(evaluate, anchorsWorld.at(-1));
    const finishScreen = { x: lastAnchorScreen.x + 1, y: lastAnchorScreen.y + 1 };
    await dispatchClick(command, finishScreen, 2);
    report.geometryTrace.deepConnect.finishGesture = {
      kind: 'doubleClick', screen: finishScreen, jitterPixels: { x: 1, y: 1 },
    };
    const deepFinalPreview = await waitOperation({ evaluate, method: 'topology.mapCommand.preview', startIndex: deepEditStart,
      predicate: parameters => mapCommand(parameters)?.action === 'deepConnect' &&
        mapCommand(parameters)?.deepConnectSource?.anchors?.length === 3 &&
        parameters?.mapRequestContext?.expectedDraftIdentity === smartReceipt.completion.draftIdentity });
    const deepFinalPath = assertPreviewOwner(deepFinalPreview, smartReceipt, mapCapture, geometryRevision, 'deepConnect');
    assert.equal(deepFinalPath.pathSHA256, deepPath.pathSHA256,
      'DeepConnect final preview differs from the last rendered path preview.');
    const deepEdit = await waitOperation({ evaluate, method: 'topology.edit', startIndex: deepEditStart,
      predicate: parameters => mapCommand(parameters)?.action === 'deepConnect' &&
        sameHash(mapCommand(parameters)?.expectedPathSHA256, deepFinalPath.pathSHA256) &&
        mapCommand(parameters)?.deepConnectSource?.anchors?.length === 3 });
    const deepReceipt = deepEdit.terminal.result;
    assert.equal(accepted((await observerItems(evaluate)).slice(deepEditStart), 'topology.edit',
      parameters => mapCommand(parameters)?.action === 'deepConnect').length, 1,
    'DeepConnect double-click must submit one topology mutation without an extra jitter anchor.');
    assert.equal(deepEdit.entry.parameters.mapRequestContext?.expectedDraftIdentity, smartReceipt.completion.draftIdentity);
    assert.equal(deepReceipt?.mapRequestContext?.mapID, mapCapture.mapID);
    assert.equal(deepReceipt?.mapRequestContext?.contextToken, mapCapture.contextToken);
    assert.equal(deepReceipt?.mapRequestContext?.expectedDraftIdentity, smartReceipt.completion.draftIdentity);
    assert.equal(deepReceipt?.isDirty, true);
    assert.notEqual(deepReceipt?.completion?.draftIdentity, smartReceipt.completion.draftIdentity);
    const deepArcFID = connectionArcFID(deepPath, deepReceipt, 'DeepConnect');
    const deepArcPath = pathForFeature(deepReceipt, 'arc', deepArcFID);
    assertPathIncluded(deepArcPath, deepPath.coordinates, Math.max(viewport.mapCellSize, 1e-9),
      'The DeepConnect Arc');
    report.geometryTrace.deepConnect.completion = { operationID: deepEdit.entry.operationId,
      newArcFID: deepArcFID, vertexCount: deepArcPath.length,
      previousDraftIdentity: smartReceipt.completion.draftIdentity,
      nextDraftIdentity: deepReceipt.completion.draftIdentity, dirty: deepReceipt.isDirty };
    checks.push('three ordered anchors on one exact topology Arc produced a read-only Host path preview; a slightly jittered double-click committed one Arc with the same three anchors into the next unsaved draft');

    const finalProjectSHA256 = sha(await readFile(fixture.projectPath));
    const finalGeometrySHA256 = sha(await readFile(fixture.geometryPath));
    assert.ok(sameHash(finalProjectSHA256, initialProjectSHA256), 'Trace acceptance changed the source .ras project.');
    assert.ok(sameHash(finalGeometrySHA256, initialGeometrySHA256), 'Trace acceptance wrote the unsaved topology draft to the Geometry HDF.');
    const finalItems = await observerItems(evaluate);
    const traceItems = finalItems.filter(item => item.kind === 'accepted' && item.parameters?.mutation?.mapCommand &&
      ['smartAdd', 'deepConnect'].includes(item.parameters.mutation.mapCommand.action));
    assert.equal(traceItems.filter(item => item.operationMethod === 'topology.edit').length, 2,
      'The trace interaction must contain exactly one SmartAdd and one DeepConnect edit.');
    assert.ok(!finalItems.some(item => item.kind === 'accepted' && item.operationMethod === 'model.save'),
      'Trace acceptance must leave the parent Geometry unsaved.');
    report.geometryTrace.finalReadback = { projectSHA256Before: initialProjectSHA256,
      projectSHA256After: finalProjectSHA256, geometrySHA256Before: initialGeometrySHA256,
      geometrySHA256After: finalGeometrySHA256, sourceFilesUnchanged: true,
      mapID: mapCapture.mapID, contextToken: mapCapture.contextToken,
      geometryRevision, finalDraftIdentity: deepReceipt.completion.draftIdentity,
      previewOperationCount: traceItems.filter(item => item.operationMethod === 'topology.mapCommand.preview').length,
      mutationCount: traceItems.filter(item => item.operationMethod === 'topology.edit').length,
      clickedWaypointsAndAnchors: clicks };
    report.geometryTrace.state = 'passed';
    checks.push('verified unchanged source .ras/HDF bytes and no parent save after both real trace previews and draft mutations');
    await saveScreenshot(command, output, 'geometry-trace-final-unsaved-draft.png');
    return report.geometryTrace;
  } catch (error) {
    report.geometryTrace.state = 'failed';
    report.geometryTrace.error = error instanceof Error ? error.message : String(error);
    report.geometryTrace.operations = traceOperationSummary(await observerItems(evaluate).catch(() => []));
    try {
      report.geometryTrace.uiAtFailure = await evaluate(`(()=>({activeFeature:document.querySelector('.ras-feature-nav button[aria-current=page]')?.textContent.trim()??null,` +
        `activeTool:document.querySelector('[data-testid=geometry-topology-canvas]')?.dataset.activeTool??null,` +
        `previewStatus:document.querySelector('[data-testid=geometry-topology-preview-status]')?.textContent.trim()??null,` +
        `toolNotice:document.querySelector('[data-testid=geometry-topology-tool-notice]')?.textContent.trim()??null,` +
        `smartAddStatus:document.querySelector('[data-testid=geometry-topology-smart-add-draft-status]')?.textContent.trim()??null,` +
        `deepConnectStatus:document.querySelector('[data-testid=geometry-topology-deep-connect-draft-status]')?.textContent.trim()??null,` +
        `preview:document.querySelector('[data-testid=geometry-topology-path-preview]')?.dataset??null,` +
        `canvases:document.querySelectorAll('[data-testid=geometry-topology-canvas]').length}))()`);
    } catch (diagnosticError) {
      report.geometryTrace.uiDiagnosticCaptureError = diagnosticError instanceof Error ? diagnosticError.message : String(diagnosticError);
    }
    throw error;
  }
}
