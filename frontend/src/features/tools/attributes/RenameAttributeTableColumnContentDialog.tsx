import { useEffect, useState } from 'react';
import type { Language } from '../../../shared/workbench';
import { RenameAttributeTableColumnControl } from './RenameAttributeTableColumnControl';

interface Props {
  open: boolean;
  language: Language;
  originalName: string;
  existingNames: string[];
  busy: boolean;
  onApply: (name: string) => Promise<boolean>;
  onClose: () => void;
}

export function RenameAttributeTableColumnContentDialog({ open, language, originalName, existingNames, busy, onApply, onClose }: Props) {
  const zh = language === 'zh';
  const [name, setName] = useState(originalName);
  useEffect(() => { if (open) setName(originalName); }, [open, originalName]);
  if (!open) return null;
  const validName = Boolean(name.trim()) && !existingNames.some(value => value.trim().toLocaleLowerCase() === name.trim().toLocaleLowerCase());
  const submit = async (): Promise<void> => {
    if (!validName || busy) return;
    if (await onApply(name.trim())) onClose();
  };
  return <div className="attribute-dialog-backdrop" role="presentation" onMouseDown={event => { if (event.target === event.currentTarget && !busy) onClose(); }}>
    <section className="attribute-dialog" role="dialog" aria-modal="true" aria-labelledby="attribute-rename-dialog-title">
      <header><div><span className="attribute-eyebrow">Attribute table</span><h3 id="attribute-rename-dialog-title">{zh ? '重命名属性表字段' : 'Rename attribute table field'}</h3></div>
        <button type="button" className="attribute-close" disabled={busy} onClick={onClose} aria-label={zh ? '关闭' : 'Close'}>×</button></header>
      <RenameAttributeTableColumnControl language={language} originalName={originalName} name={name} existingNames={existingNames} onNameChange={setName} />
      <footer><button type="button" disabled={busy} onClick={onClose}>{zh ? '取消' : 'Cancel'}</button>
        <button type="button" className="is-primary" disabled={busy || !validName || name.trim() === originalName} onClick={() => void submit()}>{busy ? (zh ? '应用中…' : 'Applying…') : (zh ? '重命名到预览' : 'Rename in preview')}</button></footer>
    </section>
  </div>;
}
