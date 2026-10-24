import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { ConceptualMeshControlPointEditor } from './ConceptualMeshControlPointEditor';

export type GeometryTopologyRegionsPageProps = Omit<GeometryTopologyTablePageProps, 'kind'>;

export function GeometryTopologyRegionsPage(props: GeometryTopologyRegionsPageProps) {
  return <ConceptualMeshControlPointEditor {...props} />;
}

export default GeometryTopologyRegionsPage;
