import { useEffect, useRef, useState } from 'react';
import type { BackendGateway, FeaturePageProps, WorkbenchContext } from '../../shared/workbench';
import { errorDiagnostics, message } from '../../shared/workbench';
import { isProjectTreeSHA256, projectTreeXmlSHA } from './ProjectTreeModelActions';

interface ProjectMigrationInspection {
  canExecute: boolean;
  validationMessage?: string | null;
  sourcePath: string;
  projectName?: string | null;
  destinationPath?: string | null;
  revision?: string | null;
}

interface ProjectMigrationReceipt {
  sourcePath: string;
  destinationPath: string;
  projectPath: string;
  projectName: string;
  messages?: string[] | null;
}

interface ProjectOpenReceipt {
  success?: boolean;
  error?: string | null;
  projectPath?: string;
  projectXml?: string | null;
  revision?: string;
}

interface VerifiedMigration {
  projectPath: string;
  projectName: string;
  projectXml: string;
  revision: string;
  projectXmlSHA: string;
  messages: string[];
}

interface Props {
  language: FeaturePageProps['language'];
  backend: BackendGateway;
  context: WorkbenchContext;
  open: boolean;
  blockedReason?: string;
  hasUnsavedDrafts?: boolean;
  onClose: () => void;
  onBusyChange?: (busy: boolean) => void;
  onContextCommit?: FeaturePageProps['onContextCommit'];
  onOpenProjectPath: (path: string) => Promise<boolean>;
}

function errorDetail(error: unknown): string {
  const details = errorDiagnostics(error);
  if (details instanceof Error) return `${details.name}: ${details.message}${details.stack ? `\n${details.stack}` : ''}`;
  try { return JSON.stringify(details, null, 2) ?? String(details); }
  catch { return String(details); }
}

function samePath(left: string, right: string): boolean {
  const normalize = (value: string): string => value.replace(/\\/g, '/').replace(/\/+$/, '');
  const a = normalize(left);
  const b = normalize(right);
  const windows = (value: string): boolean => /^[A-Za-z]:\//.test(value) || value.startsWith('//');
  return windows(a) && windows(b) ? a.toLowerCase() === b.toLowerCase() : a === b;
}

function sameProjectSource(left: WorkbenchContext, right: WorkbenchContext): boolean {
  return left.projectSessionID === right.projectSessionID && left.projectPath === right.projectPath &&
    left.projectXml === right.projectXml && left.projectRevision === right.projectRevision &&
    left.selectedAsset?.path === right.selectedAsset?.path && left.selectedAsset?.kind === right.selectedAsset?.kind &&
    left.selectedAsset?.revision === right.selectedAsset?.revision;
}

function isAbsolutePath(value: string): boolean {
  return value.startsWith('/') || /^\\\\[^\\]+\\[^\\]+/.test(value) || /^[A-Za-z]:[\\/]/.test(value);
}

