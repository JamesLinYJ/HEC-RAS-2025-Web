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
}: ExternalConnectionLinkerProps) {
  const hasSelectedChoice = choices.some((choice) => choice.id === selectedConnectionId);

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
    <small className="editor-field-muted">{language === 'zh' ? '仅支持选择已有连接，此处暂不支持导入。' : 'Select an existing connection. Importing connections is not available here.'}</small>
  </div>;
}
