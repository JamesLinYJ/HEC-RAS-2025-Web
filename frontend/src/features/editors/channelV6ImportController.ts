import type { BackendGateway, BackendRequestOptions } from '../../shared/workbench';
import {
  channelV6CaptureKey, channelV6ImportMethods, channelV6ImportRequest, channelV6OperationID,
  channelV6SourcePathIssue, channelV6TerminalConfirmed, isChannelV6ImportCancelled,
  parseChannelV6ImportCleanup, parseChannelV6ImportCompleted, parseChannelV6ImportDiscarded,
  parseChannelV6ImportFile, parseChannelV6ImportProgress, validateChannelV6ImportCapture,
} from './channelV6ImportProtocol';
import type {
  ChannelV6ImportBindings, ChannelV6ImportCapture, ChannelV6ImportCleanup, ChannelV6ImportInspection,
  ChannelV6ImportPhase, ChannelV6ImportState, ChannelV6ImportTask,
} from './channelV6ImportTypes';

interface ImportJob {
  inspection: ChannelV6ImportInspection;
  parentCapture: ChannelV6ImportCapture;
  controller: AbortController;
  busy: boolean;
  released: boolean;
  cleanup: ChannelV6ImportCleanup | null;
  rawReceipt?: unknown;
  finalPhase: 'cancelled' | 'not-adopted' | 'failed';
}

/** The parent owns this controller. Page unmounts cannot turn an unknown write into a new POST. */
export class ChannelV6ImportController {
  private state: ChannelV6ImportState = { sourcePaths: {}, inspection: null, inspecting: false, task: null };
  private readonly listeners = new Set<() => void>();
  private read: { generation: number; controller: AbortController | null } = { generation: 0, controller: null };
  private job: ImportJob | null = null;
  private inspectionKey: string | null = null;
  private inspectionBackend: BackendGateway | null = null;

