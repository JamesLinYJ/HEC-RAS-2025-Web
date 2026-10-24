import { useState } from 'react';
import type { Language } from '../shared/workbench';
import { text } from '../shared/workbench';

interface RichDescriptionProps {
  id: string;
  value: string;
  language: Language;
  readOnly: boolean;
  disabled: boolean;
  ariaInvalid: boolean;
  placeholder?: string;
  onChange: (value: string) => void;
}

/** Controlled description editor with a text-only preview; user markup is never injected as HTML. */
export function RichDescription({ id, value, language, readOnly, disabled, ariaInvalid, placeholder, onChange }: RichDescriptionProps) {
  const [previewOpen, setPreviewOpen] = useState(false);
  return <div className="editor-rich-description">
    <textarea id={id} rows={5} maxLength={32767} value={value} readOnly={readOnly} disabled={disabled}
      placeholder={placeholder} aria-invalid={ariaInvalid || undefined} onChange={event => onChange(event.currentTarget.value)} />
    <div className="editor-rich-description-actions">
      <small>{text(language, `${value.length}/32767 个字符`, `${value.length}/32767 characters`)}</small>
      <button className="editor-button" type="button" aria-expanded={previewOpen} onClick={() => setPreviewOpen(open => !open)}>
        {previewOpen ? text(language, '收起文字预览', 'Hide text preview') : text(language, '预览文字', 'Preview text')}
      </button>
    </div>
    {previewOpen && <div className="editor-rich-description-preview" aria-label={text(language, '说明文字预览', 'Description preview')}>
      {value ? <p>{value}</p> : <span>{text(language, '暂无说明。', 'No description.')}</span>}
    </div>}
  </div>;
}
