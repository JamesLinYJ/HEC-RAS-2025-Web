import { useMemo, useState } from 'react';
import type { Language } from '../../../shared/workbench';
import { DssFilterControl } from './DssFilterControl';
import type { DssPathPart } from './PartFilterItemControl';
import type { DssRecordDescriptor } from './types';

interface DssCatalogViewerProps {
  records: DssRecordDescriptor[];
  selectedPath: string;
  language: Language;
  busy: boolean;
  canRead: boolean;
  onSelectedPathChange: (path: string) => void;
  onRead: () => void;
}

const partIndex: Record<DssPathPart, number> = { A: 0, B: 1, C: 2, D: 3, E: 4, F: 5 };
type PartFilters = [string | null, string | null, string | null, string | null, string | null, string | null];

export function DssCatalogViewer({ records, selectedPath, language, busy, canRead, onSelectedPathChange, onRead }: DssCatalogViewerProps) {
  const zh = language === 'zh';
  const [partFilters, setPartFilters] = useState<PartFilters>([null, null, null, null, null, null]);
  const [recordType, setRecordType] = useState('');
  const [query, setQuery] = useState('');
  const normalizedQuery = query.trim().toLocaleLowerCase();

  const filteredRecords = useMemo(() => {
    const words = normalizedQuery.split(/\s+/).filter(Boolean);
    return records.filter((record) => {
    if (recordType && record.recordType !== recordType) return false;
    for (let index = 0; index < 6; index += 1) {
      const filter = partFilters[index];
      if (filter !== null && (record.parts[index] ?? '') !== filter) return false;
    }
    const searchable = [record.path, record.recordType, ...record.parts].join(' ').toLocaleLowerCase();
    return words.every((word) => searchable.includes(word));
    });
  }, [records, recordType, partFilters, normalizedQuery]);

  const selectedRecord = records.find((item) => item.path === selectedPath);
  const selectedIsFilteredOut = !!selectedRecord && !filteredRecords.some((item) => item.path === selectedRecord.path);

  const changePart = (part: DssPathPart, value: string | null): void => {
    setPartFilters((current) => {
      const next: PartFilters = [current[0], current[1], current[2], current[3], current[4], current[5]];
      next[partIndex[part]] = value;
      return next;
    });
  };

  const resetFilters = (): void => {
    setPartFilters([null, null, null, null, null, null]);
    setRecordType('');
    setQuery('');
  };

  return <>
    <DssFilterControl
      records={records}
      partValues={partFilters}
      recordType={recordType}
      query={query}
      language={language}
      disabled={busy}
      onPartChange={changePart}
      onRecordTypeChange={setRecordType}
      onQueryChange={setQuery}
      onReset={resetFilters}
    />
    <div className="project-dss-record-picker">
      <label htmlFor="project-dss-record">{zh ? `记录（显示 ${filteredRecords.length} / ${records.length}）` : `Records (showing ${filteredRecords.length} / ${records.length})`}</label>
      <select
        id="project-dss-record"
        value={selectedPath}
        disabled={busy || records.length === 0}
        aria-label={zh ? '选择 DSS 目录记录' : 'Select DSS catalog record'}
        onChange={(event) => onSelectedPathChange(event.currentTarget.value)}
      >
        <option value="">{zh ? '选择真实 DSS 记录' : 'Choose a source DSS record'}</option>
        {selectedIsFilteredOut && selectedRecord && <option value={selectedRecord.path}>{zh ? '当前选择（与筛选条件不符）：' : 'Current selection (outside filters): '}{selectedRecord.path}</option>}
        {filteredRecords.map((item) => <option key={item.path} value={item.path} title={item.path}>{item.path} · {item.recordType}</option>)}
      </select>
      <button type="button" className="project-button project-button-secondary" data-testid="project-dss-record-read" disabled={!canRead || busy || !selectedPath} onClick={onRead}>
        {busy ? (zh ? '正在读取…' : 'Reading…') : (zh ? '读取记录' : 'Read record')}
      </button>
      {!canRead && <small>{zh ? '当前服务未提供 dss.read。' : 'The connected service does not provide dss.read.'}</small>}
      {records.length > 0 && filteredRecords.length === 0 && <p className="project-tree-state" role="status">{zh ? '没有记录符合当前筛选条件。' : 'No records match the current filters.'}</p>}
    </div>
    {selectedRecord && <div className="project-dss-record-parts" aria-label={zh ? '当前路径六部分' : 'Selected pathname parts'}>
      {(['A', 'B', 'C', 'D', 'E', 'F'] as const).map((part, index) => <span key={part}>{part}: {selectedRecord.parts[index] || '—'}</span>)}
    </div>}
    {records.length === 0 && <p className="project-tree-state">{zh ? '服务返回的 DSS 目录为空。' : 'The service returned an empty DSS catalog.'}</p>}
    {selectedIsFilteredOut && <p className="project-dss-selection-note" role="status">{zh ? '筛选只影响目录显示；当前记录仍按完整 DSS 路径保持选中。' : 'Filters affect only the catalog display; selection remains keyed by the full DSS pathname.'}</p>}
  </>;
}
