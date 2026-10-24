import type { Language } from '../../shared/workbench';
import { gt } from './geometryText';
import type { GeometryTopologySnapshot } from './geometryTypes';
import { TopologyBuildResultViewer } from './TopologyBuildResultViewer';
import { TopologyStatus } from './TopologyStatus';
import { TopologyProperties } from './TopologyProperties';

export interface TopologyControlProps {
  language: Language;
  snapshot?: GeometryTopologySnapshot;
  onNavigate: (page: 'nodes' | 'arcs' | 'regions') => void;
  canEdit?: boolean;
  busy?: boolean;
  onApplyFactor?: (factor: number) => Promise<void>;
}

/** Topology overview and build-result surface backed by the current topology snapshot. */
export function TopologyControl({ language, snapshot, onNavigate, canEdit = false, busy = false, onApplyFactor }: TopologyControlProps) {
  const tables = [
    ['nodes', snapshot?.nodes.rows.length ?? 0, () => onNavigate('nodes')],
    ['arcs', snapshot?.arcs.rows.length ?? 0, () => onNavigate('arcs')],
    ['regions', snapshot?.regions.rows.length ?? 0, () => onNavigate('regions')],
  ] as const;
  return <section className="geometry-panel">
    <header className="geometry-panel-heading"><div><h2>{gt(language, 'topologySummary')}</h2><p>{language === 'zh' ? '当前源拓扑、所选要素表与完成检查结果。' : 'Current source topology, feature tables, and completion-check result.'}</p></div>
    </header>
    <div className="geometry-grid three geometry-panel-body">
      {tables.map(([kind, count, navigate]) => <button type="button" className="geometry-stat geometry-stat-button" key={kind} onClick={navigate}>
        <span className="geometry-stat-label">{gt(language, kind)}</span><strong className="geometry-stat-value">{snapshot ? count : '—'}</strong><span className="geometry-stat-detail">{gt(language, 'openTable')} →</span>
      </button>)}
    </div>
    <div className="geometry-grid two"><TopologyStatus language={language} snapshot={snapshot} />
      <TopologyBuildResultViewer language={language} snapshot={snapshot} /></div>
    <TopologyProperties language={language} snapshot={snapshot} canEdit={canEdit} busy={busy} onApplyFactor={onApplyFactor} />
  </section>;
}

export default TopologyControl;
