import type { Language } from '../../shared/workbench';
import { gt } from './geometryText';
import type { GeometryRow, GeometryTopologySnapshot } from './geometryTypes';
import { GeometryTopologyDiagnosticTargets } from './GeometryTopologyDiagnosticTargets';
import { topologyDiagnosticKind, topologySourceRows, type TopologyDiagnosticLocator } from './geometryTopologySelection';

export interface TopologyFeatureErrorControlProps {
  language: Language;
  featureKind: 'node' | 'arc' | 'region';
  rows: GeometryRow[];
  snapshot?: GeometryTopologySnapshot;
  onLocate?: TopologyDiagnosticLocator;
}

export function TopologyFeatureErrorControl({ language, featureKind, rows, snapshot, onLocate }: TopologyFeatureErrorControlProps) {
  const selected = snapshot ? topologySourceRows(snapshot, featureKind, rows) : [];
  const matched = (snapshot?.completion?.messages ?? []).filter((message) =>
    message.targets?.some((target) => selected.some((row) => topologyDiagnosticKind(target.featureKind) === featureKind
      && target.fid === row.fid && target.tableRowID === row.id)),
  );

  return (
    <section className="geometry-panel" aria-label={language === 'zh' ? '拓扑要素诊断' : 'Topology feature diagnostics'}>
      <header className="geometry-panel-heading"><div>
        <h3>{language === 'zh' ? '所选要素的拓扑诊断' : 'Topology diagnostics for selected features'}</h3>
        <p>{matched.length} {language === 'zh' ? '项 · 来自当前草稿的完成检查' : 'items · from the current draft completion check'}</p>
      </div></header>
      {!snapshot?.completion ? (
        <div className="geometry-empty"><strong>{language === 'zh' ? '当前拓扑响应尚无完成诊断。' : 'The current topology response has no completion diagnostics.'}</strong></div>
      ) : selected.length === 0 ? (
        <div className="geometry-empty"><strong>{language === 'zh' ? '选择一个源要素后查看其诊断。' : 'Select a source feature to see its diagnostics.'}</strong></div>
      ) : matched.length === 0 ? (
        <div className="geometry-panel-body"><span className="geometry-ok">{language === 'zh' ? '完成检查未返回所选要素的诊断。' : 'The completion check returned no diagnostics for the selection.'}</span></div>
      ) : (
        <div className="geometry-report-list">
          {matched.map((message) => <details className={`geometry-report-item ${message.severity.toLowerCase() === 'error' ? 'is-error' : message.severity.toLowerCase() === 'warning' ? 'is-warning' : ''}`} key={message.id}>
            <summary>{message.severity} · {message.code} · {message.text}</summary>
            <pre style={{ whiteSpace: 'pre-wrap' }}>{message.text}</pre>
            {!!message.logData?.length && <dl className="geometry-topology-log-data">{message.logData.map((entry, index) => <div key={`${entry.key}-${index}`}><dt>{entry.key}</dt><dd>{entry.value}</dd></div>)}</dl>}
            <GeometryTopologyDiagnosticTargets language={language} snapshot={snapshot} message={message} onLocate={onLocate} />
          </details>)}
        </div>
      )}
      <div className="geometry-inline-help">{gt(language, 'completion')} · {snapshot?.completion?.sourceRevision ?? '—'} · {snapshot?.completion?.draftIdentity ?? '—'}</div>
    </section>
  );
}

export default TopologyFeatureErrorControl;
