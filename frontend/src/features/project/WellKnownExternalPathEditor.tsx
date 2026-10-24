import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';

export function WellKnownExternalPathEditor({ language, value, onChange, readOnly = false, hint }: {
  language: Language;
  value: string;
  onChange: (value: string) => void;
  readOnly?: boolean;
  hint?: string;
}) {
  return (
    <label className="project-form-field project-external-path-editor">
      <span>{text(language, '服务器绝对路径', 'Server absolute path')} <b>*</b></span>
      <input
        type="text"
        value={value}
        readOnly={readOnly}
        autoComplete="off"
        spellCheck={false}
        placeholder={text(language, '输入运行工程服务的机器上的完整路径', 'Enter the full path on the machine running the project service')}
        onChange={(event) => onChange(event.target.value)}
      />
        <small>{hint ?? text(language, '路径由服务端检查；浏览器本地路径不会自动映射到服务器。', 'The service validates this path. Browser-local paths are not mapped to the server.')}</small>
    </label>
  );
}
