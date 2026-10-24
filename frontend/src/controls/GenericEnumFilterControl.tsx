import type { Language } from '../shared/workbench';

export interface GenericEnumFilterOption {
  value: string;
  label: string;
}

interface Props {
  language: Language;
  label: string;
  options: GenericEnumFilterOption[];
  selectedValues: readonly string[] | null;
  onSelectionChange: (values: string[] | null) => void;
  disabled?: boolean;
}

/** Controlled multi-value filter. A null selection means all source values are checked. */
export function GenericEnumFilterControl({ language, label, options, selectedValues, onSelectionChange, disabled = false }: Props) {
  const zh = language === 'zh';
  const selected = selectedValues === null ? new Set(options.map(option => option.value)) : new Set(selectedValues);
  const selectedCount = options.reduce((count, option) => count + (selected.has(option.value) ? 1 : 0), 0);
  const allChecked = options.length > 0 && selectedCount === options.length;

  const toggle = (value: string): void => {
    const next = selectedValues === null ? new Set(options.map(option => option.value)) : new Set(selectedValues);
    if (next.has(value)) next.delete(value);
    else next.add(value);
    const nextValues = options.filter(option => next.has(option.value)).map(option => option.value);
    onSelectionChange(nextValues.length === options.length ? null : nextValues);
  };

  const toggleAll = (): void => onSelectionChange(allChecked ? [] : null);

  return <details className="ras-enum-filter">
    <summary>{label}<span>{zh ? `已选 ${selectedCount}/${options.length}` : `${selectedCount}/${options.length} selected`}</span></summary>
    <div className="ras-enum-filter-options">
      <button type="button" disabled={disabled || options.length === 0} onClick={toggleAll}>
        {allChecked ? (zh ? '清除全部' : 'Uncheck all') : (zh ? '全选' : 'Check all')}
      </button>
      <div role="group" aria-label={label}>
        {options.map(option => <label key={option.value}>
          <input type="checkbox" checked={selected.has(option.value)} disabled={disabled}
            onChange={() => toggle(option.value)} />
          <span title={option.label}>{option.label}</span>
        </label>)}
      </div>
    </div>
  </details>;
}
