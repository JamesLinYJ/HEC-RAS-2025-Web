import type { Language } from '../../shared/workbench';
import { gt } from './geometryText';
import type { GeometryTopologySnapshot } from './geometryTypes';

export interface TopologyControlProps {
  language: Language;
  snapshot?: GeometryTopologySnapshot;
  onNavigate: (page: 'nodes' | 'arcs' | 'regions') => void;
}

/** Topology overview and build-result surface backed by the current topology snapshot. */
export function TopologyControl({ language, snapshot, onNavigate }: TopologyControlProps) {
  const tables = [
    ['nodes', snapshot?.nodes.rows.length ?? 0, () => onNavigate('nodes')],
    ['arcs', snapshot?.arcs.rows.length ?? 0, () => onNavigate('arcs')],
    ['regions', snapshot?.regions.rows.length ?? 0, () => onNavigate('regions')],
  ] as const;
  const complete = snapshot?.completion?.isComplete;
  return <section className="geometry-panel">
    <header className="geometry-panel-heading"><div><h2>{gt(language, 'topologySummary')}</h2><p>{language === 'zh' ? '当前源拓扑、所选要素表与完成检查结果。' : 'Current source topology, feature tables, and completion-check result.'}</p></div>
      <span className={`geometry-status-pill ${complete ? 'is-clean' : complete === false ? 'is-dirty' : ''}`}>{snapshot?.completion ? (complete ? gt(language, 'complete') : gt(language, 'incomplete')) : gt(language, 'noData')}</span>
    </header>
    {snapshot && <div className="geometry-table-footer"><span>{gt(language, 'revision')}: {snapshot.revision}</span><span>{language === 'zh' ? '拓扑草稿' : 'Topology draft'}: {snapshot.draftState}</span></div>}
    <div className="geometry-grid three geometry-panel-body">
      {tables.map(([kind, count, navigate]) => <button type="button" className="geometry-stat geometry-stat-button" key={kind} onClick={navigate}>
        <span className="geometry-stat-label">{gt(language, kind)}</span><strong className="geometry-stat-value">{snapshot ? count : '—'}</strong><span className="geometry-stat-detail">{gt(language, 'openTable')} →</span>
      </button>)}
    </div>
    {snapshot?.completion?.messages?.length ? <div className="geometry-report-list">{snapshot.completion.messages.map((item) => <details className={`geometry-report-item ${item.severity.toLowerCase() === 'error' ? 'is-error' : item.severity.toLowerCase() === 'warning' ? 'is-warning' : ''}`} key={item.id}>
      <summary>{item.severity} · {item.code} · {item.text}</summary>
      <pre style={{ whiteSpace: 'pre-wrap' }}>{item.text}</pre>
      {!!item.logData?.length && <dl className="geometry-topology-log-data">{item.logData.map((entry, index) => <div key={`${entry.key}-${index}`}><dt>{entry.key}</dt><dd>{entry.value}</dd></div>)}</dl>}
      {item.targets?.length ? <div className="geometry-inline-help">{item.targets.map((target) => `${target.featureKind} ${target.fid} (${target.tableRowID})`).join(' · ')}</div> : null}
    </details>)}</div> : null}
  </section>;
}

export default TopologyControl;
