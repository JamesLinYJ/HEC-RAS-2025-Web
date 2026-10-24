import type { ComputeHandle, RpcRequest, RpcResponse } from './protocol';
import { WasmRpcError } from './protocol';

type Pending = {
  resolve: (result: string) => void;
  reject: (error: Error) => void;
  onProgress?: (data: string, sequence: number) => void;
  lastSequence: number;
};

export class WasmClient {
  private worker: Worker | undefined;
  private readonly pending = new Map<string, Pending>();

  private readonly runtimeBaseUrl = new URL(import.meta.env.BASE_URL, document.baseURI).href;

  private getWorker(): Worker {
    if (this.worker) return this.worker;

    const worker = new Worker(new URL('./wasm.worker.ts', import.meta.url), {
      type: 'module',
      name: 'hec-ras-wasm-runtime',
    });
    worker.addEventListener('message', (event: MessageEvent<RpcResponse>) => {
      const message = event.data;
      const pending = this.pending.get(message.id);
      if (!pending) return;

      if ('sequence' in message) {
        if (message.sequence <= pending.lastSequence) return;
        pending.lastSequence = message.sequence;
        pending.onProgress?.(message.data, message.sequence);
        return;
      }

      this.pending.delete(message.id);
      if ('error' in message) {
        pending.reject(new WasmRpcError(message.error.code, message.error.message));
      } else {
        pending.resolve(message.result);
      }
    });
    worker.addEventListener('error', (event) => {
      event.preventDefault();
      this.failWorker(new WasmRpcError('WORKER_ERROR', event.message || 'WASM worker failed.'));
    });
    worker.addEventListener('messageerror', () => {
      this.failWorker(new WasmRpcError('WORKER_MESSAGE_ERROR', 'WASM worker returned an unreadable message.'));
    });
    this.worker = worker;
    return worker;
  }

  private failWorker(error: Error): void {
    this.worker?.terminate();
    this.worker = undefined;
    for (const [id, pending] of this.pending) {
      this.pending.delete(id);
      pending.reject(error);
    }
  }

  private request(method: RpcRequest['method'], parameters?: RpcRequest['parameters'], onProgress?: Pending['onProgress']): { id: string; promise: Promise<string> } {
    const id = crypto.randomUUID();
    const worker = this.getWorker();
    const promise = new Promise<string>((resolve, reject) => {
      this.pending.set(id, { resolve, reject, onProgress, lastSequence: 0 });
      worker.postMessage({ id, method, parameters } satisfies RpcRequest);
    });
    return { id, promise };
  }

  getRuntimeInfo(): Promise<string> {
    return this.request('runtime.info', { runtimeBaseUrl: this.runtimeBaseUrl }).promise;
  }

  runCompute(requestJson = '{}', onProgress?: (data: string, sequence: number) => void): ComputeHandle {
    let cancellationBuffer: SharedArrayBuffer | undefined;
    if (globalThis.crossOriginIsolated && typeof SharedArrayBuffer !== 'undefined') {
      cancellationBuffer = new SharedArrayBuffer(Int32Array.BYTES_PER_ELEMENT);
    }

    const { id, promise } = this.request(
      'compute.run',
      { requestJson, runtimeBaseUrl: this.runtimeBaseUrl, ...(cancellationBuffer ? { cancellationBuffer } : {}) },
      onProgress,
    );

    return {
      id,
      promise,
      cancel: () => {
        if (cancellationBuffer) {
          Atomics.store(new Int32Array(cancellationBuffer), 0, 1);
          Atomics.notify(new Int32Array(cancellationBuffer), 0);
          return 'cooperative';
        }

        this.failWorker(new WasmRpcError('WORKER_TERMINATED', 'The worker was stopped and its result was discarded because this browser does not support shared-memory cooperative cancellation.'));
        return 'worker-terminated';
      },
    };
  }

  dispose(): void {
    this.failWorker(new WasmRpcError('CLIENT_DISPOSED', 'WASM worker was disposed.'));
  }
}
