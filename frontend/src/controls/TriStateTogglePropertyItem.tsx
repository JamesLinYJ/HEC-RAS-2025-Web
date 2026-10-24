import { TriStateToggleSlider } from './TriStateToggleSlider';

export interface TriStateTogglePropertyItemProps {
  id: string;
  checked: boolean;
  indeterminate?: boolean;
  disabled?: boolean;
  accessibleLabel: string;
  language: 'zh' | 'en';
  onChange: (checked: boolean) => void;
}

/** Property-item adapter for the source TriStateTogglePropertyItem control. */
export function TriStateTogglePropertyItem(props: TriStateTogglePropertyItemProps) {
  return <TriStateToggleSlider {...props} />;
}
