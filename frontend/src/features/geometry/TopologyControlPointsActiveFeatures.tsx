import { useMemo } from 'react';
import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { ConceptualMeshTopologyRegionsEditor } from './ConceptualMeshTopologyRegionsEditor';
import { RegionsTable } from './RegionsTable';
import { TagFilterControl } from './TagFilterControl';
import { GeometryTopologyMapPanel } from './GeometryTopologyMapPanel';
import type { GeometryTopologyMapWorkspace } from './useGeometryTopologyMap';
import { useTopologyTableSelection } from './useTopologyTableSelection';

export type TopologyControlPointsActiveFeaturesProps = Omit<GeometryTopologyTablePageProps, 'kind'> & { mapWorkspace?: GeometryTopologyMapWorkspace };

/** Source control-point active-feature list; table selection, tag filtering, and editors share row FIDs. */
export function TopologyControlPointsActiveFeatures(props: TopologyControlPointsActiveFeaturesProps) {
  const rows = useMemo(() => [...(props.table?.rows ?? []), ...props.localRows], [props.table?.rows, props.localRows]);
  const { selectedIDs, selectedRows, selectRows } = useTopologyTableSelection('region', rows, props.topologySnapshot,
    props.mapWorkspace, props.onSelectionChange);

  return <div className="geometry-grid">
    {props.mapWorkspace && <GeometryTopologyMapPanel language={props.language} workspace={props.mapWorkspace} allowMapEditing={false} />}
    <RegionsTable {...props} selectedRowIDs={selectedIDs} onSelectionChange={selectRows} />
    <TagFilterControl language={props.language} featureKind="region" rows={rows} snapshot={props.topologySnapshot}
      canFilter={!!props.canFilterTopology} busy={props.busy} onFilter={props.onTopologyFilter}
      onSelectMatches={(fids) => {
        const matches = new Set(fids);
        selectRows(rows.filter((row) => row.fid != null && matches.has(row.fid)));
      }} />
    <ConceptualMeshTopologyRegionsEditor {...props} selectedRows={selectedRows} />
  </div>;
}

export default TopologyControlPointsActiveFeatures;
