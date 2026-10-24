import { useMemo, useState } from 'react';
import type { FormEvent } from 'react';
import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import { walkCatalogTree } from './CatalogTreeSnapshot';
import type { CatalogTreeNode, CatalogTreeSnapshot } from './CatalogTreeSnapshot';
import type { ExternalConnectionValues } from './ExternalConnectionWorkflow';

function projectResults(snapshot: CatalogTreeSnapshot | null): CatalogTreeNode[] {
  return snapshot ? walkCatalogTree(snapshot.roots).filter((node) =>
    node.sourceClass === 'ObservableResult' && typeof node.assetID === 'string' && node.assetID.length > 0) : [];
}

export function NewResultExternalConnectionPage({ language, snapshot, busy, onSubmit, onBack }: {
  language: Language;
  snapshot: CatalogTreeSnapshot | null;
  busy: boolean;
  onSubmit: (values: ExternalConnectionValues) => void;
  onBack: () => void;
}) {
  const results = useMemo(() => projectResults(snapshot), [snapshot]);
  const [selectedID, setSelectedID] = useState('');
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [validation, setValidation] = useState<string>();
  const selected = results.find((item) => item.id === selectedID);

  const submit = (event: FormEvent<HTMLFormElement>): void => {
    event.preventDefault();
    if (!selected?.assetID) {
      setValidation(text(language, '请从当前工程目录选择一个真实结果 HDF。', 'Choose an actual result HDF from the current project catalog.'));
      return;
    }
    if (!name.trim()) {
      setValidation(text(language, '请输入连接名称。', 'Enter a connection name.'));
      return;
    }
    setValidation(undefined);
    onSubmit({
      action: 'add',
      kind: 'result',
      name: name.trim(),
      path: selected.assetID,
      resultName: selected.name,
      description: description.trim(),
    });
  };

  return (
    <form className="project-external-form" onSubmit={submit}>
      <div className="project-operation-kicker">NewResultExternalConnectionPage</div>
      <h3>{text(language, '添加工程结果连接', 'Add project result connection')}</h3>
      <p className="project-form-hint">{text(language, '结果文件来自当前工程目录；列表为空时请先刷新工程目录。', 'Result files come from the active project catalog. Refresh the catalog if this list is empty.')}</p>
      <label className="project-form-field">
        <span>{text(language, `工程结果（${results.length}）`, `Project results (${results.length})`)} <b>*</b></span>
        <select data-testid="project-external-result" value={selectedID} onChange={(event) => setSelectedID(event.target.value)} disabled={busy || results.length === 0}>
          <option value="">{text(language, '选择真实结果 HDF', 'Choose an actual result HDF')}</option>
          {results.map((result) => <option key={result.id} value={result.id} title={result.assetID ?? ''}>{result.name} · {result.assetID}</option>)}
        </select>
      </label>
      {selected?.assetID && <div className="project-external-source"><span>{text(language, '结果文件', 'Result file')}</span><code>{selected.assetID}</code></div>}
      {results.length === 0 && <p className="project-tree-state">{text(language, '当前工程目录没有可供连接的结果对象。', 'The active project catalog has no result items to connect.')}</p>}
      <label className="project-form-field"><span>{text(language, '连接名称', 'Connection name')} <b>*</b></span><input value={name} onChange={(event) => setName(event.target.value)} maxLength={180} autoComplete="off" /></label>
      <label className="project-form-field"><span>{text(language, '说明', 'Description')}</span><textarea rows={2} value={description} onChange={(event) => setDescription(event.target.value)} /></label>
      {validation && <p className="project-form-error" role="alert"><span>!</span>{validation}</p>}
      <div className="project-external-form-footer">
        <button type="button" className="project-button project-button-secondary" disabled={busy} onClick={onBack}>{text(language, '返回', 'Back')}</button>
        <button type="submit" className="project-button project-button-primary" data-testid="project-external-connection-save" disabled={busy || results.length === 0}>
          {busy ? text(language, '正在检查并提交…', 'Inspecting and committing…') : text(language, '检查并提交', 'Inspect and commit')}
        </button>
      </div>
    </form>
  );
}
