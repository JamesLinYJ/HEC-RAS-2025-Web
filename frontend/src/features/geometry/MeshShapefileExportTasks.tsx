import type { Language } from '../../shared/workbench';
import { errorDiagnostics, message } from '../../shared/workbench';
import type { MeshExportPhase, MeshExportTask } from './MeshShapefileExportController';
import { meshExportTaskPending } from './MeshShapefileExportController';
import type { MeshShapefileExportWorkspace } from './useMeshShapefileExport';
import './meshShapefileExport.css';

const labels: Record<MeshExportPhase, readonly [string, string]> = {
  preparing: ['正在准备导出', 'Preparing export'], running: ['正在导出', 'Exporting'],
  cancelling: ['正在请求取消，等待终态', 'Cancellation requested; awaiting terminal state'],
  unknown: ['导出终态尚未确认', 'Export terminal state unconfirmed'], completed: ['导出完成', 'Export completed'],
  cancelled: ['已确认取消', 'Cancellation confirmed'], failed: ['导出失败', 'Export failed'],
};
function progressLabel(task: MeshExportTask, zh: boolean): string {
  const labels: Record<string, string> = { 'Collecting mesh cell centers': '正在收集完整单元中心', 'Writing mesh cell centers': '正在写入单元中心',
    'Collecting mesh perimeter faces': '正在收集外围面', 'Writing mesh perimeter faces': '正在写入外围面' };
  if (!zh) return task.progress?.taskName || 'The server is processing the export.';
  return labels[task.progress?.taskName ?? ''] ?? '服务正在处理导出任务。';
}

