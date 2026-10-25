import { BackendOperationUnavailableError } from '../shared/workbench';
import type { BackendGateway, BackendRequestOptions, BackendTransportState, Language, WorkspaceUploadResult, WorkbenchContext, MapSourceUploadResult, UploadedMapSource } from '../shared/workbench';

export interface ServerRuntimeInfo {
  mode: 'server';
  runtime: unknown;
  operations: string[];
}

interface OperationEvent {
  sequence: number;
  progress: unknown;
}

interface OperationPoll {
  id: string;
  state: 'queued' | 'running' | 'completed' | 'failed' | 'canceled';
  cursor: number;
  hasMore: boolean;
  events: OperationEvent[];
  result?: unknown;
  error?: unknown;
}

export interface ServerBackendErrorMetadata {
  status?: number;
  operation?: string;
  operationId?: string;
  terminalConfirmed?: boolean;
  cause?: unknown;
}

const serverErrorCopy = {
  zh: {
    CLIENT_DISPOSED: '服务连接已关闭，请重新连接后重试。',
    EMPTY_UPLOAD: '请先选择至少一个工程文件。',
    INVALID_WORKSPACE_ID: '工程工作区标识无效，请重新上传工程。',
    INVALID_SOURCE_UPLOAD_RESPONSE: '服务端返回的地图来源信息无效，请重新载入工程后重试。',
    INVALID_ACCEPT_RESPONSE: '服务端未返回有效的操作受理信息。',
    INVALID_RUNTIME_RESPONSE: '服务端返回的能力信息格式无效。',
    INVALID_UPLOAD_RESPONSE: '服务端返回的工作区上传信息格式无效。',
    INVALID_OPERATION_RESPONSE: '服务端返回的操作状态格式无效。',
    INVALID_OPERATION_EVENT: '服务端返回的进度信息格式无效。',
    INVALID_JSON_RESPONSE: '服务端返回的数据无法解析。',
    NETWORK_ERROR: '无法连接 C# 服务，请确认服务已启动且网络可用。',
    REQUEST_TIMEOUT: '等待 C# 服务响应超时，请检查服务状态后重试。',
    INVALID_REQUEST: '前端请求数据无法发送，请检查输入后重试。',
    OPERATION_FAILED: 'C# 服务未能完成该操作。',
    OPERATION_STATUS_UNCONFIRMED: '任务状态尚未确认。请检查服务端状态后再决定是否重试。',
    INVALID_CANCEL_RESPONSE: '服务端取消请求的响应格式无效，任务是否已取消尚未确认。',
    invalidInput: '输入内容或模型参数无效，请检查后重试。',
    notFound: '找不到所需的工程、模型或数据文件，请检查路径。',
    accessDenied: '无法访问所需的文件或目录，请检查访问权限。',
    sourceChanged: '源文件已发生变化，请重新载入后重试。',
    unsupported: '当前服务暂不支持此项操作或数据格式。',
    sourceFailure: '读取或处理源数据失败，请查看诊断信息。',
    mapOwnershipConflict: '此地图数据正由其他会话使用，请关闭占用后重试。',
  },
  en: {
    CLIENT_DISPOSED: 'The server connection has closed. Reconnect and try again.',
    EMPTY_UPLOAD: 'Choose at least one project file first.',
    INVALID_WORKSPACE_ID: 'The workspace identifier is invalid. Upload the project again.',
    INVALID_SOURCE_UPLOAD_RESPONSE: 'The server returned invalid map source upload information. Reload the project and try again.',
    INVALID_ACCEPT_RESPONSE: 'The server did not return valid operation acceptance data.',
    INVALID_RUNTIME_RESPONSE: 'The server returned invalid capability information.',
    INVALID_UPLOAD_RESPONSE: 'The server returned invalid workspace upload information.',
    INVALID_OPERATION_RESPONSE: 'The server returned an invalid operation state.',
    INVALID_OPERATION_EVENT: 'The server returned an invalid progress event.',
    INVALID_JSON_RESPONSE: 'The server returned data that could not be parsed.',
    NETWORK_ERROR: 'Cannot reach the C# service. Confirm that it is running and reachable.',
    REQUEST_TIMEOUT: 'The C# service did not respond in time. Check its status and try again.',
    INVALID_REQUEST: 'The request data could not be sent. Check the input and try again.',
    OPERATION_FAILED: 'The C# service could not complete the operation.',
    OPERATION_STATUS_UNCONFIRMED: 'The operation status is unconfirmed. Check the server before deciding whether to retry.',
    INVALID_CANCEL_RESPONSE: 'The cancellation response was invalid; the operation status is not yet confirmed.',
    invalidInput: 'The input or model parameters are invalid. Check them and try again.',
    notFound: 'A required project, model, or data file was not found. Check the path.',
    accessDenied: 'A required file or directory could not be accessed. Check its permissions.',
    sourceChanged: 'The source file has changed. Reload it and try again.',
    unsupported: 'The service does not support this operation or data format.',
    sourceFailure: 'The source data could not be read or processed. See diagnostics.',
    mapOwnershipConflict: 'Another session is using this map data. Close that session and try again.',
  },
} satisfies Record<Language, Record<string, string>>;

