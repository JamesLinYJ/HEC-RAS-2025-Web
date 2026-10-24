import { useMemo, useState } from 'react';
import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { NodeEditor } from './NodeEditor';
import { TopologyFeatureErrorControl } from './TopologyFeatureErrorControl';

export type TopologyNodesEditorProps = Omit<GeometryTopologyTablePageProps, 'kind'>;

export function TopologyNodesEditor(props: TopologyNodesEditorProps) {
  const [selectedIDs, setSelectedIDs] = useState<string[]>([]);
  const selectedRows = useMemo(() => [...(props.table?.rows ?? []), ...props.localRows]
    .filter((row) => selectedIDs.includes(row.id)), [props.table?.rows, props.localRows, selectedIDs]);

  return (
    <div className="geometry-grid">
      <NodeEditor {...props} onSelectionChange={(rows) => { setSelectedIDs(rows.map((row) => row.id)); props.onSelectionChange?.(rows); }} />
      <TopologyFeatureErrorControl language={props.language} featureKind="node" rows={selectedRows} snapshot={props.topologySnapshot} />
      <div className="geometry-inline-help">
        {props.language === 'zh'
          ? '节点工作区沿用原选择联动表和当前拓扑诊断；坐标只读，当前服务契约未提供节点移动操作。'
          : 'The node workspace follows the source selection-linked list and current topology diagnostics. Coordinates are read-only because the current service contract has no node-move operation.'}
      </div>
    </div>
  );
}

export default TopologyNodesEditor;
