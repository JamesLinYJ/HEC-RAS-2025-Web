import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { ConceptualMeshArcEditor } from './ConceptualMeshArcEditor';

export type GeometryTopologyArcsPageProps = Omit<GeometryTopologyTablePageProps, 'kind'>;

export function GeometryTopologyArcsPage(props: GeometryTopologyArcsPageProps) {
  return <ConceptualMeshArcEditor {...props} />;
}

export default GeometryTopologyArcsPage;
