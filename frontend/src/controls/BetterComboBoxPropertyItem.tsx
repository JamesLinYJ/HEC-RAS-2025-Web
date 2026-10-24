import { BetterComboBox } from './BetterComboBox';
import type { BetterComboBoxOption } from './BetterComboBox';

/** PropertyItem adapter that keeps SelectedValue controlled through BetterComboBox. */
export function BetterComboBoxPropertyItem({
  id,
  selectedValue,
  itemsSource,
  onSelectedValueChange,
  disabled = false,
  emptyLabel,
  currentValueLabel,
}: {
  id: string;
  selectedValue: string;
  itemsSource: BetterComboBoxOption[];
  onSelectedValueChange: (value: string) => void;
  disabled?: boolean;
  emptyLabel: string;
  currentValueLabel: (value: string) => string;
}) {
  return <div className="editor-better-combo-property-item" data-selected-value={selectedValue}>
    <BetterComboBox id={id} value={selectedValue} options={itemsSource} onChange={onSelectedValueChange} disabled={disabled} emptyLabel={emptyLabel} currentValueLabel={currentValueLabel} />
  </div>;
}
