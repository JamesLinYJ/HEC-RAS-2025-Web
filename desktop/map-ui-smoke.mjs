import assert from 'node:assert/strict';
import { mkdir, mkdtemp, readFile, realpath, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { checkMapUI } from '../scripts/check-map-ui.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const navigation = [
  ['project', '工程'],
  ['map', '地图'],
  ['editors', '模型编辑'],
  ['geometry', '几何与网格'],
  ['results', '结果'],
  ['tools', '工具'],
  ['compute', '计算'],
];

function assertWithin(root, candidate, label) {
  const relative = path.relative(root, candidate);
  assert.ok(relative && relative !== '..' && !relative.startsWith(`..${path.sep}`) && !path.isAbsolute(relative),
    `${label} must be inside ${root}`);
}

async function resolveFixture(fixturePath) {
  assert.ok(path.isAbsolute(fixturePath), 'HECRAS_VERIFY_MAP_FIXTURE must be an absolute path.');
  const manifestPath = await realpath(fixturePath);
  const fixture = JSON.parse(await readFile(manifestPath, 'utf8'));
  if (fixture.schema === 'hecras-geometry-topology-map-http-e2e/v1') {
    assert.equal(typeof fixture.fixtureDirectory, 'string');
    const fixtureRoot = await realpath(fixture.fixtureDirectory);
    for (const key of ['projectPath', 'geometryPath']) {
      assert.equal(typeof fixture[key], 'string', `Geometry map fixture is missing ${key}.`);
      const filePath = await realpath(fixture[key]);
      assertWithin(fixtureRoot, filePath, `Geometry map fixture ${key}`);
    }
    return fixture;
  }
  assert.equal(fixture.schema, 'hecras-plan-compute-e2e/v1', 'Use a ServerChecks map fixture.');
  assert.equal(fixture.cellCount, 16, 'The map acceptance requires the 4 × 4 fixture.');
  assert.equal(typeof fixture.rootPath, 'string');
  const fixtureRoot = await realpath(fixture.rootPath);
  for (const key of ['projectPath', 'planPath', 'geometryPath', 'boundaryPath', 'terrainPath']) {
    assert.equal(typeof fixture[key], 'string', `Map fixture is missing ${key}.`);
    const filePath = await realpath(fixture[key]);
    assertWithin(fixtureRoot, filePath, `Map fixture ${key}`);
  }
  return fixture;
}

