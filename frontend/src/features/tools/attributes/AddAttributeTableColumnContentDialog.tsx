import { useEffect, useState } from 'react';
import type { Language } from '../../../shared/workbench';
import type { AttributeTableMutation } from './attributeTableTypes';
import { AddAttributeTableColumnControl } from './AddAttributeTableColumnControl';

type NewColumnType = Extract<AttributeTableMutation, { operation: 'addColumn' }>['type'];

interface Props {
  open: boolean;
  language: Language;
  existingNames: string[];
  busy: boolean;
  onApply: (name: string, type: NewColumnType) => Promise<boolean>;
  onClose: () => void;
}

export function AddAttributeTableColumnContentDialog({ open, language, existingNames, busy, onApply, onClose }: Props) {
  const zh = language === 'zh';
  const [name, setName] = useState('');
  const [type, setType] = useState<NewColumnType>('String');
  useEffect(() => { if (open) { setName(''); setType('String'); } }, [open]);
  if (!open) return null;
  const validName = Boolean(name.trim()) && !existingNames.some(value => value.trim().toLocaleLowerCase() === name.trim().toLocaleLowerCase());
  const submit = async (): Promise<void> => {
    if (!validName || busy) return;
    if (await onApply(name.trim(), type)) onClose();
  };
  return <div className="attribute-dialog-backdrop" role="presentation" onMouseDown={event => { if (event.target === event.currentTarget && !busy) onClose(); }}>
    <section className="attribute-dialog" role="dialog" aria-modal="true" aria-labelledby="attribute-add-dialog-title">
      <header><div><span className="attribute-eyebrow">Attribute table</span><h3 id="attribute-add-dialog-title">{zh ? '添加属性表字段' : 'Add attribute table field'}</h3></div>
        <button type="button" className="attribute-close" disabled={busy} onClick={onClose} aria-label={zh ? '关闭' : 'Close'}>×</button></header>
      <AddAttributeTableColumnControl language={language} name={name} type={type} existingNames={existingNames} onNameChange={setName} onTypeChange={setType} />
      <footer><button type="button" disabled={busy} onClick={onClose}>{zh ? '取消' : 'Cancel'}</button>
        <button type="button" className="is-primary" disabled={busy || !validName} onClick={() => void submit()}>{busy ? (zh ? '应用中…' : 'Applying…') : (zh ? '添加到预览' : 'Add to preview')}</button></footer>
    </section>
  </div>;
}
