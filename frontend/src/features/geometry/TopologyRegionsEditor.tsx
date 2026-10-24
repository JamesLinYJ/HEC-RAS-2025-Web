import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { ConceptualMeshTopologyRegionsEditor } from './ConceptualMeshTopologyRegionsEditor';

export type TopologyRegionsEditorProps = Omit<GeometryTopologyTablePageProps, 'kind'>;

export function TopologyRegionsEditor(props: TopologyRegionsEditorProps) {
  return <ConceptualMeshTopologyRegionsEditor {...props} />;
}

export default TopologyRegionsEditor;
