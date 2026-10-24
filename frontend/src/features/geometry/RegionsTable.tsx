import { useMemo } from 'react';
import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { GeometryFeatureTable } from './GeometryFeatureTable';
import type { GeometryRow } from './geometryTypes';

export type RegionsTableProps = Omit<GeometryTopologyTablePageProps, 'kind'> & {
  onSelectionChange?: (rows: GeometryRow[]) => void;
  selectedRowIDs?: string[];
};

/** Source-backed control-point/region list with row identity preserved for property and recipe tools. */
export function RegionsTable(props: RegionsTableProps) {
  const rows = useMemo(() => [...(props.table?.rows ?? []), ...props.localRows], [props.table?.rows, props.localRows]);
  return <GeometryFeatureTable
    language={props.language}
    kind="region"
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

export default RegionsTable;
