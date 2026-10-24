import { useEffect, useMemo, useRef, useState } from 'react';
import type { BackendGateway, Language, WorkbenchContext } from '../../shared/workbench';
import { errorDiagnostics, message, text } from '../../shared/workbench';
import type { CatalogTreeNode, CatalogTreeSnapshot } from './CatalogTreeSnapshot';
import { walkCatalogTree } from './CatalogTreeSnapshot';
import type { ExternalConnectionCommitReceipt, ExternalConnectionIdentity, ExternalConnectionKind, ExternalConnectionValues } from './ExternalConnectionWorkflow';
import { inspectAndCommitExternalConnection } from './ExternalConnectionWorkflow';
import { NewExternalConnectionPage } from './NewExternalConnectionPage';
import { NewFileExternalConnectionPage } from './NewFileExternalConnectionPage';
import { NewResultExternalConnectionPage } from './NewResultExternalConnectionPage';

interface ConnectionRow {
  node: CatalogTreeNode;
  identity: ExternalConnectionIdentity;
  kind: Extract<ExternalConnectionKind, 'file' | 'directory'>;
}

function connectionRows(snapshot: CatalogTreeSnapshot | null): ConnectionRow[] {
  if (!snapshot) return [];
  return walkCatalogTree(snapshot.roots).flatMap((node) => {
    if (node.sourceClass !== 'ExternalFileConnectionCatalogNode' && node.sourceClass !== 'ExternalConnectionCatalogNode') return [];
    const properties = new Map((node.properties ?? []).map(({ id, value }) => [id, value]));
    const type = properties.get('Type') ?? 'File';
    const path = properties.get('AbsolutePath') ?? '';
    return [{
      node,
      kind: type.toLowerCase() === 'directory' ? 'directory' : 'file',
      identity: {
        name: properties.get('Name') ?? node.name,
        type,
        path,
        description: properties.get('Description') ?? '',
        sourcePath: path,
        searchPattern: properties.get('SearchPattern') ?? '*',
        recurse: properties.get('Recurse')?.toLowerCase() === 'true',
      },
    }];
  });
}

function sameProjectSource(left: WorkbenchContext, right: WorkbenchContext): boolean {
  return left.projectPath === right.projectPath && left.projectSessionID === right.projectSessionID &&
    left.projectXml === right.projectXml && left.projectRevision === right.projectRevision &&
    left.selectedAsset?.path === right.selectedAsset?.path && left.selectedAsset?.kind === right.selectedAsset?.kind &&
    left.selectedAsset?.revision === right.selectedAsset?.revision;
}

function rawError(error: unknown): string {
  const details = errorDiagnostics(error);
  if (details instanceof Error) return `${details.name}: ${details.message}${details.stack ? `\n${details.stack}` : ''}`;
  try { return JSON.stringify(details, null, 2) ?? String(details); }
  catch { return String(details); }
}

