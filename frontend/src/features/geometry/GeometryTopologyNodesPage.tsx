import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import type { GeometryTopologyMapWorkspace } from './useGeometryTopologyMap';
import { TopologyNodesEditor } from './TopologyNodesEditor';

export type GeometryTopologyNodesPageProps = Omit<GeometryTopologyTablePageProps, 'kind'> & {
  mapWorkspace?: GeometryTopologyMapWorkspace;
};

export function GeometryTopologyNodesPage(props: GeometryTopologyNodesPageProps) {
  return <TopologyNodesEditor {...props} />;
}

export default GeometryTopologyNodesPage;
