import { useEffect, useMemo, useState } from 'react';
import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { TopologyNodeActiveFeatures } from './TopologyNodeActiveFeatures';
import { NodeEditor } from './NodeEditor';
import { TopologyFeatureErrorControl } from './TopologyFeatureErrorControl';
import type { GeometryRow } from './geometryTypes';
import { GeometryTopologyMapPanel } from './GeometryTopologyMapPanel';
import type { GeometryTopologyMapWorkspace } from './useGeometryTopologyMap';

export type TopologyNodesEditorProps = Omit<GeometryTopologyTablePageProps, 'kind'> & {
  mapWorkspace?: GeometryTopologyMapWorkspace;
};

export function TopologyNodesEditor(props: TopologyNodesEditorProps) {
  const [selectedIDs, setSelectedIDs] = useState<string[]>([]);
  const rows = useMemo(() => [...(props.table?.rows ?? []), ...props.localRows], [props.table?.rows, props.localRows]);
  const selectedRows = rows.filter((row) => selectedIDs.includes(row.id));

  useEffect(() => {
    const available = new Set(rows.map((row) => row.id));
    setSelectedIDs((current) => {
      const retained = current.filter((id) => available.has(id));
      return retained.length === current.length ? current : retained;
    });
  }, [rows]);

  const selectRows = (nextRows: GeometryRow[]): void => {
    const nextIDs = nextRows.map((row) => row.id);
    setSelectedIDs(nextIDs);
    props.onSelectionChange?.(nextRows);
  };

  const mapNodeSelection = props.mapWorkspace?.canvasProps.selectedFIDByKind.node ?? [];
  const mapNodeSelectionKey = mapNodeSelection.join(',');
  useEffect(() => {
    if (!props.mapWorkspace) return;
    const wanted = new Set(mapNodeSelection);
    const next = rows.filter((row) => Number.isInteger(row.fid) && wanted.has(row.fid!)).map((row) => row.id);
    setSelectedIDs((current) => current.length === next.length && current.every((id, index) => id === next[index]) ? current : next);
  }, [mapNodeSelectionKey, props.mapWorkspace, rows]);

  return <div className="geometry-grid" data-testid="geometry-topology-nodes-editor">
    <TopologyNodeActiveFeatures {...props} selectedRowIDs={selectedIDs} onSelectRows={selectRows} />
    {props.mapWorkspace && <GeometryTopologyMapPanel language={props.language} workspace={props.mapWorkspace} />}
    <NodeEditor {...props} selectedRows={selectedRows} />
    <TopologyFeatureErrorControl language={props.language} featureKind="node" rows={selectedRows} snapshot={props.topologySnapshot} />
  </div>;
}

export default TopologyNodesEditor;
