import { useEffect, useId, useRef } from 'react';
import type { ResultDataset, ResultSeries } from '../data';
import type { ResultCellDrafts } from '../ResultsDraftStore';
import { TimeSeriesPayloadDateTimeColumn } from './TimeSeriesPayloadDateTimeColumn';

interface TimeSeriesPayloadTableProps {
  dataset: ResultDataset | null;
  series: ResultSeries[];
  selectedIndex: number;
  onSelectIndex: (index: number) => void;
  onDraftChange: (index: number, seriesId: string, raw: string) => void;
  onDraftCommit: (index: number, seriesId: string, value: number | null) => void;
  editable: boolean;
  language: 'zh' | 'en';
  drafts: ResultCellDrafts;
}

function numberFromDraft(raw: string): number | null | undefined {
  const trimmed = raw.trim();
  if (!trimmed) return null;
  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? parsed : undefined;
}

function EditableCell({ value, rawDraft, label, language, onSelect, onDraftChange, onCommit }: {
  value: number | null | undefined;
  rawDraft: string | undefined;
  label: string;
  language: 'zh' | 'en';
  onSelect: () => void;
  onDraftChange: (raw: string) => void;
  onCommit: (value: number | null) => void;
}) {
  const errorId = useId();
  const raw = rawDraft ?? (value === null || value === undefined ? '' : String(value));
  const parsed = numberFromDraft(raw);
  const invalid = parsed === undefined;
  const error = language === 'zh' ? '请输入有限数值，或留空表示无数据。' : 'Enter a finite number, or leave blank for no data.';

  return <div className="results-cell-editor">
    <input
      className={'results-cell-input' + (invalid ? ' is-invalid' : '')}
      aria-label={label}
      aria-invalid={invalid || undefined}
      aria-describedby={invalid ? errorId : undefined}
      inputMode="decimal"
      value={raw}
      onFocus={onSelect}
      onChange={(event) => onDraftChange(event.target.value)}
      onBlur={() => {
        const committed = numberFromDraft(raw);
        if (committed !== undefined) onCommit(committed);
      }}
    />
    {invalid && <span id={errorId} className="results-cell-error" role="alert">{error}</span>}
  </div>;
}

export function TimeSeriesPayloadTable({ dataset, series, selectedIndex, onSelectIndex, onDraftChange, onDraftCommit, editable, language, drafts }: TimeSeriesPayloadTableProps) {
  const selectedRow = useRef<HTMLTableRowElement>(null);
  const text = (zh: string, en: string): string => language === 'zh' ? zh : en;
  const locale = language === 'zh' ? 'zh-CN' : 'en-US';

  useEffect(() => {
    selectedRow.current?.scrollIntoView({ block: 'nearest' });
  }, [selectedIndex]);

  if (!dataset || dataset.times.length === 0) {
    return <div className="results-table-empty">{text('当前没有可显示的数据。', 'There is no data to display.')}</div>;
  }

  return (
    <div className="results-table-frame" role="region" aria-label={text('时间序列表格', 'Time-series table')} tabIndex={0}>
      <table className="results-data-table">
        <thead>
          <tr>
            <th className="results-time-column">{text('时间步', 'Time step')}</th>
            {series.map((item) => <th key={item.id}>{item.name}{item.units ? <small> ({item.units})</small> : null}</th>)}
          </tr>
        </thead>
        <tbody>
          {dataset.times.map((time, index) => <tr
            key={index}
            ref={index === selectedIndex ? selectedRow : undefined}
            className={index === selectedIndex ? 'results-row-selected' : ''}
            onClick={() => onSelectIndex(index)}
            onKeyDown={(event) => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); onSelectIndex(index); } }}
            tabIndex={0}
            aria-selected={index === selectedIndex}
          >
            <td className="results-time-column"><TimeSeriesPayloadDateTimeColumn rowIndex={index} time={time} /></td>
            {series.map((item) => <td key={item.id} onClick={(event) => event.stopPropagation()}>
              {editable ? <EditableCell
                value={item.values[index]}
                rawDraft={drafts[item.id]?.[String(index)]}
                label={text('编辑', 'Edit') + ' ' + item.name + ' ' + time}
                language={language}
                onSelect={() => onSelectIndex(index)}
                onDraftChange={(raw) => onDraftChange(index, item.id, raw)}
                onCommit={(value) => onDraftCommit(index, item.id, value)}
              /> : <span className="results-cell-value">{item.values[index] === null || item.values[index] === undefined ? '—' : Number(item.values[index]).toLocaleString(locale, { maximumFractionDigits: 6 })}</span>}
            </td>)}
          </tr>)}
        </tbody>
      </table>
    </div>
  );
}
