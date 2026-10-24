import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { spawn } from 'node:child_process';
import { access, mkdir, mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { startStaticServer } from '../desktop/static-server.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const dist = path.join(root, 'dist');
const outputDirectory = path.resolve(process.env.HECRAS_VERIFY_OUTPUT ?? path.join(root, 'artifacts/wasm-checks'));
const chromePath = process.env.HECRAS_CHROME ?? '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
const baselinePath = process.env.HECRAS_VERIFY_NATIVE_BASELINE;
const defaultRequest = {
  terrainElevation: 42,
  initialStage: 43,
  roughness: 0.035,
  precipitationRate: 0.000001,
  timestepSeconds: 0.1,
  durationSeconds: 4,
};
const timeoutMs = Number(process.env.HECRAS_VERIFY_TIMEOUT_MS ?? 120_000);

let browser;
let browserProfile;
let server;
let socket;
let pageSession;
let startupTimer;
let nextCommandId = 0;
const pendingCommands = new Map();
const pageExceptions = [];
const failedResponses = [];
const receipt = {
  schemaVersion: 1,
  state: 'running',
  startedAt: new Date().toISOString(),
  browser: { name: 'Chrome', executable: chromePath, headless: true },
  runtime: null,
  kernel: { Explicit: 'SolverExpSWE', Implicit: 'SolverImpSWE' },
  runs: [],
  cancellation: null,
  validationRecovery: null,
  nativeBaseline: { state: baselinePath ? 'pending' : 'not-supplied' },
  limitations: [
    'This acceptance covers the in-memory one-cell SWE CPU fixture reported by the C# WebAssembly runtime.',
    'It does not establish HDF5 project input, project file write-back, or full HEC-RAS project compute.',
  ],
  browserErrors: pageExceptions,
  failedHttpResponses: failedResponses,
};

const delay = (milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds));

function cdpCommand(method, params = {}, session = pageSession) {
  const id = ++nextCommandId;
  const packet = { id, method, params };
  if (session) packet.sessionId = session;
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      pendingCommands.delete(id);
      reject(new Error(`CDP command timed out: ${method}`));
    }, 20_000);
    pendingCommands.set(id, { resolve, reject, timer, method });
    socket.send(JSON.stringify(packet));
  });
}

async function evaluate(expression) {
  const response = await cdpCommand('Runtime.evaluate', {
    expression,
    returnByValue: true,
    awaitPromise: true,
    userGesture: true,
  });
  if (response.exceptionDetails) {
    throw new Error(response.exceptionDetails.exception?.description ?? response.exceptionDetails.text);
  }
  return response.result?.value;
}

async function waitFor(expression, description, limit = timeoutMs) {
  const deadline = Date.now() + limit;
  let lastValue;
  while (Date.now() < deadline) {
    lastValue = await evaluate(expression);
    if (lastValue) return lastValue;
    await delay(40);
  }
  throw new Error(`Timed out waiting for ${description}; last value: ${JSON.stringify(lastValue)}`);
}

async function setComputeField(field, value) {
  const serializedField = JSON.stringify(field);
  const serializedValue = JSON.stringify(String(value));
  const didSet = await evaluate(`(() => {
    const input = document.querySelector('[data-compute-field="' + ${serializedField} + '"]');
    if (!input) return false;
    const prototype = input instanceof HTMLSelectElement ? HTMLSelectElement.prototype : HTMLInputElement.prototype;
    const setter = Object.getOwnPropertyDescriptor(prototype, 'value')?.set;
    if (!setter) return false;
    setter.call(input, ${serializedValue});
    input.dispatchEvent(new Event(input instanceof HTMLSelectElement ? 'change' : 'input', { bubbles: true }));
    if (!(input instanceof HTMLSelectElement)) input.dispatchEvent(new Event('change', { bubbles: true }));
    return true;
  })()`);
  assert.equal(didSet, true, `Compute field not found or not writable: ${field}`);
  await waitFor(
    `document.querySelector('[data-compute-field="' + ${serializedField} + '"]')?.value === ${serializedValue}`,
    `compute field ${field}=${String(value)}`,
    10_000,
  );
}

async function setRequest(timeStepping, request) {
  await setComputeField('timeStepping', timeStepping);
  for (const field of Object.keys(defaultRequest)) await setComputeField(field, request[field]);
}

