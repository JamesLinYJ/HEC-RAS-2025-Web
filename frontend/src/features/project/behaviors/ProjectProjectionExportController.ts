import type { BackendGateway, BackendRequestOptions } from '../../../shared/workbench';
import { BackendOperationUnavailableError } from '../../../shared/workbench';
import { record } from '../../../shared/sessionReferences';
import { captureProjectProjection, parseProjectProjectionFile, parseProjectProjectionReceipt, projectProjectionExportMethod,
  projectProjectionOwner, type ProjectProjectionCapture, type ProjectProjectionFile, type ProjectProjectionReceipt,
  type ProjectProjectionSource } from './projectProjectionExport';

export type ProjectProjectionPhase = 'running' | 'unknown' | 'verifying' | 'completed' | 'readback-failed' | 'failed';
export interface ProjectProjectionTask {
  capture: ProjectProjectionCapture; phase: ProjectProjectionPhase;
  httpOperationID?: string; receipt?: ProjectProjectionReceipt; file?: ProjectProjectionFile; error?: unknown;
}
export interface ProjectProjectionState { task?: ProjectProjectionTask; validationError?: unknown }
interface Job { capture: ProjectProjectionCapture; backend: BackendGateway; stillCurrent: () => boolean; busy: boolean }

/** A single write remains owned until its original HTTP operation reaches a known terminal state. */
export class ProjectProjectionExportController {
  private state: ProjectProjectionState = {};
  private listeners = new Set<() => void>();
  private job: Job | null = null;
  constructor(private readonly dirty: (owner: string, active: boolean) => void) {}
  getSnapshot = (): ProjectProjectionState => this.state;
  subscribe = (listener: () => void): (() => void) => { this.listeners.add(listener); return () => { this.listeners.delete(listener); }; };
  get pending(): boolean { return !!this.state.task && ['running', 'unknown', 'verifying'].includes(this.state.task.phase); }
  private publish(state: ProjectProjectionState): void { this.state = state; this.listeners.forEach(listener => listener()); }
  private patch(patch: Partial<ProjectProjectionTask>): void {
    if (this.state.task) this.publish({ task: { ...this.state.task, ...patch } });
  }
  private finish(job: Job, phase: ProjectProjectionPhase, patch: Partial<ProjectProjectionTask> = {}): void {
    job.busy = false; this.patch({ ...patch, phase }); this.dirty(projectProjectionOwner(job.capture.id), false);
  }
  async start(source: ProjectProjectionSource, backend: BackendGateway, stillCurrent: () => boolean, blocked: boolean): Promise<void> {
    if (this.pending) return;
    let capture: ProjectProjectionCapture;
    try {
      if (typeof this.dirty !== 'function') throw new Error('当前界面缺少工程写入保护，不能导出。');
      if (blocked) throw new Error('存在未保存编辑或正在进行的写入，请先完成后再导出投影。');
      if (!backend.supports(projectProjectionExportMethod) || !backend.supports('workspace.file.inspect'))
        throw new Error('当前 C# 服务未提供投影导出及文件读回能力。');
      if (!stillCurrent()) throw new Error('工程来源已变化，请重新读取当前工程。');
      capture = captureProjectProjection(source, crypto.randomUUID());
    } catch (error) { this.publish({ ...this.state, validationError: error }); return; }
    const job: Job = { capture, backend, stillCurrent, busy: true };
    this.job = job;
    this.dirty(projectProjectionOwner(capture.id), true);
    this.publish({ task: { capture, phase: 'running' } });
    await this.track(job, options => backend.request(projectProjectionExportMethod, capture.request, options));
  }
  canResume = (): boolean => this.state.task?.phase === 'unknown' && !!this.state.task.httpOperationID &&
    !!this.job?.backend.resumeOperation && !this.job.busy;
  resume = async (): Promise<void> => {
    const job = this.job, id = this.state.task?.httpOperationID;
    if (!job || !id || !this.canResume()) return;
    job.busy = true; this.patch({ phase: 'running', error: undefined });
    await this.track(job, options => job.backend.resumeOperation!(id, options));
  };
  retryReadback = async (): Promise<void> => {
    const job = this.job, receipt = this.state.task?.receipt;
    if (!job || !receipt || job.busy || this.state.task?.phase !== 'readback-failed') return;
    job.busy = true; this.dirty(projectProjectionOwner(job.capture.id), true);
    await this.readback(job, receipt);
  };
  private async track(job: Job, request: (options: BackendRequestOptions) => Promise<unknown>): Promise<void> {
    let raw: unknown;
    try {
      raw = await request({ onTransportState: state => {
        if (!job.busy || this.job !== job) return;
        const previous = this.state.task?.httpOperationID;
        if (!previous || previous === state.operationId) this.patch({ httpOperationID: state.operationId });
      } });
    } catch (error) {
      const metadata = record(error) ? error : {};
      const httpOperationID = this.state.task?.httpOperationID ?? (typeof metadata.operationId === 'string' ? metadata.operationId : undefined);
      const patch = { error, httpOperationID };
      if (metadata.terminalConfirmed === true || error instanceof BackendOperationUnavailableError ||
          (metadata.terminalConfirmed !== false && (metadata.code === 'INVALID_REQUEST' || metadata.code === 'CLIENT_DISPOSED' ||
            (metadata.operation === 'POST /operations' && typeof metadata.status === 'number' && metadata.status >= 400 && metadata.status < 500 && metadata.status !== 408)))) {
        this.finish(job, 'failed', patch);
      } else {
        job.busy = false; this.patch({ ...patch, phase: 'unknown' });
      }
      return;
    }
    // A resolved gateway request has a confirmed terminal result. Bad receipts cannot become success or be reposted as retries.
    try {
      const receipt = parseProjectProjectionReceipt(raw, job.capture);
      if (!job.stillCurrent()) throw new Error('工程会话、XML、修订或外部来源已变化，旧导出回执未应用到当前工程。');
      await this.readback(job, receipt);
    } catch (error) { this.finish(job, 'failed', { error, receipt: undefined, file: undefined }); }
  }
  private async readback(job: Job, receipt: ProjectProjectionReceipt): Promise<void> {
    this.patch({ phase: 'verifying', receipt, error: undefined, file: undefined });
    try {
      if (!job.stillCurrent()) throw new Error('工程来源已变化，不能向当前工程应用旧文件读回。');
      const raw = await job.backend.request('workspace.file.inspect', { sourcePath: receipt.outputPath });
      const file = parseProjectProjectionFile(raw, receipt);
      if (!job.stillCurrent()) throw new Error('文件读回期间工程来源已变化，结果未应用。');
      this.finish(job, 'completed', { receipt, file, error: undefined });
    } catch (error) {
      if (!job.stillCurrent()) this.finish(job, 'failed', { error, receipt: undefined, file: undefined });
      else this.finish(job, 'readback-failed', { error, receipt, file: undefined });
    }
  }
}
