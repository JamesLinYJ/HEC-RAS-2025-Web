import type { Language } from '../../../shared/workbench';
import type { DssPathPart } from './PartFilterItemControl';
import { PartFilterItemControl } from './PartFilterItemControl';
import type { DssRecordDescriptor } from './types';

interface DssFilterControlProps {
  records: DssRecordDescriptor[];
  partValues: readonly [string | null, string | null, string | null, string | null, string | null, string | null];
  recordType: string;
  query: string;
  language: Language;
  disabled?: boolean;
  onPartChange: (part: DssPathPart, value: string | null) => void;
  onRecordTypeChange: (value: string) => void;
  onQueryChange: (value: string) => void;
  onReset: () => void;
}

const pathParts: DssPathPart[] = ['A', 'B', 'C', 'D', 'E', 'F'];

function valuesForPart(records: DssRecordDescriptor[], index: number): string[] {
  return [...new Set(records.map((record) => record.parts[index] ?? ''))];
}

export function DssFilterControl({ records, partValues, recordType, query, language, disabled = false, onPartChange, onRecordTypeChange, onQueryChange, onReset }: DssFilterControlProps) {
  const zh = language === 'zh';
  const recordTypes = [...new Set(records.map((record) => record.recordType))].sort((left, right) => left.localeCompare(right));

  return <fieldset className="project-dss-filters" disabled={disabled}>
    <legend>{zh ? '筛选 DSS 路径' : 'Filter DSS pathnames'}</legend>
    <label className="project-dss-search-filter">
      <span>{zh ? '路径搜索' : 'Path search'}</span>
      <input
        type="search"
        value={query}
        aria-label={zh ? 'DSS 路径搜索' : 'Search DSS pathnames'}
        placeholder={zh ? '可搜索路径部分或记录类型' : 'Search pathname parts or record type'}
        onChange={(event) => onQueryChange(event.currentTarget.value)}
      />
    </label>
    <label className="project-dss-record-type-filter">
      <span>{zh ? '记录类型' : 'Record type'}</span>
      <select aria-label={zh ? 'DSS 记录类型筛选' : 'DSS record type filter'} value={recordType} onChange={(event) => onRecordTypeChange(event.currentTarget.value)}>
        <option value="">{zh ? '全部类型' : 'All types'}</option>
        {recordTypes.map((value) => <option key={value} value={value}>{value}</option>)}
      </select>
    </label>
    <div className="project-dss-part-filters">
      {pathParts.map((part, index) => <PartFilterItemControl
        key={part}
        part={part}
        values={valuesForPart(records, index)}
        selectedValue={partValues[index]}
        language={language}
        onChange={(value) => onPartChange(part, value)}
      />)}
    </div>
    <button type="button" className="project-button project-button-secondary project-dss-filter-reset" onClick={onReset}>{zh ? '清除筛选' : 'Clear filters'}</button>
  </fieldset>;
}
