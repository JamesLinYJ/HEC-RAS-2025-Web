import type { Language } from '../../shared/workbench';
import { gt } from './geometryText';
import { MeshErrorProperties } from './MeshErrorProperties';
import type { GeometryMeshDegreeScan, GeometryMeshSnapshot } from './geometryTypes';

export interface MeshDiagnosticsProps {
  language: Language;
  snapshot?: GeometryMeshSnapshot;
  scan?: GeometryMeshDegreeScan;
  rawOutput?: string;
}

/** Aggregates diagnostics that the host actually returns for the current mesh snapshot and scan. */
export function MeshDiagnostics({ language, snapshot, scan, rawOutput }: MeshDiagnosticsProps) {
  const errorAreas = snapshot?.flowAreas.filter((area) => area.hasMeshErrors) ?? [];
  const report = snapshot?.generationReport;
  return <div className="geometry-grid">
    <section className="geometry-panel" aria-label={language === 'zh' ? '网格诊断' : 'Mesh diagnostics'}>
      <header className="geometry-panel-heading"><div><h3>{language === 'zh' ? '网格生成诊断' : 'Mesh generation diagnostics'}</h3><p>{snapshot ? `${language === 'zh' ? '源修订' : 'Source revision'} ${snapshot.revision} · ${language === 'zh' ? '网格草稿' : 'Mesh draft'} ${snapshot.draftState}` : gt(language, 'meshNoData')}</p></div>
        <span className={`geometry-status-pill ${errorAreas.length === 0 && report?.succeeded ? 'is-clean' : errorAreas.length || report?.partial ? 'is-dirty' : ''}`}>{errorAreas.length ? `${errorAreas.length} ${language === 'zh' ? '个区域有错误' : 'areas with errors'}` : report ? (report.succeeded ? gt(language, 'complete') : gt(language, 'incomplete')) : gt(language, 'noData')}</span>
      </header>
      {errorAreas.length ? <div className="geometry-report-list">{errorAreas.map((area) => <div className="geometry-report-item is-error" key={area.id}>
        <strong>{area.name || area.id}</strong><span> · {language === 'zh' ? '流动区域' : 'Flow area'} {area.id}</span>
      </div>)}</div> : snapshot && <div className="geometry-panel-body"><span className="geometry-ok">{language === 'zh' ? '当前流动区域快照未标记网格错误。' : 'No flow-area mesh errors are marked in the current snapshot.'}</span></div>}
      {report && <div className="geometry-panel-body">
        <div className="geometry-kv-grid">
          <div className="geometry-kv"><span>{language === 'zh' ? '生成状态' : 'Generation status'}</span><strong>{report.succeeded ? gt(language, 'complete') : gt(language, 'incomplete')}</strong></div>
          <div className="geometry-kv"><span>{language === 'zh' ? '部分完成' : 'Partial'}</span><strong>{report.partial ? gt(language, 'yes') : gt(language, 'no')}</strong></div>
          <div className="geometry-kv"><span>{language === 'zh' ? '替换现有网格' : 'Mesh replaced'}</span><strong>{report.meshWasReplaced ? gt(language, 'yes') : gt(language, 'no')}</strong></div>
          <div className="geometry-kv"><span>{gt(language, 'cells')}</span><strong>{report.cellCount ?? '—'}</strong></div>
        </div>
        {report.messages.length > 0 && <details className="geometry-report-item" style={{ marginTop: 10 }}><summary>{gt(language, 'generationDetails')} · {report.messages.length}</summary>
          {report.messages.map((message, index) => <pre key={`${index}-${message}`} style={{ whiteSpace: 'pre-wrap' }}>{message}</pre>)}
        </details>}
      </div>}
      {!snapshot && <div className="geometry-empty"><strong>{gt(language, 'meshNoData')}</strong></div>}
      <div className="geometry-inline-help">{language === 'zh' ? '诊断文本按服务端原文显示；生成状态和错误标记来自当前网格响应。' : 'Diagnostic text is shown as returned by the host; report state and error flags come from the current mesh response.'}</div>
    </section>
    <MeshErrorProperties language={language} scan={scan} rawOutput={rawOutput} />
  </div>;
}

export default MeshDiagnostics;
