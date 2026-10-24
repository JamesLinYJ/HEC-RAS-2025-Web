import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile, realpath, writeFile } from 'node:fs/promises';
import path from 'node:path';

const observerName = '__hecGeometryMapObserver';
const sha = bytes => createHash('sha256').update(bytes).digest('hex').toUpperCase();
const sameSHA = (left, right) => String(left).toUpperCase() === String(right).toUpperCase();
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));

/**
 * Main-world observer for the exact accepted requests and terminal operation
 * pages used by the Geometry map acceptance. It only parses cloned Responses;
 * the UI always receives the original Response immediately.
 */
export const geometryMapFetchObserverSource = String.raw`(() => {
  if (window.__hecGeometryMapObserver?.version === 1) return;
  const methods = new Set([
    'project.open', 'project.tree.load', 'catalog.tree.load', 'model.load', 'topology.load',
    'map.geometry.load', 'topology.spatialSelect', 'topology.edit',
  ]);
  const maximumRecords = 240;
  const trace = { version: 1, items: [], dropped: 0 };
  const watchedOperations = new Set();
  const pendingTerminals = new Map();
  const append = item => {
    if (trace.items.length < maximumRecords) trace.items.push(item);
    else trace.dropped++;
  };
  const parseSubmission = raw => {
    try {
      const body = JSON.parse(raw);
      if (!methods.has(body.method)) return null;
      return { method: body.method, parameters: body.parameters ?? {} };
    } catch { return null; }
  };
  const originalFetch = window.fetch.bind(window);
  Object.defineProperty(window, '__hecGeometryMapObserver', {
    configurable: false, enumerable: false, value: trace,
  });
  window.fetch = async (...args) => {
    const input = args[0];
    const init = args[1] ?? {};
    const request = input instanceof Request ? input : null;
    let url;
    try { url = new URL(request?.url ?? String(input), location.href); }
    catch { return await originalFetch(...args); }
    const method = String(init.method ?? request?.method ?? 'GET').toUpperCase();
    const operationsPath = url.pathname.replace(/\/$/, '');
    if (operationsPath !== '/api/operations' && !operationsPath.startsWith('/api/operations/')) {
      return await originalFetch(...args);
    }

    let submission = null;
    let requestBodyPromise = null;
    if (method === 'POST' && operationsPath === '/api/operations') {
      if (typeof init.body === 'string') submission = parseSubmission(init.body);
      else if (request) requestBodyPromise = request.clone().text().then(parseSubmission).catch(() => null);
    }

    const response = await originalFetch(...args);
    const acceptedResponse = method === 'POST' && operationsPath === '/api/operations' ? response.clone() : null;
    const observeAccepted = acceptedSubmission => {
      if (!acceptedSubmission || !acceptedResponse) return;
      void acceptedResponse.json().then(payload => {
        if (!response.ok || typeof payload?.id !== 'string') return;
        const operationId = payload.id;
        watchedOperations.add(operationId);
        append({ kind: 'accepted', operationMethod: acceptedSubmission.method, method, url: url.pathname,
          status: response.status, operationId, parameters: acceptedSubmission.parameters });
        const pending = pendingTerminals.get(operationId);
        if (pending) {
          for (const item of pending) append(item);
          pendingTerminals.delete(operationId);
        }
      }).catch(() => {});
    };
    if (submission) observeAccepted(submission);
    else if (requestBodyPromise) void requestBodyPromise.then(observeAccepted);

    if (method === 'GET') {
      const match = /^\/api\/operations\/([^/]+)$/.exec(operationsPath);
      const operationId = match ? decodeURIComponent(match[1]) : null;
      if (operationId) {
        void response.clone().json().then(payload => {
          const state = String(payload?.state ?? '').toLowerCase();
          if (!['completed', 'failed', 'canceled', 'cancelled'].includes(state) || payload?.hasMore !== false) return;
          const terminal = { kind: 'terminal', method, url: url.pathname, status: response.status,
            operationId, state, hasMore: payload.hasMore, error: payload.error ?? null, result: payload.result ?? null };
          if (watchedOperations.has(operationId)) append(terminal);
          else {
            if (pendingTerminals.size >= 32) pendingTerminals.delete(pendingTerminals.keys().next().value);
            if (!pendingTerminals.has(operationId)) pendingTerminals.set(operationId, []);
            const items = pendingTerminals.get(operationId);
            if (items.length < 3) items.push(terminal);
          }
        }).catch(() => {});
      }
    }
    return response;
  };
})()`;

function assertWithin(root, candidate, label) {
  const relative = path.relative(root, candidate);
  assert.ok(relative && relative !== '..' && !relative.startsWith(`..${path.sep}`) && !path.isAbsolute(relative),
    `${label} must be inside the geometry fixture directory.`);
}

