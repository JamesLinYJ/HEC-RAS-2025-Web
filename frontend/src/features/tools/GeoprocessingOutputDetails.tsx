import { useState } from 'react';
import type { BackendTransportState, Language } from '../../shared/workbench';
import { errorDiagnostics, message, text } from '../../shared/workbench';
import { displayToolOption, type GeoprocessingToolDefinition } from './GeoprocessingToolBase';

export interface GeoprocessingOutputFileReceipt {
  status: 'verified' | 'unverified';
  inspection?: { canonicalPath: string; revision: string; length: number };
  readback?: { driver: string; dataType: string; width: number; height: number; bandCount: number; sourceRevision: string };
  error?: unknown;
}

export interface GeoprocessingRasterResult {
  canExecute: boolean;
  operation: string;
  outputTitle: string;
  inputLineCount: number;
  outputLineCount: number;
  inputPolygonCount: number;
  outputPolygonCount: number;
  inputPointCount: number;
  outputPath: string | null;
  clipboardText: string | null;
  textOutput: string | null;
  outputFormat: string | null;
  outputKind: string;
  outputHandler: string;
  outputRasterWidth: number | null;
  outputRasterHeight: number | null;
  outputRasterExtent: number[] | null;
  validationIssues: Array<{ field: string; message: string; severity: string }>;
  fileReceipt?: GeoprocessingOutputFileReceipt;
}

export type GeoprocessingOutputView =
  | { kind: 'idle' }
  | { kind: 'running'; progress?: unknown; transport?: BackendTransportState; cancelRequested: boolean; verifyingOutput?: boolean }
  | { kind: 'completed'; result: GeoprocessingRasterResult }
  | { kind: 'rejected'; result: GeoprocessingRasterResult }
  | { kind: 'cancelled' }
  | { kind: 'failed'; error: unknown; diagnostics?: unknown }
  | { kind: 'uncertain'; error: unknown; diagnostics?: unknown };

function downloadText(value: string, tool: GeoprocessingToolDefinition, extension: string): void {
  const url = URL.createObjectURL(new Blob([value], { type: 'text/plain;charset=utf-8' }));
  const link = document.createElement('a'); link.href = url; link.download = `${tool.id}-service-output.${extension}`; link.click();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}

function downloadJson(value: unknown, tool: GeoprocessingToolDefinition): void {
  const url = URL.createObjectURL(new Blob([JSON.stringify(value, null, 2)], { type: 'application/json' }));
  const link = document.createElement('a'); link.href = url; link.download = `${tool.id}-host-result.json`; link.click();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}

function transportLabel(state: BackendTransportState, language: Language): string {
  if (language !== 'zh') return state.state;
  switch (state.state) {
    case 'reconnecting': return `连接恢复中（第 ${state.attempt} 次）`;
    case 'connected': return '服务连接已恢复';
    case 'cancelRequested': return '已请求取消，正在等待服务端终态';
    case 'cancelRetrying': return `正在重试发送取消请求（第 ${state.attempt} 次）`;
    case 'cancelSent': return state.accepted ? '取消请求已送达，等待服务端终态' : '服务端未接受取消请求，仍在等待执行终态';
    case 'cancelFailed': return '取消请求传输失败，执行终态仍待确认';
  }
}

function progressLabel(progress: unknown, language: Language): { fraction?: number; label?: string } | null {
  if (!progress || typeof progress !== 'object' || Array.isArray(progress)) return null;
  const record = progress as Record<string, unknown>;
  const fraction = typeof record.fraction === 'number' && Number.isFinite(record.fraction)
    ? Math.max(0, Math.min(1, record.fraction)) : undefined;
  const task = typeof record.taskName === 'string' ? record.taskName : '';
  const labels: Record<string, string> = {
    'Checking captured map features': '正在核对已保存地图来源',
    'Rasterizing polygons': '正在栅格化多边形',
    'Calculating distance raster': '正在计算距离栅格',
    'Computing proximity heatmap': '正在生成邻近热力图',
    'Clipping raster to polygons': '正在按多边形裁剪栅格',
    'Calculating raster cut statistics': '正在计算分区统计',
    'Generating Laplacian terrain': '正在生成拉普拉斯曲面',
    'Resampling raster': '正在重采样栅格',
    'Rounding raster values': '正在取整栅格值',
    'Writing TIFF output': '正在写入 TIFF 文件',
    'Finishing geoprocessing output': '正在完成地理处理输出',
  };
  const label = task ? (language === 'zh' ? labels[task] ?? task : task) : undefined;
  return fraction === undefined && label === undefined ? null : { fraction, label };
}

function validationField(field: string, language: Language): string {
  if (language !== 'zh') return field;
  const labels: Record<string, string> = {
    CellSize: '像元大小', FillValue: '填充值', BackgroundValue: '背景值',
    MaxDistanceCells: '最大距离（像元）', DistanceAlgorithm: '距离算法', SigmaCells: '标准差（像元）',
    OutputCellSize: '输出像元大小', BinCount: '直方图分箱数', BoundarySpacing: '边界间距',
    InteriorGridSpacing: '内部网格间距', OutputName: '输出名称', Connectivity: '像元邻接', OutputMode: '输出范围',
  };
  return labels[field] ?? field;
}

