import type { Language } from '../../shared/workbench';
import type { GeometryField, GeometryRow, GeometryTable } from './geometryTypes';
import { BarrelPropertiesDetailEditor } from './BarrelPropertiesDetailEditor';

export interface GeometryCulvertDetailPageProps {
  language: Language;
  table?: GeometryTable;
  localRows: GeometryRow[];
  canEdit: boolean;
  busy: boolean;
  onChange: (row: GeometryRow, field: GeometryField, value: string, isLocal: boolean) => void;
  onDelete: (rows: GeometryRow[]) => void;
  onAdd: () => void;
}

export function GeometryCulvertBarrelPropertiesPage({ language, table, localRows, canEdit, busy, onChange, onDelete, onAdd }: GeometryCulvertDetailPageProps) {
  return <BarrelPropertiesDetailEditor language={language} table={table} localRows={localRows} canEdit={canEdit} busy={busy} onChange={onChange} onDelete={onDelete} onAdd={onAdd} />;
}

export default GeometryCulvertBarrelPropertiesPage;
