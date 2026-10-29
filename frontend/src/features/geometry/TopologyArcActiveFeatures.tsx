import { useMemo, useState } from 'react';
import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { ArcEditor } from './ArcEditor';
import { ArcPointsEditorDialog } from './ArcPointsEditorDialog';
import { ArcsTable } from './ArcsTable';
import { ConceptualMeshTopologyArcsEditor } from './ConceptualMeshTopologyArcsEditor';
import { DetailedArcHeader } from './DetailedArcHeader';
import { TopologyArcQuickEditPicker } from './TopologyArcQuickEditPicker';
import { TopologyFeatureErrorControl } from './TopologyFeatureErrorControl';
import { TagFilterControl } from './TagFilterControl';
import { GeometryTopologyMapPanel } from './GeometryTopologyMapPanel';
import type { GeometryTopologyMapWorkspace } from './useGeometryTopologyMap';
import { useTopologyTableSelection } from './useTopologyTableSelection';

export type TopologyArcActiveFeaturesProps = Omit<GeometryTopologyTablePageProps, 'kind'> & {
  mapWorkspace?: GeometryTopologyMapWorkspace;
};

/** Source arc active-feature context: one stable row selection drives the table, filters, and details. */
export function TopologyArcActiveFeatures(props: TopologyArcActiveFeaturesProps) {
  const [editingArcFID, setEditingArcFID] = useState<number>();
  const rows = useMemo(() => [...(props.table?.rows ?? []), ...props.localRows], [props.table?.rows, props.localRows]);
  const { selectedIDs, selectedRows, selectRows } = useTopologyTableSelection('arc', rows, props.topologySnapshot,
    props.mapWorkspace, props.onSelectionChange);

  return <div className="geometry-grid">
    {props.mapWorkspace && <GeometryTopologyMapPanel language={props.language} workspace={props.mapWorkspace} showArcTools />}
    <ArcsTable {...props} selectedRowIDs={selectedIDs} onSelectionChange={selectRows} />
    <TagFilterControl language={props.language} featureKind="arc" rows={rows} snapshot={props.topologySnapshot}
      canFilter={!!props.canFilterTopology} busy={props.busy} onFilter={props.onTopologyFilter}
      onSelectMatches={(fids) => {
        const matches = new Set(fids);
        selectRows(rows.filter((row) => row.fid != null && matches.has(row.fid)));
      }} />
    <DetailedArcHeader language={props.language} rows={selectedRows} snapshot={props.topologySnapshot} />
    <ConceptualMeshTopologyArcsEditor {...props} selectedRows={selectedRows} />
    <ArcEditor language={props.language} rows={selectedRows} snapshot={props.topologySnapshot} canEdit={props.canEdit} busy={props.busy}
      onTagValueChange={props.onTopologyTagChange ? (selected, name, value) => props.onTopologyTagChange!('arc', selected, name, value) : undefined}
      onCreateTag={props.onTopologyTagCreate ? (name, type) => props.onTopologyTagCreate!('arc', name, type) : undefined}
      onEditPoints={props.canLoadArcPoints && props.onLoadArcPoints && props.onArcPointCoordinateChange ? setEditingArcFID : undefined} />
    <TopologyArcQuickEditPicker language={props.language} rows={selectedRows} snapshot={props.topologySnapshot} canEdit={props.canEdit} busy={props.busy}
      onEdit={props.onTopologyQuickEdit ? (row, edit) => props.onTopologyQuickEdit!('arc', row, edit) : undefined} />
    <TopologyFeatureErrorControl language={props.language} featureKind="arc" rows={selectedRows} snapshot={props.topologySnapshot}
      onLocate={props.onLocateTopologyDiagnostic} />
    {editingArcFID != null && props.onLoadArcPoints && props.onArcPointCoordinateChange ? <ArcPointsEditorDialog language={props.language}
      arcFID={editingArcFID} snapshot={props.topologySnapshot} canEdit={props.canEdit} busy={props.busy}
      onLoad={props.onLoadArcPoints} onCoordinateChange={props.onArcPointCoordinateChange} onClose={() => setEditingArcFID(undefined)} /> : null}
  </div>;
}

export default TopologyArcActiveFeatures;
