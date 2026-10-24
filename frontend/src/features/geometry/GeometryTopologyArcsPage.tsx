import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import type { GeometryTopologyMapWorkspace } from './useGeometryTopologyMap';
import { TopologyArcsEditor } from './TopologyArcsEditor';

export type GeometryTopologyArcsPageProps = Omit<GeometryTopologyTablePageProps, 'kind'> & {
  mapWorkspace?: GeometryTopologyMapWorkspace;
};

export function GeometryTopologyArcsPage(props: GeometryTopologyArcsPageProps) {
  return <TopologyArcsEditor {...props} />;
}

export default GeometryTopologyArcsPage;
