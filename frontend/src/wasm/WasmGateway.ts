import type { BackendGateway, BackendRequestOptions } from '../shared/workbench';
import { WasmClient } from './WasmClient';

const exposedMethods = new Set(['runtime.info', 'compute.run']);

function decodeJson<T>(raw: string): T {
  try {
    return JSON.parse(raw) as T;
  } catch {
    return raw as T;
  }
}

function abortError(): DOMException {
  return new DOMException('The operation was aborted.', 'AbortError');
}

export class WasmGateway implements BackendGateway {
  constructor(private readonly client = new WasmClient()) {}

  supports(method: string): boolean {
    return exposedMethods.has(method);
  }

  async request<T = unknown>(method: string, parameters: unknown, options?: BackendRequestOptions): Promise<T> {
    if (!this.supports(method)) throw new Error(`WebAssembly operation is not available: ${method}`);
    if (options?.signal?.aborted) throw abortError();

    if (method === 'runtime.info') {
      return decodeJson<T>(await this.client.getRuntimeInfo());
    }

    let requestJson: string;
    if (typeof parameters === 'string') {
      requestJson = parameters;
    } else if (parameters && typeof parameters === 'object' && 'requestJson' in parameters && typeof parameters.requestJson === 'string') {
      requestJson = parameters.requestJson;
    } else {
      requestJson = JSON.stringify(parameters ?? {}) ?? '{}';
    }

    const handle = this.client.runCompute(requestJson, (raw) => options?.onProgress?.(decodeJson(raw)));
    let aborted = false;
    const signal = options?.signal;
    const onAbort = (): void => {
      aborted = true;
      handle.cancel();
    };
    signal?.addEventListener('abort', onAbort, { once: true });
    if (signal?.aborted) onAbort();

    try {
      const raw = await handle.promise;
      if (aborted || signal?.aborted) throw abortError();
      return decodeJson<T>(raw);
    } catch (error) {
      if (aborted || signal?.aborted) throw abortError();
      throw error;
    } finally {
      signal?.removeEventListener('abort', onAbort);
    }
  }
}

export const wasmGateway = new WasmGateway();