/** Validates the dedicated geometry-topology HTTP manifest without creating data. */
export async function validateGeometryMapManifest(manifest) {
  assert.equal(manifest?.schema, 'hecras-geometry-topology-map-http-e2e/v1');
  assert.ok(typeof manifest.fixtureDirectory === 'string' && path.isAbsolute(manifest.fixtureDirectory));
  assert.ok(typeof manifest.projectPath === 'string' && path.isAbsolute(manifest.projectPath));
  assert.ok(typeof manifest.geometryPath === 'string' && path.isAbsolute(manifest.geometryPath));
  const fixtureRoot = await realpath(manifest.fixtureDirectory);
  const projectPath = await realpath(manifest.projectPath);
  const geometryPath = await realpath(manifest.geometryPath);
  assertWithin(fixtureRoot, projectPath, 'Project fixture');
  assertWithin(fixtureRoot, geometryPath, 'Geometry fixture');
  const projectSHA256 = sha(await readFile(projectPath));
  const geometrySHA256 = sha(await readFile(geometryPath));
  assert.ok(sameSHA(projectSHA256, manifest.projectRevision), 'The .ras fixture does not match its manifest revision.');
  assert.ok(sameSHA(geometrySHA256, manifest.savedGeometrySHA256), 'The Geometry HDF does not match the saved manifest revision.');
  for (const key of [
    'fixtureTopologyPrepared', 'savedMapCatalogReadback', 'mapGeometryContextTokenReadback',
    'mapGeometryTopologySourceReadback', 'spatialSelectionStableIDReadback', 'moveNodeDraftReadback',
    'sourceHdfUnchangedBeforeSave', 'originalFixtureUnchanged',
  ]) assert.equal(manifest[key], true, `Geometry map manifest is missing successful preparation evidence: ${key}`);
  assert.equal(manifest.map?.mapType, 'Plan');
  assert.ok(typeof manifest.map?.mapName === 'string' && manifest.map.mapName.length > 0);
  assert.ok(Number.isInteger(manifest.map?.mapIndex) && manifest.map.mapIndex >= 0);
  assert.ok(Number.isInteger(manifest.node?.selectedNodeFID) && manifest.node.selectedNodeFID >= 0);
  assert.ok(Number.isFinite(manifest.node?.savedX) && Number.isFinite(manifest.node?.savedY));
  return { fixtureRoot, projectPath, geometryPath, projectSHA256, geometrySHA256 };
}

function catalogMatches(roots, assetPath, parentPath = []) {
  const matches = [];
  for (const node of roots ?? []) {
    const nodePath = [...parentPath, node.name];
    if (typeof node.assetID === 'string' && path.resolve(node.assetID) === assetPath) {
      matches.push({ node, treePath: nodePath });
    }
    matches.push(...catalogMatches(node.children, assetPath, nodePath));
  }
  return matches;
}

function stableJSON(value) {
  if (Array.isArray(value)) return `[${value.map(stableJSON).join(',')}]`;
  if (value && typeof value === 'object') {
    return `{${Object.keys(value).sort().map(key => `${JSON.stringify(key)}:${stableJSON(value[key])}`).join(',')}}`;
  }
  return JSON.stringify(value);
}

export function buildGeometryTreeTargetExpression(treePath) {
  return `(()=>{const expected=${JSON.stringify(treePath)};` +
    `const matches=Array.from(document.querySelectorAll('.project-tree-node-name')).map(label=>{` +
    `const row=label.closest('.project-tree-row');const path=[label.getAttribute('title')??''];` +
    `let container=row?.parentElement;while(container?.classList.contains('project-tree-children')){` +
    `const parentRow=container.previousElementSibling;if(!parentRow?.classList.contains('project-tree-row'))break;` +
    `path.unshift(parentRow.querySelector('.project-tree-node-name')?.getAttribute('title')??'');container=parentRow.parentElement;}` +
    `return {label,path,kind:row?.querySelector('.project-tree-source-kind')?.textContent.trim()??null,button:label.closest('button.project-tree-node')};})` +
    `.filter(item=>JSON.stringify(item.path)===JSON.stringify(expected)&&item.kind==='几何');` +
    `if(matches.length!==1)throw Error('Expected one exact Geometry tree row; found '+matches.length);` +
    `const button=matches[0].button;if(!button)throw Error('Geometry tree item has no action button');` +
    `button.scrollIntoView({block:'center'});const rect=button.getBoundingClientRect();` +
    `return {x:rect.left+rect.width/2,y:rect.top+rect.height/2};})()`;
}

function closeCoordinate(actual, expected, message, epsilon = 1e-8) {
  assert.ok(actual && Number.isFinite(actual.x) && Number.isFinite(actual.y), `${message}: coordinate is missing.`);
  assert.ok(Math.abs(actual.x - expected.x) <= epsilon && Math.abs(actual.y - expected.y) <= epsilon,
    `${message}: actual=${JSON.stringify(actual)} expected=${JSON.stringify(expected)}`);
}

