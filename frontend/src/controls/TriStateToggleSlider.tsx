import { useLayoutEffect, useRef } from 'react';
import './triStateToggleSlider.css';

export interface TriStateToggleSliderProps {
  id: string;
  checked: boolean;
  indeterminate?: boolean;
  disabled?: boolean;
  accessibleLabel: string;
  language: 'zh' | 'en';
  onChange: (checked: boolean) => void;
}

/** A two-position switch that displays a real source-provided mixed state. */
export function TriStateToggleSlider({ id, checked, indeterminate = false, disabled = false, accessibleLabel, language, onChange }: TriStateToggleSliderProps) {
  const inputRef = useRef<HTMLInputElement>(null);
  const stateLabel = indeterminate
    ? (language === 'zh' ? '混合' : 'Mixed')
    : checked ? (language === 'zh' ? '开' : 'On') : (language === 'zh' ? '关' : 'Off');
  useLayoutEffect(() => {
    if (inputRef.current) inputRef.current.indeterminate = indeterminate;
  }, [indeterminate]);

  return <span className="editor-tristate-toggle">
    <input ref={inputRef} id={id} type="checkbox" checked={checked} disabled={disabled}
      aria-label={accessibleLabel} aria-checked={indeterminate ? 'mixed' : checked}
      onChange={() => onChange(indeterminate ? true : !checked)} />
    <span className="editor-tristate-track" aria-hidden="true"><span /></span>
    <span className="editor-tristate-state">{stateLabel}</span>
  </span>;
}