async function installProgressObserver() {
  const installed = await evaluate(`(() => {
    const progress = document.querySelector('[data-compute-progress]');
    if (!progress) return false;
    const samples = [];
    const capture = () => {
      const current = document.querySelector('[data-compute-progress]');
      if (!current) return;
      const raw = document.querySelector('.progress-code')?.textContent?.trim() ?? '';
      let backendMessage = null;
      let backendProgress = null;
      try {
        const payload = JSON.parse(raw);
        if (typeof payload.progress === 'number') backendProgress = payload.progress;
        if (typeof payload.message === 'string') backendMessage = payload.message;
      } catch { /* The aria value is the page's real backend progress value. */ }
      samples.push({
        observedAtMs: performance.now(),
        ariaValueNow: Number(current.getAttribute('aria-valuenow')),
        backendProgress,
        backendMessage,
        raw,
      });
    };
    const observer = new MutationObserver(capture);
    observer.observe(progress, { attributes: true, attributeFilter: ['aria-valuenow'], childList: true, characterData: true, subtree: true });
    globalThis.__hecrasWasmAcceptance = { samples, observer };
    capture();
    return true;
  })()`);
  assert.equal(installed, true, 'Production compute page did not expose data-compute-progress.');
}

async function readProgressSamples(startIndex = 0) {
  return await evaluate(`globalThis.__hecrasWasmAcceptance?.samples?.slice(${startIndex}) ?? []`);
}

async function readCurrentOutput() {
  const raw = await evaluate(`document.querySelector('[data-compute-output]')?.textContent?.trim() ?? ''`);
  if (!raw) return undefined;
  try { return JSON.parse(raw); } catch { return undefined; }
}

async function waitForOutputStatus(status, solverType, timeout = timeoutMs) {
  await waitFor(`(() => {
    const output = document.querySelector('[data-compute-output]');
    if (!output?.textContent?.trim()) return false;
    try {
      const result = JSON.parse(output.textContent);
      return (result.status === ${JSON.stringify(status)} || result.status === 'failed')
        && (!${JSON.stringify(solverType)} || result.solverType === ${JSON.stringify(solverType)});
    } catch { return false; }
  })()`, `C# WASM output status=${status}${solverType ? ` solver=${solverType}` : ''}`, timeout);
  const result = await readCurrentOutput();
  assert.ok(result, 'The production page returned an output element that was not valid JSON.');
  assert.equal(result.status, status, `C# ${solverType ?? 'solver'} returned ${result.status}: ${result.error ?? ''}`);
  return result;
}

function assertNumericArray(value, name) {
  assert.ok(Array.isArray(value) && value.length > 0, `${name} must be a nonempty numeric array.`);
  assert.ok(value.every((item) => typeof item === 'number' && Number.isFinite(item)), `${name} contains a non-finite or nonnumeric value.`);
}

function assertCompletedResult(result, solverType) {
  assert.equal(result.status, 'completed', `C# solver returned ${result.status}: ${result.error ?? ''}`);
  assert.equal(result.solverType, solverType);
  assert.ok(Number.isFinite(result.steps) && result.steps > 0, 'The solver did not report completed time steps.');
  assert.ok(Number.isFinite(result.finalTimeSeconds) && result.finalTimeSeconds > 0, 'The solver did not report a positive final time.');
  assert.ok(Number.isInteger(result.wetCellCount) && result.wetCellCount >= 0, 'The solver did not report a valid wet-cell count.');
  assertNumericArray(result.cellWaterSurface, 'cellWaterSurface');
  assertNumericArray(result.cellDepth, 'cellDepth');
  assertNumericArray(result.faceVelocity, 'faceVelocity');
}

function baselineMember(object, camelName, pascalName = camelName[0].toUpperCase() + camelName.slice(1)) {
  return object?.[camelName] ?? object?.[pascalName];
}

function baselineRequest(entry, solver) {
  const input = entry?.request ?? entry?.input ?? {};
  const request = { ...defaultRequest };
  for (const field of Object.keys(defaultRequest)) {
    const candidate = baselineMember(input, field);
    if (candidate !== undefined) request[field] = Number(candidate);
  }
  const timeStepping = baselineMember(input, 'timeStepping') ?? solver;
  assert.equal(timeStepping, solver, `Native baseline ${solver} request has a different timeStepping value.`);
  assert.equal(baselineMember(input, 'equationSet') ?? 'SWE', 'SWE', `Native baseline ${solver} request is not SWE.`);
  for (const [field, value] of Object.entries(request)) assert.ok(Number.isFinite(value), `Native baseline ${solver} has invalid ${field}.`);
  assert.ok(request.terrainElevation >= -10_000 && request.terrainElevation <= 10_000);
  assert.ok(request.initialStage > request.terrainElevation && request.initialStage <= 10_000);
  assert.ok(request.initialStage >= -10_000);
  assert.ok(request.roughness >= 0.005 && request.roughness <= 0.2);
  assert.ok(request.precipitationRate >= 0 && request.precipitationRate <= 0.01);
  assert.ok(request.timestepSeconds >= 0.01 && request.timestepSeconds <= 1);
  assert.ok(request.durationSeconds >= request.timestepSeconds && request.durationSeconds <= 60);
  assert.ok(request.durationSeconds / request.timestepSeconds <= 6000);
  return request;
}