/** Kept outside the Mesh route so every result remains attached to its captured source. */
export function MeshShapefileExportTasks({ language, workspace }: { language: Language; workspace: MeshShapefileExportWorkspace }) {
  const zh = language === 'zh';
  if (!workspace.state.tasks.length) return null;
  return <section className="geometry-panel mesh-shapefile-export" data-testid="mesh-export-tasks">
    <header className="geometry-panel-heading"><div><h2>{zh ? '网格导出任务' : 'Mesh export tasks'}</h2>
      <p>{zh ? '每份回执保留提交时的来源；取消以服务器终态为准。' : 'Each receipt retains its submitted source; cancellation requires a server terminal state.'}</p></div></header>
    <div className="geometry-panel-body">
      {[...workspace.state.tasks].reverse().map((task, index) => {
        const { capture, receipt } = task;
        const request = capture.request;
        const pending = meshExportTaskPending(task);
        return <details className="mesh-shapefile-export__task" open={index === 0 || pending} key={request.operationID}
          data-testid="mesh-export-task" data-state={task.phase} data-operation-id={request.operationID}>
          <summary><strong role="status">{labels[task.phase][zh ? 0 : 1]}</strong> · {request.exportKind === 'CellCenters' ? (zh ? '单元中心' : 'Cell centers') : (zh ? '外围面' : 'Perimeter faces')} · {request.expectedFlowAreaName}</summary>
          {capture.sourceKey !== workspace.sourceKey && <p>{zh ? '此任务属于之前捕获的来源；回执不会应用到当前选择。' : 'This task belongs to a previously captured source; its receipt is not applied to the current selection.'}</p>}
          <dl className="mesh-shapefile-export__identity">
            <dt>{zh ? '源工程' : 'Source project'}</dt><dd>{capture.context.projectPath}</dd>
            <dt>{zh ? '源 Geometry' : 'Source Geometry'}</dt><dd data-testid="mesh-export-task-source">{request.model.assetPath}</dd>
            <dt>{zh ? '流动区域' : 'Flow area'}</dt><dd>{request.expectedFlowAreaName} · {request.flowAreaID}</dd>
            <dt>{zh ? '导出任务 ID' : 'Export task ID'}</dt><dd>{request.operationID}</dd>
            <dt>{zh ? 'HTTP 跟踪 ID' : 'HTTP tracking ID'}</dt><dd data-testid="mesh-export-http-id">{task.httpOperationID ?? (zh ? '尚未收到受理标识' : 'Acceptance ID not yet received')}</dd>
            <dt>{zh ? '目标文件' : 'Destination'}</dt><dd>{request.outputPath}</dd>
          </dl>
          <details><summary>{zh ? '来源修订与草稿摘要' : 'Source revisions and draft digests'}</summary><dl className="mesh-shapefile-export__identity">
            <dt>{zh ? '工程会话' : 'Project session'}</dt><dd>{capture.context.projectSessionID}</dd>
            <dt>{zh ? '工程 SHA-256' : 'Project SHA-256'}</dt><dd>{capture.context.projectRevision}</dd>
            <dt>Geometry SHA-256</dt><dd>{request.revision}</dd>
            <dt>{zh ? '网格草稿 SHA-256' : 'Mesh draft SHA-256'}</dt><dd>{request.meshDraftSHA256 || (zh ? '计算中' : 'Computing')}</dd>
            <dt>{zh ? '拓扑草稿 SHA-256' : 'Topology draft SHA-256'}</dt><dd>{request.topologyDraftSHA256 || (zh ? '计算中' : 'Computing')}</dd>
            <dt>Core</dt><dd><pre>{JSON.stringify(request.model.coreLayerContext ?? null, null, 2)}</pre></dd>
          </dl></details>
          {pending && task.phase !== 'unknown' && <div className="mesh-shapefile-export__progress" role="status">
            <progress max={1} value={task.progress?.fraction} /> <span>{progressLabel(task, zh)}{task.progress?.fraction === undefined ? '' : ` (${Math.round(task.progress.fraction * 100)}%)`}</span>
          </div>}
          {task.transport?.state === 'reconnecting' && <p role="status">{zh ? '连接暂时中断，正在继续跟踪原任务；不会重新导出。' : 'Reconnecting to the original task without repeating the export.'}</p>}
          {(task.transport?.state === 'cancelFailed' || task.transport?.state === 'cancelRetrying') && <p role="status">{zh ? '取消送达尚未确认，仍在核对原任务状态。' : 'Cancellation delivery is unconfirmed; still tracking the original task.'}</p>}
          {task.phase === 'unknown' && <p role="alert">{zh ? '未收到可验证的终态。任务与来源已保留；确认前不能重新导出或放弃此任务。' : 'No verified terminal state was received. The task and source are retained; another export or discard is blocked until confirmation.'}</p>}
          {task.phase === 'cancelled' && !task.dispatched && <p>{zh ? '准备阶段已取消，请求尚未发送到服务器。' : 'Preparation was canceled before the request was sent.'}</p>}
          {task.phase === 'failed' && <p>{zh ? '请查看诊断并核对目标目录，再选择新的导出路径。' : 'Review the diagnostics and destination before choosing another output path.'}</p>}
          {task.error !== undefined && <><p role="alert">{message(task.error, language)}</p><details><summary>{zh ? '原始诊断' : 'Original diagnostics'}</summary><pre>{JSON.stringify(errorDiagnostics(task.error), null, 2)}</pre></details></>}
          {pending && <div className="mesh-shapefile-export__actions">
            <button type="button" className="geometry-secondary" data-testid="mesh-export-cancel" disabled={task.phase === 'unknown' || task.phase === 'cancelling'} onClick={workspace.cancel}>{zh ? '请求取消' : 'Request cancellation'}</button>
            {task.phase === 'unknown' && <button type="button" className="geometry-secondary" data-testid="mesh-export-resume" disabled={!workspace.canResume} onClick={() => void workspace.resume()}>{zh ? '继续核对任务' : 'Check existing task'}</button>}
          </div>}
          {task.phase === 'unknown' && !task.httpOperationID && <p>{zh ? '服务器未返回 HTTP 跟踪 ID，不能安全查询或重发；请按导出任务 ID 和目标路径核对服务器记录。' : 'No HTTP tracking ID was returned. Do not resend; inspect server records using the export task ID and destination.'}</p>}
          {receipt && <div data-testid="mesh-export-receipt">
            <p data-testid="mesh-export-feature-count">{zh ? '实际导出要素' : 'Exported features'}: <strong>{receipt.featureCount}</strong> · {zh ? '已发布文件' : 'Published files'}: <strong>{receipt.artifacts.length}</strong></p>
            {receipt.state === 'completed' && <p>{receipt.projectionWKT ? (zh ? '已按源工程投影写入 .prj。' : 'The .prj uses the source project projection.') : (zh ? '源工程使用 Local 坐标，未生成 .prj。' : 'The project uses Local coordinates; no .prj was generated.')}</p>}
            {!!receipt.artifacts.length && <div className="mesh-shapefile-export__files"><table><thead><tr><th>{zh ? '服务器实际文件' : 'Server artifact'}</th><th>{zh ? '字节数' : 'Bytes'}</th><th>SHA-256</th></tr></thead><tbody>
              {receipt.artifacts.map(file => <tr key={file.path} data-testid="mesh-export-artifact"><td>{file.path}</td><td>{file.sizeBytes}</td><td>{file.sha256}</td></tr>)}
            </tbody></table></div>}
          </div>}
          {task.progress && <details><summary>{zh ? '服务端原始进度' : 'Original server progress'}</summary><pre>{JSON.stringify(task.progress, null, 2)}</pre></details>}
        </details>;
      })}
    </div>
  </section>;
}
