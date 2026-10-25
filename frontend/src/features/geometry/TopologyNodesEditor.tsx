import { useMemo } from 'react';
import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { TopologyNodeActiveFeatures } from './TopologyNodeActiveFeatures';
import { NodeEditor } from './NodeEditor';
import { TopologyFeatureErrorControl } from './TopologyFeatureErrorControl';
import { GeometryTopologyMapPanel } from './GeometryTopologyMapPanel';
import type { GeometryTopologyMapWorkspace } from './useGeometryTopologyMap';
import { useTopologyTableSelection } from './useTopologyTableSelection';

export type TopologyNodesEditorProps = Omit<GeometryTopologyTablePageProps, 'kind'> & {
  mapWorkspace?: GeometryTopologyMapWorkspace;
};

export function TopologyNodesEditor(props: TopologyNodesEditorProps) {
  const rows = useMemo(() => [...(props.table?.rows ?? []), ...props.localRows], [props.table?.rows, props.localRows]);
  const { selectedIDs, selectedRows, selectRows } = useTopologyTableSelection('node', rows, props.topologySnapshot,
    props.mapWorkspace, props.onSelectionChange);

  return <div className="geometry-grid" data-testid="geometry-topology-nodes-editor">
    <TopologyNodeActiveFeatures {...props} selectedRowIDs={selectedIDs} onSelectRows={selectRows} />
    {props.mapWorkspace && <GeometryTopologyMapPanel language={props.language} workspace={props.mapWorkspace} />}
    <NodeEditor {...props} selectedRows={selectedRows} />
    <TopologyFeatureErrorControl language={props.language} featureKind="node" rows={selectedRows} snapshot={props.topologySnapshot}
      onLocate={props.onLocateTopologyDiagnostic} />
  </div>;
}

export default TopologyNodesEditor;