function userErrorMessage(code: string, language: Language, metadata: ServerBackendErrorMetadata): string {
  const known = (serverErrorCopy[language] as Record<string, string>)[code];
  if (known) return known;
  const status = metadata.status;
  if (status === 401) return language === 'zh' ? '服务端拒绝了请求，请重新确认登录状态。' : 'The server rejected the request. Check the sign-in state.';
  if (status === 403) return language === 'zh' ? '服务端拒绝了此请求，请检查访问地址及权限。' : 'The server refused this request. Check its address and access permissions.';
  if (status === 404) return language === 'zh' ? '服务端找不到请求的资源或接口。' : 'The server could not find the requested resource or endpoint.';
  if (status === 408 || status === 504) return serverErrorCopy[language].REQUEST_TIMEOUT;
  if (status === 413) return language === 'zh' ? '上传内容超过服务端允许的大小。' : 'The upload exceeds the size allowed by the server.';
  if (status !== undefined && status >= 500) return language === 'zh' ? 'C# 服务处理请求时发生错误。' : 'The C# service encountered an error while handling the request.';
  if (status !== undefined && status >= 400) return language === 'zh' ? '服务端未接受该请求。' : 'The server did not accept the request.';
  if (language === 'zh') return 'C# 服务操作未能完成，请展开诊断信息查看原因。';
  return `The C# service operation failed (${code}). Expand diagnostics for details.`;
}

export class ServerBackendError extends Error {
  readonly diagnosticMessage: string;
  readonly details?: unknown;
  readonly status?: number;
  readonly operation?: string;
  readonly operationId?: string;
  readonly terminalConfirmed?: boolean;
  readonly diagnosticCause?: unknown;

  constructor(readonly code: string, diagnosticMessage: string, details?: unknown, metadata: ServerBackendErrorMetadata = {}) {
    super(userErrorMessage(code, 'zh', metadata), metadata.cause === undefined ? undefined : { cause: metadata.cause });
    this.name = 'ServerBackendError';
    this.diagnosticMessage = diagnosticMessage;
    this.details = details;
    this.status = metadata.status;
    this.operation = metadata.operation;
    this.operationId = metadata.operationId;
    this.terminalConfirmed = metadata.terminalConfirmed;
    this.diagnosticCause = metadata.cause;
  }

  localizedMessage(language: Language): string {
    return userErrorMessage(this.code, language, {
      status: this.status, operation: this.operation, operationId: this.operationId,
      terminalConfirmed: this.terminalConfirmed,
    });
  }
}

const terminalStates = new Set<OperationPoll['state']>(['completed', 'failed', 'canceled']);
const pollIntervalMilliseconds = 150;
const runtimeRequestTimeoutMilliseconds = 10_000;
const operationRequestTimeoutMilliseconds = 15_000;
const uploadRequestTimeoutMilliseconds = 120_000;
const maximumConsecutivePollRetries = 8;
const pollRetryBaseMilliseconds = 500;
const pollRetryMaximumMilliseconds = 8_000;
const cancelRetryBaseMilliseconds = 1_000;
const cancelRetryMaximumMilliseconds = 5_000;

/** Same-origin HTTP gateway for the ASP.NET Core host. Compute stays server-owned. */
export class ServerBackend implements BackendGateway {
  private readonly listeners = new Set<(revision: number) => void>();
  private operations = new Set<string>();
  private revision = 0;
  private connectGeneration = 0;
  private disposed = false;
  private readonly lifetime = new AbortController();

  constructor(private readonly apiBase = '/api', private readonly fetcher: typeof fetch = fetch) {}

  supports(method: string): boolean {
    return !this.disposed && (method === 'runtime.info' || this.operations.has(method));
  }

