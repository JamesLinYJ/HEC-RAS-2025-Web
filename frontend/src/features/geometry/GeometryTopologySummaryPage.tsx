import type { Language } from '../../shared/workbench';
import { gdiagnostic, gt } from './geometryText';
import type { GeometryTopologySnapshot } from './geometryTypes';

export function GeometryTopologySummaryPage({ language, snapshot, onNavigate }: { language: Language; snapshot?: GeometryTopologySnapshot; onNavigate: (page: 'nodes' | 'arcs' | 'regions') => void }) {
  const tables = [
    ['nodes', snapshot?.nodes.rows.length ?? 0, () => onNavigate('nodes')],
    ['arcs', snapshot?.arcs.rows.length ?? 0, () => onNavigate('arcs')],
    ['regions', snapshot?.regions.rows.length ?? 0, () => onNavigate('regions')],
  ] as const;
  return (
    <section className="geometry-panel">
      <header className="geometry-panel-heading"><div><h2>{gt(language, 'topologySummary')}</h2><p>{gt(language, 'validationNote')}</p></div><span className={`geometry-status-pill ${snapshot?.completion?.isComplete ? 'is-clean' : ''}`}>{snapshot?.completion ? (snapshot.completion.isComplete ? gt(language, 'complete') : gt(language, 'incomplete')) : gt(language, 'noData')}</span></header>
      <div className="geometry-grid three geometry-panel-body">
        {tables.map(([kind, count, navigate]) => <button type="button" className="geometry-stat geometry-stat-button" key={kind} onClick={navigate}><span className="geometry-stat-label">{gt(language, kind)}</span><strong className="geometry-stat-value">{snapshot ? count : '—'}</strong><span className="geometry-stat-detail">{gt(language, 'openTable')} →</span></button>)}
      </div>
      {snapshot?.completion?.messages?.length ? <div className="geometry-report-list">{snapshot.completion.messages.map((item) => <details className={`geometry-report-item ${item.severity.toLowerCase() === 'error' ? 'is-error' : item.severity.toLowerCase() === 'warning' ? 'is-warning' : ''}`} key={item.id}><summary>{gt(language, 'diagnosticDetails')}</summary><pre>{item.code} · {gdiagnostic(language, item.text)}</pre></details>)}</div> : null}
    </section>
  );
}

export default GeometryTopologySummaryPage;
