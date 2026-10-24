import { BetterComboBoxPropertyItem } from './BetterComboBoxPropertyItem';
import type { BetterComboBoxOption } from './BetterComboBox';

/** Controlled React counterpart for the plan page's TimeWindowMode property item. */
export function PlanTimeWindowModePickerPropertyItem({
  id,
  timeWindowMode,
  modes,
  onTimeWindowModeChange,
  disabled = false,
  emptyLabel,
  currentValueLabel,
  accessibleLabel,
}: {
  id: string;
  timeWindowMode: string;
  modes: BetterComboBoxOption[];
  onTimeWindowModeChange: (mode: string) => void;
  disabled?: boolean;
  emptyLabel: string;
  currentValueLabel: (value: string) => string;
  accessibleLabel?: string;
}) {
  return <div className="editor-plan-time-window-mode-property-item" role="group" aria-label={accessibleLabel} data-time-window-mode={timeWindowMode}>
    <BetterComboBoxPropertyItem
      id={id}
      selectedValue={timeWindowMode}
      itemsSource={modes}
      onSelectedValueChange={onTimeWindowModeChange}
      disabled={disabled}
      emptyLabel={emptyLabel}
      currentValueLabel={currentValueLabel}
    />
  </div>;
}
