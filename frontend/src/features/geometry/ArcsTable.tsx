import { useMemo } from 'react';
import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { GeometryFeatureTable } from './GeometryFeatureTable';
import type { GeometryRow } from './geometryTypes';

export type ArcsTableProps = Omit<GeometryTopologyTablePageProps, 'kind'> & {
  onSelectionChange?: (rows: GeometryRow[]) => void;
  selectedRowIDs?: string[];
};

/** Source-backed arc list; the selection is returned as stable table rows for the detail panes. */
export function ArcsTable(props: ArcsTableProps) {
  const rows = useMemo(() => [...(props.table?.rows ?? []), ...props.localRows], [props.table?.rows, props.localRows]);
  return <GeometryFeatureTable
    language={props.language}
    kind="arc"
    table={props.table}
    localRows={props.localRows}
    editable={props.canEdit}
    allowLocalAdd
    busy={props.busy}
    onChange={props.onChange}
    onDelete={props.onDelete}
    onLocalAdd={props.onLocalAdd}
    selectedRowIDs={props.selectedRowIDs}
    onSelect={(ids) => props.onSelectionChange?.(rows.filter((row) => ids.includes(row.id)))}
  />;
}

export default ArcsTable;
