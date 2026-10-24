import type { ChangeEvent } from 'react';

/** Controlled browser counterpart for Visual.WinUI.Controls.InputTextPropertyItem. */
export function InputTextPropertyItem({
  id,
  value,
  onChange,
  readOnly = false,
  disabled = false,
  placeholder,
  acceptsReturn = false,
  suggestions,
  showClearButton = false,
  ariaInvalid,
  clearLabel = '清除',
}: {
  id: string;
  value: string;
  onChange: (value: string) => void;
  readOnly?: boolean;
  disabled?: boolean;
  placeholder?: string;
  acceptsReturn?: boolean;
  suggestions?: string[];
  showClearButton?: boolean;
  ariaInvalid?: boolean;
  clearLabel?: string;
}) {
  const listID = suggestions?.length ? `${id}-suggestions` : undefined;
  const handleChange = (event: ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => onChange(event.currentTarget.value);
  const control = acceptsReturn
    ? <textarea id={id} rows={4} value={value} readOnly={readOnly} disabled={disabled} placeholder={placeholder} aria-invalid={ariaInvalid || undefined} onChange={handleChange} />
    : <input id={id} type="text" value={value} readOnly={readOnly} disabled={disabled} placeholder={placeholder} list={listID} aria-invalid={ariaInvalid || undefined} onChange={handleChange} />;

  return <>
    {showClearButton && !readOnly && !disabled
      ? <span className="editor-property-input-with-clear">{control}<button type="button" className="editor-icon-button" aria-label={clearLabel} onClick={() => onChange('')}>×</button></span>
      : control}
    {listID && <datalist id={listID}>{suggestions?.map((suggestion) => <option key={suggestion} value={suggestion} />)}</datalist>}
  </>;
}
