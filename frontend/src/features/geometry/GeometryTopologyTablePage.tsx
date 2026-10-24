import type { Language } from '../../shared/workbench';
import { GeometryFeatureTable } from './GeometryFeatureTable';
import { gt } from './geometryText';
import type { GeometryFeatureKind, GeometryField, GeometryRow, GeometryTable } from './geometryTypes';

export interface GeometryTopologyTablePageProps {
  language: Language;
  kind: Extract<GeometryFeatureKind, 'node' | 'arc' | 'region'>;
  table?: GeometryTable;
  localRows: GeometryRow[];
  canEdit: boolean;
  busy: boolean;
  onChange: (row: GeometryRow, field: GeometryField, value: string, isLocal: boolean) => void;
  onCommand?: (row: GeometryRow, field: GeometryField, command: string, value?: string) => void;
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
      />
      <div className="geometry-inline-help">{gt(props.language, 'validationNote')} · {gt(props.language, title)}</div>
    </div>
  );
}
