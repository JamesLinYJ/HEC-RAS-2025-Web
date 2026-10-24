import type { Language } from '../../shared/workbench';
import { TopologyControl } from './TopologyControl';
import type { GeometryTopologySnapshot } from './geometryTypes';

export function GeometryTopologySummaryPage(props: { language: Language; snapshot?: GeometryTopologySnapshot; onNavigate: (page: 'nodes' | 'arcs' | 'regions') => void }) {
  return <TopologyControl {...props} />;
}

export default GeometryTopologySummaryPage;
