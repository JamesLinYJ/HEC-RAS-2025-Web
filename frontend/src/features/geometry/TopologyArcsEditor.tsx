import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { TopologyArcActiveFeatures } from './TopologyArcActiveFeatures';
import type { GeometryTopologyMapWorkspace } from './useGeometryTopologyMap';

export type TopologyArcsEditorProps = Omit<GeometryTopologyTablePageProps, 'kind'> & {
  mapWorkspace?: GeometryTopologyMapWorkspace;
};

export function TopologyArcsEditor(props: TopologyArcsEditorProps) {
  return <TopologyArcActiveFeatures {...props} />;
}

export default TopologyArcsEditor;
