import { useId, useSyncExternalStore } from 'react';
import { createPortal } from 'react-dom';
import type { Language } from '../../shared/workbench';
import { errorDiagnostics, message, text } from '../../shared/workbench';
import { channelV6SourcePathIssue } from './channelV6ImportProtocol';
import type { ChannelV6ImportPhase, ChannelV6ImportProgress, ChannelV6ImportTask, ChannelV6ImportWorkflow } from './channelV6ImportTypes';
import './channelV6Import.css';

interface Props { workflow?: ChannelV6ImportWorkflow; language: Language; inputId: string; canImport: boolean }

/** Glue for the original ChannelModParentEditor.Import6_Click registration. */
export function ChannelV6ImportControl(props: Props) {
  if (!props.workflow) return props.canImport ? <section className="channel-v6-import" data-testid="channel-v6-import">
    <h4>{text(props.language, '导入 Version 6 一维几何', 'Import Version 6 1D geometry')}</h4>
    <p role="status">{text(props.language, '当前服务尚未提供此河道的 Version 6 几何导入，请先连接支持该操作的 C# 服务。', 'Connect a C# service that supports Version 6 geometry import for this Channel input.')}</p>
  </section> : null;
  return <BoundChannelV6ImportControl {...props} workflow={props.workflow} />;
}

