import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { TopologyRegionsEditor } from './TopologyRegionsEditor';
import type { GeometryTopologyMapWorkspace } from './useGeometryTopologyMap';

export type GeometryTopologyRegionsPageProps = Omit<GeometryTopologyTablePageProps, 'kind'> & { mapWorkspace?: GeometryTopologyMapWorkspace };

export function GeometryTopologyRegionsPage(props: GeometryTopologyRegionsPageProps) {
  return <TopologyRegionsEditor {...props} />;
}

export default GeometryTopologyRegionsPage;
