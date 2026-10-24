import { creationLocalDateTime, creationDateTimeWithOffset } from './creationDateTimeText';
import type { Language } from '../../shared/workbench';
import type { ModelCreationInspection } from './ProjectModelCreationWorkflow';

export interface NewBoundaryConditionControlProps {
  language: Language;
  fields: Record<string, string>;
  inspection: ModelCreationInspection | null;
  disabled?: boolean;
  onChange: (field: string, value: string) => void;
}

export function NewBoundaryConditionControl({ language, fields, inspection, disabled = false, onChange }: NewBoundaryConditionControlProps) {
  const zh = language === 'zh';
  const nameKey = 'NewBoundaryConditionControl/BoundaryConditionName';
  const startKey = 'NewBoundaryConditionControl/StartTime';
  const endKey = 'NewBoundaryConditionControl/EndTime';
  return (
    <div className="project-form-grid project-model-form">
      <label className="project-form-field project-span-two">
        <span>{zh ? '边界条件名称' : 'Boundary-condition name'} <b>*</b></span>
        <input autoFocus value={fields[nameKey] ?? ''} disabled={disabled} maxLength={180}
          placeholder={zh ? '输入新边界条件名称' : 'Enter a new boundary-condition name'}
          onChange={(event) => onChange(nameKey, event.target.value)} />
      </label>
      <label className="project-form-field">
        <span>{zh ? '开始时间' : 'Start time'} <b>*</b></span>
        <input type="datetime-local" step="any" value={creationLocalDateTime(fields[startKey])} disabled={disabled}
          onChange={(event) => onChange(startKey, creationDateTimeWithOffset(event.target.value, fields[startKey]))} />
      </label>
      <label className="project-form-field">
        <span>{zh ? '结束时间' : 'End time'} <b>*</b></span>
        <input type="datetime-local" step="any" value={creationLocalDateTime(fields[endKey])} disabled={disabled}
          onChange={(event) => onChange(endKey, creationDateTimeWithOffset(event.target.value, fields[endKey]))} />
      </label>
      <div className="project-model-source-note project-span-two">
        {zh ? '时间默认值来自 C# 工程合理时窗；新边界条件由工程边界条件目录实际枚举加载。' : 'Default times come from the C# project time window; existing boundary-condition sources are enumerated from the project by the C# service.'}
      </div>
      {inspection?.boundaryDefaults && <div className="project-model-time-note project-span-two">
        {zh ? 'C# 建议名称：' : 'C# suggested name: '}<code>{inspection.boundaryDefaults.name}</code>
      </div>}
    </div>
  );
}
