import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { TopologyNodesEditor } from './TopologyNodesEditor';

export type GeometryTopologyNodesPageProps = Omit<GeometryTopologyTablePageProps, 'kind'>;

export function GeometryTopologyNodesPage(props: GeometryTopologyNodesPageProps) {
  return <TopologyNodesEditor {...props} />;
}

export default GeometryTopologyNodesPage;
