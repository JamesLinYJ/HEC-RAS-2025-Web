import { useState } from 'react';
import type { EditorPageProps, ModelSnapshot } from './model';

type TerrainInput = NonNullable<ModelSnapshot['terrain']>['inputs'][number];

/** Selected-input header, including its editable source name and returned metadata. */
export function DetailedTerrainInputHeader({ language, input, canEdit, value, onNameChange, onRemove }: {
  language: 'zh' | 'en';
  input: TerrainInput;
  canEdit: boolean;
  value: string;
  onNameChange: (fieldID: string, value: string) => void;
  onRemove?: EditorPageProps['onTerrainTransform'];
}) {
  const fieldID = `${input.scope}/Name`;
  const [confirmingRemove, setConfirmingRemove] = useState(false);
  const [removing, setRemoving] = useState(false);
  const remove = async () => {
    if (!onRemove || removing) return;
    setRemoving(true);
    try {
      if (await onRemove({ operation: 'remove', itemId: input.id })) setConfirmingRemove(false);
    } finally {
      setRemoving(false);
    }
  };
  return (
    <header className="editor-terrain-input-header">
      <div className="editor-terrain-table-heading">
        <div>
          <p className="editor-eyebrow">{language === 'zh' ? '当前输入' : 'Selected input'}</p>
          <h3>{value || input.name}</h3>
        </div>
        {onRemove && !confirmingRemove && <button type="button" className="editor-button" disabled={!canEdit || removing} onClick={() => setConfirmingRemove(true)}>
          {language === 'zh' ? '从草稿移除…' : 'Remove from draft…'}
        </button>}
      </div>
      {onRemove && confirmingRemove && <div className="editor-field-error" role="group" aria-label={language === 'zh' ? '确认移除地形输入' : 'Confirm terrain input removal'}>
        <span>{language === 'zh' ? `从地形草稿移除“${value || input.name}”？保存后才会写入源文件。` : `Remove “${value || input.name}” from the terrain draft? The source file changes only when the model is saved.`}</span>
        <div className="editor-button-row">
          <button type="button" className="editor-button" disabled={!canEdit || removing} onClick={() => void remove()}>{removing ? (language === 'zh' ? '正在移除…' : 'Removing…') : (language === 'zh' ? '确认移除' : 'Confirm removal')}</button>
          <button type="button" className="editor-button" disabled={removing} onClick={() => setConfirmingRemove(false)}>{language === 'zh' ? '取消' : 'Cancel'}</button>
        </div>
      </div>}
      <div className="editor-field-grid">
        <label className="editor-field" htmlFor="terrain-input-name">
          <span>{language === 'zh' ? '输入名称' : 'Input name'}</span>
          <input id="terrain-input-name" type="text" value={value} disabled={!canEdit} onChange={(event) => onNameChange(fieldID, event.currentTarget.value)} />
        </label>
      </div>
    </header>
  );
}
