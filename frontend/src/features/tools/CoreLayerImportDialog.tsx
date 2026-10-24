import { useEffect, useRef, useState } from 'react';
import type { AssetReference, BackendGateway, Language, WorkbenchContext } from '../../shared/workbench';
import { message, text } from '../../shared/workbench';
import { projectXmlSha } from '../map/ProjectTreeListener';
import type { CoreLayerImportInspection, CoreLayerImportReceipt, CoreLayerImportRequest, WorkspaceFileInspection } from './coreImportWorkflow';
import { isSha256, sourceOwnerKey } from './coreImportWorkflow';
import './coreImportWorkflow.css';

interface Props {
  open: boolean;
  language: Language;
  context: WorkbenchContext;
  source: AssetReference | null;
  backend: BackendGateway;
  activeMapLabel?: string | null;
  onClose: () => void;
}

function isAbsoluteServerPath(path: string): boolean {
  return path.startsWith('/') || path.startsWith('\\\\') || /^[a-z]:[\\/]/i.test(path);
}

function bytes(value: number, language: Language): string {
  if (!Number.isFinite(value) || value < 0) return '—';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let amount = value;
  let unit = 0;
  while (amount >= 1024 && unit < units.length - 1) { amount /= 1024; unit += 1; }
  return `${amount.toLocaleString(language === 'zh' ? 'zh-CN' : 'en-US', { maximumFractionDigits: 2 })} ${units[unit]}`;
}

function isFileInspection(value: unknown, sourcePath: string): value is WorkspaceFileInspection {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const capture = value as Partial<WorkspaceFileInspection>;
  return capture.sourcePath === sourcePath && typeof capture.canonicalPath === 'string' &&
    isAbsoluteServerPath(capture.canonicalPath) && isSha256(capture.revision) &&
    typeof capture.length === 'number' && Number.isSafeInteger(capture.length) && capture.length >= 0;
}

