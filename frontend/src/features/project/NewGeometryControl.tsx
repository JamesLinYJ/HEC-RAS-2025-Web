import type { Language } from '../../shared/workbench';
import type { ModelCreationInspection } from './ProjectModelCreationWorkflow';

export interface NewGeometryControlProps {
  language: Language;
  fields: Record<string, string>;
  inspection: ModelCreationInspection | null;
  disabled?: boolean;
  onChange: (field: string, value: string) => void;
}

export function NewGeometryControl({ language, fields, inspection, disabled = false, onChange }: NewGeometryControlProps) {
  const zh = language === 'zh';
  const terrainKey = 'NewGeometryControl/SelectedTerrain';
  const nvalueKey = 'NewGeometryControl/SelectedNValue';
  const terrains = inspection?.choices?.[terrainKey] ?? [];
  const nvalues = inspection?.choices?.[nvalueKey] ?? [];
  return (
    <div className="project-form-grid project-model-form">
      <label className="project-form-field project-span-two">
        <span>{zh ? '几何名称' : 'Geometry name'} <b>*</b></span>
        <input autoFocus value={fields['NewGeometryControl/GeometryName'] ?? ''} disabled={disabled} maxLength={180}
          placeholder={zh ? '输入新几何名称' : 'Enter a new geometry name'}
          onChange={(event) => onChange('NewGeometryControl/GeometryName', event.target.value)} />
      </label>
      <label className="project-form-field">
        <span>{zh ? '关联地形' : 'Associated terrain'}</span>
        <select value={fields[terrainKey] ?? ''} disabled={disabled || terrains.length === 0}
          onChange={(event) => onChange(terrainKey, event.target.value)}>
          <option value="">{terrains.length ? (zh ? '不关联地形' : 'No terrain association') : (zh ? '无可用地形' : 'No terrain choices returned')}</option>
          {terrains.map((choice) => <option key={choice.id} value={choice.id} title={choice.id}>{choice.title}</option>)}
        </select>
        {fields[terrainKey] && <small className="project-model-source-path" title={fields[terrainKey]}>{fields[terrainKey]}</small>}
      </label>
      <label className="project-form-field">
        <span>{zh ? '关联糙率层' : 'Associated n-value layer'}</span>
        <select value={fields[nvalueKey] ?? ''} disabled={disabled || nvalues.length === 0}
          onChange={(event) => onChange(nvalueKey, event.target.value)}>
          <option value="">{nvalues.length ? (zh ? '不关联糙率层' : 'No n-value association') : (zh ? '无可用糙率层' : 'No n-value choices returned')}</option>
          {nvalues.map((choice) => <option key={choice.id} value={choice.id} title={choice.id}>{choice.title}</option>)}
        </select>
        {fields[nvalueKey] && <small className="project-model-source-path" title={fields[nvalueKey]}>{fields[nvalueKey]}</small>}
      </label>
      <div className="project-model-source-note project-span-two">
        {zh ? '新几何会写入当前工程目录；可选关联只从 C# 检查回执中的实际地形和糙率层选择。' : 'The new geometry is written into the current project. Optional associations come only from the actual terrain and n-value choices returned by C# inspection.'}
      </div>
    </div>
  );
}
