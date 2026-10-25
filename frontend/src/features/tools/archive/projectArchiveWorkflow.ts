import type { BackendGateway, BackendRequestOptions, FeaturePageProps, WorkbenchContext } from '../../../shared/workbench';
import { validateSessionReferenceContext } from '../../../shared/sessionReferences';
import type { ProjectArchiveProgress, ProjectArchiveReceipt, ProjectArchiveRun, ProjectArchiveSource } from './projectArchiveTypes';

export const projectArchiveMethods = { prepare: 'developer.projectArchive.prepare', cleanup: 'developer.projectArchive.cleanup' } as const;
const record = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);
const sha = (value: unknown): value is string => typeof value === 'string' && /^[a-f\d]{64}$/i.test(value);
const equalSHA = (a: string, b: string): boolean => a.toUpperCase() === b.toUpperCase();
const absolute = (value: string): boolean => /^(?:\/|[a-z]:[\\/]|\\\\)/i.test(value);
export const archiveToken = (value: unknown): value is string => typeof value === 'string' && /^[a-f\d]{8}-[a-f\d]{4}-[a-f\d]{4}-[a-f\d]{4}-[a-f\d]{12}$/.test(value);
const canonical = (value: unknown): unknown => Array.isArray(value) ? value.map(canonical) : record(value)
  ? Object.fromEntries(Object.keys(value).sort().map(key => [key, canonical(value[key])])) : value;

