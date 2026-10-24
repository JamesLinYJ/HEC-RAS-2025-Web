import { useEffect, useState } from 'react';
import type { DisplayUnitData, EditorFieldSchema } from './model';
import { validateFieldValue } from './SchemaFieldRenderer';
import { editorText } from './editorLocalization';

/** NumericPropertyItem-style field: unit changes affect display only, while edited values are converted back to the source unit. */
export function UnitAwareNumberField({ id, title, value, baseline, field, unitData, unitLabel, disabled, language, onChange }: {
  id: string;
  title: string;
  value: string;
  baseline?: string;
  field: EditorFieldSchema;
  unitData?: DisplayUnitData;
  unitLabel?: string;
  disabled: boolean;
  language: 'zh' | 'en';
  onChange: (sourceValue: string) => void;
}) {
  const choices = unitData?.choices ?? [];
  const [unitID, setUnitID] = useState(unitData?.sourceId ?? '');
  const [rawEdit, setRawEdit] = useState<{ id: string; value: string } | null>(null);
  useEffect(() => {
    setUnitID(unitData?.sourceId ?? '');
    setRawEdit(null);
  }, [id, unitData?.sourceId]);

  const selected = choices.find((choice) => choice.id === unitID);
  const numericSource = value.trim() === '' ? Number.NaN : Number(value);
  const displayValue = Number.isFinite(numericSource) && selected
    ? formatDisplayValue(numericSource * selected.scale + selected.offset)
    : value;
  const shown = rawEdit?.id === id ? rawEdit.value : displayValue;
  const error = rawEdit?.id === id
    ? validateFieldValue(field, rawEdit.value, language)
    : value !== baseline ? validateFieldValue(field, value, language) : null;

  const update = (raw: string): void => {
    setRawEdit({ id, value: raw });
    const validation = validateFieldValue(field, raw, language);
    if (validation) {
      onChange(raw);
      return;
    }
    if (!selected) {
      onChange(raw);
      return;
    }
    const sourceValue = (Number(raw) - selected.offset) / selected.scale;
    onChange(Number.isFinite(sourceValue) ? formatDisplayValue(sourceValue) : raw);
  };

  return <div className="editor-field">
    <label htmlFor={id}>{title}{choices.length <= 1 && unitLabel && <span className="editor-meteorology-unit">{unitLabel}</span>}</label>
    <div className="editor-unit-number-control">
      <input id={id} type="text" inputMode="decimal" value={shown} readOnly={disabled} aria-invalid={error ? 'true' : undefined} onChange={(event) => update(event.currentTarget.value)} onBlur={() => setRawEdit(null)} />
      {choices.length > 1 && <select aria-label={language === 'zh' ? title + '显示单位' : title + ' display units'} value={selected ? unitID : unitData?.sourceId ?? ''} disabled={disabled} onChange={(event) => { setUnitID(event.currentTarget.value); setRawEdit(null); }}>
        {choices.map((choice) => <option key={choice.id} value={choice.id}>{editorText(language, choice.name ?? choice.id)}</option>)}
      </select>}
      {choices.length <= 1 && (selected?.name || unitLabel) && <span className="editor-meteorology-unit">{editorText(language, selected?.name ?? unitLabel ?? '')}</span>}
    </div>
    {error && <small className="editor-field-error">{error}</small>}
  </div>;
}

function formatDisplayValue(value: number): string {
  if (!Number.isFinite(value)) return '';
  return String(Number(value.toPrecision(12)));
}
