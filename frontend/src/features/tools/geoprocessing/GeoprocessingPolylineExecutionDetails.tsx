import { useState } from 'react';
import type { BackendTransportState, Language } from '../../../shared/workbench';
import { errorDiagnostics, message, text } from '../../../shared/workbench';
import type { GeoprocessingToolDefinition } from '../GeoprocessingToolBase';
import type { AttributeTableSourceCapture, AttributeTableSourceFamilyCapture } from '../attributes/attributeTableTypes';
import type { GeoprocessingMapLayerWorkflowResult } from './geoprocessingMapLayerWorkflow';

export interface GeoprocessingPolylineOutput {
  featureIdentity: string;
  pathIdentity: string;
  sourceFeatureIndex: number;
  pathIndex: number;
  coordinates: number[][];
  zValues?: string[];
  mValues?: string[];
}

export interface GeoprocessingPolygonOutput {
  featureIdentity: string;
  pathIdentity: string;
  sourceFeatureIndex: number;
  pathIndex: number;
  coordinates: number[][];
  zValues?: string[];
  mValues?: string[];
  holes: number[][][];
  holeZValues: Array<string[] | null>;
  holeMValues: Array<string[] | null>;
}

export interface GeoprocessingShapefileOutputReceipt {
  status: 'verified' | 'unverified';
  inspection?: AttributeTableSourceCapture;
  family?: AttributeTableSourceFamilyCapture;
  error?: unknown;
}

export type GeoprocessingMapLayerOutputReceipt = GeoprocessingMapLayerWorkflowResult | {
  status: 'notCommitted';
  stage: 'output-inspect';
  outputPath: string;
  error: unknown;
  diagnostics?: unknown;
};

export interface GeoprocessingPolylineResult {
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
  outputFormat: string | null;
  outputs: GeoprocessingPolylineOutput[];
  polygonOutputs: GeoprocessingPolygonOutput[];
  validationIssues: Array<{ field: string; message: string; severity: string }>;
  capturedPathIdentities: string[];
  capturedPolygonPathIdentities: string[];
  capturedPointIdentities: string[];
  outputKind: string;
  outputHandler: 'Clipboard' | 'Shapefile' | 'Map Layer';
  shapefileReceipt?: GeoprocessingShapefileOutputReceipt;
  mapLayerReceipt?: GeoprocessingMapLayerOutputReceipt;
}

export type GeoprocessingExecutionView =
  | { kind: 'idle' }
  | { kind: 'running'; progress?: unknown; transport?: BackendTransportState; cancelRequested: boolean; verifyingOutput?: boolean; importingMapLayer?: boolean }
  | { kind: 'completed'; result: GeoprocessingPolylineResult }
  | { kind: 'rejected'; result: GeoprocessingPolylineResult }
  | { kind: 'cancelled' }
  | { kind: 'failed'; error: unknown; diagnostics?: unknown }
  | { kind: 'uncertain'; error: unknown; diagnostics?: unknown };

