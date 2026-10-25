import { BackendOperationUnavailableError } from '../../shared/workbench';
import type { BackendGateway, BackendRequestOptions, BackendTransportState } from '../../shared/workbench';
import { captureMeshExport, hashMeshExportCapture, meshShapefileExportMethod, parseMeshExportReceipt,
  type MeshExportCapture, type MeshExportForm, type MeshExportSource, type MeshShapefileExportReceipt } from './meshShapefileExport';

export type MeshExportPhase = 'preparing' | 'running' | 'cancelling' | 'unknown' | 'completed' | 'cancelled' | 'failed';
export interface MeshExportTask {
  capture: MeshExportCapture;
  phase: MeshExportPhase;
  dispatched: boolean;
  cancelRequested: boolean;
  httpOperationID?: string;
  transport?: BackendTransportState;
  progress?: { fraction?: number; taskName: string; message: string };
  receipt?: MeshShapefileExportReceipt;
  error?: unknown;
}
export interface MeshExportControllerState { tasks: MeshExportTask[]; validationError?: unknown }
interface Job {
  capture: MeshExportCapture;
  backend: BackendGateway;
  controller: AbortController;
  settled: boolean;
  trackingGeneration: number;
}
const unresolved = new Set<MeshExportPhase>(['preparing', 'running', 'cancelling', 'unknown']);
export const meshExportTaskPending = (task: MeshExportTask): boolean => unresolved.has(task.phase);
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null;

/** Owns writes independently of the selected source and of the Mesh page's mount lifetime. */
export class MeshShapefileExportController {
  private state: MeshExportControllerState = { tasks: [] };
  private readonly listeners = new Set<() => void>();
  private job: Job | null = null;
  constructor(private readonly dirty: (owner: string, value: boolean) => void,
    private readonly hash = hashMeshExportCapture) {}

  getSnapshot = (): MeshExportControllerState => this.state;
  subscribe = (listener: () => void): (() => void) => { this.listeners.add(listener); return () => this.listeners.delete(listener); };
  get pending(): boolean { return this.state.tasks.some(meshExportTaskPending); }
  private publish(state: MeshExportControllerState): void { this.state = state; for (const listener of this.listeners) listener(); }
  private owner(id: string): string { return `geometry.mesh-export:${id}`; }
  private task(id: string): MeshExportTask | undefined { return this.state.tasks.find(task => task.capture.request.operationID === id); }
  private patch(id: string, patch: Partial<MeshExportTask>): void {
    this.publish({ ...this.state, tasks: this.state.tasks.map(task => task.capture.request.operationID === id ? { ...task, ...patch } : task) });
  }
  private terminal(job: Job, phase: 'completed' | 'cancelled' | 'failed', patch: Partial<MeshExportTask> = {}): void {
    const id = job.capture.request.operationID;
    job.settled = true;
    this.patch(id, { ...patch, phase });
    this.dirty(this.owner(id), false);
    if (this.job === job) this.job = null;
  }

  async start(source: MeshExportSource, form: MeshExportForm, backend: BackendGateway, stillCurrent: () => boolean): Promise<void> {
    if (this.pending) return;
    let capture: MeshExportCapture;
    try {
      if (typeof this.dirty !== 'function') throw new Error('当前界面缺少工作区写入保护，不能发起导出。');
      if (!backend.supports(meshShapefileExportMethod)) throw new Error('当前服务暂不提供网格 Shapefile 导出。');
      capture = captureMeshExport(source, form, crypto.randomUUID());
    } catch (error) { this.publish({ ...this.state, validationError: error }); return; }
    const job: Job = { capture, backend, controller: new AbortController(), settled: false, trackingGeneration: 0 };
    this.job = job;
    // Register synchronously before the first await: project navigation must see the outstanding write.
    this.dirty(this.owner(capture.request.operationID), true);
    this.publish({ tasks: [...this.state.tasks, { capture, phase: 'preparing', dispatched: false, cancelRequested: false }] });
    try {
      job.capture = await this.hash(capture);
      this.patch(capture.request.operationID, { capture: job.capture });
      if (job.controller.signal.aborted) { this.terminal(job, 'cancelled'); return; }
      if (!stillCurrent()) throw new Error('准备导出期间来源或可用状态发生变化，请重新检查当前网格；请求尚未发送。');
    } catch (error) { this.terminal(job, 'failed', { error }); return; }
    this.patch(capture.request.operationID, { phase: 'running', dispatched: true });
    await this.track(job, options => backend.request<unknown>(meshShapefileExportMethod, job.capture.request, options));
  }

