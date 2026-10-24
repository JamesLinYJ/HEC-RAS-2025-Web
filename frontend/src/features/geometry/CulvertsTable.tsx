import type { Language } from '../../shared/workbench';
import { GeometryFeatureTable } from './GeometryFeatureTable';
import type { GeometryField, GeometryRow, GeometryTable } from './geometryTypes';

export interface CulvertsTableProps {
  language: Language;
  table?: GeometryTable;
  localRows: GeometryRow[];
  editable: boolean;
  busy: boolean;
  onChange: (row: GeometryRow, field: GeometryField, value: string, isLocal: boolean) => void;
  onDelete: (rows: GeometryRow[]) => void;
  onSelect: (ids: string[]) => void;
  selectedRowIDs?: string[];
}

/** Source-backed culvert list; selection is returned as stable API row identities for CulvertEditor. */
export function CulvertsTable({ language, table, localRows, editable, busy, onChange, onDelete, onSelect, selectedRowIDs }: CulvertsTableProps) {
  const removable = editable || localRows.length > 0;

  return (
    <GeometryFeatureTable
      language={language}
      kind="barrel"
      table={table}
      localRows={localRows}
      editable={false}
      allowLocalAdd={false}
      busy={busy}
      onChange={onChange}
      onDelete={removable ? onDelete : undefined}
      selectedRowIDs={selectedRowIDs}
      onSelect={onSelect}
    />
  );
}

export default CulvertsTable;