  constructor(private readonly bindings: ChannelV6ImportBindings) {}
  getSnapshot = (): ChannelV6ImportState => this.state;
  subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener);
    return () => { this.listeners.delete(listener); };
  };
  get pending(): boolean { return !!this.job && !this.job.released; }
  get isTracking(): boolean { return this.job?.busy === true; }
  sourcePath = (owner: string, inputId: string): string => this.state.sourcePaths[JSON.stringify([owner, inputId])] ?? '';

  private publish(patch: Partial<ChannelV6ImportState>): void {
    this.state = { ...this.state, ...patch };
    this.listeners.forEach(listener => listener());
  }
  private patchTask(patch: Partial<ChannelV6ImportTask>): void {
    if (this.state.task) this.publish({ task: { ...this.state.task, ...patch } });
  }
  setSourcePath = (owner: string, inputId: string, path: string): void => {
    if (this.pending) return;
    this.invalidateInspection();
    this.publish({ sourcePaths: { ...this.state.sourcePaths, [JSON.stringify([owner, inputId])]: path } });
  };
  invalidateInspection = (): void => {
    this.read.generation++;
    this.read.controller?.abort();
    this.read.controller = null;
    this.inspectionKey = null;
    this.inspectionBackend = null;
    this.publish({ inspecting: false, inspection: null, validationError: undefined });
  };
  invalidateScope = (): void => { this.invalidateInspection(); this.cancel(); };
  dispose = (): void => { this.invalidateScope(); };

  inspect = async (inputId: string, rawSourcePath: string): Promise<void> => {
    if (this.pending) return;
    this.invalidateInspection();
    const issue = channelV6SourcePathIssue(rawSourcePath);
    if (issue) { this.publish({ validationError: new Error(issue) }); return; }
    const generation = this.read.generation;
    const controller = new AbortController(); this.read.controller = controller;
    const current = (): boolean => this.read.generation === generation && !controller.signal.aborted;
    this.publish({ inspecting: true });
    try {
      const capture = await this.bindings.capture(inputId, controller.signal);
      if (!current()) return;
      if (!capture) throw new Error('当前河道或父地形草稿尚未准备好，请重新读取后检查来源。');
      validateChannelV6ImportCapture(capture, inputId);
      if (!this.bindings.isCurrent(capture)) throw new Error('检查前父地形草稿已变化，请重新检查来源。');
      const key = channelV6CaptureKey(capture);
      const raw = await capture.backend.request(channelV6ImportMethods.inspect,
        { sourcePath: rawSourcePath.trim() }, { signal: controller.signal });
      if (!current()) return;
      const file = parseChannelV6ImportFile(raw, rawSourcePath);
      if (!this.bindings.isCurrent(capture) || key !== channelV6CaptureKey(capture))
        throw new Error('检查期间工程会话、服务、河道或父草稿发生变化，请重新检查。');
      this.inspectionKey = key;
      this.inspectionBackend = capture.backend;
      this.publish({ inspection: { capture, rawSourcePath, file }, validationError: undefined });
    } catch (error) {
      if (current()) this.publish({ inspection: null, validationError: error });
    } finally {
      if (this.read.generation === generation) { this.read.controller = null; this.publish({ inspecting: false }); }
    }
  };

  canStart = (owner?: string, inputId?: string, rawSourcePath?: string): boolean => {
    const inspection = this.state.inspection;
    return !this.pending && !this.state.inspecting && !!inspection
      && (owner === undefined || inspection.capture.owner === owner)
      && (inputId === undefined || inspection.capture.inputId === inputId)
      && (rawSourcePath === undefined || inspection.rawSourcePath === rawSourcePath)
      && inspection.capture.backend === this.inspectionBackend
      && this.inspectionKey === channelV6CaptureKey(inspection.capture) && this.bindings.isCurrent(inspection.capture);
  };

  start = async (): Promise<void> => {
    if (this.pending) return;
    const inspection = this.state.inspection;
    if (!inspection || !this.canStart()) {
      this.publish({ validationError: new Error('来源检查已过期，请重新检查当前 HDF 文件和父地形草稿。') }); return;
    }
    let request: ReturnType<typeof channelV6ImportRequest>;
    try {
      request = channelV6ImportRequest(inspection);
      if (!this.bindings.acquire(inspection.capture)) throw new Error('当前父地形操作尚未结束，无法开始导入。');
    } catch (error) { this.publish({ validationError: error }); return; }
    const detached: ChannelV6ImportInspection = { ...inspection, file: { ...inspection.file }, capture: {
      ...inspection.capture, model: structuredClone(inspection.capture.model), inputIds: [...inspection.capture.inputIds],
    } };
    const job: ImportJob = { inspection: detached, parentCapture: inspection.capture, controller: new AbortController(), busy: false, released: false,
      cleanup: null, finalPhase: 'not-adopted' };
    this.job = job;
    this.publish({ validationError: undefined, task: { inspection: detached, phase: 'running', httpOperationID: null,
      cleanupOperationID: null, cancelRequested: false, progress: null, transport: null, receipt: null } });
    await this.track(job, options => detached.capture.backend.request(channelV6ImportMethods.import, request, options));
  };

  canResume = (): boolean => this.state.task?.phase === 'unknown' && !!this.state.task.httpOperationID
    && !!this.job?.inspection.capture.backend.resumeOperation && !this.job.busy;
  resume = async (): Promise<void> => {
    const job = this.job, id = this.state.task?.httpOperationID;
    if (!job || !id || !this.canResume()) return;
    job.controller = new AbortController();
    const cancelRequested = this.state.task?.cancelRequested;
    // The gateway rejects an already-aborted signal before registering its cancellation pump.
    const tracking = this.track(job, options => job.inspection.capture.backend.resumeOperation!(id, options));
    if (cancelRequested) job.controller.abort();
    await tracking;
  };
  cancel = (): void => {
    if (this.state.inspecting) this.invalidateInspection();
    const job = this.job;
    if (!job || job.released || !this.state.task || !['running', 'unknown'].includes(this.state.task.phase)) return;
    this.patchTask({ cancelRequested: true });
    job.controller.abort();
    if (!job.busy && this.canResume()) void this.resume();
  };

  private finish(job: ImportJob, phase: ChannelV6ImportPhase, patch: Partial<ChannelV6ImportTask> = {}): void {
    job.busy = false;
    if (job.released || this.job !== job) return;
    job.released = true;
    // Terminal state and ownership release are synchronous; accepted payloads belong solely to the parent.
    this.patchTask({ ...patch, phase });
    try { this.bindings.release(job.parentCapture); }
    catch (error) { this.patchTask({ error }); }
    this.inspectionKey = null;
    this.publish({ inspection: null });
  }

  private operationID(id: string | null, cleanup = false): void {
    if (!id || !this.state.task) return;
    const previous = cleanup ? this.state.task.cleanupOperationID : this.state.task.httpOperationID;
    if (previous && previous !== id) return;
    this.patchTask(cleanup ? { cleanupOperationID: id } : { httpOperationID: id });
  }

  private async track(job: ImportJob, request: (options: BackendRequestOptions) => Promise<unknown>): Promise<void> {
    if (job.busy || job.released) return;
    job.busy = true;
    this.patchTask({ phase: 'running', error: undefined, transport: null });
    let raw: unknown;
    try {
      raw = await request({ signal: job.controller.signal,
        onProgress: value => {
          if (this.job === job && job.busy) this.patchTask({ progress: parseChannelV6ImportProgress(value) });
        },
        onTransportState: value => {
          if (this.job !== job || !job.busy) return;
          this.operationID(value.operationId);
          this.patchTask({ transport: value });
        },
      });
    } catch (error) {
      this.operationID(channelV6OperationID(error));
      if (channelV6TerminalConfirmed(error)) {
        this.finish(job, error instanceof Error && error.name === 'AbortError' ? 'cancelled' : 'failed', { error });
      } else {
        job.busy = false; this.patchTask({ phase: 'unknown', error });
      }
      return;
    }
    job.rawReceipt = raw;
    if (isChannelV6ImportCancelled(raw)) { this.finish(job, 'cancelled'); return; }
    try {
      job.cleanup = parseChannelV6ImportCleanup(raw, job.inspection);
      const receipt = parseChannelV6ImportCompleted(raw, job.inspection);
      this.patchTask({ receipt });
      if (this.state.task?.cancelRequested || job.controller.signal.aborted) {
        job.finalPhase = 'cancelled';
      } else if (this.bindings.isCurrent(job.parentCapture)
          && this.bindings.accept(job.parentCapture, receipt)) {
        this.finish(job, 'completed'); return;
      } else {
        job.finalPhase = 'not-adopted';
        this.patchTask({ error: new Error('工程、服务、河道或父草稿已变化，此回执未接纳到当前地形。') });
      }
    } catch (error) {
      job.finalPhase = 'failed';
      this.patchTask({ error });
    }
    job.busy = false;
    await this.cleanup(job, false);
  }

  canRetryCleanup = (): boolean => !!this.job && !this.job.busy && !this.job.released
    && !!this.job.cleanup && (this.state.task?.phase === 'cleanup-failed'
      || (this.state.task?.phase === 'cleanup-unknown' && !!this.state.task.cleanupOperationID
        && !!this.job.inspection.capture.backend.resumeOperation));
  retryCleanup = async (): Promise<void> => {
    if (!this.job || !this.canRetryCleanup()) return;
    await this.cleanup(this.job, this.state.task?.phase === 'cleanup-unknown');
  };

  private async cleanup(job: ImportJob, resume: boolean): Promise<void> {
    if (job.busy || job.released) return;
    if (!job.cleanup) {
      this.patchTask({ phase: 'cleanup-failed', cleanupError: new Error('服务回执缺少可核验的精确清理身份，暂存归属仍被保留。') });
      return;
    }
    job.busy = true;
    this.patchTask({ phase: 'cleaning', cleanupError: undefined, ...(!resume ? { cleanupOperationID: null } : {}) });
    const capture: ChannelV6ImportCapture = job.inspection.capture;
    let raw: unknown;
    try {
      const options: BackendRequestOptions = { onTransportState: value => {
        if (this.job === job && job.busy) this.operationID(value.operationId, true);
      } };
      raw = resume
        ? await capture.backend.resumeOperation!(this.state.task!.cleanupOperationID!, options)
        : await capture.backend.request(channelV6ImportMethods.discard,
          { model: structuredClone(capture.model), cleanup: { ...job.cleanup } }, options);
    } catch (cleanupError) {
      this.operationID(channelV6OperationID(cleanupError), true);
      job.busy = false;
      this.patchTask({ phase: channelV6TerminalConfirmed(cleanupError) ? 'cleanup-failed' : 'cleanup-unknown', cleanupError });
      return;
    }
    try {
      parseChannelV6ImportDiscarded(raw, job.cleanup);
      this.finish(job, job.finalPhase, { cleanupError: undefined });
    } catch (cleanupError) {
      job.busy = false; this.patchTask({ phase: 'cleanup-failed', cleanupError });
    }
  }
}
