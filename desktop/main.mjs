import { mkdtemp, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { app, BrowserWindow, ipcMain, Menu, shell } from 'electron';
import { connectBackend, getServerHealth, getServerRuntime } from './server-runtime.mjs';
import { startStaticServer } from './static-server.mjs';
import { runElectronMapUISmoke } from './map-ui-smoke.mjs';
import { geometryMapFetchObserverSource } from '../scripts/check-geometry-map-ui.mjs';
import { resultHoverFetchObserverSource } from '../scripts/result-hover-fetch-observer.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const distDirectory = path.resolve(here, '../dist');
const wasmSmokeMode = process.argv.includes('--wasm-smoke');
const uiSmokeMode = process.argv.includes('--ui-smoke');
const serverSmokeMode = process.argv.includes('--server-smoke');
const smokeMode = wasmSmokeMode || uiSmokeMode || serverSmokeMode;
let smokeUserDataDirectory;
if (smokeMode) {
  smokeUserDataDirectory = await mkdtemp(path.join(os.tmpdir(), 'hec-ras-electron-smoke-'));
  app.setPath('userData', smokeUserDataDirectory);
}

let staticServer;
let backendConnection;
let mainWindow;
let smokeFinished = false;
let runtimeCloseRequested = false;
let backendStartupPromise;
const backendStartupAbort = new AbortController();

ipcMain.handle('hec:open-external-url', async (event, value) => {
  if (!mainWindow || event.sender !== mainWindow.webContents || event.senderFrame !== mainWindow.webContents.mainFrame ||
      typeof value !== 'string' || value.length > 8192) throw new Error('External URL request is not from the active application.');
  const url = new URL(value);
  if (url.protocol !== 'https:' || url.username || url.password || !url.hostname)
    throw new Error('External authorization requires an HTTPS URL.');
  await shell.openExternal(url.href);
  return true;
});

Menu.setApplicationMenu(null);
if (smokeMode) app.disableHardwareAcceleration();
function requestQuit() {
  backendStartupAbort.abort();
  const pendingStartup = backendStartupPromise;
  if (pendingStartup) void pendingStartup.catch(() => {}).finally(() => app.quit());
  else app.quit();
}
process.once('SIGINT', requestQuit);
process.once('SIGTERM', requestQuit);

async function stopServer() {
  const active = staticServer;
  staticServer = undefined;
  const connection = backendConnection;
  backendConnection = undefined;
  const closes = [];
  if (active) closes.push(active.close());
  if (connection) closes.push(connection.close());
  const results = await Promise.allSettled(closes);
  const failures = results.filter((result) => result.status === 'rejected');
  if (failures.length) {
    throw new Error(`Could not stop ${failures.length} app-owned server resource(s): ${failures.map((result) => result.reason instanceof Error ? result.reason.message : String(result.reason)).join('; ')}`);
  }
}

async function createMainWindow() {
  let baseUrl;
  if (wasmSmokeMode) {
    if (!staticServer) staticServer = await startStaticServer(distDirectory);
    baseUrl = staticServer.url;
  } else {
    if (!backendConnection) {
      const pendingStartup = connectBackend({
        projectRoot: path.resolve(here, '..'),
        webRoot: distDirectory,
        signal: backendStartupAbort.signal,
      });
      backendStartupPromise = pendingStartup;
      try {
        backendConnection = await pendingStartup;
      } finally {
        if (backendStartupPromise === pendingStartup) backendStartupPromise = undefined;
      }
    }
    baseUrl = `${backendConnection.baseUrl}/`;
  }
  const startUrl = new URL(baseUrl);
  startUrl.searchParams.set('electron', '1');
  if (wasmSmokeMode) {
    startUrl.searchParams.set('smoke', '1');
    startUrl.hash = '/compute';
  }

  const additionalArguments = [];
  if (wasmSmokeMode) additionalArguments.push('--hec-wasm-smoke');
  if (uiSmokeMode) additionalArguments.push('--hec-ui-smoke');
  if (serverSmokeMode) additionalArguments.push('--hec-server-smoke');

  const window = new BrowserWindow({
    width: 1440,
    height: 960,
    minWidth: 800,
    minHeight: 560,
    show: !smokeMode,
    backgroundColor: '#071822',
    autoHideMenuBar: true,
    webPreferences: {
      preload: path.join(here, 'preload.cjs'),
      additionalArguments,
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      webSecurity: true,
      allowRunningInsecureContent: false,
    },
  });
  mainWindow = window;
  const origin = startUrl.origin;
  window.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
  window.webContents.on('will-navigate', (event, targetUrl) => {
    try {
      if (new URL(targetUrl).origin !== origin) event.preventDefault();
    } catch {
      event.preventDefault();
    }
  });
  window.webContents.on('will-redirect', (event, targetUrl) => {
    try {
      if (new URL(targetUrl).origin !== origin) event.preventDefault();
    } catch {
      event.preventDefault();
    }
  });
  window.webContents.on('render-process-gone', (_event, details) => {
    if (!smokeMode || smokeFinished) return;
    finishSmoke(1, { kind: 'renderer-exit', reason: details.reason, exitCode: details.exitCode });
  });
  window.on('closed', () => {
    if (mainWindow === window) mainWindow = undefined;
  });

  let fetchObserverAttachedHere = false;
  const geometryTopologyOnly = process.env.HECRAS_VERIFY_GEOMETRY_TOPOLOGY_ONLY === '1' ||
      process.env.HECRAS_VERIFY_GEOMETRY_TRACE_ONLY === '1';
  if (serverSmokeMode && (geometryTopologyOnly || process.env.HECRAS_VERIFY_RESULT_HOVER_ONLY === '1' ||
      process.env.HECRAS_VERIFY_TERRAIN_ONLY === '1')) {
    // A new hidden BrowserWindow may not have a renderer yet. Create one on a
    // blank document before sending Page commands; awaiting Page.enable first
    // can otherwise wait forever for the renderer that loadURL would create.
    // The observer is still installed before the first application navigation.
    await window.loadURL('about:blank');
    const debuggerClient = window.webContents.debugger;
    debuggerClient.attach('1.3');
    fetchObserverAttachedHere = true;
    for (const [method, parameters] of [
      ['Page.enable', {}],
      ['Page.addScriptToEvaluateOnNewDocument', { source: geometryTopologyOnly
        ? geometryMapFetchObserverSource : resultHoverFetchObserverSource }],
    ]) {
      let timer;
      try {
        await Promise.race([
          debuggerClient.sendCommand(method, parameters),
          new Promise((_, reject) => {
            timer = setTimeout(() => reject(new Error(`Result hover observer installation timed out: ${method}`)), 20_000);
          }),
        ]);
      } finally {
        clearTimeout(timer);
      }
    }
  }
  await window.loadURL(startUrl.href);
  if (smokeMode) window.webContents.setAudioMuted(true);
  if (serverSmokeMode) {
    const result = await runServerSmoke(window, backendConnection.baseUrl);
    if (fetchObserverAttachedHere && window.webContents.debugger.isAttached()) {
      try {
        window.webContents.debugger.detach();
      } catch (error) {
        result.passed = false;
        result.kind = 'failed';
        result.observerDetachError = error instanceof Error ? error.message : String(error);
      }
    }
    finishSmoke(result.passed ? 0 : 1, result);
  } else if (uiSmokeMode) {
    const result = await runUiSmoke(window);
    finishSmoke(result.passed ? 0 : 1, {
      kind: result.passed ? 'completed' : 'failed',
      backendExecuted: false,
      ...result,
    });
  }
}

async function runServerSmoke(window, baseUrl) {
  let hydraulicUi;
  let hydraulicPassed = true;
  let cancellationUi;
  let cancellationPassed = true;
  let mapUi;
  let mapPassed = true;
  try {
    const geometryTopologyOnly = process.env.HECRAS_VERIFY_GEOMETRY_TOPOLOGY_ONLY === '1' ||
      process.env.HECRAS_VERIFY_GEOMETRY_TRACE_ONLY === '1';
    const targetedMapOnly = geometryTopologyOnly || process.env.HECRAS_VERIFY_RESULT_HOVER_ONLY === '1' ||
      process.env.HECRAS_VERIFY_TERRAIN_ONLY === '1';
    if (targetedMapOnly && !process.env.HECRAS_VERIFY_MAP_FIXTURE?.trim()) {
      throw new Error('Targeted Electron map acceptance requires HECRAS_VERIFY_MAP_FIXTURE.');
    }
    const health = await getServerHealth(baseUrl);
    const runtime = await getServerRuntime(baseUrl);
    const pageTransport = await window.webContents.executeJavaScript(`(async () => {
      const [healthResponse, runtimeResponse] = await Promise.all([
        fetch('/api/health', { headers: { Accept: 'application/json' }, credentials: 'omit', redirect: 'error' }),
        fetch('/api/runtime', { headers: { Accept: 'application/json' }, credentials: 'omit', redirect: 'error' }),
      ]);
      if (!healthResponse.ok || !runtimeResponse.ok) throw new Error('Same-origin server API request failed.');
      return { health: await healthResponse.json(), runtime: await runtimeResponse.json() };
    })()`);
    if (pageTransport.health?.status !== 'ready' || pageTransport.health?.mode !== 'server' ||
        pageTransport.runtime?.mode !== 'server' || typeof pageTransport.runtime.runtime !== 'string' ||
        !Array.isArray(pageTransport.runtime.operations)) {
      throw new Error('The React page received an unexpected same-origin server response.');
    }
    const ui = targetedMapOnly
      ? { passed: true, backendExecuted: false, computeExecuted: false, navigationItems: [], renderBoundaryErrors: [] }
      : await runUiSmoke(window);
    if (process.env.HECRAS_VERIFY_MAP_FIXTURE?.trim()) {
      if (!ui.passed) {
        mapUi = { state: 'not-started', passed: false, checks: [], error: 'The base React UI smoke did not pass; map acceptance was not started.' };
        mapPassed = false;
      } else {
        mapUi = await runElectronMapUISmoke({
          window,
          fixturePath: process.env.HECRAS_VERIFY_MAP_FIXTURE.trim(),
          serverUrl: baseUrl,
        });
        mapPassed = mapUi.passed === true;
      }
    }
    if (!geometryTopologyOnly && process.env.HECRAS_VERIFY_CANCEL_PROJECT !== undefined) {
      const cancelProjectPath = process.env.HECRAS_VERIFY_CANCEL_PROJECT.trim();
      if (!path.isAbsolute(cancelProjectPath) || path.extname(cancelProjectPath).toLowerCase() !== '.ras') {
        cancellationUi = {
          state: 'invalid-input', step: null, resultPath: null, revision: null,
          cancelled: false, cancelStarted: false, computeStarted: false, nextInspectionId: null,
          error: 'HECRAS_VERIFY_CANCEL_PROJECT must be an absolute path to a .ras fixture.',
        };
        cancellationPassed = false;
      } else if (!ui.passed) {
        cancellationUi = {
          state: 'not-started', step: null, resultPath: null, revision: null,
          cancelled: false, cancelStarted: false, computeStarted: false, nextInspectionId: null,
          error: 'The base React UI smoke did not pass; project cancellation was not started.',
        };
        cancellationPassed = false;
      } else {
        const cancellationResult = await runHydraulicUiSmoke(
          window,
          cancelProjectPath,
          process.env.HECRAS_VERIFY_CANCEL_PLAN_NAME?.trim() || 'Plan CPU',
          'cancel',
        );
        cancellationUi = cancellationResult.cancellationUi;
        cancellationPassed = cancellationResult.passed === true;
      }
    }
    if (!geometryTopologyOnly && process.env.HECRAS_VERIFY_PROJECT !== undefined) {
      const projectPath = process.env.HECRAS_VERIFY_PROJECT.trim();
      if (!path.isAbsolute(projectPath) || path.extname(projectPath).toLowerCase() !== '.ras') {
        hydraulicUi = {
          state: 'invalid-input', resultPath: null, revision: null, profileRowCount: 0,
          completed: false, computeStarted: false,
          error: 'HECRAS_VERIFY_PROJECT must be an absolute path to a .ras fixture.',
        };
        hydraulicPassed = false;
      } else if (!ui.passed) {
        hydraulicUi = {
          state: 'not-started', resultPath: null, revision: null, profileRowCount: 0,
          completed: false, computeStarted: false,
          error: 'The base React UI smoke did not pass; project compute was not started.',
        };
        hydraulicPassed = false;
      } else {
        const hydraulicResult = await runHydraulicUiSmoke(
          window,
          projectPath,
          process.env.HECRAS_VERIFY_PLAN_NAME?.trim() || 'Plan CPU',
        );
        hydraulicUi = hydraulicResult.hydraulicUi;
        hydraulicPassed = hydraulicResult.passed === true;
      }
    }
    const passed = ui.passed && hydraulicPassed && cancellationPassed && mapPassed;
    return {
      type: 'HEC_RAS_SERVER_ELECTRON_SMOKE',
      health,
      runtime,
      sameOriginApi: true,
      ...ui,
      kind: passed ? 'completed' : 'failed',
      passed,
      backendExecuted: hydraulicUi?.computeStarted === true || cancellationUi?.computeStarted === true || mapUi?.computeStarted === true,
      computeExecuted: hydraulicUi?.computeStarted === true || cancellationUi?.computeStarted === true || mapUi?.computeStarted === true,
      ...(hydraulicUi ? { hydraulicUi } : {}),
      ...(cancellationUi ? { cancellationUi } : {}),
      ...(mapUi ? { mapUi } : {}),
    };
  } catch (error) {
    return {
      type: 'HEC_RAS_SERVER_ELECTRON_SMOKE',
      kind: 'failed',
      passed: false,
      backendExecuted: hydraulicUi?.computeStarted === true || cancellationUi?.computeStarted === true || mapUi?.computeStarted === true,
      computeExecuted: hydraulicUi?.computeStarted === true || cancellationUi?.computeStarted === true || mapUi?.computeStarted === true,
      error: error instanceof Error ? error.message : String(error),
      ...(mapUi ? { mapUi } : process.env.HECRAS_VERIFY_MAP_FIXTURE?.trim() ? {
        mapUi: { state: 'failed', passed: false, checks: [], error: error instanceof Error ? error.message : String(error) },
      } : {}),
      ...(process.env.HECRAS_VERIFY_PROJECT !== undefined ? {
        hydraulicUi: hydraulicUi ?? {
          state: 'failed', resultPath: null, revision: null, profileRowCount: 0,
          completed: false, computeStarted: false,
          error: error instanceof Error ? error.message : String(error),
        },
      } : {}),
      ...(process.env.HECRAS_VERIFY_CANCEL_PROJECT !== undefined ? {
        cancellationUi: cancellationUi ?? {
          state: 'failed', step: null, resultPath: null, revision: null,
          cancelled: false, cancelStarted: false, computeStarted: false, nextInspectionId: null,
          error: error instanceof Error ? error.message : String(error),
        },
      } : {}),
    };
  }
}

async function runHydraulicUiSmoke(window, projectPath, planName, mode = 'complete') {
  const fallback = mode === 'cancel'
    ? { state: 'not-started', step: null, resultPath: null, revision: null, cancelled: false, cancelStarted: false, computeStarted: false, nextInspectionId: null }
    : { state: 'not-started', resultPath: null, revision: null, profileRowCount: 0, completed: false, computeStarted: false };
  try {
    return await window.webContents.executeJavaScript(`(async () => {
      const observed = ${JSON.stringify(fallback)};
      const mode = ${scriptStringLiteral(mode)};
      let projectOpened = false;
      const waitFor = async (label, predicate, timeoutMs = 15000) => {
        const deadline = Date.now() + timeoutMs;
        while (Date.now() < deadline) {
          const value = predicate();
          if (value) return value;
          await new Promise((resolve) => setTimeout(resolve, 30));
        }
        throw new Error('Timed out waiting for ' + label);
      };
      const clickNav = async (label, feature) => {
        const button = await waitFor('navigation ' + label, () => Array.from(document.querySelectorAll('.ras-feature-nav button')).find((item) => item.textContent.trim() === label));
        button.click();
        await waitFor('visible ' + feature + ' page', () => {
          const page = document.querySelector('[data-feature="' + feature + '"]');
          return page && !page.hidden ? page : undefined;
        });
      };
      const parseOutput = () => {
        const output = document.querySelector('[data-feature="compute"] [data-compute-output]');
        if (!output) return undefined;
        try { return JSON.parse(output.textContent); } catch { return undefined; }
      };
      const readInspectionId = () => {
        const label = Array.from(document.querySelectorAll('[data-feature="compute"] .plan-inspection-grid dt'))
          .find((item) => item.textContent.trim() === '检查身份');
        const value = label?.nextElementSibling?.textContent.trim();
        return value || undefined;
      };
      const closeOpenedProject = async () => {
        if (!projectOpened) return;
        const menu = await waitFor('工程菜单用于关闭验收工程', () => document.querySelector('.ras-menubar details summary'));
        menu.click();
        const closeCommand = await waitFor('关闭工程菜单项', () => Array.from(document.querySelectorAll('.ras-menu-popover button')).find((item) => item.textContent.includes('关闭工程')));
        closeCommand.click();
        await waitFor('验收工程关闭', () => document.querySelector('.ras-statusbar')?.innerText.includes('无活动工程'), 30000);
        projectOpened = false;
      };
      try {
        await clickNav('工程', 'project');
        await waitFor('project open menu', () => document.querySelector('.ras-menubar details summary'));
        document.querySelector('.ras-menubar details summary').click();
        const openCommand = await waitFor('打开工程 menu item', () => Array.from(document.querySelectorAll('.ras-menu-popover button')).find((item) => item.textContent.includes('打开工程')));
        openCommand.click();
        const pathInput = await waitFor('project path dialog', () => document.querySelector('.project-open-path-row input'));
        pathInput.focus();
        Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(pathInput, ${scriptStringLiteral(projectPath)});
        pathInput.dispatchEvent(new Event('input', { bubbles: true }));
        const openButton = await waitFor('project path accepted', () => {
          const button = document.querySelector('.project-open-path-row button');
          return button && !button.disabled ? button : undefined;
        });
        openButton.click();
        projectOpened = true;
        await waitFor('real project tree from C#', () => !document.querySelector('.project-open-modal') && document.querySelectorAll('.project-tree-node').length > 0, 60000);

        const expandAll = Array.from(document.querySelectorAll('.project-tree-toolbar button')).find((item) => item.textContent.trim() === '全部展开');
        if (!expandAll) throw new Error('Project tree does not expose its expand-all control.');
        expandAll.click();
        const planNode = await waitFor('source plan ' + ${scriptStringLiteral(planName)}, () => Array.from(document.querySelectorAll('.project-tree-node')).find((item) => item.textContent.includes(${scriptStringLiteral(planName)})));
        planNode.dispatchEvent(new MouseEvent('dblclick', { bubbles: true }));
        await waitFor('opened source plan', () => document.querySelector('.ras-document-title')?.innerText.includes(${scriptStringLiteral(planName)}), 60000);

        await clickNav('计算', 'compute');
        const runButton = await waitFor('real Plan inspection response', () => {
          const button = document.querySelector('[data-feature="compute"] [data-compute-action="run"]');
          const invalid = document.querySelector('[data-feature="compute"] .compute-invalid-hint,[data-feature="compute"] [role="alert"],[data-feature="compute"] .runtime-indicator.is-error');
          return button && (!button.disabled || invalid) ? button : undefined;
        }, 60000);
        if (runButton.disabled) throw new Error('The inspected Plan is not runnable: ' + document.querySelector('[data-feature="compute"]').innerText);
        const initialInspectionId = await waitFor('source plan inspection identity', readInspectionId, 30000);
        if (mode === 'cancel') observed.initialInspectionId = initialInspectionId;
        runButton.click();
        observed.computeStarted = true;
        observed.state = 'running';

        if (mode === 'cancel') {
          const progress = await waitFor('Plan Compute progress with a positive Steps value', () => {
            const raw = document.querySelector('[data-feature="compute"] .progress-code')?.textContent;
            if (!raw) return undefined;
            try {
              const event = JSON.parse(raw);
              const rawStep = event?.values?.Steps;
              const step = Number(String(rawStep ?? '').replaceAll(',', ''));
              return event?.taskName === 'Plan Compute' && Number.isFinite(step) && step > 0
                ? { event, step }
                : undefined;
            } catch { return undefined; }
          }, 120000);
          observed.step = progress.step;
          observed.taskName = progress.event.taskName;
          observed.progressValues = progress.event.values ?? null;
          const cancelButton = await waitFor('active compute cancellation control', () => {
            const button = document.querySelector('[data-feature="compute"] [data-compute-action="cancel"]');
            return button && !button.disabled ? button : undefined;
          }, 10000);
          cancelButton.click();
          observed.cancelStarted = true;

          await waitFor('UI and raw result confirm server cancellation', () => {
            const status = document.querySelector('[data-feature="compute"] [data-testid="compute-status"]')?.innerText?.trim() ?? '';
            const result = parseOutput();
            return status.includes('计算已取消') && result?.state === 'cancelled' && result?.summary?.success !== true
              ? { status, result }
              : undefined;
          }, 180000);
          const cancelStatus = document.querySelector('[data-feature="compute"] [data-testid="compute-status"]')?.innerText?.trim() ?? '';
          const cancelledResult = parseOutput();
          observed.state = cancelledResult?.state ?? 'unverified';
          observed.rawResultState = cancelledResult?.state ?? null;
          observed.resultPath = typeof cancelledResult?.resultPath === 'string' ? cancelledResult.resultPath : null;
          observed.revision = typeof cancelledResult?.resultRevision === 'string' ? cancelledResult.resultRevision : null;
          observed.resultInspectionId = typeof cancelledResult?.inspectionId === 'string' ? cancelledResult.inspectionId : null;
          observed.summarySuccess = typeof cancelledResult?.summary?.success === 'boolean' ? cancelledResult.summary.success : null;
          observed.cancelStatus = cancelStatus;
          observed.cancelled = observed.cancelStarted === true
            && cancelStatus.includes('计算已取消')
            && cancelledResult?.state === 'cancelled'
            && cancelledResult?.summary?.success !== true
            && cancelledResult?.inspectionId === initialInspectionId;
          if (!observed.cancelled) throw new Error('The UI did not confirm a cancelled result matching its inspected plan.');

          const nextInspectionId = await waitFor('automatic post-cancellation inspection and runnable plan', () => {
            const nextId = readInspectionId();
            const nextRunButton = document.querySelector('[data-feature="compute"] [data-compute-action="run"]');
            return nextId && nextId !== initialInspectionId && nextRunButton && !nextRunButton.disabled ? nextId : undefined;
          }, 120000);
          observed.nextInspectionId = nextInspectionId;
          observed.inspectionChanged = nextInspectionId !== initialInspectionId;
          await closeOpenedProject();
          return { passed: observed.cancelled && observed.step > 0 && observed.inspectionChanged, cancellationUi: observed };
        }

        await waitFor('confirmed Plan compute terminal state', () => {
          const status = document.querySelector('[data-feature="compute"] [data-testid="compute-status"]')?.innerText ?? '';
          return /计算完成|计算失败|结果格式无法识别|计算已取消|Worker 已停止/.test(status);
        }, 180000);
        const statusText = document.querySelector('[data-feature="compute"] [data-testid="compute-status"]')?.innerText ?? '';
        const result = parseOutput();
        observed.statusText = statusText;
        if (result && typeof result === 'object') {
          observed.state = typeof result.state === 'string' ? result.state : 'failed';
          observed.resultPath = typeof result.resultPath === 'string' ? result.resultPath : null;
          observed.revision = typeof result.resultRevision === 'string' ? result.resultRevision : null;
        } else {
          observed.state = /计算已取消/.test(statusText) ? 'cancelled' : /计算失败|结果格式无法识别|Worker 已停止/.test(statusText) ? 'failed' : 'unverified';
        }
        if (observed.state !== 'completed' || !/计算完成/.test(statusText)
            || result?.summary?.success !== true
            || typeof observed.resultPath !== 'string' || observed.resultPath.length === 0
            || typeof observed.revision !== 'string' || !/^[0-9a-f]{64}$/i.test(observed.revision)) {
          throw new Error('The UI did not confirm a completed compute with a server result path and revision.');
        }
        observed.completed = true;
        observed.completionEvidence = 'compute-status:计算完成; data-compute-output.state=completed; summary.success=true';

        const openResult = await waitFor('confirmed result action', () => {
          const button = document.querySelector('[data-feature="compute"] [data-compute-action="open-result"]');
          return button && !button.disabled ? button : undefined;
        }, 30000);
        openResult.click();
        await waitFor('reopened HDF result and Cell Depth option', () => location.hash === '#/results'
          && Array.from(document.querySelectorAll('.results-selector-grid select')).some((select) => Array.from(select.options).some((option) => option.value === 'Cell Depth')), 60000);
        const variableSelect = Array.from(document.querySelectorAll('.results-selector-grid select')).find((select) => Array.from(select.options).some((option) => option.value === 'Cell Depth'));
        Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value').set.call(variableSelect, 'Cell Depth');
        variableSelect.dispatchEvent(new Event('change', { bubbles: true }));
        await waitFor('Cell Depth selection', () => Array.from(document.querySelectorAll('.results-selector-grid select')).some((select) => select.value === 'Cell Depth'));
        const loadSeries = await waitFor('Cell Depth load-series action', () => Array.from(document.querySelectorAll('.results-selector-panel button')).find((item) => item.textContent.includes('载入序列') && !item.disabled));
        loadSeries.click();
        await waitFor('real HDF depth chart and profile rows', () => document.querySelectorAll('[data-feature="results"] .results-chart-series').length === 1
          && document.querySelectorAll('[data-feature="results"] .results-data-table tbody tr').length > 0, 60000);
        observed.profileRowCount = document.querySelectorAll('[data-feature="results"] .results-data-table tbody tr').length;
        observed.resultVariable = Array.from(document.querySelectorAll('.results-selector-grid select')).find((select) => select.value === 'Cell Depth')?.value ?? null;
        observed.resultsPage = location.hash;
        return { passed: true, hydraulicUi: observed };
      } catch (error) {
        observed.error = error instanceof Error ? error.message : String(error);
        if (mode === 'cancel') {
          try { await closeOpenedProject(); }
          catch (closeError) { observed.closeError = closeError instanceof Error ? closeError.message : String(closeError); }
          return { passed: false, cancellationUi: observed, error: observed.error };
        }
        return { passed: false, hydraulicUi: observed, error: observed.error };
      }
    })()`);
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    if (mode === 'cancel') {
      return {
        passed: false,
        cancellationUi: { ...fallback, error: message },
        error: message,
      };
    }
    return {
      passed: false,
      hydraulicUi: {
        ...fallback,
        error: message,
      },
      error: message,
    };
  }
}

function scriptStringLiteral(value) {
  return JSON.stringify(value)
    .replaceAll('`', '\\u0060')
    .replaceAll('${', '\\u0024{');
}

async function runUiSmoke(window) {
  const result = await window.webContents.executeJavaScript(`(async () => {
    const waitFor = async (label, predicate, timeoutMs = 15000) => {
      const deadline = Date.now() + timeoutMs;
      while (Date.now() < deadline) {
        const value = predicate();
        if (value) return value;
        await new Promise((resolve) => setTimeout(resolve, 30));
      }
      throw new Error('Timed out waiting for ' + label);
    };
    const expected = [
      ['project', '工程'], ['map', '地图'], ['editors', '模型编辑'],
      ['geometry', '几何与网格'], ['results', '结果'], ['tools', '工具'], ['compute', '计算'],
    ];
    try {
      const navigation = await waitFor('workspace navigation', () => document.querySelector('.ras-feature-nav'));
      const buttonFor = (label) => Array.from(navigation.querySelectorAll('button')).find((button) => button.textContent.trim() === label);
      const missing = expected.filter(([, label]) => !buttonFor(label)).map(([, label]) => label);
      if (missing.length) throw new Error('Missing navigation buttons: ' + missing.join(', '));

      const visited = [];
      for (const [id, label] of expected) {
        buttonFor(label).click();
        await waitFor('active navigation item ' + id, () => {
          const active = navigation.querySelector('button[aria-current="page"]');
          return active?.textContent.trim() === label ? active : undefined;
        });
        const page = await waitFor('visible page ' + id, () => Array.from(document.querySelectorAll('.ras-feature-page[data-feature]')).find((element) => element.dataset.feature === id && !element.hidden));
        if (id !== 'compute') {
          await waitFor('page rendering ' + id, () => page.querySelector('.ras-page-error') || !Array.from(page.querySelectorAll('[role="status"]')).some((element) => ['正在加载…', 'Loading…'].includes(element.textContent.trim())));
          const boundary = page.querySelector('.ras-page-error[role="alert"]');
          if (boundary) throw new Error('Render boundary on ' + id + ': ' + boundary.innerText);
        }
        visited.push(id);
      }

      const languageButton = await waitFor('language toggle', () => Array.from(document.querySelectorAll('.ras-titlebar button')).find((button) => ['English', '简体中文'].includes(button.textContent.trim())));
      languageButton.click();
      await waitFor('English locale', () => document.documentElement.lang === 'en');
      await waitFor('English navigation labels', () => buttonFor('Project') && buttonFor('Map'));
      const englishPassed = true;
      const englishToggle = Array.from(document.querySelectorAll('.ras-titlebar button')).find((button) => button.textContent.trim() === '简体中文');
      if (!englishToggle) throw new Error('Language toggle did not offer Simplified Chinese after switching to English.');
      englishToggle.click();
      await waitFor('Chinese locale', () => document.documentElement.lang === 'zh-CN');
      await waitFor('Chinese navigation labels', () => buttonFor('工程') && buttonFor('地图'));

      const remainingErrors = Array.from(document.querySelectorAll('.ras-page-error[role="alert"]')).map((element) => element.innerText);
      if (remainingErrors.length) throw new Error('Visible feature errors: ' + remainingErrors.join(' | '));
      return { passed: true, backendExecuted: false, navigationItems: visited, featurePagesWithoutBoundaryErrors: visited.filter((id) => id !== 'compute'), languageToggle: englishPassed ? 'zh↔en' : 'failed', renderBoundaryErrors: [] };
    } catch (error) {
      return { passed: false, backendExecuted: false, error: error instanceof Error ? error.message : String(error) };
    }
  })()`);
  return result;
}

function finishSmoke(exitCode, result) {
  if (smokeFinished) return;
  smokeFinished = true;
  const pendingStartup = backendStartupPromise;
  if (pendingStartup) backendStartupAbort.abort();
  const type = result?.type ?? (wasmSmokeMode
    ? 'HEC_RAS_WASM_ELECTRON_SMOKE'
    : serverSmokeMode ? 'HEC_RAS_SERVER_ELECTRON_SMOKE' : 'HEC_RAS_UI_ELECTRON_SMOKE');
  void (async () => {
    await pendingStartup?.catch(() => {});
    try {
      await stopServer();
    } catch (error) {
      exitCode = 1;
      result = {
        ...result,
        kind: 'failed',
        passed: false,
        shutdownError: error instanceof Error ? error.message : String(error),
      };
    }
    mainWindow?.destroy();
    if (smokeUserDataDirectory) await rm(smokeUserDataDirectory, { recursive: true, force: true });
    await new Promise((resolve) => process.stdout.write(`${JSON.stringify({ type, ...result })}\n`, resolve));
    app.exit(exitCode);
  })();
}

if (wasmSmokeMode) {
  ipcMain.on('hec:wasm-smoke-result', (event, result) => {
    if (smokeFinished || event.sender !== mainWindow?.webContents) return;
    try {
      if (new URL(event.sender.getURL()).origin !== staticServer?.url.slice(0, -1)) return;
    } catch {
      return;
    }
    finishSmoke(result?.kind === 'completed' ? 0 : 1, result);
  });
  const timeout = setTimeout(() => finishSmoke(2, { kind: 'timeout', message: 'The WebAssembly runtime or solver did not finish within 180 seconds.' }), 180_000);
  timeout.unref();
} else if (uiSmokeMode) {
  const timeout = setTimeout(() => finishSmoke(2, { kind: 'timeout', backendExecuted: false, message: 'The UI smoke did not finish within 30 seconds.' }), 30_000);
  timeout.unref();
} else if (serverSmokeMode) {
  const hasCancellationFixture = process.env.HECRAS_VERIFY_CANCEL_PROJECT !== undefined;
  const hasHydraulicFixture = process.env.HECRAS_VERIFY_PROJECT !== undefined;
  const hasMapFixture = Boolean(process.env.HECRAS_VERIFY_MAP_FIXTURE?.trim());
  const timeoutMs = hasCancellationFixture ? 1_200_000 : hasHydraulicFixture || hasMapFixture ? 480_000 : 45_000;
  const timeout = setTimeout(() => finishSmoke(2, {
    type: 'HEC_RAS_SERVER_ELECTRON_SMOKE',
    kind: 'timeout',
    passed: false,
    backendExecuted: false,
    computeExecuted: false,
    ...(hasHydraulicFixture ? { hydraulicUi: {
      state: 'timed-out', resultPath: null, revision: null, profileRowCount: 0,
      completed: false,
    } } : {}),
    ...(hasCancellationFixture ? { cancellationUi: {
      state: 'timed-out', step: null, resultPath: null, revision: null,
      cancelled: false, cancelStarted: false, computeStarted: false, nextInspectionId: null,
    } } : {}),
    ...(hasMapFixture ? { mapUi: { state: 'timed-out', passed: false, checks: [], computeStarted: false } } : {}),
    message: `The server and UI smoke did not finish within ${timeoutMs / 1000} seconds.`,
  }), timeoutMs);
  timeout.unref();
}

app.whenReady().then(async () => {
  await createMainWindow();
  app.on('activate', () => {
    if (!mainWindow) void createMainWindow();
  });
}).catch((error) => {
  const message = error instanceof Error ? error.stack ?? error.message : String(error);
  if (smokeMode) finishSmoke(1, {
    type: serverSmokeMode ? 'HEC_RAS_SERVER_ELECTRON_SMOKE'
      : wasmSmokeMode ? 'HEC_RAS_WASM_ELECTRON_SMOKE' : 'HEC_RAS_UI_ELECTRON_SMOKE',
    kind: 'startup-error', backendExecuted: false, computeExecuted: false, error: message,
    ...(serverSmokeMode && process.env.HECRAS_VERIFY_PROJECT !== undefined ? { hydraulicUi: {
      state: 'not-started', resultPath: null, revision: null, profileRowCount: 0,
      completed: false, computeStarted: false, error: message,
    } } : {}),
    ...(serverSmokeMode && process.env.HECRAS_VERIFY_CANCEL_PROJECT !== undefined ? { cancellationUi: {
      state: 'not-started', step: null, resultPath: null, revision: null,
      cancelled: false, cancelStarted: false, computeStarted: false, nextInspectionId: null, error: message,
    } } : {}),
    ...(serverSmokeMode && process.env.HECRAS_VERIFY_MAP_FIXTURE?.trim() ? { mapUi: {
      state: 'not-started', passed: false, checks: [], computeStarted: false, error: message,
    } } : {}),
  });
  else {
    process.stderr.write(`${message}\n`);
    void stopServer().catch((stopError) => {
      process.stderr.write(`${stopError instanceof Error ? stopError.message : String(stopError)}\n`);
    }).finally(() => app.exit(1));
  }
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});

app.on('before-quit', (event) => {
  if (runtimeCloseRequested || (!backendConnection && !staticServer && !backendStartupPromise)) return;
  event.preventDefault();
  runtimeCloseRequested = true;
  const pendingStartup = backendStartupPromise;
  if (pendingStartup) backendStartupAbort.abort();
  void (async () => {
    await pendingStartup?.catch(() => {});
    try {
      await stopServer();
    } catch (error) {
      process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`);
    }
    app.quit();
  })();
});
