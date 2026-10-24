import { useMemo, useState, type ReactNode } from 'react';
import type { Language } from '../../shared/workbench';
import { GeometryFeatureTable } from './GeometryFeatureTable';
import { GeometryDetailFields } from './GeometryDetailFields';
import { gt } from './geometryText';
import type { GeometryField, GeometryRow, GeometryTable } from './geometryTypes';

export interface FlowAreaEditorControlProps {
  language: Language;
  table?: GeometryTable;
  canEdit: boolean;
  busy: boolean;
  onChange: (row: GeometryRow, field: GeometryField, value: string, isLocal: boolean) => void;
  headerActions?: ReactNode;
  children?: ReactNode;
}

const flowAreaFieldOrder = ['Name', 'TargetedCellSpacing', 'HasMesh', 'CellCount', 'FaceCount', 'NodeCount'];

export function FlowAreaEditorControl({ language, table, canEdit, busy, onChange, headerActions, children }: FlowAreaEditorControlProps) {
  const [selectedIDs, setSelectedIDs] = useState<string[]>([]);
  const rows = useMemo(() => table?.rows ?? [], [table?.rows]);
  const selectedRows = rows.filter((row) => selectedIDs.includes(row.id));
  const detailFields = useMemo(() => {
    const byID = new Map((table?.editableFields ?? []).map((field) => [field.id, field]));
    const columns = new Map((table?.columns ?? []).map((column) => [column.id, column]));
    return flowAreaFieldOrder.flatMap((fieldID) => {
      const field = byID.get(fieldID);
      if (field) return [field];
      const column = columns.get(fieldID);
      return column ? [{ id: column.id, title: column.title, sourceType: column.sourceType, readOnly: true }] : [];
    });
  }, [table?.columns, table?.editableFields]);

  return (
    <section className="geometry-panel">
      <header className="geometry-panel-heading"><div><h2>{gt(language, 'flowAreas')}</h2><p>{rows.length} {gt(language, 'rows')}</p></div><div className="geometry-mesh-controls">{headerActions}</div></header>
      {children}
      <GeometryFeatureTable language={language} kind="flowArea" table={table} editable={canEdit} allowLocalAdd={false} busy={busy} onChange={onChange} onSelect={setSelectedIDs} />
      <GeometryDetailFields
        language={language}
        title={`${gt(language, 'flowAreas')} · ${language === 'zh' ? '详细属性' : 'Details'}`}
        rows={selectedRows}
        fields={detailFields}
        canEdit={canEdit}
        busy={busy}
        onChange={onChange}
      />
    </section>
  );
}

export default FlowAreaEditorControl;
