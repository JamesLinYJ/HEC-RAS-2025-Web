import { useEffect, useMemo, useState } from 'react';
import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { TopologyNodeActiveFeatures } from './TopologyNodeActiveFeatures';
import { NodeEditor } from './NodeEditor';
import { TopologyFeatureErrorControl } from './TopologyFeatureErrorControl';
import type { GeometryRow } from './geometryTypes';

export type TopologyNodesEditorProps = Omit<GeometryTopologyTablePageProps, 'kind'>;

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

  return <div className="geometry-grid" data-testid="geometry-topology-nodes-editor">
    <TopologyNodeActiveFeatures {...props} selectedRowIDs={selectedIDs} onSelectRows={selectRows} />
    <NodeEditor {...props} selectedRows={selectedRows} />
    <TopologyFeatureErrorControl language={props.language} featureKind="node" rows={selectedRows} snapshot={props.topologySnapshot} />
  </div>;
}

export default TopologyNodesEditor;
