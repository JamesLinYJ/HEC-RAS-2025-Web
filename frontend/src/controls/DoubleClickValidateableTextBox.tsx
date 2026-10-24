import { useEffect, useState } from 'react';
import { ValidateableTextBox } from './ValidateableTextBox';

/** Double-click-to-edit counterpart for Visual.WinUI.Controls.DoubleClickValidateableTextBox. */
export function DoubleClickValidateableTextBox({ id, value, onChange, readOnly = false, disabled = false, ariaInvalid }: {
  id: string;
  value: string;
  onChange: (value: string) => void;
  readOnly?: boolean;
  disabled?: boolean;
  ariaInvalid?: boolean;
}) {
  const [editing, setEditing] = useState(false);
  const canEdit = !readOnly && !disabled;
  useEffect(() => {
    if (!canEdit) setEditing(false);
  }, [canEdit]);

  if (editing && canEdit) {
    return <ValidateableTextBox
      id={id}
      value={value}
      onChange={onChange}
      ariaInvalid={ariaInvalid}
      focusOnMount
      onBlur={() => setEditing(false)}
      onKeyDown={(event) => {
        if (event.key === 'Enter') event.currentTarget.blur();
      }}
    />;
  }

  return <span
    id={id}
    className="editor-double-click-validateable-text-value"
    role={canEdit ? 'button' : undefined}
    tabIndex={canEdit ? 0 : undefined}
    aria-label={value || undefined}
    aria-readonly={readOnly || undefined}
    aria-disabled={disabled || undefined}
    onDoubleClick={() => canEdit && setEditing(true)}
    onKeyDown={(event) => {
      if (canEdit && (event.key === 'Enter' || event.key === 'F2')) setEditing(true);
    }}
  >{value || '\u00a0'}</span>;
}
