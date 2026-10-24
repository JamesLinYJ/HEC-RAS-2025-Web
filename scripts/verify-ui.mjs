import { spawn, execFileSync } from 'node:child_process';
import { mkdtemp, mkdir, rm, writeFile, readFile, access, readdir } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import assert from 'node:assert/strict';
import { startStaticServer } from '../desktop/static-server.mjs';
import { checkMapUI } from './check-map-ui.mjs';
import { geometryMapFetchObserverSource } from './check-geometry-map-ui.mjs';
import { resultHoverFetchObserverSource } from './result-hover-fetch-observer.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const output = path.resolve(process.env.HECRAS_VERIFY_OUTPUT ?? path.join(root, 'artifacts/ui-checks'));
await mkdir(output, { recursive: true });
const profile = await mkdtemp(path.join(tmpdir(), 'hecras-web-ui-'));
const chrome = process.env.HECRAS_CHROME ?? '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
const serverUrl = process.env.HECRAS_VERIFY_SERVER_URL;
const computeProject = process.env.HECRAS_VERIFY_PROJECT;
const cancelProject = process.env.HECRAS_VERIFY_CANCEL_PROJECT;
const workspaceZip = process.env.HECRAS_VERIFY_WORKSPACE_ZIP;
const editorProject = process.env.HECRAS_VERIFY_EDITOR_PROJECT;
const mapFixturePath = process.env.HECRAS_VERIFY_MAP_FIXTURE;
const resultHoverOnly = process.env.HECRAS_VERIFY_RESULT_HOVER_ONLY === '1';
const terrainOnly = process.env.HECRAS_VERIFY_TERRAIN_ONLY === '1';
const geometryTopologyOnly = process.env.HECRAS_VERIFY_GEOMETRY_TOPOLOGY_ONLY === '1';
const targetedMapOnly = resultHoverOnly || terrainOnly || geometryTopologyOnly;
await access(chrome);
let browser;
let socket;
let server;
let timeout;
const pending = new Map();
let nextId = 0;
let sessionId;
const exceptions = [];
const failedResources = [];
const dialogs = [];
let expectedDialog;
let transportFaults;
const injectedNetworkRequests = new Set();
const report = { pages: [], exceptions, checks: [], failedResources, dialogs };
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
function command(method, params = {}, useSession = true) {
  const id = ++nextId;
  const packet = { id, method, params };
  if (sessionId && useSession) packet.sessionId = sessionId;
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => { pending.delete(id); reject(new Error(`CDP timed out: ${method}`)); }, 20_000);
    pending.set(id, { resolve, reject, timer });
    socket.send(JSON.stringify(packet));
  });
}
async function evaluate(expression) {
  const reply = await command('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
  if (reply.exceptionDetails) throw new Error(reply.exceptionDetails.exception?.description ?? reply.exceptionDetails.text);
  return reply.result.value;
}
async function waitFor(expression, description, limit = 20_000) {
  const deadline = Date.now() + limit;
  while (Date.now() < deadline) {
    if (await evaluate(expression)) return;
    await delay(100);
  }
  throw new Error(`UI did not reach ${description}`);
}
function expectDialog(accept) {
  assert.equal(expectedDialog, undefined, 'A dialog expectation is already pending');
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => { expectedDialog = undefined; reject(new Error('Expected confirmation dialog did not open')); }, 20_000);
    expectedDialog = { accept, resolve, reject, timer };
  });
}
async function setFirstResultCell(raw) {
  await evaluate(`(() => { const input=document.querySelector('[data-feature=results] .results-cell-input'); input.focus(); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(input,${JSON.stringify(raw)}); input.dispatchEvent(new Event('input',{bubbles:true})); })()`);
  await waitFor(`document.querySelector('[data-feature=results] .results-cell-input').value===${JSON.stringify(raw)}`, 'result cell raw input');
}
async function navigate(feature) {
  await evaluate(`document.querySelectorAll('.ras-feature-nav button')[${['project','map','editors','geometry','results','tools','compute'].indexOf(feature)}].click()`);
  await waitFor(`(() => { const node=document.querySelector('[data-feature="${feature}"]'); return node && !node.hidden && node.innerText.trim().length>20 && !node.innerText.includes('此页面尚未接入工作区') && !node.innerText.includes('正在加载…'); })()`, feature);
  const content = await evaluate(`document.querySelector('[data-feature="${feature}"]').innerText`);
  report.pages.push({ feature, textLength: content.length, heading: content.slice(0, 180) });
}
async function setFiles(selector, files) {
  const { root: document } = await command('DOM.getDocument', { depth: -1, pierce: true });
  const { nodeId } = await command('DOM.querySelector', { nodeId: document.nodeId, selector });
  assert.ok(nodeId, `File input not found: ${selector}`);
  await command('DOM.setFileInputFiles', { nodeId, files });
}
async function verifyCancellation() {
  assert.ok(serverUrl, 'A cancellation UI check requires a real server');
  await navigate('project');
  await waitFor("document.querySelector('.ras-statusbar').innerText.includes('服务已连接')", 'server connection before cancellation check');
  await evaluate("document.querySelector('.ras-menubar details summary').click(); Array.from(document.querySelectorAll('.ras-menu-popover button')).find(n=>n.textContent.includes('打开工程')).click()");
  await waitFor("Boolean(document.querySelector('.project-open-path-row input'))", 'cancellation project dialog');
  await evaluate(`(() => { const input=document.querySelector('.project-open-path-row input'); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(input,${JSON.stringify(cancelProject)}); input.dispatchEvent(new Event('input',{bubbles:true})); })()`);
  await waitFor("!document.querySelector('.project-open-path-row button').disabled", 'cancellation project path');
  await evaluate("document.querySelector('.project-open-path-row button').click()");
  await waitFor("!document.querySelector('.project-open-modal') && document.querySelectorAll('.project-tree-node').length>0", 'cancellation project tree', 30_000);
  await evaluate("Array.from(document.querySelectorAll('.project-tree-toolbar button')).find(n=>n.textContent.trim()==='全部展开').click()");
  await evaluate("Array.from(document.querySelectorAll('.project-tree-node')).find(n=>n.querySelector('.project-tree-node-name')?.textContent==='Plan CPU').dispatchEvent(new MouseEvent('dblclick',{bubbles:true}))");
  await navigate('compute');
  await waitFor("Boolean(document.querySelector('[data-compute-action=run]')) && !document.querySelector('[data-compute-action=run]').disabled", 'cancellation Plan inspection', 30_000);
  transportFaults = { poll: 0, cancel: 0, computeSubmissions: 0 };
  const faults = transportFaults;
  await command('Fetch.enable', { patterns: [{ urlPattern: '*/api/operations*', requestStage: 'Request' }] });
  const rounds = [];
  try {
    for (let round = 0; round < 2; round++) {
      const inspectionId = await evaluate("Array.from(document.querySelectorAll('.plan-inspection-grid dd')).at(2).textContent");
      await evaluate("document.querySelector('[data-compute-action=run]').click()");
      await waitFor("document.querySelector('[data-testid=compute-status]').textContent.includes('正在计算')", 'running real cancellation Plan');
      await waitFor("(() => { try { const p=JSON.parse(document.querySelector('.progress-code')?.textContent ?? '{}'); return p.taskName==='Plan Compute' && Number(p.values?.Steps)>0; } catch { return false; } })()", 'actual completed solver step before UI cancellation', 90_000);
      const step = await evaluate("Number(JSON.parse(document.querySelector('.progress-code').textContent).values.Steps)");
      await evaluate("document.querySelector('[data-compute-action=cancel]').click()");
      await waitFor("/计算已取消|计算完成|计算失败|结果格式无法识别|计算状态未确认/.test(document.querySelector('[data-testid=compute-status]').textContent)", 'confirmed UI cancellation', 60_000);
      assert.match(await evaluate("document.querySelector('[data-testid=compute-status]').textContent"), /计算已取消/);
      const result = JSON.parse(await evaluate("document.querySelector('[data-compute-output]').textContent"));
      assert.equal(result.state, 'cancelled');
      assert.notEqual(result.summary?.success, true);
      await waitFor("!document.querySelector('[data-compute-action=run]').disabled && Array.from(document.querySelectorAll('.plan-inspection-grid dd')).length===4", 'fresh inspection after cancellation', 30_000);
      const nextInspectionId = await evaluate("Array.from(document.querySelectorAll('.plan-inspection-grid dd')).at(2).textContent");
      assert.notEqual(nextInspectionId, inspectionId, 'A retained cancelled result requires a fresh output-path inspection');
      rounds.push({ step, state: result.state, resultPath: result.resultPath, summarySuccess: result.summary?.success, inspectionId, nextInspectionId });
    }
  } finally {
    await command('Fetch.disable');
    transportFaults = undefined;
  }
  assert.deepEqual(faults, { poll: 1, cancel: 1, computeSubmissions: 2 }, 'Transport faults must recover without duplicate compute submission');
  report.cancellation = { rounds, injectedTransientFailures: faults };
  report.checks.push('real CPU cancellation survives one failed poll and one failed DELETE, refreshes inspection, and can run and cancel again without duplicate submissions');
  await evaluate("document.querySelector('.ras-menubar details summary').click(); Array.from(document.querySelectorAll('.ras-menu-popover button')).find(n=>n.textContent.includes('关闭工程')).click()");
  await waitFor("document.querySelector('.ras-statusbar').innerText.includes('无活动工程')", 'close cancellation test project');
}
async function verifyPlanSave(planName) {
  const description = `中文计划保存验证 ${Date.now()}`;
  await evaluate(`(() => { const input=document.querySelector('[data-feature=editors] textarea'); Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype,'value').set.call(input,${JSON.stringify(description)}); input.dispatchEvent(new Event('input',{bubbles:true})); })()`);
  await waitFor("!document.querySelector('button[form=source-model-editor-form]').disabled", 'real Plan draft ready to save');
  await evaluate("document.querySelector('button[form=source-model-editor-form]').click()");
  await waitFor("document.querySelector('.editor-save-notice')?.textContent.includes('已从后端保存回执刷新模型') && !document.querySelector('.ras-document-title').textContent.includes('•')", 'source Plan save receipt', 30_000);
  const planRevision = await evaluate("document.querySelector('.editor-status-strip code').textContent");
  assert.match(planRevision, /^[0-9a-f]{64}$/i);
  await navigate('project');
  await waitFor("Boolean(document.querySelector('[data-testid=project-save]')) && !document.querySelector('[data-testid=project-save]').disabled", 'project save while Plan selected');
  await evaluate("document.querySelector('[data-testid=project-save]').click()");
  await waitFor("document.querySelector('[data-feature=project]').textContent.includes('C# 后端已保存并返回新的工程修订')", 'independent project save receipt', 30_000);
  await navigate('editors');
  await waitFor("Boolean(document.querySelector('.editor-status-strip code'))", 'Plan retained after project save');
  assert.equal(await evaluate("document.querySelector('.editor-status-strip code').textContent"), planRevision, 'Saving .ras must not overwrite the selected Plan revision');
  await evaluate("document.querySelector('.ras-menubar details summary').click(); Array.from(document.querySelectorAll('.ras-menu-popover button')).find(n=>n.textContent.includes('关闭工程')).click()");
  await waitFor("document.querySelector('.ras-statusbar').innerText.includes('无活动工程')", 'close saved project');
  await evaluate("document.querySelector('.ras-menubar details summary').click(); Array.from(document.querySelectorAll('.ras-menu-popover button')).find(n=>n.textContent.includes('打开工程')).click()");
  await waitFor("Boolean(document.querySelector('.project-open-path-row input'))", 'reopen saved project dialog');
  await evaluate(`(() => { const input=document.querySelector('.project-open-path-row input'); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(input,${JSON.stringify(computeProject)}); input.dispatchEvent(new Event('input',{bubbles:true})); })()`);
  await waitFor("!document.querySelector('.project-open-path-row button').disabled", 'reopen saved project path');
  await evaluate("document.querySelector('.project-open-path-row button').click()");
  await waitFor("!document.querySelector('.project-open-modal') && document.querySelectorAll('.project-tree-node').length>0", 'reopened saved project tree', 30_000);
  await evaluate("Array.from(document.querySelectorAll('.project-tree-toolbar button')).find(n=>n.textContent.trim()==='全部展开').click()");
  await waitFor(`Array.from(document.querySelectorAll('.project-tree-node')).some(n=>n.textContent.includes(${JSON.stringify(planName)}))`, 'reopened Plan node');
  await evaluate(`Array.from(document.querySelectorAll('.project-tree-node')).find(n=>n.textContent.includes(${JSON.stringify(planName)})).dispatchEvent(new MouseEvent('dblclick',{bubbles:true}))`);
  await waitFor(`document.querySelector('[data-feature=editors] textarea')?.value===${JSON.stringify(description)}`, 'saved Chinese description reread from source HDF', 30_000);
  report.planSave = { description, planRevision, reopenedFromDisk: true, projectSavePreservedPlanRevision: true };
  report.checks.push('Chinese Plan edits save to the real HDF and survive project close/reopen; .ras save preserves the selected model revision');
}
async function verifyServerProject() {
  assert.ok(serverUrl, 'A compute UI check requires HECRAS_VERIFY_SERVER_URL');
  await navigate('project');
  await waitFor("!Array.from(document.querySelectorAll('.ras-feature-nav button')).at(-1).disabled", 'C# connection attempt');
  if (!await evaluate("document.querySelector('.ras-statusbar').innerText.includes('服务已连接')")) {
    await evaluate("Array.from(document.querySelectorAll('.ras-menubar details'))[2].querySelector('summary').click(); Array.from(document.querySelectorAll('.ras-menu-popover button')).find(n=>n.textContent.includes('属性面板')).click()");
    throw new Error('C# gateway connection failed: ' + await evaluate("document.querySelector('.ras-runtime-error')?.textContent ?? document.querySelector('.ras-statusbar').innerText"));
  }
  await evaluate("document.querySelector('.ras-menubar details summary').click(); Array.from(document.querySelectorAll('.ras-menu-popover button')).find(n=>n.textContent.includes('打开工程')).click()");
  await waitFor("Boolean(document.querySelector('.project-open-path-row input'))", 'project path dialog');
  const missingProject = `${computeProject}.missing.ras`;
  await evaluate(`(() => { const input=document.querySelector('.project-open-path-row input'); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(input,${JSON.stringify(missingProject)}); input.dispatchEvent(new Event('input',{bubbles:true})); })()`);
  await waitFor("!document.querySelector('.project-open-path-row button').disabled", 'missing-project path enabled');
  await evaluate("document.querySelector('.project-open-path-row button').click()");
  await waitFor("Boolean(document.querySelector('.project-open-modal .project-form-error')) && !document.querySelector('.project-open-path-row button').disabled", 'Chinese missing-project error', 15_000);
  assert.match(await evaluate("document.querySelector('.project-open-modal .project-form-error').innerText"), /找不到.*文件/);
  report.checks.push('a missing project returns a Chinese error and leaves the open dialog usable');
  await evaluate(`(() => { const input=document.querySelector('.project-open-path-row input'); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(input,${JSON.stringify(computeProject)}); input.dispatchEvent(new Event('input',{bubbles:true})); })()`);
  await waitFor("!document.querySelector('.project-open-path-row button').disabled", 'open path enabled');
  await evaluate("document.querySelector('.project-open-path-row button').click()");
  await waitFor("!document.querySelector('.project-open-modal') && document.querySelectorAll('.project-tree-node').length>0", 'real project tree', 60_000);
  await evaluate("Array.from(document.querySelectorAll('.project-tree-toolbar button')).find(n=>n.textContent.trim()==='全部展开').click()");
  const planName = process.env.HECRAS_VERIFY_PLAN_NAME ?? 'Plan CPU';
  await waitFor(`Array.from(document.querySelectorAll('.project-tree-node')).some(n=>n.textContent.includes(${JSON.stringify(planName)}))`, 'source Plan in tree');
  await evaluate(`Array.from(document.querySelectorAll('.project-tree-node')).find(n=>n.textContent.includes(${JSON.stringify(planName)})).dispatchEvent(new MouseEvent('dblclick',{bubbles:true}))`);
  await waitFor(`document.querySelector('.ras-document-title').innerText.includes(${JSON.stringify(planName)})`, 'opened source Plan');
  await waitFor("Boolean(document.querySelector('[data-feature=editors] .editor-better-combo-property-item[data-selected-value=CPU] [role=combobox]'))", 'loaded Plan CPU selector');
  assert.match(await evaluate("document.querySelector('[data-feature=editors] .editor-better-combo-property-item[data-selected-value=CPU] [role=combobox]').textContent"), /显式.*CPU|CPU.*显式/);
  report.checks.push('real Plan form displays the CPU solver description in Chinese while preserving option values');
  await verifyPlanSave(planName);
  const planScreenshot = await command('Page.captureScreenshot', { format: 'png' });
  await writeFile(path.join(output, 'server-plan.png'), Buffer.from(planScreenshot.data, 'base64'));
  await navigate('compute');
  await waitFor("Boolean(document.querySelector('[data-compute-action=run]')) && (!document.querySelector('[data-compute-action=run]').disabled || Boolean(document.querySelector('[data-feature=compute] .compute-invalid-hint,[data-feature=compute] [role=alert],[data-feature=compute] .runtime-indicator.is-error')))", 'Plan inspection response', 60_000);
  assert.equal(await evaluate("document.querySelector('[data-compute-action=run]').disabled"), false,
    'The real Plan inspection did not enable compute: ' + await evaluate("document.querySelector('[data-feature=compute]').innerText"));
  await evaluate("document.querySelector('[data-compute-action=run]').click()");
  await waitFor("/计算完成|计算失败|结果格式无法识别/.test(document.querySelector('[data-testid=compute-status]').innerText)", 'real Plan compute terminal state', 180_000);
  assert.match(await evaluate("document.querySelector('[data-testid=compute-status]').innerText"), /计算完成/);
  const result = JSON.parse(await evaluate("document.querySelector('[data-compute-output]').textContent"));
  assert.equal(result.state, 'completed');
  assert.equal(result.summary.success, true);
  assert.ok(result.resultPath && result.resultRevision);
  report.compute = { state: result.state, resultPath: result.resultPath, resultRevision: result.resultRevision, summary: result.summary };
  const computeScreenshot = await command('Page.captureScreenshot', { format: 'png' });
  await writeFile(path.join(output, 'server-compute.png'), Buffer.from(computeScreenshot.data, 'base64'));
  await waitFor("Boolean(document.querySelector('[data-compute-action=open-result]')) && !document.querySelector('[data-compute-action=open-result]').disabled", 'confirmed result action');
  await evaluate("document.querySelector('[data-compute-action=open-result]').click()");
  await waitFor("location.hash==='#/results' && Array.from(document.querySelectorAll('.results-selector-grid select')).some(n=>Array.from(n.options).some(o=>o.value==='Cell Depth'))", 'reopened source HDF Result', 60_000);
  assert.match(await evaluate("Array.from(document.querySelectorAll('.results-selector-grid option')).find(n=>n.value==='Cell Depth').textContent"), /水深/);
  await evaluate("(() => { const input=Array.from(document.querySelectorAll('.results-selector-grid select')).find(n=>Array.from(n.options).some(o=>o.value==='Cell Depth')); Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype,'value').set.call(input,'Cell Depth'); input.dispatchEvent(new Event('change',{bubbles:true})); })()");
  await evaluate("Array.from(document.querySelectorAll('.results-selector-panel button')).find(n=>n.textContent.includes('载入序列')).click()");
  await waitFor("document.querySelectorAll('.results-chart-series').length===1 && document.querySelectorAll('.results-data-table tbody tr').length>0", 'real HDF depth chart', 60_000);
  const chartLabels = await evaluate("Array.from(document.querySelectorAll('[data-feature=results] .results-chart-axis-label')).map(n=>n.textContent)");
  assert.equal(new Set(chartLabels.slice(0, 5)).size, 5, 'Distinct real water-depth ticks must have distinct display values');
  assert.equal(new Set(chartLabels.slice(5)).size, 3, 'The short real compute times must remain distinguishable');
  report.checks.push('Chinese UI opens a real server project, inspects and computes its Plan, and reopens HDF cell depths in the result chart');
  report.checks.push('real sub-millimetre depth changes and second-scale output times have distinct chart axis labels');
  await waitFor("Boolean(document.querySelector('.results-node-details summary'))", 'embedded Result detail disclosure');
  await evaluate("if (!document.querySelector('.results-node-details').open) document.querySelector('.results-node-details summary').click()");
  await waitFor("Boolean(document.querySelector('.results-plan-description-editor textarea')) && !document.querySelector('.results-plan-description-editor textarea').readOnly", 'verified editable embedded Plan description', 60_000);
  const savedDescription = await evaluate("document.querySelector('.results-plan-description-editor textarea').value");
  const projectBeforeDescription = await readFile(computeProject);
  const resultBeforeDescription = await readFile(result.resultPath);
  const descriptionDraft = savedDescription + '\n浏览器验收：结果内嵌方案描述';
  const setDescription = async () => evaluate(`(() => { const input=document.querySelector('.results-plan-description-editor textarea'); Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype,'value').set.call(input,${JSON.stringify(descriptionDraft)}); input.dispatchEvent(new Event('input',{bubbles:true})); })()`);
  await setDescription();
  await waitFor("document.querySelector('.ras-document-title').innerText.endsWith(' •') && !document.querySelector('.results-plan-description-editor .results-primary-button').disabled", 'description draft owner without blocking its own save');
  await evaluate("document.querySelector('.results-plan-description-editor .results-secondary-button').click()");
  await waitFor(`document.querySelector('.results-plan-description-editor textarea').value === ${JSON.stringify(savedDescription)} && !document.querySelector('.ras-document-title').innerText.endsWith(' •')`, 'description cancel restores the saved value and clears its owner');
  assert.deepEqual(await readFile(result.resultPath), resultBeforeDescription, 'Canceling the description draft wrote the HDF');
  await setDescription();
  await waitFor("!document.querySelector('.results-plan-description-editor .results-primary-button').disabled", 'description save enabled after editing');
  await evaluate("document.querySelector('.results-plan-description-editor .results-primary-button').click()");
  await waitFor(`document.querySelector('.results-plan-description-editor textarea')?.value === ${JSON.stringify(descriptionDraft)} && !document.querySelector('.results-plan-description-editor textarea').readOnly && !document.querySelector('.ras-document-title').innerText.endsWith(' •')`, 'description saved and reloaded under the acknowledged Result revision', 60_000);
  assert.notDeepEqual(await readFile(result.resultPath), resultBeforeDescription, 'Saving the description did not update the real HDF');
  assert.deepEqual(await readFile(computeProject), projectBeforeDescription, 'Editing embedded Result metadata changed the source project');
  report.checks.push('embedded Plan description reports dirty state, cancels without writing, and saves/reloads through a new HDF revision while preserving project bytes');
  const resultScreenshot = await command('Page.captureScreenshot', { format: 'png' });
  await writeFile(path.join(output, 'server-result.png'), Buffer.from(resultScreenshot.data, 'base64'));
  await evaluate("document.querySelector('.ras-menubar details summary').click(); Array.from(document.querySelectorAll('.ras-menu-popover button')).find(n=>n.textContent.includes('关闭工程')).click()");
  await waitFor("document.querySelector('.ras-statusbar').innerText.includes('无活动工程')", 'close tested project');
}
async function verifyWorkspaceArchiveAndMaps() {
  assert.ok(serverUrl, 'Workspace UI checks require a real server');
  assert.ok(path.isAbsolute(workspaceZip), 'Use an absolute disposable workspace ZIP path');
  await access(workspaceZip);
  const openDialog = async () => {
    await navigate('project');
    await evaluate("document.querySelector('.ras-menubar details summary').click(); Array.from(document.querySelectorAll('.ras-menu-popover button')).find(n=>n.textContent.includes('打开工程')).click()");
    await waitFor("Boolean(document.querySelector('.project-open-path-row input'))", 'workspace project open dialog');
  };
  const closeProject = async () => {
    await evaluate("document.querySelector('.ras-menubar details summary').click(); Array.from(document.querySelectorAll('.ras-menu-popover button')).find(n=>n.textContent.includes('关闭工程')).click()");
    await waitFor("document.querySelector('.ras-statusbar').innerText.includes('无活动工程')", 'close workspace project');
    await waitFor("!document.querySelector('[data-testid=project-download-workspace]')", 'workspace download hidden without its active project');
  };
  await openDialog();
  await setFiles('[data-testid="project-upload-zip-input"]', [workspaceZip]);
  await waitFor("Boolean(document.querySelector('[data-testid=project-upload-confirm]')) && !document.querySelector('[data-testid=project-upload-confirm]').disabled", 'review actual ZIP upload');
  await evaluate("document.querySelector('[data-testid=project-upload-confirm]').click()");
  await waitFor("!document.querySelector('.project-open-modal') && Boolean(document.querySelector('[data-testid=project-download-workspace]'))", 'ZIP extracted and real project opened', 60_000);
  const projectPath = await evaluate("document.querySelector('.ras-statusbar').children[1].textContent");
  const archiveUrl = await evaluate("document.querySelector('[data-testid=project-download-workspace]').href");
  const workspaceId = new URL(archiveUrl).pathname.match(/\/workspaces\/([a-f0-9]{32})\/archive$/)?.[1];
  assert.ok(workspaceId, 'The download must belong to an accepted workspace');
  report.checks.push('ZIP file selection uploads and extracts a real project, then opens its server XML and asset tree');

  await navigate('map');
  await waitFor("document.querySelectorAll('.map-document-tab-list [role=tab]').length===1 && !document.querySelector('.map-create-document').disabled", 'saved map discovery on a new uploaded project', 30_000);
  for (let count = 2; count <= 3; count++) {
    await evaluate("document.querySelector('.map-create-document').click()");
    await waitFor(`document.querySelectorAll('.map-document-tab-list [role=tab]').length===${count} && !document.querySelector('.map-create-document').disabled`, `real saved map creation ${count - 1}`, 30_000);
  }
  const savedNames = await evaluate("Array.from(document.querySelectorAll('.map-document-tab-list [role=tab]')).slice(1).map(n=>n.textContent.trim())");
  const visualizationPath = projectPath.replace(/\.ras$/i, '.vis');
  const savedVisualization = await readFile(visualizationPath);
  await closeProject();
  await openDialog();
  await evaluate(`(() => { const input=document.querySelector('.project-open-path-row input'); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(input,${JSON.stringify(projectPath)}); input.dispatchEvent(new Event('input',{bubbles:true})); })()`);
  await waitFor("!document.querySelector('.project-open-path-row button').disabled", 'accepted saved-map project path');
  await evaluate("document.querySelector('.project-open-path-row button').click()");
  await waitFor("!document.querySelector('.project-open-modal') && Boolean(document.querySelector('[data-testid=project-download-workspace]'))", 'reopened uploaded project with matching archive ownership', 30_000);
  await navigate('map');
  await waitFor(`JSON.stringify(Array.from(document.querySelectorAll('.map-document-tab-list [role=tab]')).slice(1).map(n=>n.textContent.trim()))===${JSON.stringify(JSON.stringify(savedNames))} && !document.querySelector('.map-create-document').disabled`, 'both real map documents restored after close and reopen', 30_000);
  assert.deepEqual(await readFile(visualizationPath), savedVisualization, 'Restoring maps must not rewrite their .vis file');
  report.checks.push('two maps are created with consecutive revisions and restored from the unchanged .vis after project close/reopen');
  const screenshot = await command('Page.captureScreenshot', { format: 'png' });
  await writeFile(path.join(output, 'server-saved-maps.png'), Buffer.from(screenshot.data, 'base64'));

  await navigate('project');
  const downloads = await mkdtemp(path.join(output, 'workspace-downloads-'));
  await command('Browser.setDownloadBehavior', { behavior: 'allow', downloadPath: downloads }, false);
  await evaluate("document.querySelector('[data-testid=project-download-workspace]').click()");
  let downloaded;
  const deadline = Date.now() + 30_000;
  while (Date.now() < deadline) {
    downloaded = (await readdir(downloads)).find((file) => file.endsWith('.zip'));
    if (downloaded) break;
    await delay(100);
  }
  assert.ok(downloaded, 'The real workspace link did not download a ZIP');
  const downloadedPath = path.join(downloads, downloaded);
  execFileSync('unzip', ['-t', downloadedPath], { stdio: 'pipe' });
  const entries = execFileSync('unzip', ['-Z1', downloadedPath], { encoding: 'utf8' }).trim().split('\n');
  const visEntry = entries.find((entry) => entry === path.basename(visualizationPath));
  const projectEntry = entries.find((entry) => entry === path.basename(projectPath));
  assert.ok(visEntry && projectEntry, 'The downloaded ZIP omitted its source project or saved maps');
  assert.deepEqual(execFileSync('unzip', ['-p', downloadedPath, visEntry]), savedVisualization, 'Downloaded map bytes differ from the saved visualization');
  assert.deepEqual(execFileSync('unzip', ['-p', downloadedPath, projectEntry]), await readFile(projectPath), 'Downloaded .ras bytes differ from the opened source project');
  report.workspace = { workspaceId, projectPath, savedMapNames: savedNames, restoredFromDisk: true, archivePath: downloadedPath, archiveFileCount: entries.length, archiveContainsCurrentVisualization: true };
  report.checks.push('the workspace download link streams an actual valid ZIP containing the current .ras and saved-map bytes, and hides when the project closes');
  await closeProject();
}

