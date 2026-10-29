import { spawn } from 'node:child_process';
import { access, mkdir, mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'vite';

// Actual React controls with a fake transport. This is not a real OSM or C# service check.
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const output = process.env.HECRAS_OSM_STYLE_OUTPUT;
const temporary = await mkdtemp(path.join(tmpdir(), 'hecras-osm-picker-'));
let browser, exited;
try {
  const chrome = process.env.HECRAS_CHROME ?? '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
  await access(chrome);
  const entry = path.join(temporary, 'entry.ts');
  await writeFile(entry, `export { OSMStylePicker } from ${JSON.stringify(path.join(root, 'frontend/src/features/map/OSMStylePicker.tsx'))};\n` +
    `export { createElement, useState } from ${JSON.stringify(path.join(root, 'node_modules/react/index.js'))};\n` +
    `export { createRoot } from ${JSON.stringify(path.join(root, 'node_modules/react-dom/client.js'))};\n`);
  await build({ configFile: false, root, logLevel: 'silent', define: { 'process.env.NODE_ENV': '"production"' },
    build: { lib: { entry, formats: ['iife'], name: 'OSMPickerFixture', fileName: () => 'fixture.js' },
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
  await command('Emulation.setDeviceMetricsOverride', { width: 700, height: 900, deviceScaleFactor: 1, mobile: false }, session.sessionId);
  await evaluate(`document.body.innerHTML='<main style="width:320px;padding:20px;font-family:system-ui"><div id="root"></div></main>'`);
  const cssName = (await readdir(path.join(temporary, 'bundle'))).find(name => name.endsWith('.css'));
  const css = cssName ? await readFile(path.join(temporary, 'bundle', cssName), 'utf8') : '';
  await evaluate(`(() => {const style=document.createElement('style');style.textContent=${JSON.stringify(css)};document.head.append(style);})()`);
  await evaluate(await readFile(path.join(temporary, 'bundle/fixture.js'), 'utf8'));
  const report = await evaluate(`(${browserChecks.toString()})(OSMPickerFixture)`);
  if (output) {
    await mkdir(output, { recursive: true });
    const shot = await command('Page.captureScreenshot', { format: 'png' }, session.sessionId);
    await writeFile(path.join(output, 'osm-style-react-fake-transport.png'), Buffer.from(shot.data, 'base64'));
    await writeFile(path.join(output, 'osm-style-react-fake-transport.json'), JSON.stringify(report, null, 2));
  }
  console.log(`PASS: OSM catalog and explicit preview React lifecycle (${report.checks} checks; fake transport; ${report.previewBytes} byte PNG).`);
  await command('Browser.close');
} finally {
  if (browser && browser.exitCode === null) browser.kill('SIGTERM');
  if (exited) await Promise.race([exited, new Promise(resolve => setTimeout(resolve, 3000))]);
  await rm(temporary, { recursive: true, force: true });
}

async function browserChecks(A) {
  const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
  const jobs = [], changes = [], cacheEvents = [];
  let checks = 0;
  const check = (condition, reason) => { checks++; if (!condition) throw new Error(`${reason}\n${document.body.innerText}`); };
  const until = async (predicate, reason) => { for (let i = 0; i < 250; i++) { if (predicate()) return; await wait(10); } check(false, reason); };
  const backend = label => ({ label, supports: method => ['map.background.styles', 'map.background.cache'].includes(method),
    request(method, params, options = {}) {
      return new Promise((resolve, reject) => jobs.push({ backend: label, method, params, options, resolve, reject }));
    } });
  const firstBackend = backend('first'), secondBackend = backend('second');
  const catalogEntry = id => ({ id, name: id === 'basic' ? 'Basic' : 'Dark', description: '', previewContentType: null, preview: null, warning: null });
  const reply = (job, patch = {}) => ({ requestID: job.params.requestID, styles: [catalogEntry('basic'), catalogEntry('dark')], warning: null, ...patch });
  const previewReply = (job, patch = {}) => reply(job, { styles: [{ ...catalogEntry(job.params.styleID), ...patch }], warning: patch.warning ?? null });
  const button = label => [...document.querySelectorAll('button')].find(item => item.textContent.trim() === label);
  const previewStatus = () => document.querySelector('[data-testid=osm-style-preview-status]')?.textContent ?? '';
  const select = () => document.querySelector('.osm-style-select select');
  const load = () => document.querySelector('[data-testid=osm-style-preview-load]');
  const latest = () => jobs.at(-1);
  const h = A.createElement;
  function Fixture() {
    const [owner, setOwner] = A.useState(firstBackend), [revision, setRevision] = A.useState(0),
      [state, setState] = A.useState({ OsmStyle: 'basic', Density: 0, ShowLabels: true }),
      [mounted, setMounted] = A.useState(true);
    window.fixtureOwner = setOwner; window.fixtureRevision = setRevision; window.fixtureMount = setMounted; window.fixtureState = state;
    return mounted ? h(A.OSMStylePicker, { backend: owner, backendRevision: revision, state, language: 'zh',
      onChange: patch => { changes.push(patch); setState(previous => ({ ...previous, ...patch })); },
      onCacheCleared: () => cacheEvents.push('cleared') }) : h('span', null, '已卸载');
  }
  A.createRoot(document.getElementById('root')).render(h(Fixture));
  await until(() => jobs.length === 1, 'initial catalog request missing');
  const initial = latest();
  check(initial.method === 'map.background.styles' && initial.params.catalogOnly === true && !('styleID' in initial.params), 'initial request was not catalog-only');
  check(changes.length === 0, 'catalog request changed the style draft');
  initial.resolve(reply(initial));
  await until(() => select()?.value === 'basic' && !select().disabled && load(), 'catalog did not make styles selectable');
  check(previewStatus().includes('尚未加载') && !document.querySelector('.osm-style-preview img'), 'unrequested preview looked loaded');
  check(jobs.length === 1, 'default style was downloaded automatically');

  load().click();
  await until(() => jobs.length === 2 && document.querySelector('[data-testid=osm-style-preview-cancel]'), 'explicit preview request missing');
  const oldBasic = latest();
  check(oldBasic.params.styleID === 'basic' && !('catalogOnly' in oldBasic.params), 'preview request did not use the exact selected ID');
  check(previewStatus().includes('不影响其他编辑') && !select().disabled && !document.querySelector('.osm-style-density input').disabled &&
    !document.querySelector('.osm-style-labels input').disabled, 'preview unnecessarily blocked other editing');
  select().value = 'dark'; select().dispatchEvent(new Event('change', { bubbles: true }));
  await until(() => window.fixtureState.OsmStyle === 'dark' && previewStatus().includes('尚未加载'), 'style switch did not clear preview');
  check(oldBasic.options.signal.aborted, 'style switch did not abort the old preview');
  oldBasic.resolve(previewReply(oldBasic, { preview: 'iVBORw0KGgo=', previewContentType: 'image/png' }));
  await wait(70);
  check(previewStatus().includes('尚未加载') && !document.querySelector('.osm-style-preview img') && jobs.length === 2,
    'late old-style response was accepted or another preview was automatically fetched');

  load().click(); await until(() => jobs.length === 3, 'wrong-request-ID test not submitted');
  const wrongRequest = latest(); wrongRequest.resolve(reply(wrongRequest, { requestID: 'different', styles: [catalogEntry('dark')] }));
  await until(() => previewStatus().includes('失败'), 'wrong request ID was accepted');
  check(button('重试预览') && !document.querySelector('.osm-style-preview img'), 'wrong ID did not remain retryable');
  button('重试预览').click(); await until(() => jobs.length === 4, 'wrong-style test not submitted');
  const wrongStyle = latest(); wrongStyle.resolve(reply(wrongStyle, { styles: [catalogEntry('basic')] }));
  await until(() => previewStatus().includes('失败'), 'wrong style ID was accepted');
  check(window.fixtureState.OsmStyle === 'dark', 'invalid response changed the draft');
  button('重试预览').click(); await until(() => jobs.length === 5, 'multiple-style test not submitted');
  const multiple = latest(); multiple.resolve(reply(multiple));
  await until(() => previewStatus().includes('失败'), 'multiple styles were accepted for one selected preview');
  button('重试预览').click(); await until(() => jobs.length === 6, 'partial test not submitted');
  const partial = latest(); partial.resolve(previewReply(partial, { warning: 'preview failed' }));
  await until(() => previewStatus().includes('未完全加载'), 'partial result was not marked incomplete');
  check(document.body.innerText.includes('尚未确认此样式是否有预览图片') && !document.body.innerText.includes('此样式未提供预览图片'),
    'partial failure was misrepresented as a confirmed absent image');
  await wait(80); check(jobs.length === 6, 'partial result automatically retried');

  const canvas = document.createElement('canvas'); canvas.width = 400; canvas.height = 400;
  const ctx = canvas.getContext('2d'), pixels = ctx.createImageData(400, 400);
  let seed = 23917;
  for (let i = 0; i < pixels.data.length; i += 4) {
    seed ^= seed << 13; seed ^= seed >>> 17; seed ^= seed << 5;
    pixels.data[i] = seed & 255; pixels.data[i + 1] = (seed >>> 8) & 255;
    pixels.data[i + 2] = (seed >>> 16) & 255; pixels.data[i + 3] = 255;
  }
  ctx.putImageData(pixels, 0, 0);
  const image = canvas.toDataURL('image/png').split(',')[1];
  const previewBytes = atob(image).length;
  check(previewBytes > 65536 && previewBytes <= 1024 * 1024, 'large real PNG fixture is outside the negotiated limit');
  button('重试预览').click(); await until(() => jobs.length === 7, 'large-preview retry not submitted');
  const large = latest(); large.resolve(previewReply(large, { preview: image, previewContentType: 'image/*', description: '真实样式说明示例' }));
  await until(() => previewStatus().includes('预览已加载') && document.querySelector('.osm-style-preview img')?.naturalWidth === 400,
    'large valid image was truncated or not displayed');
  check(document.querySelector('.osm-style-preview figcaption')?.textContent === '真实样式说明示例', 'description missing');
  check(window.fixtureState.OsmStyle === 'dark', 'preview changed the selected draft style');

  button('重新加载预览').click(); await until(() => jobs.length === 8, 'reload preview missing');
  const cancelled = latest(); document.querySelector('[data-testid=osm-style-preview-cancel]').click();
  await until(() => previewStatus().includes('已取消'), 'preview cancellation not visible');
  check(cancelled.options.signal.aborted, 'cancel did not abort preview');
  cancelled.resolve(previewReply(cancelled, { preview: image })); await wait(70);
  check(previewStatus().includes('已取消') && !document.querySelector('.osm-style-preview img'), 'cancelled response was accepted');
  load().click(); await until(() => jobs.length === 9, 'empty-preview request missing');
  const empty = latest(); empty.resolve(previewReply(empty, { description: '该样式没有图片' }));
  await until(() => previewStatus().includes('确认此样式没有预览图片'), 'confirmed no-image state missing');
  check(document.body.innerText.includes('此样式未提供预览图片'), 'confirmed no-image placeholder missing');

  button('重新加载预览').click(); await until(() => jobs.length === 10, 'refresh race preview missing');
  const staleCatalogPreview = latest(); button('刷新样式').click();
  await until(() => jobs.length === 11 && latest().params.catalogOnly === true, 'catalog refresh not submitted');
  const refreshedCatalog = latest();
  check(staleCatalogPreview.options.signal.aborted, 'catalog refresh did not abort preview');
  staleCatalogPreview.resolve(previewReply(staleCatalogPreview, { preview: image }));
  refreshedCatalog.resolve(reply(refreshedCatalog));
  await until(() => select()?.value === 'dark' && previewStatus().includes('尚未加载'), 'catalog refresh did not invalidate preview');
  check(!document.querySelector('.osm-style-preview img') && window.fixtureState.OsmStyle === 'dark', 'catalog refresh leaked stale preview or changed draft');

  load().click(); await until(() => jobs.length === 12, 'revision-race preview missing');
  const staleRevision = latest(); window.fixtureRevision(1);
  await until(() => jobs.length === 13 && latest().params.catalogOnly === true, 'new backend revision did not reload catalog');
  const revisionCatalog = latest();
  check(staleRevision.options.signal.aborted, 'backend revision did not cancel old preview');
  staleRevision.resolve(previewReply(staleRevision, { preview: image })); revisionCatalog.resolve(reply(revisionCatalog));
  await until(() => select()?.value === 'dark' && previewStatus().includes('尚未加载'), 'revision change leaked old preview');

  load().click(); await until(() => jobs.length === 14, 'backend-race preview missing');
  const staleBackend = latest(); window.fixtureOwner(secondBackend);
  await until(() => jobs.length === 15 && latest().backend === 'second' && latest().params.catalogOnly === true, 'new backend did not load catalog');
  const secondCatalog = latest();
  check(staleBackend.options.signal.aborted, 'backend switch did not cancel old preview');
  staleBackend.resolve(previewReply(staleBackend, { preview: image })); secondCatalog.resolve(reply(secondCatalog));
  await until(() => select()?.value === 'dark' && previewStatus().includes('尚未加载'), 'backend switch leaked old preview');

  document.querySelector('.osm-style-density input').value = '2';
  const density = document.querySelector('.osm-style-density input');
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(density, '3'); density.dispatchEvent(new Event('input', { bubbles: true }));
  document.querySelector('.osm-style-labels input').click();
  await until(() => window.fixtureState.Density === 3 && window.fixtureState.ShowLabels === false, 'density or labels stopped working');
  document.querySelector('.osm-cache-panel > summary').click();
  button('检查缓存').click(); await until(() => jobs.length === 16, 'cache inspect missing');
  const inspect = latest(); inspect.resolve({ directory: '/cache/osm', bytes: 4096, canClear: true, cleared: false });
  await until(() => !button('清理服务端缓存').disabled, 'cache clear not enabled after inspect');
  button('清理服务端缓存').click(); await until(() => jobs.length === 17, 'cache clear missing');
  const clear = latest(); clear.resolve({ directory: '/cache/osm', bytes: 0, canClear: true, cleared: true });
  await until(() => cacheEvents.length === 1, 'confirmed cache clearing did not notify owner');
  check(window.fixtureState.OsmStyle === 'dark', 'cache action changed the style draft');

  load().click(); await until(() => jobs.length === 18, 'unmount preview missing');
  const unmounted = latest(); window.fixtureMount(false); await until(() => !document.querySelector('[data-testid=osm-style-picker]'), 'picker did not unmount');
  check(unmounted.options.signal.aborted, 'unmount did not abort preview');
  unmounted.resolve(previewReply(unmounted, { preview: image }));
  window.fixtureMount(true); await until(() => jobs.length === 19, 'remount catalog missing');
  const cancelledCatalog = latest(); button('取消加载').click();
  await until(() => document.body.innerText.includes('已取消样式加载'), 'catalog cancellation missing');
  check(cancelledCatalog.options.signal.aborted, 'catalog cancellation did not abort request');
  cancelledCatalog.resolve(reply(cancelledCatalog)); await wait(70);
  check(document.body.innerText.includes('已取消样式加载') && select().disabled, 'late cancelled catalog was accepted');
  button('重试').click(); await until(() => jobs.length === 20 && latest().params.catalogOnly === true, 'catalog retry missing');
  const retriedCatalog = latest(); retriedCatalog.resolve(reply(retriedCatalog));
  await until(() => select()?.value === 'dark' && previewStatus().includes('尚未加载'), 'catalog retry did not restore saved draft without a preview');
  check(jobs.length === 20 && !document.querySelector('.osm-style-preview img'), 'remount/retry auto-downloaded a preview');
  load().click(); await until(() => jobs.length === 21, 'unrecognized-image test missing');
  const unknownImage = latest(); unknownImage.resolve(previewReply(unknownImage, { preview: 'YWJjZA==' }));
  await until(() => previewStatus().includes('未完全加载') && document.body.innerText.includes('预览图片无法显示'),
    'unrecognized image was reported as a successful or absent preview');
  button('重试预览').click(); await until(() => jobs.length === 22, 'corrupt-image retry missing');
  const corruptImage = latest(); corruptImage.resolve(previewReply(corruptImage, { preview: 'iVBORw0KGgo=', previewContentType: 'image/png' }));
  await until(() => previewStatus().includes('未完全加载') && document.body.innerText.includes('预览图片无法显示'),
    'decode failure was not reported as retryable');
  check(button('重试预览'), 'decode failure lost its retry action');
  button('刷新样式').click(); await until(() => jobs.length === 23, 'final catalog refresh missing');
  const finalCatalog = latest(); finalCatalog.resolve(reply(finalCatalog));
  await until(() => select()?.value === 'dark' && previewStatus().includes('尚未加载'), 'final catalog did not reset preview state');
  load().click(); await until(() => jobs.length === 24, 'round-trip stale preview missing');
  const roundTrip = latest();
  select().value = 'basic'; select().dispatchEvent(new Event('change', { bubbles: true }));
  await until(() => window.fixtureState.OsmStyle === 'basic', 'first round-trip style change failed');
  select().value = 'dark'; select().dispatchEvent(new Event('change', { bubbles: true }));
  await until(() => window.fixtureState.OsmStyle === 'dark' && previewStatus().includes('尚未加载'), 'second round-trip style change failed');
  roundTrip.resolve(previewReply(roundTrip, { preview: image })); await wait(70);
  check(roundTrip.options.signal.aborted && previewStatus().includes('尚未加载') && !document.querySelector('.osm-style-preview img'),
    'a stale preview was accepted after switching away and back to the same style');
  load().click(); await until(() => jobs.length === 25, 'partial image retry missing');
  const partialImage = latest(); partialImage.resolve(previewReply(partialImage, {
    preview: image, previewContentType: 'image/*', warning: 'description failed',
  }));
  await until(() => previewStatus().includes('未完全加载') && document.querySelector('.osm-style-preview img')?.naturalWidth === 400,
    'successful image was discarded when its description failed');
  check(button('重试预览') && document.querySelector('.osm-style-diagnostic')?.textContent.includes('description failed'),
    'partial image did not retain its warning and retry');
  button('刷新样式').click(); await until(() => jobs.length === 26, 'reset catalog missing');
  const resetCatalog = latest(); resetCatalog.resolve(reply(resetCatalog));
  await until(() => select()?.value === 'dark' && previewStatus().includes('尚未加载'), 'reset catalog did not return to idle preview');
  return { checks, previewBytes, requestCount: jobs.length, catalogRequests: jobs.filter(job => job.params.catalogOnly === true).length,
    explicitPreviews: jobs.filter(job => typeof job.params.styleID === 'string').length, cacheEvents, draft: window.fixtureState };
}