function validationMessage(value: string, language: Language): string {
  if (language !== 'zh') return value;
  if (value.startsWith('Not valid ') && value.endsWith(' type.')) return '请输入有效数值。';
  return value;
}

export function GeoprocessingOutputDetails({ language, tool, view, operationID, onCancel }: {
  language: Language; tool: GeoprocessingToolDefinition; view: GeoprocessingOutputView;
  operationID: string | null; onCancel: () => void;
}) {
  const zh = language === 'zh';
  const [clipboardMessage, setClipboardMessage] = useState<string>();
  const copyOutput = async (value: string): Promise<void> => {
    setClipboardMessage(undefined);
    try {
      if (!navigator.clipboard?.writeText) throw new Error('Clipboard access is unavailable.');
      await navigator.clipboard.writeText(value);
      setClipboardMessage(text(language, '已将服务端返回文本复制到剪贴板。', 'The text returned by the service was copied to the clipboard.'));
    } catch {
      setClipboardMessage(text(language, '无法访问系统剪贴板；可下载同一份服务端结果文本。', 'Clipboard access failed; download the same service result instead.'));
    }
  };
  const running = view.kind === 'running';
  const result = view.kind === 'completed' || view.kind === 'rejected' ? view.result : undefined;
  const error = view.kind === 'failed' || view.kind === 'uncertain' ? view.error : undefined;
  const diagnostics = view.kind === 'failed' || view.kind === 'uncertain' ? view.diagnostics : undefined;
  const progress = view.kind === 'running' ? progressLabel(view.progress, language) : null;

  return <section className="tools-output-details gp-execution-details" data-testid="geoprocessing-output-details">
    <div className="tools-subheading-row"><h4>{zh ? '服务端执行与结果' : 'Service execution and results'}</h4>
      {result && <button className="tools-text-button" type="button" onClick={() => downloadJson(result, tool)}>{zh ? '下载回执 JSON' : 'Download receipt JSON'}</button>}
    </div>
    <div className="tools-progress-line"><span className={`tools-state-dot ${running ? 'is-running' : view.kind === 'completed' ? 'is-ready' : ''}`} />
      <strong>{running ? (zh ? '正在处理已核验的地图来源…' : 'Processing verified map sources…')
        : view.kind === 'completed' ? (zh ? '服务端执行已返回' : 'Service execution returned')
          : view.kind === 'rejected' ? (zh ? '服务端未执行' : 'The service rejected execution')
            : view.kind === 'cancelled' ? (zh ? '服务端已确认取消' : 'The service confirmed cancellation')
              : view.kind === 'uncertain' ? (zh ? '执行终态尚未确认' : 'Execution terminal state is unconfirmed')
                : view.kind === 'failed' ? (zh ? '服务端执行失败' : 'Service execution failed')
                  : (zh ? '尚未运行' : 'Not run')}</strong>
    </div>
    {running && <>
      {progress?.label && <div className="tools-export-progress-label">{progress.label}</div>}
      {progress?.fraction !== undefined && <progress max={1} value={progress.fraction} />}
      {view.verifyingOutput && <p className="tools-field-help" role="status">{zh ? '服务端已返回输出路径，正在检查文件并读取实际栅格。' : 'The service returned an output path; inspecting the file and reading its raster contents.'}</p>}
      {view.transport && <p className="tools-field-help" role="status">{transportLabel(view.transport, language)}</p>}
      {operationID && <details><summary>{zh ? '操作诊断' : 'Operation diagnostics'}</summary><small>{operationID}</small></details>}
      <button className="tools-button tools-button-danger" type="button" onClick={onCancel} disabled={view.cancelRequested || view.verifyingOutput}>
        {view.verifyingOutput ? (zh ? '正在核验输出…' : 'Verifying output…')
          : view.cancelRequested ? (zh ? '正在等待取消终态…' : 'Waiting for cancellation terminal state…') : (zh ? '请求取消' : 'Request cancellation')}
      </button>
    </>}
    {view.kind === 'cancelled' && <p className="tools-field-help" role="status">{zh ? '已收到取消终态；本次结果未采纳。' : 'A cancellation terminal state was received; no result was adopted.'}</p>}
    {view.kind === 'uncertain' && <p className="tools-inline-error" role="alert">{text(language, '未收到可核验的完成或取消回执。请检查服务端操作状态后再决定是否重试。', 'No verifiable completion or cancellation receipt was received. Check the service operation status before retrying.')}</p>}
    {error !== undefined && <div className="tools-inline-error" role="alert">{message(error, language)}
      {diagnostics !== undefined && <details><summary>{zh ? '原始诊断' : 'Raw diagnostic'}</summary><pre>{JSON.stringify(diagnostics ?? errorDiagnostics(error), null, 2)}</pre></details>}
    </div>}
    {result && <>
      <dl className="tools-result-grid"><div><dt>{zh ? '工具' : 'Tool'}</dt><dd>{result.outputTitle}</dd></div>
        <div><dt>{zh ? '输入数量' : 'Inputs'}</dt><dd>{tool.inputKind === 'Polygon' ? result.inputPolygonCount : tool.inputKind === 'Polyline' ? result.inputLineCount : result.inputPointCount}</dd></div>
        <div><dt>{zh ? '输出类型' : 'Output kind'}</dt><dd>{displayToolOption(result.outputKind, language)}</dd></div>
        {result.outputRasterWidth !== null && result.outputRasterHeight !== null && <div><dt>{zh ? '栅格尺寸' : 'Raster size'}</dt><dd>{result.outputRasterWidth} × {result.outputRasterHeight}</dd></div>}
        <div><dt>{zh ? '输出方式' : 'Output'}</dt><dd>{displayToolOption(result.outputHandler, language)}</dd></div>
        {result.outputPath && <div><dt>{zh ? '服务器文件' : 'Server file'}</dt><dd>{result.outputPath}</dd></div>}
      </dl>
      {result.validationIssues.length > 0 && <div className="tools-validation-result">
        {result.validationIssues.map((issue, index) => <div className={`tools-validation-item severity-${issue.severity.toLowerCase()}`} key={`${issue.field}-${index}`}>
          <strong>{validationField(issue.field, language)}</strong><span>{validationMessage(issue.message, language)}</span><small>{language === 'zh'
            ? ({ error: '错误', typeerror: '类型错误', warning: '警告', information: '信息' }[issue.severity.toLowerCase()] ?? issue.severity)
            : issue.severity}</small>
        </div>)}
      </div>}
      {result.canExecute && result.outputHandler === 'Tiff' && <section className="gp-execution-preview">
        <h4>{zh ? 'TIFF 文件核验' : 'TIFF file verification'}</h4>
        {result.fileReceipt?.status === 'verified' && result.fileReceipt.inspection && result.fileReceipt.readback
          ? <><p className="tools-notice is-success">{zh ? '文件检查与栅格读取回执均匹配本次服务端输出。此文件保存在服务器工程目录。' : 'File inspection and raster readback both match this service output. The file remains under the server project directory.'}</p>
            <dl className="tools-result-grid"><div><dt>{zh ? 'SHA-256' : 'SHA-256'}</dt><dd>{result.fileReceipt.inspection.revision}</dd></div>
              <div><dt>{zh ? '文件长度' : 'File length'}</dt><dd>{result.fileReceipt.inspection.length} bytes</dd></div>
              <div><dt>{zh ? '实际格式 / 类型' : 'Readback format / type'}</dt><dd>{result.fileReceipt.readback.driver} · {result.fileReceipt.readback.dataType}</dd></div>
              <div><dt>{zh ? '实际尺寸 / 波段' : 'Readback size / bands'}</dt><dd>{result.fileReceipt.readback.width} × {result.fileReceipt.readback.height} · {result.fileReceipt.readback.bandCount}</dd></div></dl></>
          : <div className="tools-inline-error" role="alert">{zh ? '输出路径已返回，但文件身份或实际栅格读取未通过核验；页面不会将其标记为可用 TIFF。' : 'An output path was returned, but file identity or raster readback was not verified. This page does not mark it as a usable TIFF.'}
            {result.fileReceipt?.error !== undefined && <details><summary>{zh ? '文件检查诊断' : 'File inspection diagnostic'}</summary><pre>{JSON.stringify(errorDiagnostics(result.fileReceipt.error), null, 2)}</pre></details>}</div>}
      </section>}
      {result.canExecute && result.outputHandler === 'Clipboard' && result.textOutput !== null && <div className="gp-execution-preview">
        <div className="gp-execution-preview-actions"><strong>{zh ? '服务端文本结果' : 'Service text result'}</strong>
          <button className="tools-text-button" type="button" onClick={() => void copyOutput(result.textOutput!)}>{zh ? '复制文本' : 'Copy text'}</button>
          <button className="tools-text-button" type="button" onClick={() => downloadText(result.textOutput!, tool, 'txt')}>{zh ? '下载文本' : 'Download text'}</button>
        </div>
        {clipboardMessage && <small role="status">{clipboardMessage}</small>}
        <pre className="tools-output-preview">{result.textOutput || (zh ? '服务端返回了空文本。' : 'The service returned empty text.')}</pre>
      </div>}
      {view.kind === 'rejected' && <p className="tools-field-help" role="status">{zh ? '参数或输入校验未通过；此回执没有标记执行成功。' : 'Parameter or input validation failed; this receipt does not indicate successful execution.'}</p>}
    </>}
    {view.kind === 'idle' && <p className="tools-muted">{zh ? '读取并核验工具所需的已保存地图来源后，可提交真实服务端执行。' : 'Load and verify the saved map sources required by the tool before requesting execution.'}</p>}
  </section>;
}
