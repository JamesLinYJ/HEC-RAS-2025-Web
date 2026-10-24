import { useEffect, useMemo, useState } from 'react';
import type { Language } from '../../shared/workbench';
import { ActiveFeaturesControl } from './ActiveFeaturesControl';
import { gchoice, gfield, gt } from './geometryText';
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
  const sourceRows = table?.rows ?? [];
  const allRows = useMemo(() => [...sourceRows, ...localRows], [sourceRows, localRows]);
  const columns = table?.columns ?? [];
  const fieldsById = useMemo(() => new Map((table?.editableFields ?? []).map((field) => [field.id, field])), [table?.editableFields]);
  const localIDSet = new Set(localRows.map((row) => row.id));

  return (
    <ActiveFeaturesControl language={language} kind={kind} rows={allRows} selectedRowIDs={selectedRowIDs} onSelectionChange={onSelect}
      actions={(selectedRows, selection) => <>
        {allowLocalAdd && <button className="geometry-secondary" disabled={busy} onClick={onLocalAdd}>{gt(language, 'addLocal')}</button>}
        {onDelete && <button className="geometry-secondary" disabled={busy || selectedRows.length === 0} onClick={() => {
          onDelete(selectedRows);
          const removed = new Set(selectedRows.map((row) => row.id));
          selection.updateSelection(selection.selectedIDs.filter((id) => !removed.has(id)));
        }}>{gt(language, 'delete')}</button>}
      </>}>
      {({ visibleRows, selectedIDs, toggleRow, toggleVisible }) => <>
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
      </>}
    </ActiveFeaturesControl>
  );
}