export function CoreLayerImportDialog({ open, language, context, source, backend, activeMapLabel, onClose }: Props) {
  const zh = language === 'zh';
  const owner = sourceOwnerKey(context);
  const ownerRef = useRef(owner);
  ownerRef.current = owner;
  const generation = useRef(0);
  const controller = useRef<AbortController | null>(null);
  const [path, setPath] = useState(source?.path ?? '');
  const [capture, setCapture] = useState<WorkspaceFileInspection>();
  const [inspection, setInspection] = useState<CoreLayerImportInspection>();
  const [receipt, setReceipt] = useState<CoreLayerImportReceipt>();
  const [busy, setBusy] = useState<'inspect' | 'copy' | null>(null);
  const [error, setError] = useState<string>();
  const [rawDiagnostic, setRawDiagnostic] = useState<string>();
  const [cancelNote, setCancelNote] = useState<string>();

  const projectReady = Boolean(context.projectPath && context.projectRevision && context.projectXml !== null && context.projectSessionID);
  const canInspect = projectReady && isAbsoluteServerPath(path.trim()) &&
    backend.supports('workspace.file.inspect') && backend.supports('workspace.import.coreLayer.inspect');
  const canCopy = Boolean(inspection && inspection.availableActions.includes('copyFile') &&
    backend.supports('workspace.import.coreLayer.perform') && ownerRef.current === owner);
  const capability = {
    inspect: backend.supports('workspace.import.coreLayer.inspect'),
    perform: backend.supports('workspace.import.coreLayer.perform'),
  };

  useEffect(() => {
    if (!open) return;
    controller.current?.abort();
    generation.current += 1;
    setPath(source?.path ?? '');
    setCapture(undefined);
    setInspection(undefined);
    setReceipt(undefined);
    setBusy(null);
    setError(undefined);
    setRawDiagnostic(undefined);
    setCancelNote(undefined);
  }, [open, owner]);

  useEffect(() => () => {
    generation.current += 1;
    controller.current?.abort();
  }, []);

  if (!open) return null;

  const changePath = (value: string): void => {
    controller.current?.abort();
    generation.current += 1;
    setBusy(null);
    setPath(value);
    setCapture(undefined);
    setInspection(undefined);
    setReceipt(undefined);
    setError(undefined);
    setRawDiagnostic(undefined);
  };

  const begin = (): { id: number; signal: AbortSignal } => {
    controller.current?.abort();
    const requestController = new AbortController();
    controller.current = requestController;
    const id = ++generation.current;
    return { id, signal: requestController.signal };
  };

  const isCurrent = (id: number, signal: AbortSignal): boolean =>
    generation.current === id && ownerRef.current === owner && !signal.aborted;

  const buildRequest = async (sourceCapture: WorkspaceFileInspection): Promise<CoreLayerImportRequest> => {
    if (!context.projectPath || !context.projectRevision || context.projectXml === null || !context.projectSessionID) {
      throw new Error(text(language, '请先打开并保存 HEC-RAS 工程。', 'Open and save a HEC-RAS project first.'));
    }
    if (!isAbsoluteServerPath(path.trim())) {
      throw new Error(text(language, '请填写 C# 服务端可见的绝对文件路径。浏览器本地文件尚未上传。', 'Enter an absolute path visible to the C# server. Browser-local files have not been uploaded.'));
    }
    if (!isFileInspection(sourceCapture, path.trim())) throw new Error(text(language, '来源路径捕获已失效，请重新检查。', 'The source path capture is stale. Inspect it again.'));
    const projectXmlRevision = await projectXmlSha(context.projectXml);
    if (!projectXmlRevision) throw new Error(text(language, '当前工程 XML 快照无法校验。', 'The current project XML snapshot cannot be verified.'));
    const coreLayerContext = source?.locator && typeof source.locator === 'object'
      ? ((source.locator as Record<string, unknown>).coreLayerContext ?? (source.locator as Record<string, unknown>).CoreLayerContext)
      : undefined;
    const exactContext = coreLayerContext && typeof coreLayerContext === 'object' && !Array.isArray(coreLayerContext)
      ? coreLayerContext as Record<string, unknown> : undefined;
    if (exactContext) {
      const ownerProject = exactContext.projectPath ?? exactContext.ProjectPath;
      const ownerSession = exactContext.projectSessionID ?? exactContext.ProjectSessionID;
      const token = exactContext.identityToken ?? exactContext.IdentityToken;
      const capturedProjectRevision = exactContext.projectRevision ?? exactContext.ProjectRevision;
      if (ownerProject !== context.projectPath || ownerSession !== context.projectSessionID || typeof token !== 'string' || !token ||
          (typeof capturedProjectRevision === 'string' && capturedProjectRevision.toUpperCase() !== context.projectRevision?.toUpperCase())) {
        throw new Error(text(language, '选中数据携带的核心图层上下文与当前工程会话不匹配。', 'The selected asset’s core-layer context does not match the active project session.'));
      }
    }
    return {
      projectPath: context.projectPath,
      projectSessionID: context.projectSessionID,
      projectRevision: context.projectRevision,
      projectXml: context.projectXml,
      projectXmlRevision,
      sourcePath: path.trim(),
      expectedSourceCanonicalPath: sourceCapture.canonicalPath,
      sourceRevision: sourceCapture.revision.trim().toUpperCase(),
      ...(exactContext ? { coreLayerContext: exactContext } : {}),
    };
  };

  const inspect = async (): Promise<void> => {
    const request = begin();
    setBusy('inspect');
    setError(undefined);
    setRawDiagnostic(undefined);
    setCancelNote(undefined);
    setInspection(undefined);
    setReceipt(undefined);
    try {
      if (!backend.supports('workspace.import.coreLayer.inspect')) {
        throw new Error(text(language, 'C# 服务未发布核心图层检查接口。', 'The C# server has not advertised core-layer inspection.'));
      }
      if (!backend.supports('workspace.file.inspect')) throw new Error(text(language, 'C# 服务未发布 workspace.file.inspect，无法自动捕获来源 SHA-256。', 'The C# service has not advertised workspace.file.inspect; source SHA-256 cannot be captured automatically.'));
      const sourcePath = path.trim();
      const sourceCapture = await backend.request<WorkspaceFileInspection>('workspace.file.inspect', { sourcePath }, { signal: request.signal });
      if (!isFileInspection(sourceCapture, sourcePath)) throw new Error(text(language, '服务端路径检查回执缺少匹配的原路径、规范路径、SHA-256 或文件长度。', 'The server path-inspection receipt is missing the matching original path, canonical path, SHA-256, or file length.'));
      if (!isCurrent(request.id, request.signal)) return;
      setCapture(sourceCapture);
      const payload = await buildRequest(sourceCapture);
      const result = await backend.request<CoreLayerImportInspection>('workspace.import.coreLayer.inspect', payload, { signal: request.signal });
      if (!isCurrent(request.id, request.signal)) return;
      if (!result || result.projectPath !== context.projectPath || result.projectSessionID !== context.projectSessionID ||
          result.projectRevision.toUpperCase() !== context.projectRevision?.toUpperCase() ||
          result.projectXmlRevision.toUpperCase() !== payload.projectXmlRevision.toUpperCase() ||
          typeof result.sourcePath !== 'string' || !isAbsoluteServerPath(result.sourcePath) || result.canonicalSourcePath !== payload.expectedSourceCanonicalPath || result.sourceRevision.toUpperCase() !== payload.sourceRevision.toUpperCase() ||
          result.fileSize !== sourceCapture.length ||
          !Array.isArray(result.availableActions) || !result.availableActions.includes('copyFile') ||
          !['geometry', 'boundary conditions'].includes(result.kind.toLowerCase())) {
        throw new Error(text(language, '核心图层检查回执与当前来源或工程会话不匹配。', 'The inspection receipt does not match the current source or project session.'));
      }
      setInspection(result);
    } catch (caught) {
      if (request.signal.aborted && generation.current === request.id) {
        setCancelNote(text(language, '检查已取消；服务端没有返回可确认的检查回执。', 'Inspection was cancelled without a confirmed server receipt.'));
      } else if (isCurrent(request.id, request.signal)) {
        setError(message(caught, language));
        setRawDiagnostic(caught instanceof Error ? caught.message : String(caught));
      }
    } finally {
      if (generation.current === request.id) setBusy(null);
    }
  };

  const copyToProject = async (): Promise<void> => {
    const request = begin();
    setBusy('copy');
    setError(undefined);
    setRawDiagnostic(undefined);
    setCancelNote(undefined);
    setReceipt(undefined);
    try {
      if (!inspection || !backend.supports('workspace.import.coreLayer.perform')) {
        throw new Error(text(language, '请先检查来源，并确认 C# 服务发布了导入接口。', 'Inspect the source first and confirm the C# service advertises import.'));
      }
      const sourceCapture = capture;
      if (!sourceCapture || !isFileInspection(sourceCapture, path.trim())) throw new Error(text(language, '来源路径捕获已失效，请先重新检查。', 'The source path capture is stale. Inspect it again first.'));
      const payload = await buildRequest(sourceCapture);
      if (inspection.projectPath !== payload.projectPath || inspection.projectSessionID !== payload.projectSessionID ||
          inspection.projectRevision.toUpperCase() !== payload.projectRevision.toUpperCase() ||
          inspection.projectXmlRevision.toUpperCase() !== payload.projectXmlRevision.toUpperCase() ||
          inspection.canonicalSourcePath !== payload.expectedSourceCanonicalPath || inspection.sourceRevision.toUpperCase() !== payload.sourceRevision.toUpperCase()) {
        throw new Error(text(language, '检查后来源或工程上下文已变化，请重新检查。', 'The source or project context changed after inspection. Inspect it again.'));
      }
      const result = await backend.request<CoreLayerImportReceipt>('workspace.import.coreLayer.perform', {
        ...payload,
        action: 'copyFile',
      }, { signal: request.signal });
      if (!isCurrent(request.id, request.signal)) return;
      if (!result || result.action !== 'copyFile' || result.projectPath !== payload.projectPath ||
          result.projectSessionID !== payload.projectSessionID ||
          result.projectRevision.toUpperCase() !== payload.projectRevision.toUpperCase() ||
          result.projectXmlRevision.toUpperCase() !== payload.projectXmlRevision.toUpperCase() ||
          typeof result.sourcePath !== 'string' || !isAbsoluteServerPath(result.sourcePath) || result.canonicalSourcePath !== payload.expectedSourceCanonicalPath || result.sourceRevision.toUpperCase() !== payload.sourceRevision.toUpperCase() ||
          !result.importedAssetPath || !isSha256(result.importedAssetRevision)) {
        throw new Error(text(language, '服务端导入回执缺少匹配的目标路径或 SHA-256。', 'The import receipt is missing a matching target path or SHA-256 revision.'));
      }
      setReceipt(result);
      setInspection(undefined);
    } catch (caught) {
      if (request.signal.aborted && generation.current === request.id) {
        setCancelNote(text(language, '已请求取消。服务端未返回最终回执；重新检查来源后再决定是否重试。', 'Cancellation was requested, but the server did not return a final receipt. Reinspect before retrying.'));
      } else if (isCurrent(request.id, request.signal)) {
        setError(message(caught, language));
        setRawDiagnostic(caught instanceof Error ? caught.message : String(caught));
      }
    } finally {
      if (generation.current === request.id) setBusy(null);
    }
  };

  const requestCancel = (): void => {
    controller.current?.abort();
    generation.current += 1;
    setBusy(null);
    setCancelNote(text(language, '已请求取消。若服务端已开始复制，结果仍需通过工程目录和服务端回执确认。', 'Cancellation was requested. If the server had started copying, confirm the result from the project directory and server receipt.'));
  };

  const close = (): void => {
    if (busy) requestCancel();
    else onClose();
  };

  return (
    <div className="ci-backdrop" role="presentation" onMouseDown={event => { if (event.target === event.currentTarget && !busy) onClose(); }}>
      <section className="ci-dialog" role="dialog" aria-modal="true" aria-labelledby="ci-title">
        <header className="ci-dialog-header">
          <div><span className="ci-kicker">{zh ? '核心图层 · CoreLayerImportDialog' : 'CORE LAYER · CoreLayerImportDialog'}</span><h2 id="ci-title">{text(language, '检查并复制核心图层', 'Inspect and copy a core layer')}</h2></div>
          <button type="button" className="ci-icon-button" aria-label={text(language, '关闭', 'Close')} disabled={busy !== null} onClick={onClose}>×</button>
        </header>

        <p className="ci-explanation">{text(language,
          '该对话框保留原 CoreLayerImportDialog 的取消、复制和引用决策。当前页面接通 C# 的检查与“复制文件”；引用操作需要地图树事务接收会话引用后才能确认。',
          'This dialog retains the source control’s cancel, copy, and reference choices. C# inspection and Copy File are connected. A reference requires a map-tree transaction to accept its session capture before it can be confirmed.')}</p>

        <div className="ci-context-grid">
          <div><span>{text(language, '工程', 'Project')}</span><strong>{context.projectPath ?? text(language, '未打开', 'Not open')}</strong><small>{text(language, '工程修订', 'Project revision')}: {context.projectRevision ?? '—'}</small></div>
          <div><span>{text(language, '地图目录', 'Map catalog')}</span><strong>{activeMapLabel || text(language, '未载入地图上下文', 'No map context loaded')}</strong><small>{text(language, '会话', 'Session')}: {context.projectSessionID || '—'}</small></div>
        </div>

        <label className="ci-field"><span>{text(language, 'C# 服务端可见的核心图层绝对路径', 'Absolute core-layer path visible to the C# server')}</span><input spellCheck={false} disabled={busy !== null} value={path} onChange={event => changePath(event.target.value)} placeholder="/project/Geometry/Geometry.hdf" /></label>
        <p className="ci-help">{text(language, 'Web 界面当前不会上传本机文件。点击“检查来源”时会由 C# 服务读取原路径、规范路径和 SHA-256；路径仍按原样发送，不会自动替换为规范路径。', 'The Web UI does not upload local files in this workflow. Clicking Inspect asks C# to capture the original path, canonical path, and SHA-256; requests retain the original path and do not silently replace it.')}</p>
        {capture && <div className="ci-result"><strong>{text(language, '已捕获来源文件身份', 'Source file identity captured')}</strong><span>{capture.sourcePath}</span><code>{capture.canonicalPath}</code><code>{capture.revision}</code><small>{bytes(capture.length, language)}</small></div>}

        <div className="ci-actions">
          <button type="button" className="ci-button ci-primary" disabled={!canInspect || busy !== null} onClick={() => void inspect()}>{busy === 'inspect' ? text(language, '检查中…', 'Inspecting…') : text(language, '检查来源', 'Inspect source')}</button>
          <button type="button" className="ci-button ci-primary" disabled={!canCopy || busy !== null} onClick={() => void copyToProject()}>{busy === 'copy' ? text(language, '复制中…', 'Copying…') : text(language, '复制到工程', 'Copy to project')}</button>
          <button type="button" className="ci-button" disabled title={text(language, '需要将 capture 传入精确地图操作；本页还没有该事务所有者。', 'The capture must be passed to the exact map action; this page has no owner for that transaction.')}>{text(language, '添加会话引用（未接通）', 'Add session reference (not connected)')}</button>
          {busy !== null && <button type="button" className="ci-button ci-danger" onClick={requestCancel}>{text(language, '请求取消', 'Request cancellation')}</button>}
          <button type="button" className="ci-button" disabled={busy !== null} onClick={close}>{text(language, '关闭', 'Close')}</button>
        </div>
        {(!capability.inspect || !capability.perform) && <div className="ci-warning" role="status">{text(language, '当前 C# 服务尚未同时发布核心图层检查和导入操作。', 'The C# server has not advertised both core-layer operations.')}</div>}
        {!projectReady && <div className="ci-warning" role="status">{text(language, '需要已保存工程、工程 SHA 和当前会话，才能检查核心图层。', 'A saved project, project SHA, and active session are required.')}</div>}
        {!backend.supports('workspace.file.inspect') && <div className="ci-warning" role="status">{text(language, '当前 C# 服务未发布 workspace.file.inspect；不能自动获取来源 SHA-256。', 'The C# service has not advertised workspace.file.inspect; the source SHA-256 cannot be captured automatically.')}</div>}
        {busy === null && !isAbsoluteServerPath(path.trim()) && <div className="ci-warning" role="status">{text(language, '检查需要 C# 服务端可见的绝对文件路径。', 'Inspection needs an absolute file path visible to the C# server.')}</div>}
        {error && <div className="ci-error" role="alert"><strong>{error}</strong>{rawDiagnostic && <details><summary>{text(language, '原始诊断', 'Raw diagnostic')}</summary><pre>{rawDiagnostic}</pre></details>}</div>}
        {cancelNote && <div className="ci-warning" role="status">{cancelNote}</div>}

        {inspection && <section className="ci-result" aria-live="polite">
          <div className="ci-result-heading"><span className="ci-state-dot" /><strong>{text(language, '服务端检查已确认', 'Server inspection confirmed')}</strong><span className="ci-chip">{inspection.kind}</span></div>
          <dl className="ci-details"><div><dt>{text(language, '图层名称', 'Layer name')}</dt><dd>{inspection.name}</dd></div><div><dt>{text(language, '说明', 'Description')}</dt><dd>{inspection.description || '—'}</dd></div><div><dt>{text(language, '规范路径', 'Canonical path')}</dt><dd>{inspection.canonicalSourcePath}</dd></div><div><dt>{text(language, '文件大小', 'File size')}</dt><dd>{bytes(inspection.fileSize, language)}</dd></div><div><dt>{text(language, '来源 SHA-256', 'Source SHA-256')}</dt><dd><code>{inspection.sourceRevision}</code></dd></div><div><dt>{text(language, '会话范围', 'Session scope')}</dt><dd>{inspection.referenceIsSessionOnly ? text(language, '仅当前会话', 'Current session only') : text(language, '工程', 'Project')}</dd></div></dl>
          <p className="ci-help">{text(language, '“复制到工程”会由 C# 服务在 Geometry 或 Boundary Condition 目录创建一个不覆盖已有文件的副本，并返回目标路径及修订。', 'Copy to project asks the C# service to create a non-overwriting copy in Geometry or Boundary Condition and return its path and revision.')}</p>
        </section>}

        {receipt && <section className="ci-success" aria-live="polite">
          <strong>{text(language, 'C# 已确认复制回执', 'C# copy receipt confirmed')}</strong>
          <dl className="ci-details"><div><dt>{text(language, '图层名称', 'Layer name')}</dt><dd>{receipt.name}</dd></div><div><dt>{text(language, '目标路径', 'Target path')}</dt><dd>{receipt.importedAssetPath}</dd></div><div><dt>{text(language, '目标 SHA-256', 'Target SHA-256')}</dt><dd><code>{receipt.importedAssetRevision}</code></dd></div></dl>
        </section>}
      </section>
    </div>
  );
}
