import assert from 'node:assert/strict';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { build } from 'vite';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const temporaryDirectory = await mkdtemp(path.join(tmpdir(), 'hecras-gateway-'));
const unhandledRejections = [];
const recordUnhandledRejection = (reason) => unhandledRejections.push(reason);
process.on('unhandledRejection', recordUnhandledRejection);

const testMethod = 'fixture.test';
const testOperationId = 'gateway-test-operation';

function jsonResponse(value, status = 200) {
  return new Response(JSON.stringify(value), {
    status,
    headers: { 'content-type': 'application/json' },
  });
}

function requestDetails(input, init = {}) {
  const url = new URL(typeof input === 'string' ? input : input.url, 'http://127.0.0.1');
  return { url, method: (init.method ?? 'GET').toUpperCase() };
}

function acceptedOperation() {
  return jsonResponse({ id: testOperationId }, 202);
}

function sleep(milliseconds) {
  return new Promise((resolve) => globalThis.setTimeout(resolve, milliseconds));
}

async function createBackend(ServerBackend, fetcher) {
  const backend = new ServerBackend('/api', fetcher);
  const runtime = await backend.connect();
  assert.deepEqual(runtime.operations, [testMethod]);
  return backend;
}

async function withTimeout(promise, label, timeoutMilliseconds = 10_000) {
  let timeout;
  try {
    return await Promise.race([
      promise,
      new Promise((_, reject) => {
        timeout = globalThis.setTimeout(() => reject(new Error(`Timed out: ${label}`)), timeoutMilliseconds);
      }),
    ]);
  } finally {
    if (timeout !== undefined) globalThis.clearTimeout(timeout);
  }
}

async function verifyPollRecoveryAndTerminalPagination(ServerBackend) {
  let pollCalls = 0;
  const requestedCursors = [];
  const transportStates = [];
  const progressEvents = [];
  const fetcher = async (input, init = {}) => {
    const { url, method } = requestDetails(input, init);
    if (url.pathname === '/api/runtime' && method === 'GET') {
      return jsonResponse({ mode: 'server', runtime: {}, operations: [testMethod] });
    }
    if (url.pathname === '/api/operations' && method === 'POST') return acceptedOperation();
    if (url.pathname === `/api/operations/${testOperationId}` && method === 'GET') {
      pollCalls++;
      requestedCursors.push(url.searchParams.get('after'));
      if (pollCalls === 1) return jsonResponse({ error: { code: 'TEMPORARY', message: 'temporary outage' } }, 503);
      if (pollCalls === 2) {
        return jsonResponse({
          id: testOperationId,
          state: 'completed',
          cursor: 1,
          hasMore: true,
          events: [{ sequence: 0, progress: { fraction: 0.5, message: 'first page' } }],
        });
      }
      if (pollCalls === 3) {
        return jsonResponse({
          id: testOperationId,
          state: 'completed',
          cursor: 1,
          hasMore: false,
          events: [],
          result: { outputPath: '/fixture/result.g01' },
        });
      }
      throw new Error(`Unexpected poll request ${pollCalls}`);
    }
    throw new Error(`Unexpected request: ${method} ${url.pathname}`);
  };

  const backend = await createBackend(ServerBackend, fetcher);
  try {
    const result = await withTimeout(backend.request(testMethod, {}, {
      onProgress: (progress) => progressEvents.push(progress),
      onTransportState: (state) => transportStates.push(state),
    }), 'poll retry and hasMore terminal pagination');
    assert.deepEqual(result, { outputPath: '/fixture/result.g01' });
    assert.equal(pollCalls, 3);
    assert.deepEqual(requestedCursors, ['0', '0', '1']);
    assert.equal(progressEvents.length, 1);
    assert.deepEqual(progressEvents[0], { fraction: 0.5, message: 'first page' });
    assert.deepEqual(transportStates.map((state) => state.state), ['reconnecting', 'connected']);
  } finally {
    backend.dispose();
  }
}

