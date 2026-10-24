import { useEffect, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import type { Language } from '../../shared/workbench';
import { gchoice, gfield, gt } from './geometryText';
import type { GeometryField, GeometryRow } from './geometryTypes';

export interface GeometryDetailFieldsProps {
  language: Language;
  title: string;
  header?: ReactNode;
  rows: GeometryRow[];
  localRowIDs?: ReadonlySet<string>;
  fields: GeometryField[];
  canEdit: boolean;
  busy: boolean;
  onChange: (row: GeometryRow, field: GeometryField, value: string, isLocal: boolean) => void;
  onCommand?: (row: GeometryRow, field: GeometryField, command: string, value?: string) => void;
  onSelectionChange?: (rows: GeometryRow[], field: GeometryField, value: string) => void;
  onSelectionCommand?: (rows: GeometryRow[], field: GeometryField, command: string, value?: string) => void;
}

function DetailField({
  language, row, field, value, isLocal, canEdit, busy, onChange, onCommand,
}: {
  language: Language;
  row: GeometryRow;
  field: GeometryField;
  value: string;
  isLocal: boolean;
  canEdit: boolean;
  busy: boolean;
  onChange: GeometryDetailFieldsProps['onChange'];
  onCommand?: GeometryDetailFieldsProps['onCommand'];
}) {
  const [draft, setDraft] = useState(value);
  useEffect(() => setDraft(value), [value]);
  const disabled = busy || !canEdit || !!field.readOnly;
  const commit = (next = draft): void => {
    if (next === value || disabled) return;
    if (field.forceSetCommand && onCommand && !isLocal) onCommand(row, field, field.forceSetCommand, next);
    else onChange(row, field, next, isLocal);
  };
  const errors = row.errors?.[field.errorValidationPropertyName ?? field.id] ?? [];
  const isBoolean = ['bool', 'boolean'].includes(field.sourceType.toLowerCase());

  return (
    <label className={`geometry-detail-field${errors.length ? ' has-error' : ''}`}>
      <span>{gfield(language, field.id, field.title)}</span>
      {field.choices?.length ? (
        <select
          value={draft}
          disabled={disabled}
          aria-label={gfield(language, field.id, field.title)}
          onChange={(event) => { setDraft(event.target.value); commit(event.target.value); }}
        >
          <option value="">{gt(language, 'select')}</option>
          {field.choices.map((choice) => <option key={choice} value={choice}>{gchoice(language, choice)}</option>)}
        </select>
      ) : isBoolean ? (
        <select
          value={draft.toLowerCase() === 'true' ? 'true' : draft.toLowerCase() === 'false' ? 'false' : ''}
          disabled={disabled}
          aria-label={gfield(language, field.id, field.title)}
          onChange={(event) => { setDraft(event.target.value); commit(event.target.value); }}
        >
          <option value="">—</option><option value="true">{gt(language, 'yes')}</option><option value="false">{gt(language, 'no')}</option>
        </select>
      ) : field.sourceType.toLowerCase() === 'string' && field.id === 'Recipe' ? (
        <textarea
          value={draft}
          disabled={disabled}
          aria-label={gfield(language, field.id, field.title)}
          onChange={(event) => setDraft(event.target.value)}
          onBlur={() => commit()}
          onKeyDown={(event) => { if (event.key === 'Enter' && (event.metaKey || event.ctrlKey)) { commit(); event.currentTarget.blur(); } }}
        />
      ) : (
        <input
          value={draft}
          disabled={disabled}
          aria-label={gfield(language, field.id, field.title)}
          inputMode={['int', 'integer', 'float', 'double', 'number'].includes(field.sourceType.toLowerCase()) ? 'decimal' : undefined}
          onChange={(event) => setDraft(event.target.value)}
          onBlur={() => commit()}
          onKeyDown={(event) => { if (event.key === 'Enter') { commit(); event.currentTarget.blur(); } }}
        />
      )}
      {field.showClearButton && field.clearCommand && (
        <button
          type="button"
          className="geometry-detail-clear"
          disabled={disabled}
          onClick={() => {
            if (!disabled && !isLocal && onCommand) onCommand(row, field, field.clearCommand!);
            else if (!disabled && isLocal) { setDraft(''); onChange(row, field, '', true); }
          }}
        >{language === 'zh' ? '清除' : 'Clear'}</button>
      )}
      {errors.map((error, index) => <small className="geometry-detail-error" key={`${index}-${error}`}>{error}</small>)}
    </label>
  );
}

function MultiDetailField({
  language, rows, field, canEdit, busy, onChange, onCommand,
}: {
  language: Language;
  rows: GeometryRow[];
  field: GeometryField;
  canEdit: boolean;
  busy: boolean;
  onChange: NonNullable<GeometryDetailFieldsProps['onSelectionChange']>;
  onCommand?: GeometryDetailFieldsProps['onSelectionCommand'];
}) {
  const values = rows.map((row) => displaySourceValue(row.values[field.id] ?? '', field));
  const mixed = values.some((value) => value !== values[0]);
  const sharedValue = mixed ? '' : values[0] ?? '';
  const selectionKey = rows.map((row) => row.id).join('|');
  const [draft, setDraft] = useState(sharedValue);
  const lastCommitted = useRef<string | undefined>(undefined);
  useEffect(() => { setDraft(sharedValue); lastCommitted.current = undefined; }, [sharedValue, selectionKey]);

  const disabled = busy || !canEdit || !!field.readOnly;
  const commit = (next = draft): void => {
    if (disabled || (!mixed && next === sharedValue) || lastCommitted.current === next) return;
    lastCommitted.current = next;
    if (field.forceSetCommand && onCommand) onCommand(rows, field, field.forceSetCommand, next);
    else onChange(rows, field, next);
  };
  const errors = [...new Set(rows.flatMap((row) => row.errors?.[field.errorValidationPropertyName ?? field.id] ?? []))];
  const isBoolean = ['bool', 'boolean'].includes(field.sourceType.toLowerCase());
  const multipleText = language === 'zh' ? '多个值；选择或输入后统一设置' : 'Multiple values; choose or enter one value to apply it to all selected rows';

  return (
    <label className={`geometry-detail-field${errors.length ? ' has-error' : ''}`}>
      <span>{gfield(language, field.id, field.title)}</span>
      {field.choices?.length ? (
        <select value={draft} disabled={disabled} aria-label={gfield(language, field.id, field.title)} onChange={(event) => { setDraft(event.target.value); commit(event.target.value); }}>
          <option value="">{mixed ? multipleText : gt(language, 'select')}</option>
          {field.choices.map((choice) => <option key={choice} value={choice}>{gchoice(language, choice)}</option>)}
        </select>
      ) : isBoolean ? (
        <select value={draft.toLowerCase() === 'true' ? 'true' : draft.toLowerCase() === 'false' ? 'false' : ''} disabled={disabled} aria-label={gfield(language, field.id, field.title)} onChange={(event) => { setDraft(event.target.value); commit(event.target.value); }}>
          <option value="">{mixed ? multipleText : '—'}</option><option value="true">{gt(language, 'yes')}</option><option value="false">{gt(language, 'no')}</option>
        </select>
      ) : (
        <input
          value={draft}
          placeholder={mixed ? multipleText : undefined}
          disabled={disabled}
          aria-label={gfield(language, field.id, field.title)}
          inputMode={['int', 'integer', 'float', 'double', 'number'].includes(field.sourceType.toLowerCase()) ? 'decimal' : undefined}
          onChange={(event) => setDraft(event.target.value)}
          onBlur={() => commit()}
          onKeyDown={(event) => { if (event.key === 'Enter') { commit(); event.currentTarget.blur(); } }}
        />
      )}
      {field.showClearButton && field.clearCommand && (
        <button type="button" className="geometry-detail-clear" disabled={disabled} onClick={() => onCommand?.(rows, field, field.clearCommand!)}>{language === 'zh' ? '重置' : 'Reset'}</button>
      )}
      {errors.map((error, index) => <small className="geometry-detail-error" key={`${index}-${error}`}>{error}</small>)}
    </label>
  );
}

export function GeometryDetailFields({
  language, title, header, rows, localRowIDs = new Set(), fields, canEdit, busy, onChange, onCommand, onSelectionChange, onSelectionCommand,
}: GeometryDetailFieldsProps) {
  const selected = rows.length === 1 ? rows[0] : undefined;
  const isLocal = selected ? localRowIDs.has(selected.id) : false;
  const uniqueFields = fields.filter((field, index) => fields.findIndex((candidate) => candidate.id === field.id) === index);

  return (
    <section className="geometry-panel geometry-detail-panel" aria-label={title}>
      <header className="geometry-panel-heading">{header ?? <div><h3>{title}</h3><p>{rows.length} {gt(language, 'selected')}</p></div>}</header>
      {!selected ? (
        rows.length > 1 && onSelectionChange ? (
          uniqueFields.length === 0 ? <div className="geometry-empty geometry-detail-empty">{language === 'zh' ? '此响应没有提供可编辑的源字段。' : 'This response contains no source fields for this editor.'}</div> : (
            <div className="geometry-detail-fields">
              {uniqueFields.map((field) => <MultiDetailField key={field.id} language={language} rows={rows} field={field} canEdit={canEdit} busy={busy} onChange={onSelectionChange} onCommand={onSelectionCommand} />)}
            </div>
          )
        ) : <div className="geometry-empty geometry-detail-empty"><strong>{rows.length === 0 ? (language === 'zh' ? '先从上方表格选择一项。' : 'Select one row in the table above.') : (language === 'zh' ? '详细编辑一次处理一项，请只保留一行选中。' : 'Detailed editing handles one item at a time. Select a single row.')}</strong></div>
      ) : uniqueFields.length === 0 ? (
        <div className="geometry-empty geometry-detail-empty">{language === 'zh' ? '此响应没有提供可编辑的源字段。' : 'This response contains no source fields for this editor.'}</div>
      ) : (
        <div className="geometry-detail-fields">
          {uniqueFields.map((field) => (
            <DetailField
              key={`${selected.id}:${field.id}`}
              language={language}
              row={selected}
              field={field}
              value={displaySourceValue(selected.values[field.id] ?? '', field)}
              isLocal={isLocal}
              canEdit={canEdit}
              busy={busy}
              onChange={onChange}
              onCommand={onCommand}
            />
          ))}
        </div>
      )}
    </section>
  );
}

function displaySourceValue(value: string, field: GeometryField): string {
  const numericType = ['int', 'integer', 'float', 'double', 'number'].includes(field.sourceType.toLowerCase());
  return numericType && value.trim().toLowerCase() === 'nan' ? '' : value;
}
