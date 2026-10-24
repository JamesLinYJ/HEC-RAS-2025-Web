import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { TopologyControlPointsActiveFeatures } from './TopologyControlPointsActiveFeatures';

export type TopologyRegionsEditorProps = Omit<GeometryTopologyTablePageProps, 'kind'>;

export function TopologyRegionsEditor(props: TopologyRegionsEditorProps) {
  return <TopologyControlPointsActiveFeatures {...props} />;
}

export default TopologyRegionsEditor;