function BoundChannelV6ImportControl({ workflow, language, inputId, canImport }: Props & { workflow: ChannelV6ImportWorkflow }) {
  const { controller } = workflow;
  const state = useSyncExternalStore(controller.subscribe, controller.getSnapshot, controller.getSnapshot);
  const fieldId = useId();
  const sourcePath = controller.sourcePath(workflow.owner, inputId);
  const sourceIssue = sourcePath ? channelV6SourcePathIssue(sourcePath) : null;
  const inspection = state.inspection?.capture.owner === workflow.owner && state.inspection.capture.inputId === inputId
    && state.inspection.rawSourcePath === sourcePath ? state.inspection : null;
  const task = state.task;
  const currentTask = !!task && task.inspection.capture.owner === workflow.owner && task.inspection.capture.inputId === inputId;
  if (!canImport && !currentTask && !controller.pending) return null;
  const showForm = canImport;
  const readDisabled = !workflow.canInspect || controller.pending || state.inspecting;
  const canStart = showForm && workflow.canInspect && controller.canStart(workflow.owner, inputId, sourcePath);
  const taskPanel = task && (currentTask || controller.pending) && <div className="channel-v6-import__task" data-testid="channel-v6-task" data-phase={task.phase}>
      {!currentTask && <p className="channel-v6-import__notice">{text(language, '仍在处理之前选择的河道：', 'Still processing the previously selected Channel: ')}{task.inspection.capture.inputName ?? task.inspection.capture.inputId}</p>}
      <p role="status" aria-live="polite" data-testid="channel-v6-status">{taskStatus(task, language)}</p>
      {task.phase === 'running' && <div className="channel-v6-import__progress">
        <progress max={1} value={task.progress?.fraction} aria-label={text(language, '河道几何导入进度', 'Channel geometry import progress')} />
        <span>{progressText(task.progress, language)}{task.progress?.fraction === undefined ? '' : ` · ${Math.round(task.progress.fraction * 100)}%`}</span>
      </div>}
      {task.phase === 'running' && task.transport?.state === 'reconnecting' && <small>{text(language, '连接中断，正在继续查询原任务…', 'Connection interrupted; following the original task…')}</small>}
      {task.httpOperationID && <small>{text(language, '导入任务：', 'Import task: ')}<code data-testid="channel-v6-operation-id">{task.httpOperationID}</code></small>}
      {task.cleanupOperationID && <small>{text(language, '清理任务：', 'Cleanup task: ')}<code>{task.cleanupOperationID}</code></small>}
      {task.phase === 'unknown' && !task.httpOperationID && <p className="channel-v6-import__notice">{text(language,
        '服务未返回可续查的任务编号。已保留本次请求和父草稿保护，不能重复提交导入。',
        'The service returned no resumable task ID. This request and parent draft protection are retained; the import cannot be submitted again.')}</p>}
      <div className="channel-v6-import__actions">
        {['running', 'unknown'].includes(task.phase) && <button type="button" className="editor-button" data-testid="channel-v6-cancel"
          disabled={task.cancelRequested} onClick={controller.cancel}>{task.cancelRequested ? text(language, '已请求取消', 'Cancellation requested') : text(language, '取消导入', 'Cancel import')}</button>}
        {task.phase === 'unknown' && <button type="button" className="editor-button" data-testid="channel-v6-resume" disabled={!controller.canResume()}
          onClick={() => void controller.resume()}>{text(language, '续查原导入任务', 'Resume original import task')}</button>}
        {['cleanup-unknown', 'cleanup-failed'].includes(task.phase) && <button type="button" className="editor-button" data-testid="channel-v6-cleanup"
          disabled={!controller.canRetryCleanup()} onClick={() => void controller.retryCleanup()}>{text(language,
            task.phase === 'cleanup-unknown' ? '续查原清理任务' : '重试本次文件清理',
            task.phase === 'cleanup-unknown' ? 'Resume original cleanup task' : 'Retry this file cleanup')}</button>}
      </div>
      {task.error !== undefined && <Diagnostics value={task.error} language={language} />}
      {task.cleanupError !== undefined && <Diagnostics value={task.cleanupError} language={language} />}
      {task.progress?.taskName && <details className="channel-v6-import__diagnostics"><summary>{text(language, '原始进度信息', 'Original progress information')}</summary><pre>{task.progress.taskName}{task.progress.message ? `\n${task.progress.message}` : ''}</pre></details>}
    </div>;

  return <section className="channel-v6-import" data-testid="channel-v6-import" aria-label={text(language, 'Version 6 河道几何导入', 'Version 6 Channel geometry import')}>
    <div className="channel-v6-import__heading"><h4>{text(language, '导入 Version 6 一维几何', 'Import Version 6 1D geometry')}</h4>
      <span>{text(language, 'HEC-RAS 6 · .hdf', 'HEC-RAS 6 · .hdf')}</span></div>
    {showForm && <>
      <p>{text(language, '选择 C# 服务所在计算机上的 Version 6 几何 HDF 文件。检查文件后，可将河道与横断面加入当前空河道输入。', 'Choose a Version 6 geometry HDF file on the computer running the C# service. After checking the file, import its channels and cross sections into this empty Channel input.')}</p>
      <label className="channel-v6-import__field" htmlFor={fieldId}>{text(language, 'Version 6 HDF 完整路径', 'Version 6 HDF full path')}</label>
      <div className="channel-v6-import__source">
        <input id={fieldId} data-testid="channel-v6-source" value={sourcePath} disabled={controller.pending || state.inspecting}
          autoComplete="off" spellCheck={false} placeholder={text(language, '例如 /工程/河道模型.g01.hdf', 'For example /project/River.g01.hdf')}
          aria-invalid={!!sourceIssue} aria-describedby={`${fieldId}-help`}
          onChange={event => controller.setSourcePath(workflow.owner, inputId, event.currentTarget.value)}
          onKeyDown={event => {
            if (event.key !== 'Enter' || event.nativeEvent.isComposing) return;
            // This field lives inside the parent save form; Enter belongs to source inspection.
            event.preventDefault();
            if (!readDisabled && !sourceIssue && sourcePath) void controller.inspect(inputId, sourcePath);
          }} />
        <button type="button" className="editor-button" data-testid="channel-v6-inspect" disabled={readDisabled || !sourcePath || !!sourceIssue}
          onClick={() => void controller.inspect(inputId, sourcePath)}>{state.inspecting
            ? text(language, '正在检查…', 'Checking…') : text(language, '检查 HDF 文件', 'Check HDF file')}</button>
      </div>
      <small id={`${fieldId}-help`}>{text(language, '保留原始路径输入；文件内容由服务端读取并计算 SHA-256。', 'The entered path is retained; the server reads the file and computes its SHA-256.')}</small>
      {sourceIssue && <p className="channel-v6-import__error" role="status">{language === 'zh' ? sourceIssue : 'Enter an absolute server-visible path ending in .hdf.'}</p>}
      {!workflow.canInspect && workflow.blockedMessage && !controller.pending && <p className="channel-v6-import__notice" role="status">{workflow.blockedMessage}</p>}
      {inspection && <dl className="channel-v6-import__facts" data-testid="channel-v6-inspected">
        <dt>{text(language, '已核验文件', 'Verified file')}</dt><dd><code>{inspection.file.canonicalPath}</code></dd>
        <dt>SHA-256</dt><dd><code data-testid="channel-v6-source-sha">{inspection.file.revision}</code></dd>
        <dt>{text(language, '文件大小', 'File size')}</dt><dd>{inspection.file.length.toLocaleString(language === 'zh' ? 'zh-CN' : 'en-US')} {text(language, '字节', 'bytes')}</dd>
        <dt>{text(language, '目标河道', 'Target Channel')}</dt><dd>{inspection.capture.inputName ?? inspection.capture.inputId}</dd>
      </dl>}
      {inspection && !controller.canStart(workflow.owner, inputId, sourcePath) && !controller.pending && <p className="channel-v6-import__notice" role="status">
        {text(language, '父草稿或工程来源已变化，请重新检查 HDF 文件。', 'The parent draft or project source changed. Check the HDF file again.')}</p>}
      <div className="channel-v6-import__actions">
        <button type="button" className="editor-button editor-button-primary" data-testid="channel-v6-start" disabled={!canStart}
          onClick={() => void controller.start()}>{text(language, '导入到当前河道草稿', 'Import into current Channel draft')}</button>
        {state.inspecting && <button type="button" className="editor-button" onClick={controller.invalidateInspection}>{text(language, '取消检查', 'Cancel check')}</button>}
      </div>
      <small>{text(language, '导入后使用父地形的撤销、重做和保存；点击父编辑器“保存”后才写入地形文件。', 'Use the parent terrain editor to undo, redo and save. The terrain file is written only when you save the parent editor.')}</small>
    </>}
    {state.validationError !== undefined && <div className="channel-v6-import__error" role="alert">
      <p>{message(state.validationError, language)}</p><Diagnostics value={state.validationError} language={language} />
    </div>}
    {taskPanel && (workflow.taskHost
      ? createPortal(<section className="channel-v6-import channel-v6-import--task" aria-label={text(language, 'Version 6 河道导入任务', 'Version 6 Channel import task')}>
        {taskPanel}
      </section>, workflow.taskHost)
      : taskPanel)}
  </section>;
}

