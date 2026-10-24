import { useEffect, useMemo, useState } from 'react';
import type { Language } from '../../shared/workbench';
import { gchoice, gfield, gkind, gt } from './geometryText';
import type { GeometryField, GeometryRow, GeometryTable } from './geometryTypes';

interface GeometryFeatureTableProps {
  language: Language;
  kind: string;
  table?: GeometryTable;
  localRows?: GeometryRow[];
  editable?: boolean;
  allowLocalAdd?: boolean;
  busy?: boolean;
  onChange?: (row: GeometryRow, field: GeometryField, value: string, isLocal: boolean) => void;
  onDelete?: (rows: GeometryRow[]) => void;
  onLocalAdd?: () => void;
  onSelect?: (ids: string[]) => void;
  selectedRowIDs?: string[];
}

function CellEditor({
  field,
  value,
  language,
  disabled,
  onCommit,
}: {
  field: GeometryField;
  value: string;
  language: 'zh' | 'en';
  disabled: boolean;
  onCommit: (value: string) => void;
}) {
  const [draft, setDraft] = useState(value);
  useEffect(() => setDraft(value), [value]);
  const commit = (): void => {
    if (draft !== value) onCommit(draft);
  };

  if (field.choices?.length) {
    return (
      <select
        className="geometry-table-cell-input"
        value={draft}
        disabled={disabled}
        aria-label={gfield(language, field.id, field.title)}
        onChange={(event) => { setDraft(event.target.value); onCommit(event.target.value); }}
      >
        <option value="">—</option>
        {field.choices.map((choice) => <option key={choice} value={choice}>{gchoice(language, choice)}</option>)}
      </select>
    );
  }
  if (field.sourceType.toLowerCase() === 'bool' || field.sourceType.toLowerCase() === 'boolean') {
    return (
      <select
        className="geometry-table-cell-input"
        value={draft.toLowerCase() === 'true' ? 'true' : draft.toLowerCase() === 'false' ? 'false' : ''}
        disabled={disabled}
        aria-label={gfield(language, field.id, field.title)}
        onChange={(event) => { setDraft(event.target.value); onCommit(event.target.value); }}
      >
        <option value="">—</option><option value="true">{gt(language, 'yes')}</option><option value="false">{gt(language, 'no')}</option>
      </select>
    );
  }
  return (
    <input
      className="geometry-table-cell-input"
      value={draft}
      disabled={disabled}
      aria-label={gfield(language, field.id, field.title)}
      inputMode={['int', 'integer', 'float', 'double', 'number'].includes(field.sourceType.toLowerCase()) ? 'decimal' : 'text'}
      onChange={(event) => setDraft(event.target.value)}
      onBlur={commit}
      onKeyDown={(event) => { if (event.key === 'Enter') { commit(); event.currentTarget.blur(); } }}
    />
  );
}