function MapLayerReceiptDetails({ language, receipt }: { language: Language; receipt: GeoprocessingMapLayerOutputReceipt }) {
  const zh = language === 'zh';
  if (receipt.status === 'committed') {
    return <div className="tools-notice is-success" role="status" data-testid="geoprocessing-map-layer-committed">
      {zh ? `地图图层已提交到“${receipt.receipt.mapName}”（${receipt.receipt.mapType}，索引 ${receipt.receipt.mapIndex}）；可视化修订 ${receipt.receipt.visualizationRevision}。`
        : `The map layer was committed to “${receipt.receipt.mapName}” (${receipt.receipt.mapType}, index ${receipt.receipt.mapIndex}); visualization revision ${receipt.receipt.visualizationRevision}.`}
    </div>;
  }
  if (receipt.status === 'receiptNotAdopted') {
    const diagnostic = receipt.diagnostics ?? ('adoptionError' in receipt ? receipt.adoptionError : undefined);
    return <div className="tools-inline-error" role="alert" data-testid="geoprocessing-map-layer-receipt-not-adopted">
      <p>{zh ? `服务已返回地图提交回执，但当前工作区没有接纳该回执。文件已生成；请刷新地图目录核对“${receipt.receipt.mapName}”，不要直接重试。`
        : `The service returned a map commit receipt, but the current workbench did not adopt it. The file was generated; refresh the map catalog to reconcile “${receipt.receipt.mapName}” and do not retry directly.`}</p>
      {diagnostic !== undefined && <details><summary>{zh ? '原始诊断' : 'Raw diagnostic'}</summary><pre>{JSON.stringify(errorDiagnostics(diagnostic), null, 2)}</pre></details>}
    </div>;
  }
  if (receipt.status === 'uncertain') {
    return <div className="tools-inline-error" role="alert" data-testid="geoprocessing-map-layer-uncertain">
      <p>{zh ? `Shapefile 文件已生成，但地图提交终态未确认。目标为“${receipt.targetMap.name}”；请刷新地图目录核对后再决定下一步，切勿直接重试。`
        : `The Shapefile was generated, but the map commit outcome is unconfirmed for “${receipt.targetMap.name}”. Refresh the map catalog before deciding what to do; do not retry directly.`}</p>
      <p>{zh ? '输出路径' : 'Output path'} · {receipt.outputPath}</p>
      <details><summary>{zh ? '原始诊断' : 'Raw diagnostic'}</summary><pre>{JSON.stringify(receipt.diagnostics ?? errorDiagnostics(receipt.error), null, 2)}</pre></details>
    </div>;
  }
  return <div className="tools-inline-error" role="status" data-testid="geoprocessing-map-layer-not-added">
    <p>{receipt.stage === 'output-inspect'
      ? (zh ? '服务已返回 Shapefile 输出路径，但输出文件族没有通过核验；未将文件加入地图。' : 'The service returned a Shapefile output path, but the output family did not pass verification. It was not added to the map.')
      : (zh ? 'Shapefile 文件已生成并通过文件族核验，但地图导入未提交；文件仍保存在服务器输出路径。' : 'The Shapefile was generated and its family was verified, but map import was not submitted. The file remains at the server output path.')}</p>
    <p>{zh ? '输出路径' : 'Output path'} · {receipt.outputPath}</p>
    <details><summary>{zh ? '原始诊断' : 'Raw diagnostic'}</summary><pre>{JSON.stringify(receipt.diagnostics ?? errorDiagnostics(receipt.error), null, 2)}</pre></details>
  </div>;
}

