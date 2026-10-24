import { BetterComboBox } from './BetterComboBox';
import type { BetterComboBoxOption } from './BetterComboBox';

/** Controlled React counterpart for PlanTimeWindowModePicker.TimeWindowMode. */
export function PlanTimeWindowModePicker({
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
  // Keep the enum's persisted string intact, including an unknown future value.
  return <div className="editor-plan-time-window-mode-picker" role="group" aria-label={accessibleLabel} data-time-window-mode={timeWindowMode}>
    <BetterComboBox
      id={id}
      value={timeWindowMode}
      options={modes}
      onChange={onTimeWindowModeChange}
      disabled={disabled}
      emptyLabel={emptyLabel}
      currentValueLabel={currentValueLabel}
    />
  </div>;
}
