import { BackendOperationUnavailableError } from '../shared/workbench';
import type { BackendGateway, BackendRequestOptions } from '../shared/workbench';
import { WasmClient } from '../wasm/WasmClient';

/** Browser/Electron share the same WASM transport; desktop system APIs do not execute models. */
export class BrowserWasmBackend implements BackendGateway {
  readonly client = new WasmClient();
  private runtimeReady = false;

  supports(method: string): boolean {
    return method === 'runtime.info' || (this.runtimeReady && method === 'compute.run');
  }

  async request<T = unknown>(method: string, parameters: unknown, options?: BackendRequestOptions): Promise<T> {
    if (options?.signal?.aborted) throw options.signal.reason ?? new DOMException('Aborted', 'AbortError');
    if (!this.supports(method)) throw new BackendOperationUnavailableError(method);
    if (method === 'runtime.info') {
      const result: unknown = JSON.parse(await this.client.getRuntimeInfo());
      if (options?.signal?.aborted) throw options.signal.reason ?? new DOMException('Aborted', 'AbortError');
      this.runtimeReady = true;
      return result as T;
    }
    const run = this.client.runCompute(JSON.stringify(parameters ?? {}), (data) => {
      let progress: unknown = data;
      try { progress = JSON.parse(data); } catch { /* Retain the exact diagnostic payload. */ }
      options?.onProgress?.(progress);
    });
    const cancel = () => { run.cancel(); };
    options?.signal?.addEventListener('abort', cancel, { once: true });
    if (options?.signal?.aborted) cancel();
    try {
      return JSON.parse(await run.promise) as T;
    } finally {
      options?.signal?.removeEventListener('abort', cancel);
    }
  }

  dispose(): void {
    this.runtimeReady = false;
    this.client.dispose();
  }
}
