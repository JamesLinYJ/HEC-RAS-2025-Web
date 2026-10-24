export type RpcMethod = 'runtime.info' | 'compute.run';

export interface RpcRequest {
  id: string;
  method: RpcMethod;
  parameters?: {
    requestJson?: string;
    runtimeBaseUrl?: string;
    cancellationBuffer?: SharedArrayBuffer;
  };
}

export type RpcResponse =
  | { id: string; result: string }
  | { id: string; sequence: number; data: string }
  | { id: string; error: { code: string; message: string } };

export class WasmRpcError extends Error {
  readonly code: string;

  constructor(code: string, message: string) {
    super(message);
    this.name = 'WasmRpcError';
    this.code = code;
  }
}

export interface ComputeHandle {
  id: string;
  promise: Promise<string>;
  cancel: () => 'cooperative' | 'worker-terminated';
}
