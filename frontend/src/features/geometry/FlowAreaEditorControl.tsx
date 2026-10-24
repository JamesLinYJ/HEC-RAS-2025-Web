import type { Language } from '../../shared/workbench';
import { GeometryDetailFields } from './GeometryDetailFields';
import { gt } from './geometryText';
import type { GeometryField, GeometryRow } from './geometryTypes';

export interface FlowAreaEditorControlProps {
  language: Language;
  row?: GeometryRow;
  fields: GeometryField[];
  canEdit: boolean;
  busy: boolean;
  onChange: (row: GeometryRow, field: GeometryField, value: string, isLocal: boolean) => void;
}

const editableFlowAreaFields = ['Name', 'TargetedCellSpacing'];

/** Source FlowAreaEditorControl edits the linked area's name and target cell spacing. */
export function FlowAreaEditorControl({ language, row, fields, canEdit, busy, onChange }: FlowAreaEditorControlProps) {
  const orderedFields = editableFlowAreaFields.flatMap((id) => {
    const field = fields.find((item) => item.id === id);
    return field ? [field] : [];
  });
  return <GeometryDetailFields language={language}
    title={`${gt(language, 'flowAreas')} · ${language === 'zh' ? '编辑属性' : 'Edit properties'}`}
    rows={row ? [row] : []} fields={orderedFields} canEdit={canEdit} busy={busy} onChange={onChange} />;
}

export default FlowAreaEditorControl;