async function verifyEditorSeries() {
  assert.ok(serverUrl && path.isAbsolute(editorProject), 'Use a disposable editor fixture with a real loopback server');
  const boundaryPath = process.env.HECRAS_VERIFY_EDITOR_BOUNDARY ?? path.join(path.dirname(editorProject), 'Boundary Conditions', 'Solver Boundary.h5');
  let rowsExpression;
  const openBoundary = async () => {
    await navigate('project');
    await evaluate("document.querySelector('.ras-menubar details summary').click(); Array.from(document.querySelectorAll('.ras-menu-popover button')).find(n=>n.textContent.includes('打开工程')).click()");
    await waitFor("Boolean(document.querySelector('.project-open-path-row input'))", 'editor fixture path dialog');
    await evaluate(`(() => { const input=document.querySelector('.project-open-path-row input'); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(input,${JSON.stringify(editorProject)}); input.dispatchEvent(new Event('input',{bubbles:true})); })()`);
    await waitFor("!document.querySelector('.project-open-path-row button').disabled", 'editor fixture accepted path');
    await evaluate("document.querySelector('.project-open-path-row button').click()");
    await waitFor("!document.querySelector('.project-open-modal') && document.querySelectorAll('.project-tree-node').length>0", 'editor source project loaded', 30_000);
    await evaluate("Array.from(document.querySelectorAll('.project-tree-toolbar button')).find(n=>n.textContent.trim()==='全部展开').click()");
    await waitFor("Array.from(document.querySelectorAll('.project-tree-node')).some(n=>n.textContent.includes('Solver Boundary'))", 'source boundary tree node');
    await evaluate("Array.from(document.querySelectorAll('.project-tree-node')).find(n=>n.textContent.includes('Solver Boundary')).dispatchEvent(new MouseEvent('dblclick',{bubbles:true}))");
    await waitFor("Boolean(document.querySelector('.source-editor-boundary .editor-page-tabs'))", 'real boundary editor loaded', 30_000);
    assert.equal(await evaluate("document.querySelectorAll('[data-feature=editors] .source-editor-boundary > nav.editor-page-tabs').length"), 1, 'The source boundary editor must own a single primary page navigation');
    assert.equal(await evaluate("document.querySelectorAll('[data-feature=editors] nav.editor-workbench-tabs').length"), 0, 'The workbench must not duplicate the source boundary navigation');
    await evaluate("Array.from(document.querySelectorAll('.source-editor-boundary .editor-page-tabs button')).find(n=>n.textContent.trim()==='降水').click()");
    await waitFor("Boolean(document.querySelector('[data-operation=appendRow]')) && !document.querySelector('[data-operation=appendRow]').disabled", 'actual editable irregular gauge', 30_000);
    const tableID = await evaluate("document.querySelector('[data-feature=editors] tr[data-table-id]').dataset.tableId");
    rowsExpression = `Array.from(document.querySelectorAll('[data-feature=editors] tr[data-table-id]')).filter(n=>n.dataset.tableId===${JSON.stringify(tableID)})`;
  };
  const closeProject = async () => {
    await evaluate("document.querySelector('.ras-menubar details summary').click(); Array.from(document.querySelectorAll('.ras-menu-popover button')).find(n=>n.textContent.includes('关闭工程')).click()");
    await waitFor("document.querySelector('.ras-statusbar').innerText.includes('无活动工程')", 'close saved boundary project');
  };
  await openBoundary();
  assert.match(await evaluate("document.querySelector('[data-feature=editors]').innerText"), /中文雨量站/);
  assert.equal(await evaluate(`${rowsExpression}.length`), 3, 'The fixture must initially have three actual HDF gauge rows');
  const initialRevision = await evaluate("document.querySelector('.editor-status-strip code').textContent");
  const initialBytes = await readFile(boundaryPath);
  await evaluate("document.querySelector('[data-operation=appendRow]').click()");
  await waitFor(`${rowsExpression}.length===4 && document.querySelector('[data-operation=appendRow]') && !document.querySelector('[data-operation=appendRow]').disabled`, 'real C# appendRow draft transform', 30_000);
  assert.equal(await evaluate("document.querySelector('.editor-status-strip code').textContent"), initialRevision, 'A draft transform must retain the source revision');
  assert.deepEqual(await readFile(boundaryPath), initialBytes, 'A draft transform must not write the source HDF');
  const setLastValue = async (raw) => {
    await evaluate(`(() => { const row=${rowsExpression}.at(-1); const input=Array.from(row.querySelectorAll('input')).at(-1); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(input,${JSON.stringify(raw)}); input.dispatchEvent(new Event('input',{bubbles:true})); })()`);
    await waitFor(`Array.from(${rowsExpression}.at(-1).querySelectorAll('input')).at(-1).value===${JSON.stringify(raw)}`, 'new gauge row draft input');
  };
  await setLastValue('无效数值');
  await waitFor("document.querySelector('[data-operation=appendRow]').disabled", 'invalid newly appended row blocks another transform');
  await setLastValue('2.75');
  await waitFor("!document.querySelector('[data-operation=appendRow]').disabled", 'corrected gauge value accepted');
  const saveBoundary = async (previousRevision) => {
    await evaluate("document.querySelector('.editor-workbench-actions button[type=submit]').click()");
    await waitFor(`(() => { const code=document.querySelector('.editor-status-strip code'); return code && /^[a-f0-9]{64}$/i.test(code.textContent) && code.textContent!==${JSON.stringify(previousRevision)} && !document.querySelector('.editor-status-strip strong') && !document.querySelector('.editor-workbench-actions button[type=submit]')?.textContent.includes('保存中'); })()`, 'source HDF save and new revision', 30_000);
    return await evaluate("document.querySelector('.editor-status-strip code').textContent");
  };
  const appendedRevision = await saveBoundary(initialRevision);
  assert.notDeepEqual(await readFile(boundaryPath), initialBytes, 'Explicit save must commit changed HDF bytes');
  await closeProject();
  await openBoundary();
  await waitFor(`${rowsExpression}.length===4`, 'four gauge rows reread from saved HDF');
  assert.equal(await evaluate(`Array.from(${rowsExpression}.at(-1).querySelectorAll('input')).at(-1).value`), '2.75');
  assert.equal(await evaluate("document.querySelector('.editor-status-strip code').textContent"), appendedRevision);
  await evaluate("document.querySelector('[data-operation=removeLastRow]').click()");
  await waitFor(`${rowsExpression}.length===3 && !document.querySelector('[data-operation=appendRow]').disabled`, 'real C# removeLastRow draft transform', 30_000);
  const removedRevision = await saveBoundary(appendedRevision);
  await closeProject();
  await openBoundary();
  await waitFor(`${rowsExpression}.length===3`, 'row removal reread from saved HDF');
  const finalValues = await evaluate(`${rowsExpression}.map(row=>Array.from(row.querySelectorAll('input')).at(-1).value)`);
  assert.deepEqual(finalValues, ['0.5', '1.25', '0.75'], 'Removing the appended row must preserve all original source values');
  report.editorSeries = { initialRows: 3, appendedRows: 4, finalRows: 3, appendedValue: 2.75, initialRevision, appendedRevision, removedRevision, draftLeftHdfUnchanged: true, invalidDraftBlocked: true, sourceValuesPreserved: true, reopenedFromDisk: true };
  report.checks.push('irregular rain-gauge append/remove run through C#, preserve source bytes until explicit save, reject an invalid new row and survive two HDF save/reopen cycles');
  const screenshot = await command('Page.captureScreenshot', { format: 'png' });
  await writeFile(path.join(output, 'server-gauge-editor.png'), Buffer.from(screenshot.data, 'base64'));
  await closeProject();
}