async function verifyCancelRetryAndConfirmedCanceled(ServerBackend) {
  const controller = new AbortController();
  const transportStates = [];
  let pollStartedResolve;
  const pollStarted = new Promise((resolve) => { pollStartedResolve = resolve; });
  let cancelAcknowledgedResolve;
  const cancelAcknowledged = new Promise((resolve) => { cancelAcknowledgedResolve = resolve; });
  let pollCalls = 0;
  let deleteCalls = 0;
  let cancelAcknowledgedAt = 0;
  const fetcher = async (input, init = {}) => {
    const { url, method } = requestDetails(input, init);
    if (url.pathname === '/api/runtime' && method === 'GET') {
      return jsonResponse({ mode: 'server', runtime: {}, operations: [testMethod] });
    }
    if (url.pathname === '/api/operations' && method === 'POST') return acceptedOperation();
    if (url.pathname === `/api/operations/${testOperationId}` && method === 'GET') {
      pollCalls++;
      pollStartedResolve();
      if (cancelAcknowledgedAt && Date.now() - cancelAcknowledgedAt >= 350) {
        return jsonResponse({ id: testOperationId, state: 'canceled', cursor: 0, hasMore: false, events: [] });
      }
      return jsonResponse({ id: testOperationId, state: 'running', cursor: 0, hasMore: false, events: [] });
    }
    if (url.pathname === `/api/operations/${testOperationId}` && method === 'DELETE') {
      deleteCalls++;
      if (deleteCalls === 1) return jsonResponse({ error: { code: 'TEMPORARY', message: 'temporary outage' } }, 503);
      if (deleteCalls === 2) {
        cancelAcknowledgedAt = Date.now();
        return jsonResponse({ id: testOperationId, cancelRequested: true });
      }
      throw new Error(`Unexpected cancellation retry ${deleteCalls}`);
    }
    throw new Error(`Unexpected request: ${method} ${url.pathname}`);
  };

  const backend = await createBackend(ServerBackend, fetcher);
  let requestSettled = false;
  try {
    const request = backend.request(testMethod, {}, {
      signal: controller.signal,
      onTransportState: (state) => {
        transportStates.push(state);
        if (state.state === 'cancelSent') cancelAcknowledgedResolve();
      },
    });
    request.then(() => { requestSettled = true; }, () => { requestSettled = true; });
    await withTimeout(pollStarted, 'first cancel-case poll');
    controller.abort();
    await withTimeout(cancelAcknowledged, 'successful cancellation acknowledgment');
    assert.equal(deleteCalls, 2, 'the failed DELETE must be resent');
    assert.deepEqual(transportStates.map((state) => state.state), ['cancelRequested', 'cancelRetrying', 'cancelSent']);
    assert.equal(transportStates.at(-1).accepted, true);
    await sleep(75);
    assert.equal(requestSettled, false, 'DELETE acknowledgment must not be treated as terminal cancellation');
    await assert.rejects(withTimeout(request, 'server-confirmed canceled terminal state'), (error) => {
      assert.equal(error.name, 'AbortError');
      return true;
    });
    assert.ok(pollCalls >= 2, 'the client must keep polling after DELETE acknowledgment');
  } finally {
    backend.dispose();
  }
}

async function verifyCompletionDuringCancelRetryHasNoUnhandledRejection(ServerBackend) {
  const controller = new AbortController();
  let pollStartedResolve;
  const pollStarted = new Promise((resolve) => { pollStartedResolve = resolve; });
  let retryingResolve;
  const cancelRetrying = new Promise((resolve) => { retryingResolve = resolve; });
  let pollCalls = 0;
  let deleteCalls = 0;
  const fetcher = async (input, init = {}) => {
    const { url, method } = requestDetails(input, init);
    if (url.pathname === '/api/runtime' && method === 'GET') {
      return jsonResponse({ mode: 'server', runtime: {}, operations: [testMethod] });
    }
    if (url.pathname === '/api/operations' && method === 'POST') return acceptedOperation();
    if (url.pathname === `/api/operations/${testOperationId}` && method === 'GET') {
      pollCalls++;
      pollStartedResolve();
      if (pollCalls === 1) return jsonResponse({ id: testOperationId, state: 'running', cursor: 0, hasMore: false, events: [] });
      return jsonResponse({
        id: testOperationId,
        state: 'completed',
        cursor: 0,
        hasMore: false,
        events: [],
        result: { completedByServer: true },
      });
    }
    if (url.pathname === `/api/operations/${testOperationId}` && method === 'DELETE') {
      deleteCalls++;
      return jsonResponse({ error: { code: 'TEMPORARY', message: 'temporary outage' } }, 503);
    }
    throw new Error(`Unexpected request: ${method} ${url.pathname}`);
  };

  const backend = await createBackend(ServerBackend, fetcher);
  try {
    const request = backend.request(testMethod, {}, {
      signal: controller.signal,
      onTransportState: (state) => {
        if (state.state === 'cancelRetrying') retryingResolve();
      },
    });
    await withTimeout(pollStarted, 'first completion-race poll');
    controller.abort();
    await withTimeout(cancelRetrying, 'DELETE retry sleeper');
    const result = await withTimeout(request, 'late server completion during DELETE retry wait');
    assert.deepEqual(result, { completedByServer: true });
    assert.equal(deleteCalls, 1, 'the operation should finish before another DELETE retry is due');
    await sleep(1_100);
    assert.deepEqual(unhandledRejections, [], 'completion during cancellation backoff must not emit unhandledRejection');
  } finally {
    backend.dispose();
  }
}

