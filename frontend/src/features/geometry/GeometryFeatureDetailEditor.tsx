import { useEffect, useMemo, useState } from 'react';
import type { Language } from '../../shared/workbench';
import { GeometryFeatureTable } from './GeometryFeatureTable';
import { GeometryDetailFields } from './GeometryDetailFields';
import { gt } from './geometryText';
import type { GeometryColumn, GeometryField, GeometryRow, GeometryTable } from './geometryTypes';

export interface GeometryFeatureDetailEditorProps {
  language: Language;
  kind: string;
  table?: GeometryTable;
  localRows?: GeometryRow[];
  fieldOrder: string[];
  readOnlyFieldIDs?: string[];
  clearCommands?: Record<string, string>;
  forceSetCommands?: Record<string, string>;
  editable: boolean;
  busy: boolean;
  allowLocalAdd?: boolean;
  onChange: (row: GeometryRow, field: GeometryField, value: string, isLocal: boolean) => void;
  onDelete?: (rows: GeometryRow[]) => void;
  onLocalAdd?: () => void;
  onCommand?: (row: GeometryRow, field: GeometryField, command: string, value?: string) => void;
  onSelectionChange?: (rows: GeometryRow[]) => void;
}

export function GeometryFeatureDetailEditor({
  language, kind, table, localRows = [], fieldOrder, readOnlyFieldIDs = [], clearCommands = {}, forceSetCommands = {}, editable, busy,
  allowLocalAdd = false, onChange, onDelete, onLocalAdd, onCommand, onSelectionChange,
}: GeometryFeatureDetailEditorProps) {
  const [selectedIDs, setSelectedIDs] = useState<string[]>([]);
  const rows = useMemo(() => [...(table?.rows ?? []), ...localRows], [table?.rows, localRows]);
  const selectedRows = rows.filter((row) => selectedIDs.includes(row.id));
  const localRowIDs = useMemo(() => new Set(localRows.map((row) => row.id)), [localRows]);
  const selectRows = (ids: string[]): void => {
    setSelectedIDs(ids);
    onSelectionChange?.(rows.filter((row) => ids.includes(row.id)));
  };
  const detailFields = useMemo(() => {
    const sourceFields = new Map((table?.editableFields ?? []).map((field) => [field.id, field]));
    const columns = new Map((table?.columns ?? []).map((column) => [column.id, column]));
    return fieldOrder.flatMap((fieldID): GeometryField[] => {
      const field = sourceFields.get(fieldID);
      if (field) return [withCommands(field, clearCommands[fieldID], forceSetCommands[fieldID])];
      if (!readOnlyFieldIDs.includes(fieldID)) return [];
      const column = columns.get(fieldID);
      return column ? [asReadOnlyField(column)] : [];
    });
  }, [clearCommands, fieldOrder, forceSetCommands, readOnlyFieldIDs, table?.columns, table?.editableFields]);

  useEffect(() => {
    const rowIDs = new Set(rows.map((row) => row.id));
    setSelectedIDs((current) => {
      const retained = current.filter((id) => rowIDs.has(id));
      return retained.length === current.length ? current : retained;
    });
  }, [rows]);

  return (
    <div className="geometry-grid geometry-detail-layout">
      <GeometryFeatureTable
        language={language}
        kind={kind}
        table={table}
        localRows={localRows}
        editable={editable}
        allowLocalAdd={allowLocalAdd}
        busy={busy}
        onChange={onChange}
        onDelete={onDelete}
        onLocalAdd={onLocalAdd}
        onSelect={selectRows}
      />
      <GeometryDetailFields
        language={language}
        title={`${gt(language, kindKey(kind))} · ${language === 'zh' ? '详细属性' : 'Details'}`}
        rows={selectedRows}
        localRowIDs={localRowIDs}
        fields={detailFields}
        canEdit={editable}
        busy={busy}
        onChange={onChange}
        onCommand={onCommand}
      />
    </div>
  );
}

function kindKey(kind: string): Parameters<typeof gt>[1] {
  if (kind === 'barrelProperties') return 'barrelPropertiesKind';
  if (kind === 'openingProperties') return 'openingPropertiesKind';
  if (kind === 'flowArea') return 'flowArea';
  if (kind === 'region') return 'region';
  if (kind === 'arc') return 'arc';
  if (kind === 'node') return 'node';
  if (kind === 'barrel') return 'barrel';
  return 'table';
}

function asReadOnlyField(column: GeometryColumn): GeometryField {
  return { id: column.id, title: column.title, sourceType: column.sourceType, readOnly: true };
}

function withCommands(field: GeometryField, clearCommand?: string, forceSetCommand?: string): GeometryField {
  return {
    ...field,
    ...(clearCommand ? { clearCommand, showClearButton: true } : {}),
    ...(forceSetCommand ? { forceSetCommand } : {}),
  };
}

export default GeometryFeatureDetailEditor;
