import { useId } from 'react';
import type { Language } from '../shared/workbench';

/** FilterSearchBar.cs: a two-way SearchText editor for its parent's string filter. */
export function FilterSearchBar({ value, onChange, label, placeholder, language, className, disabled = false }: {
  value: string;
  onChange: (value: string) => void;
  label: string;
  placeholder?: string;
  language: Language;
  className?: string;
  disabled?: boolean;
}) {
  const inputID = useId();
  return <div className={className}>
    <span aria-hidden="true">⌕</span>
    <input id={inputID} aria-label={label} value={value} placeholder={placeholder}
      disabled={disabled} autoComplete="off" spellCheck={false}
      onChange={event => onChange(event.currentTarget.value)}
      onKeyDown={event => {
        if (event.key === 'Escape' && value) {
          event.preventDefault(); event.stopPropagation(); onChange('');
        }
      }} />
    <button type="button" aria-label={language === 'zh' ? '清除筛选' : 'Clear filter'}
      aria-controls={inputID} onClick={() => onChange('')} disabled={disabled || !value}>×</button>
  </div>;
}