export function GeometryFeatureTable({
  language,
  kind,
  table,
  localRows = [],
  editable = false,
  allowLocalAdd = true,
  busy = false,
  onChange,
  onDelete,
  onLocalAdd,
  onSelect,
  selectedRowIDs,
}: GeometryFeatureTableProps) {
  const [filter, setFilter] = useState('');
  const [internalSelectedIDs, setInternalSelectedIDs] = useState<string[]>([]);
  const selectedIDs = selectedRowIDs ?? internalSelectedIDs;
  const sourceRows = table?.rows ?? [];
  const allRows = useMemo(() => [...sourceRows, ...localRows], [sourceRows, localRows]);
  const columns = table?.columns ?? [];
  const fieldsById = useMemo(() => new Map((table?.editableFields ?? []).map((field) => [field.id, field])), [table?.editableFields]);
  const visibleRows = useMemo(() => {
    const needle = filter.trim().toLocaleLowerCase();
    if (!needle) return allRows;
    return allRows.filter((row) => [row.id, String(row.fid ?? ''), ...Object.values(row.values ?? {})]
      .some((value) => String(value).toLocaleLowerCase().includes(needle)));
  }, [allRows, filter]);
  const visibleIDSet = useMemo(() => new Set(visibleRows.map((row) => row.id)), [visibleRows]);
  const selectionCount = selectedIDs.filter((id) => visibleIDSet.has(id)).length;
  const selectedRows = allRows.filter((row) => selectedIDs.includes(row.id));
  const localIDSet = new Set(localRows.map((row) => row.id));

  const updateSelection = (ids: string[]): void => {
    if (selectedRowIDs === undefined) setInternalSelectedIDs(ids);
    onSelect?.(ids);
  };

  const toggleRow = (id: string): void => {
    const next = selectedIDs.includes(id) ? selectedIDs.filter((current) => current !== id) : [...selectedIDs, id];
    updateSelection(next);
  };

  const toggleVisible = (): void => {
    const allVisibleSelected = visibleRows.length > 0 && visibleRows.every((row) => selectedIDs.includes(row.id));
    const next = allVisibleSelected
      ? selectedIDs.filter((id) => !visibleIDSet.has(id))
      : [...new Set([...selectedIDs, ...visibleRows.map((row) => row.id)])];
    updateSelection(next);
  };

  const commitDelete = (): void => {
    if (!selectedRows.length || busy) return;
    onDelete?.(selectedRows);
    const selected = selectedIDs.filter((id) => !selectedRows.some((row) => row.id === id));
    updateSelection(selected);
  };

  return (
    <section className="geometry-panel" aria-label={`${gkind(language, kind)} ${gt(language, 'table')}`}>
      <header className="geometry-panel-heading">
        <div><h2>{gkind(language, kind)} · {gt(language, 'table')}</h2><p>{allRows.length} {gt(language, 'rows')}{selectionCount ? ` · ${selectionCount} ${gt(language, 'selected')}` : ''}</p></div>
        <div className="geometry-toolbar-group">
          <input className="geometry-filter" value={filter} onChange={(event) => setFilter(event.target.value)} placeholder={gt(language, 'filter')} aria-label={gt(language, 'filter')} />
          {allowLocalAdd && <button className="geometry-secondary" disabled={busy} onClick={onLocalAdd}>{gt(language, 'addLocal')}</button>}
          {onDelete && <button className="geometry-secondary" disabled={busy || selectedRows.length === 0} onClick={commitDelete}>{gt(language, 'delete')}</button>}
        </div>
      </header>
      {localRows.length > 0 && <div className="geometry-local-banner">{gt(language, 'localOnly')}</div>}
      {visibleRows.length === 0 ? (
        <div className="geometry-empty">
          <span className="geometry-empty-mark" aria-hidden="true">⌗</span>
          <strong>{gt(language, 'noRows')}</strong>
          <p>{sourceRows.length === 0 && localRows.length === 0 ? gt(language, 'emptyHint') : gt(language, 'validationNote')}</p>
        </div>
      ) : (
        <div className="geometry-table-wrap">
          <table className="geometry-table">
            <thead><tr>
              <th><input type="checkbox" aria-label={gt(language, 'select')} checked={visibleRows.length > 0 && visibleRows.every((row) => selectedIDs.includes(row.id))} onChange={toggleVisible} /></th>
              <th>{gt(language, 'identifier')}</th>
              {columns.map((column) => <th key={column.id}>{gfield(language, column.id, column.title)}</th>)}
              {(table?.rows.some((row) => row.x != null || row.y != null) ?? false) && <><th>X</th><th>Y</th></>}
            </tr></thead>
            <tbody>{visibleRows.map((row) => {
              const isLocal = localIDSet.has(row.id);
              const rowSelected = selectedIDs.includes(row.id);
              const hasCoords = table?.rows.some((item) => item.x != null || item.y != null) ?? false;
              return (
                <tr key={row.id} className={`${rowSelected ? 'is-selected ' : ''}${isLocal ? 'is-local' : ''}`}>
                  <td><input type="checkbox" checked={rowSelected} aria-label={`${gt(language, 'select')} ${row.id}`} onChange={() => toggleRow(row.id)} /></td>
                  <td><span>{row.id}</span>{isLocal && <div className="geometry-local-tag">{gt(language, 'draftTag')}</div>}</td>
                  {columns.map((column) => {
                    const field = fieldsById.get(column.id);
                    const value = row.values?.[column.id] ?? '';
                    const mayEdit = !column.readOnly && field && !field.readOnly && (isLocal || editable) && !!onChange;
                    return <td key={column.id}>
                      {mayEdit ? <CellEditor
                        field={field}
                        value={value}
                        language={language}
                        disabled={busy || (isLocal ? false : !editable)}
                        onCommit={(next) => onChange?.(row, field, next, isLocal)}
                      /> : <span className="geometry-field-readonly">{value || '—'}</span>}
                    </td>;
                  })}
                  {hasCoords && <><td>{row.x ?? '—'}</td><td>{row.y ?? '—'}</td></>}
                </tr>
              );
            })}</tbody>
          </table>
        </div>
      )}
      <footer className="geometry-table-footer">
        <span>{sourceRows.length} {gt(language, 'sourceBacked')}</span>
        <span>{localRows.length ? `${localRows.length} ${gt(language, 'localDraft')}` : gt(language, 'validationNote')}</span>
      </footer>
    </section>
  );
}
