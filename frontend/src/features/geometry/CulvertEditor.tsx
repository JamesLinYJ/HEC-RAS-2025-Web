import type { Language } from '../../shared/workbench';
import { GeometryDetailFields } from './GeometryDetailFields';
import { DetailedCulvertBarrelHeader } from './DetailedCulvertBarrelHeader';
import type { GeometryField, GeometryRow, GeometryTable } from './geometryTypes';

export interface CulvertEditorProps {
  language: Language;
  table?: GeometryTable;
  selectedRows: GeometryRow[];
  localRows: GeometryRow[];
  editable: boolean;
  busy: boolean;
  onChange: (row: GeometryRow, field: GeometryField, value: string, isLocal: boolean) => void;
  onCommand?: (row: GeometryRow, field: GeometryField, command: string, value?: string) => void;
  onSelectionChange?: (rows: GeometryRow[], field: GeometryField, value: string) => void;
  onSelectionCommand?: (rows: GeometryRow[], field: GeometryField, command: string, value?: string) => void;
}

const culvertFieldOrder = [
  'Name', 'BarrelPropertyName', 'UpstreamSampledTerrainElevationDisplay', 'UpstreamInvert',
  'DownstreamSampledTerrainElevationDisplay', 'DownstreamInvert', 'UpstreamOpeningName', 'DownstreamOpeningName',
];

export function CulvertEditor({
  language, table, selectedRows, localRows, editable, busy, onChange, onCommand, onSelectionChange, onSelectionCommand,
}: CulvertEditorProps) {
  const fields = culvertFieldOrder.flatMap((id) => {
    const field = table?.editableFields.find((item) => item.id === id);
    if (field) return [field];
    const column = table?.columns.find((item) => item.id === id);
    return column ? [{ id, title: column.title, sourceType: column.sourceType, readOnly: true }] : [];
  });
  const localRowIDs = new Set(localRows.map((row) => row.id));

  return (
    <GeometryDetailFields
      language={language}
      title={language === 'zh' ? '涵洞属性' : 'Culvert properties'}
      header={<DetailedCulvertBarrelHeader language={language} rows={selectedRows} />}
      rows={selectedRows}
      localRowIDs={localRowIDs}
      fields={fields}
      canEdit={editable}
      busy={busy}
      onChange={onChange}
      onCommand={onCommand}
      onSelectionChange={onSelectionChange}
      onSelectionCommand={onSelectionCommand}
    />
  );
}

export default CulvertEditor;