async function verifyNotFoundIsUnconfirmedAndKeepsOperationId(ServerBackend) {
  const fetcher = async (input, init = {}) => {
    const { url, method } = requestDetails(input, init);
    if (url.pathname === '/api/runtime' && method === 'GET') {
      return jsonResponse({ mode: 'server', runtime: {}, operations: [testMethod] });
    }
    if (url.pathname === '/api/operations' && method === 'POST') return acceptedOperation();
    if (url.pathname === `/api/operations/${testOperationId}` && method === 'GET') {
      return jsonResponse({ error: { code: 'NOT_FOUND', message: 'operation lookup unavailable' } }, 404);
    }
    throw new Error(`Unexpected request: ${method} ${url.pathname}`);
  };

  const backend = await createBackend(ServerBackend, fetcher);
  try {
    await assert.rejects(backend.request(testMethod, {}), (error) => {
      assert.equal(error.code, 'OPERATION_STATUS_UNCONFIRMED');
      assert.equal(error.operationId, testOperationId);
      assert.equal(error.terminalConfirmed, false);
      assert.equal(error.details.operationId, testOperationId);
      assert.equal(error.details.lastError.status, 404);
      return true;
    });
  } finally {
    backend.dispose();
  }
}

async function verifyFailedTerminalPagination(ServerBackend) {
  let pollCalls = 0;
  let finalErrorDelivered = false;
  const requestedCursors = [];
  const fetcher = async (input, init = {}) => {
    const { url, method } = requestDetails(input, init);
    if (url.pathname === '/api/runtime' && method === 'GET') {
      return jsonResponse({ mode: 'server', runtime: {}, operations: [testMethod] });
    }
    if (url.pathname === '/api/operations' && method === 'POST') return acceptedOperation();
    if (url.pathname === `/api/operations/${testOperationId}` && method === 'GET') {
      pollCalls++;
      requestedCursors.push(url.searchParams.get('after'));
      if (pollCalls === 1) {
        return jsonResponse({
          id: testOperationId,
          state: 'failed',
          cursor: 1,
          hasMore: true,
          events: [{ sequence: 0, progress: { fraction: 1, message: 'failure reported' } }],
        });
      }
      if (pollCalls === 2) {
        finalErrorDelivered = true;
        return jsonResponse({
          id: testOperationId,
          state: 'failed',
          cursor: 1,
          hasMore: false,
          events: [],
          error: { code: 'SOLVER_FAILED', message: 'fixture solver failed' },
        });
      }
      throw new Error(`Unexpected poll request ${pollCalls}`);
    }
    throw new Error(`Unexpected request: ${method} ${url.pathname}`);
  };

  const backend = await createBackend(ServerBackend, fetcher);
  try {
    await assert.rejects(withTimeout(backend.request(testMethod, {}), 'failed terminal pagination'), (error) => {
      assert.equal(finalErrorDelivered, true, 'the failure should only be thrown after the final progress page arrives');
      assert.equal(pollCalls, 2);
      assert.deepEqual(requestedCursors, ['0', '1']);
      assert.equal(error.code, 'SOLVER_FAILED');
      assert.equal(error.operationId, testOperationId);
      assert.equal(error.terminalConfirmed, true);
      return true;
    });
  } finally {
    backend.dispose();
  }
}

async function main() {
  const bundleDirectory = path.join(temporaryDirectory, 'bundle');
  await build({
    configFile: false,
    root: repositoryRoot,
    logLevel: 'silent',
    build: {
      lib: {
        entry: path.join(repositoryRoot, 'frontend/src/platform/ServerBackend.ts'),
        formats: ['es'],
        fileName: () => 'server-backend.mjs',
      },
      outDir: bundleDirectory,
      emptyOutDir: true,
      minify: false,
      sourcemap: false,
      rollupOptions: {
        output: { chunkFileNames: 'chunks/[name]-[hash].mjs' },
      },
    },
  });

  const bundleUrl = pathToFileURL(path.join(bundleDirectory, 'server-backend.mjs')).href;
  const { ServerBackend } = await import(bundleUrl);

  await verifyPollRecoveryAndTerminalPagination(ServerBackend);
  console.log('PASS GET 503 recovery and hasMore terminal pagination');

  await verifyCancelRetryAndConfirmedCanceled(ServerBackend);
  console.log('PASS DELETE 503 retry and server-confirmed canceled terminal');

  await verifyCompletionDuringCancelRetryHasNoUnhandledRejection(ServerBackend);
  console.log('PASS completion during DELETE retry wait without unhandled rejection');

  await verifyNotFoundIsUnconfirmedAndKeepsOperationId(ServerBackend);
  console.log('PASS GET 404 remains unconfirmed and preserves operation id');

  await verifyFailedTerminalPagination(ServerBackend);
  console.log('PASS failed state throws only after final page with confirmed terminal');
}

try {
  await main();
} finally {
  process.removeListener('unhandledRejection', recordUnhandledRejection);
  await rm(temporaryDirectory, { recursive: true, force: true });
}
