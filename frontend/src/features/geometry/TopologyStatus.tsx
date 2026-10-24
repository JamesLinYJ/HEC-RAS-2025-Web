import type { Language } from '../../shared/workbench';
import { gt } from './geometryText';
import type { GeometryTopologySnapshot } from './geometryTypes';

export function TopologyStatus({ language, snapshot }: { language: Language; snapshot?: GeometryTopologySnapshot }) {
  const complete = snapshot?.completion?.isComplete;
  return <section className="geometry-panel" aria-label={language === 'zh' ? '拓扑状态' : 'Topology status'}>
    <header className="geometry-panel-heading"><div>
      <h3>{language === 'zh' ? '拓扑状态' : 'Topology status'}</h3>
      <p>{language === 'zh' ? '来自当前源拓扑快照及其完成检查。' : 'From the current source topology snapshot and its completion check.'}</p>
    </div>
      <span className={`geometry-status-pill ${complete ? 'is-clean' : complete === false ? 'is-dirty' : ''}`}>
        {snapshot?.completion ? (complete ? gt(language, 'complete') : gt(language, 'incomplete')) : gt(language, 'noData')}
      </span>
    </header>
    {!snapshot && <div className="geometry-empty"><strong>{language === 'zh' ? '尚未加载拓扑快照。' : 'The topology snapshot has not loaded.'}</strong></div>}
  </section>;
}

export default TopologyStatus;
