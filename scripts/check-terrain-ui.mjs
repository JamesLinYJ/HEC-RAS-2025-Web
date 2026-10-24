import assert from 'node:assert/strict';
import path from 'node:path';
import { writeFile } from 'node:fs/promises';

const stable = value => Array.isArray(value) ? value.map(stable)
  : value && typeof value === 'object' ? Object.fromEntries(Object.keys(value).sort().map(key => [key, stable(value[key])])) : value;
const key = value => JSON.stringify(stable(value));
const sameSHA = (left, right) => String(left).toUpperCase() === String(right).toUpperCase();

/** Shared real-pointer/PNG/HTTP acceptance for browser and hidden Electron. */
export async function checkTerrainUI({ manifest, evaluate, waitFor, command, navigate, output, report, open, close }) {
  assert.equal(manifest.schema, 'hecras-map-terrain-association-e2e/v1');
  const locator = manifest.terrain.locator;
  const rowSelector = `.map-tree-row[data-node-root=${JSON.stringify(locator.rootName)}]` +
    `[data-node-type=${JSON.stringify(locator.nodeType)}][data-node-path=${JSON.stringify(JSON.stringify(locator.nodePath))}]`;
  const row = `document.querySelector(${JSON.stringify(rowSelector)})`;
  const images = `Array.from(document.querySelectorAll('svg.map-canvas image[data-source-kind=terrain]'))`;
  const image = `${images}.find(image=>image.dataset.sourcePath===${JSON.stringify(manifest.terrain.sourcePath)}&&image.dataset.treeNodeId===(${row})?.dataset.nodeId)`;
  const imageFallback = `${images}.find(image=>image.dataset.sourcePath===${JSON.stringify(manifest.terrain.sourcePath)}&&String(image.dataset.sourceRevision).toUpperCase()===${JSON.stringify(manifest.terrain.sourceRevision.toUpperCase())})`;
  const currentImage = `(${image})??(${imageFallback})`;
  const checks = [];
  const samples = [];
  const frames = [];

  async function ready() {
    await navigate('map');
    await waitFor(`Array.from(document.querySelectorAll('.map-document-tab-list [role=tab]')).some(tab=>tab.getAttribute('aria-selected')==='true'&&tab.textContent.trim()===${JSON.stringify(manifest.mapRequest.mapName)})`, 'saved Terrain map selected', 90_000);
    for (let index = 0; index < 8; index++) {
      await evaluate("Array.from(document.querySelectorAll('.map-project-tree-section .map-tree-expand[aria-expanded=false]')).forEach(button=>button.click())");
      if (await evaluate(`Boolean(${row})`)) break;
    }
    await waitFor(`(() => {const input=(${row})?.querySelector('input[type=checkbox]');return input?.checked===true&&!input.disabled;})()`, 'exact checked Terrain tree row', 90_000);
    await waitFor(`Boolean(${currentImage})`, 'real C# Terrain RGBA image', 90_000);
  }

  async function inspectFrame(label) {
    const info = await evaluate(`(async () => {
      const image=${currentImage};if(!image)return null;
      const source=new Image();source.src=image.getAttribute('href');await source.decode();
      const canvas=document.createElement('canvas');canvas.width=source.naturalWidth;canvas.height=source.naturalHeight;
      const context=canvas.getContext('2d');context.drawImage(source,0,0);
      const pixels=context.getImageData(0,0,canvas.width,canvas.height).data;
      const digest=await crypto.subtle.digest('SHA-256',pixels);
      return {sourcePath:image.dataset.sourcePath,sourceRevision:image.dataset.sourceRevision,
        treeNodeID:image.dataset.treeNodeId,contextToken:image.dataset.associationContext,width:canvas.width,height:canvas.height,
        extent:{minX:Number(image.dataset.extentMinX),minY:Number(image.dataset.extentMinY),maxX:Number(image.dataset.extentMaxX),maxY:Number(image.dataset.extentMaxY)},
        rgbaSHA256:Array.from(new Uint8Array(digest),byte=>byte.toString(16).padStart(2,'0')).join('').toUpperCase()};
    })()`);
    assert.ok(info, `${label}: actual PNG frame is missing.`);
    assert.equal(info.sourcePath, manifest.terrain.sourcePath);
    assert.ok(sameSHA(info.sourceRevision, manifest.terrain.sourceRevision));
    await waitFor(`window.__hecResultHoverObserver.items.some(item=>item.kind==='poll'&&item.state==='completed'&&item.hasMore===false&&item.result?.contextToken===${JSON.stringify(info.contextToken)}&&Array.isArray(item.result?.layers))`, `${label} completed source-rendered frame`, 30_000);
    const completed = await evaluate(`window.__hecResultHoverObserver.items.findLast(item=>item.kind==='poll'&&item.state==='completed'&&item.hasMore===false&&item.result?.contextToken===${JSON.stringify(info.contextToken)}&&Array.isArray(item.result?.layers))`);
    const layer = completed.result.layers.find(layer => key(layer.locator) === key(locator));
    assert.ok(layer, `${label}: frame has no exact Terrain owner.`);
    assert.equal(layer.status, 'available');
    assert.equal(layer.isChecked, true); assert.equal(layer.effectiveVisible, true);
    assert.equal(layer.assetPath, info.sourcePath); assert.ok(sameSHA(layer.revision, info.sourceRevision));
    assert.equal(layer.width, info.width); assert.equal(layer.height, info.height);
    assert.deepEqual(completed.result.viewport, info.extent, 'A viewport raster must not be positioned using SourceExtent.');
    assert.ok(sameSHA(layer.rgbaSHA256, info.rgbaSHA256), 'The browser PNG must preserve actual C# RGBA pixels.');
    const accepted = await evaluate(`window.__hecResultHoverObserver.items.find(item=>item.kind==='accepted'&&item.operationId===${JSON.stringify(completed.operationId)})`);
    assert.equal(accepted.operationMethod, 'map.associations.load');
    assert.deepEqual(accepted.association.viewport, info.extent);
    frames.push({ label, ...info, operationId: completed.operationId, owner: layer.locator });
    return info;
  }

  async function hover(point, label, expected = manifest.expectedTerrainValue) {
    await evaluate("document.querySelector('svg.map-canvas')?.dispatchEvent(new PointerEvent('pointerout',{bubbles:true,relatedTarget:document.body}))");
    await waitFor("!document.querySelector('[data-testid=map-hover-tooltip]')", `${label} clears previous tooltip`, 15_000);
    const marker = await evaluate('window.__hecResultHoverObserver.items.length');
    const screen = await evaluate(`(() => {const image=${currentImage};const canvas=document.querySelector('svg.map-canvas');if(!image||!canvas)return null;
      const minX=Number(image.dataset.extentMinX),minY=Number(image.dataset.extentMinY),maxX=Number(image.dataset.extentMaxX),maxY=Number(image.dataset.extentMaxY);
      const u=(${point.x}-minX)/(maxX-minX),v=(maxY-${point.y})/(maxY-minY);
      if(u<0||u>1||v<0||v>1)return null;
      const rect=canvas.getBoundingClientRect();const clientX=rect.left+Number(image.getAttribute('x'))+Number(image.getAttribute('width'))*u;
      const clientY=rect.top+Number(image.getAttribute('y'))+Number(image.getAttribute('height'))*v;
      canvas.dispatchEvent(new PointerEvent('pointermove',{bubbles:true,clientX,clientY,pointerId:1,pointerType:'mouse',isPrimary:true,buttons:0}));return{clientX,clientY};})()`);
    assert.ok(screen, `${label}: independent world point is outside the drawn frame.`);
    await waitFor(`window.__hecResultHoverObserver.items.slice(${marker}).some(item=>item.kind==='accepted'&&item.operationMethod==='map.hover.identify'&&item.layers.some(layer=>layer.kind==='terrain'))`, `${label} real Terrain pointer submission`, 30_000);
    const accepted = await evaluate(`window.__hecResultHoverObserver.items.slice(${marker}).findLast(item=>item.kind==='accepted'&&item.operationMethod==='map.hover.identify'&&item.layers.some(layer=>layer.kind==='terrain'))`);
    const capture = accepted.layers.find(layer => layer.kind === 'terrain' && key(layer.node) === key(locator));
    assert.ok(capture); assert.equal(capture.sourcePath, manifest.terrain.sourcePath);
    assert.ok(sameSHA(capture.sourceRevision, manifest.terrain.sourceRevision));
    assert.ok(Math.abs(accepted.point.x - point.x) < 1e-10 && Math.abs(accepted.point.y - point.y) < 1e-10);
    await waitFor(`window.__hecResultHoverObserver.items.some(item=>item.kind==='poll'&&item.operationId===${JSON.stringify(accepted.operationId)}&&item.state==='completed'&&item.hasMore===false&&item.result?.requestID===${JSON.stringify(accepted.requestID)})`, `${label} C# sample completion`, 30_000);
    const completed = await evaluate(`window.__hecResultHoverObserver.items.findLast(item=>item.kind==='poll'&&item.operationId===${JSON.stringify(accepted.operationId)}&&item.state==='completed'&&item.hasMore===false&&item.result?.requestID===${JSON.stringify(accepted.requestID)})`);
    const entries = completed.result.entries.filter(entry => entry.kind === 'terrain' && key(entry.node) === key(locator));
    if (expected === null) {
      assert.deepEqual(entries, [], 'Outside-source NoData must not become a zero value.');
      await waitFor("!document.querySelector('[data-testid=map-hover-tooltip-entry][data-source-kind=terrain]')", 'NoData leaves no Terrain label', 15_000);
    } else {
      assert.equal(entries.length, 1);
      assert.ok(Math.abs(Number(entries[0].value) - expected) <= manifest.terrainTolerance);
      assert.equal(entries[0].sourcePath, manifest.terrain.sourcePath);
      assert.ok(sameSHA(entries[0].sourceRevision, manifest.terrain.sourceRevision));
      await waitFor(`(() => {const item=Array.from(document.querySelectorAll('[data-testid=map-hover-tooltip-entry][data-source-kind=terrain]')).find(item=>item.dataset.sourcePath===${JSON.stringify(manifest.terrain.sourcePath)});return Boolean(item&&Math.abs(Number(item.dataset.value)-${expected})<=${manifest.terrainTolerance}&&item.textContent.includes('地形高程'));})()`, `${label} actual Chinese Terrain label`, 30_000);
    }
    samples.push({ label, expected, point: accepted.point, operationId: accepted.operationId, entries });
    process.stdout.write(`Terrain UI: ${label} verified.\n`);
  }

  await ready();
  await inspectFrame('initial Terrain');
  await hover(manifest.knownWorldPoint, 'Terrain alongside Result');
  checks.push('Actual C# association RGBA, exact node/source revisions, viewport mapping and Chinese hover match the direct Terrain baseline.');

  const hiddenRows = await evaluate("Array.from(document.querySelectorAll('.map-tree-row[data-node-type=Result],.map-tree-row[data-node-type=MultiResult]')).filter(row=>row.querySelector('input[type=checkbox]')?.checked).map(row=>({root:row.dataset.nodeRoot,type:row.dataset.nodeType,path:row.dataset.nodePath}))");
  const selectorFor = saved => `.map-tree-row[data-node-root=${JSON.stringify(saved.root)}][data-node-type=${JSON.stringify(saved.type)}][data-node-path=${JSON.stringify(saved.path)}] input[type=checkbox]`;
  async function checked(selector, value, label) {
    await waitFor(`(() => {const input=document.querySelector(${JSON.stringify(selector)});if(!input||input.disabled)return false;if(input.checked!==${value})input.click();return true;})()`, label, 60_000);
    await waitFor(`(() => {const input=document.querySelector(${JSON.stringify(selector)});return input?.checked===${value}&&!input.disabled;})()`, `${label} saved receipt`, 90_000);
  }
  for (const saved of hiddenRows) await checked(selectorFor(saved), false, `hide ${saved.type}`);
  await waitFor("!document.querySelector('svg.map-canvas image[data-source-kind=result]')", 'Terrain-only map without Result frames', 90_000);
  await waitFor(`Boolean(${currentImage})`, 'Terrain remains drawn independently of Result', 90_000);
  await inspectFrame('Terrain only');
  await hover(manifest.knownWorldPoint, 'Terrain-only sampling');
  checks.push('Terrain rendering and hover remain functional when every Result layer is hidden.');
  const baselineFrame = frames.at(-1);
  await evaluate("(() => {const canvas=document.querySelector('svg.map-canvas');const rect=canvas.getBoundingClientRect();canvas.dispatchEvent(new WheelEvent('wheel',{bubbles:true,cancelable:true,clientX:rect.left+rect.width/2,clientY:rect.top+rect.height/2,deltaY:120}));})()");
  await waitFor(`(() => {const image=${currentImage};return Boolean(image&&image.dataset.associationContext!==${JSON.stringify(baselineFrame.contextToken)});})()`, 'new actual Terrain viewport frame', 90_000);
  const next = await inspectFrame('changed viewport');
  assert.notDeepEqual(next.extent, baselineFrame.extent);
  await hover(manifest.knownWorldPoint, 'Terrain after zoom');
  const sourceExtent = manifest.terrain.sourceExtent;
  const outside = { x: sourceExtent[0] - (sourceExtent[2] - sourceExtent[0]) * .05, y: manifest.knownWorldPoint.y };
  assert.ok(outside.x >= next.extent.minX && outside.x <= next.extent.maxX, 'The source-edge NoData point must remain on the actual canvas.');
  await hover(outside, 'Terrain NoData outside source bounds', null);
  checks.push('Camera changes invalidate old frames and labels; source-edge NoData stays missing.');

  await checked(rowSelector + ' input[type=checkbox]', false, 'hide Terrain');
  await waitFor(`!(${currentImage})`, 'hidden Terrain image removed', 90_000);
  await waitFor("!document.querySelector('[data-testid=map-hover-tooltip-entry][data-source-kind=terrain]')", 'hidden Terrain label removed', 15_000);
  await checked(rowSelector + ' input[type=checkbox]', true, 'show Terrain');
  await ready();
  await hover(manifest.knownWorldPoint, 'Terrain restored');
  const screenshot = await command('Page.captureScreenshot', { format: 'png' });
  await writeFile(path.join(output, 'terrain-only.png'), Buffer.from(screenshot.data, 'base64'));
  for (const saved of hiddenRows) await checked(selectorFor(saved), true, `restore ${saved.type}`);
  if (open && close) {
    await close(); await open(); await ready();
    await hover(manifest.knownWorldPoint, 'Terrain after project reopen');
  }
  checks.push('Terrain hide/save/show and project reopen preserve membership and restore the actual source.');
  report.terrain = { state: 'passed', checks, frames, samples, originalResultVisibilityRestored: true };
  report.checks.push(...checks);
}