/** Runs the shared browser map acceptance against this hidden Electron renderer. */
export async function runElectronMapUISmoke({ window, fixturePath, serverUrl }) {
  const result = { state: 'not-started', passed: false, checks: [] };
  let output;
  let gdbOutput;
  let gdbRasterOutput;
  let debuggerAttachedHere = false;
  let computeStarted = false;
  let detachError;
  const checkReport = { checks: [] };

  try {
    const fixture = await resolveFixture(fixturePath);
    const outputRoot = path.resolve(process.env.HECRAS_VERIFY_OUTPUT ?? path.join(here, '../artifacts/ui-checks'));
    await mkdir(outputRoot, { recursive: true });
    output = await mkdtemp(path.join(outputRoot, 'electron-map-ui-'));
    if (process.env.HECRAS_VERIFY_GDB_SOURCE?.trim()) {
      gdbOutput = await mkdtemp(path.join(output, 'gdb-velocity-'));
    }
    if (process.env.HECRAS_VERIFY_GDB_RASTER_SOURCE?.trim()) {
      gdbRasterOutput = await mkdtemp(path.join(output, 'gdb-raster-'));
    }
    const webContents = window.webContents;
    const evaluate = async (expression) => {
      if (expression.includes('[data-compute-action=run]') && expression.includes('.click()')) computeStarted = true;
      return webContents.executeJavaScript(expression);
    };
    const waitFor = async (expression, label, timeoutMs = 15000) => {
      const deadline = Date.now() + timeoutMs;
      while (Date.now() < deadline) {
        const value = await evaluate(expression);
        if (value) return value;
        await new Promise((resolve) => setTimeout(resolve, 100));
      }
      throw new Error(`Timed out waiting for ${label}`);
    };
    const navigate = async (feature) => {
      const entry = navigation.find(([id]) => id === feature);
      assert.ok(entry, `Unknown workspace feature: ${feature}`);
      const [, label] = entry;
      await waitFor(`Boolean(Array.from(document.querySelectorAll('.ras-feature-nav button')).find(button=>button.textContent.trim()===${JSON.stringify(label)}))`, `${label} navigation`);
      await evaluate(`Array.from(document.querySelectorAll('.ras-feature-nav button')).find(button=>button.textContent.trim()===${JSON.stringify(label)}).click()`);
      await waitFor(`(() => {const page=document.querySelector('[data-feature="${feature}"]');return Boolean(page && !page.hidden && page.getClientRects().length);})()`, `visible ${feature} page`);
    };
    const command = async (method, params = {}) => {
      if (method === 'Page.captureScreenshot') {
        assert.equal(params.format ?? 'png', 'png', 'Electron map screenshots must use PNG.');
        // Keep the smoke page hidden while capturing, matching the owned
        // BrowserWindow's visibility contract throughout the acceptance run.
        const image = await window.capturePage(undefined, { stayHidden: true });
        return { data: image.toPNG().toString('base64') };
      }
      assert.ok(method === 'Input.dispatchMouseEvent' || method === 'Input.dispatchKeyEvent',
        `Unsupported Electron map smoke command: ${method}`);
      const debuggerClient = webContents.debugger;
      if (!debuggerClient.isAttached()) {
        debuggerClient.attach('1.3');
        debuggerAttachedHere = true;
      }
      return await debuggerClient.sendCommand(method, params);
    };
    const setFiles = async (selector, files) => {
      const debuggerClient = webContents.debugger;
      if (!debuggerClient.isAttached()) {
        debuggerClient.attach('1.3');
        debuggerAttachedHere = true;
      }
      await debuggerClient.sendCommand('DOM.enable');
      const { root } = await debuggerClient.sendCommand('DOM.getDocument', { depth: -1, pierce: true });
      const { nodeId } = await debuggerClient.sendCommand('DOM.querySelector', { nodeId: root.nodeId, selector });
      assert.ok(nodeId, `File input not found: ${selector}`);
      await debuggerClient.sendCommand('DOM.setFileInputFiles', { nodeId, files });
    };

    await checkMapUI({ fixture, output, evaluate, waitFor, command, setFiles, navigate, report: checkReport, serverUrl, gdbOutput, gdbRasterOutput });
    result.state = 'completed';
    result.passed = true;
    result.checks = checkReport.checks;
    result.mapSources = checkReport.mapSources;
    result.mapResult = checkReport.mapResult;
    result.resultHover = checkReport.resultHover;
    result.terrain = checkReport.terrain;
    result.geometryTopology = checkReport.geometryTopology;
    result.geometryTrace = checkReport.geometryTrace;
    result.modelCopies = checkReport.modelCopies;
    result.savedMapLayout = checkReport.savedMapLayout;
    result.gdbVelocity = checkReport.gdbVelocity;
    result.gdbOutput = gdbOutput;
    result.gdbRaster = checkReport.gdbRaster;
    result.gdbRasterOutput = gdbRasterOutput;
  } catch (error) {
    result.state = 'failed';
    result.checks = checkReport.checks;
    result.mapSources = checkReport.mapSources;
    result.mapResult = checkReport.mapResult;
    result.resultHover = checkReport.resultHover;
    result.terrain = checkReport.terrain;
    result.geometryTopology = checkReport.geometryTopology;
    result.geometryTrace = checkReport.geometryTrace;
    result.modelCopies = checkReport.modelCopies;
    result.savedMapLayout = checkReport.savedMapLayout;
    result.gdbVelocity = checkReport.gdbVelocity;
    result.gdbOutput = gdbOutput;
    result.gdbRaster = checkReport.gdbRaster;
    result.gdbRasterOutput = gdbRasterOutput;
    result.error = error instanceof Error ? error.message : String(error);
    result.stack = error instanceof Error ? error.stack : undefined;
  } finally {
    if (debuggerAttachedHere && window.webContents.debugger.isAttached()) {
      try {
        window.webContents.debugger.detach();
      } catch (error) {
        detachError = error instanceof Error ? error.message : String(error);
      }
    }
  }

  if (detachError) {
    result.passed = false;
    result.state = 'failed';
    result.detachError = detachError;
  }
  result.computeStarted = computeStarted;
  if (output) {
    result.output = output;
    try {
      await writeFile(path.join(output, 'electron-map-ui-report.json'), `${JSON.stringify(result, null, 2)}\n`);
    } catch (error) {
      result.passed = false;
      result.state = 'failed';
      result.reportWriteError = error instanceof Error ? error.message : String(error);
    }
  }
  return result;
}
