import { useEffect, useMemo, useState } from 'react';
import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { GeometryFeatureTable } from './GeometryFeatureTable';
import { NodeEditor } from './NodeEditor';
import { TopologyFeatureErrorControl } from './TopologyFeatureErrorControl';

export type TopologyNodeActiveFeaturesProps = Omit<GeometryTopologyTablePageProps, 'kind'>;

/** Source node active-feature context with shared row selection for details and diagnostics. */
export function TopologyNodeActiveFeatures(props: TopologyNodeActiveFeaturesProps) {
  const [selectedIDs, setSelectedIDs] = useState<string[]>([]);
  const rows = useMemo(() => [...(props.table?.rows ?? []), ...props.localRows], [props.table?.rows, props.localRows]);
  const selectedRows = rows.filter((row) => selectedIDs.includes(row.id));
  const selectRows = (ids: string[]): void => {
    setSelectedIDs(ids);
    props.onSelectionChange?.(rows.filter((row) => ids.includes(row.id)));
  };

  useEffect(() => {
    const available = new Set(rows.map((row) => row.id));
    setSelectedIDs((current) => {
      const retained = current.filter((id) => available.has(id));
      return retained.length === current.length ? current : retained;
    });
  }, [rows]);

  return <div className="geometry-grid">
    <GeometryFeatureTable language={props.language} kind="node" table={props.table} localRows={props.localRows}
      editable={props.canEdit} busy={props.busy} onChange={props.onChange} onDelete={props.onDelete}
      selectedRowIDs={selectedIDs} onSelect={selectRows} />
    <NodeEditor {...props} selectedRows={selectedRows} />
    <TopologyFeatureErrorControl language={props.language} featureKind="node" rows={selectedRows} snapshot={props.topologySnapshot} />
    <div className="geometry-inline-help">
      {props.language === 'zh'
        ? '节点工作区沿用原选择联动表和当前拓扑诊断；坐标只读，当前服务契约未提供节点移动操作。'
        : 'The node workspace follows the source selection-linked list and current topology diagnostics. Coordinates are read-only because the current host has no node-move command.'}
    </div>
  </div>;
}

export default TopologyNodeActiveFeatures;
