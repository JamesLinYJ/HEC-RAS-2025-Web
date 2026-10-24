import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { TopologyArcActiveFeatures } from './TopologyArcActiveFeatures';

export type TopologyArcsEditorProps = Omit<GeometryTopologyTablePageProps, 'kind'>;

export function TopologyArcsEditor(props: TopologyArcsEditorProps) {
  return <TopologyArcActiveFeatures {...props} />;
}

export default TopologyArcsEditor;
