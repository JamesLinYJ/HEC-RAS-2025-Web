import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { TopologyArcsEditor } from './TopologyArcsEditor';

export type GeometryTopologyArcsPageProps = Omit<GeometryTopologyTablePageProps, 'kind'>;

export function GeometryTopologyArcsPage(props: GeometryTopologyArcsPageProps) {
  return <TopologyArcsEditor {...props} />;
}

export default GeometryTopologyArcsPage;
