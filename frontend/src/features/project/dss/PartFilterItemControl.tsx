import type { Language } from '../../../shared/workbench';

export type DssPathPart = 'A' | 'B' | 'C' | 'D' | 'E' | 'F';

interface PartFilterItemControlProps {
  part: DssPathPart;
  values: string[];
  selectedValue: string | null;
  language: Language;
  disabled?: boolean;
  onChange: (value: string | null) => void;
}

function optionValue(value: string | null): string {
  if (value === null) return '__all_parts__';
  if (value === '') return '__blank_part__';
  return `value:${value}`;
}

function parseOption(value: string): string | null {
  if (value === '__all_parts__') return null;
  if (value === '__blank_part__') return '';
  return value.slice('value:'.length);
}

export function PartFilterItemControl({ part, values, selectedValue, language, disabled = false, onChange }: PartFilterItemControlProps) {
  const zh = language === 'zh';
  const options = [...new Set(values)].sort((left, right) => left.localeCompare(right));
  const selected = selectedValue !== null && options.includes(selectedValue) ? selectedValue : null;

  return <label className="project-dss-part-filter-item">
    <span>{part}</span>
    <select
      aria-label={zh ? `DSS ${part} 部分筛选` : `DSS ${part} part filter`}
      data-testid={`project-dss-part-filter-${part.toLowerCase()}`}
      value={optionValue(selected)}
      disabled={disabled}
      onChange={(event) => onChange(parseOption(event.currentTarget.value))}
    >
      <option value="__all_parts__">{zh ? '全部' : 'All'}</option>
      {options.includes('') && <option value="__blank_part__">{zh ? '(空白)' : '(blank)'}</option>}
      {options.filter((value) => value !== '').map((value) => <option key={value} value={optionValue(value)}>{value}</option>)}
    </select>
  </label>;
}
