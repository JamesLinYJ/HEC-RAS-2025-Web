import type { Language } from '../../shared/workbench';
import { FlowAreaEditorControl } from './FlowAreaEditorControl';
import { FlowAreaInformation } from './FlowAreaInformation';
import { FlowAreaMetaDataContextualControl } from './FlowAreaMetaDataContextualControl';
import type { GeometryField, GeometryRow, GeometryTable } from './geometryTypes';

export interface FlowAreaEditorContextualControlProps {
  language: Language;
  table?: GeometryTable;
  rows: GeometryRow[];
  canEdit: boolean;
  busy: boolean;
  onChange: (row: GeometryRow, field: GeometryField, value: string, isLocal: boolean) => void;
}

/** Source flow-area context control: edit fields and metadata of the linked selected area. */
export function FlowAreaEditorContextualControl({ language, table, rows, canEdit, busy, onChange }: FlowAreaEditorContextualControlProps) {
  return <div className="geometry-grid geometry-detail-layout two">
      <FlowAreaEditorControl language={language} row={rows.length === 1 ? rows[0] : undefined}
        fields={table?.editableFields ?? []} canEdit={canEdit} busy={busy} onChange={onChange} />
      <FlowAreaMetaDataContextualControl language={language} rows={rows} />
      <FlowAreaInformation language={language} rows={rows} />
    </div>
}

export default FlowAreaEditorContextualControl;