/** Selection is irrelevant to full-project archives; all project/session/XML/Core identities are exact. */
export function projectArchiveSourceIdentity(context: WorkbenchContext): string {
  return JSON.stringify(canonical([context.projectSessionID, context.projectPath, context.projectRevision,
    context.projectXml, context.coreLayerContext ?? null]));
}
export function captureProjectArchiveSource(context: WorkbenchContext, backend: BackendGateway, backendRevision = 0): ProjectArchiveSource {
  if (!context.projectSessionID || !context.projectPath || !absolute(context.projectPath) || !/\.ras$/i.test(context.projectPath)
    || !sha(context.projectRevision) || !context.projectXml) throw new Error('请先打开具有完整源修订的已保存工程。');
  const captured = structuredClone(context);
  return { context: captured, identity: projectArchiveSourceIdentity(captured), backend, backendRevision };
}
export function isProjectArchiveSourceCurrent(source: ProjectArchiveSource, context: WorkbenchContext,
  backend: BackendGateway, backendRevision = 0): boolean {
  return source.backend === backend && source.backendRevision === backendRevision && source.identity === projectArchiveSourceIdentity(context);
}
export function projectArchiveBlocker(props: Pick<FeaturePageProps, 'context' | 'backend' | 'onDirtyStateChange' | 'hasUnsavedDrafts' | 'dirtyOwners' | 'planAnalysisDrafts'>,
  settingsOwner: string): string | null {
  if (!props.backend.supports('project.open') || !props.backend.supports(projectArchiveMethods.prepare) || !props.backend.supports(projectArchiveMethods.cleanup))
    return '当前 C# 服务未提供完整的工程归档和清理操作。';
  if (!props.onDirtyStateChange) return '当前工作区尚不能保护归档来源，暂不能准备归档。';
  if ([...(props.dirtyOwners ?? [])].some(owner => owner !== settingsOwner) || props.planAnalysisDrafts?.entries.some(entry => entry.dirty)
    || (props.hasUnsavedDrafts && (!props.dirtyOwners || props.dirtyOwners.size === 0)))
    return '工程存在未保存输入或正在执行的任务。请先保存或明确放弃修改，再准备归档；当前服务不能归档未保存输入。';
  const coreBlocker = projectArchiveCoreBlocker(props.context);
  if (coreBlocker) return coreBlocker;
  try { captureProjectArchiveSource(props.context, props.backend); } catch (error) { return (error as Error).message; }
  return null;
}
/** Session-only HDF/Core references are not inputs of this saved-directory archive service. */
export function projectArchiveCoreBlocker(context: WorkbenchContext): string | null {
  if (!context.coreLayerContext) return null;
  try { validateSessionReferenceContext(context.coreLayerContext, context); }
  catch { return '当前核心图层会话身份无法核验，暂不能归档。'; }
  const core = context.coreLayerContext;
  return (core.references as unknown[]).length || (core.sessionHdfReferences as unknown[]).length
    ? '当前工程含仅在会话中加载的外部来源；归档服务不能包含这些输入，请先将来源保存到工程并重新打开后归档。' : null;
}
export function validateProjectArchivePreflight(raw: unknown, source: ProjectArchiveSource): void {
  if (!record(raw) || raw.success !== true || raw.projectPath !== source.context.projectPath
    || !sha(raw.revision) || !equalSHA(raw.revision, source.context.projectRevision!) || raw.projectXml !== source.context.projectXml)
    throw new Error('当前工程 XML 或源修订与磁盘文件不同。请先保存或重新打开工程，不能改用旧的已保存内容归档。');
  const blocker = projectArchiveCoreBlocker(source.context);
  if (blocker) throw new Error(blocker);
}
export function parseProjectArchiveReceipt(raw: unknown, run: Pick<ProjectArchiveRun, 'request'>): ProjectArchiveReceipt {
  if (!record(raw) || (raw.state !== 'completed' && raw.state !== 'cancelled') || !archiveToken(raw.cleanupToken)
    || typeof raw.archivePath !== 'string' || !absolute(raw.archivePath) || typeof raw.projectPath !== 'string'
    || raw.projectPath.replace(/\\/g, '/') !== run.request.projectPath.replace(/\\/g, '/')
    || !sha(raw.sourceProjectSHA256) || !equalSHA(raw.sourceProjectSHA256, run.request.expectedProjectSHA256)
    || raw.includeResults !== run.request.includeResults || !Number.isSafeInteger(raw.entryCount) || (raw.entryCount as number) < 0
    || typeof raw.projectSHA256 !== 'string' || typeof raw.inventorySHA256 !== 'string')
    throw new Error('归档回执与捕获来源不一致，保留任务保护并继续核对原任务。');
  if (raw.state === 'completed' && (!sha(raw.projectSHA256) || !sha(raw.inventorySHA256) || (raw.entryCount as number) < 1))
    throw new Error('已完成归档缺少有效的内容校验回执。');
  if (raw.state === 'cancelled' && ((raw.projectSHA256 !== '' && !sha(raw.projectSHA256)) || (raw.inventorySHA256 !== '' && !sha(raw.inventorySHA256))))
    throw new Error('已取消归档的部分回执无效。');
  return raw as unknown as ProjectArchiveReceipt;
}
export function parseProjectArchiveProgress(raw: unknown): ProjectArchiveProgress | null {
  if (!record(raw)) return null;
  const fraction = typeof raw.fraction === 'number' && Number.isFinite(raw.fraction) && raw.fraction >= 0 && raw.fraction <= 1 ? raw.fraction : undefined;
  return { fraction, message: typeof raw.message === 'string' ? raw.message : '', taskName: typeof raw.taskName === 'string' ? raw.taskName : '' };
}
export function projectArchiveTerminalConfirmed(error: unknown): boolean { return record(error) && error.terminalConfirmed === true; }

