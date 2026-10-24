import { useEffect, useMemo, useState } from 'react';
import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { ConceptualMeshTopologyRegionsEditor } from './ConceptualMeshTopologyRegionsEditor';
import { RegionsTable } from './RegionsTable';
import { TagFilterControl } from './TagFilterControl';
import type { GeometryRow } from './geometryTypes';

export type TopologyControlPointsActiveFeaturesProps = Omit<GeometryTopologyTablePageProps, 'kind'>;

/** Source control-point active-feature list; table selection, tag filtering, and editors share row FIDs. */
export function TopologyControlPointsActiveFeatures(props: TopologyControlPointsActiveFeaturesProps) {
  const [selectedIDs, setSelectedIDs] = useState<string[]>([]);
  const rows = useMemo(() => [...(props.table?.rows ?? []), ...props.localRows], [props.table?.rows, props.localRows]);
  const selectedRows = rows.filter((row) => selectedIDs.includes(row.id));
  const selectRows = (selected: GeometryRow[]): void => {
    setSelectedIDs(selected.map((row) => row.id));
    props.onSelectionChange?.(selected);
  };

  useEffect(() => {
    const available = new Set(rows.map((row) => row.id));
    setSelectedIDs((current) => {
      const retained = current.filter((id) => available.has(id));
      return retained.length === current.length ? current : retained;
    });
  }, [rows]);

  return <div className="geometry-grid">
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
