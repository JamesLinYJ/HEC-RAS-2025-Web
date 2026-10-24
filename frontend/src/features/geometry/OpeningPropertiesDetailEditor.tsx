import type { Language } from '../../shared/workbench';
import { GeometryFeatureDetailEditor } from './GeometryFeatureDetailEditor';
import { gt } from './geometryText';
import type { GeometryField, GeometryRow, GeometryTable } from './geometryTypes';

export interface OpeningPropertiesDetailEditorProps {
  language: Language;
  table?: GeometryTable;
  localRows: GeometryRow[];
  canEdit: boolean;
  busy: boolean;
  onChange: (row: GeometryRow, field: GeometryField, value: string, isLocal: boolean) => void;
  onDelete: (rows: GeometryRow[]) => void;
  onAdd: () => void;
  onCommand?: (row: GeometryRow, field: GeometryField, command: string, value?: string) => void;
}

const openingFieldOrder = ['Name', 'KIn', 'KOut', 'OpeningType'];

export function OpeningPropertiesDetailEditor({ language, table, localRows, canEdit, busy, onChange, onDelete, onAdd, onCommand }: OpeningPropertiesDetailEditorProps) {
  return (
    <div className="geometry-grid">
      <div className="geometry-muted">{language === 'zh' ? '入口、出口损失系数和进出口类型使用 C# 字段元数据及对应草稿动作。' : 'Entrance/exit coefficients and opening type use the source field metadata and available draft commands.'}</div>
      <GeometryFeatureDetailEditor language={language} kind="openingProperties" table={table} localRows={localRows} fieldOrder={openingFieldOrder} editable={canEdit} busy={busy} onChange={onChange} onDelete={onDelete} onCommand={onCommand} />
      <div className="geometry-toolbar-group"><button className="geometry-secondary" disabled={!canEdit || busy} onClick={onAdd}>{gt(language, 'addOpeningProperties')}</button><span className="geometry-inline-help">{gt(language, 'parentTxn')}</span></div>
    </div>
  );
}

export default OpeningPropertiesDetailEditor;
