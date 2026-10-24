import { useEffect, useMemo, useState } from 'react';
import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { ArcEditor } from './ArcEditor';
import { ArcPointsEditorDialog } from './ArcPointsEditorDialog';
import { ArcsTable } from './ArcsTable';
import { ConceptualMeshTopologyArcsEditor } from './ConceptualMeshTopologyArcsEditor';
import { DetailedArcHeader } from './DetailedArcHeader';
import { TopologyArcQuickEditPicker } from './TopologyArcQuickEditPicker';
import { TopologyFeatureErrorControl } from './TopologyFeatureErrorControl';
import { TagFilterControl } from './TagFilterControl';
import type { GeometryRow } from './geometryTypes';
import { GeometryTopologyMapPanel } from './GeometryTopologyMapPanel';
import type { GeometryTopologyMapWorkspace } from './useGeometryTopologyMap';

export type TopologyArcActiveFeaturesProps = Omit<GeometryTopologyTablePageProps, 'kind'> & {
  mapWorkspace?: GeometryTopologyMapWorkspace;
};

/** Source arc active-feature context: one stable row selection drives the table, filters, and details. */
export function TopologyArcActiveFeatures(props: TopologyArcActiveFeaturesProps) {
  const [selectedIDs, setSelectedIDs] = useState<string[]>([]);
  const [editingArcFID, setEditingArcFID] = useState<number>();
  const rows = useMemo(() => [...(props.table?.rows ?? []), ...props.localRows], [props.table?.rows, props.localRows]);
  const selectedRows = rows.filter((row) => selectedIDs.includes(row.id));
  const mapArcSelection = props.mapWorkspace?.canvasProps.selectedFIDByKind.arc ?? [];
  const mapArcSelectionKey = mapArcSelection.join(',');
  const hasMapWorkspace = !!props.mapWorkspace;
  const selectRows = (selected: GeometryRow[]): void => {
    setSelectedIDs(selected.map((row) => row.id));
    props.onSelectionChange?.(selected);
  };
  const resize = (row: GeometryRow, operation: 'halfSize' | 'doubleSize'): void => props.onTopologyResize?.('arc', row, operation);

  useEffect(() => {
    const available = new Set(rows.map((row) => row.id));
    setSelectedIDs((current) => {
      const retained = current.filter((id) => available.has(id));
      return retained.length === current.length ? current : retained;
    });
  }, [rows]);

  useEffect(() => {
    if (!hasMapWorkspace) return;
    const wanted = new Set(mapArcSelectionKey ? mapArcSelectionKey.split(',').map(Number) : []);
    const next = rows.filter((row) => Number.isInteger(row.fid) && wanted.has(row.fid!)).map((row) => row.id);
    setSelectedIDs((current) => current.length === next.length && current.every((id, index) => id === next[index]) ? current : next);
  }, [hasMapWorkspace, mapArcSelectionKey, rows]);

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
    <TopologyArcQuickEditPicker language={props.language} rows={selectedRows} canEdit={props.canEdit} busy={props.busy} onResize={resize} />
    <TopologyFeatureErrorControl language={props.language} featureKind="arc" rows={selectedRows} snapshot={props.topologySnapshot} />
    {editingArcFID != null && props.onLoadArcPoints && props.onArcPointCoordinateChange ? <ArcPointsEditorDialog language={props.language}
      arcFID={editingArcFID} snapshot={props.topologySnapshot} canEdit={props.canEdit} busy={props.busy}
      onLoad={props.onLoadArcPoints} onCoordinateChange={props.onArcPointCoordinateChange} onClose={() => setEditingArcFID(undefined)} /> : null}
  </div>;
}

export default TopologyArcActiveFeatures;
