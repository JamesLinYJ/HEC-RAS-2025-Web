import { useEffect, useRef } from 'react';
import type { ChangeEvent, FocusEventHandler, KeyboardEventHandler, Ref } from 'react';

/** Controlled text editor for the value, read-only, return, and suggestion behavior of ValidateableTextBox. */
export function ValidateableTextBox({ id, value, onChange, readOnly = false, disabled = false, placeholder, acceptsReturn = false, suggestions, ariaInvalid, focusOnMount = false, onBlur, onKeyDown }: {
  id: string;
  value: string;
  onChange: (value: string) => void;
  readOnly?: boolean;
  disabled?: boolean;
  placeholder?: string;
  acceptsReturn?: boolean;
  suggestions?: string[];
  ariaInvalid?: boolean;
  focusOnMount?: boolean;
  onBlur?: FocusEventHandler<HTMLInputElement | HTMLTextAreaElement>;
  onKeyDown?: KeyboardEventHandler<HTMLInputElement | HTMLTextAreaElement>;
}) {
  const ref = useRef<HTMLInputElement | HTMLTextAreaElement>(null);
  const listID = suggestions?.length ? `${id}-suggestions` : undefined;
  useEffect(() => {
    if (!focusOnMount || !ref.current) return;
    ref.current.focus();
    ref.current.select();
  }, [focusOnMount]);

  const handleChange = (event: ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => onChange(event.currentTarget.value);
  const inputProps = {
    id,
    value,
    readOnly,
    disabled,
    placeholder,
    'aria-invalid': ariaInvalid || undefined,
    onChange: handleChange,
    onBlur,
    onKeyDown,
  };
  return <>
    {acceptsReturn
      ? <textarea {...inputProps} ref={ref as Ref<HTMLTextAreaElement>} rows={4} />
      : <input {...inputProps} ref={ref as Ref<HTMLInputElement>} type="text" list={listID} autoComplete={listID ? 'off' : undefined} />}
    {listID && <datalist id={listID}>{suggestions?.map((suggestion) => <option key={suggestion} value={suggestion} />)}</datalist>}
  </>;
}
