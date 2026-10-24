import type { Language } from '../../shared/workbench';

export interface QuickRatioCellSizeButtonProps {
  language: Language;
  factor: 0.5 | 2;
  axis?: 'NorthSouth' | 'EastWest';
  disabled: boolean;
  onResize: (factor: 0.5 | 2) => void;
}

/** The source quick-ratio button dispatches only a half or double cell-size factor. */
export function QuickRatioCellSizeButton({ language, factor, axis, disabled, onResize }: QuickRatioCellSizeButtonProps) {
  const action = factor === 0.5
    ? (language === 'zh' ? '减半' : 'Half')
    : (language === 'zh' ? '加倍' : 'Double');
  const direction = axis === 'NorthSouth'
    ? (language === 'zh' ? '南北向' : 'North–South ')
    : axis === 'EastWest'
      ? (language === 'zh' ? '东西向' : 'East–West ')
      : '';
  const label = language === 'zh'
    ? `${direction}单元尺寸${action}`
    : `${factor === 0.5 ? 'Half' : 'Double'} ${direction}cell size`;

  return <button type="button" className="geometry-secondary" disabled={disabled} aria-label={label} title={label}
    onClick={() => onResize(factor)}>{label}</button>;
}

export default QuickRatioCellSizeButton;
