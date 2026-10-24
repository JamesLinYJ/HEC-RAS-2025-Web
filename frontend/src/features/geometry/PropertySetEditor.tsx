import type { Language } from '../../shared/workbench';
import { GeometryFeatureDetailEditor } from './GeometryFeatureDetailEditor';
import { gt } from './geometryText';
import type { GeometryField, GeometryRow, GeometryTable } from './geometryTypes';

export interface PropertySetEditorProps {
  language: Language;
  kind: 'barrelProperties' | 'openingProperties';
  table?: GeometryTable;
  localRows: GeometryRow[];
  fieldOrder: string[];
  canEdit: boolean;
  busy: boolean;
  onChange: (row: GeometryRow, field: GeometryField, value: string, isLocal: boolean) => void;
  onDelete: (rows: GeometryRow[]) => void;
  onAdd: () => void;
  onCommand?: (row: GeometryRow, field: GeometryField, command: string, value?: string) => void;
  onSelectionChange?: (rows: GeometryRow[], field: GeometryField, value: string) => void;
  onSelectionCommand?: (rows: GeometryRow[], field: GeometryField, command: string, value?: string) => void;
}

/** Named-property list and selected-set detail editor shared by both source property-set controls. */
export function PropertySetEditor({
  language, kind, table, localRows, fieldOrder, canEdit, busy, onChange, onDelete, onAdd, onCommand, onSelectionChange, onSelectionCommand,
}: PropertySetEditorProps) {
  const addLabel = kind === 'barrelProperties' ? 'addBarrelProperties' : 'addOpeningProperties';
  const name = kind === 'barrelProperties' ? gt(language, 'barrelPropertiesKind') : gt(language, 'openingPropertiesKind');

  return (
    <div className="geometry-grid">
      <div className="geometry-muted">{language === 'zh' ? '选择属性集后编辑其源字段；新增、删除与修改都留在同一涵洞草稿并由父级保存。' : 'Edit source fields for selected property sets. Adds, deletes and edits remain in the shared culvert draft and are saved by the parent.'}</div>
      <GeometryFeatureDetailEditor
        language={language}
        kind={kind}
        table={table}
        localRows={localRows}
        fieldOrder={fieldOrder}
        editable={canEdit}
        busy={busy}
        onChange={onChange}
        onDelete={onDelete}
        onCommand={onCommand}
        onSelectionFieldChange={onSelectionChange}
        onSelectionFieldCommand={onSelectionCommand}
      />
      <div className="geometry-toolbar-group">
        <button className="geometry-secondary" disabled={!canEdit || busy} onClick={onAdd}>{gt(language, addLabel)}</button>
        <span className="geometry-inline-help">{gt(language, 'parentTxn')}</span>
        <span className="geometry-count-pill">{name}: {table?.rows.length ?? 0}{localRows.length ? ` + ${localRows.length}` : ''}</span>
      </div>
    </div>
  );
}

export default PropertySetEditor;
