import type { Language } from '../../shared/workbench';
import type { GeometryField, GeometryRow, GeometryTable } from './geometryTypes';
import { OpeningPropertiesDetailEditor } from './OpeningPropertiesDetailEditor';

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
}

export function GeometryCulvertOpeningPropertiesPage({ language, table, localRows, canEdit, busy, onChange, onDelete, onAdd, onCommand }: GeometryCulvertOpeningPropertiesPageProps) {
  return <OpeningPropertiesDetailEditor language={language} table={table} localRows={localRows} canEdit={canEdit} busy={busy} onChange={onChange} onDelete={onDelete} onAdd={onAdd} onCommand={onCommand} />;
}

export default GeometryCulvertOpeningPropertiesPage;
