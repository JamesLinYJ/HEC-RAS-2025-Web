/** Controlled counterpart for a radio option paired with an editable raw text value. */
export function RadioButtonInputTextPropertyItem({
  id,
  value,
  title,
  groupName,
  optionValue,
  selectedValue,
  disabled = false,
  readOnly = false,
  readOnlyLabel,
  ariaInvalid = false,
  onTextChange,
  onSelectionChange,
}: {
  id: string;
  value: string;
  title: string;
  groupName: string;
  optionValue: string;
  selectedValue: string;
  disabled?: boolean;
  readOnly?: boolean;
  readOnlyLabel?: string;
  ariaInvalid?: boolean;
  onTextChange: (value: string) => void;
  onSelectionChange: (value: string) => void;
}) {
  const radioID = `${id}-radio`;
  return <div className="editor-radio-text-property-item">
    <label className="editor-radio-text-choice" htmlFor={radioID}>
      <input id={radioID} type="radio" name={groupName} value={optionValue} checked={selectedValue === optionValue} disabled={disabled || readOnly} onChange={() => onSelectionChange(optionValue)} />
      <span>{title}</span>
      {readOnly && readOnlyLabel && <span className="editor-readonly-tag">{readOnlyLabel}</span>}
    </label>
    <input id={id} className="editor-radio-text-value" type="text" value={value} readOnly={readOnly} disabled={disabled} aria-label={title} aria-invalid={ariaInvalid || undefined} onChange={(event) => onTextChange(event.currentTarget.value)} />
  </div>;
}
