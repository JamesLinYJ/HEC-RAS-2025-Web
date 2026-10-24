import { GeometryTopologyTablePage, type GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';

export type GeometryTopologyNodesPageProps = Omit<GeometryTopologyTablePageProps, 'kind'>;

export function GeometryTopologyNodesPage(props: GeometryTopologyNodesPageProps) {
  return <GeometryTopologyTablePage {...props} kind="node" />;
}

export default GeometryTopologyNodesPage;
