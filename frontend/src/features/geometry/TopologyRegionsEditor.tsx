import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { TopologyControlPointsActiveFeatures } from './TopologyControlPointsActiveFeatures';
import type { GeometryTopologyMapWorkspace } from './useGeometryTopologyMap';

export type TopologyRegionsEditorProps = Omit<GeometryTopologyTablePageProps, 'kind'> & { mapWorkspace?: GeometryTopologyMapWorkspace };

export function TopologyRegionsEditor(props: TopologyRegionsEditorProps) {
  return <TopologyControlPointsActiveFeatures {...props} />;
}

export default TopologyRegionsEditor;