function downloadText(value: string, tool: GeoprocessingToolDefinition, outputKind: string): void {
  const geoJson = outputKind === 'Polygon';
  const blob = new Blob([value], { type: geoJson ? 'application/geo+json;charset=utf-8' : 'text/plain;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = `${tool.id}-service-output.${geoJson ? 'geojson' : 'txt'}`;
  link.click();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}

function downloadJson(value: unknown, tool: GeoprocessingToolDefinition): void {
  const blob = new Blob([JSON.stringify(value, null, 2)], { type: 'application/json' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = `${tool.id}-host-result.json`;
  link.click();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}

function progressSummary(value: unknown, language: Language): { fraction?: number; label?: string } | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null;
  const record = value as Record<string, unknown>;
  const fraction = typeof record.fraction === 'number' && Number.isFinite(record.fraction)
    ? Math.max(0, Math.min(1, record.fraction)) : undefined;
  const name = typeof record.taskName === 'string' ? record.taskName : '';
  const zhLabels: Record<string, string> = {
    'Checking source lines': '正在核对来源折线',
    'Filtering polylines': '正在简化折线',
    'Smoothing polylines': '正在平滑折线',
    'Reversing polylines': '正在反转折线方向',
    'Filtering polylines by length': '正在按长度筛除折线',
    'Densifying polylines': '正在加密折线顶点',
    'Merging polylines': '正在合并折线',
    'Offsetting polylines': '正在偏移折线',
    'Checking captured map features': '正在核对地图来源几何',
    'Buffering polylines': '正在生成折线缓冲区',
    'Converting polylines to polygons': '正在将折线转换为多边形',
    'Buffering polygons': '正在生成多边形缓冲区',
    'Densifying polygons': '正在加密多边形边界',
    'Converting polygon boundaries': '正在转换多边形边界',
    'Writing Shapefile output': '正在写入 Shapefile',
    'Finishing output': '正在完成输出',
    'Finishing geoprocessing output': '正在完成地理处理输出',
  };
  const label = name ? (language === 'zh' ? zhLabels[name] ?? name : name) : undefined;
  return fraction === undefined && label === undefined ? null : { fraction, label };
}

function fieldLabel(field: string, language: Language): string {
  if (language !== 'zh') return field;
  const labels: Record<string, string> = {
    Tolerance: '简化容差', MovingAverageWindow: '移动平均窗口', MovingAveragePasses: '移动平均次数',
    MinLength: '最小长度', MaxSegmentLength: '最大线段长度', Mode: '分支处理', Sides: '偏移侧',
    BufferWidth: '缓冲宽度', BufferDistance: '缓冲距离', SmoothingMethod: '平滑方法',
    OutputName: '输出名称',
  };
  return labels[field] ?? field;
}

function issueMessage(value: string, language: Language): string {
  if (language !== 'zh') return value;
  if (value.startsWith('Not valid ') && value.endsWith(' type.')) return '请输入有效的数值。';
  const translations: Record<string, string> = {
    'Choose a supported merge mode.': '请选择有效的合并分支处理方式。',
    'Choose a supported offset side.': '请选择有效的折线偏移侧。',
    'The value is outside the allowed range.': '数值超出允许范围。',
  };
  return translations[value] ?? value;
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

export function GeoprocessingPolylineExecutionDetails({ language, tool, view, operationID, onCancel }:
  { language: Language; tool: GeoprocessingToolDefinition; view: GeoprocessingExecutionView;
    operationID: string | null; onCancel: () => void }) {
  const zh = language === 'zh';
  const [clipboardMessage, setClipboardMessage] = useState<string>();
  const copyOutput = async (value: string): Promise<void> => {
    setClipboardMessage(undefined);
    try {
      if (!navigator.clipboard?.writeText) throw new Error('Clipboard access is unavailable.');
      await navigator.clipboard.writeText(value);
      setClipboardMessage(text(language, '已将服务端返回文本复制到剪贴板。', 'The text returned by the service was copied to the clipboard.'));
    } catch (error) {
      setClipboardMessage(text(language, '无法访问系统剪贴板；可下载同一份服务端结果文本。', 'System clipboard access failed; download the same service result instead.'));
      void error;
    }
  };
  const running = view.kind === 'running';
  const result = view.kind === 'completed' || view.kind === 'rejected' ? view.result : undefined;
  const progress = view.kind === 'running' ? progressSummary(view.progress, language) : null;
  const error = view.kind === 'failed' || view.kind === 'uncertain' ? view.error : undefined;
  const diagnostics = view.kind === 'failed' || view.kind === 'uncertain' ? view.diagnostics : undefined;

  return <section className="tools-output-details gp-execution-details">
    <div className="tools-subheading-row"><h4>{zh ? '服务端执行与结果预览' : 'Service execution and preview'}</h4>
      {result && <button className="tools-text-button" type="button" onClick={() => downloadJson(result, tool)}>{zh ? '下载回执 JSON' : 'Download receipt JSON'}</button>}
    </div>
    <div className="tools-progress-line"><span className={`tools-state-dot ${running ? 'is-running' : view.kind === 'completed' ? 'is-ready' : ''}`} />
      <strong>{running ? (zh ? `正在处理所选${tool.inputKind === 'Polygon' ? '多边形' : tool.inputKind === 'Point' ? '点' : '折线'}…` : `Processing the selected ${tool.inputKind.toLowerCase()}…`)
        : view.kind === 'completed' ? (zh ? '服务端执行完成' : 'Service execution completed')
          : view.kind === 'rejected' ? (zh ? '服务端未执行' : 'The service rejected execution')
            : view.kind === 'cancelled' ? (zh ? '服务端已确认取消' : 'The service confirmed cancellation')
              : view.kind === 'uncertain' ? (zh ? '执行终态尚未确认' : 'Execution terminal state is unconfirmed')
                : view.kind === 'failed' ? (zh ? '服务端执行失败' : 'Service execution failed')
                  : (zh ? '尚未运行' : 'Not run')}</strong>
    </div>
    {running && <>
      {progress?.label && <div className="tools-export-progress-label">{progress.label}</div>}
      {progress?.fraction !== undefined && <progress max={1} value={progress.fraction} />}
      {view.verifyingOutput && <p className="tools-field-help" role="status">{zh ? '地理处理已返回文件路径，正在核验 Shapefile 主文件 SHA-256 与完整文件族。' : 'The service returned an output path. Verifying the Shapefile primary SHA-256 and complete file family.'}</p>}
      {view.importingMapLayer && <p className="tools-field-help" role="status">{zh ? '输出文件族已核验，正在把图层提交到本次捕获的地图。' : 'The output family was verified. Committing the layer to the map captured for this run.'}</p>}
      {view.transport && <p className="tools-field-help" role="status">{transportLabel(view.transport, language)}</p>}
      {operationID && <details><summary>{zh ? '操作诊断' : 'Operation diagnostics'}</summary><small>{operationID}</small></details>}
      <button className="tools-button tools-button-danger" type="button" onClick={onCancel} disabled={view.cancelRequested || view.verifyingOutput || view.importingMapLayer}>
        {view.verifyingOutput ? (zh ? '正在核验输出…' : 'Verifying output…')
          : view.importingMapLayer ? (zh ? '正在提交地图图层…' : 'Committing map layer…')
          : view.cancelRequested ? (zh ? '正在等待取消终态…' : 'Waiting for cancellation terminal state…') : (zh ? '请求取消' : 'Request cancellation')}
      </button>
    </>}
    {view.kind === 'cancelled' && <p className="tools-field-help" role="status">{zh ? '已收到取消终态；本次执行结果未采纳。' : 'A cancellation terminal state was received; no execution result was adopted.'}</p>}
    {view.kind === 'uncertain' && <p className="tools-inline-error" role="alert">{text(language, '未收到可核验的完成或取消回执。请检查服务端操作状态后再决定是否重试。', 'No verifiable completion or cancellation receipt was received. Check the service operation state before deciding whether to retry.')}</p>}
    {error !== undefined && <div className="tools-inline-error" role="alert">{message(error, language)}
      {diagnostics !== undefined && <details><summary>{zh ? '原始诊断' : 'Raw diagnostic'}</summary><pre>{JSON.stringify(diagnostics ?? errorDiagnostics(error), null, 2)}</pre></details>}
    </div>}
    {result && <>
      <dl className="tools-result-grid"><div><dt>{zh ? '工具' : 'Tool'}</dt><dd>{tool.title[language]}</dd></div>
        <div><dt>{zh ? (tool.inputKind === 'Polygon' ? '输入多边形组件' : tool.inputKind === 'Point' ? '输入点' : '输入折线路径') : (tool.inputKind === 'Polygon' ? 'Input polygons' : tool.inputKind === 'Point' ? 'Input points' : 'Input paths')}</dt>
          <dd>{tool.inputKind === 'Polygon' ? result.inputPolygonCount : tool.inputKind === 'Point' ? result.inputPointCount : result.inputLineCount}</dd></div>
        <div><dt>{zh ? (result.outputKind === 'Polygon' ? '输出多边形' : '输出折线路径') : (result.outputKind === 'Polygon' ? 'Output polygons' : 'Output paths')}</dt>
          <dd>{result.outputKind === 'Polygon' ? result.outputPolygonCount : result.outputLineCount}</dd></div>
        <div><dt>{zh ? '输出方式' : 'Output'}</dt><dd>{result.outputHandler === 'Map Layer' ? (zh ? '地图图层（Shapefile）' : 'Map layer (Shapefile)') : result.outputHandler === 'Shapefile' ? 'Shapefile' : (zh ? '服务端文本预览' : 'Service text preview')}</dd></div>
      </dl>
      {result.validationIssues.length > 0 && <div className="tools-validation-result">
        {result.validationIssues.map((issue, index) => <div className={`tools-validation-item severity-${issue.severity.toLowerCase()}`} key={`${issue.field}-${index}`}>
          <strong>{fieldLabel(issue.field, language)}</strong><span>{issueMessage(issue.message, language)}</span><small>{language === 'zh' ? ({ error: '错误', typeerror: '类型错误', warning: '警告' }[issue.severity.toLowerCase()] ?? issue.severity) : issue.severity}</small>
        </div>)}
      </div>}
      {result.canExecute && (result.outputHandler === 'Shapefile' || result.outputHandler === 'Map Layer') && <section className="gp-execution-preview gp-shapefile-output-receipt">
        <div className="tools-subheading-row"><h4>{result.outputHandler === 'Map Layer' ? (zh ? '地图图层输出' : 'Map layer output') : (zh ? '服务器 Shapefile 输出' : 'Server Shapefile output')}</h4></div>
        {result.outputPath && <p className="tools-field-help">{zh ? '服务器输出路径' : 'Server output path'} · {result.outputPath}</p>}
        {result.shapefileReceipt?.status === 'verified' && result.shapefileReceipt.inspection && result.shapefileReceipt.family ? <>
          <p className="tools-notice is-success">{result.outputHandler === 'Map Layer'
            ? (zh ? '已核对输出主文件 SHA-256 和完整 Shapefile 文件族。' : 'The output primary SHA-256 and complete Shapefile family were verified.')
            : (zh ? '服务器已检查主文件 SHA-256 并捕获完整 Shapefile 文件族。文件保存在服务器路径，不会伪装成浏览器下载。' : 'The service inspected the primary SHA-256 and captured the complete Shapefile family. The file remains at its server path; it is not presented as a browser download.')}</p>
          <dl className="tools-result-grid"><div><dt>{zh ? '主文件 SHA-256' : 'Primary SHA-256'}</dt><dd>{result.shapefileReceipt.inspection.revision}</dd></div>
            <div><dt>{zh ? '主文件长度' : 'Primary file length'}</dt><dd>{result.shapefileReceipt.inspection.length} bytes</dd></div>
            <div><dt>{zh ? '文件族修订' : 'Family revision'}</dt><dd>{result.shapefileReceipt.family.revision}</dd></div></dl>
          <ul>{result.shapefileReceipt.family.files.map(file => <li key={file.role}>{file.role} · {file.exists ? (zh ? '已捕获' : 'captured') : (zh ? '可选文件不存在' : 'optional file absent')} · {file.revision ?? '—'} · {file.path}</li>)}</ul>
        </> : <div className="tools-inline-error" role="alert">{zh ? '服务端操作已返回，但完整输出文件族未通过检查；本页面不确认该 Shapefile 可用。' : 'The operation returned, but the complete output family did not pass inspection. This page does not confirm that the Shapefile is usable.'}
          {result.shapefileReceipt?.error !== undefined && <details><summary>{zh ? '文件检查诊断' : 'File inspection diagnostic'}</summary><pre>{JSON.stringify(errorDiagnostics(result.shapefileReceipt.error), null, 2)}</pre></details>}
        </div>}
        {result.outputHandler === 'Map Layer' && result.mapLayerReceipt && <MapLayerReceiptDetails language={language} receipt={result.mapLayerReceipt} />}
        {result.outputHandler === 'Map Layer' && !result.mapLayerReceipt && <p className="tools-field-help" role="status">{zh ? '输出文件已生成；尚未提交到地图图层。' : 'The output file was generated; it has not been added to a map layer.'}</p>}
      </section>}
      {result.canExecute && result.outputHandler === 'Clipboard' && result.clipboardText !== null && <div className="gp-execution-preview">
        <div className="gp-execution-preview-actions"><strong>{zh ? '文本结果' : 'Text result'}</strong>
          <button className="tools-text-button" type="button" onClick={() => void copyOutput(result.clipboardText!)}>{zh ? '复制文本' : 'Copy text'}</button>
          <button className="tools-text-button" type="button" onClick={() => downloadText(result.clipboardText!, tool, result.outputKind)}>{zh ? '下载文本' : 'Download text'}</button>
        </div>
        {clipboardMessage && <small role="status">{clipboardMessage}</small>}
        <pre className="tools-output-preview">{result.clipboardText || (zh ? '服务端返回了空文本（例如所有路径均被按长度筛除）。' : 'The service returned empty text (for example, all paths were removed by the length filter).')}</pre>
      </div>}
      {view.kind === 'rejected' && <p className="tools-field-help" role="status">{zh ? '参数或输入校验未通过；此回执没有标记执行成功。' : 'Parameter or input validation failed; this receipt does not indicate successful execution.'}</p>}
    </>}
    {view.kind === 'idle' && <p className="tools-muted">{zh ? '读取并核验已保存地图中的真实输入几何后，可提交服务端参数校验及执行。' : 'Load and verify real input geometry from a saved map before requesting service validation or execution.'}</p>}
  </section>;
}
