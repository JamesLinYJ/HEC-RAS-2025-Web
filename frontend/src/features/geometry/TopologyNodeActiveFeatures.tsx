import { useMemo } from 'react';
import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { GeometryFeatureTable } from './GeometryFeatureTable';
import type { GeometryRow } from './geometryTypes';

export type TopologyNodeActiveFeaturesProps = Omit<GeometryTopologyTablePageProps, 'kind'> & {
  selectedRowIDs: string[];
  onSelectRows: (rows: GeometryRow[]) => void;
};

/** Source ActiveFeaturesControl list for source-backed nodes; selection ownership stays in TopologyNodesEditor. */
export function TopologyNodeActiveFeatures({ selectedRowIDs, onSelectRows, ...props }: TopologyNodeActiveFeaturesProps) {
  const rows = useMemo(() => [...(props.table?.rows ?? []), ...props.localRows], [props.table?.rows, props.localRows]);
  return <div className="geometry-grid">
    <GeometryFeatureTable language={props.language} kind="node" table={props.table} localRows={props.localRows}
      editable={props.canEdit} busy={props.busy} onChange={props.onChange} onDelete={props.canEdit ? props.onDelete : undefined}
      allowLocalAdd={false} selectedRowIDs={selectedRowIDs}
      onSelect={(ids) => onSelectRows(rows.filter((row) => ids.includes(row.id)))} />
  </div>;
}

export default TopologyNodeActiveFeatures;
