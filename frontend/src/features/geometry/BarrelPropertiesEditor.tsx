import type { Language } from '../../shared/workbench';
import { PropertySetEditor } from './PropertySetEditor';
import type { GeometryField, GeometryRow, GeometryTable } from './geometryTypes';

export interface BarrelPropertiesEditorProps {
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

const fieldOrder = ['Name', 'Shape', 'Rise', 'Span', 'Mannings', 'ManningsBot', 'ManningsSwitchDepth', 'DepthBlocked'];

export function BarrelPropertiesEditor(props: BarrelPropertiesEditorProps) {
  return <PropertySetEditor {...props} kind="barrelProperties" fieldOrder={fieldOrder} />;
}

export default BarrelPropertiesEditor;
