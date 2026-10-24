import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { TopologyRegionsEditor } from './TopologyRegionsEditor';

export type GeometryTopologyRegionsPageProps = Omit<GeometryTopologyTablePageProps, 'kind'>;

export function GeometryTopologyRegionsPage(props: GeometryTopologyRegionsPageProps) {
  return <TopologyRegionsEditor {...props} />;
}

export default GeometryTopologyRegionsPage;
