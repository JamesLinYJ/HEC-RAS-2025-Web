import type { Language } from '../../../shared/workbench';
import { displayToolOption, type ToolField } from '../GeoprocessingToolBase';

export function ToolParameterField({ field, value, language, onChange }: {
  field: ToolField;
  value: string | boolean | undefined;
  language: Language;
  onChange: (value: string | boolean) => void;
}) {
  const label = field.label[language];
  if (field.kind === 'toggle') return <label className="tools-toggle-field gp-control-field">
    <span>{label}</span><input type="checkbox" checked={value === true} onChange={event => onChange(event.target.checked)} />
    {field.description && <small>{field.description[language]}</small>}
  </label>;
  if (field.kind === 'select') return <label className="tools-field gp-control-field"><span>{label}</span>
    <select value={typeof value === 'string' ? value : ''} onChange={event => onChange(event.target.value)}>
      {(field.options ?? []).map(option => <option value={option} key={option}>{displayToolOption(option, language)}</option>)}
    </select>{field.description && <small>{field.description[language]}</small>}
  </label>;
  return <label className="tools-field gp-control-field"><span>{label}</span>
    <input type={field.kind === 'number' ? 'number' : 'text'} min={field.min} step={field.step ?? 'any'}
      inputMode={field.kind === 'number' ? 'decimal' : undefined} value={typeof value === 'string' ? value : ''}
      onChange={event => onChange(event.target.value)} />
    {field.description && <small>{field.description[language]}</small>}
  </label>;
}
