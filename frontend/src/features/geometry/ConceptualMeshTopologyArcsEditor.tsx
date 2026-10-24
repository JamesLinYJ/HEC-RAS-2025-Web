import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { ConceptualMeshArcEditor } from './ConceptualMeshArcEditor';
import { ConceptualMeshContextualControl } from './ConceptualMeshContextualControl';
import type { GeometryRow } from './geometryTypes';

export type ConceptualMeshTopologyArcsEditorProps = Omit<GeometryTopologyTablePageProps, 'kind'> & { selectedRows: GeometryRow[] };

/** Source conceptual-mesh details for the active arc rows; selection remains owned by the active-feature list. */
export function ConceptualMeshTopologyArcsEditor({ selectedRows, ...props }: ConceptualMeshTopologyArcsEditorProps) {
  return <div className="geometry-grid">
    <ConceptualMeshContextualControl language={props.language} featureKind="arc" rows={selectedRows}
      canEdit={props.canEdit} busy={props.busy} onApplyFactor={props.onApplyTopologyFactor} />
    <ConceptualMeshArcEditor {...props} hideTable selectedRowsOverride={selectedRows} />
  </div>;
}

export default ConceptualMeshTopologyArcsEditor;
