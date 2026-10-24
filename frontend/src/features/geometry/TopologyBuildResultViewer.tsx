import type { Language } from '../../shared/workbench';
import type { GeometryTopologySnapshot } from './geometryTypes';

/** Displays the retained build/completion diagnostics without translating source text or codes. */
export function TopologyBuildResultViewer({ language, snapshot }: { language: Language; snapshot?: GeometryTopologySnapshot }) {
  const messages = snapshot?.completion?.messages ?? [];
  return <section className="geometry-panel" aria-label={language === 'zh' ? '拓扑构建结果' : 'Topology build result'}>
    <header className="geometry-panel-heading"><div>
      <h3>{language === 'zh' ? '拓扑构建结果' : 'Topology build result'}</h3>
      <p>{language === 'zh' ? '显示当前拓扑草稿返回的原始诊断和目标。' : 'Displays the original diagnostics and targets returned for the current topology draft.'}</p>
    </div><span className="geometry-count-pill">{messages.length} {language === 'zh' ? '项' : 'items'}</span></header>
    {!snapshot?.completion ? <div className="geometry-empty"><strong>{language === 'zh' ? '当前快照没有构建结果。' : 'The current snapshot has no build result.'}</strong></div>
      : messages.length === 0 ? <div className="geometry-panel-body"><span className="geometry-ok">{language === 'zh' ? '完成检查没有返回诊断项。' : 'The completion check returned no diagnostics.'}</span></div>
        : <div className="geometry-report-list">{messages.map((item) => <details className={`geometry-report-item ${item.severity.toLowerCase() === 'error' ? 'is-error' : item.severity.toLowerCase() === 'warning' ? 'is-warning' : ''}`} key={item.id}>
          <summary>{item.severity} · {item.code} · {item.text}</summary>
          <pre style={{ whiteSpace: 'pre-wrap' }}>{item.text}</pre>
          {!!item.logData?.length && <dl className="geometry-topology-log-data">{item.logData.map((entry, index) => <div key={`${entry.key}-${index}`}>
            <dt>{entry.key}</dt><dd>{entry.value}</dd></div>)}</dl>}
          {item.targets?.length ? <div className="geometry-inline-help">{item.targets.map((target) => `${target.featureKind} ${target.fid} (${target.tableRowID})`).join(' · ')}</div> : null}
        </details>)}</div>}
  </section>;
}

export default TopologyBuildResultViewer;