function mapNodeCoordinate(snapshot, fid) {
  const feature = snapshot?.map?.features?.find(item => item.featureKind === 'node' && item.fid === fid);
  const point = feature?.paths?.find(points => points.length > 0)?.[0];
  assert.ok(point && Number.isFinite(point.x) && Number.isFinite(point.y),
    'Topology snapshot omitted the selected source node coordinate.');
  return { x: point.x, y: point.y };
}

function safeOperationSummary(items) {
  const accepted = new Map(items.filter(item => item.kind === 'accepted').map(item => [item.operationId, item]));
  return items.flatMap(item => {
    if (item.kind !== 'terminal') return [];
    const request = accepted.get(item.operationId);
    if (!request) return [];
    const parameters = request.parameters ?? {};
    const mutation = parameters.mutation ?? {};
    const command = mutation.mapCommand ?? null;
    const result = item.result ?? {};
    return [{
      method: request.operationMethod,
      operationID: item.operationId,
      acceptedStatus: request.status,
      terminalState: item.state,
      hasMore: item.hasMore,
      requestedMapID: parameters.map?.mapID ?? parameters.mapID ?? parameters.mapRequestContext?.mapID ?? null,
      geometryRevision: result.geometryRevision ?? result.mapRequestContext?.geometryRevision ?? null,
      sourceRevision: result.revision ?? result.mapRequestContext?.expectedDraftIdentity ?? null,
      draftState: result.draftState ?? null,
      draftIdentity: result.completion?.draftIdentity ?? result.mapRequestContext?.expectedDraftIdentity ?? null,
      contextToken: result.contextToken ?? result.mapRequestContext?.contextToken ?? null,
      featureKind: command?.featureKind ?? null,
      action: command?.action ?? null,
      featureIDs: command?.featureIDs ?? null,
      nodeFID: command?.nodeFID ?? null,
      coordinates: command?.coordinates ?? null,
      mapCellSize: command?.mapCellSize ?? null,
      selectionCount: Array.isArray(result.selections) ? result.selections.length : null,
      resultNodeCount: Array.isArray(result.map?.features) ? result.map.features.filter(feature => feature.featureKind === 'node').length : null,
      errorCode: item.error?.code ?? null,
    }];
  });
}

