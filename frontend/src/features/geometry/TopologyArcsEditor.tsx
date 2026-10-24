import { useMemo, useState } from 'react';
import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { ConceptualMeshArcEditor } from './ConceptualMeshArcEditor';
import { ArcEditor } from './ArcEditor';
import { ArcPointsEditorDialog } from './ArcPointsEditorDialog';
import { TopologyArcQuickEditPicker } from './TopologyArcQuickEditPicker';
import { TopologyFeatureErrorControl } from './TopologyFeatureErrorControl';
import type { GeometryRow } from './geometryTypes';

export type TopologyArcsEditorProps = Omit<GeometryTopologyTablePageProps, 'kind'>;

export function TopologyArcsEditor(props: TopologyArcsEditorProps) {
  const [selectedIDs, setSelectedIDs] = useState<string[]>([]);
  const [editingArcFID, setEditingArcFID] = useState<number>();
  const tableRows = useMemo(() => [...(props.table?.rows ?? []), ...props.localRows], [props.table?.rows, props.localRows]);
  const selectedRows = tableRows.filter((row) => selectedIDs.includes(row.id));

  const onSelect = (rows: GeometryRow[]): void => setSelectedIDs(rows.map((row) => row.id));
  const resize = (row: GeometryRow, operation: 'halfSize' | 'doubleSize'): void => props.onTopologyResize?.('arc', row, operation);

  return (
    <div className="geometry-grid">
      <ConceptualMeshArcEditor {...props} onSelectionChange={onSelect} />
      <ArcEditor language={props.language} rows={selectedRows} snapshot={props.topologySnapshot}
        canEdit={props.canEdit} busy={props.busy}
        onTagValueChange={props.onTopologyTagChange ? (rows, name, value) => props.onTopologyTagChange!('arc', rows, name, value) : undefined}
        onCreateTag={props.onTopologyTagCreate ? (name, type) => props.onTopologyTagCreate!('arc', name, type) : undefined}
        onEditPoints={props.canLoadArcPoints && props.onLoadArcPoints && props.onArcPointCoordinateChange ? setEditingArcFID : undefined} />
      <TopologyArcQuickEditPicker language={props.language} rows={selectedRows} canEdit={props.canEdit} busy={props.busy} onResize={resize} />
      <TopologyFeatureErrorControl language={props.language} featureKind="arc" rows={selectedRows} snapshot={props.topologySnapshot} />
      {editingArcFID != null && props.onLoadArcPoints && props.onArcPointCoordinateChange ? <ArcPointsEditorDialog language={props.language}
        arcFID={editingArcFID} snapshot={props.topologySnapshot} canEdit={props.canEdit} busy={props.busy}
        onLoad={props.onLoadArcPoints} onCoordinateChange={props.onArcPointCoordinateChange} onClose={() => setEditingArcFID(undefined)} /> : null}
    </div>
  );
}

export default TopologyArcsEditor;