function compareNumber(actual, expected, absoluteTolerance, relativeTolerance, pathLabel) {
  assert.ok(typeof actual === 'number' && Number.isFinite(actual), `${pathLabel}: actual value is not finite.`);
  assert.ok(typeof expected === 'number' && Number.isFinite(expected), `${pathLabel}: baseline value is not finite.`);
  const absoluteError = Math.abs(actual - expected);
  const allowedError = absoluteTolerance + relativeTolerance * Math.abs(expected);
  assert.ok(absoluteError <= allowedError, `${pathLabel}: ${actual} differs from baseline ${expected} (error ${absoluteError}, allowed ${allowedError}).`);
  return absoluteError;
}

function compareNativeBaseline(result, entry, globalBaseline, solver) {
  const expected = entry.output ?? entry.result;
  assert.ok(expected && typeof expected === 'object', `Native baseline ${solver} must contain an output or result object.`);
  assert.equal(baselineMember(expected, 'status') ?? 'completed', 'completed', `Native baseline ${solver} is not a completed run.`);
  const expectedKernel = baselineMember(expected, 'solverType') ?? (solver === 'Explicit' ? 'SolverExpSWE' : 'SolverImpSWE');
  assert.equal(result.solverType, expectedKernel, `Native baseline ${solver} kernel differs.`);
  const absoluteTolerance = Number(entry.absoluteTolerance ?? globalBaseline.absoluteTolerance ?? 1e-8);
  const relativeTolerance = Number(entry.relativeTolerance ?? globalBaseline.relativeTolerance ?? 1e-6);
  assert.ok(Number.isFinite(absoluteTolerance) && absoluteTolerance >= 0, 'Native baseline absoluteTolerance is invalid.');
  assert.ok(Number.isFinite(relativeTolerance) && relativeTolerance >= 0, 'Native baseline relativeTolerance is invalid.');
  const expectedSteps = baselineMember(expected, 'steps');
  if (expectedSteps !== undefined) assert.equal(result.steps, expectedSteps, `Native baseline ${solver} step count differs.`);
  const expectedTime = baselineMember(expected, 'finalTimeSeconds');
  if (expectedTime !== undefined) compareNumber(result.finalTimeSeconds, expectedTime, absoluteTolerance, relativeTolerance, `${solver}.finalTimeSeconds`);
  const expectedWetCells = baselineMember(expected, 'wetCellCount');
  if (expectedWetCells !== undefined) assert.equal(result.wetCellCount, expectedWetCells, `Native baseline ${solver} wet cell count differs.`);
  const values = expected.values ?? expected;
  const errors = {};
  for (const field of ['cellWaterSurface', 'cellDepth', 'faceVelocity']) {
    const expectedArray = baselineMember(values, field);
    assertNumericArray(expectedArray, `native ${solver} ${field}`);
    assert.equal(result[field].length, expectedArray.length, `Native baseline ${solver} ${field} array length differs.`);
    errors[field] = result[field].map((value, index) => compareNumber(value, expectedArray[index], absoluteTolerance, relativeTolerance, `${solver}.${field}[${index}]`));
  }
  return { absoluteTolerance, relativeTolerance, maxAbsoluteErrors: Object.fromEntries(Object.entries(errors).map(([name, values]) => [name, Math.max(...values)])) };
}

async function clickRunAndWait(status, solverType, expectedRequest) {
  const runButton = await evaluate(`document.querySelector('[data-compute-action="run"]') !== null`);
  assert.equal(runButton, true, 'Production compute page did not expose data-compute-action=run.');
  const buttonEnabled = await evaluate(`!document.querySelector('[data-compute-action="run"]').disabled`);
  assert.equal(buttonEnabled, true, 'The valid compute request is still disabled.');
  const samplesBefore = await evaluate('globalThis.__hecrasWasmAcceptance?.samples?.length ?? 0');
  const startedAt = Date.now();
  await evaluate(`document.querySelector('[data-compute-action="run"]').click()`);
  await waitFor(`(() => {
    const output = document.querySelector('[data-compute-output]');
    return !output?.textContent?.trim();
  })()`, 'previous result clearing before a new request', 10_000);
  const result = await waitForOutputStatus(status, solverType);
  if (status === 'completed') assertCompletedResult(result, solverType);
  if (expectedRequest && Number.isFinite(result.finalTimeSeconds)) {
    assert.ok(result.finalTimeSeconds <= expectedRequest.durationSeconds + Math.max(1e-8, expectedRequest.timestepSeconds), 'Result time exceeds the request duration.');
  }
  return {
    result,
    elapsedWallMs: Date.now() - startedAt,
    progressSamples: await readProgressSamples(samplesBefore),
  };
}

