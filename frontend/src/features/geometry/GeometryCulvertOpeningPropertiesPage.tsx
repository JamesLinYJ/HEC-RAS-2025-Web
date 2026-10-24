import type { Language } from '../../shared/workbench';
import type { GeometryField, GeometryRow, GeometryTable } from './geometryTypes';
import { OpeningPropertiesEditor } from './OpeningPropertiesEditor';

export interface GeometryCulvertOpeningPropertiesPageProps {
  language: Language;
  table?: GeometryTable;
  localRows: GeometryRow[];
  canEdit: boolean;
  busy: boolean;
  onChange: (row: GeometryRow, field: GeometryField, value: string, isLocal: boolean) => void;
  onDelete: (rows: GeometryRow[]) => void;
  onAdd: () => void;
  onCommand?: (row: GeometryRow, field: GeometryField, command: string, value?: string) => void;
  onSelectionChange?: (rows: GeometryRow[], field: GeometryField, value: string) => void;
  onSelectionCommand?: (rows: GeometryRow[], field: GeometryField, command: string, value?: string) => void;
}

export function GeometryCulvertOpeningPropertiesPage({ language, table, localRows, canEdit, busy, onChange, onDelete, onAdd, onCommand, onSelectionChange, onSelectionCommand }: GeometryCulvertOpeningPropertiesPageProps) {
  return <OpeningPropertiesEditor language={language} table={table} localRows={localRows} canEdit={canEdit} busy={busy} onChange={onChange} onDelete={onDelete} onAdd={onAdd} onCommand={onCommand} onSelectionChange={onSelectionChange} onSelectionCommand={onSelectionCommand} />;
}

export default GeometryCulvertOpeningPropertiesPage;