try {
  if (serverUrl) {
    const address = new URL(serverUrl);
    assert.ok(['http:', 'https:'].includes(address.protocol) && ['localhost', '127.0.0.1', '[::1]'].includes(address.hostname), 'Use a loopback server for UI verification');
    server = { url: `${address.origin}/`, close: async () => {} };
    const health = await (await fetch(new URL('/api/health', server.url))).json();
    assert.deepEqual(health, { status: 'ready', mode: 'server' });
    report.checks.push('real ASP.NET Core server readiness');
  } else server = await startStaticServer(path.join(root, 'dist'));
  const response = await fetch(server.url);
  assert.equal(response.status, 200);
  browser = spawn(chrome, ['--headless=new', '--no-first-run', '--no-default-browser-check', '--disable-background-networking', '--disable-component-update', '--disable-sync', '--disable-extensions', '--remote-debugging-port=0', `--user-data-dir=${profile}`, 'about:blank'], { stdio: ['ignore','ignore','pipe'] });
  const wsUrl = await new Promise((resolve, reject) => {
    let stderr = '';
    timeout = setTimeout(() => reject(new Error('Chrome did not expose a headless debug endpoint.')), 30_000);
    browser.once('error', reject);
    browser.once('exit', (code) => reject(new Error(`Chrome exited ${code}: ${stderr.slice(-1500)}`)));
    browser.stderr.on('data', (chunk) => {
      stderr += chunk.toString();
      const match = stderr.match(/DevTools listening on (ws:\/\/\S+)/);
      if (match) { clearTimeout(timeout); resolve(match[1]); }
    });
  });
  socket = new WebSocket(wsUrl);
  await new Promise((resolve, reject) => { socket.addEventListener('open', resolve, { once: true }); socket.addEventListener('error', reject, { once: true }); });
  socket.addEventListener('message', (event) => {
    const packet = JSON.parse(String(event.data));
    if (packet.id) {
      const waiter = pending.get(packet.id);
      if (!waiter) return;
      pending.delete(packet.id); clearTimeout(waiter.timer);
      if (packet.error) waiter.reject(new Error(JSON.stringify(packet.error))); else waiter.resolve(packet.result ?? {});
    } else if (packet.method === 'Runtime.exceptionThrown') exceptions.push(packet.params.exceptionDetails.exception?.description ?? packet.params.exceptionDetails.text);
    else if (packet.method === 'Fetch.requestPaused') {
      void (async () => {
        const { requestId, request, networkId } = packet.params;
        let fail = false;
        if (transportFaults) {
          if (request.method === 'POST' && request.postData) {
            try { if (JSON.parse(request.postData).method === 'plan.compute.execute') transportFaults.computeSubmissions++; } catch { /* Not an operation JSON body. */ }
          }
          if (request.method === 'GET' && /\/api\/operations\/[^/?]+\?/.test(request.url) && transportFaults.poll === 0) { transportFaults.poll++; fail = true; }
          if (request.method === 'DELETE' && transportFaults.cancel === 0) { transportFaults.cancel++; fail = true; }
        }
        if (fail) {
          if (networkId) injectedNetworkRequests.add(networkId);
          await command('Fetch.fulfillRequest', { requestId, responseCode: 503, responseHeaders: [{ name: 'Content-Type', value: 'application/json' }], body: Buffer.from(JSON.stringify({ error: { code: 'testTransientFailure', message: 'Injected transient transport failure' } })).toString('base64') });
        } else await command('Fetch.continueRequest', { requestId });
      })().catch((error) => exceptions.push(String(error)));
    }
    else if (packet.method === 'Network.responseReceived' && packet.params.response.status >= 400 && !injectedNetworkRequests.has(packet.params.requestId)) failedResources.push({ status: packet.params.response.status, url: packet.params.response.url });
    else if (packet.method === 'Page.javascriptDialogOpening') {
      const expectation = expectedDialog;
      expectedDialog = undefined;
      dialogs.push({ type: packet.params.type, message: packet.params.message, accepted: expectation?.accept ?? false });
      if (expectation) clearTimeout(expectation.timer);
      void command('Page.handleJavaScriptDialog', { accept: expectation?.accept ?? false }).then(
        () => expectation?.resolve(packet.params),
        (error) => expectation?.reject(error),
      );
      if (!expectation) exceptions.push('Unexpected browser dialog: ' + packet.params.message);
    }
  });
  const target = await command('Target.createTarget', { url: 'about:blank' }, false);
  ({ sessionId } = await command('Target.attachToTarget', { targetId: target.targetId, flatten: true }, false));
  await command('Runtime.enable'); await command('Page.enable'); await command('Network.enable');
  await command('Emulation.setDeviceMetricsOverride', { width: 1440, height: 1000, deviceScaleFactor: 1, mobile: false });
  if (targetedMapOnly) {
    const targetedManifestPath = geometryTopologyOnly
      ? mapFixturePath
      : terrainOnly ? process.env.HECRAS_VERIFY_TERRAIN_FIXTURE : process.env.HECRAS_VERIFY_RESULT_HOVER_FIXTURE;
    assert.ok(serverUrl && mapFixturePath && path.isAbsolute(targetedManifestPath ?? ''),
      'Targeted map verification requires the real server, map fixture and an absolute acceptance manifest.');
    await command('Page.addScriptToEvaluateOnNewDocument', {
      source: geometryTopologyOnly ? geometryMapFetchObserverSource : resultHoverFetchObserverSource,
    });
  }
  await command('Page.navigate', { url: server.url });
  await waitFor("document.querySelectorAll('.ras-feature-nav button').length>=8", 'React workspace');
  if (targetedMapOnly) {
    if (geometryTopologyOnly) {
      report.checks.push('geometry-only acceptance uses a scoped observer for real accepted and terminal operations');
    } else {
      report.checks.push('read-only Result-hover fetch observer installed before first page navigation');
    }
    await checkMapUI({ fixture: JSON.parse(await readFile(mapFixturePath, 'utf8')), output,
      evaluate, waitFor, command, setFiles, navigate, report, serverUrl });
    assert.deepEqual(exceptions, [], 'Uncaught browser errors');
    report.state = 'passed'; report.url = server.url;
    await writeFile(path.join(output, 'report.json'), JSON.stringify(report, null, 2) + '\n');
    process.stdout.write(JSON.stringify(report, null, 2) + '\n');
  } else {
  report.crossOriginIsolated = await evaluate('globalThis.crossOriginIsolated');
  assert.equal(await evaluate('document.documentElement.lang'), 'zh-CN');
  report.checks.push('Simplified Chinese is the default interface');
  for (const feature of ['project','map','editors','geometry','results','tools','compute']) await navigate(feature);
  assert.equal(await evaluate("document.querySelectorAll('.ras-page-error').length"), 0, 'Feature render boundary reported an error');
  await evaluate("document.querySelector('.ras-titlebar button').click()");
  await waitFor("document.documentElement.lang==='en'", 'English locale');
  assert.match(await evaluate("document.querySelector('.ras-feature-nav').innerText"), /Geometry and mesh/);
  await evaluate("document.querySelector('.ras-titlebar button').click()");
  await waitFor("document.documentElement.lang==='zh-CN'", 'Chinese locale');
  report.checks.push('Chinese and English workspace navigation');
  if (cancelProject) await verifyCancellation();
  if (computeProject) await verifyServerProject();
  if (editorProject) await verifyEditorSeries();
  if (workspaceZip) await verifyWorkspaceArchiveAndMaps();
  if (mapFixturePath) await checkMapUI({ fixture: JSON.parse(await readFile(mapFixturePath, 'utf8')), output,
    evaluate, waitFor, command, setFiles, navigate, report });
  await navigate('map');
  const geoFile = path.join(output, '地图检验.geojson');
  await writeFile(geoFile, JSON.stringify({ type: 'FeatureCollection', features: [
    { type: 'Feature', id: 'area', properties: { name: '验证区域' }, geometry: { type: 'Polygon', coordinates: [[[120,30],[120.02,30],[120.02,30.02],[120,30.02],[120,30]]] } },
    { type: 'Feature', id: 'line', properties: { name: '验证河道' }, geometry: { type: 'LineString', coordinates: [[120.002,30.003],[120.01,30.01],[120.018,30.015]] } },
  ] }));
  await setFiles('[data-feature="map"] input[type="file"]', [geoFile]);
  await waitFor("document.querySelectorAll('[data-feature=map] svg.map-canvas [data-feature-id]').length===2", 'two imported GeoJSON features');
  await evaluate("document.querySelector('[data-feature=map] svg.map-canvas [data-feature-id]').dispatchEvent(new MouseEvent('click',{bubbles:true}))");
  await waitFor("document.querySelector('[data-feature=map] .map-inspector-pane').innerText.includes('验证区域')", 'selected feature properties');
  report.checks.push('actual GeoJSON file import, two rendered features and selection properties');
  const importedMapPath = await evaluate("document.querySelector('[data-feature=map] svg.map-canvas .map-feature-path').getAttribute('d')");
  await evaluate("document.querySelector('[data-testid=map-zoom-to-point]').click()");
  await waitFor("document.querySelector('dialog.map-location-dialog')?.open", 'coordinate navigation dialog');
  await evaluate("(() => {const input=document.querySelector('.map-location-form input'); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(input,'invalid');input.dispatchEvent(new Event('input',{bubbles:true}));})()");
  await evaluate("document.querySelector('.map-location-form button[type=submit]').click()");
  await waitFor("Boolean(document.querySelector('.map-location-form [role=alert]'))", 'invalid coordinate diagnostic');
  assert.equal(await evaluate("document.querySelector('[data-feature=map] svg.map-canvas .map-feature-path').getAttribute('d')"), importedMapPath, 'Invalid coordinates changed the map camera');
  await evaluate("(() => {const input=document.querySelector('.map-location-form input'); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(input,'120.01, 30.01'); input.dispatchEvent(new Event('input',{bubbles:true}));})()");
  await evaluate("document.querySelector('.map-location-form button[type=submit]').click()");
  await waitFor(`!document.querySelector('dialog.map-location-dialog') && document.querySelector('[data-feature=map] svg.map-canvas .map-feature-path').getAttribute('d')!==${JSON.stringify(importedMapPath)}`, 'real camera movement to pasted point');
  const pointCameraPath = await evaluate("document.querySelector('[data-feature=map] svg.map-canvas .map-feature-path').getAttribute('d')");
  await evaluate("document.querySelector('[data-testid=map-zoom-to-point]').click()");
  await waitFor("document.querySelector('dialog.map-location-dialog')?.open", 'extent navigation dialog');
  await evaluate("(() => {const input=document.querySelector('.map-location-form input'); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(input,'120 30.02 120.02 30'); input.dispatchEvent(new Event('input',{bubbles:true}));})()");
  await waitFor("document.querySelector('.map-location-form select').value==='extent' && document.querySelectorAll('.map-location-form input').length===4", 'pasted source extent selects extent mode');
  await evaluate("document.querySelector('.map-location-form button[type=submit]').click()");
  await waitFor(`!document.querySelector('dialog.map-location-dialog') && document.querySelector('[data-feature=map] svg.map-canvas .map-feature-path').getAttribute('d')!==${JSON.stringify(pointCameraPath)}`, 'real extent camera movement');
  await evaluate("document.querySelector('[data-feature=map] .map-view-controls button[aria-label=适图]').click()");
  report.checks.push('coordinate navigation rejects invalid input without camera changes, parses point/extent pastes and navigates actual GeoJSON before fitting layers again');
  await waitFor("document.querySelectorAll('.ras-map-minimap__feature').length===2 && Boolean(document.querySelector('.ras-map-minimap__viewport'))", 'overview reuses actual visible GeoJSON and displays camera bounds');
  const beforeOverviewPan = await evaluate("document.querySelector('[data-feature=map] svg.map-canvas .map-feature-path').getAttribute('d')");
  await evaluate("document.querySelector('.ras-map-minimap__canvas').dispatchEvent(new KeyboardEvent('keydown',{key:'ArrowRight',bubbles:true,cancelable:true}))");
  await waitFor(`document.querySelector('[data-feature=map] svg.map-canvas .map-feature-path').getAttribute('d')!==${JSON.stringify(beforeOverviewPan)}`, 'overview keyboard moves the real main camera');
  const beforeOverviewWheelWidth = await evaluate("document.querySelector('[data-feature=map] svg.map-canvas .map-feature-path').getBBox().width");
  await evaluate("document.querySelector('.ras-map-minimap__canvas').dispatchEvent(new WheelEvent('wheel',{deltaY:120,bubbles:true,cancelable:true}))");
  await waitFor(`document.querySelector('[data-feature=map] svg.map-canvas .map-feature-path').getBBox().width < ${beforeOverviewWheelWidth * 0.95}`, 'overview scroll-down zooms the main map out');
  await evaluate("document.querySelector('[data-feature=map] .map-view-controls button[aria-label=适图]').click()");
  report.checks.push('overview draws the two real GeoJSON features, tracks camera bounds, pans by keyboard and zooms out for positive DOM wheel delta');
  await evaluate("document.querySelector('[data-feature=map] .map-tool-button[aria-label=点]').click()");
  await evaluate("(() => { const canvas=document.querySelector('[data-feature=map] svg.map-canvas'); const bounds=canvas.getBoundingClientRect(); canvas.dispatchEvent(new MouseEvent('click',{bubbles:true,clientX:bounds.left+bounds.width*0.35,clientY:bounds.top+bounds.height*0.45})); })()");
  await waitFor("document.querySelectorAll('[data-feature=map] svg.map-canvas [data-feature-id]').length===3", 'a newly drawn point');
  await evaluate("document.querySelectorAll('[data-feature=map] .map-undo-redo button')[0].click()");
  await waitFor("document.querySelectorAll('[data-feature=map] svg.map-canvas [data-feature-id]').length===2", 'undo removes only the drawing');
  await evaluate("document.querySelectorAll('[data-feature=map] .map-undo-redo button')[1].click()");
  await waitFor("document.querySelectorAll('[data-feature=map] svg.map-canvas [data-feature-id]').length===3", 'redo restores the drawing');
  await evaluate("document.querySelector('[data-feature=map] svg.map-canvas').dispatchEvent(new KeyboardEvent('keydown',{key:'z',ctrlKey:true,bubbles:true}))");
  await waitFor("document.querySelectorAll('[data-feature=map] svg.map-canvas [data-feature-id]').length===2", 'map keyboard undo');
  await evaluate("document.querySelector('[data-feature=map] svg.map-canvas').dispatchEvent(new KeyboardEvent('keydown',{key:'z',ctrlKey:true,shiftKey:true,bubbles:true}))");
  await waitFor("document.querySelectorAll('[data-feature=map] svg.map-canvas [data-feature-id]').length===3", 'map keyboard redo');
  report.checks.push('drawing history undo and redo through buttons and map-scoped keyboard shortcuts');
  await evaluate("(() => { const input=document.querySelector('[data-feature=map] .map-background-opacity input'); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(input,'128'); input.dispatchEvent(new Event('input',{bubbles:true})); input.dispatchEvent(new Event('change',{bubbles:true})); document.querySelector('[data-feature=map] .map-background-master input').click(); })()");
  await waitFor("document.querySelector('[data-feature=map] .map-background-opacity input').getAttribute('aria-valuetext')==='50%' && document.querySelector('[data-feature=map] .map-background-master input').checked", 'background settings local draft');
  await evaluate("document.querySelector('[data-feature=map] .map-background-reset').click()");
  await waitFor("document.querySelector('[data-feature=map] .map-background-opacity input').value==='255' && !document.querySelector('[data-feature=map] .map-background-master input').checked", 'background settings reset');
  report.checks.push('background options and opacity update and reset local settings without fabricating imagery');
  const screenshot = await command('Page.captureScreenshot', { format: 'png' });
  await writeFile(path.join(output, 'workspace-map.png'), Buffer.from(screenshot.data, 'base64'));
  await navigate('results');
  const csvFile = path.join(output, '时间序列检验.csv');
  await writeFile(csvFile, 'Time,上游水位,出流\n2026-10-22T00:00:00Z,43,1.25\n2026-10-22T00:01:00Z,43.5,1.5\n2026-10-22T00:02:00Z,44,2\n');
  await setFiles('[data-feature="results"] input[type="file"]', [csvFile]);
  await waitFor("document.querySelectorAll('[data-feature=results] .results-chart-series').length===2 && document.querySelectorAll('[data-feature=results] .results-data-table tbody tr').length===3", 'CSV chart and table');
  const beforeValues = await evaluate("Array.from(document.querySelectorAll('[data-feature=results] .results-cell-input')).map(n=>n.value)");
  assert.deepEqual(beforeValues, ['43','1.25','43.5','1.5','44','2']);
  const originalCurve = await evaluate("document.querySelector('[data-feature=results] .results-chart-series').getAttribute('d')");
  await evaluate("(() => { const input=document.querySelector('[data-feature=results] .results-cell-input'); input.focus(); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(input,'45.25'); input.dispatchEvent(new Event('input',{bubbles:true})); })()");
  await waitFor("document.querySelector('[data-feature=results] .results-cell-input').value==='45.25'", 'edited CSV cell');
  await evaluate("document.querySelector('[data-feature=results] .results-cell-input').blur()");
  await waitFor(`document.querySelector('[data-feature=results] .results-chart-series').getAttribute('d')!==${JSON.stringify(originalCurve)}`, 'edited value reaching the real chart');
  await navigate('map'); await navigate('results');
  assert.equal(await evaluate("document.querySelector('[data-feature=results] .results-cell-input').value"), '45.25');
  report.checks.push('actual CSV import, two chart series, three table rows and draft retention across navigation');
  await setFirstResultCell('invalid-number');
  await evaluate("document.querySelector('[data-feature=results] .results-cell-input').blur()");
  await waitFor("document.querySelector('[data-feature=results] .results-cell-input').getAttribute('aria-invalid')==='true'", 'invalid cell error');
  assert.equal(await evaluate("Array.from(document.querySelectorAll('[data-feature=results] button')).find(n=>n.textContent.includes('导出 CSV')).disabled"), true);
  await navigate('map'); await navigate('results');
  assert.equal(await evaluate("document.querySelector('[data-feature=results] .results-cell-input').value"), 'invalid-number');
  const rejectImport = expectDialog(false);
  await setFiles('[data-feature="results"] input[type="file"]', [csvFile]);
  await rejectImport;
  assert.equal(await evaluate("document.querySelector('[data-feature=results] .results-cell-input').value"), 'invalid-number');
  const acceptImport = expectDialog(true);
  await setFiles('[data-feature="results"] input[type="file"]', [csvFile]);
  await acceptImport;
  await waitFor("document.querySelector('[data-feature=results] .results-cell-input').value==='43' && !document.querySelector('[data-feature=results] .results-cell-input').hasAttribute('aria-invalid')", 'confirmed replacement clearing old drafts');
  report.checks.push('invalid numeric input survives blur and navigation, blocks export, and reimport honors discard confirmation');
  const downloads = await mkdtemp(path.join(output, 'downloads-'));
  await command('Browser.setDownloadBehavior', { behavior: 'allow', downloadPath: downloads }, false);
  await setFirstResultCell('46.125');
  // Programmatic click leaves the input focused: export must include the valid raw draft.
  await evaluate("Array.from(document.querySelectorAll('[data-feature=results] button')).find(n=>n.textContent.includes('导出 CSV')).click()");
  let downloaded;
  const downloadDeadline = Date.now() + 20_000;
  while (Date.now() < downloadDeadline) {
    downloaded = (await readdir(downloads)).find((file) => file.endsWith('.csv'));
    if (downloaded) break;
    await delay(100);
  }
  assert.ok(downloaded, 'CSV export did not create a downloaded file');
  const exported = await readFile(path.join(downloads, downloaded), 'utf8');
  assert.ok(exported.includes('"2026-10-22T00:00:00Z","46.125","1.25"'), 'Export lost a valid unblurred cell draft');
  assert.equal(await evaluate("document.querySelectorAll('[data-feature=results] [aria-invalid=true]').length"), 0);
  report.checks.push('actual CSV download includes the valid unblurred draft');
  const delayedCsv = path.join(output, 'delayed-import.csv');
  const latestCsv = path.join(output, 'latest-import.csv');
  await writeFile(delayedCsv, 'Time,Value\n2026-10-22T00:00:00Z,100\n');
  await writeFile(latestCsv, 'Time,Value\n2026-10-22T00:00:00Z,200\n');
  // Control only read completion order; both inputs use real browser File objects.
  await evaluate("(() => { const original=File.prototype.text; globalThis.__restoreFileText=()=>{File.prototype.text=original;}; File.prototype.text=async function(){const content=await original.call(this); if(this.name==='delayed-import.csv') await new Promise(resolve=>{globalThis.__releaseDelayedImport=resolve;}); return content;}; })()");
  await setFiles('[data-feature="results"] input[type="file"]', [delayedCsv]);
  await waitFor("typeof globalThis.__releaseDelayedImport==='function'", 'delayed file read');
  await setFiles('[data-feature="results"] input[type="file"]', [latestCsv]);
  await waitFor("document.querySelector('[data-feature=results] .results-cell-input').value==='200'", 'latest imported file');
  await evaluate("(async()=>{globalThis.__releaseDelayedImport();await new Promise(requestAnimationFrame);await new Promise(requestAnimationFrame);globalThis.__restoreFileText();})()");
  assert.equal(await evaluate("document.querySelector('[data-feature=results] .results-cell-input').value"), '200');
  assert.ok(await evaluate("document.querySelector('[data-feature=results] .results-source-field select').textContent.includes('latest-import.csv')"));
  report.checks.push('a delayed older real-file import cannot replace the newer accepted dataset');
  const resultScreenshot = await command('Page.captureScreenshot', { format: 'png' });
  await writeFile(path.join(output, 'workspace-results.png'), Buffer.from(resultScreenshot.data, 'base64'));
  await navigate('project');
  await evaluate("document.querySelector('.ras-menubar details summary').click(); Array.from(document.querySelectorAll('.ras-menu-popover button')).find(n=>n.textContent.includes('新建工程')).click()");
  await waitFor("document.querySelector('[data-feature=project]').innerText.includes('投影设置') || document.querySelector('[data-feature=project]').innerText.includes('坐标参考')", 'new project menu action');
  report.checks.push('File menu new-project action reaches its real form');
  await navigate('map');
  await command('Emulation.setDeviceMetricsOverride', { width: 900, height: 700, deviceScaleFactor: 1, mobile: false });
  assert.equal(await evaluate('document.documentElement.scrollWidth<=window.innerWidth'), true, 'Workspace exceeds viewport width');
  report.checks.push('900px responsive workspace width');
  assert.deepEqual(exceptions, [], 'Uncaught browser errors');
  report.state = 'passed'; report.url = server.url;
  await writeFile(path.join(output, 'report.json'), JSON.stringify(report, null, 2) + '\n');
  process.stdout.write(JSON.stringify(report, null, 2) + '\n');
  }
} catch (error) {
  report.state = 'failed'; report.error = error instanceof Error ? error.stack : String(error);
  if (socket?.readyState === WebSocket.OPEN && sessionId) {
    try {
      report.diagnostics = await evaluate("({hash:location.hash, navigation:Array.from(document.querySelectorAll('.ras-feature-nav button')).map(n=>({text:n.innerText,current:n.getAttribute('aria-current')})),pages:Array.from(document.querySelectorAll('[data-feature]')).map(n=>({feature:n.getAttribute('data-feature'),hidden:n.hidden,text:n.innerText.slice(0,1000)})),body:document.body.innerText.slice(0,9000)})");
      const failureScreenshot = await command('Page.captureScreenshot', { format: 'png' });
      await writeFile(path.join(output, 'failure.png'), Buffer.from(failureScreenshot.data, 'base64'));
    } catch (diagnosticError) { report.diagnosticError = String(diagnosticError); }
  }
  await writeFile(path.join(output, 'report.json'), JSON.stringify(report, null, 2) + '\n');
  process.stderr.write(JSON.stringify(report, null, 2) + '\n');
  process.exitCode = 1;
} finally {
  clearTimeout(timeout);
  if (expectedDialog) clearTimeout(expectedDialog.timer);
  for (const waiter of pending.values()) clearTimeout(waiter.timer);
  socket?.close(); browser?.kill('SIGTERM');
  if (browser && browser.exitCode === null) await Promise.race([new Promise((resolve) => browser.once('exit', resolve)), delay(5000).then(() => browser.kill('SIGKILL'))]);
  await server?.close();
  await rm(profile, { recursive: true, force: true, maxRetries: 3, retryDelay: 150 });
}
