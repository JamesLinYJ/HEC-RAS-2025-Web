import { useState } from 'react';
import type { FormEvent } from 'react';
import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import { isAbsoluteServerPath } from './ProjectTypes';
import type { ExternalConnectionAction, ExternalConnectionKind, ExternalConnectionValues } from './ExternalConnectionWorkflow';
import type { ExternalConnectionIdentity } from './ExternalConnectionWorkflow';
import { WellKnownExternalPathEditor } from './WellKnownExternalPathEditor';

export function NewFileExternalConnectionPage({ language, action, kind = 'file', connection, busy, onSubmit, onBack }: {
  language: Language;
  action: Extract<ExternalConnectionAction, 'add' | 'update'>;
  kind?: Extract<ExternalConnectionKind, 'file' | 'directory'>;
  connection?: ExternalConnectionIdentity;
  busy: boolean;
  onSubmit: (values: ExternalConnectionValues) => void;
  onBack: () => void;
}) {
  const [name, setName] = useState(action === 'update' ? connection?.name ?? '' : '');
  const [path, setPath] = useState(action === 'update' ? connection?.path ?? '' : '');
  const [description, setDescription] = useState(action === 'update' ? connection?.description ?? '' : '');
  const [searchPattern, setSearchPattern] = useState(action === 'update' ? connection?.searchPattern ?? '*' : '*');
  const [recurse, setRecurse] = useState(action === 'update' ? connection?.recurse ?? false : false);
  const [validation, setValidation] = useState<string>();

  const submit = (event: FormEvent<HTMLFormElement>): void => {
    event.preventDefault();
    if (!name.trim()) {
      setValidation(text(language, '请输入连接名称。', 'Enter a connection name.'));
      return;
    }
    if (!isAbsoluteServerPath(path.trim())) {
      setValidation(text(language, '请输入服务所在机器上的绝对路径。', 'Enter an absolute path on the machine running the service.'));
      return;
    }
    setValidation(undefined);
    onSubmit({
      action,
      kind,
      ...(action === 'update' && connection?.name ? { connectionName: connection.name } : {}),
      name: name.trim(),
      path: path.trim(),
      description: description.trim(),
      ...(kind === 'directory' ? { searchPattern: searchPattern.trim() || '*', recurse } : {}),
    });
  };

  return (
    <form className="project-external-form" onSubmit={submit}>
      <div className="project-operation-kicker">{text(language, '文件连接', 'External connection')}</div>
      <h3>{text(language, action === 'add' ? '添加外部文件连接' : '编辑外部连接', action === 'add' ? 'Add external file connection' : 'Edit external connection')}</h3>
      {action === 'update' && connection && <p className="project-form-hint">{text(language, '当前连接', 'Current connection')}: <code>{connection.name}</code></p>}
      <label className="project-form-field"><span>{text(language, '连接名称', 'Connection name')} <b>*</b></span><input value={name} onChange={(event) => setName(event.target.value)} maxLength={180} autoComplete="off" /></label>
      <WellKnownExternalPathEditor language={language} value={path} onChange={setPath} />
      <label className="project-form-field"><span>{text(language, '说明', 'Description')}</span><textarea rows={2} value={description} onChange={(event) => setDescription(event.target.value)} /></label>
      {kind === 'directory' && <div className="project-external-directory-fields">
        <label className="project-form-field"><span>{text(language, '文件名模式', 'Filename pattern')}</span><input value={searchPattern} onChange={(event) => setSearchPattern(event.target.value)} /></label>
        <label className="project-external-check"><input type="checkbox" checked={recurse} onChange={(event) => setRecurse(event.target.checked)} /><span>{text(language, '包含子目录', 'Include subfolders')}</span></label>
      </div>}
      {validation && <p className="project-form-error" role="alert"><span>!</span>{validation}</p>}
      <div className="project-external-form-footer">
        <button type="button" className="project-button project-button-secondary" disabled={busy} onClick={onBack}>{text(language, '返回', 'Back')}</button>
        <button type="submit" className="project-button project-button-primary" data-testid="project-external-connection-save" disabled={busy}>
          {busy ? text(language, '正在检查并提交…', 'Inspecting and committing…') : text(language, '检查并提交', 'Inspect and commit')}
        </button>
      </div>
    </form>
  );
}
