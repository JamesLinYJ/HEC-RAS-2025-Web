import { useEffect, useMemo, useRef, useState } from 'react';
import type { BackendGateway, FeaturePageProps, Language, WorkbenchContext } from '../../../shared/workbench';
import { errorDiagnostics, message, text } from '../../../shared/workbench';
import type { CatalogTreeNode } from '../CatalogTreeSnapshot';
import type { GeometryBackupActionIdentity, GeometryBackupCommittedCallback, GeometryBackupCommittedEvent } from './geometryBackupWorkflow';
import './geometryBackupAction.css';
import {
  captureGeometryBackupIdentity,
  createNamedGeometryBackup,
  geometryBackupIdentityIsCurrent,
  geometryBackupOwnerKey,
  geometryBackupTargetIsReady,
  restoreGeometryBackupAsNew,
} from './geometryBackupWorkflow';

export type GeometryBackupActionKind = 'create' | 'restore';

export interface GeometryBackupActionProps {
  language: Language;
  backend: BackendGateway;
  context: WorkbenchContext;
  node: CatalogTreeNode;
  action: GeometryBackupActionKind;
  hasUnsavedDrafts?: boolean;
  onContextCommit?: FeaturePageProps['onContextCommit'];
  onGeometryBackupCommitted?: GeometryBackupCommittedCallback;
}

function diagnosticText(error: unknown): string {
  const details = errorDiagnostics(error);
  if (details instanceof Error) return `${details.name}: ${details.message}${details.stack ? `\n${details.stack}` : ''}`;
  try { return JSON.stringify(details, null, 2) ?? String(details); }
  catch { return String(details); }
}

function initialBackupName(node: CatalogTreeNode): string {
  const day = new Date();
  const date = `${day.getFullYear()}-${String(day.getMonth() + 1).padStart(2, '0')}-${String(day.getDate()).padStart(2, '0')}`;
  return `${node.name} ${date}`;
}

