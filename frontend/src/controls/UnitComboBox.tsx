import type { ChangeEvent } from 'react';

export interface UnitComboBoxOption {
  id: string;
  name?: string | null;
  unitTypeName?: string | null;
}

/** Controlled counterpart of Visual.WinUI.Controls.UnitComboBox. */
export function UnitComboBox<T extends UnitComboBoxOption>({
  units,
  selectedUnitId,
  onSelectedUnitChange,
  ariaLabel,
  placeholder,
  disabled = false,
  className,
  getLabel,
}: {
  units: readonly T[];
  selectedUnitId: string;
  onSelectedUnitChange: (unitId: string) => void;
  ariaLabel: string;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
  getLabel?: (unit: T) => string;
}) {
  const handleChange = (event: ChangeEvent<HTMLSelectElement>) => {
    onSelectedUnitChange(event.currentTarget.value);
  };

  return <select
    className={className}
    aria-label={ariaLabel}
    value={selectedUnitId}
    disabled={disabled}
    onChange={handleChange}
  >
    {placeholder !== undefined && <option value="">{placeholder}</option>}
    {units.map((unit) => <option key={unit.id} value={unit.id}>
      {getLabel ? getLabel(unit) : unit.name ?? unit.id}
    </option>)}
  </select>;
}