  cancel = (): void => {
    const job = this.job;
    if (!job || job.settled || job.controller.signal.aborted) return;
    this.patch(job.capture.request.operationID, { phase: 'cancelling', cancelRequested: true });
    job.controller.abort();
    // Abort is a cancellation request. The gateway continues polling until a terminal response.
  };

  canResume = (): boolean => !!this.job?.settled && !!this.task(this.job.capture.request.operationID)?.httpOperationID &&
    typeof this.job.backend.resumeOperation === 'function';

  resume = async (): Promise<void> => {
    const job = this.job;
    if (!job || !this.canResume()) return;
    const task = this.task(job.capture.request.operationID)!;
    const id = task.httpOperationID!;
    job.controller = new AbortController();
    job.settled = false;
    this.patch(job.capture.request.operationID, { phase: 'running', error: undefined, transport: undefined });
    // Reattach only to the captured HTTP operation. Never replay an export POST as a status query.
    await this.track(job, options => job.backend.resumeOperation!<unknown>(id, options));
  };

  private async track(job: Job, request: (options: BackendRequestOptions) => Promise<unknown>): Promise<void> {
    const id = job.capture.request.operationID;
    const trackingGeneration = ++job.trackingGeneration;
    try {
      const raw = await request({
        signal: job.controller.signal,
        onProgress: value => {
          if (job.settled || trackingGeneration !== job.trackingGeneration || !record(value)) return;
          this.patch(id, { progress: {
            ...(typeof value.fraction === 'number' && Number.isFinite(value.fraction)
              ? { fraction: Math.min(1, Math.max(0, value.fraction)) } : {}),
            taskName: typeof value.taskName === 'string' ? value.taskName : '', message: typeof value.message === 'string' ? value.message : '',
          } });
        },
        onTransportState: transport => {
          if (job.settled || trackingGeneration !== job.trackingGeneration) return;
          const previous = this.task(id)?.httpOperationID;
          if (previous && previous !== transport.operationId) return;
          this.patch(id, { httpOperationID: transport.operationId, transport });
        },
      });
      const receipt = parseMeshExportReceipt(raw, job.capture);
      this.terminal(job, receipt.state, { receipt, error: undefined });
    } catch (error) {
      const metadata = record(error) ? error : {};
      const previousID = this.task(id)?.httpOperationID;
      const httpOperationID = previousID ?? (typeof metadata.operationId === 'string' ? metadata.operationId : undefined);
      const patch = { error, ...(httpOperationID ? { httpOperationID } : {}) };
      // Includes reattaching to an already canceled operation without aborting the new signal.
      if (metadata.terminalConfirmed === true && error instanceof Error && error.name === 'AbortError') {
        this.terminal(job, 'cancelled', { ...patch, error: undefined });
      } else if (metadata.terminalConfirmed === true || error instanceof BackendOperationUnavailableError ||
          (metadata.terminalConfirmed !== false && (metadata.code === 'INVALID_REQUEST' || metadata.code === 'CLIENT_DISPOSED' ||
            (metadata.operation === 'POST /operations' && typeof metadata.status === 'number' && metadata.status >= 400 && metadata.status < 500 && metadata.status !== 408)))) {
        this.terminal(job, 'failed', patch);
      } else {
        job.settled = true;
        this.patch(id, { ...patch, phase: 'unknown' });
        // Keep both captured source and root dirty ownership until a later terminal read is verified.
      }
    }
  }
}
