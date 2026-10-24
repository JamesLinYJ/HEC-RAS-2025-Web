import { useMemo, useState } from 'react';
import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { ConceptualMeshControlPointEditor } from './ConceptualMeshControlPointEditor';
import { TopologyFeatureErrorControl } from './TopologyFeatureErrorControl';
import { TopologyRecipeBuilder } from './TopologyRecipeBuilder';
import { TopologyRegionQuickEditPicker } from './TopologyRegionQuickEditPicker';
import type { GeometryRow } from './geometryTypes';

export type TopologyRegionsEditorProps = Omit<GeometryTopologyTablePageProps, 'kind'>;

export function TopologyRegionsEditor(props: TopologyRegionsEditorProps) {
  const [selectedIDs, setSelectedIDs] = useState<string[]>([]);
  const selectedRows = useMemo(() => [...(props.table?.rows ?? []), ...props.localRows]
    .filter((row) => selectedIDs.includes(row.id)), [props.table?.rows, props.localRows, selectedIDs]);
  const resize = (row: GeometryRow, operation: 'halfSize' | 'doubleSize', axis?: 'NorthSouth' | 'EastWest'): void =>
    props.onTopologyResize?.('region', row, operation, axis);

  return (
    <div className="geometry-grid">
      <ConceptualMeshControlPointEditor {...props} onSelectionChange={(rows) => setSelectedIDs(rows.map((row) => row.id))} />
      <TopologyRegionQuickEditPicker language={props.language} rows={selectedRows} canEdit={props.canEdit} busy={props.busy} onResize={resize} />
      <TopologyRecipeBuilder language={props.language} rows={selectedRows} snapshot={props.topologySnapshot}
        canEdit={props.canEdit} busy={props.busy} supported={!!props.canLoadRecipe}
        onLoadRecipe={props.onLoadRecipe} onApplyRecipe={props.onApplyRecipe} />
      <TopologyFeatureErrorControl language={props.language} featureKind="region" rows={selectedRows} snapshot={props.topologySnapshot} />
    </div>
  );
}

export default TopologyRegionsEditor;
