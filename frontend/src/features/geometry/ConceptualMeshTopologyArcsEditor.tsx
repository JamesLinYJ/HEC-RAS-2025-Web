import { useMemo, useState } from 'react';
import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { ArcEditor } from './ArcEditor';
import { ArcPointsEditorDialog } from './ArcPointsEditorDialog';
import { ArcsTable } from './ArcsTable';
import { ConceptualMeshArcEditor } from './ConceptualMeshArcEditor';
import { DetailedArcHeader } from './DetailedArcHeader';
import { TopologyArcQuickEditPicker } from './TopologyArcQuickEditPicker';
import { TopologyFeatureErrorControl } from './TopologyFeatureErrorControl';
import type { GeometryRow } from './geometryTypes';

export type ConceptualMeshTopologyArcsEditorProps = Omit<GeometryTopologyTablePageProps, 'kind'>;

/** Topology arc selection and conceptual-mesh sizing share one source-backed row selection. */
export function ConceptualMeshTopologyArcsEditor(props: ConceptualMeshTopologyArcsEditorProps) {
  const [selectedIDs, setSelectedIDs] = useState<string[]>([]);
  const [editingArcFID, setEditingArcFID] = useState<number>();
  const rows = useMemo(() => [...(props.table?.rows ?? []), ...props.localRows], [props.table?.rows, props.localRows]);
  const selectedRows = rows.filter((row) => selectedIDs.includes(row.id));
  const selectRows = (selected: GeometryRow[]): void => {
    setSelectedIDs(selected.map((row) => row.id));
    props.onSelectionChange?.(selected);
  };
  const resize = (row: GeometryRow, operation: 'halfSize' | 'doubleSize'): void => props.onTopologyResize?.('arc', row, operation);

  return <div className="geometry-grid">
    <ArcsTable {...props} onSelectionChange={selectRows} />
    <DetailedArcHeader language={props.language} rows={selectedRows} snapshot={props.topologySnapshot} />
    <ConceptualMeshArcEditor {...props} hideTable selectedRowsOverride={selectedRows} />
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

export default ConceptualMeshTopologyArcsEditor;