async function triggerImmediateCancel(timeStepping) {
  const expectedSolverType = timeStepping === 'Implicit' ? 'SolverImpSWE' : 'SolverExpSWE';
  const cancellationSetup = await evaluate(`(() => {
    const run = document.querySelector('[data-compute-action="run"]');
    if (!run || run.disabled) return null;
    const record = {
      state: 'starting',
      requestedAtMs: performance.now(),
      runReturnedAtMs: null,
      clickedAtMs: null,
      attempts: 0,
      cancelClickedInSameEvaluation: false,
    };
    globalThis.__hecrasCancelAttempt = record;
    run.click();
    record.runReturnedAtMs = performance.now();
    const cancel = document.querySelector('[data-compute-action="cancel"]');
    if (!cancel || cancel.disabled) {
      record.state = 'cancel-button-not-rendered-synchronously';
      return record;
    }
    record.state = 'clicked';
    record.cancelClickedInSameEvaluation = true;
    record.clickedAtMs = performance.now();
    cancel.click();
    return record;
  })()`);
  assert.ok(cancellationSetup, 'Unable to start the cancellation test from the real compute UI.');
  assert.equal(cancellationSetup.state, 'clicked', 'Production Cancel was not rendered synchronously after Run; same-evaluation cooperative cancellation could not be tested.');
  assert.equal(cancellationSetup.cancelClickedInSameEvaluation, true, 'Run and Cancel did not occur in the same browser evaluation.');
  await waitFor(`(() => {
    const output = document.querySelector('[data-compute-output]');
    if (!output?.textContent?.trim()) return false;
    try {
      const result = JSON.parse(output.textContent);
      return result.status === 'cancelled' && result.solverType === ${JSON.stringify(expectedSolverType)};
    } catch { return false; }
  })()`, `engine-confirmed ${timeStepping} cancellation`, 30_000);
  const result = await readCurrentOutput();
  assert.ok(result, 'Cancellation result was not valid JSON.');
  assert.equal(result.status, 'cancelled', 'Cancellation was not confirmed by the C# engine.');
  assert.equal(result.solverType, expectedSolverType, `Cancellation response did not identify the ${timeStepping} CPU solver.`);
  const attempt = cancellationSetup;
  return {
    status: result.status,
    solverType: result.solverType,
    steps: result.steps,
    finalTimeSeconds: result.finalTimeSeconds,
    immediateCancelDelayMs: attempt.clickedAtMs - attempt.requestedAtMs,
    runToCancelDelayMs: attempt.clickedAtMs - attempt.runReturnedAtMs,
    cancelClickedInSameEvaluation: attempt.cancelClickedInSameEvaluation,
    engineConfirmed: true,
  };
}

async function readBaseline() {
  if (!baselinePath) return undefined;
  const raw = await readFile(path.resolve(baselinePath), 'utf8');
  const value = JSON.parse(raw);
  assert.ok(value && typeof value === 'object' && value.runs && typeof value.runs === 'object', 'Baseline JSON must contain a runs object keyed by Explicit and/or Implicit.');
  const keys = Object.keys(value.runs);
  assert.ok(keys.length > 0, 'Native baseline runs must contain at least one Explicit or Implicit entry.');
  for (const key of keys) {
    assert.ok(['Explicit', 'Implicit'].includes(key), `Unsupported baseline kernel: ${key}`);
    assert.ok(value.runs[key] && typeof value.runs[key] === 'object', `Native baseline ${key} entry must be an object.`);
  }
  return value;
}

async function configureChrome() {
  browserProfile = await mkdtemp(path.join(tmpdir(), 'hecras-wasm-chrome-'));
  await access(chromePath);
  browser = spawn(chromePath, [
    '--headless=new',
    '--no-first-run',
    '--no-default-browser-check',
    '--disable-background-networking',
    '--disable-component-update',
    '--disable-sync',
    '--disable-extensions',
    '--remote-debugging-port=0',
    `--user-data-dir=${browserProfile}`,
    'about:blank',
  ], { stdio: ['ignore', 'ignore', 'pipe'] });

  const browserWsUrl = await new Promise((resolve, reject) => {
    let stderr = '';
    startupTimer = setTimeout(() => reject(new Error('Chrome did not expose a headless DevTools endpoint.')), 30_000);
    browser.once('error', reject);
    browser.once('exit', (code) => reject(new Error(`Chrome exited with code ${code}: ${stderr.slice(-1800)}`)));
    browser.stderr.on('data', (chunk) => {
      stderr += chunk.toString();
      const match = stderr.match(/DevTools listening on (ws:\/\/\S+)/);
      if (match) { clearTimeout(startupTimer); resolve(match[1]); }
    });
  });

  socket = new WebSocket(browserWsUrl);
  await new Promise((resolve, reject) => {
    socket.addEventListener('open', resolve, { once: true });
    socket.addEventListener('error', reject, { once: true });
  });
  socket.addEventListener('message', (event) => {
    const packet = JSON.parse(String(event.data));
    if (packet.id) {
      const waiter = pendingCommands.get(packet.id);
      if (!waiter) return;
      pendingCommands.delete(packet.id);
      clearTimeout(waiter.timer);
      if (packet.error) waiter.reject(new Error(`${waiter.method}: ${JSON.stringify(packet.error)}`));
      else waiter.resolve(packet.result ?? {});
      return;
    }
    if (packet.sessionId === pageSession && packet.method === 'Runtime.exceptionThrown') {
      const details = packet.params.exceptionDetails;
      pageExceptions.push(details.exception?.description ?? details.text ?? 'Unknown browser JavaScript exception');
    }
    if (packet.sessionId === pageSession && packet.method === 'Network.responseReceived' && packet.params.response.status >= 400) {
      failedResponses.push({ status: packet.params.response.status, url: packet.params.response.url });
    }
  });

  const target = await cdpCommand('Target.createTarget', { url: 'about:blank' }, null);
  const attached = await cdpCommand('Target.attachToTarget', { targetId: target.targetId, flatten: true }, null);
  pageSession = attached.sessionId;
  await cdpCommand('Runtime.enable');
  await cdpCommand('Page.enable');
  await cdpCommand('Network.enable');
  await cdpCommand('Emulation.setDeviceMetricsOverride', { width: 1440, height: 1050, deviceScaleFactor: 1, mobile: false });
}

