import { useEffect, useRef, useState } from 'react';
import type { FeaturePageProps } from '../../../shared/workbench';
import { errorDiagnostics, message } from '../../../shared/workbench';
import type { ProjectArchiveRun } from './projectArchiveTypes';
import { captureProjectArchiveSource, isProjectArchiveSourceCurrent, projectArchiveBlocker,
  projectArchiveSourceIdentity, ProjectArchiveWorkflow } from './projectArchiveWorkflow';
import './projectArchive.css';

/** DevHomePage.cs: current-project ZipFullProject preparation only. Cloud upload/auth are separate responsibilities. */
export function DevHomePage(props: FeaturePageProps) {
  const live = useRef(props); live.current = props;
  const [run, setRun] = useState<ProjectArchiveRun | null>(null);
  const workflow = useRef<ProjectArchiveWorkflow | null>(null);
  if (!workflow.current) workflow.current = new ProjectArchiveWorkflow(setRun);
  const workflowRef = workflow.current;
  const sourceIdentity = projectArchiveSourceIdentity(props.context);
  const discardRevision = props.planAnalysisDrafts?.discardRevision ?? 0;
  const [form, setForm] = useState({ sourceIdentity, discardRevision, includeResults: true, baseline: true, dirty: false });
  const formRef = useRef(form); formRef.current = form;
  const settings = useRef({ owner: `tools.project-archive-settings:${crypto.randomUUID()}`, register: props.onDirtyStateChange });
  const [error, setError] = useState<unknown>();
  const [downloadRequested, setDownloadRequested] = useState<string | null>(null);
  const formCurrent = form.sourceIdentity === sourceIdentity && form.discardRevision === discardRevision;
  const includeResults = formCurrent ? form.includeResults : true;
  const formDirty = formCurrent && form.dirty;
  // Reset only the old render's exact source/discard tuple. A newer user edit wins over an old effect.
  useEffect(() => {
    if (formCurrent) return;
    const oldSource = form.sourceIdentity, oldDiscard = form.discardRevision;
    if (formRef.current.sourceIdentity !== oldSource || formRef.current.discardRevision !== oldDiscard) return;
    setForm(current => current.sourceIdentity === oldSource && current.discardRevision === oldDiscard
      ? { sourceIdentity, discardRevision, includeResults: true, baseline: true, dirty: false } : current);
    settings.current.register?.(settings.current.owner, false);
  }, [sourceIdentity, discardRevision, formCurrent, form.sourceIdentity, form.discardRevision]);
  useEffect(() => () => {
    settings.current.register?.(settings.current.owner, false);
    // Artifacts and an unknown operation remain owned by the workflow. Hidden tabs do not unmount it.
    // No cleanup or cancel occurs on navigation; the operation promise releases its owner only at terminal confirmation.
  }, []);

  const changeIncludeResults = (value: boolean): void => {
    if (workflowRef.protected || run?.cleanup === 'cleaning') return;
    const baseline = formCurrent ? form.baseline : true;
    const next = { sourceIdentity, discardRevision, includeResults: value, baseline, dirty: value !== baseline };
    formRef.current = next;
    settings.current.register?.(settings.current.owner, next.dirty);
    setForm(next); setError(undefined);
  };
  const resetSettings = (): void => {
    if (workflowRef.protected) return;
    settings.current.register?.(settings.current.owner, false);
    setForm({ sourceIdentity, discardRevision, includeResults: true, baseline: true, dirty: false });
  };
  const blocker = projectArchiveBlocker(props, settings.current.owner);
  const prepare = async (): Promise<void> => {
    if (!workflowRef.canPrepare) return;
    try {
      const current = live.current;
      const blocked = projectArchiveBlocker(current, settings.current.owner);
      if (blocked) throw new Error(blocked);
      const source = captureProjectArchiveSource(current.context, current.backend, current.backendRevision);
      setError(undefined); setDownloadRequested(null);
      await workflowRef.prepare(source, includeResults, current.onDirtyStateChange!, () => {
        const latest = live.current;
        // The task's own protection is expected. Any new editor/session owner still invalidates preparation.
        const owners = new Set([...latest.dirtyOwners ?? []].filter(owner => owner !== `tools.project-archive:${workflowRef.run?.localID}`));
        return isProjectArchiveSourceCurrent(source, latest.context, latest.backend, latest.backendRevision)
          && !projectArchiveBlocker({ ...latest, dirtyOwners: owners, hasUnsavedDrafts: latest.dirtyOwners ? owners.size > 0 : latest.hasUnsavedDrafts }, settings.current.owner);
      });
      if (workflowRef.run?.receipt && isProjectArchiveSourceCurrent(source, live.current.context, live.current.backend, live.current.backendRevision)) {
        settings.current.register?.(settings.current.owner, false);
        setForm(currentForm => currentForm.sourceIdentity === source.identity
          ? { ...currentForm, baseline: currentForm.includeResults, dirty: false } : currentForm);
      }
    } catch (caught) { setError(caught); }
  };
  const oldSource = !!run && !isProjectArchiveSourceCurrent(run.source, props.context, props.backend, props.backendRevision);
  const receipt = run?.receipt;
  const downloadURL = receipt?.state === 'completed' && run?.cleanup !== 'cleaned' && run?.cleanup !== 'cleaning'
    ? run?.source.backend.projectArchiveUrl?.(receipt.cleanupToken) : null;
  const retained = !!receipt && run?.cleanup !== 'cleaned';
  const diagnostic = (value: unknown): string => { try { return JSON.stringify({ message: message(value, props.language), details: errorDiagnostics(value) }, null, 2) ?? String(value); } catch { return String(value); } };
  const phase = !run ? '' : run.phase === 'unverified' ? '服务已返回终态，归档回执尚未通过核验' : run.phase === 'unknown' ? '归档终态尚未确认'
    : run.phase === 'running' ? run.cancelRequested ? '已请求取消，等待服务确认' : '正在核验和准备归档'
    : receipt?.state === 'completed' ? '归档准备完成' : receipt?.state === 'cancelled' ? '服务已确认取消' : '归档任务已终止';

  return <section className="project-archive" data-testid="project-archive">
    <header><div><span className="project-archive-eyebrow">工程文件</span><h2>完整工程归档</h2></div><span className="project-archive-badge">ZIP</span></header>
    <p className="project-archive-help">将已保存工程及其目录文件准备为 ZIP，可下载后保存或分享。计算结果可按需包含。</p>
    <div className="project-archive-source"><span>当前工程</span><strong>{props.context.projectPath || '尚未打开工程'}</strong></div>
    <div className="project-archive-settings">
      <label><input data-testid="project-archive-include-results" type="checkbox" checked={includeResults}
        disabled={workflowRef.protected || run?.cleanup === 'cleaning'} onChange={event => changeIncludeResults(event.target.checked)} />包含计算结果</label>
      <span>仅归档保存到工程目录的内容；未保存输入和仅在会话中加载的来源需要先处理。</span>
    </div>
    <div className="project-archive-actions">
      <button type="button" className="project-archive-primary" data-testid="project-archive-prepare"
        disabled={!!blocker || !workflowRef.canPrepare} onClick={() => void prepare()}>准备归档</button>
      {formDirty && <button type="button" disabled={workflowRef.protected} onClick={resetSettings}>重置选项</button>}
      {run?.tracking && <button type="button" disabled={run.cancelRequested} onClick={() => workflowRef.cancel()}>取消准备</button>}
      {(run?.phase === 'unknown' || run?.phase === 'unverified') && !run.tracking && <button type="button" data-testid="project-archive-resume"
        disabled={!run.operationID || !run.source.backend.resumeOperation} onClick={() => void workflowRef.resume()}>继续核对原任务</button>}
    </div>
    {blocker && !workflowRef.protected && <p className="project-archive-note" role="status">{blocker}</p>}
    {retained && <p className="project-archive-note">当前归档回执保留在下方。下载不会自动删除服务端文件；明确清理后可准备下一份归档。</p>}
    {error != null && <div role="alert" className="project-archive-error"><p>不能准备归档，请核对工程与未保存状态。</p><details><summary>原因与诊断</summary><pre>{diagnostic(error)}</pre></details></div>}
    {run && <section className="project-archive-receipt" data-testid="project-archive-receipt" aria-label="归档任务回执">
      <div className="project-archive-status"><h3>{phase}</h3>{run.phase === 'running' && run.progress?.fraction !== undefined && <span>{Math.round(run.progress.fraction * 100)}%</span>}</div>
      {run.phase === 'running' && <progress aria-label="服务端归档进度" max={1} value={run.progress?.fraction} />}
      {oldSource && <p className="project-archive-note" data-testid="project-archive-previous-source">此任务属于下方捕获的原工程会话，与当前工程不同；回执和清理仍对应原归档。</p>}
      {run.phase === 'unverified' && <p role="alert" className="project-archive-note">原任务已返回，但回执与捕获来源不一致或不完整。保留原始回执和来源保护；可核对原任务回执，不能重新提交或使用未核验标识清理。</p>}
      {run.phase === 'unknown' && <p role="alert" className="project-archive-note">尚不能确认任务是否完成，继续保留原来源与写保护。续查只读取同一任务，不会重新提交归档。</p>}
      {run.transport && ['reconnecting', 'cancelRetrying', 'cancelFailed'].includes(run.transport) && <p role="status">服务连接或取消确认尚未恢复，正在保留任务状态。</p>}
      {run.phase === 'terminal' && !receipt && <p className="project-archive-note">没有收到可核验的归档回执，不能提供下载或声称临时文件已经清理。</p>}
      <dl>
        <div><dt>捕获工程</dt><dd>{run.source.context.projectPath}</dd></div>
        <div><dt>工程会话</dt><dd>{run.source.context.projectSessionID}</dd></div>
        <div><dt>包含计算结果</dt><dd>{run.request.includeResults ? '是' : '否'}</dd></div>
        <div><dt>源 .ras SHA-256</dt><dd>{receipt?.sourceProjectSHA256 || run.request.expectedProjectSHA256}</dd></div>
        {run.operationID && <div><dt>任务编号</dt><dd>{run.operationID}</dd></div>}
        {receipt && <><div><dt>ZIP 内 .ras SHA-256</dt><dd>{receipt.projectSHA256 || '未生成'}</dd></div>
          <div><dt>归档清单 SHA-256</dt><dd>{receipt.inventorySHA256 || '未生成'}</dd></div>
          <div><dt>归档条目</dt><dd>{receipt.entryCount}</dd></div>
          <div><dt>临时归档路径</dt><dd>{receipt.archivePath}</dd></div></>}
      </dl>
      {receipt?.state === 'completed' && <p className="project-archive-help">归档会按当前工程格式保存暂存工程及模型，名称等元数据可能更新，源文件保持不变。源 .ras 校验值对应原文件，ZIP 内 .ras 校验值对应暂存保存后的工程；归档清单校验值不是 ZIP 文件自身的校验值。</p>}
      {receipt && <div className="project-archive-actions">
        {downloadURL && <a className="project-archive-primary" data-testid="project-archive-download" href={downloadURL} download
          onClick={() => setDownloadRequested(run.localID)}>下载 ZIP</a>}
        {receipt.state === 'completed' && !downloadURL && run.cleanup === 'retained' && <span>当前连接尚未提供归档下载地址。</span>}
        {run.cleanup !== 'cleaned' && <button type="button" data-testid="project-archive-cleanup" disabled={run.cleanup === 'cleaning'}
          onClick={() => void workflowRef.cleanup()}>{run.cleanup === 'cleaning' ? '正在清理…' : run.cleanup === 'failed' ? '重试清理临时归档' : '清理临时归档'}</button>}
      </div>}
      {downloadRequested === run.localID && run.cleanup !== 'cleaned' && <p role="status">已请求浏览器下载。请在浏览器中确认文件已保存，再清理服务端归档。</p>}
      {run.cleanup === 'cleaned' && <p className="project-archive-success" role="status" data-testid="project-archive-cleaned">服务已确认临时归档已清理。</p>}
      {run.cleanup === 'failed' && <p className="project-archive-error" role="alert">清理尚未确认，归档回执仍保留。可使用同一归档标识重试清理。</p>}
      {(run.error != null || run.cleanupError != null || run.progress || run.rawReceipt !== undefined) && <details><summary>服务端进度与回执诊断</summary>
        <pre>{JSON.stringify({ progress: run.progress, receipt: run.rawReceipt, error: run.error == null ? undefined : diagnostic(run.error),
          cleanupError: run.cleanupError == null ? undefined : diagnostic(run.cleanupError) }, null, 2)}</pre>
      </details>}
    </section>}
  </section>;
}
