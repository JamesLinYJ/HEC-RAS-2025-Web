import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { access, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { createServer } from 'node:http';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { build } from 'vite';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const temporary = await mkdtemp(path.join(tmpdir(), 'hecras-background-rendering-'));
const directory = path.join(root, 'frontend/src/features/map/background');
const checks = [];
let browser;
let socket;
let exitPromise;
let fixtureServer;
let capturedRasterReplay;

try {
  await build({ configFile: false, root, logLevel: 'silent', build: {
    lib: { entry: { protocol: path.join(directory, 'protocol.ts'), geometry: path.join(directory, 'sceneGeometry.ts') },
      formats: ['es'], fileName: (_format, name) => `${name}.mjs` }, outDir: path.join(temporary, 'node'), minify: false,
  } });
  const api = await import(pathToFileURL(path.join(temporary, 'node/protocol.mjs')).href);
  const geometry = await import(pathToFileURL(path.join(temporary, 'node/geometry.mjs')).href);
  const vm = { AreBackgroundMapsOn: true, IsOpenStreetMapOn: true, IsSatelliteImageryOn: true,
    IsShadedReliefOn: false, SatelliteMapOpacity: 128, OsmStyle: 'basic', Density: 1, ShowLabels: true };
  const settings = api.backgroundMapSettings(vm);
  assert.equal(settings.satelliteOpacity, 128 / 255);
  for (const patch of [{ SatelliteMapOpacity: 256 }, { SatelliteMapOpacity: 0.5 }, { Density: 6 }, { Density: 1.2 },
    { OsmStyle: '../other' }, { OsmStyle: '' }, { ShowLabels: 'yes' }])
    assert.throws(() => api.backgroundMapSettings({ ...vm, ...patch }));
  const viewport = { minX: 10, minY: 20, maxX: 12, maxY: 21, pixelWidth: 2, pixelHeight: 1 };
  assert.deepEqual(api.backgroundMapViewportFromCamera({ scale: 1, offsetX: -10, offsetY: 21 }, { width: 2, height: 1 }), viewport);
  const large = api.backgroundMapViewportFromCamera({ scale: 2, offsetX: -100, offsetY: 500 }, { width: 5000, height: 3000 });
  assert.equal(large.minX, 50); assert.equal(large.maxY, 250); assert.equal(large.minY, -1250);
  assert.ok(large.pixelWidth <= 2048 && large.pixelHeight <= 2048 && large.pixelWidth * large.pixelHeight <= 2_097_152);
  for (const camera of [{ scale: 0, offsetX: 0, offsetY: 0 }, { scale: Infinity, offsetX: 0, offsetY: 0 },
    { scale: 1, offsetX: NaN, offsetY: 0 }]) assert.throws(() => api.backgroundMapViewportFromCamera(camera, { width: 100, height: 100 }));
  checks.push('Host settings bounds, exact byte-opacity conversion, Y-axis camera extent and bounded raster dimensions.');

  const request = { requestID: 'request-A', mapID: 'map-A', projectSessionID: 'session-A', projectionType: 'EPSG',
    projectionValue: '3857', viewport, worldUnitsPerPixel: 1, settings };
  const feature = { id: 'polygon-A', sourceLayer: 'landuse', styleLayer: 'park', geometryType: 'Polygon',
    parts: [{ points: [{ x: 10, y: 20 }, { x: 12, y: 20 }, { x: 12, y: 21 }], isClosed: true }],
    strokeColor: '#00000000', fillColor: '#804488CC', strokeOpacity: 1, fillOpacity: 1, strokeWidth: 1, lineWidth: 1,
    lineGapWidth: 0, isDashed: false, dash: [], zIndex: 2,
    clipPolygon: [{ x: 10, y: 20 }, { x: 12, y: 20 }, { x: 12, y: 21 }, { x: 10, y: 21 }],
    isTranslated: false, translationX: 0, translationY: 0 };
  const label = { id: 'label-A', text: '<b>真实名字</b>', point: { x: 11, y: 20.5 }, line: [], fontFamily: 'sans-serif',
    fontSize: 12, rotation: 30, textColor: '#FF123456', haloColor: '#80FFFFFF', haloWidth: 2, zIndex: 3,
    bold: true, italic: false, maximumWidth: 10 };
  const result = { requestID: request.requestID, mapID: request.mapID, projectSessionID: request.projectSessionID, viewport,
    rasters: [{ id: 'satellite', source: 'Satellite', width: 2, height: 1, rgba: Buffer.from([255, 0, 0, 128, 0, 0, 255, 255]).toString('base64'),
      minX: 10, minY: 20, maxX: 12, maxY: 21, opacity: 1 }], features: [feature], labels: [label],
    sources: [{ id: 'shadedRelief', state: 'disabled', message: null, attribution: null },
      { id: 'satellite', state: 'available', message: null, attribution: 'Satellite' },
      { id: 'osm', state: 'available', message: null, attribution: '© OSM' }], osmBackgroundColor: '#FFE8E4D8' };
  const parsed = api.parseBackgroundMapResult(result, request);
  assert.equal(parsed.osmBackgroundColor, '#E8E4D8FF'); assert.equal(parsed.features[0].fillColor, '#4488CC80');
  assert.equal(parsed.labels[0].text, '<b>真实名字</b>');
  for (const patch of [{ requestID: 'old' }, { mapID: 'other' }, { projectSessionID: 'old' },
    { viewport: { ...viewport, minX: 9 } }, { viewport: { ...viewport, pixelWidth: 3 } },
    { sources: result.sources.slice(1) }, { sources: [result.sources[0], result.sources[1], result.sources[1]] },
    { sources: result.sources.map(source => source.id === 'satellite' ? { ...source, state: 'failed' } : source) },
    { rasters: [] }, { features: [feature, feature] }, { osmBackgroundColor: 'url(https://example.invalid/a)' }])
    assert.throws(() => api.parseBackgroundMapResult({ ...result, ...patch }, request));
  for (const patch of [{ width: 3 }, { height: 0 }, { minY: 19 }, { maxX: Infinity }, { opacity: 1.1 },
    { rgba: '' }, { rgba: '!!!!!!!!!!!!' }, { rgba: Buffer.from([255, 0, 0, 128, 0, 0, 255]).toString('base64') }])
    assert.throws(() => api.parseBackgroundMapResult({ ...result, rasters: [{ ...result.rasters[0], ...patch }] }, request));
  for (const patch of [{ geometryType: 'Point' }, { fillColor: '#123456' }, { strokeOpacity: NaN }, { clipPolygon: [] },
    { lineGapWidth: 2 }, { isDashed: true, dash: [0, 0] }, { parts: [{ points: [{ x: 1, y: Infinity }], isClosed: false }] }])
    assert.throws(() => api.parseBackgroundMapResult({ ...result, features: [{ ...feature, ...patch }] }, request));
  for (const patch of [{ point: null }, { point: { x: NaN, y: 0 } }, { fontSize: 0 }, { haloColor: 'red' }, { maximumWidth: -1 }])
    assert.throws(() => api.parseBackgroundMapResult({ ...result, labels: [{ ...label, ...patch }] }, request));
  assert.throws(() => api.parseBackgroundMapResult(result, { ...request, settings: { ...settings, showLabels: false } }));
  const failed = { ...result, rasters: [], features: [], labels: [], osmBackgroundColor: null,
    sources: result.sources.map(source => source.state === 'available' ? { ...source, state: 'failed', message: 'Source timeout' } : source) };
  assert.equal(api.parseBackgroundMapResult(failed, request).sources.filter(source => source.state === 'failed').length, 2);
  checks.push('Strict request/map/session/viewport echoes; raster bytes, source status, ARGB, paths and label validation; failed sources stay failed.');

  // RasterDefinition(Extent, rows, cols) uses square cells anchored at the
  // upper left. These fixed extents catch both expansion directions instead
  // of accepting any nearby image bounds or merely echoing the request.
  for (const [name, view, expected] of [
    ['square', { minX: 0, minY: 0, maxX: 2, maxY: 2, pixelWidth: 2, pixelHeight: 2 },
      { minX: 0, minY: 0, maxX: 2, maxY: 2, cellSize: 1 }],
    ['right expansion', { minX: 10, minY: 20, maxX: 12, maxY: 23, pixelWidth: 2, pixelHeight: 2 },
      { minX: 10, minY: 20, maxX: 13, maxY: 23, cellSize: 1.5 }],
    ['bottom expansion', { minX: 10, minY: 20, maxX: 13, maxY: 22, pixelWidth: 2, pixelHeight: 2 },
      { minX: 10, minY: 19, maxX: 13, maxY: 22, cellSize: 1.5 }],
    ['rectangular aligned', { minX: -3, minY: -2, maxX: 3, maxY: 2, pixelWidth: 3, pixelHeight: 2 },
      { minX: -3, minY: -2, maxX: 3, maxY: 2, cellSize: 2 }],
  ]) {
    assert.deepEqual(api.backgroundMapRasterGrid(view), expected, name);
    const { cellSize: _cellSize, ...extent } = expected;
    const raster = { ...result.rasters[0], ...extent, width: view.pixelWidth, height: view.pixelHeight,
      rgba: Buffer.alloc(view.pixelWidth * view.pixelHeight * 4, 128).toString('base64') };
    const response = { ...result, viewport: view, rasters: [raster] };
    const renderRequest = { ...request, viewport: view };
    assert.equal(api.parseBackgroundMapResult(response, renderRequest).rasters.length, 1, name);
    for (const field of ['minX', 'minY', 'maxX', 'maxY']) {
      for (const delta of [-0.001, 0.001]) assert.throws(() => api.parseBackgroundMapResult({ ...response,
        rasters: [{ ...raster, [field]: raster[field] + delta }] }, renderRequest), `${name}: shifted ${field}`);
    }
    if (view.minY !== expected.minY || view.maxX !== expected.maxX)
      assert.throws(() => api.parseBackgroundMapResult({ ...response, rasters: [{ ...raster,
        minY: view.minY, maxX: view.maxX }] }, renderRequest), `${name}: cropped/stretched request extent is not the Host grid`);
  }
  assert.deepEqual([large.pixelWidth, large.pixelHeight], [1869, 1121]);
  assert.deepEqual(api.backgroundMapRasterGrid(large), { minX: 50, maxY: 250, maxX: 2550.892060660125,
    minY: -1250, cellSize: 1.3380909901873328 });
  const portrait = api.backgroundMapViewportFromCamera({ scale: 1, offsetX: 0, offsetY: 5000 }, { width: 3000, height: 5000 });
  assert.deepEqual([portrait.pixelWidth, portrait.pixelHeight], [1121, 1869]);
  assert.deepEqual(api.backgroundMapRasterGrid(portrait), { minX: -0, maxY: 5000, maxX: 3000,
    minY: -1.7841213202500512, cellSize: 2.6761819803746656 });
  const fractional = api.backgroundMapViewportFromCamera({ scale: 1, offsetX: 0, offsetY: 60.1 }, { width: 100.9, height: 60.1 });
  assert.deepEqual([fractional.pixelWidth, fractional.pixelHeight], [100, 60]);
  assert.deepEqual(api.backgroundMapRasterGrid(fractional), { minX: -0, maxY: 60.1, maxX: 100.9,
    minY: -0.44000000000000483, cellSize: 1.0090000000000001 });
  checks.push('Square-cell grid contract: exact upper-left anchor, right/bottom expansion, fractional CSS bounds and reduced landscape/portrait dimensions; shifted or request-stretched rasters are rejected.');

  const context = { projectPath: '/fixture/project.ras', projectRevision: 'a'.repeat(64), projectSessionID: 'session-A',
    projectXml: '<Project><Projection Type="EPSG">3857</Projection></Project>', selectedAsset: null };
  const document = { id: 'tab-A', mapID: 'map-A', mapIndex: 0, mapType: 'Project', name: '地图', visualizationRevision: 'b'.repeat(64), roots: [] };
  const key = api.backgroundMapSourceKey(context, document, settings, true, 1);
  assert.equal(api.backgroundMapHasProject(context, document), true);
  assert.equal(api.backgroundMapHasProject({ ...context, projectPath: 'browser-file://local.ras' }, document), false);
  assert.equal(api.backgroundMapHasProject(context, { ...document, mapID: 'local:source' }), false);
  assert.equal(api.backgroundMapHasProject(context, { ...document, visualizationRevision: null }), false);
  for (const patch of [{ projectXml: context.projectXml + ' ' }, { projectRevision: 'c'.repeat(64) },
    { projectPath: '/other.ras' }, { projectSessionID: 'other' }])
    assert.notEqual(api.backgroundMapSourceKey({ ...context, ...patch }, document, settings, true, 1), key);
  for (const patch of [{ mapID: 'other' }, { id: 'other' }, { visualizationRevision: 'c'.repeat(64) }, { mapIndex: 1 }])
    assert.notEqual(api.backgroundMapSourceKey(context, { ...document, ...patch }, settings, true, 1), key);
  assert.notEqual(api.backgroundMapSourceKey(context, document, { ...settings, osmStyle: 'darkmode' }, true, 1), key);
  assert.notEqual(api.backgroundMapSourceKey(context, document, settings, true, 2), key);
  assert.notEqual(api.backgroundMapSourceKey(context, document, settings, false, 1), key);
  assert.equal(api.backgroundMapSourceKey({ ...context, selectedAsset: { path: '/other.h5' } }, document, settings, true, 1), key);
  const gate = new api.BackgroundMapRequestGate(), backend = {}, abort = new AbortController();
  const capture = gate.begin(backend, key, 'camera-A');
  const current = { backend, sourceKey: key, viewportKey: 'camera-A' };
  assert.equal(gate.accepts(capture, current, abort.signal), true);
  for (const patch of [{ backend: {} }, { sourceKey: key + 'changed' }, { viewportKey: 'camera-B' }])
    assert.equal(gate.accepts(capture, { ...current, ...patch }, abort.signal), false);
  const newer = gate.begin(backend, key, 'camera-A');
  assert.equal(gate.accepts(capture, current, abort.signal), false);
  assert.equal(gate.accepts(newer, current, abort.signal), true);
  abort.abort(); assert.equal(gate.accepts(newer, current, abort.signal), false);
  const late = gate.begin(backend, key, 'camera-A'); gate.invalidate();
  assert.equal(gate.accepts(late, current, new AbortController().signal), false);
  checks.push('Exact project XML/revisions/map/settings/backend epoch identity; source changes, newer generations and ignored aborts reject old responses.');

  const camera = { scale: 2, offsetX: 10, offsetY: 100 };
  assert.equal(geometry.backgroundPathData([{ points: [{ x: 1, y: 2 }, { x: 3, y: 4 }], isClosed: true }], camera, { x: 3, y: -4 }),
    'M 15 92 L 19 88 Z');
  const lineLabel = { ...label, point: null, line: [{ x: 100, y: 0 }, { x: 90, y: 0 }, { x: 0, y: 0 }], rotation: 12 };
  const placement = geometry.backgroundLabelPlacement(lineLabel, { scale: 1, offsetX: 0, offsetY: 100 });
  assert.deepEqual(placement.anchor, { x: 50, y: 100 }); assert.equal(placement.pathLength, 100);
  assert.equal(placement.path, 'M 0 100 L 90 100 L 100 100'); assert.equal(placement.rotation, 12);
  assert.deepEqual(geometry.backgroundLabelLines({ ...label, text: '水文测站A', maximumWidth: 2, fontSize: 10 }, text => [...text].length * 10),
    ['水文', '测站', 'A']);
  checks.push('World-to-screen inversion, pixel translations, multipart paths, line-label arc midpoint/readable direction and CJK wrapping.');

  if (process.env.HECRAS_BACKGROUND_CAPTURE_DIR) {
    const captureDirectory = path.resolve(process.env.HECRAS_BACKGROUND_CAPTURE_DIR);
    const capturedRequest = JSON.parse(await readFile(path.join(captureDirectory, 'osm-after-fix-request.json'), 'utf8'));
    const capturedResult = JSON.parse(await readFile(path.join(captureDirectory, 'osm-after-fix-result.json'), 'utf8'));
    const actual = api.parseBackgroundMapResult(capturedResult.result ?? capturedResult, capturedRequest.parameters ?? capturedRequest);
    assert.ok(actual.features.length > 0 && actual.labels.length > 0);
    checks.push(`Previously captured real C# HTTP response parsed: ${actual.features.length} OSM features and ${actual.labels.length} labels; no new network request.`);
  }
  if (process.env.HECRAS_BACKGROUND_RASTER_REPLAY) {
    const capture = JSON.parse(await readFile(path.resolve(process.env.HECRAS_BACKGROUND_RASTER_REPLAY), 'utf8'));
    const capturedResult = JSON.parse(capture.terminalBody).result;
    const parsed = api.parseBackgroundMapResult(capturedResult, capture.parameters);
    assert.equal(parsed.rasters.length, 2);
    assert.ok(parsed.rasters.some(raster => raster.maxX !== parsed.viewport.maxX || raster.minY !== parsed.viewport.minY),
      'The replay must reproduce a real grid-aligned extent that differs from the requested viewport.');
    const satellite = parsed.rasters.find(raster => raster.id === 'satellite');
    const rgba = Buffer.from(satellite.rgba, 'base64');
    const requestedAlpha = Math.round(capture.parameters.settings.satelliteOpacity * 255);
    const alphas = new Set();
    for (let index = 3; index < rgba.length; index += 4) {
      assert.ok(rgba[index] <= requestedAlpha, 'Satellite opacity must already be baked into source alpha.'); alphas.add(rgba[index]);
    }
    assert.ok(alphas.has(requestedAlpha)); assert.equal(satellite.opacity, 1);
    capturedRasterReplay = { request: capture.parameters, result: capturedResult };
    checks.push(`Saved non-square C# raster replay accepted: ${satellite.width}x${satellite.height}; extent follows square-cell grid; satellite alpha includes requested ${requestedAlpha}/255 opacity and frame opacity remains 1. No source network request.`);
  }

  if (process.argv.includes('--browser')) {
    const chrome = process.env.HECRAS_CHROME ?? '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
    await access(chrome);
    const entry = path.join(temporary, 'browser.ts');
    await writeFile(entry, `import * as protocol from ${JSON.stringify(path.join(directory, 'protocol.ts'))};
      import * as geometry from ${JSON.stringify(path.join(directory, 'sceneGeometry.ts'))};
      import { BackgroundMapScene } from ${JSON.stringify(path.join(directory, 'BackgroundMapScene.tsx'))};
      import { useBackgroundMapRendering } from ${JSON.stringify(path.join(directory, 'useBackgroundMapRendering.ts'))};
      import { rgbaFrameImage } from ${JSON.stringify(path.join(root, 'frontend/src/features/map/mapImages.ts'))};
      import { createElement } from ${JSON.stringify(path.join(root, 'node_modules/react/index.js'))};
      import { createRoot } from ${JSON.stringify(path.join(root, 'node_modules/react-dom/client.js'))};
      export { protocol, geometry, BackgroundMapScene, useBackgroundMapRendering, rgbaFrameImage, createElement, createRoot };`);
    await build({ configFile: false, root, logLevel: 'silent', define: { 'process.env.NODE_ENV': '"production"' }, build: {
      lib: { entry, formats: ['iife'], name: 'BackgroundTest', fileName: () => 'background.js' },
      outDir: path.join(temporary, 'browser'), minify: false,
    } });
    fixtureServer = createServer((_request, response) => {
      response.writeHead(200, { 'content-type': 'text/html; charset=utf-8' });
      response.end('<!doctype html><html><head><title>Background protocol fixture</title></head><body></body></html>');
    });
    await new Promise(resolve => fixtureServer.listen(0, '127.0.0.1', resolve));
    const fixtureOrigin = `http://127.0.0.1:${fixtureServer.address().port}`;
    browser = spawn(chrome, ['--headless=new', '--no-first-run', '--no-default-browser-check', '--disable-background-networking',
      '--disable-component-update', '--disable-sync', '--disable-extensions', '--remote-debugging-port=0',
      `--user-data-dir=${path.join(temporary, 'profile')}`, 'about:blank'], { stdio: ['ignore', 'ignore', 'pipe'] });
    exitPromise = new Promise(resolve => browser.once('exit', resolve));
    const endpoint = await new Promise((resolve, reject) => {
      let output = '';
      const timeout = setTimeout(() => reject(new Error('Headless Chrome did not start.')), 20_000);
      browser.once('error', error => { clearTimeout(timeout); reject(error); });
      browser.stderr.on('data', bytes => { output += bytes; const match = output.match(/DevTools listening on (ws:\/\/[^\s]+)/);
        if (match) { clearTimeout(timeout); resolve(match[1]); } });
    });
    socket = new WebSocket(endpoint);
    await new Promise((resolve, reject) => { socket.addEventListener('open', resolve, { once: true }); socket.addEventListener('error', reject, { once: true }); });
    let sequence = 0;
    const pending = new Map();
    socket.addEventListener('message', event => { const response = JSON.parse(event.data); const item = pending.get(response.id);
      if (item) { pending.delete(response.id); clearTimeout(item.timeout); response.error ? item.reject(new Error(JSON.stringify(response.error))) : item.resolve(response.result); } });
    const command = (method, params = {}, sessionId) => new Promise((resolve, reject) => {
      const id = ++sequence, timeout = setTimeout(() => { pending.delete(id); reject(new Error(`CDP timeout: ${method}`)); }, 30_000);
      pending.set(id, { resolve, reject, timeout }); socket.send(JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) }));
    });
    const target = await command('Target.createTarget', { url: fixtureOrigin });
    const session = await command('Target.attachToTarget', { targetId: target.targetId, flatten: true });
    const evaluate = async expression => {
      const response = await command('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true }, session.sessionId);
      if (response.exceptionDetails) throw new Error(response.exceptionDetails.exception?.description ?? response.exceptionDetails.text);
      return response.result.value;
    };
    for (let attempt = 0; attempt < 100; attempt++) {
      if (await evaluate(`location.origin === ${JSON.stringify(fixtureOrigin)} && document.readyState === 'complete'`)) break;
      await new Promise(resolve => setTimeout(resolve, 20));
    }
    assert.equal(await evaluate('isSecureContext && typeof crypto.randomUUID === "function"'), true,
      'Hook tests require the same secure loopback origin as the application.');
    await evaluate(await readFile(path.join(temporary, 'browser/background.js'), 'utf8'));
    const browserResult = await evaluate(`(${browserChecks.toString()})(BackgroundTest)`);
    checks.push(...browserResult);
    if (capturedRasterReplay) checks.push(await evaluate(`(${browserRasterReplay.toString()})(BackgroundTest, ${JSON.stringify(capturedRasterReplay)})`));
    await command('Browser.close');
  }
  console.log(JSON.stringify({ passed: true, checks, browser: process.argv.includes('--browser') ? 'headless protocol/SVG/hook fixtures' : 'not requested' }, null, 2));
} finally {
  socket?.close();
  if (browser && browser.exitCode === null) browser.kill('SIGTERM');
  if (exitPromise) await Promise.race([exitPromise, new Promise(resolve => setTimeout(resolve, 3000))]);
  if (fixtureServer) await new Promise(resolve => fixtureServer.close(resolve));
  await rm(temporary, { recursive: true, force: true });
}

