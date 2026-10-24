import { UnitComboBox } from '../../../controls/UnitComboBox';
import type { UnitCalculatorConvertResult, UnitCalculatorLoadResult } from './wilcockTypes';

export type UnitCalculatorDirection = 'FromTo' | 'ToFrom';

export function UnitCalculator({
  language,
  catalog,
  selectedFromID,
  selectedToID,
  fromValue,
  toValue,
  direction,
  conversion,
  conversionStale,
  canConvert,
  converting,
  onFamilyChange,
  onFromChange,
  onToChange,
  onFromValueChange,
  onToValueChange,
  onDirectionChange,
  onConvert,
}: {
  language: 'zh' | 'en';
  catalog?: UnitCalculatorLoadResult;
  selectedFromID: string;
  selectedToID: string;
  fromValue: string;
  toValue: string;
  direction: UnitCalculatorDirection;
  conversion?: UnitCalculatorConvertResult;
  conversionStale: boolean;
  canConvert: boolean;
  converting: boolean;
  onFamilyChange: (unitTypeName: string) => void;
  onFromChange: (unitId: string) => void;
  onToChange: (unitId: string) => void;
  onFromValueChange: (value: string) => void;
  onToValueChange: (value: string) => void;
  onDirectionChange: (direction: UnitCalculatorDirection) => void;
  onConvert: () => void;
}) {
  const zh = language === 'zh';
  const families = Array.from(new Set((catalog?.fromUnits ?? []).map((unit) => unit.unitTypeName)));
  const selectedFrom = catalog?.fromUnits.find((unit) => unit.id === selectedFromID);
  const selectedFamily = selectedFrom?.unitTypeName ?? '';
  const familyUnits = (catalog?.fromUnits ?? []).filter((unit) => unit.unitTypeName === selectedFamily);
  const fromUnit = selectedFrom;
  const toUnit = catalog?.toUnits.find((unit) => unit.id === selectedToID);
  const resultDirection = direction === 'FromTo'
    ? `${fromUnit?.name ?? selectedFromID} → ${toUnit?.name ?? selectedToID}`
    : `${toUnit?.name ?? selectedToID} → ${fromUnit?.name ?? selectedFromID}`;

  return <>
    <section className="hydraulics-unit-card" aria-label={zh ? '单位换算参数' : 'Unit conversion inputs'}>
      <label className="hydraulics-field">
        <span>{zh ? '单位族' : 'Unit family'}</span>
        <UnitComboBox
          units={families.map((name) => ({ id: name, name }))}
          selectedUnitId={selectedFamily}
          onSelectedUnitChange={onFamilyChange}
          ariaLabel={zh ? '单位族' : 'Unit family'}
          placeholder={zh ? '选择单位族' : 'Choose a unit family'}
          disabled={!catalog || converting}
        />
      </label>
      <label className="hydraulics-field">
        <span>{zh ? '源单位' : 'From unit'}</span>
        <UnitComboBox
          units={familyUnits}
          selectedUnitId={selectedFromID}
          onSelectedUnitChange={onFromChange}
          ariaLabel={zh ? '源单位' : 'From unit'}
          placeholder={zh ? '选择源单位' : 'Choose a from unit'}
          getLabel={(unit) => unit.name}
          disabled={!selectedFamily || converting}
        />
      </label>
      <label className="hydraulics-field">
        <span>{zh ? '目标单位' : 'To unit'}</span>
        <UnitComboBox
          units={catalog?.toUnits ?? []}
          selectedUnitId={selectedToID}
          onSelectedUnitChange={onToChange}
          ariaLabel={zh ? '目标单位' : 'To unit'}
          placeholder={zh ? '选择目标单位' : 'Choose a to unit'}
          getLabel={(unit) => unit.name}
          disabled={!selectedFromID || converting}
        />
      </label>
      <label className="hydraulics-field">
        <span>{zh ? '源数值' : 'From value'}</span>
        <input inputMode="decimal" value={fromValue} disabled={converting} onChange={(event) => onFromValueChange(event.currentTarget.value)} placeholder={zh ? '例如 12.5' : 'e.g. 12.5'} />
      </label>
      <label className="hydraulics-field">
        <span>{zh ? '目标数值' : 'To value'}</span>
        <input inputMode="decimal" value={toValue} disabled={converting} onChange={(event) => onToValueChange(event.currentTarget.value)} placeholder={zh ? '例如 3.81' : 'e.g. 3.81'} />
      </label>
      <fieldset className="hydraulics-direction">
        <legend>{zh ? '换算方向' : 'Conversion direction'}</legend>
        <label><input type="radio" name="unit-direction" disabled={converting} checked={direction === 'FromTo'} onChange={() => onDirectionChange('FromTo')} />{zh ? '源单位 → 目标单位' : 'From unit → To unit'}</label>
        <label><input type="radio" name="unit-direction" disabled={converting} checked={direction === 'ToFrom'} onChange={() => onDirectionChange('ToFrom')} />{zh ? '目标单位 → 源单位' : 'To unit → From unit'}</label>
      </fieldset>
      <p className="hydraulics-muted">{zh
        ? '输入空值或非法文本不会转换为 0；小数请使用点号，不输入千位分隔符。单位目录与换算均由 C# 服务提供。'
        : 'Empty or invalid text is never converted as zero. Use a period for decimals and omit thousands separators. The C# service supplies the unit catalog and conversion.'}</p>
      <button type="button" className="hydraulics-button hydraulics-button-primary" disabled={!canConvert || converting} onClick={onConvert}>
        {converting ? (zh ? '转换中…' : 'Converting…') : (zh ? '换算' : 'Convert')}
      </button>
    </section>
    {conversion && <section className="hydraulics-conversion-result" aria-live="polite">
      <span>{zh ? '方向：' : 'Direction: '}{resultDirection}</span>
      <strong>{conversion.value}</strong>
      {conversionStale && <small>{zh ? '输入、单位或方向已变化，请重新换算。' : 'The input, units, or direction changed. Convert again.'}</small>}
    </section>}
  </>;
}