export function ExternalConnectionManager({ language, backend, context, snapshot, open, hasUnsavedDrafts = false, onContextCommit, onClose, onCommitted }: {
  language: Language;
  backend: BackendGateway;
  context: WorkbenchContext;
  snapshot: CatalogTreeSnapshot | null;
  open: boolean;
  hasUnsavedDrafts?: boolean;
  onContextCommit?: (source: WorkbenchContext, committed: WorkbenchContext) => boolean;
  onClose: () => void;
  onCommitted: (source: WorkbenchContext, committed: WorkbenchContext, receipt: ExternalConnectionCommitReceipt) => void;
}) {
  const rows = useMemo(() => connectionRows(snapshot), [snapshot]);
  const [page, setPage] = useState<'list' | 'choose' | 'file' | 'result'>('list');
  const [editTarget, setEditTarget] = useState<ConnectionRow | null>(null);
  const [removeTarget, setRemoveTarget] = useState<ConnectionRow | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const [details, setDetails] = useState<string>();
  const [notice, setNotice] = useState<string>();
  const contextRef = useRef(context);
  contextRef.current = context;
  const requestAbort = useRef<AbortController | undefined>(undefined);
  const supported = backend.supports('project.connection.inspect') && backend.supports('project.connection.commit');

  useEffect(() => {
    requestAbort.current?.abort();
    requestAbort.current = undefined;
    setBusy(false);
    setError(undefined);
    setDetails(undefined);
    setNotice(undefined);
    setPage('list');
    setEditTarget(null);
    setRemoveTarget(null);
    if (!open) return;
    return () => requestAbort.current?.abort();
  }, [open, context.projectPath, context.projectSessionID, context.projectRevision, context.projectXml]);

  const beginAdd = (): void => {
    setError(undefined); setDetails(undefined); setNotice(undefined); setEditTarget(null); setRemoveTarget(null); setPage('choose');
  };

  const beginEdit = (row: ConnectionRow): void => {
    setError(undefined); setDetails(undefined); setNotice(undefined); setRemoveTarget(null); setEditTarget(row); setPage('file');
  };

  const returnToList = (): void => {
    setPage('list'); setEditTarget(null); setError(undefined); setDetails(undefined);
  };

  const commit = async (values: ExternalConnectionValues): Promise<void> => {
    if (!supported || hasUnsavedDrafts || !onContextCommit) {
      setError(!supported
        ? text(language, '当前服务未同时提供连接检查和提交操作。', 'The connected service does not provide both connection inspection and commit.')
        : hasUnsavedDrafts
          ? text(language, '存在未保存的模型草稿；请先保存或处理草稿，再修改工程连接。', 'Unsaved model drafts exist. Save or resolve them before changing project connections.')
          : text(language, '当前工程会话未提供回执校验，不能提交连接变更。', 'The active project session does not provide receipt validation, so connection changes cannot be committed.'));
      return;
    }
    const source = contextRef.current;
    const controller = new AbortController();
    requestAbort.current?.abort();
    requestAbort.current = controller;
    setBusy(true); setError(undefined); setDetails(undefined); setNotice(undefined);
    try {
      const result = await inspectAndCommitExternalConnection(backend, source, values, controller.signal,
        () => sameProjectSource(contextRef.current, source));
      if (controller.signal.aborted || !sameProjectSource(contextRef.current, source)) return;
      const accepted = onContextCommit(source, result.committed);
      if (!accepted) {
        setNotice(text(language, '工程连接已提交，但当前会话未接受回执；本地上下文未更新。请重新加载工程目录核对。', 'The project connection was committed, but the active session did not accept the receipt. The local context was not updated; reload the project catalog to verify it.'));
        return;
      }
      onCommitted(source, result.committed, result.receipt);
      setNotice(result.receipt.action === 'remove'
        ? text(language, `已从工程删除连接“${result.receipt.before?.name ?? values.connectionName ?? ''}”。`, `Removed connection “${result.receipt.before?.name ?? values.connectionName ?? ''}” from the project.`)
        : text(language, `已将连接“${result.receipt.after?.name ?? values.name ?? ''}”提交到工程。`, `Committed connection “${result.receipt.after?.name ?? values.name ?? ''}” to the project.`));
      setPage('list'); setEditTarget(null); setRemoveTarget(null);
    } catch (caught) {
      if (!controller.signal.aborted) {
        setError(message(caught, language));
        setDetails(rawError(caught));
      }
    } finally {
      if (requestAbort.current === controller && !controller.signal.aborted) setBusy(false);
    }
  };

  const remove = (row: ConnectionRow): void => {
    void commit({ action: 'remove', kind: row.kind, connectionName: row.identity.name });
  };

  if (!open) return null;
  const modalTitle = page === 'list'
    ? text(language, '工程外部连接', 'Project external connections')
    : page === 'choose'
      ? text(language, '新建外部连接', 'New external connection')
      : page === 'result'
        ? text(language, '添加工程结果连接', 'Add project result connection')
        : text(language, editTarget ? '编辑外部连接' : '添加外部文件连接', editTarget ? 'Edit external connection' : 'Add external file connection');

  return (
    <div className="project-modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget && !busy) onClose(); }}>
      <section className="project-open-modal project-operation-modal project-external-manager" role="dialog" aria-modal="true" aria-labelledby="project-external-manager-title">
        <header className="project-modal-header"><div><div className="project-operation-kicker">ExternalConnectionManager</div><h2 id="project-external-manager-title">{modalTitle}</h2></div><button type="button" className="project-icon-button" aria-label={text(language, '关闭', 'Close')} disabled={busy} onClick={onClose}>×</button></header>
        <div className="project-modal-body">
          {!supported && <p className="project-form-error" role="status"><span>!</span>{text(language, '当前服务未提供 project.connection.inspect 与 project.connection.commit；连接信息仍可查看。', 'The service does not provide project.connection.inspect and project.connection.commit; connection details remain available.')}</p>}
          {hasUnsavedDrafts && <p className="project-form-error" role="status"><span>!</span>{text(language, '存在未保存的模型编辑草稿。请先保存或处理草稿，再修改工程连接。', 'Unsaved model editor drafts exist. Save or resolve them before changing project connections.')}</p>}
          {!onContextCommit && <p className="project-form-error" role="status"><span>!</span>{text(language, '当前工程会话未提供回执接收回调。', 'The active project session has not provided a receipt callback.')}</p>}
          {page === 'list' && <>
            <div className="project-external-list-heading"><p>{text(language, '此列表来自当前工程目录回执；每次修改先检查连接身份与源文件摘要，再由工程服务原子提交。', 'This list comes from the active project catalog receipt. Changes verify connection identity and source digests before atomic server commit.')}</p><button type="button" className="project-button project-button-primary" data-testid="project-external-connection-add" disabled={!supported || hasUnsavedDrafts || !onContextCommit || busy} onClick={beginAdd}>{text(language, '添加连接', 'Add connection')}</button></div>
            {rows.length === 0 ? <div className="project-tree-state"><span>◇</span><p>{snapshot ? text(language, '当前工程没有外部连接。', 'The current project has no external connections.') : text(language, '工程目录尚未加载；请先刷新工程目录。', 'The project catalog has not loaded. Refresh it before managing connections.')}</p></div> : <div className="project-external-connection-list">
              {rows.map((row) => <article className="project-external-connection-row" key={row.node.id}>
                <div className="project-external-connection-identity"><strong>{row.identity.name}</strong><span>{row.identity.type}</span><code title={row.identity.path ?? ''}>{row.identity.path || text(language, '未提供路径', 'Path not provided')}</code>
                  {row.identity.description && <small>{row.identity.description}</small>}
                </div>
                <div className="project-external-row-actions"><button type="button" className="project-button project-button-secondary" data-testid="project-external-connection-edit" disabled={!supported || hasUnsavedDrafts || busy || !row.identity.path} onClick={() => beginEdit(row)}>{text(language, '编辑', 'Edit')}</button><button type="button" className="project-button project-button-danger" data-testid="project-external-connection-remove" disabled={!supported || hasUnsavedDrafts || busy} onClick={() => setRemoveTarget(row)}>{text(language, '删除', 'Remove')}</button></div>
              </article>)}
            </div>}
            {removeTarget && <div className="project-external-remove-confirm" role="alertdialog" aria-label={text(language, '确认删除连接', 'Confirm connection removal')}>
              <p>{text(language, `将从工程中删除连接“${removeTarget.identity.name}”。此操作通过工程事务提交。`, `Remove “${removeTarget.identity.name}” from this project through the project transaction?`)}</p>
              <div className="project-footer-actions"><button type="button" className="project-button project-button-secondary" disabled={busy} onClick={() => setRemoveTarget(null)}>{text(language, '取消', 'Cancel')}</button><button type="button" className="project-button project-button-danger" data-testid="project-external-connection-remove-confirm" disabled={busy || !supported || hasUnsavedDrafts} onClick={() => remove(removeTarget)}>{busy ? text(language, '提交中…', 'Committing…') : text(language, '确认删除', 'Remove connection')}</button></div>
            </div>}
          </>}
          {page === 'choose' && <NewExternalConnectionPage language={language} onChoose={(kind) => setPage(kind === 'result' ? 'result' : 'file')} onCancel={returnToList} />}
          {page === 'file' && <NewFileExternalConnectionPage
            language={language}
            action={editTarget ? 'update' : 'add'}
            kind={editTarget?.kind ?? 'file'}
            connection={editTarget?.identity}
            busy={busy}
            onSubmit={(values) => void commit(values)}
            onBack={() => { if (!busy) { setPage(editTarget ? 'list' : 'choose'); if (editTarget) setEditTarget(null); } }}
          />}
          {page === 'result' && <NewResultExternalConnectionPage
            language={language}
            snapshot={snapshot}
            busy={busy}
            onSubmit={(values) => void commit(values)}
            onBack={() => { if (!busy) setPage('choose'); }}
          />}
          {busy && <div className="project-external-busy" role="status"><span className="project-spinner" />{text(language, '正在校验工程、连接身份与源文件摘要…', 'Checking project, connection identity, and source file digests…')}</div>}
          {error && <div className="project-form-error" role="alert"><span>!</span><div>{error}{details && <details><summary>{text(language, '原始诊断', 'Raw diagnostics')}</summary><pre>{details}</pre></details>}</div></div>}
          {notice && <div className="project-external-notice" role="status">{notice}</div>}
        </div>
        <footer className="project-modal-footer"><small>{context.projectPath ? <><span>{text(language, '工程会话', 'Project session')}</span> <code>{context.projectSessionID.slice(0, 8)}</code> · <code title={context.projectRevision ?? ''}>{context.projectRevision?.slice(0, 12) ?? '—'}</code></> : text(language, '未打开工程', 'No project open')}</small><div className="project-footer-actions">{page === 'list' && <button type="button" className="project-button project-button-secondary" disabled={busy} onClick={onClose}>{text(language, '关闭', 'Close')}</button>}</div></footer>
      </section>
    </div>
  );
}
