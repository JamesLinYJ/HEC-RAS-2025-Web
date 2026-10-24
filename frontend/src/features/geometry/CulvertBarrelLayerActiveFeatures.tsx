import { useEffect, useMemo, useState } from 'react';
import type { Language } from '../../shared/workbench';
import { CulvertEditor } from './CulvertEditor';
import { CulvertsTable } from './CulvertsTable';
import { DetailedCulvertBarrelLayerHeader } from './DetailedCulvertBarrelLayerHeader';
import type { GeometryField, GeometryRow, GeometryTable } from './geometryTypes';

export interface CulvertBarrelLayerActiveFeaturesProps {
  language: Language;
  table?: GeometryTable;
  localRows: GeometryRow[];
  editable: boolean;
  busy: boolean;
  onChange: (row: GeometryRow, field: GeometryField, value: string, isLocal: boolean) => void;
  onDelete: (rows: GeometryRow[]) => void;
  onSelectionChange: (ids: string[]) => void;
  onCommand?: (row: GeometryRow, field: GeometryField, command: string, value?: string) => void;
  onSelectionFieldChange?: (rows: GeometryRow[], field: GeometryField, value: string) => void;
  onSelectionFieldCommand?: (rows: GeometryRow[], field: GeometryField, command: string, value?: string) => void;
}

/** Source barrel-layer selection: list selection and the linked detail editor share stable barrel row IDs. */
export function CulvertBarrelLayerActiveFeatures({
  language, table, localRows, editable, busy, onChange, onDelete, onSelectionChange, onCommand,
  onSelectionFieldChange, onSelectionFieldCommand,
}: CulvertBarrelLayerActiveFeaturesProps) {
  const [selectedIDs, setSelectedIDs] = useState<string[]>([]);
  const rows = useMemo(() => [...(table?.rows ?? []), ...localRows], [table?.rows, localRows]);
  const selectedRows = rows.filter((row) => selectedIDs.includes(row.id));
  const selectRows = (ids: string[]): void => {
    setSelectedIDs(ids);
    onSelectionChange(ids);
  };

  useEffect(() => {
    const available = new Set(rows.map((row) => row.id));
    const retained = selectedIDs.filter((id) => available.has(id));
    if (retained.length === selectedIDs.length) return;
    setSelectedIDs(retained);
    onSelectionChange(retained);
  }, [onSelectionChange, rows, selectedIDs]);

  return <div className="geometry-grid geometry-detail-layout">
    <DetailedCulvertBarrelLayerHeader language={language} rows={rows} selectedRows={selectedRows} />
    <CulvertsTable language={language} table={table} localRows={localRows} editable={editable} busy={busy}
      onChange={onChange} onDelete={onDelete} onSelect={selectRows} selectedRowIDs={selectedIDs} />
    <CulvertEditor language={language} table={table} selectedRows={selectedRows} localRows={localRows}
      editable={editable} busy={busy} onChange={onChange} onCommand={onCommand}
      onSelectionChange={onSelectionFieldChange} onSelectionCommand={onSelectionFieldCommand} />
  </div>;
}

export default CulvertBarrelLayerActiveFeatures;
