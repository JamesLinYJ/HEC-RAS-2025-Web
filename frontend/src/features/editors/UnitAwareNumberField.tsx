import { useEffect, useState } from 'react';
import { UnitComboBox } from '../../controls/UnitComboBox';
import { UnitCalculatorButton } from '../../controls/UnitCalculatorButton';
import type { BackendGateway } from '../../shared/workbench';
import type { DisplayUnitData, EditorFieldSchema } from './model';
import { validateFieldValue } from './SchemaFieldRenderer';

/** NumericPropertyItem-style field: unit changes affect display only, while edited values are converted back to the source unit. */
export function UnitAwareNumberField({ id, title, value, baseline, field, unitData, unitLabel, disabled, language, backend, onChange }: {
  id: string;
  title: string;
  value: string;
  baseline?: string;
  field: EditorFieldSchema;
  unitData?: DisplayUnitData;
  unitLabel?: string;
  disabled: boolean;
  language: 'zh' | 'en';
  backend?: BackendGateway;
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
  const selectedUnitIsFinite = Boolean(selected && Number.isFinite(selected.scale) && selected.scale > 0 && Number.isFinite(selected.offset));
  const numericSource = value.trim() === '' ? Number.NaN : Number(value);
  const displayValue = Number.isFinite(numericSource) && selected && selectedUnitIsFinite
    ? formatDisplayValue(numericSource * selected.scale + selected.offset)
    : value;
  const shown = rawEdit?.id === id ? rawEdit.value : displayValue;
  const rawEditSourceValue = rawEdit?.id === id ? sourceValueFromDisplay(rawEdit.value, selected, selectedUnitIsFinite) : null;
  const error = rawEdit?.id === id
    ? rawEditSourceValue === null
      ? validateFieldValue(field, rawEdit.value, language)
      : validateFieldValue(field, String(rawEditSourceValue), language)
    : value !== baseline ? validateFieldValue(field, value, language) : null;

  const update = (raw: string): void => {
    setRawEdit({ id, value: raw });
    const sourceValue = sourceValueFromDisplay(raw, selected, selectedUnitIsFinite);
    if (sourceValue === null) {
      onChange(raw);
      return;
    }
    // Schema limits belong to the stored source units. Commit the converted
    // source value even when it violates a limit so the draft remains visible
    // and the ordinary source-unit validation can report the actual issue.
    onChange(formatDisplayValue(sourceValue));
  };

  return <div className="editor-field">
    <label htmlFor={id}>{title}{choices.length <= 1 && unitLabel && <span className="editor-meteorology-unit">{unitLabel}</span>}</label>
    <div className="editor-unit-number-control">
      <input id={id} type="text" inputMode="decimal" value={shown} readOnly={disabled} aria-invalid={error ? 'true' : undefined} onChange={(event) => update(event.currentTarget.value)} onBlur={() => setRawEdit(null)} />
      {choices.length > 1 && <UnitComboBox
        units={choices}
        selectedUnitId={selected ? unitID : unitData?.sourceId ?? ''}
        onSelectedUnitChange={(unitId) => {
          setUnitID(unitId);
          const currentRawIsConvertible = rawEdit?.id === id &&
            sourceValueFromDisplay(rawEdit.value, selected, selectedUnitIsFinite) !== null;
          if (!rawEdit || rawEdit.id !== id || currentRawIsConvertible) setRawEdit(null);
        }}
        ariaLabel={language === 'zh' ? title + '显示单位' : title + ' display units'}
        getLabel={(choice) => choice.name ?? choice.id}
        disabled={disabled}
      />}
      {choices.length <= 1 && (selected?.name || unitLabel) && <span className="editor-meteorology-unit">{selected?.name ?? unitLabel}</span>}
      {backend && unitData?.sourceUnitTypeId && selected?.unitTypeId && selectedUnitIsFinite && <UnitCalculatorButton
        backend={backend}
        language={language}
        fieldID={id}
        fieldTitle={title}
        sourceUnitId={unitData.sourceUnitTypeId}
        sourceUnitName={unitLabel ?? unitData.sourceId}
        displayUnitId={selected.unitTypeId}
        displayUnitName={selected.name ?? selected.id}
        initialSourceValue={value}
        disabled={disabled}
        onApply={update}
      />}
    </div>
    {error && <small className="editor-field-error">{error}</small>}
  </div>;
}

function formatDisplayValue(value: number): string {
  if (!Number.isFinite(value)) return '';
  return String(Number(value.toPrecision(12)));
}

function sourceValueFromDisplay(raw: string, selected: DisplayUnitData['choices'][number] | undefined, selectedIsFinite: boolean): number | null {
  if (!selected || !selectedIsFinite || !/^[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?$/.test(raw)) return null;
  const displayValue = Number(raw);
  if (!Number.isFinite(displayValue)) return null;
  const sourceValue = (displayValue - selected.offset) / selected.scale;
  return Number.isFinite(sourceValue) ? sourceValue : null;
}
