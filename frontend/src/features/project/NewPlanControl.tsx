import { creationLocalDateTime, creationDateTimeWithOffset } from './creationDateTimeText';
import { useId } from 'react';
import { PlanTimeWindowModePicker } from '../../controls/PlanTimeWindowModePicker';
import type { Language } from '../../shared/workbench';
import type { ModelCreationInspection } from './ProjectModelCreationWorkflow';

export interface NewPlanControlProps {
  language: Language;
  fields: Record<string, string>;
  inspection: ModelCreationInspection | null;
  disabled?: boolean;
  onChange: (field: string, value: string) => void;
}

export function NewPlanControl({ language, fields, inspection, disabled = false, onChange }: NewPlanControlProps) {
  const zh = language === 'zh';
  const modeId = useId();
  const geometryKey = 'NewPlanControl/SelectedGeometry';
  const boundaryKey = 'NewPlanControl/SelectedBoundaryCondition';
  const modeKey = 'NewPlanControl/TimeWindowMode';
  const startKey = 'NewPlanControl/StartTime';
  const endKey = 'NewPlanControl/EndTime';
  const geometries = inspection?.choices?.[geometryKey] ?? [];
  const boundaries = inspection?.choices?.[boundaryKey] ?? [];
  const selectedBoundary = fields[boundaryKey] ?? '';
  const boundaryWindow = selectedBoundary ? inspection?.boundaryTimeWindows?.[selectedBoundary] : undefined;
  const inherited = (fields[modeKey] ?? 'BoundaryCondition') === 'BoundaryCondition';
  return (
    <div className="project-form-grid project-model-form">
      <label className="project-form-field project-span-two">
        <span>{zh ? '方案名称' : 'Plan name'} <b>*</b></span>
        <input autoFocus value={fields['NewPlanControl/PlanName'] ?? ''} disabled={disabled} maxLength={180}
          placeholder={zh ? '输入新方案名称' : 'Enter a new plan name'}
          onChange={(event) => onChange('NewPlanControl/PlanName', event.target.value)} />
      </label>
      <label className="project-form-field">
        <span>{zh ? '几何' : 'Geometry'} <b>*</b></span>
        <select value={fields[geometryKey] ?? ''} disabled={disabled || geometries.length === 0}
          onChange={(event) => onChange(geometryKey, event.target.value)}>
          <option value="">{geometries.length ? (zh ? '选择工程中的几何' : 'Choose a project geometry') : (zh ? '无可用几何' : 'No geometry choices returned')}</option>
          {geometries.map((choice) => <option key={choice.id} value={choice.id} title={choice.id}>{choice.title}</option>)}
        </select>
        {fields[geometryKey] && <small className="project-model-source-path" title={fields[geometryKey]}>{fields[geometryKey]}</small>}
      </label>
      <label className="project-form-field">
        <span>{zh ? '边界条件' : 'Boundary condition'} <b>*</b></span>
        <select value={selectedBoundary} disabled={disabled || boundaries.length === 0}
          onChange={(event) => {
            const nextPath = event.target.value;
            onChange(boundaryKey, nextPath);
            const window = inspection?.boundaryTimeWindows?.[nextPath];
            if (window) {
              onChange(startKey, window.start);
              onChange(endKey, window.end);
            }
          }}>
          <option value="">{boundaries.length ? (zh ? '选择工程中的边界条件' : 'Choose a project boundary condition') : (zh ? '无可用边界条件' : 'No boundary-condition choices returned')}</option>
          {boundaries.map((choice) => <option key={choice.id} value={choice.id} title={choice.id}>{choice.title}</option>)}
        </select>
        {selectedBoundary && <small className="project-model-source-path" title={selectedBoundary}>{selectedBoundary}</small>}
      </label>
      <fieldset className="project-model-mode project-span-two" disabled={disabled}>
        <legend>{zh ? '计算时窗' : 'Computation time window'}</legend>
        <PlanTimeWindowModePicker id={modeId} timeWindowMode={fields[modeKey] ?? 'BoundaryCondition'} disabled={disabled}
          modes={[{ value: 'BoundaryCondition', label: zh ? '沿用边界条件时窗' : 'Use boundary-condition window' },
            { value: 'Plan', label: zh ? '自定义方案时窗' : 'Use a custom plan window' }]}
          emptyLabel={zh ? '选择计算时窗' : 'Select computation window'}
          currentValueLabel={value => zh ? `当前值：${value}` : `Current value: ${value}`}
          accessibleLabel={zh ? '计算时窗模式' : 'Computation window mode'}
          onTimeWindowModeChange={mode => {
            onChange(modeKey, mode);
            if (mode === 'BoundaryCondition' && boundaryWindow) {
              onChange(startKey, boundaryWindow.start);
              onChange(endKey, boundaryWindow.end);
            }
          }} />
      </fieldset>
      {inherited ? <div className="project-model-time-note project-span-two">
        {boundaryWindow
          ? <>{zh ? '边界条件时窗：' : 'Boundary-condition window: '}<code>{boundaryWindow.start}</code><span> → </span><code>{boundaryWindow.end}</code></>
          : (zh ? '选择边界条件后，C# 检查结果会显示其实际时窗。' : 'Choose a boundary condition to display its actual time window from C# inspection.')}
      </div> : <>
        <label className="project-form-field"><span>{zh ? '开始时间' : 'Start time'} <b>*</b></span>
          <input type="datetime-local" step="any" value={creationLocalDateTime(fields[startKey])} disabled={disabled}
            onChange={(event) => onChange(startKey, creationDateTimeWithOffset(event.target.value, fields[startKey]))} />
        </label>
        <label className="project-form-field"><span>{zh ? '结束时间' : 'End time'} <b>*</b></span>
          <input type="datetime-local" step="any" value={creationLocalDateTime(fields[endKey])} disabled={disabled}
            onChange={(event) => onChange(endKey, creationDateTimeWithOffset(event.target.value, fields[endKey]))} />
        </label>
      </>}
      <div className="project-model-source-note project-span-two">
        {zh ? '方案必须关联工程中已保存且可读的几何和边界条件。时窗验证与方案 HDF 写入均由 C# 服务执行。' : 'A plan must reference readable, saved geometry and boundary-condition models in this project. C# validates the time window and writes the plan HDF.'}
      </div>
    </div>
  );
}
