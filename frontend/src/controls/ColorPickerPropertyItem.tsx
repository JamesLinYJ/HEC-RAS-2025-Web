/** Hex-color subset of Visual.WinUI.Controls.ColorPickerPropertyItem. */
export function ColorPickerPropertyItem({ id, value, onChange, disabled = false, readOnly = false, language = 'zh' }: {
  id: string;
  value: string;
  onChange: (value: string) => void;
  disabled?: boolean;
  readOnly?: boolean;
  language?: 'zh' | 'en';
}) {
  const hexColor = parseRgbHex(value) ?? (value === '' ? '#000000' : null);
  if (!hexColor) {
    return <div className="editor-color-property-item" aria-live="polite">
      <code>{value || '—'}</code>
      <small className="editor-field-muted">{language === 'zh' ? '当前颜色格式暂不支持直接选取。' : 'This color format cannot be edited with the current picker.'}</small>
    </div>;
  }
  return <span className="editor-color-property-item">
    <input id={id} type="color" value={hexColor} disabled={disabled || readOnly} aria-label={language === 'zh' ? '选择颜色' : 'Choose color'} onChange={(event) => onChange(event.currentTarget.value.toUpperCase())} />
    <code>{value || (language === 'zh' ? '未设置' : 'Unset')}</code>
  </span>;
}

function parseRgbHex(value: string): string | null {
  if (/^#[0-9a-f]{6}$/i.test(value)) return value.toUpperCase();
  if (/^#[0-9a-f]{3}$/i.test(value)) return `#${value.slice(1).split('').map((digit) => digit + digit).join('').toUpperCase()}`;
  return null;
}
