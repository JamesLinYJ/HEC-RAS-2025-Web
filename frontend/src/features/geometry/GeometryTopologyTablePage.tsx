import type { Language } from '../../shared/workbench';
import { GeometryFeatureTable } from './GeometryFeatureTable';
import { gt } from './geometryText';
import type { GeometryFeatureKind, GeometryField, GeometryRow, GeometryTable, GeometryTopologyArcPointsSnapshot, GeometryTopologyFilterClause, GeometryTopologyFilterSnapshot, GeometryTopologyRecipeSnapshot, GeometryTopologySnapshot } from './geometryTypes';

export interface GeometryTopologyTablePageProps {
  language: Language;
  kind: Extract<GeometryFeatureKind, 'node' | 'arc' | 'region'>;
  table?: GeometryTable;
  localRows: GeometryRow[];
  canEdit: boolean;
  busy: boolean;
  onChange: (row: GeometryRow, field: GeometryField, value: string, isLocal: boolean) => void;
  onCommand?: (row: GeometryRow, field: GeometryField, command: string, value?: string) => void;
  onTopologyResize?: (kind: 'arc' | 'region', row: GeometryRow, operation: 'halfSize' | 'doubleSize', axis?: 'NorthSouth' | 'EastWest') => void;
  onTopologyTagChange?: (kind: 'arc' | 'region', rows: GeometryRow[], tagName: string, value: string | null) => Promise<void>;
  onTopologyTagCreate?: (kind: 'arc' | 'region', tagName: string, tagType: string) => Promise<void>;
  canFilterTopology?: boolean;
  onTopologyFilter?: (kind: 'arc' | 'region', clauses: GeometryTopologyFilterClause[], signal?: AbortSignal) => Promise<GeometryTopologyFilterSnapshot>;
  onArcPointCoordinateChange?: (arcFID: number, point: GeometryTopologyArcPointsSnapshot['rows'][number], axis: 'X' | 'Y', value: string,
    expected: { revision: string; draftState: string; draftIdentity: string }) => Promise<void>;
  topologySnapshot?: GeometryTopologySnapshot;
  canLoadArcPoints?: boolean;
  canLoadRecipe?: boolean;
  onLoadArcPoints?: (arcFID: number) => Promise<GeometryTopologyArcPointsSnapshot>;
  onLoadRecipe?: (controlPointFIDs: number[], recipeText?: string) => Promise<GeometryTopologyRecipeSnapshot>;
  onApplyRecipe?: (controlPointFIDs: number[], recipeText: string, forceSet: boolean,
    expected: { revision: string; draftState: string; draftIdentity: string }) => Promise<void>;
  onSelectionChange?: (rows: GeometryRow[]) => void;
  onDelete: (rows: GeometryRow[]) => void;
  onLocalAdd: () => void;
}

export function GeometryTopologyTablePage(props: GeometryTopologyTablePageProps) {
  const title = props.kind === 'node' ? 'nodes' : props.kind === 'arc' ? 'arcs' : 'regions';
  return (
    <div className="geometry-grid">
      <div className="geometry-muted">{props.canEdit ? gt(props.language, 'sourceBacked') : gt(props.language, 'localEditMode')}</div>
      <GeometryFeatureTable
        language={props.language}
        kind={props.kind}
        table={props.table}
        localRows={props.localRows}
        editable={props.canEdit}
        busy={props.busy}
        onChange={props.onChange}
        onDelete={props.onDelete}
        onLocalAdd={props.onLocalAdd}
        onSelect={(ids) => props.onSelectionChange?.([...(props.table?.rows ?? []), ...props.localRows].filter((row) => ids.includes(row.id)))}
      />
      <div className="geometry-inline-help">{gt(props.language, 'validationNote')} · {gt(props.language, title)}</div>
    </div>
  );
}
