import type { Language } from '../../shared/workbench';
import { GeometryFeatureDetailEditor } from './GeometryFeatureDetailEditor';
import { gt } from './geometryText';
import type { GeometryField, GeometryRow, GeometryTable } from './geometryTypes';

export interface BarrelPropertiesDetailEditorProps {
  language: Language;
  table?: GeometryTable;
  localRows: GeometryRow[];
  canEdit: boolean;
  busy: boolean;
  onChange: (row: GeometryRow, field: GeometryField, value: string, isLocal: boolean) => void;
  onDelete: (rows: GeometryRow[]) => void;
  onAdd: () => void;
}

const propertyFieldOrder = ['Name', 'Shape', 'Rise', 'Span', 'Mannings', 'ManningsBot', 'ManningsSwitchDepth', 'DepthBlocked'];

export function BarrelPropertiesDetailEditor({ language, table, localRows, canEdit, busy, onChange, onDelete, onAdd }: BarrelPropertiesDetailEditorProps) {
  return (
    <div className="geometry-grid">
      <div className="geometry-muted">{language === 'zh' ? '字段与选择项来自当前几何响应；修改进入同一涵洞草稿。' : 'Fields and choices come from the current geometry response; edits enter the same culvert draft.'}</div>
      <GeometryFeatureDetailEditor language={language} kind="barrelProperties" table={table} localRows={localRows} fieldOrder={propertyFieldOrder} editable={canEdit} busy={busy} onChange={onChange} onDelete={onDelete} />
      <div className="geometry-toolbar-group"><button className="geometry-secondary" disabled={!canEdit || busy} onClick={onAdd}>{gt(language, 'addBarrelProperties')}</button><span className="geometry-inline-help">{gt(language, 'parentTxn')}</span></div>
    </div>
  );
}

export default BarrelPropertiesDetailEditor;
