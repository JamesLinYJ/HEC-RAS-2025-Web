import type { SchemaChoice } from '../features/editors/model';
import type { Language } from '../shared/workbench';

export interface ExternalConnectionLinkerProps {
  id: string;
  label: string;
  selectedConnectionId: string;
  choices: SchemaChoice[];
  disabled?: boolean;
  language: Language;
  placeholder: string;
  emptyMessage: string;
  formatChoice: (choice: SchemaChoice) => string;
  onSelectionChange: (connectionId: string) => void;
  onCreate?: () => void;
  createDisabledReason?: string;
  creating?: boolean;
}

/** Selects one existing external connection from the Host-provided choices. */
export function ExternalConnectionLinker({
  id,
  label,
  selectedConnectionId,
  choices,
  disabled = false,
  language,
  placeholder,
  emptyMessage,
  formatChoice,
  onSelectionChange,
  onCreate,
  createDisabledReason,
  creating = false,
}: ExternalConnectionLinkerProps) {
  const hasSelectedChoice = choices.some((choice) => choice.id === selectedConnectionId);
  const canCreate = Boolean(onCreate) && !createDisabledReason;
  const createHelpID = `${id}-create-help`;

  return <div className="editor-field" data-control="external-connection-linker">
    <label htmlFor={id}>{label}</label>
    <select id={id} value={selectedConnectionId} disabled={disabled || choices.length === 0}
      aria-label={label} onChange={(event) => onSelectionChange(event.currentTarget.value)}>
      {!selectedConnectionId && choices.length > 0 && <option value="" disabled>{placeholder}</option>}
      {selectedConnectionId && !hasSelectedChoice && <option value={selectedConnectionId}>{selectedConnectionId}</option>}
      {choices.length === 0 && <option value="">{emptyMessage}</option>}
      {choices.map((choice) => <option key={choice.id} value={choice.id}>{formatChoice(choice)}</option>)}
    </select>
    {choices.length === 0 && <small className="editor-field-muted">{emptyMessage}</small>}
    <div className="editor-workbench-actions">
      <button type="button" className="editor-button" data-testid="payload-external-connection-create"
        disabled={disabled || !canCreate || creating} aria-describedby={!canCreate ? createHelpID : undefined}
        onClick={onCreate}>
        {creating ? (language === 'zh' ? '正在新建外部连接…' : 'Creating external connection…')
          : (language === 'zh' ? '新建外部连接' : 'Create external connection')}
      </button>
    </div>
    {!canCreate && <small id={createHelpID} className="editor-field-muted">
      {createDisabledReason ?? (language === 'zh' ? '当前工程会话未提供安全的连接创建事务。' : 'The current project session does not provide a safe connection-creation workflow.')}
    </small>}
  </div>;
}