function Diagnostics({ value, language }: { value: unknown; language: Language }) {
  const diagnostic = errorDiagnostics(value);
  let content: string;
  try { content = typeof diagnostic === 'string' ? diagnostic : JSON.stringify(diagnostic, null, 2) ?? String(value); }
  catch { content = String(value); }
  return <details className="channel-v6-import__diagnostics"><summary>{text(language, '原始诊断', 'Original diagnostics')}</summary><pre>{content}</pre></details>;
}

function taskStatus(task: ChannelV6ImportTask, language: Language): string {
  if (task.phase === 'running' && task.cancelRequested) return text(language, '已请求取消，正在等待原任务的最终结果…', 'Cancellation requested; awaiting the original task’s final result…');
  const labels: Record<ChannelV6ImportPhase, [string, string]> = {
    running: ['正在导入 Version 6 几何…', 'Importing Version 6 geometry…'],
    unknown: ['导入结果暂未确认。保留原任务编号，可继续查询。', 'The import outcome is unconfirmed. The original task ID is retained for resuming.'],
    cleaning: ['正在清理本次未接纳的几何暂存文件…', 'Cleaning up the unadopted geometry file from this import…'],
    'cleanup-unknown': ['清理结果暂未确认；父草稿保护和原清理任务编号仍保留。', 'The cleanup outcome is unconfirmed; parent draft protection and the original cleanup task ID are retained.'],
    'cleanup-failed': ['本次几何暂存文件尚未确认清理，请查看诊断并重试。', 'Cleanup of this geometry file is not confirmed. Review diagnostics and retry.'],
    completed: ['河道几何已加入父地形草稿，可撤销、重做或保存。', 'Channel geometry was added to the parent terrain draft. You can undo, redo or save it.'],
    cancelled: ['导入已取消，本次结果未加入父地形草稿。', 'The import was cancelled; this result was not added to the parent terrain draft.'],
    'not-adopted': ['来源或父草稿已变化，本次结果未接纳，暂存文件已清理。', 'The source or parent draft changed. This result was not adopted and its temporary file was cleaned up.'],
    failed: ['导入失败；请检查文件与原始诊断后重新检查来源。', 'Import failed. Review the file and original diagnostics, then check the source again.'],
  };
  return labels[task.phase][language === 'zh' ? 0 : 1];
}

function progressText(progress: ChannelV6ImportProgress | null, language: Language): string {
  const labels: Record<string, [string, string]> = {
    'Checking Version 6 geometry': ['核验 Version 6 文件', 'Checking Version 6 geometry'],
    'Converting Version 6 geometry': ['转换几何并准备一维线', 'Converting geometry and preparing 1D lines'],
    'Preparing imported Channel geometry': ['准备河道与横断面', 'Preparing channels and cross sections'],
    'Verifying imported geometry source': ['复核来源文件', 'Verifying the source file'],
    'Channel geometry is ready to save': ['河道草稿已准备完成', 'The Channel draft is ready'],
  };
  return progress?.taskName && labels[progress.taskName]
    ? labels[progress.taskName][language === 'zh' ? 0 : 1]
    : text(language, '正在处理几何', 'Processing geometry');
}