/** Real source-backed Geometry map acceptance shared by hidden Chrome and Electron adapters. */
export async function checkGeometryMapUI({ manifest, output, evaluate, waitFor, command, navigate, report }) {
  const fixture = await validateGeometryMapManifest(manifest);
  const geometryName = path.basename(fixture.geometryPath, path.extname(fixture.geometryPath));
  const nodeFID = manifest.node.selectedNodeFID;
  const initialProjectSHA256 = fixture.projectSHA256;
  const initialGeometrySHA256 = fixture.geometrySHA256;
  const checks = [];
  let mapGeometry;
  let topologyLoad;
  let movedTopology;
  let arcTopology;
  report.geometryTopology = {
    state: 'running',
    manifestSchema: manifest.schema,
    projectRevision: manifest.projectRevision,
    geometryRevision: manifest.savedGeometrySHA256,
    geometryName,
    selectedNodeFID: nodeFID,
    fixtureFiles: { projectSHA256: initialProjectSHA256, geometrySHA256: initialGeometrySHA256 },
    checks,
  };

  async function observerItems() {
    return await evaluate(`window.${observerName}?.items ?? []`);
  }

  async function waitForOperation(method, startIndex, predicate = () => true, timeoutMs = 150_000) {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
      const items = await observerItems();
      const accepted = items.slice(startIndex).findLast(item => item.kind === 'accepted' && item.operationMethod === method && predicate(item.parameters));
      if (accepted) {
        const terminal = items.findLast(item => item.kind === 'terminal' && item.operationId === accepted.operationId);
        if (terminal) {
          assert.equal(accepted.status, 202, `${method} was not accepted by the real server.`);
          assert.equal(terminal.hasMore, false, `${method} was not read through its terminal page.`);
          assert.equal(terminal.state, 'completed', `${method} failed: ${JSON.stringify(terminal.error)}`);
          return { entry: accepted, accepted: { id: accepted.operationId }, terminal };
        }
      }
      await delay(100);
    }
    throw new Error(`Timed out waiting for ${method} to reach a terminal operation receipt.`);
  }

  async function clickByText(selector, value, label = value) {
    await waitFor(`Boolean(Array.from(document.querySelectorAll(${JSON.stringify(selector)})` +
      `).some(candidate=>candidate.textContent.trim()===${JSON.stringify(value)}&&!candidate.disabled))`,
    `operable ${label}`, 60_000);
    await evaluate(`(()=>{const element=Array.from(document.querySelectorAll(${JSON.stringify(selector)})` +
      `).find(candidate=>candidate.textContent.trim()===${JSON.stringify(value)});` +
      `if(!element)throw Error(${JSON.stringify('Missing ' + label)});if(element.disabled)throw Error(${JSON.stringify('Disabled ' + label)});element.click()})()`);
  }

  async function setInputValue(selector, value) {
    await evaluate(`(()=>{const input=document.querySelector(${JSON.stringify(selector)});` +
      `if(!input)throw Error(${JSON.stringify('Missing ' + selector)});` +
      `Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(input,${JSON.stringify(value)});` +
      `input.dispatchEvent(new Event('input',{bubbles:true}));input.dispatchEvent(new Event('change',{bubbles:true}));})()`);
  }

  async function openProject() {
    await navigate('project');
    await waitFor("Boolean(document.querySelector('.ras-statusbar')?.innerText.includes('服务已连接'))", 'server connection');
    await clickByText('.project-feature-toolbar button', '打开工程');
    await waitFor("Boolean(document.querySelector('.project-open-modal'))", 'geometry project open dialog');
    await waitFor('Boolean(document.querySelector(".project-open-path-row input"))', 'geometry project open dialog');
    await setInputValue('.project-open-path-row input', fixture.projectPath);
    await waitFor('!document.querySelector(".project-open-path-row button").disabled', 'geometry project path accepted');
    const startIndex = (await observerItems()).length;
    await evaluate('document.querySelector(".project-open-path-row button").click()');
    const projectOpen = await waitForOperation('project.open', startIndex,
      parameters => path.resolve(parameters?.projectPath ?? '') === fixture.projectPath);
    assert.equal(path.resolve(projectOpen.terminal.result?.projectPath ?? ''), fixture.projectPath);
    assert.ok(sameSHA(projectOpen.terminal.result?.revision, manifest.projectRevision), 'Project-open receipt does not match the manifest .ras revision.');
    await waitFor('!document.querySelector(".project-open-modal")&&document.querySelectorAll(".project-tree-node").length>0',
      'geometry project tree loaded', 60_000);
    await clickByText('.project-tree-mode-switch button', '目录');
    await waitFor("document.querySelector('.project-tree-mode-switch button[aria-selected=true]')?.textContent.trim()==='目录'",
      'Catalog tree mode selected');
    await clickByText('.project-tree-categories button', '全部');
    await clickByText('.project-tree-tool-button', '全部展开');
    const catalog = await waitForOperation('catalog.tree.load', startIndex,
      parameters => path.resolve(parameters?.projectPath ?? '') === fixture.projectPath && parameters?.mode === 'Catalog');
    const matches = catalogMatches(catalog.terminal.result?.roots, fixture.geometryPath)
      .filter(match => match.node.name === geometryName && match.node.sourceClass === 'ObservableGeometry');
    assert.equal(matches.length, 1, 'Catalog must uniquely identify the exact Geometry locator and source class.');
    return { projectOpen, catalog, geometryTreePath: matches[0].treePath };
  }

  async function clickTreeGeometry(treePath) {
    const target = await evaluate(buildGeometryTreeTargetExpression(treePath));
    await command('Input.dispatchMouseEvent', { type: 'mousePressed', x: target.x, y: target.y,
      button: 'left', buttons: 1, clickCount: 1 });
    await command('Input.dispatchMouseEvent', { type: 'mouseReleased', x: target.x, y: target.y,
      button: 'left', buttons: 0, clickCount: 1 });
    await delay(90);
    await command('Input.dispatchMouseEvent', { type: 'mousePressed', x: target.x, y: target.y,
      button: 'left', buttons: 1, clickCount: 2 });
    await command('Input.dispatchMouseEvent', { type: 'mouseReleased', x: target.x, y: target.y,
      button: 'left', buttons: 0, clickCount: 2 });
  }

  async function selectSavedMap() {
    const selector = '[data-testid="geometry-topology-map-workspace"] header select';
    await waitFor(`Boolean(Array.from(document.querySelector(${JSON.stringify(selector)})?.options ?? []).length)`,
      'exact saved Geometry map selector');
    const option = await evaluate(`(()=>{const select=document.querySelector(${JSON.stringify(selector)});` +
      `const matches=Array.from(select.options).filter(option=>{try{const tuple=JSON.parse(option.value);` +
      `return tuple?.[1]?.assetID===${JSON.stringify(fixture.geometryPath)}&&` +
      `option.textContent.includes(${JSON.stringify(manifest.map.mapName)})&&` +
      `option.textContent.includes(${JSON.stringify(geometryName)})&&` +
      `option.textContent.includes(${JSON.stringify(manifest.map.mapType)});}catch{return false}});` +
      `if(matches.length!==1)throw Error('Expected one saved map option with the exact Geometry locator; found '+matches.length);` +
      `const tuple=JSON.parse(matches[0].value);return {value:matches[0].value,label:matches[0].textContent,mapID:tuple[0],geometryLocator:tuple[1]};})()`);
    assert.ok(typeof option.mapID === 'string' && option.mapID.length > 0);
    const currentValue = await evaluate(`document.querySelector(${JSON.stringify(selector)}).value`);
    const changed = currentValue !== option.value;
    if (changed) {
      await evaluate(`(()=>{const select=document.querySelector(${JSON.stringify(selector)});` +
        `Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype,'value').set.call(select,${JSON.stringify(option.value)});` +
        `select.dispatchEvent(new Event('change',{bubbles:true}));})()`);
      await waitFor(`document.querySelector(${JSON.stringify(selector)}).value===${JSON.stringify(option.value)}`,
        'selected exact saved Geometry map');
    }
    return { option, changed };
  }

  async function nodeScreenPoint(fid) {
    return await evaluate(`(()=>{const svg=document.querySelector('[data-testid="geometry-topology-canvas"]');` +
      `if(!svg)throw Error('Missing topology SVG');const group=svg.querySelector(${JSON.stringify(`g[data-feature-kind=node][data-fid="${fid}"]`)});` +
      `const circle=group?.querySelector('circle');if(!circle)throw Error('Missing source node circle for FID '+${JSON.stringify(fid)});` +
      `const matrix=svg.getScreenCTM();if(!matrix)throw Error('SVG has no screen transform');` +
      `const worldX=Number(circle.getAttribute('cx'));const svgY=Number(circle.getAttribute('cy'));` +
      `const screen=new DOMPoint(worldX,svgY).matrixTransform(matrix);const rect=svg.getBoundingClientRect();` +
      `const viewBox=svg.viewBox.baseVal;return {x:screen.x,y:screen.y,worldX,worldY:-svgY,` +
      `rect:{left:rect.left,top:rect.top,right:rect.right,bottom:rect.bottom,width:rect.width,height:rect.height},` +
      `mapCellSize:viewBox.width/rect.width};})()`);
  }

  async function dispatchClick(point, clickCount = 1) {
    await command('Input.dispatchMouseEvent', { type: 'mousePressed', x: point.x, y: point.y,
      button: 'left', buttons: 1, clickCount });
    await command('Input.dispatchMouseEvent', { type: 'mouseReleased', x: point.x, y: point.y,
      button: 'left', buttons: 0, clickCount });
  }

  async function dispatchDrag(start, end) {
    await command('Input.dispatchMouseEvent', { type: 'mousePressed', x: start.x, y: start.y,
      button: 'left', buttons: 1, clickCount: 1 });
    for (let index = 1; index <= 4; index++) {
      const ratio = index / 4;
      await command('Input.dispatchMouseEvent', { type: 'mouseMoved',
        x: start.x + (end.x - start.x) * ratio, y: start.y + (end.y - start.y) * ratio,
        button: 'left', buttons: 1 });
      await delay(45);
    }
    await command('Input.dispatchMouseEvent', { type: 'mouseReleased', x: end.x, y: end.y,
      button: 'left', buttons: 0, clickCount: 1 });
  }

  async function saveScreenshot(name) {
    if (!output) return;
    const screenshot = await command('Page.captureScreenshot', { format: 'png' });
    await writeFile(path.join(output, name), Buffer.from(screenshot.data, 'base64'));
  }

  try {
    await evaluate(geometryMapFetchObserverSource);
    await waitFor(`Boolean(window.${observerName}?.version===1)`, 'geometry operation observer installed');
    const openedProject = await openProject();
    const geometryTreePath = openedProject.geometryTreePath;
    const modelLoadStart = (await observerItems()).length;
    await clickTreeGeometry(geometryTreePath);
    await waitFor(`document.querySelector('.geometry-page .geometry-heading h1')?.textContent.trim()===${JSON.stringify(geometryName)}`,
      'exact Geometry model editor opened');
    const modelLoad = await waitForOperation('model.load', modelLoadStart,
      parameters => JSON.stringify(parameters ?? {}).includes(fixture.geometryPath));
    assert.ok(sameSHA(modelLoad.terminal.result?.revision, manifest.savedGeometrySHA256),
      'The loaded Geometry revision does not match its saved HDF.');
    topologyLoad = await waitForOperation('topology.load', modelLoadStart,
      parameters => JSON.stringify(parameters ?? {}).includes(fixture.geometryPath));
    const initialNode = mapNodeCoordinate(topologyLoad.terminal.result, nodeFID);
    closeCoordinate(initialNode, { x: manifest.node.savedX, y: manifest.node.savedY },
      'The loaded topology node differs from the saved HDF coordinate.');
    assert.ok(topologyLoad.terminal.result?.completion?.draftIdentity, 'Topology load omitted the current draft identity.');
    await waitFor('Boolean(document.querySelector(".geometry-page")&&!document.querySelector(".geometry-page .geometry-toolbar .geometry-loading"))',
      'Geometry model and topology loads settled', 60_000);
    checks.push('opened the exact Geometry HDF through the project tree and matched its saved node coordinate and revision');
    await saveScreenshot('geometry-01-loaded.png');

    await clickByText('.geometry-tabstrip button', '节点');
    await waitFor('Boolean(document.querySelector("[data-testid=geometry-topology-map-workspace]"))', 'Geometry topology map workspace');
    const selection = await selectSavedMap();
    await evaluate('document.querySelector("[data-testid=geometry-topology-map-workspace]").scrollIntoView({block:"center"})');
    await waitFor(`Boolean(document.querySelector('[data-testid="geometry-topology-canvas"] g[data-feature-kind="node"][data-fid="${nodeFID}"]'))`,
      'source node rendered on topology SVG');
    mapGeometry = await waitForOperation('map.geometry.load', modelLoadStart, parameters =>
      parameters?.map?.mapID === selection.option.mapID &&
      parameters?.map?.mapIndex === manifest.map.mapIndex &&
      parameters?.map?.mapName === manifest.map.mapName &&
      parameters?.map?.mapType === manifest.map.mapType &&
      stableJSON(parameters?.geometryNode) === stableJSON(selection.option.geometryLocator));
    assert.ok(mapGeometry.terminal.result?.contextToken, 'map.geometry.load did not return the current map context token.');
    assert.ok(sameSHA(mapGeometry.terminal.result?.geometryRevision, manifest.savedGeometrySHA256),
      'map.geometry.load returned a different Geometry source revision.');
    assert.equal(mapGeometry.terminal.result?.mapID, selection.option.mapID);
    assert.equal(mapGeometry.entry.parameters?.map?.mapIndex, manifest.map.mapIndex,
      'The selected UI map ID does not belong to the manifest saved-map index.');
    assert.equal(mapGeometry.entry.parameters?.map?.mapName, manifest.map.mapName);
    assert.equal(mapGeometry.entry.parameters?.map?.mapType, manifest.map.mapType);
    assert.equal(mapGeometry.terminal.result?.mapIndex, manifest.map.mapIndex);
    assert.equal(mapGeometry.terminal.result?.mapName, manifest.map.mapName);
    assert.equal(mapGeometry.terminal.result?.mapType, manifest.map.mapType);
    await waitFor('Boolean(document.querySelector("[data-testid=geometry-topology-map-status]")?.textContent&&!document.querySelector("[data-testid=geometry-topology-map-status]").textContent.includes("尚未就绪"))',
      'map capture accepted by the Geometry canvas');
    await delay(1200);
    const counts = await evaluate('({pages:document.querySelectorAll(".geometry-page").length,' +
      'panels:document.querySelectorAll("[data-testid=geometry-topology-map-workspace]").length,' +
      'canvases:document.querySelectorAll("[data-testid=geometry-topology-canvas]").length,' +
      'nodes:document.querySelectorAll("[data-testid=geometry-topology-canvas] g[data-feature-kind=node]").length})');
    assert.deepEqual(counts, { pages: 1, panels: 1, canvases: 1, nodes: 4 }, 'The saved map did not render one stable topology canvas.');
    await evaluate('document.querySelector("[data-testid=geometry-topology-canvas]").scrollIntoView({block:"center"})');
    const initialProjection = await nodeScreenPoint(nodeFID);
    closeCoordinate({ x: initialProjection.worldX, y: initialProjection.worldY }, initialNode,
      'The SVG node projection does not preserve Host source coordinates.');
    assert.ok(initialProjection.rect.width > 0 && initialProjection.rect.height > 0 && initialProjection.mapCellSize > 0);
    report.geometryTopology.mapCapture = {
      selectedMapLabel: selection.option.label,
      mapID: selection.option.mapID,
      mapIndex: mapGeometry.terminal.result.mapIndex,
      mapName: manifest.map.mapName,
      mapType: manifest.map.mapType,
      exactGeometryLocator: true,
      contextToken: mapGeometry.terminal.result.contextToken,
      geometryRevision: mapGeometry.terminal.result.geometryRevision,
      topologyRevision: topologyLoad.terminal.result.revision,
      draftIdentity: topologyLoad.terminal.result.completion.draftIdentity,
      canvas: counts,
      nodeFID,
      sourceCoordinate: initialNode,
      screenPoint: { x: initialProjection.x, y: initialProjection.y },
      mapCellSize: initialProjection.mapCellSize,
    };
    checks.push('selected the saved map by its dynamic mapID and exact Geometry locator; one SVG canvas projects the source node');
    await saveScreenshot('geometry-02-map-capture.png');

    const selectionStart = (await observerItems()).length;
    await dispatchClick({ x: initialProjection.x, y: initialProjection.y });
    const spatialSelection = await waitForOperation('topology.spatialSelect', selectionStart,
      parameters => parameters?.mapID === selection.option.mapID && parameters?.gesture === 'Click');
    const selectedNode = spatialSelection.terminal.result?.selections?.find(item =>
      item.featureKind === 'node' && item.fid === nodeFID);
    assert.ok(selectedNode, 'The Host spatial-selection receipt omitted the clicked source node FID.');
    report.geometryTopology.spatialSelection = {
      operationID: spatialSelection.entry.operationId,
      mapID: spatialSelection.terminal.result.mapID,
      contextToken: spatialSelection.terminal.result.contextToken,
      selections: [selectedNode],
    };
    checks.push('clicked the source SVG node and received its stable Host FID through topology.spatialSelect');

    const nodeScreen = await nodeScreenPoint(nodeFID);
    const start = { x: nodeScreen.x, y: nodeScreen.y };
    const end = {
      x: Math.min(nodeScreen.rect.right - 18, start.x + 20),
      y: Math.max(nodeScreen.rect.top + 18, start.y - 18),
    };
    assert.ok(end.x - start.x >= 8 && start.y - end.y >= 8, 'The source node drag target is clipped by the SVG viewport.');
    const projectedTarget = await evaluate(`(()=>{const svg=document.querySelector('[data-testid="geometry-topology-canvas"]');` +
      `const point=new DOMPoint(${end.x},${end.y}).matrixTransform(svg.getScreenCTM().inverse());return {x:point.x,y:-point.y};})()`);
    assert.ok(Number.isFinite(projectedTarget.x) && Number.isFinite(projectedTarget.y));
    const moveStart = (await observerItems()).length;
    await dispatchDrag(start, end);
    const move = await waitForOperation('topology.edit', moveStart, parameters => {
      const mapCommand = parameters?.mutation?.mapCommand;
      return parameters?.mutation?.operation === 'mapCommand' && mapCommand?.action === 'moveNode' && mapCommand?.nodeFID === nodeFID;
    });
    movedTopology = move.terminal.result;
    const moveCommand = move.entry.parameters.mutation.mapCommand;
    assert.equal(movedTopology?.revision, topologyLoad.terminal.result?.revision,
      'Map node editing changed the captured source revision.');
    assert.ok(movedTopology?.completion?.draftIdentity &&
      movedTopology.completion.draftIdentity !== topologyLoad.terminal.result.completion.draftIdentity,
    'The move receipt did not advance the owning topology draft identity.');
    closeCoordinate(moveCommand.coordinates?.[0], projectedTarget,
      'The submitted node coordinate differs from the CDP pointer target.', nodeScreen.mapCellSize * 0.001);
    closeCoordinate(mapNodeCoordinate(movedTopology, nodeFID), moveCommand.coordinates[0],
      'The Host topology receipt differs from the submitted node coordinate.');
    assert.equal(move.entry.parameters.mapRequestContext?.mapID, selection.option.mapID);
    assert.equal(move.entry.parameters.mapRequestContext?.contextToken, mapGeometry.terminal.result.contextToken);
    assert.equal(move.entry.parameters.mapRequestContext?.expectedDraftIdentity, topologyLoad.terminal.result.completion.draftIdentity);
    assert.equal(movedTopology?.mapRequestContext?.mapID, selection.option.mapID);
    assert.equal(movedTopology?.mapRequestContext?.contextToken, mapGeometry.terminal.result.contextToken);
    assert.equal(movedTopology?.mapRequestContext?.expectedDraftIdentity, topologyLoad.terminal.result.completion.draftIdentity);
    await waitFor('Boolean(document.querySelector(".geometry-header-actions .geometry-status-pill.is-dirty"))',
      'unsaved Geometry draft after node drag');
    report.geometryTopology.nodeMove = {
      operationID: move.entry.operationId,
      nodeFID: moveCommand.nodeFID,
      nodeFIDs: moveCommand.nodeFIDs,
      sourceRevision: movedTopology.revision,
      previousDraftIdentity: topologyLoad.terminal.result.completion.draftIdentity,
      nextDraftIdentity: movedTopology.completion.draftIdentity,
      screenStart: start,
      screenEnd: end,
      pointerTarget: projectedTarget,
      submittedCoordinate: moveCommand.coordinates[0],
      hostReadbackCoordinate: mapNodeCoordinate(movedTopology, nodeFID),
      mapCellSize: moveCommand.mapCellSize,
      requestMapContext: move.entry.parameters.mapRequestContext,
      receiptMapContext: movedTopology.mapRequestContext,
    };
    checks.push('dragged the source node through real mouse input; captured map token, draft identity, FID and coordinate match Host receipts');
    await saveScreenshot('geometry-03-node-move-draft.png');

    await clickByText('.geometry-tabstrip button', '弧段');
    await waitFor('Boolean(document.querySelector(".topology-map-arc-tools"))', 'arc map toolbar mounted');
    await evaluate('document.querySelector("[data-testid=geometry-topology-canvas]").scrollIntoView({block:"center"})');
    await clickByText('.topology-map-arc-tools button', '绘制弧段');
    const arcScreenPoints = await evaluate(`(()=>{const svg=document.querySelector('[data-testid="geometry-topology-canvas"]');` +
      `return [[1,1],[2,2],[3,1]].map(([x,y])=>{const point=new DOMPoint(x,-y).matrixTransform(svg.getScreenCTM());return {x:point.x,y:point.y};});})()`);
    assert.equal(arcScreenPoints.length, 3);
    const addStart = (await observerItems()).length;
    for (let index = 0; index < arcScreenPoints.length; index++) {
      await dispatchClick(arcScreenPoints[index]);
      await delay(index === arcScreenPoints.length - 1 ? 70 : 350);
    }
    const finalPoint = arcScreenPoints.at(-1);
    await dispatchClick({ x: finalPoint.x + 0.35, y: finalPoint.y + 0.35 }, 2);
    const addArc = await waitForOperation('topology.edit', addStart,
      parameters => parameters?.mutation?.mapCommand?.action === 'addArc');
    arcTopology = addArc.terminal.result;
    const addCommand = addArc.entry.parameters.mutation.mapCommand;
    assert.equal(addCommand.coordinates?.length, 3, 'Double-click subpixel jitter appended an extra arc endpoint.');
    assert.equal(addCommand.featureKind, 'arc');
    assert.equal(addArc.entry.parameters.mapRequestContext?.mapID, selection.option.mapID);
    assert.equal(addArc.entry.parameters.mapRequestContext?.contextToken, mapGeometry.terminal.result.contextToken);
    assert.equal(addArc.entry.parameters.mapRequestContext?.expectedDraftIdentity, movedTopology.completion.draftIdentity);
    assert.equal(arcTopology?.mapRequestContext?.expectedDraftIdentity, movedTopology.completion.draftIdentity);
    assert.equal(arcTopology?.isDirty, true);
    assert.deepEqual(await evaluate('({panels:document.querySelectorAll("[data-testid=geometry-topology-map-workspace]").length,' +
      'canvases:document.querySelectorAll("[data-testid=geometry-topology-canvas]").length})'), { panels: 1, canvases: 1 });
    report.geometryTopology.arcDraw = {
      operationID: addArc.entry.operationId,
      command: addCommand,
      terminalState: addArc.terminal.state,
      nextDraftIdentity: arcTopology?.completion?.draftIdentity,
      exactlyThreePointsAfterSubpixelDoubleClick: true,
    };
    checks.push('drew a three-point arc and double-clicked with subpixel jitter; Host accepted exactly three points in the same draft and canvas');
    await saveScreenshot('geometry-04-arc-draw-draft.png');

    const finalProjectSHA256 = sha(await readFile(fixture.projectPath));
    const finalGeometrySHA256 = sha(await readFile(fixture.geometryPath));
    assert.ok(sameSHA(finalProjectSHA256, initialProjectSHA256), 'The draft-only map interaction changed the .ras file.');
    assert.ok(sameSHA(finalGeometrySHA256, initialGeometrySHA256), 'The draft-only map interaction changed the Geometry HDF.');
    const items = await observerItems();
    const observedMethods = new Set(items.filter(item => item.kind === 'accepted').map(item => item.operationMethod));
    assert.ok(observedMethods.has('map.geometry.load') && observedMethods.has('topology.spatialSelect') && observedMethods.has('topology.edit'));
    report.geometryTopology.finalReadback = {
      projectSHA256Before: initialProjectSHA256,
      projectSHA256After: finalProjectSHA256,
      geometrySHA256Before: initialGeometrySHA256,
      geometrySHA256After: finalGeometrySHA256,
      sourceHDFUnchanged: true,
      noParentSaveRequested: !observedMethods.has('model.save'),
      mapID: selection.option.mapID,
      contextToken: mapGeometry.terminal.result.contextToken,
      sourceRevision: topologyLoad.terminal.result.revision,
      nodeDraftIdentity: movedTopology.completion.draftIdentity,
      arcDraftIdentity: arcTopology.completion?.draftIdentity ?? null,
    };
    report.geometryTopology.operations = safeOperationSummary(items);
    report.geometryTopology.observerDropped = (await evaluate(`window.${observerName}?.dropped ?? 0`)) ?? 0;
    report.geometryTopology.state = 'passed';
    checks.push('verified the accepted move and arc remain draft-only while the real .ras and Geometry HDF bytes are unchanged');
    await saveScreenshot('geometry-05-final-draft.png');
  } catch (error) {
    report.geometryTopology.state = 'failed';
    report.geometryTopology.error = error instanceof Error ? error.message : String(error);
    report.geometryTopology.operations = safeOperationSummary(await observerItems().catch(() => []));
    try {
      report.geometryTopology.uiAtFailure = await evaluate(`(()=>({activeFeature:document.querySelector('.ras-feature-nav button[aria-current=page]')?.textContent.trim()??null,` +
        `geometryTitle:document.querySelector('.geometry-page .geometry-heading h1')?.textContent.trim()??null,` +
        `mapStatus:document.querySelector('[data-testid=geometry-topology-map-status]')?.textContent.trim()??null,` +
        `canvas:!!document.querySelector('[data-testid=geometry-topology-canvas]'),` +
        `canvases:document.querySelectorAll('[data-testid=geometry-topology-canvas]').length,` +
        `status:Array.from(document.querySelectorAll('.geometry-status-pill')).map(node=>node.textContent.trim())}))()`);
    } catch (diagnosticError) {
      report.geometryTopology.uiDiagnosticCaptureError = diagnosticError instanceof Error ? diagnosticError.message : String(diagnosticError);
    }
    throw error;
  }
  return report.geometryTopology;
}
