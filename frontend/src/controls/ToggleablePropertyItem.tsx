/** Controlled switch counterpart for Visual.WinUI.Controls.ToggleablePropertyItem. */
export function ToggleablePropertyItem({
  id,
  checked,
  title,
  disabled = false,
  onChange,
}: {
  id: string;
  checked: boolean;
  title: string;
  disabled?: boolean;
  onChange: (checked: boolean) => void;
}) {
  return <label className="editor-toggleable-property-item" htmlFor={id}>
    <input id={id} type="checkbox" role="switch" checked={checked} disabled={disabled} aria-checked={checked} onChange={(event) => onChange(event.currentTarget.checked)} />
    <span className="editor-toggleable-track" aria-hidden="true"><span /></span>
    <span className="editor-toggleable-title">{title}</span>
  </label>;
}
