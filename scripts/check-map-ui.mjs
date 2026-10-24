import assert from 'node:assert/strict';
import path from 'node:path';
import { readFile, writeFile, readdir } from 'node:fs/promises';
import { createHash, randomUUID } from 'node:crypto';
import { checkGdbVelocityUI } from './check-gdb-velocity-ui.mjs';
import { checkGdbRasterUI } from './check-gdb-raster-ui.mjs';
import { checkResultHoverUI } from './check-result-hover-ui.mjs';
import { checkTerrainUI } from './check-terrain-ui.mjs';

const sha = (bytes) => createHash('sha256').update(bytes).digest('hex').toUpperCase();

async function checkUploadValidation(fixture, report, serverUrl) {
  const origin = new URL(serverUrl);
  assert.ok(['127.0.0.1', 'localhost', '[::1]'].includes(origin.hostname));
  const projectBefore = await readFile(fixture.projectPath);
  const revision = sha(projectBefore);
  const sourcesPath = path.join(path.dirname(fixture.projectPath), 'Map Sources');
  const children = () => readdir(sourcesPath).catch(error => { if (error.code === 'ENOENT') return []; throw error; });
  const before = await children();
  for (const [name, expected, stale] of [['../escape.geojson', 400, false], ['incomplete.shp', 400, false], ['bad.exe', 400, false], ['stale.geojson', 409, true]]) {
    const form = new FormData();
    form.set('projectPath', fixture.projectPath);
    form.set('projectRevision', stale ? '0'.repeat(64) : revision);
    form.set('projectSessionID', randomUUID());
    form.append('files', new Blob(['{}']), name);
    const response = await fetch(new URL('/api/source-uploads', origin), { method: 'POST', body: form });
    assert.equal(response.status, expected, `Upload validation accepted ${name}`);
    assert.equal((await response.json()).error.code, stale ? 'sourceChanged' : 'invalidInput');
  }
  assert.deepEqual(await readFile(fixture.projectPath), projectBefore);
  assert.deepEqual(await children(), before, 'Rejected uploads left project source folders behind');
  report.checks.push('source uploads reject traversal names, incomplete Shapefile families, unsupported extensions and stale projects without modifying the project or publishing source folders');
}

