import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { ConceptualMeshTopologyArcsEditor } from './ConceptualMeshTopologyArcsEditor';

export type TopologyArcsEditorProps = Omit<GeometryTopologyTablePageProps, 'kind'>;

export function TopologyArcsEditor(props: TopologyArcsEditorProps) {
  return <ConceptualMeshTopologyArcsEditor {...props} />;
}

export default TopologyArcsEditor;
