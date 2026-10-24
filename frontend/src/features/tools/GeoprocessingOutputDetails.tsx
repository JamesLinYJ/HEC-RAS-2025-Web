import type { Language } from '../../shared/workbench';
import { displayToolOption } from './GeoprocessingToolBase';

export type GeoprocessingResponse = {
  canExecute: boolean; operation: string; outputTitle: string; inputLineCount: number; outputLineCount: number;
  outputPath?: string | null; clipboardText?: string | null; outputs?: unknown[]; validationIssues?: Array<{ field: string; message: string; severity: string }>;
  outputKind?: string; polygonOutputs?: unknown[] | null; inputPolygonCount?: number; outputPolygonCount?: number;
  textOutput?: string | null; outputFormat?: string | null; inputPointCount?: number; outputRasterWidth?: number | null; outputRasterHeight?: number | null;
};

function downloadJson(value: unknown, language: Language): void {
  const url = URL.createObjectURL(new Blob([JSON.stringify(value, null, 2)], { type: 'application/json' }));
  const link = document.createElement('a'); link.href = url; link.download = (language === 'zh' ? '地理处理结果' : 'geoprocessing-result') + '.json'; link.click();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}

export function GeoprocessingOutputDetails({ language, result, progress, running, error }: {
  language: Language; result?: GeoprocessingResponse; progress?: unknown; running: boolean; error?: string;
}) {
  const zh = language === 'zh';
  const outputKindLabel = (value: string | null | undefined): string => value ? displayToolOption(value, language) : '—';
  return <div className="tools-output-details">
    <div className="tools-subheading-row"><h4>{zh ? '执行与结果' : 'Execution and results'}</h4>{result && <button className="tools-text-button" type="button" onClick={() => downloadJson(result, language)}>{zh ? '导出 JSON' : 'Export JSON'}</button>}</div>
    <div className="tools-progress-line"><span className={`tools-state-dot ${running ? 'is-running' : result?.canExecute ? 'is-ready' : ''}`} /><strong>{running ? (zh ? '正在执行 C# 工具…' : 'Running C# tool…') : result ? (result.canExecute ? (zh ? '执行完成' : 'Completed') : (zh ? '返回未执行' : 'Execution was not accepted')) : (zh ? '尚未运行' : 'Not run')}</strong></div>
    {running && <button className="tools-button tools-button-danger" type="button" disabled title={zh ? '当前执行 RPC 未连接，无法发送取消帧' : 'Execution RPC is not connected, so no cancellation frame can be sent'}>{zh ? '取消执行' : 'Cancel execution'}</button>}
    {progress !== undefined && <pre className="tools-output-preview">{typeof progress === 'string' ? progress : JSON.stringify(progress, null, 2)}</pre>}
    {error && <div className="tools-inline-error" role="alert">{zh ? '后端原始错误：' : 'Backend error: '}{error}</div>}
    {result && <><dl className="tools-result-grid"><div><dt>{zh ? '工具' : 'Tool'}</dt><dd>{result.outputTitle}</dd></div><div><dt>{zh ? '输出类型' : 'Output kind'}</dt><dd>{outputKindLabel(result.outputKind)}</dd></div><div><dt>{zh ? '输入折线' : 'Input lines'}</dt><dd>{result.inputLineCount}</dd></div><div><dt>{zh ? '输出折线' : 'Output lines'}</dt><dd>{result.outputLineCount}</dd></div><div><dt>{zh ? '输入多边形' : 'Input polygons'}</dt><dd>{result.inputPolygonCount ?? '—'}</dd></div><div><dt>{zh ? '输出多边形' : 'Output polygons'}</dt><dd>{result.outputPolygonCount ?? '—'}</dd></div><div><dt>{zh ? '栅格尺寸' : 'Raster size'}</dt><dd>{result.outputRasterWidth && result.outputRasterHeight ? `${result.outputRasterWidth} × ${result.outputRasterHeight}` : '—'}</dd></div><div><dt>{zh ? '文件路径' : 'Output path'}</dt><dd>{result.outputPath ?? '—'}</dd></div></dl>
      {result.textOutput && <pre className="tools-output-preview">{result.textOutput}</pre>}{result.clipboardText && <pre className="tools-output-preview">{result.clipboardText}</pre>}
    </>}
    {!result && !error && !running && <p className="tools-muted">{zh ? '完整地图源与几何输入接通后，后端进度、验证、结果摘要和导出在此显示。' : 'Backend progress, validation, result summary, and export will appear here after map source and geometry inputs are connected.'}</p>}
  </div>;
}
