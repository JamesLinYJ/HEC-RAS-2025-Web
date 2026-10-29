import { spawn } from 'node:child_process';
import { access, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'vite';

// Focused fake-transport/React lifecycle regression. The real C# file and HTTP checks run separately.
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const temporary = await mkdtemp(path.join(tmpdir(), 'hecras-feature-query-'));
let browser, exited;
try {
  const chrome = process.env.HECRAS_CHROME ?? '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
  await access(chrome);
  const entry = path.join(temporary, 'entry.ts');
  await writeFile(entry, `import { FeatureQueryDialog } from ${JSON.stringify(path.join(root, 'frontend/src/features/map/query-import/FeatureQueryDialog.tsx'))};
import { queryInputError } from ${JSON.stringify(path.join(root, 'frontend/src/features/map/query-import/featureQueryProtocol.ts'))};
import { readBackgroundMapProjection } from ${JSON.stringify(path.join(root, 'frontend/src/features/map/background/protocol.ts'))};
import { ServerBackendError } from ${JSON.stringify(path.join(root, 'frontend/src/platform/ServerBackend.ts'))};
import { createElement, useState } from ${JSON.stringify(path.join(root, 'node_modules/react/index.js'))};
import { createRoot } from ${JSON.stringify(path.join(root, 'node_modules/react-dom/client.js'))};
export { FeatureQueryDialog, queryInputError, readBackgroundMapProjection, ServerBackendError, createElement, useState, createRoot };`);
  await build({ configFile: false, root, logLevel: 'silent', define: { 'process.env.NODE_ENV': '"production"' }, build: {
    lib: { entry, formats: ['iife'], name: 'FeatureQueryFixture', fileName: () => 'fixture.js' },
    outDir: path.join(temporary, 'bundle'), minify: false,
  } });
  browser = spawn(chrome, ['--headless=new', '--no-first-run', '--no-default-browser-check', '--disable-background-networking',
    '--disable-component-update', '--disable-sync', '--disable-extensions', '--remote-debugging-pipe',
    `--user-data-dir=${path.join(temporary, 'profile')}`, 'about:blank'], { stdio: ['ignore', 'ignore', 'pipe', 'pipe', 'pipe'] });
  exited = new Promise(resolve => browser.once('exit', resolve));
  let sequence = 0, incoming = '', diagnostic = '';
  const pending = new Map();
  browser.stderr.on('data', bytes => { diagnostic += bytes.toString(); });
  const failPending = error => { for (const item of pending.values()) { clearTimeout(item.timeout); item.reject(error); } pending.clear(); };
  browser.on('error', failPending);
  browser.stdio[3].on('error', failPending);
  browser.on('exit', code => failPending(new Error(`Headless Chrome exited (${code}): ${diagnostic}`)));
  browser.stdio[4].on('data', bytes => {
    incoming += bytes.toString();
    let end;
    while ((end = incoming.indexOf('\0')) !== -1) {
      const response = JSON.parse(incoming.slice(0, end)); incoming = incoming.slice(end + 1);
      const item = pending.get(response.id);
      if (item) { pending.delete(response.id); clearTimeout(item.timeout); response.error ? item.reject(new Error(JSON.stringify(response.error))) : item.resolve(response.result); }
    }
  });
  const command = (method, params = {}, sessionId) => new Promise((resolve, reject) => {
    const id = ++sequence, timeout = setTimeout(() => { pending.delete(id); reject(new Error(`CDP timeout: ${method}`)); }, 30000);
    pending.set(id, { resolve, reject, timeout }); browser.stdio[3].write(`${JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) })}\0`);
  });
  const target = await command('Target.createTarget', { url: 'about:blank' });
  const session = await command('Target.attachToTarget', { targetId: target.targetId, flatten: true });
  const evaluate = async expression => {
    const response = await command('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true }, session.sessionId);
    if (response.exceptionDetails) throw new Error(response.exceptionDetails.exception?.description ?? response.exceptionDetails.text);
    return response.result.value;
  };
  await evaluate(`document.body.innerHTML = '<div id="root"></div>'`);
  await evaluate(await readFile(path.join(temporary, 'bundle/fixture.js'), 'utf8'));
  await evaluate(`(${browserChecks.toString()})(FeatureQueryFixture)`);
  console.log('PASS: Dialog focus, current and stale read diagnostics, owner changes, stage cleanup, Local/EPSG:4326 projection boundary, unknown export resume, dirty owners, cancellations, import resume, and own .vis advancement.');
  await command('Browser.close');
} finally {
  if (browser && browser.exitCode === null) browser.kill('SIGTERM');
  if (exited) await Promise.race([exited, new Promise(resolve => setTimeout(resolve, 3000))]);
  await rm(temporary, { recursive: true, force: true });
}

async function browserChecks(A) {
  const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
  const check = (condition, reason) => { if (!condition) throw new Error(`${reason}\n${document.body.innerText}`); };
  const until = async (predicate, reason) => { for (let i = 0; i < 120; i++) { if (predicate()) return; await wait(15); } check(false, reason); };
  const sha = char => char.repeat(64), id = char => char.repeat(32);
  const context = { projectSessionID: 'session', projectPath: '/fixture/project.ras', projectXml: null,
    projectRevision: sha('a'), selectedAsset: null };
  const epsg = A.readBackgroundMapProjection('<Project><Projection Type="EPSG">4326</Projection></Project>');
  const local = A.readBackgroundMapProjection('<Project><Projection Type="Local">Local</Projection></Project>');
  const projectionForm = { nldLayerId: 'AlignmentLines', useBoundaryPolygon: false, polygonSourceId: '', rawQl: '', queryLabel: '' };
  const extent = { minX: -1, minY: -1, maxX: 1, maxY: 1 };
  check(epsg.projection?.type === 'EPSG' && epsg.projection.value === '4326' &&
    A.queryInputError('NLD', projectionForm, [], extent, true, epsg.issue) === null, 'EPSG:4326 must remain eligible for server-side transformation');
  check(local.issue === 'local' && A.queryInputError('NLD', projectionForm, [], extent, true, local.issue)?.includes('Local'),
    'Local project must block online queries');
  check(A.queryInputError('NSI', { ...projectionForm, useBoundaryPolygon: true, polygonSourceId: 'polygon' },
    [{ id: 'polygon', name: 'polygon' }], null, false, local.issue)?.includes('Local'), 'NSI polygon queries also require a usable project projection');
  const makeDocument = (name, revision = sha('b')) => ({ id: name, mapID: name, name, mapIndex: 0, mapType: 'Project', visualizationRevision: revision, roots: [] });
  const map = request => ({ projectPath: request.projectPath, projectRevision: sha('a'), projectXmlSHA: null,
    visualizationPath: '/fixture/project.vis', visualizationRevision: request.expectedVisualizationRevision,
    mapID: request.mapID, mapIndex: request.mapIndex, mapName: request.mapName, mapType: request.mapType, isPersisted: true });
  const queried = (request, resultId) => ({ map: map(request.map), kind: request.kind, resultId, hasFeatures: true, info: 'query info', layers: [{
    name: 'Alignment', sourcePath: `/stage/${resultId}/feature-00.geojson`, sourceRevision: sha('d'), mapType: 'PolylinesVector',
    lineColor: '#0080FFFF', fillColor: '#00000000', lineWidth: 3, coordinateSpace: 'ProjectWorld' }] });
  const preview = request => ({ map: request.expectedMap, resultId: request.resultId, coordinateSpace: 'ProjectWorld', budget: {
    maxFeatures: 2000, maxCoordinates: 100000, maxPayloadBytes: 6291456, maxSourceBytes: 134217728 },
    totalFeatureCount: 1, returnedFeatureCount: 1, totalCoordinateCount: 2, returnedCoordinateCount: 2, truncated: false, warnings: [], layers: [{
      layerId: 'feature-00.geojson', name: 'Alignment', sourceRevision: sha('d'), mapType: 'PolylinesVector',
      lineColor: '#0080FFFF', fillColor: '#00000000', lineWidth: 3, totalFeatureCount: 1, returnedFeatureCount: 1,
      totalCoordinateCount: 2, returnedCoordinateCount: 2, truncated: false,
      featureCollection: { type: 'FeatureCollection', features: [{ type: 'Feature', geometry: { type: 'LineString', coordinates: [[1, 2], [3, 4]] }, properties: { NAME: 'test' } }] } }] });
  const exported = request => ({ map: map(request.map), info: 'saved', warnings: [], sources: [{ path: '/exports/Alignment.geojson',
    revision: sha('d'), name: 'Alignment', mapType: 'PolylinesVector', featureCount: 1, lineColor: '#0080FFFF', fillColor: '#00000000', lineWidth: 3,
    family: { family: 'geojson', sourcePath: '/exports/Alignment.geojson', revision: sha('e'), files: [{ inputPath: '/exports/Alignment.geojson',
      path: '/exports/Alignment.geojson', role: 'source', required: true, exists: true, revision: sha('d') }] } }] });
  const requests = [], registrations = [], queryJobs = [], saves = [], resumes = [];
  const residualPath = String.raw`C:\Exports\Retained.shp`;
  let saveMode = 'unknown', changeDocument, setProjectionIssue, resolveFirstSnapshot;
  const backend = { supports: () => true, request(method, parameters, options = {}) {
    requests.push({ method, parameters });
    if (method === 'featureQuery.snapshot') {
      const snapshot = { map: map(parameters.map), polygonSources: [], selectedPolygonId: null };
      if (!resolveFirstSnapshot) return new Promise(resolve => { resolveFirstSnapshot = () => resolve(snapshot); });
      return Promise.resolve(snapshot);
    }
    if (method === 'featureQuery.query') return new Promise(resolve => { queryJobs.push({ parameters, options, resolve }); });
    if (method === 'featureQuery.preview') return Promise.resolve(preview(parameters));
    if (method === 'featureQuery.discard') return Promise.resolve({ resultId: parameters.resultId, discarded: true, alreadyMissing: false });
    if (method === 'featureQuery.save') {
      saves.push(parameters); options.onTransportState?.({ state: 'connected', operationId: id('f'), recoveredAfterAttempt: 0 });
      if (saveMode === 'cancel') return Promise.resolve({ state: 'canceled', cleanupComplete: true });
      return Promise.reject(Object.assign(new Error('lost transport'), { operationId: id('f'), terminalConfirmed: false,
        diagnosticMessage: `清理失败：${residualPath}`, details: { remainingPath: residualPath } }));
    }
    throw new Error(`unexpected ${method}`);
  }, resumeOperation(operationID) { resumes.push(operationID); return Promise.resolve(exported(saves[0])); } };
  const h = A.createElement;
  function Fixture() {
    const [doc, setDoc] = A.useState(makeDocument('Map A'));
    const [projectionIssue, setProjection] = A.useState(null);
    changeDocument = setDoc;
    setProjectionIssue = setProjection;
    return h(A.FeatureQueryDialog, { backend, context, document: doc, backendRevision: 0, extent: { minX: 0, minY: 0, maxX: 10, maxY: 10 },
      coordinatesAreProject: true, projectionIssue, language: 'zh', kind: 'NLD', dirtyOwner: 'map.query:fixture', onClose: () => {},
      onDirtyStateChange: (owner, dirty) => registrations.push({ owner, dirty }), renderControl: () => h('p', null, 'query fixture'),
      onImportSources: async (_context, _target, _sources, isCurrent) => {
        check(isCurrent(), 'source must be current when import starts');
        return { status: 'unknown', imported: [], failures: [], operationID: id('9'), resume: async () => {
          check(isCurrent(), 'source must remain current when import resumes');
          setDoc(current => ({ ...current, visualizationRevision: sha('c') }));
          return { status: 'complete', imported: ['/exports/Alignment.geojson'], failures: [] };
        } };
      },
    });
  }
  const root = A.createRoot(document.getElementById('root')); root.render(h(Fixture));
  const button = text => [...document.querySelectorAll('button')].find(element => element.textContent.trim() === text);
  await until(() => resolveFirstSnapshot && document.querySelector('.feature-query-close')?.disabled, 'snapshot should remain pending');
  const dialog = document.querySelector('.feature-query-dialog');
  check(dialog.contains(document.activeElement), 'focus left the dialog while the snapshot was loading');
  const outside = document.createElement('button'); outside.textContent = 'outside'; document.body.append(outside); outside.focus();
  check(dialog.contains(document.activeElement), 'focus was not trapped in the open dialog');
  const activeInput = [...document.querySelectorAll('input')].find(element => element.placeholder.includes('/data/exports'));
  activeInput.focus();
  check(document.activeElement === activeInput, 'fixture could not focus an enabled field while loading');
  resolveFirstSnapshot();
  await until(() => button('查询区域') && !button('查询区域').disabled, 'initial snapshot');
  check(dialog.contains(document.activeElement) && document.activeElement === activeInput,
    'snapshot completion lost focus or stole focus from the field being edited');
  button('查询区域').focus();
  button('查询区域').click();
  await until(() => queryJobs.length === 1, 'first query');
  await wait(20);
  check(dialog.contains(document.activeElement), 'disabling the focused query button moved focus outside the dialog');
  changeDocument(makeDocument('Map B'));
  await until(() => requests.filter(item => item.method === 'featureQuery.snapshot').length === 2, 'new map snapshot');
  check(!document.querySelector('.feature-query-preview-svg'), 'old owner preview remained visible');
  queryJobs[0].resolve(queried(queryJobs[0].parameters, id('1')));
  await until(() => requests.some(item => item.method === 'featureQuery.discard' && item.parameters.resultId === id('1')), 'stale stage not cleaned');
  await until(() => !button('查询区域').disabled, 'new map query enabled');
  button('查询区域').click();
  await until(() => queryJobs.length === 2, 'second query');
  queryJobs[1].resolve(queried(queryJobs[1].parameters, id('2')));
  await until(() => document.querySelector('.feature-query-preview-svg') && document.body.innerText.includes('test'), 'real geometry or attributes absent');
  changeDocument(makeDocument('Map C'));
  await until(() => requests.filter(item => item.method === 'featureQuery.snapshot').length === 3, 'third owner snapshot');
  check(!document.querySelector('.feature-query-preview-svg') && !document.body.innerText.includes('test'), 'visible old-owner preview was not hidden');
  await until(() => requests.some(item => item.method === 'featureQuery.discard' && item.parameters.resultId === id('2')), 'displayed old stage not cleaned');
  setProjectionIssue('local');
  await until(() => document.body.innerText.includes('Local 局部坐标'), 'Local projection blocker absent');
  check(button('查询区域').disabled, 'Local projection must not dispatch a network query');
  setProjectionIssue(null);
  await until(() => !button('查询区域').disabled, 'query should allow supported projection after blocker clears');
  button('查询区域').click();
  await until(() => queryJobs.length === 3, 'third query');
  queryJobs[2].resolve(queried(queryJobs[2].parameters, id('3')));
  await until(() => document.querySelector('.feature-query-preview-svg') && document.body.innerText.includes('test'), 'third query preview');
  const directory = [...document.querySelectorAll('input')].find(element => element.placeholder.includes('/data/exports'));
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(directory, '/exports');
  directory.dispatchEvent(new Event('input', { bubbles: true }));
  await until(() => !button('导出文件').disabled, 'export enabled');
  button('导出文件').click();
  await until(() => button('继续核对原导出任务'), 'unknown export not retained');
  const diagnostics = [...document.querySelectorAll('details')].find(item => item.querySelector('summary')?.textContent === '服务诊断');
  check(diagnostics?.querySelector('pre')?.textContent.includes(residualPath), 'server diagnostic must retain the exact output path');
  check(saves.length === 1 && document.querySelector('.feature-query-close').disabled, 'unknown export must lock and retain one POST');
  check(registrations.some(event => event.owner === 'map.write:feature-query-export:map.query:fixture' && event.dirty), 'export write owner absent');
  button('继续核对原导出任务').click();
  await until(() => document.querySelector('.feature-query-export-files') && !button('加入地图').disabled, 'resumed receipt missing');
  check(saves.length === 1 && resumes.length === 1 && resumes[0] === id('f'), 'resume did not use the original operation ID');
  check(registrations.some(event => event.owner === 'map.write:feature-query-export:map.query:fixture' && !event.dirty), 'write owner not released after confirmed receipt');
  check(registrations.some(event => event.owner === 'map.query:fixture' && event.dirty), 'ordinary draft owner absent');
  button('加入地图').click();
  await until(() => button('继续核对原加入任务'), 'unknown import not retained');
  check(document.querySelector('.feature-query-close').disabled, 'unknown import must disable close');
  button('继续核对原加入任务').click();
  await until(() => document.body.innerText.includes('已确认导出文件加入地图。'), 'import resume did not complete');
  await until(() => requests.filter(item => item.method === 'featureQuery.snapshot').length === 4, 'updated map snapshot missing');
  check(document.body.innerText.includes('/exports/Alignment.geojson'), 'export/import result disappeared after own .vis advancement');
  await until(() => !button('查询区域').disabled, 'query after own .vis advancement');
  button('查询区域').click();
  await until(() => queryJobs.length === 4, 'fourth query');
  queryJobs[3].resolve(queried(queryJobs[3].parameters, id('4')));
  await until(() => document.querySelector('.feature-query-preview-svg') && !button('导出文件').disabled, 'fourth preview');
  saveMode = 'cancel'; button('导出文件').click();
  await until(() => document.body.innerText.includes('服务器已确认取消导出'), 'canceled terminal result not handled');
  check(saves.length === 2 && registrations.at(-1)?.dirty !== true, 'cancelled export did not release its write owner');
  await until(() => !button('查询区域').disabled, 'query after canceled export');
  button('查询区域').click();
  await until(() => queryJobs.length === 5 && button('取消查询'), 'cancel query available');
  button('取消查询').click();
  check(queryJobs[4].options.signal.aborted, 'query cancellation was not signaled');
  queryJobs[4].resolve(queried(queryJobs[4].parameters, id('5')));
  await until(() => requests.some(item => item.method === 'featureQuery.discard' && item.parameters.resultId === id('5')), 'finished query after cancellation was not cleaned');
  root.unmount();

  // The ServerBackend exposes a localized message separately from raw provider diagnostics.
  // Check snapshot/query display and ensure superseded reads cannot leak their errors.
  const snapshots = [], failedQueries = [];
  let changeDiagnosticDocument;
  const diagnosticBackend = { supports: () => true, request(method, parameters) {
    if (method === 'featureQuery.snapshot') return new Promise((resolve, reject) => snapshots.push({ parameters, resolve, reject }));
    if (method === 'featureQuery.query') return new Promise((resolve, reject) => failedQueries.push({ resolve, reject }));
    throw new Error(`unexpected diagnostic fixture request: ${method}`);
  } };
  function DiagnosticFixture() {
    const [doc, setDoc] = A.useState(makeDocument('Map D'));
    changeDiagnosticDocument = setDoc;
    return h(A.FeatureQueryDialog, { backend: diagnosticBackend, context, document: doc, backendRevision: 0,
      extent: { minX: 0, minY: 0, maxX: 10, maxY: 10 }, coordinatesAreProject: true, language: 'zh', kind: 'NLD',
      dirtyOwner: 'map.query:diagnostics', onClose: () => {}, renderControl: () => h('p', null, 'query fixture') });
  }
  const diagnosticRoot = A.createRoot(document.getElementById('root')); diagnosticRoot.render(h(DiagnosticFixture));
  const issue = () => document.querySelector('.feature-query-status .feature-query-error');
  const failure = detail => new A.ServerBackendError('OPERATION_FAILED', detail, { raw: '<b>literal diagnostic</b>' });
  await until(() => snapshots.length === 1, 'diagnostic snapshot missing');
  const snapshotFailure = failure('snapshot TLS failure'); snapshots[0].reject(snapshotFailure);
  await until(() => issue()?.querySelector('details pre'), 'snapshot diagnostic missing');
  check(issue().textContent.includes(snapshotFailure.localizedMessage('zh')) && issue().querySelector('pre').textContent.includes('snapshot TLS failure'),
    'snapshot did not preserve localized message and server diagnostic');
  check(!issue().querySelector('details').open && !issue().querySelector('pre b') &&
    issue().querySelector('pre').textContent.includes('<b>literal diagnostic</b>'), 'diagnostic was not collapsed and rendered as text');
  changeDiagnosticDocument(makeDocument('Map E'));
  await until(() => snapshots.length === 2, 'second diagnostic snapshot missing');
  check(!issue(), 'previous snapshot diagnostic was not cleared on owner change');
  changeDiagnosticDocument(makeDocument('Map F'));
  await until(() => snapshots.length === 3, 'third diagnostic snapshot missing');
  snapshots[1].reject(failure('stale snapshot failure'));
  snapshots[2].resolve({ map: map(snapshots[2].parameters.map), polygonSources: [], selectedPolygonId: null });
  await until(() => button('查询区域') && !button('查询区域').disabled, 'current diagnostic snapshot not ready');
  check(!issue(), 'stale snapshot failure replaced the current owner');
  button('查询区域').click();
  await until(() => failedQueries.length === 1, 'diagnostic query missing');
  failedQueries[0].reject(failure('query TLS failure'));
  await until(() => issue()?.querySelector('details pre'), 'query diagnostic missing');
  check(issue().querySelector('pre').textContent.includes('query TLS failure'), 'query diagnostic was lost');
  await until(() => !button('查询区域').disabled, 'query retry not enabled');
  button('查询区域').click();
  await until(() => failedQueries.length === 2, 'diagnostic query retry missing');
  check(!issue(), 'query retry retained the previous diagnostic');
  changeDiagnosticDocument(makeDocument('Map G'));
  await until(() => snapshots.length === 4, 'final diagnostic snapshot missing');
  failedQueries[1].reject(failure('stale query failure'));
  snapshots[3].resolve({ map: map(snapshots[3].parameters.map), polygonSources: [], selectedPolygonId: null });
  await until(() => !button('查询区域').disabled, 'final diagnostic snapshot not ready');
  check(!issue(), 'stale query failure replaced the current owner');
  diagnosticRoot.unmount();
}