async function verifyPackagedRuntimeAssets() {
  const framework = path.join(dist, '_framework');
  const files = await readdir(framework);
  assert.ok(files.includes('dotnet.js'), 'Published .NET runtime is missing dist/_framework/dotnet.js.');
  const runtimeResponse = await fetch(new URL('_framework/dotnet.js', server.url));
  assert.equal(runtimeResponse.status, 200, 'The static server could not read the published .NET runtime.');
  assert.match(runtimeResponse.headers.get('content-type') ?? '', /javascript/);
  const runtimeJavaScript = await runtimeResponse.text();
  const manifestStart = runtimeJavaScript.indexOf('/*json-start*/');
  const manifestEnd = runtimeJavaScript.indexOf('/*json-end*/', manifestStart + '/*json-start*/'.length);
  assert.ok(manifestStart >= 0 && manifestEnd > manifestStart, 'dotnet.js does not contain the expected embedded .NET boot manifest.');
  const bootManifest = JSON.parse(runtimeJavaScript.slice(manifestStart + '/*json-start*/'.length, manifestEnd));
  const mainAssemblyName = bootManifest.mainAssemblyName;
  assert.equal(mainAssemblyName, 'Ras.Wasm', 'The .NET boot manifest does not identify Ras.Wasm as its main assembly.');

  const verifyManifestAsset = async (asset, description) => {
    assert.ok(asset && typeof asset.name === 'string' && typeof asset.hash === 'string', `${description} is missing a physical name or SHA-256 hash in the boot manifest.`);
    assert.equal(path.posix.basename(asset.name), asset.name, `${description} physical name must stay within _framework.`);
    assert.ok(files.includes(asset.name), `${description} physical asset is missing from dist/_framework: ${asset.name}`);
    const localBytes = await readFile(path.join(framework, asset.name));
    assert.ok(localBytes.length > 0, `${description} physical asset is empty: ${asset.name}`);
    const localHash = `sha256-${createHash('sha256').update(localBytes).digest('base64')}`;
    assert.equal(localHash, asset.hash, `${description} physical asset does not match its boot manifest SHA-256: ${asset.name}`);

    const response = await fetch(new URL(`_framework/${encodeURIComponent(asset.name)}`, server.url));
    assert.equal(response.status, 200, `The static server could not fetch ${description} physical asset ${asset.name}.`);
    const contentType = response.headers.get('content-type') ?? '';
    const servedBytes = Buffer.from(await response.arrayBuffer());
    assert.ok(servedBytes.length > 0, `${description} HTTP response body is empty: ${asset.name}`);
    assert.equal(servedBytes.length, localBytes.length, `${description} HTTP response length differs from the published file: ${asset.name}`);
    const servedHash = `sha256-${createHash('sha256').update(servedBytes).digest('base64')}`;
    assert.equal(servedHash, asset.hash, `${description} HTTP response does not match its boot manifest SHA-256: ${asset.name}`);
    return { virtualPath: asset.virtualPath, physicalName: asset.name, bytes: servedBytes.length, sha256: servedHash, httpStatus: response.status, contentType };
  };

  const assemblyAssets = bootManifest.resources?.assembly;
  assert.ok(Array.isArray(assemblyAssets), 'The .NET boot manifest has no managed assembly resource table.');
  const appAssemblies = assemblyAssets.filter((asset) => {
    const virtualStem = path.posix.basename(asset.virtualPath ?? '').replace(/\.(?:dll|wasm)$/i, '');
    const physicalStem = path.posix.basename(asset.name ?? '').replace(/\.[^.]+$/, '').replace(/\.[a-z0-9]+$/i, '');
    return virtualStem === mainAssemblyName && physicalStem === mainAssemblyName;
  });
  assert.equal(appAssemblies.length, 1, `Expected exactly one boot-manifest mapping for main assembly ${mainAssemblyName}, found ${appAssemblies.length}.`);
  const appAssembly = appAssemblies[0];
  assert.match(appAssembly.virtualPath, /^Ras\.Wasm\.(?:dll|wasm)$/i, 'The main assembly virtual path is not a recognized managed assembly name.');
  assert.match(appAssembly.name, /^Ras\.Wasm\.[a-z0-9]+\.(?:dll|wasm)$/i, 'The main assembly physical filename is not a hashed DLL or Webcil asset.');
  const appAssemblyAsset = await verifyManifestAsset(appAssembly, `main assembly ${mainAssemblyName}`);
  const assemblyExtension = path.extname(appAssembly.name).toLowerCase();
  assert.ok(['.dll', '.wasm'].includes(assemblyExtension), 'The main assembly physical asset is neither a DLL nor a Webcil WASM file.');

  const wasmNativeAssets = bootManifest.resources?.wasmNative;
  assert.ok(Array.isArray(wasmNativeAssets) && wasmNativeAssets.length > 0, 'The .NET boot manifest has no native WebAssembly runtime asset.');
  const nativeRuntimeAssets = [];
  for (const nativeAsset of wasmNativeAssets) {
    assert.match(nativeAsset.name ?? '', /^dotnet\.native\.[a-z0-9]+\.wasm$/i, 'The boot manifest native runtime asset has an unexpected physical name.');
    nativeRuntimeAssets.push(await verifyManifestAsset(nativeAsset, '.NET native WebAssembly runtime'));
  }

  return {
    frameworkFileCount: files.length,
    wasmAssets: files.filter((file) => file.endsWith('.wasm')),
    bootManifest: { mainAssemblyName, appAssemblyVirtualPath: appAssembly.virtualPath },
    appAssembly: { ...appAssemblyAsset, format: assemblyExtension === '.wasm' ? 'webcil-wasm' : 'managed-dll' },
    nativeRuntimeAssets,
  };
}