/** Owns one retained artifact. Unknown prepare outcomes can only resume the original accepted task. */
export class ProjectArchiveWorkflow {
  run: ProjectArchiveRun | null = null;
  private locked = false;
  private controller: AbortController | null = null;
  private register: ((owner: string, dirty: boolean) => void) | null = null;
  private owner = '';
  private currentSource: (() => boolean) | null = null;
  constructor(private readonly changed: (run: ProjectArchiveRun) => void) {}
  get protected(): boolean { return this.locked; }
  get canPrepare(): boolean { return !this.locked && (!this.run?.receipt || this.run.cleanup === 'cleaned') && this.run?.cleanup !== 'cleaning'; }
  private patch(patch: Partial<ProjectArchiveRun>): void {
    if (!this.run) return;
    this.run = { ...this.run, ...patch }; this.changed(this.run);
  }
  private release(): void { this.register?.(this.owner, false); this.locked = false; }
  async prepare(source: ProjectArchiveSource, includeResults: boolean, register: (owner: string, dirty: boolean) => void, isCurrent: () => boolean): Promise<void> {
    if (!this.canPrepare) return;
    // Freeze and register synchronously before the first await or network dispatch.
    this.currentSource = isCurrent;
    const captured = { ...source, context: structuredClone(source.context) };
    const localID = crypto.randomUUID();
    this.owner = `tools.project-archive:${localID}`; this.register = register; this.locked = true;
    register(this.owner, true);
    this.run = { localID, operationID: null, source: captured, request: { projectPath: captured.context.projectPath!, includeResults,
      expectedProjectSHA256: captured.context.projectRevision! }, phase: 'running', tracking: false, cancelRequested: false,
      transport: null, progress: null, receipt: null, cleanup: 'retained' };
    this.changed(this.run);
    await this.track(false);
  }
  async resume(): Promise<void> {
    if (!this.locked || this.run?.tracking || !this.run?.operationID || !this.run.source.backend.resumeOperation) return;
    await this.track(true);
  }
  cancel(): void {
    if (!this.run?.tracking || !this.controller) return;
    this.patch({ cancelRequested: true }); this.controller.abort('User requested project archive cancellation.');
  }
  private async track(resume: boolean): Promise<void> {
    const run = this.run!;
    this.controller = new AbortController();
    this.patch({ tracking: true, phase: 'running', error: undefined, transport: null, cancelRequested: false });
    const options: BackendRequestOptions = { signal: this.controller.signal, onProgress: raw => {
      const progress = parseProjectArchiveProgress(raw);
      if (progress) this.patch({ progress: { fraction: progress.fraction ?? this.run?.progress?.fraction,
        message: progress.message || this.run?.progress?.message || '', taskName: progress.taskName || this.run?.progress?.taskName || '' } });
    }, onTransportState: state => {
      if (this.run?.operationID && state.operationId !== this.run.operationID) return;
      this.patch({ operationID: state.operationId, transport: state.state });
    } };
    let dispatched = resume, received = false;
    try {
      if (!resume) {
        const opened = await run.source.backend.request('project.open', { projectPath: run.request.projectPath }, { signal: this.controller.signal });
        validateProjectArchivePreflight(opened, run.source);
        if (this.controller.signal.aborted) throw new Error('归档准备已取消，未提交归档任务。');
        if (!this.currentSource?.()) throw new Error('工程、会话或输入已变化，未提交归档任务，请重新准备。');
      }
      dispatched = true;
      const raw = resume ? await run.source.backend.resumeOperation!(run.operationID!, options)
        : await run.source.backend.request(projectArchiveMethods.prepare, run.request, options);
      received = true;
      this.patch({ rawReceipt: raw });
      const receipt = parseProjectArchiveReceipt(raw, run);
      this.patch({ phase: 'terminal', receipt, cancelRequested: false }); this.release();
    } catch (error) {
      const operationID = record(error) && typeof error.operationId === 'string' ? error.operationId : null;
      if (!this.run?.operationID && operationID) this.patch({ operationID });
      const terminal = !dispatched || projectArchiveTerminalConfirmed(error);
      this.patch({ phase: terminal ? 'terminal' : received ? 'unverified' : 'unknown', error });
      if (terminal) this.release();
    } finally { this.patch({ tracking: false }); }
  }
  async cleanup(): Promise<void> {
    const run = this.run;
    if (!run?.receipt || this.locked || run.cleanup === 'cleaning' || run.cleanup === 'cleaned') return;
    this.patch({ cleanup: 'cleaning', cleanupError: undefined });
    try {
      const raw = await run.source.backend.request(projectArchiveMethods.cleanup, { cleanupToken: run.receipt.cleanupToken });
      if (!record(raw) || raw.cleaned !== true) throw new Error('服务尚未确认归档已清理；保留回执，可重试清理。');
      this.patch({ cleanup: 'cleaned' });
    } catch (cleanupError) { this.patch({ cleanup: 'failed', cleanupError }); }
  }
}
