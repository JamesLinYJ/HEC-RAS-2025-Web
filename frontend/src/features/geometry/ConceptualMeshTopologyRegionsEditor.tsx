import { useMemo, useState } from 'react';
import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { ConceptualMeshControlPointEditor } from './ConceptualMeshControlPointEditor';
import { ControlPointEditor } from './ControlPointEditor';
import { RegionsTable } from './RegionsTable';
import { TopologyFeatureErrorControl } from './TopologyFeatureErrorControl';
import { TopologyRecipeBuilder } from './TopologyRecipeBuilder';
import { TopologyRegionQuickEditPicker } from './TopologyRegionQuickEditPicker';
import { TagFilterControl } from './TagFilterControl';
import { ConceptualMeshContextualControl } from './ConceptualMeshContextualControl';
import type { GeometryRow } from './geometryTypes';

export type ConceptualMeshTopologyRegionsEditorProps = Omit<GeometryTopologyTablePageProps, 'kind'>;

/** Region sizing, metadata tags, and recipes operate on the same selected control-point FIDs. */
export function ConceptualMeshTopologyRegionsEditor(props: ConceptualMeshTopologyRegionsEditorProps) {
  const [selectedIDs, setSelectedIDs] = useState<string[]>([]);
  const rows = useMemo(() => [...(props.table?.rows ?? []), ...props.localRows], [props.table?.rows, props.localRows]);
  const selectedRows = rows.filter((row) => selectedIDs.includes(row.id));
  const selectRows = (selected: GeometryRow[]): void => {
    setSelectedIDs(selected.map((row) => row.id));
    props.onSelectionChange?.(selected);
  };
  const resize = (row: GeometryRow, operation: 'halfSize' | 'doubleSize', axis?: 'NorthSouth' | 'EastWest'): void =>
    props.onTopologyResize?.('region', row, operation, axis);

  return <div className="geometry-grid">
    <RegionsTable {...props} selectedRowIDs={selectedIDs} onSelectionChange={selectRows} />
    <ConceptualMeshContextualControl language={props.language} featureKind="region" rows={selectedRows}
      canEdit={props.canEdit} busy={props.busy} onApplyFactor={props.onApplyTopologyFactor} />
    <TagFilterControl language={props.language} featureKind="region" rows={rows} snapshot={props.topologySnapshot}
      canFilter={!!props.canFilterTopology} busy={props.busy} onFilter={props.onTopologyFilter}
      onSelectMatches={(fids) => {
        const matches = new Set(fids);
        selectRows(rows.filter((row) => row.fid != null && matches.has(row.fid)));
      }} />
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
