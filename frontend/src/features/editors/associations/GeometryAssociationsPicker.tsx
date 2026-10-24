import type { Language } from '../../../shared/workbench';
import type { AssociationOption } from './AssociationTypes';

function AssociationSelect({ language, kind, label, options, value, onChange, disabled }: {
  language: Language;
  kind: 'terrain' | 'nValue';
  label: string;
  options: AssociationOption[];
  value: string;
  onChange: (path: string) => void;
  disabled: boolean;
}) {
  return (
    <label className="model-association-field">
      <span>{label}</span>
      <select data-testid={`association-${kind}`} value={value} disabled={disabled} onChange={(event) => onChange(event.target.value)}>
        <option value="">{language === 'zh' ? '无关联' : 'Not associated'}</option>
        {options.map((option) => <option key={option.path} value={option.path} title={`${option.path}\nSHA-256 ${option.sha256}`}>{option.name} · {option.path}</option>)}
      </select>
      {options.length === 0 && <small>{language === 'zh' ? '工程目录中没有可用选项；可保持无关联。' : 'No project options are available; the association may remain empty.'}</small>}
    </label>
  );
}

export function GeometryAssociationsPicker({ language, options, terrainPath, nValuePath, disabled, onTerrainChange, onNValueChange }: {
  language: Language;
  options: { terrain: AssociationOption[]; nValue: AssociationOption[] };
  terrainPath: string;
  nValuePath: string;
  disabled: boolean;
  onTerrainChange: (path: string) => void;
  onNValueChange: (path: string) => void;
}) {
  return (
    <section className="model-association-group" aria-label={language === 'zh' ? '几何关联' : 'Geometry associations'}>
      <header><h3>{language === 'zh' ? '几何关联' : 'Geometry associations'}</h3><p>{language === 'zh' ? '可分别选择或清除地形与糙率关联。' : 'Choose or clear the terrain and roughness associations independently.'}</p></header>
      <div className="model-association-grid">
        <AssociationSelect language={language} kind="terrain" label={language === 'zh' ? '地形' : 'Terrain'} options={options.terrain} value={terrainPath} onChange={onTerrainChange} disabled={disabled} />
        <AssociationSelect language={language} kind="nValue" label={language === 'zh' ? '糙率图层' : 'Manning’s n layer'} options={options.nValue} value={nValuePath} onChange={onNValueChange} disabled={disabled} />
      </div>
    </section>
  );
}