  subscribeCapabilities(listener: (revision: number) => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  async connect(signal?: AbortSignal): Promise<ServerRuntimeInfo> {
    if (this.disposed) throw new ServerBackendError('CLIENT_DISPOSED', 'The server gateway has been disposed.');
    const generation = ++this.connectGeneration;
    try {
      const response = await this.fetchTimed(this.url('/runtime'), {
        method: 'GET',
        headers: { Accept: 'application/json' },
        credentials: 'same-origin',
      }, runtimeRequestTimeoutMilliseconds, signal);
      const body = await this.readJson(response);
      if (!response.ok) throw this.httpError(response, body, 'GET /runtime');
      const runtime = this.parseRuntime(body);
      if (generation !== this.connectGeneration || this.disposed) return runtime;
      const nextOperations = new Set(runtime.operations);
      if (!sameSet(this.operations, nextOperations)) {
        this.operations = nextOperations;
        this.publishCapabilities();
      }
      return runtime;
    } catch (error) {
      if (generation === this.connectGeneration && !this.disposed && !signal?.aborted && this.operations.size > 0) {
        this.operations.clear();
        this.publishCapabilities();
      }
      throw error;
    }
  }

  async request<T = unknown>(method: string, parameters: unknown, options?: BackendRequestOptions): Promise<T> {
    if (this.disposed) throw new ServerBackendError('CLIENT_DISPOSED', 'The server gateway has been disposed.');
    if (options?.signal?.aborted) throw abortError(options.signal.reason);
    if (method === 'runtime.info') return await this.connect(options?.signal) as T;
    if (!this.supports(method)) throw new BackendOperationUnavailableError(method);
    return await this.runOperation<T>(method, parameters, options);
  }

  /** Rejoin a previously accepted task without submitting its write request again. */
  async resumeOperation<T = unknown>(operationId: string, options?: BackendRequestOptions): Promise<T> {
    if (this.disposed) throw new ServerBackendError('CLIENT_DISPOSED', 'The server gateway has been disposed.');
    if (options?.signal?.aborted) throw abortError(options.signal.reason);
    if (!/^[a-f0-9]{32}$/i.test(operationId)) {
      throw new ServerBackendError('INVALID_REQUEST', 'Expected the exact HTTP operation id returned by the server.');
    }
    return await this.trackOperation<T>(operationId, options);
  }

  async uploadWorkspace(files: File[], options?: Pick<BackendRequestOptions, 'signal'>): Promise<WorkspaceUploadResult> {
    if (options?.signal?.aborted) throw abortError(options.signal.reason);
    if (files.length === 0) throw new ServerBackendError('EMPTY_UPLOAD', 'Choose at least one workspace file.', { fileCount: 0 });
    const form = new FormData();
    for (const file of files) {
      const relativePath = 'webkitRelativePath' in file && typeof file.webkitRelativePath === 'string'
        ? file.webkitRelativePath
        : '';
      form.append('files', file, relativePath || file.name);
    }
    const response = await this.fetchTimed(this.url('/workspaces'), {
      method: 'POST',
      body: form,
      credentials: 'same-origin',
      headers: { Accept: 'application/json' },
    }, uploadRequestTimeoutMilliseconds, options?.signal);
    const body = await this.readJson(response);
    if (!response.ok) throw this.httpError(response, body, 'POST /workspaces');
    return this.parseUploadResult(body);
  }

  workspaceArchiveUrl(workspaceId: string): string {
    if (this.disposed) throw new ServerBackendError('CLIENT_DISPOSED', 'The server gateway has been disposed.');
    if (!/^[a-f0-9]{32}$/.test(workspaceId)) {
      throw new ServerBackendError('INVALID_WORKSPACE_ID', 'Expected a canonical workspace GUID.');
    }
    return this.url(`/workspaces/${workspaceId}/archive`);
  }

  projectArchiveUrl(cleanupToken: string): string {
    if (this.disposed) throw new ServerBackendError('CLIENT_DISPOSED', 'The server gateway has been disposed.');
    if (!/^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/.test(cleanupToken)) {
      throw new ServerBackendError('INVALID_REQUEST', 'Expected a canonical project archive UUID.');
    }
    return this.url(`/project-archives/${cleanupToken}`);
  }

  async uploadMapSources(context: WorkbenchContext, files: File[], options?: Pick<BackendRequestOptions, 'signal'> & { directory?: boolean }): Promise<MapSourceUploadResult> {
    if (this.disposed) throw new ServerBackendError('CLIENT_DISPOSED', 'The server gateway has been disposed.');
    if (options?.signal?.aborted) throw abortError(options.signal.reason);
    if (!context.projectPath || !context.projectRevision || files.length === 0) {
      throw new ServerBackendError('INVALID_REQUEST', 'A saved project context and source files are required.');
    }
    const form = new FormData();
    form.set('projectPath', context.projectPath);
    form.set('projectRevision', context.projectRevision);
    form.set('projectSessionID', context.projectSessionID);
    for (const file of files) {
      const name = options?.directory ? file.webkitRelativePath : file.name;
      if (!name) throw new ServerBackendError('INVALID_REQUEST', 'Select a complete .gdb directory with relative file paths.');
      form.append('files', file, name);
    }
    const endpoint = options?.directory ? '/gdb-source-uploads' : '/source-uploads';
    const response = await this.fetchTimed(this.url(endpoint), {
      method: 'POST', body: form, credentials: 'same-origin', headers: { Accept: 'application/json' },
    }, uploadRequestTimeoutMilliseconds, options?.signal);
    const body = await this.readJson(response);
    if (!response.ok) throw this.httpError(response, body, 'POST ' + endpoint);
    const fail = () => new ServerBackendError('INVALID_SOURCE_UPLOAD_RESPONSE', 'The upload receipt does not match its saved project context.', body);
    if (!isRecord(body) || typeof body.uploadID !== 'string' || !/^[a-f0-9]{32}$/.test(body.uploadID) ||
        body.projectPath !== context.projectPath || body.projectRevision !== context.projectRevision ||
        body.projectSessionID !== context.projectSessionID || !Array.isArray(body.sources) || body.sources.length === 0) throw fail();
    const sources: UploadedMapSource[] = body.sources.map((source: unknown) => {
      if (!isRecord(source) || typeof source.sourcePath !== 'string' || !source.sourcePath ||
          typeof source.sourceCanonicalPath !== 'string' || !source.sourceCanonicalPath ||
          typeof source.sourceRevision !== 'string' || !/^[a-f0-9]{64}$/i.test(source.sourceRevision) ||
          typeof source.fileName !== 'string' || !(options?.directory ? source.kind === 'gdb' : ['geojson', 'shapefile', 'raster'].includes(String(source.kind)))) throw fail();
      return { sourcePath: source.sourcePath, sourceCanonicalPath: source.sourceCanonicalPath, sourceRevision: source.sourceRevision,
        fileName: source.fileName, kind: source.kind as UploadedMapSource['kind'] };
    });
    return { uploadID: body.uploadID, projectPath: body.projectPath as string, projectRevision: body.projectRevision as string,
      projectSessionID: body.projectSessionID as string, sources };
  }

  dispose(): void {
    this.disposed = true;
    this.connectGeneration++;
    this.lifetime.abort(abortError('The server gateway was disposed.'));
    this.listeners.clear();
    this.operations.clear();
  }

  private async runOperation<T>(method: string, parameters: unknown, options?: BackendRequestOptions): Promise<T> {
    let requestBody: string;
    try { requestBody = JSON.stringify({ method, parameters: parameters ?? {} }); }
    catch (cause) {
      throw new ServerBackendError('INVALID_REQUEST', errorDescription(cause), { operation: method, cause }, { operation: method, cause });
    }
    let response: Response;
    try {
      response = await this.fetchTimed(this.url('/operations'), {
        method: 'POST',
        credentials: 'same-origin',
        headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
        body: requestBody,
      }, operationRequestTimeoutMilliseconds);
    } catch (error) {
      if (isUncertainAcceptanceError(error)) throw unconfirmedAcceptanceError(method, error);
      throw error;
    }
    let accepted: unknown;
    try { accepted = await this.readJson(response); }
    catch (error) {
      if (isUncertainAcceptanceError(error)) throw unconfirmedAcceptanceError(method, error);
      throw error;
    }
    if (response.status !== 202) {
      if (!response.ok) {
        const error = this.httpError(response, accepted, 'POST /operations');
        if (isUncertainAcceptanceError(error)) throw unconfirmedAcceptanceError(method, error);
        throw error;
      }
      throw new ServerBackendError('OPERATION_STATUS_UNCONFIRMED', 'The server returned success without a trackable operation id.',
        { stage: 'acceptance', method, status: response.status, response: accepted }, { operation: method, terminalConfirmed: false });
    }
    const id = readString(accepted, 'id');
    if (!id) {
      throw new ServerBackendError('OPERATION_STATUS_UNCONFIRMED', 'The server accepted an operation but did not return its tracking id.',
        { stage: 'acceptance', method, response: accepted }, { operation: method, terminalConfirmed: false });
    }

    return await this.trackOperation<T>(id, options);
  }

  private async trackOperation<T>(id: string, options?: BackendRequestOptions): Promise<T> {
    const signal = options?.signal;
    let cancelRequested = false;
    let trackingEnded = false;
    let cancelPump: Promise<void> | undefined;
    const cancelPumpController = new AbortController();
    const reportTransport = (state: BackendTransportState): void => {
      try { options?.onTransportState?.(state); }
      catch { /* Transport observers must not change operation semantics. */ }
    };
    reportTransport({ state: 'connected', operationId: id, recoveredAfterAttempt: 0 });
    const reportCancelFailure = (error: unknown): void => {
      const normalized = error instanceof ServerBackendError ? error : undefined;
      reportTransport({
        state: 'cancelFailed', operationId: id,
        code: normalized?.code ?? 'NETWORK_ERROR',
        diagnosticMessage: normalized?.diagnosticMessage ?? errorDescription(error),
      });
    };
    const sendCancel = (): Promise<void> => {
      if (cancelPump) return cancelPump;
      cancelPump = (async () => {
        let retryAttempt = 0;
        while (!trackingEnded && !this.disposed && !cancelPumpController.signal.aborted) {
          try {
            const cancelResponse = await this.fetchTimed(this.url(`/operations/${encodeURIComponent(id)}`), {
              method: 'DELETE', credentials: 'same-origin', headers: { Accept: 'application/json' },
            }, operationRequestTimeoutMilliseconds, cancelPumpController.signal);
            const cancelBody = await this.readJson(cancelResponse);
            if (!cancelResponse.ok) throw this.httpError(cancelResponse, cancelBody, 'DELETE /operations/{id}');
            if (!isRecord(cancelBody) || cancelBody.id !== id || typeof cancelBody.cancelRequested !== 'boolean') {
              reportCancelFailure(new ServerBackendError('INVALID_CANCEL_RESPONSE', 'The server returned an invalid cancellation acknowledgment.', cancelBody, { operation: id, operationId: id }));
              return;
            }
            // A successful response is delivery acknowledgment only. The operation poll remains authoritative.
            reportTransport({ state: 'cancelSent', operationId: id, accepted: cancelBody.cancelRequested });
            return;
          } catch (error) {
            if (trackingEnded || this.disposed || cancelPumpController.signal.aborted) return;
            if (!isRetryableTransportError(error)) {
              reportCancelFailure(error);
              return;
            }
            retryAttempt++;
            const retryInMilliseconds = backoffMilliseconds(cancelRetryBaseMilliseconds, cancelRetryMaximumMilliseconds, retryAttempt);
            const code = error instanceof ServerBackendError ? error.code : 'NETWORK_ERROR';
            reportTransport({ state: 'cancelRetrying', operationId: id, attempt: retryAttempt, retryInMilliseconds, code });
            await delay(retryInMilliseconds, cancelPumpController.signal);
          }
        }
      })();
      return cancelPump;
    };
    const onAbort = (): void => {
      if (cancelRequested) return;
      cancelRequested = true;
      reportTransport({ state: 'cancelRequested', operationId: id });
      void sendCancel().catch((error: unknown) => {
        // Terminal polling aborts the retry sleeper. That internal shutdown is not a cancellation failure.
        if (trackingEnded || this.disposed || cancelPumpController.signal.aborted || isAbortError(error)) return;
        reportCancelFailure(error);
      });
    };
    signal?.addEventListener('abort', onAbort, { once: true });
    if (signal?.aborted) onAbort();

    let cursor = 0;
    let lastSequence = -1;
    let consecutivePollFailures = 0;
    try {
      while (true) {
        if (this.disposed) throw unconfirmedStatusError(id, new ServerBackendError('CLIENT_DISPOSED', 'The server gateway closed before the operation terminal state was received.', undefined, { operation: id, operationId: id }), consecutivePollFailures);
        let poll: OperationPoll;
        try {
          const pollResponse = await this.fetchTimed(`${this.url(`/operations/${encodeURIComponent(id)}`)}?after=${cursor}`, {
            method: 'GET', credentials: 'same-origin', headers: { Accept: 'application/json' },
          }, operationRequestTimeoutMilliseconds);
          const pollBody = await this.readJson(pollResponse);
          if (!pollResponse.ok) throw this.httpError(pollResponse, pollBody, 'GET /operations/{id}');
          poll = parsePoll(pollBody, id);
        } catch (error) {
          if (this.disposed) throw unconfirmedStatusError(id, error, consecutivePollFailures);
          if (!isRetryableTransportError(error)) throw unconfirmedStatusError(id, error, consecutivePollFailures);
          consecutivePollFailures++;
          if (consecutivePollFailures > maximumConsecutivePollRetries) {
            throw unconfirmedStatusError(id, error, consecutivePollFailures - 1);
          }
          const retryInMilliseconds = backoffMilliseconds(pollRetryBaseMilliseconds, pollRetryMaximumMilliseconds, consecutivePollFailures);
          reportTransport({ state: 'reconnecting', operationId: id, attempt: consecutivePollFailures, retryInMilliseconds });
          await delay(retryInMilliseconds, this.lifetime.signal);
          continue;
        }

        if (consecutivePollFailures > 0) {
          reportTransport({ state: 'connected', operationId: id, recoveredAfterAttempt: consecutivePollFailures });
          consecutivePollFailures = 0;
        }

        for (const event of [...poll.events].sort((left, right) => left.sequence - right.sequence)) {
          if (event.sequence <= lastSequence) continue;
          lastSequence = event.sequence;
          options?.onProgress?.(event.progress);
        }
        const previousCursor = cursor;
        cursor = Math.max(cursor, poll.cursor, lastSequence);
        if (poll.hasMore && cursor <= previousCursor) {
          throw unconfirmedStatusError(id,
            new ServerBackendError('INVALID_OPERATION_RESPONSE', 'The server marked another progress page as available without advancing its cursor.', poll, { operation: id, operationId: id }),
            consecutivePollFailures);
        }

        if (terminalStates.has(poll.state) && !poll.hasMore) {
          if (poll.state === 'failed') throw operationError(poll.error, id);
          if (poll.state === 'completed' && !Object.hasOwn(poll, 'result')) {
            throw unconfirmedStatusError(id,
              new ServerBackendError('INVALID_OPERATION_RESPONSE', 'The server marked the operation completed but omitted its result.', poll, { operation: id, operationId: id }),
              consecutivePollFailures);
          }
          if (poll.state === 'canceled' && !Object.hasOwn(poll, 'result')) {
            throw Object.assign(abortError(signal?.reason), { operationId: id, terminalConfirmed: true });
          }
          return poll.result as T;
        }

        if (!poll.hasMore) await delay(pollIntervalMilliseconds, this.lifetime.signal);
      }
    } finally {
      trackingEnded = true;
      cancelPumpController.abort(abortError('The operation no longer needs cancellation retries.'));
      signal?.removeEventListener('abort', onAbort);
    }
  }

  private parseRuntime(value: unknown): ServerRuntimeInfo {
    if (!isRecord(value) || value.mode !== 'server' || !Array.isArray(value.operations)
      || !value.operations.every((operation) => typeof operation === 'string')) {
      throw new ServerBackendError('INVALID_RUNTIME_RESPONSE', 'The server returned an invalid /api/runtime response.', value);
    }
    return { mode: 'server', runtime: value.runtime, operations: [...new Set(value.operations as string[])] };
  }

  private parseUploadResult(value: unknown): WorkspaceUploadResult {
    if (!isRecord(value) || typeof value.workspaceId !== 'string' || typeof value.rootPath !== 'string'
      || !Number.isInteger(value.fileCount) || !Array.isArray(value.projects)) {
      throw new ServerBackendError('INVALID_UPLOAD_RESPONSE', 'The server returned an invalid workspace upload response.', value);
    }
    const projects = value.projects.map((project) => {
      if (!isRecord(project) || typeof project.path !== 'string' || typeof project.name !== 'string') {
        throw new ServerBackendError('INVALID_UPLOAD_RESPONSE', 'The server returned an invalid project entry.', project);
      }
      return { path: project.path, name: project.name };
    });
    return { workspaceId: value.workspaceId, rootPath: value.rootPath, projects, fileCount: value.fileCount as number };
  }

  private async readJson(response: Response): Promise<unknown> {
    let responseText: string;
    try { responseText = await response.text(); }
    catch (cause) {
      throw new ServerBackendError('NETWORK_ERROR', errorDescription(cause), { cause }, { cause });
    }
    if (!responseText) return undefined;
    const contentType = response.headers.get('content-type')?.toLowerCase() ?? '';
    if (contentType.includes('json')) {
      try { return JSON.parse(responseText) as unknown; }
      catch (cause) {
        return { message: responseText, rawResponse: responseText, parseError: cause instanceof Error ? cause.message : String(cause) };
      }
    }
    return { message: responseText, rawResponse: responseText };
  }

  private httpError(response: Response, body: unknown, operation?: string): ServerBackendError {
    const detail = isRecord(body) ? body : undefined;
    const nested = detail && isRecord(detail.error) ? detail.error : undefined;
    const diagnosticMessage = firstString(nested?.message, detail?.message, detail?.title, response.statusText) ?? `HTTP ${response.status}`;
    const code = firstString(nested?.code, detail?.code) ?? `HTTP_${response.status}`;
    return new ServerBackendError(code, diagnosticMessage, { status: response.status, statusText: response.statusText, body }, { status: response.status, operation });
  }

  private url(path: string): string {
    const base = this.apiBase.endsWith('/') ? this.apiBase.slice(0, -1) : this.apiBase;
    return `${base}${path}`;
  }

  private publishCapabilities(): void {
    this.revision++;
    for (const listener of this.listeners) listener(this.revision);
  }

  private async fetchTimed(input: RequestInfo | URL, init: RequestInit, timeoutMilliseconds: number, externalSignal?: AbortSignal): Promise<Response> {
    const controller = new AbortController();
    const signals = [this.lifetime.signal, externalSignal].filter((signal): signal is AbortSignal => signal !== undefined);
    const abort = (signal: AbortSignal): void => {
      if (!controller.signal.aborted) controller.abort(signal.reason ?? abortError());
    };
    const listeners = signals.map((signal) => {
      const handler = (): void => abort(signal);
      signal.addEventListener('abort', handler, { once: true });
      if (signal.aborted) handler();
      return { signal, handler };
    });
    const timer = globalThis.setTimeout(() => {
      if (!controller.signal.aborted) controller.abort(new DOMException('The server request timed out.', 'TimeoutError'));
    }, timeoutMilliseconds);
    try {
      return await this.fetcher.call(globalThis, input, { ...init, signal: controller.signal });
    } catch (cause) {
      if (externalSignal?.aborted || this.lifetime.signal.aborted) {
        throw abortError(externalSignal?.reason ?? this.lifetime.signal.reason);
      }
      const reason = controller.signal.reason;
      const requestOperation = requestLabel(input, init);
      if (reason instanceof DOMException && reason.name === 'TimeoutError') {
        throw new ServerBackendError('REQUEST_TIMEOUT', reason.message, { operation: requestOperation, cause }, { operation: requestOperation, cause });
      }
      if (cause instanceof TypeError || cause instanceof DOMException && cause.name === 'NetworkError') {
        throw new ServerBackendError('NETWORK_ERROR', errorDescription(cause), { operation: requestOperation, cause }, { operation: requestOperation, cause });
      }
      throw new ServerBackendError('NETWORK_ERROR', errorDescription(cause), { operation: requestOperation, cause }, { operation: requestOperation, cause });
    } finally {
      globalThis.clearTimeout(timer);
      for (const { signal, handler } of listeners) signal.removeEventListener('abort', handler);
    }
  }
}

function parsePoll(value: unknown, expectedId: string): OperationPoll {
  if (!isRecord(value) || value.id !== expectedId || !['queued', 'running', 'completed', 'failed', 'canceled'].includes(String(value.state))
    || !Number.isSafeInteger(value.cursor) || (value.cursor as number) < 0 || typeof value.hasMore !== 'boolean' || !Array.isArray(value.events)) {
    throw new ServerBackendError('INVALID_OPERATION_RESPONSE', `The server returned an invalid operation poll for ${expectedId}.`, value);
  }
  const events = value.events.map((entry) => {
    if (!isRecord(entry) || !Number.isSafeInteger(entry.sequence) || (entry.sequence as number) < 0 || !('progress' in entry)) {
      throw new ServerBackendError('INVALID_OPERATION_EVENT', `The server returned an invalid progress event for ${expectedId}.`, entry);
    }
    return { sequence: entry.sequence as number, progress: entry.progress };
  });
  return {
    id: expectedId,
    state: value.state as OperationPoll['state'],
    cursor: value.cursor as number,
    hasMore: value.hasMore,
    events,
    ...(Object.hasOwn(value, 'result') ? { result: value.result } : {}),
    ...(Object.hasOwn(value, 'error') ? { error: value.error } : {}),
  };
}

function operationError(error: unknown, id: string): ServerBackendError {
  if (isRecord(error)) {
    const diagnosticMessage = firstString(error.message, error.title) ?? `Operation ${id} failed.`;
    return new ServerBackendError(firstString(error.code) ?? 'OPERATION_FAILED', diagnosticMessage, error, {
      operation: id, operationId: id, terminalConfirmed: true, cause: error,
    });
  }
  const diagnosticMessage = typeof error === 'string' ? error : `Operation ${id} failed.`;
  return new ServerBackendError('OPERATION_FAILED', diagnosticMessage, error, {
    operation: id, operationId: id, terminalConfirmed: true, cause: error,
  });
}

function unconfirmedStatusError(id: string, cause: unknown, retryAttempts: number): ServerBackendError {
  const diagnostic = cause instanceof ServerBackendError
    ? {
      code: cause.code,
      message: cause.diagnosticMessage,
      status: cause.status,
      operation: cause.operation,
      details: cause.details,
    }
    : { message: errorDescription(cause) };
  const status = cause instanceof ServerBackendError ? cause.status : undefined;
  return new ServerBackendError(
    'OPERATION_STATUS_UNCONFIRMED',
    `Could not confirm the terminal state of operation ${id}.`,
    { operationId: id, retryAttempts, lastError: diagnostic },
    { operation: id, operationId: id, terminalConfirmed: false, status, cause },
  );
}

function unconfirmedAcceptanceError(method: string, cause: unknown): ServerBackendError {
  const diagnostic = cause instanceof ServerBackendError
    ? { code: cause.code, message: cause.diagnosticMessage, status: cause.status, details: cause.details }
    : { message: errorDescription(cause) };
  return new ServerBackendError(
    'OPERATION_STATUS_UNCONFIRMED',
    `Could not determine whether operation ${method} was accepted by the server.`,
    { stage: 'acceptance', method, lastError: diagnostic },
    {
      operation: method,
      status: cause instanceof ServerBackendError ? cause.status : undefined,
      terminalConfirmed: false,
      cause,
    },
  );
}

function isRetryableTransportError(error: unknown): boolean {
  if (!(error instanceof ServerBackendError)) return false;
  if (error.code === 'NETWORK_ERROR' || error.code === 'REQUEST_TIMEOUT') return true;
  return error.status === 408 || error.status === 429 || error.status !== undefined && error.status >= 500;
}

function isUncertainAcceptanceError(error: unknown): boolean {
  if (!(error instanceof ServerBackendError)) return false;
  if (error.code === 'NETWORK_ERROR' || error.code === 'REQUEST_TIMEOUT') return true;
  return error.status === 408 || error.status !== undefined && error.status >= 500;
}

function backoffMilliseconds(baseMilliseconds: number, maximumMilliseconds: number, attempt: number): number {
  return Math.min(maximumMilliseconds, baseMilliseconds * 2 ** Math.max(0, attempt - 1));
}

function requestLabel(input: RequestInfo | URL, init: RequestInit): string {
  const request = typeof input === 'object' && input !== null && 'url' in input ? input as Request : undefined;
  const rawUrl = request?.url ?? String(input);
  let path = rawUrl;
  try { path = new URL(rawUrl, globalThis.location?.origin ?? 'http://localhost').pathname; }
  catch { /* Keep the supplied value for diagnostics if it is not a parseable URL. */ }
  return `${(init.method ?? request?.method ?? 'GET').toUpperCase()} ${path}`;
}

function errorDescription(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

function abortError(reason?: unknown): DOMException {
  if (reason instanceof DOMException && reason.name === 'AbortError') return reason;
  const message = reason instanceof Error ? reason.message : typeof reason === 'string' ? reason : 'The operation was canceled.';
  return new DOMException(message, 'AbortError');
}

function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError'
    || error instanceof Error && error.name === 'AbortError';
}

function delay(milliseconds: number, signal?: AbortSignal): Promise<void> {
  return new Promise((resolve) => {
    if (signal?.aborted) { resolve(); return; }
    let timer: ReturnType<typeof globalThis.setTimeout> | undefined;
    const finish = (): void => {
      if (timer !== undefined) globalThis.clearTimeout(timer);
      signal?.removeEventListener('abort', finish);
      resolve();
    };
    timer = globalThis.setTimeout(finish, milliseconds);
    signal?.addEventListener('abort', finish, { once: true });
  });
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function readString(value: unknown, key: string): string | undefined {
  return isRecord(value) && typeof value[key] === 'string' ? value[key] as string : undefined;
}

function firstString(...values: unknown[]): string | undefined {
  return values.find((value): value is string => typeof value === 'string' && value.length > 0);
}

function sameSet(left: Set<string>, right: Set<string>): boolean {
  return left.size === right.size && [...left].every((value) => right.has(value));
}
