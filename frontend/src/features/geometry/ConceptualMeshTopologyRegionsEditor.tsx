import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { ConceptualMeshControlPointEditor } from './ConceptualMeshControlPointEditor';
import { ControlPointEditor } from './ControlPointEditor';
import { TopologyFeatureErrorControl } from './TopologyFeatureErrorControl';
import { TopologyRecipeBuilder } from './TopologyRecipeBuilder';
import { TopologyRegionQuickEditPicker } from './TopologyRegionQuickEditPicker';
import { ConceptualMeshContextualControl } from './ConceptualMeshContextualControl';
import type { GeometryRow } from './geometryTypes';

export type ConceptualMeshTopologyRegionsEditorProps = Omit<GeometryTopologyTablePageProps, 'kind'> & { selectedRows: GeometryRow[] };

/** Source conceptual-mesh and recipe details for active control points; selection is owned by the active-feature list. */
export function ConceptualMeshTopologyRegionsEditor({ selectedRows, ...props }: ConceptualMeshTopologyRegionsEditorProps) {
  const resize = (row: GeometryRow, operation: 'halfSize' | 'doubleSize', axis?: 'NorthSouth' | 'EastWest'): void =>
    props.onTopologyResize?.('region', row, operation, axis);

  return <div className="geometry-grid">
    <ConceptualMeshContextualControl language={props.language} featureKind="region" rows={selectedRows}
      canEdit={props.canEdit} busy={props.busy} onApplyFactor={props.onApplyTopologyFactor} />
    <ConceptualMeshControlPointEditor {...props} hideTable selectedRowsOverride={selectedRows} />
    <ControlPointEditor language={props.language} rows={selectedRows} snapshot={props.topologySnapshot} canEdit={props.canEdit} busy={props.busy}
      onTagValueChange={props.onTopologyTagChange ? (selected, name, value) => props.onTopologyTagChange!('region', selected, name, value) : undefined}
      onCreateTag={props.onTopologyTagCreate ? (name, type) => props.onTopologyTagCreate!('region', name, type) : undefined} />
    <TopologyRegionQuickEditPicker language={props.language} rows={selectedRows} canEdit={props.canEdit} busy={props.busy} onResize={resize} />
    <TopologyRecipeBuilder language={props.language} rows={selectedRows} snapshot={props.topologySnapshot}
      canEdit={props.canEdit} busy={props.busy} supported={!!props.canLoadRecipe}
      onLoadRecipe={props.onLoadRecipe} onApplyRecipe={props.onApplyRecipe} />
    <TopologyFeatureErrorControl language={props.language} featureKind="region" rows={selectedRows} snapshot={props.topologySnapshot} />
  </div>;
}

export default ConceptualMeshTopologyRegionsEditor;