async function main() {
  await mkdir(outputDirectory, { recursive: true });
  const baseline = await readBaseline();
  server = await startStaticServer(dist);
  const rootResponse = await fetch(server.url);
  assert.equal(rootResponse.status, 200);
  assert.equal(rootResponse.headers.get('cross-origin-opener-policy'), 'same-origin');
  assert.equal(rootResponse.headers.get('cross-origin-embedder-policy'), 'require-corp');
  receipt.staticServer = { url: server.url, coop: rootResponse.headers.get('cross-origin-opener-policy'), coep: rootResponse.headers.get('cross-origin-embedder-policy') };
  receipt.runtimeAssets = await verifyPackagedRuntimeAssets();

  await configureChrome();
  await cdpCommand('Page.navigate', { url: server.url });
  await waitFor("document.querySelectorAll('.ras-feature-nav button').length >= 7", 'production React workspace', 60_000);
  assert.equal(await evaluate('globalThis.crossOriginIsolated'), true, 'Chrome did not enter cross-origin-isolated mode; SharedArrayBuffer cancellation cannot be verified.');
  assert.equal(await evaluate("typeof SharedArrayBuffer !== 'undefined'"), true, 'SharedArrayBuffer is not available in this Chrome context.');

  await evaluate(`(() => {
    const button = Array.from(document.querySelectorAll('.ras-feature-nav button')).find((node) => /^(计算|Compute)$/.test(node.textContent.trim()));
    if (!button) throw new Error('Compute navigation button was not found.');
    button.click();
  })()`);
  await waitFor(`(() => {
    const page = document.querySelector('[data-feature="compute"]');
    const fields = ['timeStepping','terrainElevation','initialStage','roughness','precipitationRate','timestepSeconds','durationSeconds'];
    return page && !page.hidden && fields.every((field) => page.querySelector('[data-compute-field="' + field + '"]'));
  })()`, 'production compute form and stable field selectors', 60_000);

  receipt.runtime = await waitFor(`(() => {
    const node = document.querySelector('[data-compute-runtime]') || document.querySelector('.runtime-code');
    if (!node?.textContent?.trim()) return false;
    try { return JSON.parse(node.textContent); } catch { return false; }
  })()`, 'actual GetRuntimeInfo result from dotnet-browser-wasm', 90_000);
  assert.equal(receipt.runtime.runtime, 'dotnet-browser-wasm');
  assert.match(receipt.runtime.solver, /Ras\.Engine CPU solver/i);
  assert.ok(receipt.runtime.solverTypes?.includes('SolverExpSWE'));
  assert.ok(receipt.runtime.solverTypes?.includes('SolverImpSWE'));
  assert.equal(receipt.runtime.cancellation, true);
  receipt.engineScope = {
    fixture: receipt.runtime.fixture,
    computePath: receipt.runtime.computePath,
    projectFileCompute: receipt.runtime.projectFileCompute,
    hdf5ProjectInput: receipt.runtime.hdf5ProjectInput,
    cancellation: receipt.runtime.cancellation,
  };
  await installProgressObserver();

  const explicitBaseline = baseline?.runs?.Explicit;
  const explicitRequest = explicitBaseline ? baselineRequest(explicitBaseline, 'Explicit') : { ...defaultRequest };
  await setRequest('Explicit', explicitRequest);
  await setComputeField('roughness', 'not-a-number');
  const validationFailure = await waitFor(`(() => {
    const input = document.querySelector('[data-compute-field="roughness"]');
    const run = document.querySelector('[data-compute-action="run"]');
    return input?.getAttribute('aria-invalid') === 'true' && run?.disabled;
  })()`, 'invalid numeric input blocked by production validation', 10_000);
  assert.ok(validationFailure);
  const validationMessage = await evaluate(`document.querySelector('[data-compute-field="roughness"]')?.parentElement?.innerText ?? ''`);
  assert.match(validationMessage, /有限数值|finite numeric/i, 'Invalid value did not show the expected validation issue.');
  receipt.validationRecovery = {
    state: 'rejected-by-production-client-validation',
    field: 'roughness',
    rejectedValue: 'not-a-number',
    runButtonDisabled: true,
    backendInvokedForRejectedValue: false,
  };
  await setComputeField('roughness', String(explicitRequest.roughness));
  await waitFor("!document.querySelector('[data-compute-action=run]').disabled", 'corrected request re-enabled after client validation failure', 10_000);

  const explicit = await clickRunAndWait('completed', 'SolverExpSWE', explicitRequest);
  receipt.runs.push({
    timeStepping: 'Explicit', kernel: explicit.result.solverType,
    request: { equationSet: 'SWE', timeStepping: 'Explicit', ...explicitRequest },
    elapsedWallMs: explicit.elapsedWallMs,
    simulationTimeSeconds: explicit.result.finalTimeSeconds,
    steps: explicit.result.steps,
    wetCellCount: explicit.result.wetCellCount,
    values: {
      cellWaterSurface: explicit.result.cellWaterSurface,
      cellDepth: explicit.result.cellDepth,
      faceVelocity: explicit.result.faceVelocity,
    },
    progressSamples: explicit.progressSamples,
  });
  assert.ok(explicit.progressSamples.some((sample) => sample.backendProgress !== null || Number(sample.ariaValueNow) > 0), 'No real C# progress update was observed for the explicit solve.');
  const explicitProgress = await evaluate(`document.querySelector('.progress-code')?.textContent?.trim() ?? ''`);
  let explicitLatestProgress;
  try { explicitLatestProgress = JSON.parse(explicitProgress); } catch { explicitLatestProgress = undefined; }
  assert.ok(explicitLatestProgress && typeof explicitLatestProgress.progress === 'number', 'Progress panel does not show a real backend progress payload.');
  assert.equal(explicitLatestProgress.progress, 100, 'The explicit C# solver did not report its completed progress value.');
  receipt.runs.at(-1).latestProgress = explicitLatestProgress;
  if (explicitBaseline) {
    receipt.nativeBaseline.Explicit = compareNativeBaseline(explicit.result, explicitBaseline, baseline, 'Explicit');
  }

  const implicitBaseline = baseline?.runs?.Implicit;
  const implicitRequest = implicitBaseline ? baselineRequest(implicitBaseline, 'Implicit') : { ...defaultRequest };
  await setRequest('Implicit', implicitRequest);
  const implicit = await clickRunAndWait('completed', 'SolverImpSWE', implicitRequest);
  receipt.runs.push({
    timeStepping: 'Implicit', kernel: implicit.result.solverType,
    request: { equationSet: 'SWE', timeStepping: 'Implicit', ...implicitRequest },
    elapsedWallMs: implicit.elapsedWallMs,
    simulationTimeSeconds: implicit.result.finalTimeSeconds,
    steps: implicit.result.steps,
    wetCellCount: implicit.result.wetCellCount,
    values: {
      cellWaterSurface: implicit.result.cellWaterSurface,
      cellDepth: implicit.result.cellDepth,
      faceVelocity: implicit.result.faceVelocity,
    },
    progressSamples: implicit.progressSamples,
  });
  assert.ok(implicit.progressSamples.some((sample) => sample.backendProgress !== null || Number(sample.ariaValueNow) > 0), 'No real C# progress update was observed for the implicit solve.');
  const implicitProgressRaw = await evaluate(`document.querySelector('.progress-code')?.textContent?.trim() ?? ''`);
  let implicitLatestProgress;
  try { implicitLatestProgress = JSON.parse(implicitProgressRaw); } catch { implicitLatestProgress = undefined; }
  assert.ok(implicitLatestProgress && typeof implicitLatestProgress.progress === 'number', 'Progress panel does not show a real backend progress payload for the implicit solve.');
  assert.equal(implicitLatestProgress.progress, 100, 'The implicit C# solver did not report its completed progress value.');
  receipt.runs.at(-1).latestProgress = implicitLatestProgress;
  if (implicitBaseline) receipt.nativeBaseline.Implicit = compareNativeBaseline(implicit.result, implicitBaseline, baseline, 'Implicit');
  if (baseline) {
    receipt.nativeBaseline.state = Object.keys(baseline.runs).length === 2 ? 'compared' : 'partially-compared';
    receipt.nativeBaseline.path = path.resolve(baselinePath);
  }

  const cancellationRequest = { ...defaultRequest, timestepSeconds: 0.01, durationSeconds: 60 };
  receipt.cancellations = [];
  receipt.retryAfterCancellation = [];
  for (const [timeStepping, solverType, retryRequest] of [
    ['Explicit', 'SolverExpSWE', explicitRequest],
    ['Implicit', 'SolverImpSWE', implicitRequest],
  ]) {
    await setRequest(timeStepping, cancellationRequest);
    const cancelStart = Date.now();
    const cancellation = await triggerImmediateCancel(timeStepping);
    cancellation.elapsedWallMs = Date.now() - cancelStart;
    cancellation.request = { equationSet: 'SWE', timeStepping, ...cancellationRequest };
    assert.ok(cancellation.immediateCancelDelayMs >= 0 && cancellation.immediateCancelDelayMs < 1000, `${timeStepping} cancel was not issued promptly after Run.`);
    receipt.cancellations.push(cancellation);

    await setRequest(timeStepping, retryRequest);
    const retry = await clickRunAndWait('completed', solverType, retryRequest);
    receipt.retryAfterCancellation.push({
      timeStepping,
      status: retry.result.status,
      kernel: retry.result.solverType,
      elapsedWallMs: retry.elapsedWallMs,
      simulationTimeSeconds: retry.result.finalTimeSeconds,
      steps: retry.result.steps,
      values: {
        cellWaterSurface: retry.result.cellWaterSurface,
        cellDepth: retry.result.cellDepth,
        faceVelocity: retry.result.faceVelocity,
      },
    });
  }

  assert.deepEqual(pageExceptions, [], 'The production browser page raised uncaught JavaScript exceptions.');
  assert.deepEqual(failedResponses, [], 'The production browser requested a missing or failed resource.');
  receipt.finishedAt = new Date().toISOString();
  receipt.state = 'passed';
  await writeFile(path.join(outputDirectory, 'receipt.json'), `${JSON.stringify(receipt, null, 2)}\n`);
  process.stdout.write(`${JSON.stringify(receipt, null, 2)}\n`);
}