function sourceParentPath(path: string): string {
  const normalized = path.trim().replace(/\\/g, '/').replace(/\/+$/, '');
  const separator = normalized.lastIndexOf('/');
  if (separator === 0) return '/';
  if (separator === 2 && /^[A-Za-z]:\//.test(normalized)) return normalized.slice(0, 3);
  return separator > 0 ? normalized.slice(0, separator) : '';
}

function progressDescription(value: unknown): string | undefined {
  if (!value || typeof value !== 'object') return undefined;
  const item = value as { message?: unknown; taskName?: unknown; fraction?: unknown };
  const task = typeof item.taskName === 'string' ? item.taskName : '';
  const detail = typeof item.message === 'string' ? item.message : '';
  const fraction = typeof item.fraction === 'number' && Number.isFinite(item.fraction)
    ? `${Math.round(Math.max(0, Math.min(1, item.fraction)) * 100)}%` : '';
  const line = [task, fraction, detail].filter(Boolean).join(' · ');
  return line || undefined;
}

export function MigrateProjectControl({
  language,
  backend,
  context,
  open,
  blockedReason,
  hasUnsavedDrafts = false,
  onClose,
  onBusyChange,
  onContextCommit,
  onOpenProjectPath,
}: Props) {
  const [sourcePath, setSourcePath] = useState('');
  const [destinationParentPath, setDestinationParentPath] = useState('');
  const [inspection, setInspection] = useState<ProjectMigrationInspection | null>(null);
  const [busy, setBusy] = useState<'inspect' | 'execute' | null>(null);
  const [progress, setProgress] = useState<string>();
  const [error, setError] = useState<string>();
  const [details, setDetails] = useState<string>();
  const [verified, setVerified] = useState<VerifiedMigration | null>(null);
  const [opening, setOpening] = useState(false);
  const [notice, setNotice] = useState<string>();
  const generation = useRef(0);
  const abort = useRef<AbortController | undefined>(undefined);
  const contextRef = useRef(context);
  contextRef.current = context;
  const zh = language === 'zh';
  const canInspect = backend.supports('projectMigration.inspect');
  const canExecute = backend.supports('projectMigration.execute');
  const canReadBack = backend.supports('project.open');
  const hasValidInspect = Boolean(inspection?.canExecute && inspection.destinationPath && isProjectTreeSHA256(inspection.revision));

  useEffect(() => {
    if (open) return;
    generation.current += 1;
    abort.current?.abort();
    abort.current = undefined;
  }, [open]);

  useEffect(() => {
    onBusyChange?.(busy !== null || opening);
  }, [busy, opening, onBusyChange]);

  const resetInspection = (): void => {
    setInspection(null);
    setVerified(null);
    setProgress(undefined);
    setNotice(undefined);
    setError(undefined);
    setDetails(undefined);
  };

  const inspect = async (): Promise<void> => {
    const source = sourcePath.trim();
    const parent = destinationParentPath.trim();
    if (!canInspect) {
      setError(zh ? '当前 C# 服务未发布 projectMigration.inspect。' : 'The connected C# service has not advertised projectMigration.inspect.');
      return;
    }
    if (!isAbsolutePath(source) || !source.toLowerCase().endsWith('.prj')) {
      setError(zh ? '请输入 C# 服务所在机器上的绝对 .prj 文件路径。' : 'Enter an absolute .prj file path on the machine running the C# service.');
      return;
    }
    if (!isAbsolutePath(parent)) {
      setError(zh ? '请输入 C# 服务所在机器上的绝对输出父目录。' : 'Enter an absolute output parent directory on the machine running the C# service.');
      return;
    }
    const requestGeneration = ++generation.current;
    abort.current?.abort();
    const controller = new AbortController();
    abort.current = controller;
    setBusy('inspect');
    setError(undefined);
    setDetails(undefined);
    setNotice(undefined);
    setVerified(null);
    setProgress(undefined);
    try {
      const result = await backend.request<ProjectMigrationInspection>('projectMigration.inspect', {
        sourcePath: source,
        destinationParentPath: parent,
      }, { signal: controller.signal });
      if (requestGeneration !== generation.current || controller.signal.aborted) return;
      if (!sameProjectSource(contextRef.current, context)) return;
      if (!result || typeof result.canExecute !== 'boolean' || typeof result.sourcePath !== 'string') {
        throw new Error(zh ? '迁移检查返回了不完整的回执。' : 'The migration inspection returned an incomplete receipt.');
      }
      if (result.canExecute) {
        if (!samePath(result.sourcePath, source) || !result.destinationPath || !isAbsolutePath(result.destinationPath) ||
            !isProjectTreeSHA256(result.revision) || !result.projectName) {
          throw new Error(zh ? '迁移检查回执缺少匹配的源路径、输出路径、项目名或源文件 SHA-256。' : 'The migration inspection did not return a matching source path, output path, project name, or source SHA-256.');
        }
      }
      setInspection(result);
      if (!result.canExecute) setNotice(result.validationMessage || (zh ? '迁移条件尚未满足。' : 'The migration requirements are not satisfied.'));
    } catch (requestError) {
      if (requestGeneration !== generation.current || controller.signal.aborted) return;
      setError(`${message(requestError, language)}${zh ? ' 展开查看原始诊断。' : ' Expand to view raw diagnostics.'}`);
      setDetails(errorDetail(requestError));
    } finally {
      if (requestGeneration === generation.current && !controller.signal.aborted) setBusy(null);
    }
  };

  const execute = async (): Promise<void> => {
    if (!hasValidInspect || !inspection?.destinationPath || !inspection.revision || !inspection.projectName) return;
    if (!canExecute || !canReadBack || !onContextCommit) {
      setError(zh
        ? '迁移需要当前服务同时提供 execute、project.open 读回及父级工程会话提交校验。'
        : 'Migration requires execute, project.open read-back, and the parent project-session commit guard.');
      return;
    }
    if (blockedReason) {
      setError(blockedReason);
      return;
    }
    const sourceContext = contextRef.current;
    const sourceXmlSHA = sourceContext.projectXml === null ? null : await projectTreeXmlSHA(sourceContext.projectXml);
    if (!sameProjectSource(contextRef.current, sourceContext)) return;
    const requestGeneration = ++generation.current;
    abort.current?.abort();
    const controller = new AbortController();
    abort.current = controller;
    setBusy('execute');
    setError(undefined);
    setDetails(undefined);
    setNotice(undefined);
    setVerified(null);
    setProgress(undefined);
    try {
      const receipt = await backend.request<ProjectMigrationReceipt>('projectMigration.execute', {
        sourcePath: inspection.sourcePath,
        destinationPath: inspection.destinationPath,
        revision: inspection.revision,
      }, {
        signal: controller.signal,
        onProgress: (item) => {
          if (requestGeneration !== generation.current || controller.signal.aborted) return;
          setProgress(progressDescription(item));
        },
      });
      if (requestGeneration !== generation.current || controller.signal.aborted) return;
      if (!sameProjectSource(contextRef.current, sourceContext)) {
        setNotice(zh
          ? `迁移已执行，但当前工程会话已变化。输出保留在 ${receipt?.projectPath ?? inspection.destinationPath}；不会自动打开。`
          : `Migration completed, but the current project session changed. Output remains at ${receipt?.projectPath ?? inspection.destinationPath}; it will not be opened automatically.`);
        return;
      }
      if (!receipt || typeof receipt.sourcePath !== 'string' || typeof receipt.destinationPath !== 'string' ||
          typeof receipt.projectPath !== 'string' || typeof receipt.projectName !== 'string' ||
          !samePath(receipt.sourcePath, inspection.sourcePath) || !samePath(receipt.destinationPath, inspection.destinationPath)) {
        throw new Error(zh ? '迁移执行回执与检查时的源文件或输出目录不匹配。' : 'The migration receipt does not match the inspected source file or destination.');
      }
      const outputParent = sourceParentPath(receipt.projectPath);
      if (!samePath(outputParent, receipt.destinationPath) || !receipt.projectPath.toLowerCase().endsWith('.ras')) {
        throw new Error(zh ? '迁移回执中的工程文件不在指定输出目录，或扩展名不是 .ras。' : 'The migrated project file is outside the destination folder or does not use the .ras extension.');
      }
      const opened = await backend.request<ProjectOpenReceipt>('project.open', { projectPath: receipt.projectPath }, { signal: controller.signal });
      if (requestGeneration !== generation.current || controller.signal.aborted) return;
      if (!sameProjectSource(contextRef.current, sourceContext)) {
        setNotice(zh
          ? `迁移后的工程已由 C# 打开检查，但当前会话发生变化；请从输出路径手动打开：${receipt.projectPath}`
          : `The migrated project was read back by C#, but the current session changed. Open it manually from: ${receipt.projectPath}`);
        return;
      }
      if (opened?.success !== true || typeof opened.projectPath !== 'string' || !samePath(opened.projectPath, receipt.projectPath) ||
          typeof opened.projectXml !== 'string' || !isProjectTreeSHA256(opened.revision)) {
        throw new Error(opened?.error || (zh ? 'project.open 未返回可核验的迁移工程 XML 与 SHA-256。' : 'project.open did not return verifiable migrated project XML and SHA-256.'));
      }
      const outputXmlSHA = await projectTreeXmlSHA(opened.projectXml);
      if (requestGeneration !== generation.current || controller.signal.aborted) return;
      if (sourceXmlSHA !== null && sourceContext.projectXml !== null &&
          await projectTreeXmlSHA(sourceContext.projectXml) !== sourceXmlSHA) {
        throw new Error(zh ? '迁移期间捕获的当前工程 XML SHA-256 不稳定。' : 'The captured current-project XML SHA-256 changed during migration.');
      }
      const accepted = onContextCommit(sourceContext, sourceContext);
      if (!accepted) {
        setNotice(zh
          ? `迁移及输出读回已完成，但父级工程会话拒绝了旧请求校验。输出保留在 ${receipt.projectPath}，不会打开。`
          : `Migration and output read-back completed, but the parent rejected the captured session. Output remains at ${receipt.projectPath} and will not be opened.`);
        return;
      }
      setVerified({
        projectPath: opened.projectPath,
        projectName: receipt.projectName,
        projectXml: opened.projectXml,
        revision: opened.revision,
        projectXmlSHA: outputXmlSHA,
        messages: Array.isArray(receipt.messages) ? receipt.messages.filter((item): item is string => typeof item === 'string') : [],
      });
      setNotice(zh ? '迁移完成。C# 已重新打开输出工程并返回 XML 与源文件 SHA-256。' : 'Migration completed. C# reopened the output project and returned its XML and file SHA-256.');
    } catch (requestError) {
      if (requestGeneration !== generation.current || controller.signal.aborted) return;
      setError(`${message(requestError, language)}${zh ? ' 展开查看原始诊断；若目录已生成，请核对部分输出。' : ' Expand raw diagnostics; if the folder was created, inspect partial output.'}`);
      setDetails(errorDetail(requestError));
    } finally {
      if (requestGeneration === generation.current && !controller.signal.aborted) setBusy(null);
    }
  };

  if (!open) return null;
  return (
    <div className="project-modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget && !busy && !opening) onClose(); }}>
      <section className="project-new-modal project-operation-modal" role="dialog" aria-modal="true" aria-labelledby="project-migrate-title">
        <header className="project-modal-header">
          <div><div className="project-kicker">{zh ? '旧版工程转换' : 'LEGACY PROJECT CONVERSION'}</div><h2 id="project-migrate-title">{zh ? '迁移 HEC-RAS v6 工程' : 'Migrate HEC-RAS v6 project'}</h2></div>
          <button className="project-icon-button" type="button" disabled={Boolean(busy) || opening} aria-label={zh ? '关闭' : 'Close'} onClick={onClose}>×</button>
        </header>
        <div className="project-modal-body">
          <p className="project-migration-description">{zh ? '选择 C# 服务所在机器上的旧版 .prj 文件和输出父目录。源文件只读；转换会写入独立的新目录。' : 'Choose a legacy .prj file and output parent on the machine running the C# service. The source is read-only; conversion writes to a separate new folder.'}</p>
          <label className="project-form-field"><span>{zh ? '旧版工程文件路径' : 'Legacy project file path'}</span>
            <input value={sourcePath} disabled={busy === 'execute'} placeholder={zh ? '服务器绝对路径，例如 /data/model.prj' : 'Absolute server path, e.g. /data/model.prj'} onChange={(event) => { setSourcePath(event.target.value); setDestinationParentPath(sourceParentPath(event.target.value)); resetInspection(); }} />
          </label>
          <label className="project-form-field"><span>{zh ? '输出父目录' : 'Output parent folder'}</span>
            <input value={destinationParentPath} disabled={busy === 'execute'} placeholder={zh ? 'C# 服务端上的绝对目录路径' : 'Absolute folder path on the C# server'} onChange={(event) => { setDestinationParentPath(event.target.value); resetInspection(); }} />
          </label>
          {!canInspect && <div className="project-form-error"><span>!</span>{zh ? '当前 C# 服务未发布 projectMigration.inspect。' : 'The C# service has not advertised projectMigration.inspect.'}</div>}
          {inspection && <div className={`project-migration-inspection ${inspection.canExecute ? 'is-valid' : 'is-invalid'}`}>
            <strong>{inspection.projectName || (zh ? '未识别工程名' : 'Project name not recognized')}</strong>
            <span>{zh ? '源路径' : 'Source'}<code>{inspection.sourcePath}</code></span>
            {inspection.destinationPath && <span>{zh ? '建议输出目录' : 'Suggested destination'}<code>{inspection.destinationPath}</code></span>}
            {inspection.revision && <span>{zh ? '源 .prj SHA-256' : 'Source .prj SHA-256'}<code>{inspection.revision}</code></span>}
            {inspection.validationMessage && <p>{inspection.validationMessage}</p>}
          </div>}
          {progress && <div className="project-migration-progress" role="status">{progress}</div>}
          {verified && <div className="project-migration-receipt" role="status">
            <strong>{verified.projectName}</strong>
            <span>{zh ? '输出工程' : 'Output project'}<code>{verified.projectPath}</code></span>
            <span>{zh ? '工程文件 SHA-256' : 'Project file SHA-256'}<code>{verified.revision}</code></span>
            <span>{zh ? '返回 XML SHA-256' : 'Returned XML SHA-256'}<code>{verified.projectXmlSHA}</code></span>
            {verified.messages.length > 0 && <details><summary>{zh ? `C# 转换消息（${verified.messages.length}）` : `C# migration messages (${verified.messages.length})`}</summary><pre>{verified.messages.join('\n')}</pre></details>}
          </div>}
          {blockedReason && <div className="project-form-error"><span>!</span>{blockedReason}</div>}
          {!canReadBack && <div className="project-form-error"><span>!</span>{zh ? '缺少 project.open 输出读回，迁移不能安全确认完成。' : 'project.open read-back is unavailable, so migration cannot be safely confirmed.'}</div>}
          {notice && <div className="project-inline-warning" role="status">{notice}</div>}
          {error && <div className="project-form-error" role="alert"><span>!</span><div>{error}{details && <details><summary>{zh ? '原始诊断' : 'Raw diagnostics'}</summary><pre>{details}</pre></details>}</div></div>}
        </div>
        <footer className="project-modal-footer">
          <span className="project-footer-hint">{zh ? '迁移不会覆盖旧版源文件；失败时保留转换器生成的部分输出供诊断。' : 'Migration does not overwrite the legacy source. Partial converter output is retained for diagnosis if conversion fails.'}</span>
          <div className="project-footer-actions">
            <button type="button" className="project-button project-button-secondary" disabled={Boolean(busy) || opening} onClick={() => void inspect()}>{busy === 'inspect' ? (zh ? '正在检查…' : 'Inspecting…') : (zh ? '检查源文件' : 'Inspect source')}</button>
            {verified && <button type="button" className="project-button project-button-primary" disabled={Boolean(busy) || opening || Boolean(blockedReason) || hasUnsavedDrafts} onClick={async () => {
              setOpening(true);
              try {
                const opened = await onOpenProjectPath(verified.projectPath);
                if (opened) onClose();
              } catch (openError) {
                setError(`${message(openError, language)}${zh ? ' 展开查看原始诊断。' : ' Expand raw diagnostics.'}`);
                setDetails(errorDetail(openError));
              } finally { setOpening(false); }
            }}>{opening ? (zh ? '正在打开…' : 'Opening…') : (zh ? '打开迁移工程' : 'Open migrated project')}</button>}
            {!verified && <button type="button" className="project-button project-button-primary" disabled={!hasValidInspect || !canExecute || !canReadBack || Boolean(blockedReason) || busy !== null || !onContextCommit} onClick={() => void execute()}>{busy === 'execute' ? (zh ? '正在迁移…' : 'Migrating…') : (zh ? '执行迁移' : 'Run migration')}</button>}
          </div>
        </footer>
      </section>
    </div>
  );
}