/** Real server files and browser controls; no in-memory map data injection. */
export async function checkMapUI({ fixture, output, evaluate, waitFor, command, setFiles, navigate, report,
  serverUrl = process.env.HECRAS_VERIFY_SERVER_URL, gdbOutput, gdbRasterOutput }) {
  const resultHoverOnly = process.env.HECRAS_VERIFY_RESULT_HOVER_ONLY === '1';
  const terrainOnly = process.env.HECRAS_VERIFY_TERRAIN_ONLY === '1';
  if (!resultHoverOnly && !terrainOnly) await checkUploadValidation(fixture, report, serverUrl);
  const initialGeometrySHA = sha(await readFile(fixture.geometryPath));
  const initialPlanSHA = sha(await readFile(fixture.planPath));
  const open = async () => {
    await navigate('project');
    await evaluate("document.querySelector('.ras-menubar details summary').click(); Array.from(document.querySelectorAll('.ras-menu-popover button')).find(n=>n.textContent.includes('打开工程')).click()");
    await waitFor("Boolean(document.querySelector('.project-open-path-row input'))", 'map project open dialog');
    await evaluate(`(() => { const input=document.querySelector('.project-open-path-row input'); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(input,${JSON.stringify(fixture.projectPath)}); input.dispatchEvent(new Event('input',{bubbles:true})); })()`);
    await waitFor("!document.querySelector('.project-open-path-row button').disabled", 'map project path');
    await evaluate("document.querySelector('.project-open-path-row button').click()");
    await waitFor("!document.querySelector('.project-open-modal') && document.querySelectorAll('.project-tree-node').length>0", 'map source project tree', 60_000);
    await evaluate("Array.from(document.querySelectorAll('.project-tree-toolbar button')).find(n=>n.textContent.trim()==='全部展开').click()");
  };
  const close = async () => {
    await evaluate("document.querySelector('.ras-menubar details summary').click(); Array.from(document.querySelectorAll('.ras-menu-popover button')).find(n=>n.textContent.includes('关闭工程')).click()");
    await waitFor("document.querySelector('.ras-statusbar').innerText.includes('无活动工程')", 'close map fixture');
  };
  await open();
  process.stdout.write('Map UI: real project opened.\n');
  if (!resultHoverOnly && !terrainOnly) {
    // A group has no asset path. Selecting it after an actual model must retain
    // the local catalog selection when the active workbench asset is cleared.
    await evaluate("Array.from(document.querySelectorAll('.project-tree-node')).find(n=>n.querySelector('.project-tree-node-name')?.textContent==='Plan CPU').click()");
    await waitFor("document.querySelector('.project-tree-inspector').innerText.includes('Plan CPU')", 'source model selection before local group');
    await navigate('project');
    await evaluate("document.querySelector('.project-tree-node').click()");
    await waitFor("document.querySelector('.project-tree-node').getAttribute('aria-selected')==='true' && !document.querySelector('.project-tree-detail-empty')", 'non-asset catalog selection remains mounted');
    report.checks.push('non-asset catalog selection survives clearing the prior model asset without unmounting its detail pane');
  }
  if (terrainOnly) {
    const manifestPath = process.env.HECRAS_VERIFY_TERRAIN_FIXTURE?.trim();
    assert.ok(manifestPath && path.isAbsolute(manifestPath), 'Terrain acceptance requires an absolute manifest path.');
    const manifest = JSON.parse(await readFile(manifestPath, 'utf8'));
    assert.equal(path.resolve(manifest.mapRequest.projectPath), path.resolve(fixture.projectPath));
    try { await checkTerrainUI({ manifest, evaluate, waitFor, command, navigate, output, report, open, close }); }
    catch (error) { report.terrainDiagnostics = await evaluate('window.__hecResultHoverObserver?.items ?? []').catch(() => []); throw error; }
    await close();
    return;
  }
  if (resultHoverOnly) {
    const manifestPath = process.env.HECRAS_VERIFY_RESULT_HOVER_FIXTURE?.trim();
    assert.ok(manifestPath && path.isAbsolute(manifestPath), 'Result hover-only acceptance requires an absolute HECRAS_VERIFY_RESULT_HOVER_FIXTURE path.');
    const manifest = JSON.parse(await readFile(manifestPath, 'utf8'));
    assert.equal(path.resolve(manifest.projectPath), path.resolve(fixture.projectPath),
      'The Result hover manifest and opened ServerChecks map fixture must refer to the same project.');
    try {
      await checkResultHoverUI({ manifest, evaluate, waitFor, command, navigate, output, report });
    } catch (error) {
      report.resultHoverDiagnostics = await evaluate('window.__hecResultHoverObserver?.items ?? []').catch(() => []);
      throw error;
    }
    await close();
    return;
  }
  if (process.env.HECRAS_VERIFY_MAP_RASTER_ONLY === '1') {
    assert.ok(process.env.HECRAS_VERIFY_GDB_RASTER_SOURCE?.trim(), 'Raster-only acceptance requires a real GDB raster source.');
    await checkGdbRasterUI({ evaluate, waitFor, command, setFiles, navigate, open, close, output, gdbRasterOutput }, fixture, report);
    await close();
    return;
  }
  if (process.env.HECRAS_VERIFY_MAP_VIEW_ONLY === '1') {
    const visualizationPath = fixture.projectPath.replace(/\.ras$/i, '.vis');
    const before = await readFile(visualizationPath);
    await navigate('map');
    await waitFor("Boolean(document.querySelector('svg.map-canvas image[data-source-kind=result]')) && !document.querySelector('[data-testid^=map-result-profile-]')?.disabled", 'saved Result frame', 90_000);
    await waitFor("(() => {const axis=document.querySelector('.map-time-axis').getBoundingClientRect();return axis.top>=0 && axis.bottom<=window.innerHeight-20;})()", 'timeline visible inside desktop workspace');
    for (let i=0;i<6;i++) await evaluate("Array.from(document.querySelectorAll('.map-tree-expand[aria-expanded=false]')).forEach(n=>n.click())");
    const layout = await evaluate("(() => {const axis=document.querySelector('.map-time-axis').getBoundingClientRect();const pane=document.querySelector('.map-viewport-shell').getBoundingClientRect();const image=document.querySelector('svg.map-canvas image[data-source-kind=result]').getBoundingClientRect();return {timelineTop:axis.top,timelineBottom:axis.bottom,windowHeight:window.innerHeight,canvasHeight:pane.height,imageWidth:image.width,imageHeight:image.height};})()");
    assert.ok(layout.timelineBottom<=layout.windowHeight-20 && layout.canvasHeight>150 && layout.imageHeight>layout.canvasHeight*0.5, 'The Result or its time controls are not usable inside the map workspace');
    report.savedMapLayout = layout;
    report.checks.push('saved Result reopens with native time controls inside the desktop viewport, a focused Result image and independently scrolling expanded layer trees');
    const screenshot = await command('Page.captureScreenshot', { format: 'png' });
    await writeFile(path.join(output, 'server-map-layout.png'), Buffer.from(screenshot.data, 'base64'));
    assert.deepEqual(await readFile(visualizationPath), before, 'Viewing the saved Result rewrote .vis');
    if (process.env.HECRAS_VERIFY_GDB_RASTER_SOURCE?.trim()) {
      await checkGdbRasterUI({ evaluate, waitFor, command, setFiles, navigate, open, close, output, gdbRasterOutput }, fixture, report);
    }
    await close();
    return;
  }
  for (const name of ['Solver Geometry', 'Plan CPU']) {
    await waitFor(`Array.from(document.querySelectorAll('.project-tree-node')).some(n=>n.querySelector('.project-tree-node-name')?.textContent===${JSON.stringify(name)})`, `source model ${name}`);
    await evaluate(`Array.from(document.querySelectorAll('.project-tree-node')).find(n=>n.querySelector('.project-tree-node-name')?.textContent===${JSON.stringify(name)}).dispatchEvent(new MouseEvent('contextmenu',{bubbles:true,clientX:300,clientY:300}))`);
    await waitFor("Boolean(document.querySelector('[data-testid=project-tree-copy-model]')) && !document.querySelector('[data-testid=project-tree-copy-model]').disabled", 'real duplicate command');
    assert.equal(await evaluate("location.hash"), '#/project', 'Opening a tree context menu must not navigate to the model editor');
    await evaluate("document.querySelector('[data-testid=project-tree-copy-model]').click()");
    await waitFor(`Array.from(document.querySelectorAll('.project-tree-node-name')).some(n=>n.textContent===${JSON.stringify(name + ' (1)')})`, `committed copy of ${name}`, 60_000);
  }
  assert.equal(sha(await readFile(fixture.geometryPath)), initialGeometrySHA, 'Copy modified the original Geometry HDF');
  assert.equal(sha(await readFile(fixture.planPath)), initialPlanSHA, 'Copy modified the original Plan HDF');
  const geometryCopies = (await readdir(path.dirname(fixture.geometryPath))).filter(name => name.includes('(1)'));
  const planCopies = (await readdir(path.dirname(fixture.planPath))).filter(name => name.includes('(1)'));
  assert.equal(geometryCopies.length, 1); assert.equal(planCopies.length, 1);
  const projectXml = await readFile(fixture.projectPath, 'utf8');
  const associations = await evaluate(`(() => {const xml=new DOMParser().parseFromString(${JSON.stringify(projectXml)},'application/xml');const geometry=Array.from(xml.querySelectorAll('GeometryAssociation')).find(n=>n.getAttribute('Geometry')==='Solver Geometry (1)');const plan=Array.from(xml.querySelectorAll('PlanAssociation')).find(n=>n.getAttribute('Plan')==='Plan CPU (1)');return{geometryTerrain:geometry?.getAttribute('Terrain'),geometryRoughness:geometry?.getAttribute('N_Values'),planGeometry:plan?.getAttribute('Geometry'),planBoundary:plan?.getAttribute('BoundaryCondition')};})()`);
  assert.deepEqual(associations, { geometryTerrain: 'Solver Terrain', geometryRoughness: 'Solver Roughness', planGeometry: 'Solver Geometry', planBoundary: 'Solver Boundary' });
  report.modelCopies = { associations, originalGeometrySHA: initialGeometrySHA, originalPlanSHA: initialPlanSHA,
    copiedGeometrySHA: sha(await readFile(path.join(path.dirname(fixture.geometryPath), geometryCopies[0]))),
    copiedPlanSHA: sha(await readFile(path.join(path.dirname(fixture.planPath), planCopies[0]))) };
  await evaluate("Array.from(document.querySelectorAll('.project-tree-node')).find(n=>n.querySelector('.project-tree-node-name')?.textContent==='Plan CPU (1)').dispatchEvent(new MouseEvent('dblclick',{bubbles:true}))");
  await waitFor("document.querySelector('.ras-document-title').textContent.includes('Plan CPU (1)') && Array.from(document.querySelectorAll('[data-feature=editors] [role=combobox]')).some(n=>/^CPU\\b/.test(n.textContent.trim()))", 'copied Plan reopens through actual HDF model reader', 60_000);
  report.checks.push('project-tree Geometry and Plan copies publish actual HDF files and .ras associations, while original HDF bytes remain unchanged');

  await navigate('map');
  await waitFor("!document.querySelector('.map-create-document').disabled", 'map catalog ready', 60_000);
  await evaluate("document.querySelector('.map-create-document').click()");
  await waitFor("document.querySelectorAll('.map-document-tab-list [role=tab]').length===2 && !document.querySelector('.map-create-document').disabled", 'saved project map', 60_000);
  const geojsonPath = path.join(output, '工程矢量检验.geojson');
  await writeFile(geojsonPath, JSON.stringify({ type: 'FeatureCollection', features: [
    { type: 'Feature', id: 'point-cn', properties: { name: '中文测点', value: 2.75, enabled: true }, geometry: { type: 'Point', coordinates: [0.5, 0.5] } },
    { type: 'Feature', id: 42, properties: { name: '中文河线', value: 3.5 }, geometry: { type: 'LineString', coordinates: [[0,0],[2,2],[4,2]] } },
    { type: 'Feature', id: 'hole', properties: { name: '带洞区域' }, geometry: { type: 'Polygon', coordinates: [[[0,0],[3,0],[3,3],[0,3],[0,0]],[[1,1],[1,2],[2,2],[2,1],[1,1]]] } },
    { type: 'Feature', id: 'islands', properties: { name: '多岛区域' }, geometry: { type: 'MultiPolygon', coordinates: [[[[0,3],[1,3],[1,4],[0,4],[0,3]]],[[[3,3],[4,3],[4,4],[3,4],[3,3]]]] } },
  ] }));
  await setFiles('[data-testid=map-import-source-input]', [geojsonPath, fixture.terrainPath]);
  await waitFor("!document.querySelector('[data-testid=map-import-source]').disabled && Boolean(document.querySelector('.map-status-message')) && !document.querySelector('.map-status-message').textContent.includes('正在上传')", 'source upload and .vis import', 90_000);
  assert.match(await evaluate("document.querySelector('.map-status-message').textContent"), /已保存 2 个工程图层来源/, 'Browser source upload/import did not confirm both sources');
  process.stdout.write('Map UI: sources uploaded and persisted.\n');
  const expandTree = async () => {
    for (let i=0;i<5;i++) await evaluate("Array.from(document.querySelectorAll('.map-tree-expand[aria-expanded=false]')).forEach(n=>n.click())");
  };
  await expandTree();
  // Imported layers may use source defaults; exercise the real persisted visibility command.
  for (const label of ['工程矢量检验', 'flat']) {
    for (let i=0;i<4;i++) {
      const clicked = await evaluate(`(() => {const row=Array.from(document.querySelectorAll('.map-tree-row')).find(n=>n.querySelector('.map-tree-name')?.textContent.includes(${JSON.stringify(label)}) && n.querySelector('input[type=checkbox]') && !n.querySelector('input[type=checkbox]').checked && !n.querySelector('input[type=checkbox]').disabled);if(!row)return false;row.querySelector('input[type=checkbox]').click();return true;})()`);
      if (!clicked) break;
      await waitFor("!document.querySelectorAll('.map-tree-row input:disabled').length", 'persisted layer visibility', 30_000);
    }
  }
  await waitFor("document.querySelectorAll('svg.map-canvas [data-feature-id]').length===4 && document.querySelectorAll('svg.map-canvas image[data-source-kind=raster]').length===1", 'four persisted features and real raster pixels', 90_000);
  const vectorPaths = await evaluate("Array.from(document.querySelectorAll('svg.map-canvas .map-feature-path')).map(n=>({d:n.getAttribute('d'),rule:n.getAttribute('fill-rule')}))");
  assert.ok(vectorPaths.some(item => (item.d.match(/M /g)??[]).length===2 && item.rule==='evenodd'), 'The persisted polygon hole did not render with two rings');
  await evaluate("Array.from(document.querySelectorAll('svg.map-canvas [data-feature-id]')).find(n=>n.querySelector('circle')).dispatchEvent(new MouseEvent('click',{bubbles:true}))");
  await waitFor("document.querySelector('.map-inspector-pane').innerText.includes('中文测点') && document.querySelector('.map-inspector-pane').innerText.includes('2.75')", 'real persisted attribute selection');
  assert.ok(await evaluate("document.querySelector('[data-testid=ras-layer-properties-pane]')?.innerText.includes('2.75')"), 'Contextual properties lost the actual source attribute');
  await evaluate("Array.from(document.querySelectorAll('svg.map-canvas [data-feature-id]')).find(n=>n.querySelector('circle')).dispatchEvent(new MouseEvent('contextmenu',{bubbles:true,clientX:500,clientY:300}))");
  await waitFor("document.activeElement?.closest('.map-feature-context-menu') && document.activeElement.textContent.includes('缩放到要素')", 'context action initial keyboard focus');
  await evaluate("document.activeElement.dispatchEvent(new KeyboardEvent('keydown',{key:'ArrowUp',bubbles:true}))");
  assert.equal(await evaluate("document.activeElement.textContent"), '关闭', 'Context actions did not wrap keyboard focus');
  assert.equal(await evaluate("document.querySelector('.map-feature-context-menu button[disabled]')?.textContent"), '隐藏图层', 'Persisted source visibility must remain owned by the project tree');
  await evaluate("document.activeElement.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true}))");
  await waitFor("!document.querySelector('.map-feature-context-menu') && document.querySelector('[data-testid=ras-layer-properties-pane]').innerText.includes('中文测点')", 'context action dismissal retains current selection');
  report.checks.push('source-backed selection properties and contextual keyboard actions retain source attributes and project-owned visibility');
  const inspectImage = async (kind) => evaluate(`(async()=>{const node=document.querySelector('svg.map-canvas image[data-source-kind=${kind}]');if(!node)return null;const image=new Image();image.src=node.getAttribute('href');await image.decode();const canvas=document.createElement('canvas');canvas.width=image.width;canvas.height=image.height;const context=canvas.getContext('2d');context.drawImage(image,0,0);const pixels=context.getImageData(0,0,image.width,image.height).data;let opaque=0;for(let i=3;i<pixels.length;i+=4)if(pixels[i]>0)opaque++;return{width:image.width,height:image.height,opaque,sourcePath:node.dataset.sourcePath,profileIndex:node.dataset.profileIndex,screenWidth:node.getBBox().width,screenHeight:node.getBBox().height};})()`);
  const raster = await inspectImage('raster');
  assert.ok(raster.width>0 && raster.height>0 && raster.opaque>0 && raster.screenWidth>0 && raster.screenHeight>0);
  report.mapSources = { projectPath: fixture.projectPath, vectorFeatureCount: 4, raster, polygonHoles: true };
  report.checks.push('browser files upload into the project, persist three GeoJSON geometry layers and a GeoTIFF, then draw four source features, polygon holes, Chinese attributes and actual raster pixels');
  const mapShot = await command('Page.captureScreenshot', { format: 'png' });
  await writeFile(path.join(output, 'server-map-sources.png'), Buffer.from(mapShot.data, 'base64'));
  const visPath = fixture.projectPath.replace(/\.ras$/i, '.vis');
  const savedVis = await readFile(visPath);
  await close(); await open(); await navigate('map');
  await waitFor("document.querySelectorAll('svg.map-canvas [data-feature-id]').length===4 && document.querySelectorAll('svg.map-canvas image[data-source-kind=raster]').length===1", 'persisted source layers reopened without import', 90_000);
  assert.deepEqual(await readFile(visPath), savedVis, 'Reading saved map sources rewrote .vis');
  report.checks.push('closing and reopening the project restores real vector/raster layers without rewriting .vis');
  process.stdout.write('Map UI: vector/raster canvas and reopen verified.\n');

  await navigate('project');
  await evaluate("Array.from(document.querySelectorAll('.project-tree-toolbar button')).find(n=>n.textContent.trim()==='全部展开').click()");
  await evaluate("Array.from(document.querySelectorAll('.project-tree-node')).find(n=>n.querySelector('.project-tree-node-name')?.textContent==='Plan CPU').dispatchEvent(new MouseEvent('dblclick',{bubbles:true}))");
  await navigate('compute');
  await waitFor("Boolean(document.querySelector('[data-compute-action=run]')) && !document.querySelector('[data-compute-action=run]').disabled", '4x4 CPU Plan inspection', 60_000);
  await evaluate("document.querySelector('[data-compute-action=run]').click()");
  await waitFor("/计算完成|计算失败/.test(document.querySelector('[data-testid=compute-status]').innerText)", '4x4 CPU result', 180_000);
  assert.match(await evaluate("document.querySelector('[data-testid=compute-status]').innerText"), /计算完成/);
  const computed = JSON.parse(await evaluate("document.querySelector('[data-compute-output]').textContent"));
  assert.ok(computed.resultPath && computed.resultRevision);
  process.stdout.write('Map UI: real 4x4 CPU calculation completed.\n');
  await waitFor("Boolean(document.querySelector('[data-compute-action=open-result]')) && !document.querySelector('[data-compute-action=open-result]').disabled", 'open confirmed 4x4 result');
  await evaluate("document.querySelector('[data-compute-action=open-result]').click()");
  await waitFor("location.hash==='#/results'", '4x4 HDF result selected', 60_000);
  await navigate('map');
  await waitFor("!document.querySelector('[data-testid=map-refresh-sources]').disabled", 'fresh result map tree', 60_000);
  await expandTree();
  // Enable the original Plan's Result using the persisted map checkbox.
  const resultCheckbox = await evaluate("(() => {const rows=Array.from(document.querySelectorAll('.map-tree-row'));const row=rows.find(n=>n.querySelector('.map-tree-name span:last-child')?.textContent.trim()==='Plan CPU' && n.querySelector('input[type=checkbox]'));if(!row)return null;const input=row.querySelector('input[type=checkbox]');if(!input.checked)input.click();return true;})()");
  assert.equal(resultCheckbox, true, 'No persisted Plan result layer was available');
  await waitFor("document.querySelectorAll('svg.map-canvas image[data-source-kind=result]').length>0 && document.querySelectorAll('datalist option[data-testid^=map-result-step-]').length===5", 'actual Result frame and five native times', 90_000);
  const firstFrame = await inspectImage('result');
  assert.ok(firstFrame.opaque>0 && firstFrame.screenWidth>0 && firstFrame.screenHeight>0, 'Result pixels are empty or outside a valid extent');
  assert.equal(firstFrame.sourcePath, computed.resultPath);
  const nativeSteps = await evaluate("Array.from(document.querySelectorAll('datalist option[data-testid^=map-result-step-]')).map(n=>({profileIndex:n.dataset.profileIndex,label:n.label}))");
  const profileSelector = '[data-testid^=map-result-profile-]';
  await evaluate(`(() => {const input=document.querySelector('${profileSelector}');Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(input,'4');input.dispatchEvent(new Event('input',{bubbles:true}));input.dispatchEvent(new Event('change',{bubbles:true}));})()`);
  await waitFor("document.querySelector('svg.map-canvas image[data-source-kind=result]')?.dataset.profileIndex==='4' && !document.querySelector('[data-testid^=map-result-profile-]').disabled", 'native final output profile', 90_000);
  assert.equal((await inspectImage('result')).profileIndex, '4');
  const finalTime = await evaluate("document.querySelector('.map-time-current[data-testid^=map-result-time-]').textContent");
  assert.equal(finalTime, nativeSteps[4].label, 'The displayed time differs from the native selected output profile');
  await evaluate("(() => {const input=document.querySelector('[data-testid^=map-result-profile-]');Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(input,'0');input.dispatchEvent(new Event('input',{bubbles:true}));input.dispatchEvent(new Event('change',{bubbles:true}));})()");
  await waitFor("document.querySelector('svg.map-canvas image[data-source-kind=result]')?.dataset.profileIndex==='0' && Boolean(document.querySelector('[data-testid^=map-result-play-]')) && !document.querySelector('[data-testid^=map-result-play-]').disabled", 'first native profile and playback');
  await evaluate("document.querySelector('[data-testid^=map-result-play-]').click()");
  await waitFor("Number(document.querySelector('svg.map-canvas image[data-source-kind=result]')?.dataset.profileIndex)>0", 'serial native playback advances', 90_000);
  await evaluate("document.querySelector('[data-testid^=map-result-pause-]')?.click()");
  const resultShot = await command('Page.captureScreenshot', { format: 'png' });
  await writeFile(path.join(output, 'server-map-result.png'), Buffer.from(resultShot.data, 'base64'));
  report.mapResult = { resultPath: computed.resultPath, resultRevision: computed.resultRevision, firstFrame, nativeSteps, finalTime, playbackAdvanced: true };
  report.checks.push('actual 4x4 CPU calculation produces a nonempty map Result, five native time steps, selectable output profiles and serial playback on the same canvas');
  if (process.env.HECRAS_VERIFY_GDB_SOURCE?.trim()) {
    await checkGdbVelocityUI({ evaluate, waitFor, command, setFiles, output, gdbOutput }, fixture, report);
  }
  if (process.env.HECRAS_VERIFY_GDB_RASTER_SOURCE?.trim()) {
    await checkGdbRasterUI({ evaluate, waitFor, command, setFiles, navigate, open, close, output, gdbRasterOutput }, fixture, report);
  }
  await close();
}
