import { useEffect, useState, type ReactNode } from 'react';
import type { Language } from '../../shared/workbench';
import { FlowAreaEditorControl } from './FlowAreaEditorControl';
import { FlowAreaTableControl } from './FlowAreaTableControl';
import { FlowAreaInformation } from './FlowAreaInformation';
import { FlowAreaMetaDataContextualControl } from './FlowAreaMetaDataContextualControl';
import type { GeometryField, GeometryRow, GeometryTable } from './geometryTypes';

export interface FlowAreaEditorContextualControlProps {
  language: Language;
  table?: GeometryTable;
  canEdit: boolean;
  busy: boolean;
  onChange: (row: GeometryRow, field: GeometryField, value: string, isLocal: boolean) => void;
  headerActions?: ReactNode;
  children?: ReactNode;
}

/** Keeps a stable selected flow-area identity and shows its source-backed details beside the table. */
export function FlowAreaEditorContextualControl(props: FlowAreaEditorContextualControlProps) {
  const { language, table, canEdit, busy, onChange, headerActions, children } = props;
  const [selectedIDs, setSelectedIDs] = useState<string[]>([]);
  const rows = table?.rows ?? [];
  const selectedRows = rows.filter((row) => selectedIDs.includes(row.id));

  useEffect(() => {
    const available = new Set(rows.map((row) => row.id));
    setSelectedIDs((current) => current.filter((id) => available.has(id)));
  }, [table?.rows]);

  return <>
    <FlowAreaTableControl language={language} table={table} canEdit={canEdit} busy={busy} onChange={onChange}
      headerActions={headerActions} onSelectionChange={(selected) => setSelectedIDs(selected.map((row) => row.id))}>
      {children}
    </FlowAreaTableControl>
    <div className="geometry-grid geometry-detail-layout two">
      <FlowAreaEditorControl language={language} row={selectedRows.length === 1 ? selectedRows[0] : undefined}
        fields={table?.editableFields ?? []} canEdit={canEdit} busy={busy} onChange={onChange} />
      <FlowAreaMetaDataContextualControl language={language} rows={selectedRows} />
      <FlowAreaInformation language={language} rows={selectedRows} />
    </div>
  </>;
}

export default FlowAreaEditorContextualControl;
