import type { Language } from '../../shared/workbench';
import type { GeometryField, GeometryRow, GeometryTable } from './geometryTypes';
import { BarrelPropertiesEditor } from './BarrelPropertiesEditor';

export interface GeometryCulvertDetailPageProps {
  language: Language;
  table?: GeometryTable;
  localRows: GeometryRow[];
  canEdit: boolean;
  busy: boolean;
  onChange: (row: GeometryRow, field: GeometryField, value: string, isLocal: boolean) => void;
  onDelete: (rows: GeometryRow[]) => void;
  onAdd: () => void;
  onSelectionChange?: (rows: GeometryRow[], field: GeometryField, value: string) => void;
}

export function GeometryCulvertBarrelPropertiesPage({ language, table, localRows, canEdit, busy, onChange, onDelete, onAdd, onSelectionChange }: GeometryCulvertDetailPageProps) {
  return <BarrelPropertiesEditor language={language} table={table} localRows={localRows} canEdit={canEdit} busy={busy} onChange={onChange} onDelete={onDelete} onAdd={onAdd} onSelectionChange={onSelectionChange} />;
}

export default GeometryCulvertBarrelPropertiesPage;
