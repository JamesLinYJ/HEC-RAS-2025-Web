/// <reference lib="webworker" />

import type { RpcRequest, RpcResponse } from './protocol';

declare const self: DedicatedWorkerGlobalScope;

type RuntimeExports = {
  Ras: {
    Wasm: {
      EngineExports: {
        GetRuntimeInfo: () => string | Promise<string>;
        RunCompute: (requestJson: string) => string | Promise<string>;
      };
    };
  };
};

type DotnetRuntime = {
  getAssemblyExports: (name: string) => Promise<RuntimeExports>;
  setModuleImports: (name: string, imports: {
    reportProgress: (json: string) => void;
    isCancellationRequested: () => boolean;
  }) => void;
};

let runtimePromise: Promise<{ runtime: DotnetRuntime; exports: RuntimeExports }> | undefined;
let activeCancellation: Int32Array | undefined;
let currentRequestId: string | undefined;
let progressSequence = 0;

function send(message: RpcResponse): void {
  self.postMessage(message);
}

function getRuntime(runtimeBaseUrl: string): Promise<{ runtime: DotnetRuntime; exports: RuntimeExports }> {
  if (!runtimePromise) {
    const attempt = (async () => {
      const assetBase = new URL(runtimeBaseUrl, self.location.href);
      const runtimeModuleUrl = new URL('_framework/dotnet.js', assetBase);
      const { dotnet } = await import(/* @vite-ignore */ runtimeModuleUrl.href) as {
        dotnet: { create: () => Promise<DotnetRuntime> };
      };
      const runtime = await dotnet.create();
      runtime.setModuleImports('runtime-host', {
        reportProgress: (json: string) => {
          if (!currentRequestId) return;
          progressSequence += 1;
          send({ id: currentRequestId, sequence: progressSequence, data: json });
        },
        isCancellationRequested: () => activeCancellation !== undefined && Atomics.load(activeCancellation, 0) !== 0,
      });
      const exports = await runtime.getAssemblyExports('Ras.Wasm.dll');
      return { runtime, exports };
    })();
    runtimePromise = attempt;
    void attempt.catch(() => {
      if (runtimePromise === attempt) runtimePromise = undefined;
    });
  }
  return runtimePromise;
}

self.addEventListener('message', async (event: MessageEvent<RpcRequest>) => {
  const request = event.data;
  if (!request || typeof request.id !== 'string' || !request.id) return;
  if (request.method !== 'runtime.info' && request.method !== 'compute.run') {
    send({ id: request.id, error: { code: 'UNSUPPORTED_METHOD', message: `Unsupported RPC method: ${String(request.method)}` } });
    return;
  }

  try {
    const runtimeBaseUrl = request.parameters?.runtimeBaseUrl ?? new URL(import.meta.env.BASE_URL, self.location.href).href;
    const { exports } = await getRuntime(runtimeBaseUrl);
    if (request.method === 'runtime.info') {
      const result = await exports.Ras.Wasm.EngineExports.GetRuntimeInfo();
      send({ id: request.id, result });
      return;
    }

    if (currentRequestId) {
      send({ id: request.id, error: { code: 'COMPUTE_BUSY', message: 'A compute request is already running.' } });
      return;
    }

    activeCancellation = request.parameters?.cancellationBuffer
      ? new Int32Array(request.parameters.cancellationBuffer)
      : undefined;
    currentRequestId = request.id;
    progressSequence = 0;
    const requestJson = request.parameters?.requestJson ?? '{}';
    try {
      const result = await exports.Ras.Wasm.EngineExports.RunCompute(requestJson);
      send({ id: request.id, result });
    } finally {
      currentRequestId = undefined;
      activeCancellation = undefined;
    }
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    const code = error && typeof error === 'object' && 'code' in error && typeof error.code === 'string'
      ? error.code
      : 'WASM_RUNTIME_ERROR';
    send({ id: request.id, error: { code, message } });
  }
});
