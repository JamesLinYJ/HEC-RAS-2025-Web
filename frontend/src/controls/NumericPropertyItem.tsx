import { NumberBox } from './NumberBox';

/** Property-row adapter for Visual.WinUI.Controls.NumericPropertyItem. */
export function NumericPropertyItem({ id, value, onChange, readOnly = false, disabled = false, ariaInvalid, inputMode = 'decimal', unitLabel }: {
  id: string;
  value: string;
  onChange: (value: string) => void;
  readOnly?: boolean;
  disabled?: boolean;
  ariaInvalid?: boolean;
  inputMode?: 'decimal' | 'numeric';
  unitLabel?: string;
}) {
  return <span className="editor-numeric-property-item">
    <NumberBox id={id} value={value} onChange={onChange} readOnly={readOnly} disabled={disabled} ariaInvalid={ariaInvalid} inputMode={inputMode} />
    {unitLabel && <small className="editor-numeric-unit">{unitLabel}</small>}
  </span>;
}
