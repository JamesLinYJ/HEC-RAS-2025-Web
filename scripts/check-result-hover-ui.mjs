import assert from 'node:assert/strict';
import path from 'node:path';
import { writeFile } from 'node:fs/promises';

const stableValue = (value) => {
  if (Array.isArray(value)) return value.map(stableValue);
  if (!value || typeof value !== 'object') return value;
  return Object.fromEntries(Object.keys(value).sort().map((key) => [key, stableValue(value[key])]));
};
const stableJSON = (value) => JSON.stringify(stableValue(value));
const sameRevision = (left, right) => String(left).toUpperCase() === String(right).toUpperCase();
const sourceValue = (source) => `${source.resultPath}\u0000${source.revision ?? source.resultRevision}`;
const entryValue = (entry) => entry.expectedValue ?? entry.value;
const valueTolerance = (entry, testCase) => entry.tolerance ?? testCase.tolerance ?? 5e-7;
const normalizedMapID = (value) => String(value ?? '').replace(/[{}]/g, '').toUpperCase();
const numericMatch = (left, right, tolerance = 0) => {
  if (left === null || left === undefined || right === null || right === undefined) return left === right;
  const a = Number(left); const b = Number(right);
  return Number.isFinite(a) && Number.isFinite(b) ? Math.abs(a - b) <= tolerance : String(left) === String(right);
};

function assertSource(actual, expected, label) {
  assert.equal(actual?.resultPath ?? actual?.sourcePath, expected.resultPath, `${label}: Result path differs.`);
  assert.ok(sameRevision(actual?.revision ?? actual?.resultRevision ?? actual?.sourceRevision,
    expected.revision ?? expected.resultRevision), `${label}: Result revision differs.`);
}

function matrixSourceList(value) {
  return value?.selectedSources ?? value?.sources ?? [];
}

function matrixEntryList(value) {
  return Array.isArray(value?.entries) ? value.entries : Array.isArray(value?.expectedEntries) ? value.expectedEntries : [];
}

