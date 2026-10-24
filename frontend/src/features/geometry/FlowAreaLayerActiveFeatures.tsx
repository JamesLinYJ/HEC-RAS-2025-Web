import { useEffect, useState, type ReactNode } from 'react';
import type { Language } from '../../shared/workbench';
import { FlowAreaEditorContextualControl } from './FlowAreaEditorContextualControl';
import { FlowAreaTableControl } from './FlowAreaTableControl';
import type { GeometryField, GeometryRow, GeometryTable } from './geometryTypes';

export interface FlowAreaLayerActiveFeaturesProps {
  language: Language;
  table?: GeometryTable;
  canEdit: boolean;
  busy: boolean;
  onChange: (row: GeometryRow, field: GeometryField, value: string, isLocal: boolean) => void;
  headerActions?: ReactNode;
  children?: ReactNode;
}

/** Binds the flow-area layer rows to its stable selected-feature context, mirroring the source Layer selection. */
export function FlowAreaLayerActiveFeatures(props: FlowAreaLayerActiveFeaturesProps) {
  const { language, table, canEdit, busy, onChange, headerActions, children } = props;
  const [selectedIDs, setSelectedIDs] = useState<string[]>([]);
  const rows = table?.rows ?? [];
  const selectedRows = rows.filter((row) => selectedIDs.includes(row.id));

  useEffect(() => {
    const available = new Set(rows.map((row) => row.id));
    setSelectedIDs((current) => {
      const retained = current.filter((id) => available.has(id));
      return retained.length === current.length ? current : retained;
    });
  }, [rows]);

  return <>
    <FlowAreaTableControl language={language} table={table} canEdit={canEdit} busy={busy} onChange={onChange}
      selectedRowIDs={selectedIDs} headerActions={headerActions}
      onSelectionChange={(selected) => setSelectedIDs(selected.map((row) => row.id))}>
      {children}
    </FlowAreaTableControl>
    <FlowAreaEditorContextualControl language={language} table={table} rows={selectedRows}
      canEdit={canEdit} busy={busy} onChange={onChange} />
  </>;
}

export default FlowAreaLayerActiveFeatures;
