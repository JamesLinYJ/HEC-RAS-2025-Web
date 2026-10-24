export interface ComboBoxPropertyItemOption {
  value: string;
  label: string;
}

/** Controlled selection counterpart for Visual.WinUI.Controls.ComboBoxPropertyItem. */
export function ComboBoxPropertyItem({
  id,
  value,
  options,
  onChange,
  disabled = false,
  currentValueLabel,
  emptyLabel,
}: {
  id: string;
  value: string;
  options: ComboBoxPropertyItemOption[];
  onChange: (value: string) => void;
  disabled?: boolean;
  currentValueLabel: (value: string) => string;
  emptyLabel: string;
}) {
  const hasCurrentChoice = options.some((option) => option.value === value);
  return <select id={id} value={value} disabled={disabled} onChange={(event) => onChange(event.currentTarget.value)}>
    {!hasCurrentChoice && value !== '' && <option value={value}>{currentValueLabel(value)}</option>}
    {!hasCurrentChoice && value === '' && <option value="">{emptyLabel}</option>}
    {options.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
  </select>;
}