/** Real React pointer handler, completed C# operation and independent HDF values. */
export async function checkResultHoverUI({ manifest, evaluate, waitFor, command, navigate, output, report }) {
  assert.equal(manifest?.schema, 'hecras-result-hover-e2e/v1', 'Use the server-generated Result hover fixture manifest.');
  assert.ok(manifest.projectPath && manifest.map?.mapID && Number.isInteger(manifest.map.mapIndex) &&
    manifest.map.mapName && manifest.map.mapType && manifest.nodes?.ordinaryResult?.locator && manifest.nodes?.multiResult?.locator,
    'The Result hover manifest is missing its project, map, or source-node locators.');
  assert.ok(Array.isArray(manifest.cases) && manifest.cases.length >= 6,
    'The Result hover manifest must cover ordinary Result and all five A/B MultiResult cases.');

  await navigate('map');
  const nodeRowSelector = (node) => {
    const locator = node.locator;
    assert.ok(locator?.rootName && locator.nodeType && Array.isArray(locator.nodePath),
      'The Result hover manifest needs exact rootName, nodeType, and nodePath locators for tree visibility.');
    return `.map-project-tree-section .map-tree-row[data-node-root=${JSON.stringify(locator.rootName)}]` +
      `[data-node-type=${JSON.stringify(locator.nodeType)}][data-node-path=${JSON.stringify(JSON.stringify(locator.nodePath))}]`;
  };
  const expectedMapTabText = String(manifest.map.mapName).trim();
  await waitFor(`(() => {const create=document.querySelector('.map-create-document');const tabs=Array.from(document.querySelectorAll('.map-document-tab-list [role=tab]'));const tab=tabs.find(item=>item.textContent.trim()===${JSON.stringify(expectedMapTabText)});const heading=document.querySelector('.map-canvas-head strong');return Boolean(create&&!create.disabled&&tab?.getAttribute('aria-selected')==='true'&&heading?.textContent.trim()===${JSON.stringify(expectedMapTabText)});})()`,
    `saved ${manifest.map.mapType} map document ${manifest.map.mapName} and its project catalog`, 90_000);

  const ensureTreeNodeRendered = async (node, label) => {
    const rowSelector = nodeRowSelector(node);
    const rowExpression = `document.querySelector(${JSON.stringify(rowSelector)})`;
    for (let pass = 0; pass < 32; pass++) {
      const state = await evaluate(`(() => ({target:Boolean(${rowExpression}),rows:document.querySelectorAll('.map-project-tree-section .map-tree-row').length,collapsed:document.querySelectorAll('.map-project-tree-section .map-tree-expand[aria-expanded=false]').length,error:document.querySelector('.map-project-tree-section .map-tree-load-error')?.textContent??null}))()`);
      if (state.target) return rowSelector;
      assert.equal(state.error, null, `${label} saved map layer tree failed to load: ${state.error ?? ''}`);
      assert.ok(state.rows > 0, `${label} saved map document loaded without any project tree roots.`);
      if (state.collapsed === 0) break;
      await evaluate("Array.from(document.querySelectorAll('.map-project-tree-section .map-tree-expand[aria-expanded=false]')).forEach(button=>button.click())");
      await waitFor(`Boolean(${rowExpression})||document.querySelectorAll('.map-project-tree-section .map-tree-row').length>${state.rows}`,
        `${label} saved map tree expansion`, 60_000);
    }
    await waitFor(`Boolean(${rowExpression})`, `${label} exact node in the loaded saved map tree`, 60_000);
    return rowSelector;
  };

  const visibility = new Map();
  const ensureVisible = async (node, label) => {
    const locatorKey = stableJSON(node.locator);
    const rowSelector = await ensureTreeNodeRendered(node, label);
    const rowExpression = `document.querySelector(${JSON.stringify(rowSelector)})`;
    const checked = await evaluate(`(() => {const row=${rowExpression};const input=row?.querySelector('input[type=checkbox]');return input?{checked:input.checked,disabled:input.disabled}:null;})()`);
    assert.ok(checked, `The ${label} Result layer is missing from the actual project tree: ${locatorKey}`);
    if (!visibility.has(locatorKey)) visibility.set(locatorKey, checked.checked);
    if (!checked.checked) {
      await evaluate(`(() => {const row=${rowExpression};const input=row?.querySelector('input[type=checkbox]');if(!input||input.disabled)throw new Error('Result layer cannot be shown');input.click();})()`);
      await waitFor(`(() => {const row=${rowExpression};const input=row?.querySelector('input[type=checkbox]');return input?.checked===true&&!input.disabled;})()`, `${label} Result visibility`, 60_000);
    }
    return locatorKey;
  };
  const ordinaryLocatorKey = await ensureVisible(manifest.nodes.ordinaryResult, 'ordinary Result');
  const multiLocatorKey = await ensureVisible(manifest.nodes.multiResult, 'MultiResult');
  const ordinaryRowSelector = nodeRowSelector(manifest.nodes.ordinaryResult);
  const multiRowSelector = nodeRowSelector(manifest.nodes.multiResult);
  const nodeForCase = (testCase) => {
    const selector = testCase.node ?? testCase.nodeKey ?? testCase.nodeKind;
    if (selector === 'ordinaryResult' || selector === 'Result') return manifest.nodes.ordinaryResult;
    if (selector === 'multiResult' || selector === 'MultiResult') return manifest.nodes.multiResult;
    assert.fail(`Unknown Result hover case node: ${selector}`);
  };

  const waitTooltipCleared = async (label) => {
    await waitFor("!document.querySelector('[data-testid=map-hover-tooltip]')", `${label} clears the stale tooltip`, 15_000);
  };
  let currentNodeKey = null;
  const selectNode = async (node, label) => {
    const locatorValue = stableJSON(node.locator);
    const selector = '[data-testid^="map-result-node-"]';
    await waitFor(`(() => {const select=document.querySelector(${JSON.stringify(selector)});return Boolean(select&&!select.disabled&&Array.from(select.options).some(option=>option.value===${JSON.stringify(locatorValue)}));})()`,
      `${label} appears in the loaded Result timeline`, 90_000);
    const previous = await evaluate(`document.querySelector(${JSON.stringify(selector)})?.value`);
    if (previous !== locatorValue) {
      await evaluate(`(() => {const select=document.querySelector(${JSON.stringify(selector)});select.value=${JSON.stringify(locatorValue)};select.dispatchEvent(new Event('change',{bubbles:true}));})()`);
      await waitFor(`document.querySelector(${JSON.stringify(selector)})?.value===${JSON.stringify(locatorValue)}`, `select ${label} timeline node`, 30_000);
      await waitTooltipCleared(`switching to ${label}`);
    }
    currentNodeKey = locatorValue;
  };

  const selectSource = async (selectionIndex, source, label) => {
    const selector = `[data-testid^="map-result-source-"][data-testid$="-${selectionIndex}"]`;
    const desired = sourceValue(source);
    await waitFor(`(() => {const control=document.querySelector(${JSON.stringify(selector)});return Boolean(control&&(control.tagName==='OUTPUT'?control.textContent.trim().length>0:!control.disabled&&Array.from(control.options).some(option=>option.value===${JSON.stringify(desired)})));})()`,
      `${label} source control and choice are loaded`, 90_000);
    const present = await evaluate(`(() => {const select=document.querySelector(${JSON.stringify(selector)});return Boolean(select&&select.tagName==='SELECT'&&Array.from(select.options).some(option=>option.value===${JSON.stringify(desired)}));})()`);
    if (!present) {
      // A Result with a single immutable source displays an output instead of
      // a select. The completed hover request below still verifies its source.
      const output = await evaluate(`document.querySelector('[data-testid^="map-result-source-"][data-testid$="-${selectionIndex}"]')?.textContent ?? null`);
      assert.ok(output, `${label} source choice is not available in the actual Result timeline.`);
      return false;
    }
    const current = await evaluate(`document.querySelector(${JSON.stringify(selector)})?.value`);
    if (current === desired) return false;
    await waitFor(`(() => {const select=document.querySelector(${JSON.stringify(selector)});if(!select||select.disabled)return false;select.value=${JSON.stringify(desired)};select.dispatchEvent(new Event('change',{bubbles:true}));return true;})()`,
      `${label} source change on an enabled control`, 60_000);
    await waitFor(`(() => {const select=document.querySelector(${JSON.stringify(selector)});return select?.value===${JSON.stringify(desired)}&&!select.disabled;})()`, `${label} confirmed selected source`, 60_000);
    await waitTooltipCleared(`${label} source change`);
    await waitFor(`(() => {const select=document.querySelector(${JSON.stringify(selector)});const profile=document.querySelector('[data-testid^=map-result-profile-]');return select?.value===${JSON.stringify(desired)}&&!select.disabled&&profile&&!profile.disabled;})()`,
      `${label} source render completed`, 90_000);
    return true;
  };

  const activateComparisonControl = async (action, label) => {
    const selector = `[data-testid="map-result-${action}-comparison"]`;
    const deadline = Date.now() + 60_000;
    while (Date.now() < deadline) {
      const clicked = await evaluate(`(() => {
        const status=document.querySelector('.map-time-axis [role=status]')?.textContent??'';
        if(/读取 Result 帧失败/.test(status))throw new Error(status);
        const button=document.querySelector(${JSON.stringify(selector)});
        if(!button||button.disabled)return false;
        button.click();return true;
      })()`);
      if (clicked) return;
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
    throw new Error(`${label}: the ${action} comparison control did not become ready`);
  };

  const ensureSourceSlot = async (selectionIndex, label) => {
    const selector = `[data-testid^="map-result-source-"][data-testid$="-${selectionIndex}"]`;
    for (let attempt = 0; attempt < 32; attempt++) {
      if (await evaluate(`Boolean(document.querySelector(${JSON.stringify(selector)}))`)) return;
      const count = await evaluate("document.querySelectorAll('[data-testid^=map-result-source-]').length");
      await activateComparisonControl('add', label);
      await waitFor(`document.querySelectorAll('[data-testid^=map-result-source-]').length>${count}`,
        `${label} new actual comparison source slot`, 30_000);
    }
    assert.fail(`${label} could not create source slot ${selectionIndex} using the real comparison control.`);
  };

  const applySources = async (selectedSources, label) => {
    for (const source of selectedSources) {
      assert.ok(Number.isInteger(source.selectionIndex), `${label} selected source is missing its native slot index.`);
      await ensureSourceSlot(source.selectionIndex, label);
      await selectSource(source.selectionIndex, source, `${label} slot ${source.selectionIndex}`);
    }
  };

  const setProfile = async (profileIndex, label) => {
    const profile = String(profileIndex);
    const deadline = Date.now() + 60_000;
    while (Date.now() < deadline) {
      const action = await evaluate(`(() => {
        const status=document.querySelector('.map-time-axis [role=status]')?.textContent??'';
        if(/读取 Result 帧失败/.test(status))throw new Error(status);
        const input=document.querySelector('[data-testid^="map-result-profile-"]');
        if(!input||input.disabled)return 'waiting';
        if(input.dataset.profileIndex===${JSON.stringify(profile)})return 'unchanged';
        const option=Array.from(document.querySelectorAll('datalist option[data-testid^="map-result-step-"]')).find(item=>item.dataset.profileIndex===${JSON.stringify(profile)});
        if(!option)throw new Error('Requested native Result time profile is unavailable');
        Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(input,option.value);
        input.dispatchEvent(new Event('input',{bubbles:true}));input.dispatchEvent(new Event('change',{bubbles:true}));return 'changed';
      })()`);
      if (action === 'unchanged') return false;
      if (action === 'changed') {
        await waitFor(`document.querySelector('[data-testid^="map-result-profile-"]')?.dataset.profileIndex===${JSON.stringify(profile)}`, `${label} native time ${profile}`, 30_000);
        await waitTooltipCleared(`${label} time change`);
        return true;
      }
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
    throw new Error(`${label} time slider did not become ready`);
  };

  const waitForCaseFrames = async (testCase, entries, label) => {
    const profile = testCase.anchorProfileIndex ?? testCase.profileIndex;
    const expression = `(() => {const frames=Array.from(document.querySelectorAll('svg.map-canvas image[data-source-kind="result"]')).map(image=>({path:image.dataset.sourcePath,profileIndex:Number(image.dataset.profileIndex)}));return ${JSON.stringify(entries)}.every(expected=>frames.some(frame=>frame.path===expected.resultPath&&frame.profileIndex===expected.nativeProfileIndex));})()`;
    await waitFor(expression, `${label} actual Result frames`, 90_000);
    const currentProfile = await evaluate("Number(document.querySelector('[data-testid^=map-result-profile-]')?.dataset.profileIndex)");
    assert.equal(currentProfile, profile, `${label} anchor timeline profile changed unexpectedly.`);
  };

  const triggerPoint = async (testCase, entries) => {
    const primary = entries[0];
    assert.ok(primary, 'A real pointer hover requires at least one independently sampled visible HDF entry.');
    const point = testCase.point ?? testCase.sample ?? testCase;
    const profile = primary.nativeProfileIndex;
    const worldX = Number.isFinite(point.x) ? Number(point.x) : null;
    const worldY = Number.isFinite(point.y) ? Number(point.y) : null;
    const fallbackU = Number.isFinite(point.u) ? Number(point.u)
      : Number.isFinite(point.column) && Number.isFinite(point.width) ? (point.column + 0.5) / point.width : 0.5;
    const fallbackV = Number.isFinite(point.v) ? Number(point.v)
      : Number.isFinite(point.row) && Number.isFinite(point.height) ? (point.row + 0.5) / point.height : 0.5;
    return evaluate(`(() => {
      const canvas=document.querySelector('svg.map-canvas');
      const image=Array.from(canvas?.querySelectorAll('image[data-source-kind="result"]')??[]).find(item=>item.dataset.sourcePath===${JSON.stringify(primary.resultPath)}&&Number(item.dataset.profileIndex)===${Number(profile)});
      if(!canvas||!image)return null;
      const rect=image.getBoundingClientRect(),canvasRect=canvas.getBoundingClientRect();
      const x=${JSON.stringify(worldX)},y=${JSON.stringify(worldY)};
      const minX=Number(image.dataset.extentMinX),minY=Number(image.dataset.extentMinY),maxX=Number(image.dataset.extentMaxX),maxY=Number(image.dataset.extentMaxY);
      const hasWorldExtent=Number.isFinite(x)&&Number.isFinite(y)&&Number.isFinite(minX)&&Number.isFinite(minY)&&Number.isFinite(maxX)&&Number.isFinite(maxY)&&maxX>minX&&maxY>minY;
      const u=hasWorldExtent?(x-minX)/(maxX-minX):${fallbackU},v=hasWorldExtent?(maxY-y)/(maxY-minY):${fallbackV};
      // SVG attributes preserve the camera's double precision. DOMRect image
      // bounds are float32 and can shift a nominal cell-center sample.
      const svgX=Number(image.getAttribute('x')),svgY=Number(image.getAttribute('y'));
      const svgWidth=Number(image.getAttribute('width')),svgHeight=Number(image.getAttribute('height'));
      const clientX=canvasRect.left+svgX+svgWidth*u,clientY=canvasRect.top+svgY+svgHeight*v;
      if(!(rect.width>0&&rect.height>0&&u>=0&&u<=1&&v>=0&&v<=1))return null;
      canvas.dispatchEvent(new PointerEvent('pointermove',{bubbles:true,clientX,clientY,pointerId:1,pointerType:'mouse',isPrimary:true,buttons:0}));
      return{clientX,clientY,rect:{left:rect.left,top:rect.top,width:rect.width,height:rect.height},svgGeometry:{x:svgX,y:svgY,width:svgWidth,height:svgHeight},u,v,imageExtent:hasWorldExtent?{minX,minY,maxX,maxY}:null};
    })()`);
  };

  const runCase = async (testCase, index) => {
    const label = testCase.label ?? testCase.id ?? `Result hover case ${index + 1}`;
    const node = nodeForCase(testCase);
    const nodeLocatorValue = stableJSON(node.locator);
    if (currentNodeKey !== nodeLocatorValue) await selectNode(node, label);
    await ensureVisible(node, label);
    const selectedSources = matrixSourceList(testCase);
    const entries = matrixEntryList(testCase);
    assert.ok(selectedSources.length > 0, `${label} must list every selected MultiResult source, including sources with no matching frame.`);
    assert.ok(entries.length > 0, `${label} must list the independently sampled HDF hover entries.`);
    assert.equal(testCase.expectedEntryCount ?? entries.length, entries.length, `${label} expected entry count disagrees with its HDF samples.`);
    await waitFor(`Boolean(document.querySelector('[data-testid^="map-result-source-"][data-testid$="-0"]'))`,
      `${label} first real Result source control`, 60_000);
    if (node.locator?.nodeType === 'MultiResult') {
      await waitFor("Boolean(document.querySelector('[data-testid=map-result-add-comparison]'))",
        `${label} real MultiResult comparison control`, 60_000);
    }
    await applySources(selectedSources, label);
    const profile = testCase.anchorProfileIndex ?? testCase.profileIndex;
    assert.ok(Number.isInteger(profile), `${label} is missing its timeline anchor native profile index.`);
    await setProfile(profile, label);
    await waitForCaseFrames(testCase, entries, label);

    // A new state can cause a hover refresh at the old pointer position while
    // frames load. Leave and re-enter through React's delegated pointer route
    // so this case owns an unambiguous, independently observed POST.
    await evaluate("(() => {const canvas=document.querySelector('svg.map-canvas');canvas?.dispatchEvent(new PointerEvent('pointerout',{bubbles:true,clientX:0,clientY:0,pointerId:1,pointerType:'mouse',relatedTarget:document.body}));})()");
    await waitTooltipCleared(`${label} fresh pointer entry`);
    await evaluate('new Promise(resolve=>setTimeout(resolve,180))');
    const marker = await evaluate('window.__hecResultHoverObserver?.items.length ?? -1');
    assert.ok(marker >= 0, 'Read-only fetch observer was not installed before page navigation.');
    const point = await triggerPoint(testCase, entries);
    assert.ok(point, `${label} has no visible Result image at the independent sample pixel.`);
    // triggerPoint dispatches the actual SVG PointerEvent. The observer is
    // sampled before that event below by comparing unique request IDs; the
    // event itself is never replaced with backend or tooltip data.
    await waitFor(`window.__hecResultHoverObserver?.items.slice(${marker}).some(item=>item.kind==='accepted'&&item.operationMethod==='map.hover.identify')===true`,
      `${label} actual C# hover submission`, 30_000);
    const accepted = await evaluate(`window.__hecResultHoverObserver.items.slice(${marker}).findLast(item=>item.kind==='accepted'&&item.operationMethod==='map.hover.identify')`);
    assert.ok(accepted?.operationId && accepted.requestID && accepted.point, `${label} hover was not accepted by the real operations endpoint.`);
    assert.ok(accepted.requestID.startsWith('map-hover-'), `${label} did not travel through the React map hover handler.`);
    assert.ok(accepted.map?.mapID, `${label} hover request omitted the active UI map session ID.`);
    assert.equal(accepted.map.mapIndex, manifest.map.mapIndex, `${label} UI map index differs from the saved map identity.`);
    assert.equal(accepted.map.mapName, manifest.map.mapName, `${label} UI map name differs from the saved map identity.`);
    assert.equal(accepted.map.mapType, manifest.map.mapType, `${label} UI map type differs from the saved map identity.`);
    if (Number.isFinite(testCase.point?.x ?? testCase.x)) {
      const expectedX = testCase.point?.x ?? testCase.x;
      assert.ok(Math.abs(accepted.point.x - expectedX) <= (testCase.coordinateTolerance ?? 1e-5), `${label} X coordinate missed the independent HDF sample.`);
    }
    if (Number.isFinite(testCase.point?.y ?? testCase.y)) {
      const expectedY = testCase.point?.y ?? testCase.y;
      assert.ok(Math.abs(accepted.point.y - expectedY) <= (testCase.coordinateTolerance ?? 1e-5), `${label} Y coordinate missed the independent HDF sample.`);
    }
    assert.equal(accepted.profileSelections.length, 1, `${label} request must include the active Result anchor selection.`);
    assert.equal(accepted.profileSelections[0].profileIndex, profile, `${label} request used the wrong anchor profile.`);
    const resultLayer = accepted.layers.find((layer) => layer.kind === 'result');
    assert.ok(resultLayer, `${label} HTTP request omitted its Result layer capture.`);
    assert.equal(resultLayer.resultSelections.length, selectedSources.length, `${label} HTTP request omitted selected MultiResult source slots.`);
    for (const source of selectedSources) {
      const submitted = resultLayer.resultSelections.find((item) => item.selectionIndex === source.selectionIndex);
      assertSource(submitted, source, `${label} submitted source slot ${source.selectionIndex}`);
    }

    await waitFor(`window.__hecResultHoverObserver?.items.some(item=>item.kind==='poll'&&item.operationId===${JSON.stringify(accepted.operationId)}&&item.state==='completed'&&item.hasMore===false&&item.result?.requestID===${JSON.stringify(accepted.requestID)})===true`,
      `${label} terminal completed hover HTTP operation`, 30_000);
    const completed = await evaluate(`window.__hecResultHoverObserver.items.findLast(item=>item.kind==='poll'&&item.operationId===${JSON.stringify(accepted.operationId)}&&item.state==='completed'&&item.hasMore===false&&item.result?.requestID===${JSON.stringify(accepted.requestID)})`);
    assert.equal(completed.status, 200, `${label} operation poll did not return HTTP 200.`);
    assert.equal(normalizedMapID(completed.result.mapID), normalizedMapID(accepted.map.mapID),
      `${label} completed response map ID differs from the submitted UI session ID.`);
    assert.equal(completed.result.mapIndex, accepted.map.mapIndex, `${label} completed response map index differs from the request.`);
    assert.equal(completed.result.mapName, accepted.map.mapName, `${label} completed response map name differs from the request.`);
    assert.equal(completed.result.mapType, accepted.map.mapType, `${label} completed response map type differs from the request.`);
    assert.equal(completed.result.point?.x, accepted.point.x, `${label} completed response X coordinate differs from its request.`);
    assert.equal(completed.result.point?.y, accepted.point.y, `${label} completed response Y coordinate differs from its request.`);
    const responseEntries = completed.result.entries.filter((entry) => entry.kind === 'result');
    report.resultHoverCurrent = { label, targetPoint: testCase.point, point: accepted.point,
      frameGeometry: point, responseEntries };
    assert.equal(responseEntries.length, entries.length, `${label} completed operation returned the wrong number of available source frames.`);
    for (const expected of entries) {
      const entry = responseEntries.find((item) => item.sourcePath === expected.resultPath && sameRevision(item.sourceRevision, expected.revision ?? expected.resultRevision));
      assert.ok(entry, `${label} completed operation omitted HDF source ${expected.resultPath}.`);
      assert.equal(entry.mapType, testCase.mapType ?? manifest.map.mapType);
      assert.equal(entry.profileIndex, expected.nativeProfileIndex, `${label} response lost the child source's native profile index.`);
      assert.ok(numericMatch(entry.value, entryValue(expected), valueTolerance(expected, testCase)),
        `${label} C# response value ${entry.value} differs from independent HDF sample ${entryValue(expected)}.`);
      if (expected.valueType) assert.equal(entry.valueType, expected.valueType, `${label} source value type differs.`);
    }
    await waitFor(`(() => {const ui=Array.from(document.querySelectorAll('[data-testid="map-hover-tooltip-entry"]')).filter(item=>item.dataset.sourceKind==='result');return ui.length===${entries.length}&&${JSON.stringify(entries)}.every(expected=>ui.some(item=>item.dataset.sourcePath===expected.resultPath&&String(item.dataset.sourceRevision).toUpperCase()===String(expected.revision??expected.resultRevision).toUpperCase()&&Number(item.dataset.profileIndex)===expected.nativeProfileIndex&&item.dataset.mapType===(${JSON.stringify(testCase.mapType ?? manifest.map.mapType)})));})()`,
      `${label} React tooltip matches its completed operation and HDF values`, 30_000);
    const tooltipEntries = await evaluate("Array.from(document.querySelectorAll('[data-testid=map-hover-tooltip-entry]')).map(item=>({kind:item.dataset.sourceKind,path:item.dataset.sourcePath,revision:item.dataset.sourceRevision,mapType:item.dataset.mapType,profileIndex:Number(item.dataset.profileIndex),value:item.dataset.value,valueType:item.dataset.valueType}))");
    for (const expected of entries) {
      const tooltip = tooltipEntries.find((item) => item.path === expected.resultPath &&
        sameRevision(item.revision, expected.revision ?? expected.resultRevision) &&
        item.profileIndex === expected.nativeProfileIndex && item.mapType === (testCase.mapType ?? manifest.map.mapType));
      assert.ok(tooltip, `${label} React tooltip omitted HDF source ${expected.resultPath}.`);
      assert.ok(numericMatch(tooltip.value, entryValue(expected), valueTolerance(expected, testCase)),
        `${label} tooltip value ${tooltip.value} differs from independent HDF sample ${entryValue(expected)}.`);
    }
    delete report.resultHoverCurrent;
    process.stdout.write(`Result hover: ${label} verified (${responseEntries.length} source values).\n`);
    return { label, requestID: accepted.requestID, operationId: accepted.operationId, map: accepted.map, point: accepted.point,
      profileSelection: accepted.profileSelections[0], selectedSources: resultLayer.resultSelections,
      responseEntries, tooltipEntries, frameGeometry: point };
  };

  const activeSelector = '[data-testid^="map-result-node-"]';
  const results = [];
  for (let index = 0; index < manifest.cases.length; index++) {
    const testCase = manifest.cases[index];
    const beforeNode = await evaluate(`document.querySelector(${JSON.stringify(activeSelector)})?.value ?? null`);
    const beforeProfile = await evaluate("document.querySelector('[data-testid^=map-result-profile-]')?.dataset.profileIndex ?? null");
    const beforeSources = await evaluate("Array.from(document.querySelectorAll('select[data-testid^=map-result-source-]')).map(select=>({testID:select.dataset.testid,value:select.value}))");
    const result = await runCase(testCase, index);
    results.push(result);
    if (result.responseEntries.length > 1 && !results.some((item) => item.screenshot)) {
      const screenshot = await command('Page.captureScreenshot', { format: 'png' });
      const screenshotPath = path.join(output, 'result-hover-multi-sources.png');
      await writeFile(screenshotPath, Buffer.from(screenshot.data, 'base64'));
      result.screenshot = screenshotPath;
    }
    const afterProfile = await evaluate("document.querySelector('[data-testid^=map-result-profile-]')?.dataset.profileIndex ?? null");
    result.uiTransition = { beforeNode, beforeProfile, beforeSources, afterProfile };
  }

  const comparisonOperations = [];
  const multiCases = manifest.cases.filter((testCase) => {
    const key = testCase.node ?? testCase.nodeKey ?? testCase.nodeKind;
    return key === 'multiResult' || key === 'MultiResult';
  });
  const swappedB4Case = manifest.cases.find((testCase) => testCase.id === 'multi-swapped-B4-no-A-match') ?? multiCases.at(-1);
  assert.ok(swappedB4Case, 'The comparison controls require the swapped B4 MultiResult case.');
  const swappedB4Index = manifest.cases.indexOf(swappedB4Case);
  const sourceB = matrixSourceList(swappedB4Case).find((source) => source.selectionIndex === 0);
  const sourceA = matrixSourceList(swappedB4Case).find((source) => source.selectionIndex === 1);
  assert.ok(sourceA && sourceB, 'The swapped B4 comparison fixture must contain both A and B Result sources.');
  const profileSelector = '[data-testid^="map-result-profile-"]';
  const sourceSelector = (selectionIndex) => `[data-testid^="map-result-source-"][data-testid$="-${selectionIndex}"]`;
  const selectedSource = (selectionIndex) => evaluate(`document.querySelector(${JSON.stringify(sourceSelector(selectionIndex))})?.value ?? null`);
  const clickComparison = activateComparisonControl;

  // Remove the second source slot through the actual UI. At anchor B4 only B
  // has a matching native frame, so the completed operation must contain one
  // real B entry after the slot removal.
  await selectNode(manifest.nodes.multiResult, 'MultiResult before removing a comparison slot');
  await applySources(matrixSourceList(swappedB4Case), 'prepare B4 comparison-slot removal');
  await setProfile(swappedB4Case.anchorProfileIndex ?? swappedB4Case.profileIndex, 'prepare B4 comparison-slot removal');
  await waitForCaseFrames(swappedB4Case, matrixEntryList(swappedB4Case), 'prepare B4 comparison-slot removal');
  await clickComparison('remove', 'remove comparison slot 1');
  await waitFor(`!document.querySelector(${JSON.stringify(sourceSelector(1))})`, 'comparison slot 1 removed', 30_000);
  await waitFor(`document.querySelector(${JSON.stringify(sourceSelector(0))})?.value===${JSON.stringify(sourceValue(sourceB))}`,
    'slot 0 remains on B after removing comparison slot 1', 30_000);
  await waitFor(`document.querySelector(${JSON.stringify(profileSelector)})?.dataset.profileIndex===${JSON.stringify(String(swappedB4Case.anchorProfileIndex ?? swappedB4Case.profileIndex))}`,
    'B4 anchor profile remains selected after removing comparison slot 1', 30_000);
  const onlyBCase = {
    ...swappedB4Case,
    id: 'comparison-removed-slot1-b4',
    label: 'B4 after removing comparison slot 1',
    selectedSources: [{ ...sourceB, selectionIndex: 0 }],
    entries: matrixEntryList(swappedB4Case).filter((entry) => entry.selectionIndex === 0),
    expectedEntryCount: 1,
  };
  const afterRemoveHover = await runCase(onlyBCase, swappedB4Index);
  comparisonOperations.push({ action: 'remove-slot-1', selectedSources: [{ selectionIndex: 0, source: await selectedSource(0) }], hover: afterRemoveHover });

  // Restore slot 1 with A using the real add and source select controls, then
  // prove the all-sources-used fallback on a third slot selects latest B.
  await ensureSourceSlot(1, 'restore comparison slot 1');
  await selectSource(1, sourceA, 'restore slot 1 to A');
  await waitFor(`document.querySelector(${JSON.stringify(sourceSelector(1))})?.value===${JSON.stringify(sourceValue(sourceA))}`,
    'restored slot 1 selects A', 30_000);
  const restoredHover = await runCase(swappedB4Case, swappedB4Index);
  comparisonOperations.push({ action: 'restore-slot-1-to-A', selectedSources: [
    { selectionIndex: 0, source: await selectedSource(0) },
    { selectionIndex: 1, source: await selectedSource(1) },
  ], hover: restoredHover });

  await ensureSourceSlot(2, 'add third comparison slot after using A and B');
  await waitFor(`document.querySelector(${JSON.stringify(sourceSelector(2))})?.value===${JSON.stringify(sourceValue(sourceB))}`,
    'third slot falls back to the latest B source when A and B are already used', 30_000);
  comparisonOperations.push({ action: 'add-third-slot-all-sources-used', selectedSources: [
    { selectionIndex: 0, source: await selectedSource(0) },
    { selectionIndex: 1, source: await selectedSource(1) },
    { selectionIndex: 2, source: await selectedSource(2) },
  ] });
  await clickComparison('remove', 'remove comparison slot 2');
  await waitFor(`!document.querySelector(${JSON.stringify(sourceSelector(2))})`, 'comparison slot 2 removed', 30_000);
  await waitFor(`document.querySelector(${JSON.stringify(sourceSelector(0))})?.value===${JSON.stringify(sourceValue(sourceB))}&&document.querySelector(${JSON.stringify(sourceSelector(1))})?.value===${JSON.stringify(sourceValue(sourceA))}`,
    'two comparison slots are restored after removing slot 2', 30_000);
  comparisonOperations.push({ action: 'remove-slot-2', selectedSources: [
    { selectionIndex: 0, source: await selectedSource(0) },
    { selectionIndex: 1, source: await selectedSource(1) },
  ] });

  await evaluate("(() => {const canvas=document.querySelector('svg.map-canvas');canvas?.dispatchEvent(new PointerEvent('pointerout',{bubbles:true,clientX:0,clientY:0,pointerId:1,pointerType:'mouse',relatedTarget:document.body}));})()");
  await waitTooltipCleared('pointer leaving the map canvas');
  const firstMultiIndex = manifest.cases.findIndex((testCase) => {
    const key = testCase.node ?? testCase.nodeKey ?? testCase.nodeKind;
    return key === 'multiResult' || key === 'MultiResult';
  });
  assert.ok(firstMultiIndex >= 0, 'The manifest must include a MultiResult case to restore its original source selections.');
  const firstMultiCase = manifest.cases[firstMultiIndex];
  await applySources(matrixSourceList(firstMultiCase), 'restore initial MultiResult source order');
  await setProfile(firstMultiCase.anchorProfileIndex ?? firstMultiCase.profileIndex, 'restore initial MultiResult time');
  await waitForCaseFrames(firstMultiCase, matrixEntryList(firstMultiCase), 'restored initial MultiResult');
  await runCase(firstMultiCase, firstMultiIndex);

  // Hide the ordinary Result first so that hiding MultiResult cannot switch
  // the controller to the same HDF source through the ordinary node. The
  // assertion below then distinguishes the hidden MultiResult from that
  // legitimate fallback while exercising the real tree controls.
  await evaluate("(() => {const canvas=document.querySelector('svg.map-canvas');canvas?.dispatchEvent(new PointerEvent('pointerout',{bubbles:true,clientX:0,clientY:0,pointerId:1,pointerType:'mouse',relatedTarget:document.body}));})()");
  await waitTooltipCleared('pointer leaving before the visibility fallback check');
  const ordinaryRowExpression = `document.querySelector(${JSON.stringify(ordinaryRowSelector)})`;
  const multiRowExpression = `document.querySelector(${JSON.stringify(multiRowSelector)})`;
  const ordinaryHide = await evaluate(`(() => {const row=${ordinaryRowExpression};const input=row?.querySelector('input[type=checkbox]');if(!input||!input.checked||input.disabled)return false;input.click();return true;})()`);
  assert.equal(ordinaryHide, true, 'Could not temporarily hide ordinary Result through its real map-tree checkbox.');
  await waitFor(`(() => {const input=(${ordinaryRowExpression})?.querySelector('input[type=checkbox]');return input?.checked===false&&!input.disabled;})()`, 'hide ordinary Result while checking active MultiResult', 60_000);
  await runCase(firstMultiCase, firstMultiIndex);

  const hidden = await evaluate(`(() => {const row=${multiRowExpression};const input=row?.querySelector('input[type=checkbox]');if(!input||!input.checked||input.disabled)return false;input.click();return true;})()`);
  assert.equal(hidden, true, 'Could not hide the active MultiResult through its real map-tree checkbox.');
  await waitTooltipCleared('hiding the active Result layer');
  await waitFor(`(() => {const input=(${multiRowExpression})?.querySelector('input[type=checkbox]');return input?.checked===false&&!input.disabled;})()`, 'saved hidden MultiResult membership', 60_000);
  await waitFor("!Array.from(document.querySelectorAll('[data-testid=map-hover-tooltip-entry]')).some(entry=>entry.dataset.sourceKind==='result')",
    'hidden active MultiResult does not fall back to a visible Result tooltip', 15_000);

  const wasVisible = visibility.get(multiLocatorKey) === true;
  if (wasVisible) {
    await evaluate(`(() => {const row=${multiRowExpression};const input=row?.querySelector('input[type=checkbox]');if(!input||input.checked||input.disabled)throw new Error('Could not restore the original Result visibility');input.click();})()`);
    await waitFor(`(() => {const input=(${multiRowExpression})?.querySelector('input[type=checkbox]');return input?.checked===true&&!input.disabled;})()`, 'restore original MultiResult visibility', 60_000);
  }
  const ordinaryWasVisible = visibility.get(ordinaryLocatorKey) === true;
  if (ordinaryWasVisible) {
    await evaluate(`(() => {const row=${ordinaryRowExpression};const input=row?.querySelector('input[type=checkbox]');if(!input||input.checked||input.disabled)throw new Error('Could not restore the original ordinary Result visibility');input.click();})()`);
    await waitFor(`(() => {const input=(${ordinaryRowExpression})?.querySelector('input[type=checkbox]');return input?.checked===true&&!input.disabled;})()`, 'restore original ordinary Result visibility', 60_000);
  }

  report.resultHover = {
    manifest: manifest.schema,
    projectPath: manifest.projectPath,
    map: manifest.map,
    cases: results,
    comparisonOperations,
    pointerLeaveClearedTooltip: true,
    hiddenResultClearedTooltip: true,
    originalMultiResultVisibilityRestored: wasVisible,
    originalOrdinaryResultVisibilityRestored: ordinaryWasVisible,
  };
  report.checks.push('ordinary Result and five A/B MultiResult samples were read from real SVG pointer events, correlated with completed C# hover operations and independent HDF values; timeline/source switches clear stale values; child native profile indices survive slot swaps; pointer leave and hiding a Result clear its tooltip');
}
