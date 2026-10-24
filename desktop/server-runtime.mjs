import { spawn } from 'node:child_process';
import { access } from 'node:fs/promises';
import { createServer } from 'node:net';
import path from 'node:path';

const healthPath = '/api/health';
const defaultStartupTimeoutMs = 45_000;
const serverShutdownTimeoutMs = 18_000;
const delay = (milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds));

function normalizeServerUrl(raw) {
  let url;
  try {
    url = new URL(raw);
  } catch {
    throw new Error('HECRAS_SERVER_URL must be a valid HTTP or HTTPS origin.');
  }
  if (!['http:', 'https:'].includes(url.protocol)
    || url.username || url.password || url.pathname !== '/' || url.search || url.hash) {
    throw new Error('HECRAS_SERVER_URL must be a root HTTP or HTTPS origin without embedded credentials, a path, query, or fragment.');
  }
  return url.origin;
}

function readConfiguredPort(raw) {
  if (raw === undefined || raw.trim() === '' || raw.trim() === '0') return undefined;
  const port = Number(raw);
  if (!Number.isInteger(port) || port < 1 || port > 65_535) {
    throw new Error('HECRAS_SERVER_PORT must be an integer from 1 to 65535, or 0 for a dynamic loopback port.');
  }
  return port;
}

async function findLoopbackPort() {
  const probe = createServer();
  await new Promise((resolve, reject) => {
    probe.once('error', reject);
    probe.listen(0, '127.0.0.1', () => {
      probe.removeListener('error', reject);
      resolve();
    });
  });
  const address = probe.address();
  await new Promise((resolve, reject) => probe.close((error) => error ? reject(error) : resolve()));
  if (!address || typeof address === 'string') throw new Error('Could not allocate a loopback port for the C# server.');
  return address.port;
}

async function waitForReadiness(baseUrl, child, timeoutMs, signal) {
  const endpoint = new URL(healthPath, `${baseUrl}/`);
  const deadline = Date.now() + timeoutMs;
  let lastIssue = 'no health response yet';
  while (Date.now() < deadline) {
    if (signal?.aborted) throw new Error('C# server startup was cancelled.');
    if (child?.spawnError) throw new Error(`Could not start the C# server process: ${child.spawnError.message}`);
    if (child && (child.exitCode !== null || child.signalCode !== null)) {
      throw new Error(`The C# server process exited before ${healthPath} reported ready (exit ${child.exitCode ?? child.signalCode}).`);
    }

    const controller = new AbortController();
    const requestTimer = setTimeout(() => controller.abort(), 1_500);
    try {
      const response = await fetch(endpoint, {
        headers: { Accept: 'application/json' },
        redirect: 'error',
        signal: signal ? AbortSignal.any([controller.signal, signal]) : controller.signal,
      });
      if (response.ok) {
        const payload = await response.json();
        if (payload?.status === 'ready' && payload?.mode === 'server') return payload;
        lastIssue = 'health response did not contain { status: "ready", mode: "server" }';
      } else {
        lastIssue = `health endpoint returned HTTP ${response.status}`;
      }
    } catch (error) {
      lastIssue = error instanceof Error ? error.name : 'request failed';
    } finally {
      clearTimeout(requestTimer);
    }
    await delay(150);
  }
  throw new Error(`C# server did not become ready at ${healthPath} within ${timeoutMs} ms (${lastIssue}).`);
}

function processGroupExists(pid) {
  try {
    process.kill(-pid, 0);
    return true;
  } catch (error) {
    return error?.code !== 'ESRCH';
  }
}

async function waitForOwnedProcessExit(child, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const parentExited = child.exitCode !== null || child.signalCode !== null;
    if (process.platform === 'win32' ? parentExited : parentExited && !processGroupExists(child.pid)) return true;
    await delay(100);
  }
  return process.platform === 'win32'
    ? child.exitCode !== null || child.signalCode !== null
    : (child.exitCode !== null || child.signalCode !== null) && !processGroupExists(child.pid);
}

async function killWindowsProcessTree(pid) {
  const killer = spawn('taskkill.exe', ['/PID', String(pid), '/T', '/F'], {
    stdio: 'ignore', shell: false, windowsHide: true,
  });
  await new Promise((resolve) => {
    killer.once('error', resolve);
    killer.once('exit', resolve);
  });
}

