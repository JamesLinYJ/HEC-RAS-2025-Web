import { spawn } from 'node:child_process';
import { access, mkdir, mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { build } from 'vite';

// Actual React controls with a deterministic fake transport. Real C# acceptance is separate.
const root = process.cwd();
const temporary = await mkdtemp(path.join(tmpdir(), 'hecras-result-groups-react-'));
const output = process.env.HECRAS_RESULT_GROUP_UI_OUTPUT;
let browser, exited;
try {
  const chrome = process.env.HECRAS_CHROME ?? '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
  await access(chrome);
  const entry = path.join(temporary, 'entry.ts');
  await writeFile(entry, `export { MapTimeAxis } from ${JSON.stringify(path.join(root, 'frontend/src/features/map/MapTimeAxis.tsx'))};\n` +
    `export { useMapResultFrames } from ${JSON.stringify(path.join(root, 'frontend/src/features/map/useMapResultFrames.ts'))};\n` +
    `export { useResultLayerSettings } from ${JSON.stringify(path.join(root, 'frontend/src/features/map/results-layer-settings/useResultLayerSettings.ts'))};\n` +
    `export { activeResultProfile } from ${JSON.stringify(path.join(root, 'frontend/src/features/map/results-layer-settings/activeResultProfile.ts'))};\n` +
    `export { loadMapDocumentCatalog } from ${JSON.stringify(path.join(root, 'frontend/src/features/map/ProjectTreeListener.ts'))};\n` +
    `export { createElement, useState, useEffect, useRef } from ${JSON.stringify(path.join(root, 'node_modules/react/index.js'))};\n` +
    `export { createRoot } from ${JSON.stringify(path.join(root, 'node_modules/react-dom/client.js'))};\n` +
    `import ${JSON.stringify(path.join(root, 'frontend/src/features/map/map.css'))};\n`);
  await build({ configFile: false, root, logLevel: 'silent', define: { 'process.env.NODE_ENV': '"production"' },
    build: { lib: { entry, formats: ['iife'], name: 'ResultGroupFixture', fileName: () => 'fixture.js' },
      outDir: path.join(temporary, 'bundle'), minify: false } });
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
  await command('Emulation.setDeviceMetricsOverride', { width: 1280, height: 720, deviceScaleFactor: 1, mobile: false }, session.sessionId);
  await evaluate(`document.body.innerHTML='<div id="root"></div>'`);
  const cssName = (await readdir(path.join(temporary, 'bundle'))).find(name => name.endsWith('.css'));
  const css = cssName ? await readFile(path.join(temporary, 'bundle', cssName), 'utf8') : '';
  await evaluate(`(() => {const style=document.createElement('style');style.textContent=${JSON.stringify(css)};document.head.append(style);})()`);
  await evaluate(await readFile(path.join(temporary, 'bundle/fixture.js'), 'utf8'));
  const report = await evaluate(`(${browserChecks.toString()})(ResultGroupFixture)`);
  report.layouts = [];
  for (const [width, innerWidth] of [[1300, 1500], [360, 390]]) {
    await command('Emulation.setDeviceMetricsOverride', { width: innerWidth, height: 720, deviceScaleFactor: 1, mobile: false }, session.sessionId);
    await evaluate(`window.fixtureWidth(${width})`);
    const layout = await evaluate(`(${layoutChecks.toString()})()`);
    report.layouts.push(layout);
    if (output) {
      await mkdir(output, { recursive: true });
      const shot = await command('Page.captureScreenshot', { format: 'png' }, session.sessionId);
      await writeFile(path.join(output, `result-layer-layout-${innerWidth}.png`), Buffer.from(shot.data, 'base64'));
    }
  }
  if (output) {
    await mkdir(output, { recursive: true });
    const shot = await command('Page.captureScreenshot', { format: 'png' }, session.sessionId);
    await writeFile(path.join(output, 'result-layer-react-fake-transport.png'), Buffer.from(shot.data, 'base64'));
    await writeFile(path.join(output, 'result-layer-react-fake-transport.json'), JSON.stringify(report, null, 2));
  }
  console.log(`PASS: actual React result controls, mixed types, shared timestamp across distinct ordinals, unconfirmed operation resume and verified readback (${report.checks} checks; fake transport).`);
  await command('Browser.close');
} finally {
  if (browser && browser.exitCode === null) browser.kill('SIGTERM');
  if (exited) await Promise.race([exited, new Promise(resolve => setTimeout(resolve, 3000))]);
  await rm(temporary, { recursive: true, force: true });
}

async function browserChecks(A) {
  // about:blank has no secure-context WebCrypto; the synthetic backend only
  // needs a stable identity, while real SHA verification is covered elsewhere.
  if (!crypto.subtle) Object.defineProperty(crypto, 'subtle', { value: { digest: async () => new Uint8Array(32).fill(11).buffer } });
  if (!crypto.randomUUID) Object.defineProperty(crypto, 'randomUUID', { value: () => '00000000-0000-4000-8000-000000000002' });
  let checks = 0;
  const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
  const check = (condition, reason) => { checks++; if (!condition) throw new Error(`${reason}\n${document.body.innerText}`); };
  const until = async (predicate, reason) => { for (let i = 0; i < 250; i++) { if (predicate()) return; await wait(20); }
    check(false, `${reason}; heights ${viewportChanges.join(',')}; renders ${calls.filter(call => call.method === 'map.resultRender').length}`); };
  const sha = char => char.repeat(64), operationID = '9'.repeat(32);
  const context = { projectSessionID: 'session', projectPath: '/fixture/project.ras', projectXml: '<Project />',
    projectRevision: sha('a'), selectedAsset: null };
  const node = n => ({ rootName: 'Results', nodeType: 'MultiResult', nodeName: `方案 ${n}`, nodePath: [1, n], assetID: `/fixture/plan${n}.hdf` });
  const source = n => ({ resultPath: `/fixture/result${n}.hdf`, revision: sha('d'),
    resultName: `结果 ${n} — 这里是用于检查截断和完整提示的很长计算结果名称 0123456789`, planName: `方案 ${n}`,
    endTime: null, isComputing: false, computeState: null, computeFailureReason: null, diagnosticStatus: null, diagnosticReadError: null });
  const group = n => ({ node: node(n), nodeKind: 'MultiResult', name: `方案 ${n} — 用于验证完整标题的很长结果组名称 0123456789`, selectedMapType: currentRevision === sha('c') ? 'Velocity' : n === 0 ? 'Depth' : 'Water Surface',
    profileIndex: 0, selections: [{ selectionIndex: 0, selectionMode: 'Explicit', selectedResult: source(n), choices: [source(n)] }] });
  const timeline = n => n === 0 ? [0, 1] : [1, 2, 3];
  const profile = time => ({ name: `01JAN2020 ${String(time + 1).padStart(2, '0')}:00:00`, time: `2020-01-01T${String(time + 1).padStart(2, '0')}:00:00` });
  let currentRevision = sha('b'), doc, capturedWrite;
  const calls = [], leases = [], rows = [];
  window.fixtureCalls = calls;
  const viewportChanges = [];
  const roots = () => [{ id: 'root', name: 'Results', sourceClass: 'TreeNodeVM', children: [] }];
  const tree = () => ({ roots: roots(), previousVisualizationRevision: currentRevision, visualizationRevision: currentRevision,
    expandedNodeIDs: [], selectedNodeID: null, hiddenLayerIDs: [] });
  const gateway = { supports: () => true, async request(method, p, opts) {
    calls.push({ method, p: structuredClone(p) });
    if (method === 'map.document.list') return { projectRevision: sha('a'), visualizationRevision: currentRevision,
      maps: [{ mapIndex: 0, mapName: 'Map', mapType: 'Project' }], activeMapIndex: 0 };
    if (method === 'map.tree.load') return tree();
    if (method === 'map.resultSources') return { projectPath: context.projectPath, projectRevision: sha('a'),
      projectXmlSHA: p.map.projectXmlSHA, visualizationRevision: currentRevision, mapID: doc.mapID, mapIndex: 0, mapName: 'Map', mapType: 'Project',
      contextToken: sha('f'), activeNodes: [group(0), group(1)], treeRoots: roots(), treeSelectionOverrides: [] };
    if (method === 'map.resultRender') {
      await wait(30);
      const n = p.node.nodeName.endsWith('0') ? 0 : 1, own = timeline(n);
      const anchor = p.sharedKeyframeAnchor;
      const selected = anchor ? own.findIndex(value => profile(value).time === anchor.time) : p.profileIndex;
      const sourceID = `source-${n}`, selectedStep = anchor?.time ?? profile(own[selected]).time;
      const steps = anchor ? anchor.profiles.map(item => ({ id: item.index, label: item.name, time: item.time }))
        : own.map((value, index) => ({ id: index, label: profile(value).name, time: profile(value).time }));
      const src = source(n), chosen = selected >= 0 ? profile(own[selected]) : null;
      const layer = chosen ? { id: `layer-${n}`, sourceID, sourceMapID: doc.mapID, resultPath: src.resultPath, revision: src.revision,
        resultName: src.resultName, mapType: group(n).selectedMapType, terrainPath: '/fixture/terrain', terrainRevision: sha('e'),
        profileIndex: selected, profileName: chosen.name, time: chosen.time, units: 'm', width: 1, height: 1, rgba: 'AAAA/w==',
        minX: n * 2, minY: 0, maxX: n * 2 + 1, maxY: 1, noDataValue: -9999, legendStops: [], selectionIndices: [0] } : null;
      const identity = anchor ? { node: anchor.node, selectionIndex: 0, resultPath: anchor.resultPath, revision: anchor.revision,
        runID: null, profileIndex: anchor.profileIndex, profileName: anchor.profileName, time: anchor.time } : null;
      return { contextToken: sha('f'), sourceID, selectedProfileIndex: anchor?.profileIndex ?? p.profileIndex,
        selectedProfileTime: selectedStep, steps, layers: layer ? [layer] : [], sources: [{ resultPath: src.resultPath, revision: src.revision,
          resultName: src.resultName, selectionIndices: [0], available: !!layer, profileIndex: selected >= 0 ? selected : null,
          unavailableReason: layer ? null : 'This Result has no profile within the 10 millisecond MultiResult timestamp tolerance.' }],
        unavailableReason: layer ? null : 'This Result has no profile within the 10 millisecond MultiResult timestamp tolerance.', sharedKeyframeAnchor: identity };
    }
    if (method === 'map.tree.perform') {
      check(p.action === 'setResultMapType' && p.name === 'Velocity', 'group action invalid');
      opts.onTransportState({ state: 'connected', operationId: operationID, recoveredAfterAttempt: 0 });
      currentRevision = sha('c'); capturedWrite = { ...tree(), previousVisualizationRevision: sha('b') };
      throw Object.assign(new Error('lost connection'), { operationId: operationID, terminalConfirmed: false });
    }
    throw new Error(`unexpected ${method}`);
  }, async resumeOperation(id) { calls.push({ method: 'resume', id }); check(id === operationID, 'resume changed operation id'); return capturedWrite; } };
  doc = (await A.loadMapDocumentCatalog(gateway, context)).documents[0];
  const h = A.createElement;
  function Fixture() {
    const [documentState, setDocument] = A.useState(doc), [guard, setGuard] = A.useState(false);
    const [width, setWidth] = A.useState(480);
    window.fixtureWidth = setWidth;
    const [height, setHeight] = A.useState(360);
    const box = A.useRef(null);
    A.useEffect(() => {
      const observer = new ResizeObserver(entries => {
        const height = Math.round(entries[0].contentRect.height);
        viewportChanges.push(height);
        setHeight(previous => previous === 600 - height ? previous : 600 - height);
      });
      observer.observe(box.current);
      return () => observer.disconnect();
    }, []);
    const frames = A.useMapResultFrames({ backend: gateway, context, document: documentState, language: 'zh',
      viewport: { minX: 0, minY: 0, maxX: 4, maxY: 4, pixelWidth: 480, pixelHeight: height } });
    const settings = A.useResultLayerSettings({ backend: gateway, context, document: documentState, frames, language: 'zh',
      scope: 'fixture', otherBusy: false, onGuardChange: setGuard,
      onBeginMapVisualizationMutation: () => { leases.push('begin'); return 'lease'; },
      onCommitMapVisualization: () => { leases.push('commit'); return true; },
      onEndMapVisualizationMutation: () => { leases.push('end'); },
      onInstall: readback => { leases.push('install'); setDocument(readback.document); return true; } });
    window.fixtureFrames = frames;
    const projected = A.activeResultProfile(frames);
    return h('div', { ref: box, style: { width } }, h(A.MapTimeAxis, { language: 'zh', controller: { ...frames,
      resultLayerSettings: settings, activeResultProfileReady: projected.ready,
      ...(guard ? { status: 'loading', canChangeSource: false, canPlay: false } : {}) } }));
  }
  function Owner() {
    const [mounted, setMounted] = A.useState(true);
    window.fixtureMount = setMounted;
    return mounted ? h(Fixture) : h('span', null, '地图界面已切换');
  }
  A.createRoot(document.getElementById('root')).render(h(Owner));
  await until(() => window.fixtureFrames?.status === 'ready' && window.fixtureFrames.visibleLayers.length === 1 &&
    window.fixtureFrames.groupRenderMessages.length > 0, 'unmatched group was not reported');
  check(document.querySelector('[data-testid=results-section-map-type]').selectedOptions[0].textContent.includes('各组不同'), 'mixed types missing');
  check(document.body.innerText.includes('活动结果组（2）'), 'target groups absent');
  const targetKey = window.fixtureFrames.nodeOptions.find(option => option.node.name.startsWith('方案 1')).key;
  const anchorKey = window.fixtureFrames.nodeOptions.find(option => option.node.name.startsWith('方案 0')).key;
  const warning = () => document.body.innerText.includes('当前组首个来源的时刻未读回');
  window.fixtureFrames.selectNode(targetKey);
  await until(() => window.fixtureFrames.status === 'ready' && window.fixtureFrames.activeNodeKey === targetKey && warning(),
    'unmatched target source was not explicitly reported');
  await wait(1200);
  const unmatchedCount = calls.filter(call => call.method === 'map.resultRender').length;
  await wait(1200);
  check(unmatchedCount === calls.filter(call => call.method === 'map.resultRender').length && warning(),
    `unmatched target caused repeated render/resize: heights ${viewportChanges.join(',')}`);
  window.fixtureFrames.selectNode(anchorKey);
  await until(() => window.fixtureFrames.activeNodeKey === anchorKey && window.fixtureFrames.status === 'loading', 'anchor switch was not pending');
  check(!warning(), 'unmatched warning leaked from the previous source while loading another group');
  await until(() => window.fixtureFrames.status === 'ready' && window.fixtureFrames.activeNodeKey === anchorKey, 'anchor switch did not settle');
  window.fixtureFrames.selectNode(targetKey);
  await until(() => window.fixtureFrames.status === 'ready' && window.fixtureFrames.activeNodeKey === targetKey && warning(), 'unmatched target did not settle');
  const range = document.querySelector('input[type=range]');
  range.value = '1'; range.dispatchEvent(new Event('input', { bubbles: true })); range.dispatchEvent(new Event('change', { bubbles: true }));
  // React's range tracker is controlled; invoking the same controller method tests the timeline action deterministically.
  window.fixtureFrames.selectProfile(1);
  await until(() => window.fixtureFrames.status === 'loading' && window.fixtureFrames.selectedProfileIndex === 1, 'new time was not pending');
  check(!warning() && !A.activeResultProfile(window.fixtureFrames).ready,
    'old unavailable indication or prior-frame permission leaked to the new time');
  await until(() => window.fixtureFrames.status === 'ready' && window.fixtureFrames.selectedProfileIndex === 1 &&
    window.fixtureFrames.visibleLayers.find(layer => layer.id === 'layer-1')?.profileIndex === 0, 'shared time did not match different local ordinals');
  check(window.fixtureFrames.visibleLayers.find(layer => layer.id === 'layer-0').profileIndex === 1, 'anchor ordinal changed');
  const shared = calls.filter(call => call.method === 'map.resultRender' && call.p.sharedKeyframeAnchor);
  check(shared.some(call => call.p.node.nodeName === '方案 1' && call.p.sharedKeyframeAnchor.profileIndex === 1 &&
    call.p.sharedKeyframeAnchor.time === profile(1).time), 'target did not receive exact shared time anchor');
  const local = A.activeResultProfile(window.fixtureFrames);
  check(local.ready && local.selectedProfileIndex === 0 && local.selectedProfileTime === profile(1).time,
    'non-anchor identify and analysis did not receive the verified local ordinal and time');
  const damaged = A.activeResultProfile({ ...window.fixtureFrames, renderSnapshot: { ...window.fixtureFrames.renderSnapshot,
    sources: window.fixtureFrames.renderSnapshot.sources.map(status => ({ ...status, profileIndex: 1 })) } });
  check(!damaged.ready && damaged.selectedProfileIndex === null, 'conflicting target ordinal was accepted');
  await wait(1500);
  const firstCount = calls.filter(call => call.method === 'map.resultRender').length;
  await wait(1500);
  const laterCount = calls.filter(call => call.method === 'map.resultRender').length;
  check(firstCount === laterCount && window.fixtureFrames.status === 'ready',
    `selecting a non-anchor group caused repeated render/resize: counts ${firstCount} -> ${laterCount}, heights ${viewportChanges.join(',')}`);
  const select = document.querySelector('[data-testid=results-section-map-type]');
  select.value = 'Velocity'; select.dispatchEvent(new Event('change', { bubbles: true }));
  await until(() => document.querySelector('[data-testid=results-section-resume]'), 'unknown operation not shown');
  check(document.querySelector('[data-testid=results-section-layer-properties] details')?.textContent.includes(operationID), 'original operation ID missing');
  check(document.querySelector('[data-testid=results-section-map-type]').disabled, 'unconfirmed write remained editable');
  check(leases.join(',') === 'begin', 'unknown write released or committed prematurely');
  window.fixtureMount(false);
  await until(() => !document.querySelector('[data-testid=results-section-layer-properties]'), 'map did not unmount');
  window.fixtureMount(true);
  await until(() => document.querySelector('[data-testid=results-section-resume]'), 'remount lost the original operation');
  check(document.querySelector('[data-testid=results-section-map-type]').disabled, 'remount released the unconfirmed write lock');
  check(document.querySelector('[data-testid=results-section-layer-properties] details')?.textContent.includes(operationID), 'remount lost original operation ID');
  document.querySelector('[data-testid=results-section-resume]').click();
  await until(() => document.body.innerText.includes('结果组地图类型已保存并重新读取') && window.fixtureFrames.status === 'ready', 'resumed write not verified');
  check(calls.filter(call => call.method === 'map.tree.perform').length === 1, 'write was posted twice');
  check(calls.filter(call => call.method === 'resume').length === 1, 'original operation was not resumed once');
  check(leases.join(',') === 'begin,commit,install,end', 'lease was not held through readback');
  check(document.querySelector('[data-testid=results-section-map-type]').value === 'Velocity', 'new type not shown from readback');
  window.fixtureFrames.selectProfile(1);
  await until(() => window.fixtureFrames.status === 'ready' && window.fixtureFrames.selectedProfileIndex === 1 &&
    window.fixtureFrames.visibleLayers.length === 2, 'shared time did not reload after remount');
  check(window.fixtureFrames.visibleLayers.find(layer => layer.id === 'layer-0')?.time ===
    window.fixtureFrames.visibleLayers.find(layer => layer.id === 'layer-1')?.time,
    'shared time was not restored after a page remount and map revision');
  rows.push(...window.fixtureFrames.visibleLayers.map(layer => ({ id: layer.id, index: layer.profileIndex, time: layer.time })));
  return { checks, rows, operationID, requests: calls.map(call => call.method), leases };
}

async function layoutChecks() {
  const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
  let stable = false;
  for (let i = 0; i < 30; i++) {
    await wait(250);
    const count = window.fixtureCalls.filter(call => call.method === 'map.resultRender').length;
    if (window.fixtureFrames.status !== 'ready') continue;
    await wait(500);
    if (window.fixtureFrames.status === 'ready' && count === window.fixtureCalls.filter(call => call.method === 'map.resultRender').length) {
      stable = true;
      break;
    }
  }
  if (!stable) throw new Error(`layout did not settle at ${window.innerWidth}px`);
  const axis = document.querySelector('.map-time-axis');
  const bounds = axis.getBoundingClientRect();
  const elements = [...axis.querySelectorAll('select,button,output,input[type=range],.results-section-target,.map-result-time-current,.map-time-caption-label')];
  const outside = elements.filter(element => {
    const rect = element.getBoundingClientRect();
    return rect.left < bounds.left - 1 || rect.right > bounds.right + 1;
  });
  if (outside.length) throw new Error(`controls overflow at ${window.innerWidth}px: ${outside.map(element => element.outerHTML.slice(0, 140)).join(', ')}`);
  const controls = [...axis.querySelectorAll('select,button')];
  if (controls.some(element => element.getBoundingClientRect().height < 33)) throw new Error(`control below 34px at ${window.innerWidth}px`);
  const range = axis.querySelector('input[type=range]');
  if (!range || range.getBoundingClientRect().width < 100) throw new Error(`range too narrow at ${window.innerWidth}px`);
  const target = axis.querySelector('.results-section-target');
  const source = axis.querySelector('[data-testid^=map-result-source-]');
  const keyframe = axis.querySelector('[data-testid=results-section-keyframe]');
  const time = axis.querySelector('.map-result-time-current');
  if (!target?.title.includes('0123456789') || target.scrollWidth <= target.clientWidth ||
      !source?.title.includes('0123456789') || source.scrollWidth <= source.clientWidth ||
      !keyframe?.title.includes('0123456789') || !time?.title) throw new Error('truncated values must retain full titles');
  const enabled = controls.find(element => !element.disabled);
  enabled?.focus();
  if (!enabled || getComputedStyle(enabled).outlineStyle === 'none' || parseFloat(getComputedStyle(enabled).outlineWidth) < 2) {
    throw new Error('keyboard focus indicator is missing');
  }
  return { viewportWidth: window.innerWidth, axisWidth: bounds.width, axisHeight: bounds.height,
    rangeWidth: range.getBoundingClientRect().width, controlCount: controls.length,
    targetTruncated: true, sourceTruncated: true, titlesRetained: true, focusVisible: true, horizontalOverflow: false };
}
