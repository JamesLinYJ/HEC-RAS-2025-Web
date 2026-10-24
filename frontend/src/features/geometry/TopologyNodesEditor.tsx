import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { TopologyNodeActiveFeatures } from './TopologyNodeActiveFeatures';

export type TopologyNodesEditorProps = Omit<GeometryTopologyTablePageProps, 'kind'>;

export function TopologyNodesEditor(props: TopologyNodesEditorProps) {
  return <TopologyNodeActiveFeatures {...props} />;
}

export default TopologyNodesEditor;