try {
  await main();
} catch (error) {
  receipt.finishedAt = new Date().toISOString();
  receipt.state = 'failed';
  receipt.error = error instanceof Error ? error.stack : String(error);
  if (socket?.readyState === WebSocket.OPEN && pageSession) {
    try {
      receipt.diagnostics = await evaluate(`({
        url: location.href,
        isolated: crossOriginIsolated,
        currentFeature: document.querySelector('.ras-feature-nav [aria-current="page"]')?.textContent?.trim(),
        pageText: document.querySelector('[data-feature="compute"]')?.innerText?.slice(0, 7000) ?? '',
        fields: Array.from(document.querySelectorAll('[data-compute-field]')).map((node) => ({ field: node.dataset.computeField, value: node.value, invalid: node.getAttribute('aria-invalid') })),
        output: document.querySelector('[data-compute-output]')?.textContent?.trim() ?? '',
        progress: document.querySelector('[data-compute-progress]')?.getAttribute('aria-valuenow') ?? null,
      })`);
      const screenshot = await cdpCommand('Page.captureScreenshot', { format: 'png' });
      await writeFile(path.join(outputDirectory, 'failure.png'), Buffer.from(screenshot.data, 'base64'));
    } catch (diagnosticError) {
      receipt.diagnosticError = String(diagnosticError);
    }
  }
  await mkdir(outputDirectory, { recursive: true });
  await writeFile(path.join(outputDirectory, 'receipt.json'), `${JSON.stringify(receipt, null, 2)}\n`);
  process.stderr.write(`${JSON.stringify(receipt, null, 2)}\n`);
  process.exitCode = 1;
} finally {
  clearTimeout(startupTimer);
  for (const pending of pendingCommands.values()) clearTimeout(pending.timer);
  socket?.close();
  browser?.kill('SIGTERM');
  if (browser && browser.exitCode === null) {
    await Promise.race([
      new Promise((resolve) => browser.once('exit', resolve)),
      delay(5_000).then(() => browser.kill('SIGKILL')),
    ]);
  }
  await server?.close();
  if (browserProfile) await rm(browserProfile, { recursive: true, force: true, maxRetries: 3, retryDelay: 150 });
}