/** Browser-only fixtures use the real DOM parser, SVG rasterizer and React lifecycle. No live project writes. */
async function browserChecks(A) {
  const check = (value, label) => { if (!value) throw new Error(label); };
  const pause = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));
  const until = async (predicate, label) => { for (let count = 0; count < 250; count++) { if (predicate()) return; await pause(10); } throw new Error(`Timed out: ${label}`); };
  const result = [];
  for (const [xml, expected] of [
    ['<Project><Projection Type="EPSG">3857</Projection></Project>', 'EPSG'],
    ['<File><Project><Projection Type="WKT"><![CDATA[PROJCS["a & b"]]]></Projection></Project></File>', 'WKT'],
    ['<Project><Projection Type="None">SI</Projection></Project>', 'local'],
    ['<Project><Projection Type="Local">USC</Projection></Project>', 'local'],
    ['<Project><Child><Projection Type="EPSG">3857</Projection></Child></Project>', 'missing'],
    ['<Project><Projection Type="EPSG">3857</Projection><Projection Type="EPSG">4326</Projection></Project>', 'invalid'],
    ['<Project><Projection Type="EPSG">3857', 'invalid'],
    ['<Project><Projection Type="EPSG"><E>3857</E></Projection></Project>', 'invalid'],
    ['<Project><Projection Type="Other">3857</Projection></Project>', 'unknown'],
    ['<!DOCTYPE Project [<!ENTITY p "3857">]><Project><Projection Type="EPSG">&p;</Projection></Project>', 'invalid'],
  ]) { const parsed = A.protocol.readBackgroundMapProjection(xml); check((parsed.projection?.type ?? parsed.issue) === expected, `XML projection gate: ${expected}`); }
  const xmlValue = A.protocol.readBackgroundMapProjection('<Project><Projection Type="WKT">  PROJCS[&quot;a &amp; b&quot;]  </Projection></Project>');
  check(xmlValue.projection.value === '  PROJCS["a & b"]  ', 'Projection XML text must be decoded and preserved exactly.');
  result.push('Real browser DOMParser: exact Project/Projection scope, entities/CDATA, Local, missing, duplicate, malformed and unsupported definitions.');

  const rgba = btoa(String.fromCharCode(255, 0, 0, 128, 0, 0, 255, 255));
  const image = new Image(); image.src = A.rgbaFrameImage(rgba, 1, 2); await image.decode();
  const canvas = document.createElement('canvas'); canvas.width = 1; canvas.height = 2;
  const context2d = canvas.getContext('2d'); context2d.drawImage(image, 0, 0);
  check([...context2d.getImageData(0, 0, 1, 2).data].join(',') === '255,0,0,128,0,0,255,255', 'Straight-alpha RGBA and top-down row order changed.');

  const points = (x0, y0, x1, y1) => [{ x: x0, y: y0 }, { x: x1, y: y0 }, { x: x1, y: y1 }, { x: x0, y: y1 }];
  const polygon = { id: 'under', sourceLayer: 'landuse', styleLayer: 'base', geometryType: 'Polygon',
    parts: [{ points: points(0, 0, 100, 100), isClosed: true }], strokeColor: null, fillColor: '#FF0000FF',
    strokeOpacity: 1, fillOpacity: 1, strokeWidth: 0, lineWidth: 0, lineGapWidth: 0, isDashed: false, dash: [], zIndex: 0,
    clipPolygon: points(0, 0, 100, 100), isTranslated: false, translationX: 0, translationY: 0 };
  const land = { ...polygon, id: 'land', fillColor: '#00FF00FF', zIndex: 10,
    parts: [...polygon.parts, { points: points(40, 40, 60, 60), isClosed: true }] };
  const line = { ...polygon, id: 'gap', geometryType: 'LineString', zIndex: 20, fillColor: null, strokeColor: '#0000FFFF',
    strokeWidth: 12, lineWidth: 4, lineGapWidth: 4, parts: [{ points: [{ x: 0, y: 50 }, { x: 100, y: 50 }], isClosed: false }] };
  const dash = { ...line, id: 'dash', strokeColor: '#000000FF', strokeWidth: 2, lineWidth: 2, lineGapWidth: 0, zIndex: 30,
    parts: [{ points: [{ x: 10, y: 10 }, { x: 90, y: 10 }], isClosed: false }],
    isDashed: true, dash: [5, 5], isTranslated: true, translationX: 5, translationY: -5, clipPolygon: points(20, 0, 80, 100) };
  const label = { id: 'label', text: '<script>window.injected=true</script>', point: { x: 150, y: 150 }, line: [],
    fontFamily: 'sans-serif', fontSize: 12, rotation: 25, textColor: '#000000FF', haloColor: '#FFFFFFFF', haloWidth: 2,
    zIndex: 40, bold: true, italic: true, maximumWidth: 2_147_483_647 };
  const sceneFrame = { requestID: 'scene', mapID: 'map', projectSessionID: 'session', sourceKey: 'source',
    viewport: { minX: 0, minY: 0, maxX: 100, maxY: 100, pixelWidth: 100, pixelHeight: 100 }, rasters: [],
    features: [dash, line, land, polygon], labels: [label, { ...label, id: 'line-label', text: 'Line', point: null,
      line: [{ x: 200, y: 200 }, { x: 100, y: 200 }] }], sources: [], osmBackgroundColor: null };
  const container = document.createElement('div'); document.body.append(container); const root = A.createRoot(container);
  root.render(A.createElement('svg', { xmlns: 'http://www.w3.org/2000/svg', width: 100, height: 100 },
    A.createElement(A.BackgroundMapScene, { frame: sceneFrame, viewport: { scale: 1, offsetX: 0, offsetY: 100 }, size: { width: 100, height: 100 } })));
  await until(() => container.querySelectorAll('[data-background-feature]').length === 4, 'SVG scene');
  check([...container.querySelectorAll('[data-background-feature]')].map(node => node.dataset.backgroundFeature).join(',') === 'under,land,gap,dash', 'Host Z ordering was not applied.');
  check(container.querySelector('textPath') && container.querySelector('text[paint-order="stroke fill"]'), 'Line label path/halo missing.');
  check(!container.querySelector('script') && !window.injected && container.textContent.includes('<script>'), 'Label text became markup.');
  check(container.querySelector('.map-background-scene').getAttribute('pointer-events') === 'none', 'Background intercepted pointer input.');
  const svgImage = new Image(); const url = URL.createObjectURL(new Blob([new XMLSerializer().serializeToString(container.querySelector('svg'))], { type: 'image/svg+xml' }));
  svgImage.src = url; await svgImage.decode();
  const rendered = document.createElement('canvas'); rendered.width = rendered.height = 100; const rendering = rendered.getContext('2d'); rendering.drawImage(svgImage, 0, 0);
  const pixel = (x, y) => [...rendering.getImageData(x, y, 1, 1).data].join(',');
  check(pixel(50, 50) === '255,0,0,255', 'Gap or polygon hole painted over lower layers.');
  check(pixel(50, 45) === '0,0,255,255', 'Line casing width is wrong.');
  check(pixel(30, 30) === '0,255,0,255', 'Polygon fill is wrong.');
  check(pixel(16, 85) === '0,255,0,255', 'Translated line escaped its tile clip.');
  check(pixel(26, 85) === '0,0,0,255' && pixel(31, 85) === '0,255,0,255', 'Pixel translation or dash spacing is wrong.');
  URL.revokeObjectURL(url); root.unmount(); container.remove();
  result.push('Headless browser pixels: straight RGBA/row orientation, polygon holes, transparent line gaps, clipping, translation and dashes; SVG Z order, safe labels/halo/path and pointer transparency.');

  const calls = [];
  const backend = { supports: () => true, request: (method, parameters, options) => new Promise((resolve, reject) => calls.push({ method, parameters, options, resolve, reject })) };
  let options = { backend, backendRevision: 1, language: 'zh', coordinatesAreProject: true,
    context: { projectPath: '/fixture/project.ras', projectRevision: 'a'.repeat(64), projectSessionID: 'session', selectedAsset: null,
      projectXml: '<Project><Projection Type="EPSG">3857</Projection></Project>' },
    document: { id: 'map', mapID: 'map', mapIndex: 0, name: '地图', mapType: 'Project', visualizationRevision: 'b'.repeat(64), roots: [] },
    camera: { scale: 1, offsetX: 0, offsetY: 100 }, size: { width: 100, height: 100 },
    settings: { AreBackgroundMapsOn: true, IsOpenStreetMapOn: true, IsSatelliteImageryOn: false, IsShadedReliefOn: false,
      SatelliteMapOpacity: 255, OsmStyle: 'basic', Density: 1, ShowLabels: true } };
  let state;
  function Harness(props) { state = A.useBackgroundMapRendering(props); return A.createElement('span', null, state.status); }
  const hookContainer = document.createElement('div'); document.body.append(hookContainer); const hookRoot = A.createRoot(hookContainer);
  const render = async patch => { options = { ...options, ...patch }; hookRoot.render(A.createElement(Harness, options)); await pause(30); };
  const reply = (call, sourcePatch = {}) => {
    const q = call.parameters;
    return { requestID: q.requestID, mapID: q.mapID, projectSessionID: q.projectSessionID, viewport: q.viewport,
      rasters: [], features: [], labels: [], osmBackgroundColor: '#FFE8E4D8',
      sources: [{ id: 'shadedRelief', state: 'disabled', message: null, attribution: null },
        { id: 'satellite', state: q.settings.isSatelliteImageryOn ? 'failed' : 'disabled', message: null, attribution: null },
        { id: 'osm', state: 'available', message: null, attribution: '© OSM' }], ...sourcePatch };
  };
  const finishProjection = async start => {
    await until(() => calls.length > start, 'projection request');
    check(calls[start].method === 'projection.evaluate', 'Projection was not verified by the host.');
    calls[start].resolve({ isValid: true, isProjected: true }); await until(() => calls.length > start + 1, 'render request');
    return calls[start + 1];
  };
  await render({}); const first = await finishProjection(0); first.resolve(reply(first)); await until(() => state.status === 'ready', 'first accepted frame');
  const firstID = state.frame.requestID;
  await render({ camera: { ...options.camera, offsetX: 10 } });
  check(state.frame.requestID === firstID, 'Panning discarded a correctly georeferenced same-source frame too soon.');
  await render({ camera: { ...options.camera, offsetX: 20 } });
  await until(() => calls.length === 3, 'debounced camera request'); await pause(70); check(calls.length === 3, 'Camera debounce submitted more than one request.');
  const old = calls[2];
  await render({ settings: { ...options.settings, OsmStyle: 'darkmode' } });
  check(state.frame === null && old.options.signal.aborted, 'Source switch did not immediately hide/abort the old frame.');
  const current = await finishProjection(3);
  old.resolve(reply(old)); await pause(25); check(state.frame === null && state.status === 'loading', 'Late source response was published.');
  current.resolve(reply(current)); await until(() => state.status === 'ready', 'new source frame');
  await render({ camera: { ...options.camera, offsetX: 30 } }); await until(() => calls.length === 6, 'request to cancel');
  const cancelled = calls[5]; state.cancel(); await pause(25);
  check(cancelled.options.signal.aborted && state.frame === null && state.status === 'cancelled', 'Cancellation did not retire the current request/frame.');
  cancelled.resolve(reply(cancelled)); await pause(25); check(state.status === 'cancelled', 'Ignored abort overwrote cancellation.');
  await render({ camera: { ...options.camera, offsetX: 40 } }); await until(() => calls.length === 7, 'camera resumes after cancel');
  calls[6].resolve(reply(calls[6])); await until(() => state.status === 'ready', 'post-cancel frame');
  await render({ settings: { ...options.settings, IsSatelliteImageryOn: true } }); const partial = await finishProjection(7);
  partial.resolve(reply(partial)); await until(() => state.status === 'partial', 'partial source state'); check(state.frame !== null, 'Available OSM content was discarded with failed satellite.');
  state.refresh(); await until(() => calls.length === 10, 'retry request'); const failedCall = calls[9];
  failedCall.resolve(reply(failedCall, { osmBackgroundColor: null,
    sources: reply(failedCall).sources.map(source => source.id === 'osm' ? { ...source, state: 'failed', message: 'timeout' } : source) }));
  await until(() => state.status === 'error', 'source failure state'); check(state.frame === null, 'Failed sources retained a successful-looking frame.');
  await render({ context: { ...options.context, projectXml: '<Project><Projection Type="EPSG">4326</Projection></Project>' } });
  await until(() => calls.length === 11, 'geographic projection check'); calls[10].resolve({ isValid: true, isProjected: false });
  await until(() => state.status === 'blocked', 'nonprojected CRS gate'); await pause(200); check(calls.length === 11 && state.frame === null, 'Geographic CRS was sent to the renderer.');
  await render({ context: { ...options.context, projectXml: '<Project><Projection Type="Local">SI</Projection></Project>' } });
  await pause(200); check(state.status === 'blocked' && calls.length === 11, 'Local CRS triggered a network render.');
  await render({ context: { ...options.context, projectXml: '<Project><Projection Type="EPSG">3857</Projection></Project>' }, coordinatesAreProject: false });
  await pause(200); check(state.status === 'blocked' && calls.length === 11, 'Source-coordinate canvas triggered a network render.');
  await render({ coordinatesAreProject: true, camera: { scale: 1, offsetX: 0, offsetY: 60.1 }, size: { width: 100.9, height: 60.1 },
    settings: { ...options.settings, IsOpenStreetMapOn: false, IsSatelliteImageryOn: true, SatelliteMapOpacity: 128 } });
  const nonSquare = await finishProjection(11);
  check(nonSquare.parameters.viewport.pixelWidth === 100 && nonSquare.parameters.viewport.pixelHeight === 60,
    'Fractional CSS viewport did not produce the expected integer render target.');
  const nonSquareReply = reply(nonSquare, { osmBackgroundColor: null,
    sources: [{ id: 'shadedRelief', state: 'disabled', message: null, attribution: null },
      { id: 'satellite', state: 'available', message: null, attribution: 'Satellite' },
      { id: 'osm', state: 'disabled', message: null, attribution: '© OSM' }],
    rasters: [{ id: 'satellite', source: 'Satellite', width: 100, height: 60, rgba: btoa('\xff\0\0\x80'.repeat(6000)),
      minX: 0, minY: -0.44000000000000483, maxX: 100.9, maxY: 60.1, opacity: 1 }] });
  nonSquare.resolve(nonSquareReply); await until(() => state.status === 'ready', 'aligned non-square frame');
  check(state.frame.rasters[0].minY === -0.44000000000000483 && state.frame.rasters[0].opacity === 1,
    'Hook replaced the Host grid extent or applied the satellite opacity again.');
  state.refresh(); await until(() => calls.length === 14, 'misaligned frame rejection');
  calls[13].resolve({ ...nonSquareReply, requestID: calls[13].parameters.requestID,
    rasters: [{ ...nonSquareReply.rasters[0], minY: 0 }] });
  await until(() => state.status === 'error', 'request-stretched raster rejection');
  check(state.frame === null, 'A raster stretched to the camera viewport was shown as an aligned result.');
  hookRoot.unmount(); hookContainer.remove();
  result.push('Real React lifecycle fixtures: projection gate, camera debounce, synchronous source hiding, ignored-abort/late-response rejection, cancel and move-to-retry, partial/all-source failures, non-square aligned frame acceptance and request-stretched frame rejection.');
  return result;
}