/** Source-compatible backup actions; every write is tied to the captured project and catalog owner. */
export function GeometryBackupAction({
  language,
  backend,
  context,
  node,
  action,
  hasUnsavedDrafts = false,
  onContextCommit,
  onGeometryBackupCommitted,
}: GeometryBackupActionProps) {
  const [name, setName] = useState(() => initialBackupName(node));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const [details, setDetails] = useState<string>();
  const [notice, setNotice] = useState<string>();
  const [receiptEvent, setReceiptEvent] = useState<GeometryBackupCommittedEvent | null>(null);
  const sourceRef = useRef({ context, node, hasUnsavedDrafts });
  sourceRef.current = { context, node, hasUnsavedDrafts };
  const ownerKey = useMemo(() => geometryBackupOwnerKey(context, node), [context, node]);
  useEffect(() => {
    setName(initialBackupName(node));
    setError(undefined);
    setDetails(undefined);
    setNotice(undefined);
    setReceiptEvent(null);
  }, [action, node.id, node.name]);
  const capabilityAvailable = action === 'create'
    ? backend.supports('workspace.file.inspect') && backend.supports('model.load') && backend.supports('geometry.backup.createNamed')
    : backend.supports('workspace.file.inspect') && backend.supports('geometry.backup.restore');
  const targetReady = geometryBackupTargetIsReady(action, context, node);
  const allowed = !busy && !hasUnsavedDrafts && capabilityAvailable && targetReady && !!onContextCommit && !!onGeometryBackupCommitted;

  const run = async (): Promise<void> => {
    if (!allowed) return;
    const source = sourceRef.current.context;
    const sourceNode = sourceRef.current.node;
    const identityKey = geometryBackupOwnerKey(source, sourceNode);
    let identity: GeometryBackupActionIdentity;
    try {
      identity = captureGeometryBackupIdentity(source, sourceNode, sourceRef.current.hasUnsavedDrafts);
    } catch (caught) {
      setError(message(caught, language));
      setDetails(diagnosticText(caught));
      setNotice(undefined);
      return;
    }

    if (identityKey !== ownerKey) return;
    setBusy(true);
    setError(undefined);
    setDetails(undefined);
    setNotice(undefined);
    setReceiptEvent(null);
    try {
      if (!geometryBackupIdentityIsCurrent(identity, sourceRef.current.context, sourceRef.current.node)) {
        throw new Error('工程或目录节点已切换；请重新选择对象后再执行。');
      }
      if (action === 'create') {
        const receipt = await createNamedGeometryBackup(backend, identity, name, () =>
          geometryBackupIdentityIsCurrent(identity, sourceRef.current.context, sourceRef.current.node));
        setReceiptEvent({ action: 'create', receipt });
        await onGeometryBackupCommitted?.(source, { action: 'create', receipt });
        if (!geometryBackupIdentityIsCurrent(identity, sourceRef.current.context, sourceRef.current.node)) return;
        setNotice(text(language, `C# 服务已返回“${name}”的备份回执；工程会话正在核对并刷新目录。`, `The C# service returned the “${name}” backup receipt. The project session is checking it and refreshing the catalog.`));
      } else {
        const receipt = await restoreGeometryBackupAsNew(backend, identity, () =>
          geometryBackupIdentityIsCurrent(identity, sourceRef.current.context, sourceRef.current.node));
        setReceiptEvent({ action: 'restore', receipt });
        await onGeometryBackupCommitted?.(source, { action: 'restore', receipt });
        if (!geometryBackupIdentityIsCurrent(identity, sourceRef.current.context, sourceRef.current.node)) return;
        const cleanup = receipt.cleanupWarning ? ` ${receipt.cleanupWarning}` : '';
        setNotice(text(language,
          `恢复回执已交给工程会话；将按新路径和 SHA-256 读回“${receipt.newName}”后才切换选择。${cleanup}`,
          `The restore receipt was handed to the project session. It will select “${receipt.newName}” only after path and SHA-256 read-back succeeds.${cleanup}`));
      }
    } catch (caught) {
      setError(message(caught, language));
      setDetails(diagnosticText(caught));
      setNotice(text(language,
        '若 C# 写入请求已经发出但未收到可核验回执，请先刷新工程目录检查文件，再重试以免生成重复备份或几何。',
        'If the C# write request was sent but no verifiable receipt arrived, refresh the project catalog and check the files before retrying to avoid duplicate backups or geometries.'));
    } finally {
      setBusy(false);
    }
  };

  const disabledReason = hasUnsavedDrafts
    ? text(language, '存在未保存的模型草稿；请先保存或处理草稿。', 'Unsaved model drafts exist. Save or resolve them first.')
    : !capabilityAvailable
      ? text(language, '当前 C# 服务没有发布所需的备份操作。', 'The current C# service has not advertised the required backup operation.')
        : !targetReady
          ? text(language, '当前目录节点缺少匹配的文件身份或版本快照；请刷新目录并重新选择。', 'The selected catalog node lacks a matching file identity or revision snapshot. Refresh and select it again.')
          : !onContextCommit
            ? text(language, '当前工程会话没有提供回执校验。', 'The active project session cannot validate the operation receipt.')
            : !onGeometryBackupCommitted
              ? text(language, '当前工程目录没有提供回执接收与刷新处理。', 'The active project catalog cannot validate the receipt and refresh its tree.')
              : undefined;

  return (
    <section className="project-detail-properties geometry-backup-action" data-testid={`geometry-backup-${action}`}>
      <h4>{action === 'create' ? text(language, '创建几何备份', 'Create Geometry backup') : text(language, '恢复备份', 'Restore backup')}</h4>
      {action === 'create' ? <label className="geometry-backup-name-field">
        <span>{text(language, '备份名称', 'Backup name')}</span>
        <input
          type="text"
          data-testid="geometry-backup-create-name"
          aria-label={text(language, '备份名称', 'Backup name')}
          value={name}
          disabled={busy || hasUnsavedDrafts}
          onChange={(event) => { setName(event.currentTarget.value); setError(undefined); setDetails(undefined); setReceiptEvent(null); }}
          maxLength={180}
        />
      </label> : <p>{text(language,
        '恢复会创建一份新的几何文件；源几何和备份文件都不会被覆盖。新名称由服务按现有规则生成。',
        'Restore creates a new Geometry file. Neither the source Geometry nor the backup is overwritten; the service generates the new name.')}</p>}
      <button
        type="button"
        className="project-button project-button-secondary"
        data-testid={`geometry-backup-${action}-submit`}
        disabled={!allowed || (action === 'create' && (!name.trim() || name !== name.trim() || /[\\/]/.test(name)))}
        title={!allowed ? disabledReason : undefined}
        onClick={() => { void run(); }}
      >{busy
        ? text(language, '正在校验并提交…', 'Validating and submitting…')
        : action === 'create'
          ? text(language, '创建备份', 'Create backup')
          : text(language, '恢复为新几何', 'Restore as new Geometry')}</button>
      {disabledReason && <p className="geometry-backup-disabled" role="status">{disabledReason}</p>}
      {notice && <p className="geometry-backup-notice" role="status">{notice}</p>}
      {receiptEvent?.action === 'create' && <dl className="geometry-backup-receipt" data-testid="geometry-backup-create-receipt">
        <div><dt>{text(language, '备份文件', 'Backup file')}</dt><dd><code>{receiptEvent.receipt.backupPath}</code></dd></div>
        <div><dt>{text(language, '备份 SHA-256', 'Backup SHA-256')}</dt><dd><code>{receiptEvent.receipt.backupRevision}</code></dd></div>
      </dl>}
      {receiptEvent?.action === 'restore' && <dl className="geometry-backup-receipt" data-testid="geometry-backup-restore-receipt">
        <div><dt>{text(language, '新几何名称', 'New Geometry name')}</dt><dd>{receiptEvent.receipt.newName}</dd></div>
        <div><dt>{text(language, '新几何路径', 'New Geometry path')}</dt><dd><code>{receiptEvent.receipt.newAssetPath}</code></dd></div>
        <div><dt>{text(language, '新几何 SHA-256', 'New Geometry SHA-256')}</dt><dd><code>{receiptEvent.receipt.newAssetRevision}</code></dd></div>
      </dl>}
      {error && <div className="project-form-error" role="alert"><span>!</span><div>{error}{details && <details><summary>{text(language, '原始诊断', 'Raw diagnostics')}</summary><pre>{details}</pre></details>}</div></div>}
    </section>
  );
}
