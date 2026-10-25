import type { Language } from '../../../shared/workbench';
import { errorDiagnostics, text } from '../../../shared/workbench';
import type { ProjectProjectionExportController, ProjectProjectionState } from './ProjectProjectionExportController';

export function ProjectProjectionExportPanel({ language, state, controller }: {
  language: Language; state: ProjectProjectionState; controller: ProjectProjectionExportController;
}) {
  const task = state.task, error = state.validationError ?? task?.error;
  if (!task && !error) return null;
  const labels = {
    running: ['正在导出工程投影…', 'Exporting project projection…'],
    unknown: ['导出结果尚未确认，请继续核对原任务。', 'Export status is unknown. Check the original task.'],
    verifying: ['正在读回服务器投影文件…', 'Reading back the server projection file…'],
    completed: ['投影已导出，服务器文件读回已确认。', 'Projection exported; server file readback confirmed.'],
    'readback-failed': ['服务器已返回导出路径，但文件读回尚未确认。', 'Server returned an export path; file readback is unconfirmed.'],
    failed: ['未能确认投影导出，请查看诊断。', 'Projection export could not be confirmed. See diagnostics.'],
  };
  return <section className="project-global-notice" data-testid="project-projection-export-status" data-state={task?.phase}>
    <div>
      {task && <>
        <p role="status">{labels[task.phase][language === 'zh' ? 0 : 1]}</p>
        <p>{text(language, '来源工程', 'Source project')}：{task.capture.request.projectPath}</p>
        {task.httpOperationID && <p data-testid="project-projection-http-id">{text(language, '原 HTTP 任务', 'Original HTTP task')}：{task.httpOperationID}</p>}
        {task.receipt && <p data-testid="project-projection-output-path">{text(language, '服务器输出路径', 'Server output path')}：{task.receipt.outputPath}</p>}
        {task.file && <p data-testid="project-projection-file-readback">{task.file.length} {text(language, '字节', 'bytes')} · SHA-256：{task.file.revision}</p>}
        {task.phase === 'unknown' && <>
          <button type="button" className="project-button project-button-secondary" data-testid="project-projection-export-resume"
            disabled={!controller.canResume()} onClick={() => void controller.resume()}>{text(language, '继续核对原任务', 'Check original task')}</button>
          {!controller.canResume() && <p>{text(language, '缺少原任务标识或恢复能力，写入保护仍然保留。请保持当前会话并核对服务日志。', 'Tracking ID or resume support is unavailable. Write protection remains active; retain this session and inspect server logs.')}</p>}
        </>}
        {task.phase === 'readback-failed' && <button type="button" className="project-button project-button-secondary" data-testid="project-projection-export-readback"
          onClick={() => void controller.retryReadback()}>{text(language, '重新读回文件', 'Retry file readback')}</button>}
      </>}
      {error != null && <details open><summary>{text(language, '导出诊断', 'Export diagnostics')}</summary><pre>{error instanceof Error ? error.message : String(error)}</pre>
        <pre>{JSON.stringify(errorDiagnostics(error), null, 2)}</pre></details>}
    </div>
  </section>;
}