/** Replay the retained real C# response directly into SVG, without downloading any new tiles. */
async function browserRasterReplay(A, capture) {
  const parsed = A.protocol.parseBackgroundMapResult(capture.result, capture.request);
  const satellite = parsed.rasters.find(raster => raster.id === 'satellite');
  const frame = { ...parsed, sourceKey: 'saved-real-raster-replay', rasters: [{ ...satellite,
    imageSrc: A.rgbaFrameImage(satellite.rgba, satellite.width, satellite.height) }] };
  const size = { width: parsed.viewport.maxX - parsed.viewport.minX, height: parsed.viewport.maxY - parsed.viewport.minY };
  const camera = { scale: 1, offsetX: -parsed.viewport.minX, offsetY: parsed.viewport.maxY };
  const container = document.createElement('div'); document.body.append(container);
  const root = A.createRoot(container);
  let imageURL;
  try {
    root.render(A.createElement('svg', { xmlns: 'http://www.w3.org/2000/svg', width: Math.ceil(size.width), height: Math.ceil(size.height) },
      A.createElement(A.BackgroundMapScene, { frame, viewport: camera, size })));
    for (let count = 0; count < 100 && !container.querySelector('[data-background-raster]'); count++)
      await new Promise(resolve => setTimeout(resolve, 10));
    const raster = container.querySelector('[data-background-raster]');
    if (!raster) throw new Error('Saved raster replay did not mount.');
    const expectedWidth = satellite.maxX - satellite.minX, expectedHeight = satellite.maxY - satellite.minY;
    if (Number(raster.getAttribute('width')) !== expectedWidth || Number(raster.getAttribute('height')) !== expectedHeight ||
      Number(raster.getAttribute('opacity')) !== 1)
      throw new Error('SVG replaced the real grid extent or reapplied baked satellite opacity.');
    const clip = container.querySelector('clipPath rect');
    if (Number(clip.getAttribute('width')) !== size.width || Number(clip.getAttribute('height')) !== size.height)
      throw new Error('SVG did not clip the aligned image to the actual canvas viewport.');
    const image = new Image();
    imageURL = URL.createObjectURL(new Blob([new XMLSerializer().serializeToString(container.querySelector('svg'))], { type: 'image/svg+xml' }));
    image.src = imageURL; await image.decode();
    const canvas = document.createElement('canvas'); canvas.width = Math.ceil(size.width); canvas.height = Math.ceil(size.height);
    const context = canvas.getContext('2d'); context.drawImage(image, 0, 0);
    const center = context.getImageData(Math.floor(size.width / 2), Math.floor(size.height / 2), 1, 1).data;
    const expectedAlpha = Math.round(capture.request.settings.satelliteOpacity * 255);
    if (center[3] !== expectedAlpha) throw new Error(`Real satellite SVG alpha ${center[3]} != baked alpha ${expectedAlpha}.`);
    return `Saved real ${satellite.width}x${satellite.height} satellite replay: SVG retains ${expectedWidth}x${expectedHeight} world extent, clips at the canvas edge and renders alpha ${expectedAlpha} exactly once.`;
  } finally {
    if (imageURL) URL.revokeObjectURL(imageURL);
    root.unmount(); container.remove();
  }
}