async function stopChild(child) {
  if (child.spawnError) return;

  if (process.platform === 'win32') {
    if (child.exitCode === null && child.signalCode === null) child.kill('SIGTERM');
    if (await waitForOwnedProcessExit(child, serverShutdownTimeoutMs)) return;
    await killWindowsProcessTree(child.pid);
    await waitForOwnedProcessExit(child, 2_000);
    return;
  }

  if (!Number.isInteger(child.pid)) return;
  if ((child.exitCode !== null || child.signalCode !== null) && !child.exitedWithSurvivingGroup) return;
  if (child.exitCode === null && child.signalCode === null) child.kill('SIGTERM');
  if (await waitForOwnedProcessExit(child, serverShutdownTimeoutMs)) return;
  try { process.kill(-child.pid, 'SIGKILL'); }
  catch (error) { if (error?.code !== 'ESRCH') throw error; }
  await waitForOwnedProcessExit(child, 2_000);
}

function makeConnection(baseUrl, ownership, close) {
  let closePromise;
  return {
    baseUrl,
    ownership,
    close: () => {
      closePromise ??= Promise.resolve().then(close);
      return closePromise;
    },
  };
}

export async function connectBackend({ projectRoot, environment = process.env, webRoot = path.join(projectRoot, 'dist'), startupTimeoutMs = defaultStartupTimeoutMs, signal }) {
  if (signal?.aborted) throw new Error('C# server startup was cancelled.');
  const configuredUrl = environment.HECRAS_SERVER_URL?.trim();
  if (configuredUrl) {
    const baseUrl = normalizeServerUrl(configuredUrl);
    await waitForReadiness(baseUrl, undefined, startupTimeoutMs, signal);
    return makeConnection(baseUrl, 'external', async () => {});
  }

  const resolvedWebRoot = path.resolve(webRoot);
  try {
    await access(path.join(resolvedWebRoot, 'index.html'));
  } catch {
    throw new Error('The built React UI is missing. Run `npm run build:web` before starting the C# server.');
  }

  const serverDll = path.resolve(environment.HECRAS_SERVER_DLL ?? path.join(projectRoot, 'artifacts/server/Ras.Backend.Server.dll'));
  try {
    await access(serverDll);
  } catch {
    throw new Error('The published C# server is missing. Run `npm run build:server` or set HECRAS_SERVER_DLL.');
  }

  const fixedPort = readConfiguredPort(environment.HECRAS_SERVER_PORT);
  const port = fixedPort ?? await findLoopbackPort();
  if (signal?.aborted) throw new Error('C# server startup was cancelled.');
  const baseUrl = `http://127.0.0.1:${port}`;
  const dotnet = environment.HECRAS_DOTNET?.trim() || 'dotnet';
  const child = spawn(dotnet, [serverDll, '--urls', baseUrl, '--content-root', projectRoot], {
    cwd: projectRoot,
    env: {
      ...environment,
      ASPNETCORE_URLS: baseUrl,
      HECRAS_WEB_ROOT: resolvedWebRoot,
    },
    stdio: 'ignore',
    shell: false,
    detached: process.platform !== 'win32',
    windowsHide: true,
  });
  child.spawnError = undefined;
  child.on('error', (error) => { child.spawnError = error; });
  child.once('exit', () => {
    child.exitedWithSurvivingGroup = process.platform !== 'win32' && processGroupExists(child.pid);
  });

  try {
    await waitForReadiness(baseUrl, child, startupTimeoutMs, signal);
  } catch (error) {
    await stopChild(child);
    throw error;
  }

  return makeConnection(baseUrl, 'owned', () => stopChild(child));
}

export async function getServerHealth(baseUrl) {
  const response = await fetch(new URL(healthPath, `${baseUrl}/`), {
    headers: { Accept: 'application/json' },
    redirect: 'error',
    signal: AbortSignal.timeout(5_000),
  });
  if (!response.ok) throw new Error(`C# server health endpoint returned HTTP ${response.status}.`);
  const payload = await response.json();
  if (payload?.status !== 'ready' || payload?.mode !== 'server') {
    throw new Error('C# server health endpoint returned an unexpected readiness response.');
  }
  return payload;
}

export async function getServerRuntime(baseUrl) {
  const response = await fetch(new URL('/api/runtime', `${baseUrl}/`), {
    headers: { Accept: 'application/json' },
    redirect: 'error',
    signal: AbortSignal.timeout(5_000),
  });
  if (!response.ok) throw new Error(`C# server runtime endpoint returned HTTP ${response.status}.`);
  const payload = await response.json();
  if (payload?.mode !== 'server' || typeof payload.runtime !== 'string' || !Array.isArray(payload.operations)) {
    throw new Error('C# server runtime endpoint returned an unexpected response.');
  }
  return payload;
}
