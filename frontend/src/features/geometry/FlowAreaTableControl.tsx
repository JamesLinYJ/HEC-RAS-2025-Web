import type { ReactNode } from 'react';
import type { Language } from '../../shared/workbench';
import { GeometryFeatureTable } from './GeometryFeatureTable';
import { gt } from './geometryText';
import type { GeometryField, GeometryRow, GeometryTable } from './geometryTypes';

export interface FlowAreaTableControlProps {
  language: Language;
  table?: GeometryTable;
  canEdit: boolean;
  busy: boolean;
  onChange: (row: GeometryRow, field: GeometryField, value: string, isLocal: boolean) => void;
  selectedRowIDs?: string[];
  onSelectionChange?: (rows: GeometryRow[]) => void;
  headerActions?: ReactNode;
  children?: ReactNode;
}

/** Table and workspace actions are runtime glue around the source-linked flow-area editor. */
export function FlowAreaTableControl({ language, table, canEdit, busy, onChange, selectedRowIDs, onSelectionChange, headerActions, children }: FlowAreaTableControlProps) {
  const rows = table?.rows ?? [];
  return <section className="geometry-panel">
    <header className="geometry-panel-heading"><div><h2>{gt(language, 'flowAreas')}</h2><p>{rows.length} {gt(language, 'rows')}</p></div><div className="geometry-mesh-controls">{headerActions}</div></header>
    {children}
    <GeometryFeatureTable language={language} kind="flowArea" table={table} editable={canEdit} allowLocalAdd={false} busy={busy} onChange={onChange}
      selectedRowIDs={selectedRowIDs} onSelect={(ids) => onSelectionChange?.(rows.filter((row) => ids.includes(row.id)))} />
  </section>;
}

export default FlowAreaTableControl;
