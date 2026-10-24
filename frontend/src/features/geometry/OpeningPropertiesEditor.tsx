import type { Language } from '../../shared/workbench';
import { PropertySetEditor } from './PropertySetEditor';
import type { GeometryField, GeometryRow, GeometryTable } from './geometryTypes';

export interface OpeningPropertiesEditorProps {
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

const fieldOrder = ['Name', 'KIn', 'KOut', 'OpeningType'];

export function OpeningPropertiesEditor(props: OpeningPropertiesEditorProps) {
  return <PropertySetEditor {...props} kind="openingProperties" fieldOrder={fieldOrder} />;
}

export default OpeningPropertiesEditor;
