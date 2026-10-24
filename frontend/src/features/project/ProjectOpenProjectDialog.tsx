import { useEffect, useRef, useState } from 'react';
import type { BackendGateway, Language, WorkbenchContext, WorkspaceUploadResult } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import { isAbsoluteServerPath } from './ProjectTypes';
import { QuickProjectImportControl } from './QuickProjectImportControl';

interface ProjectOpenProjectDialogProps {
  language: Language;
  backend: BackendGateway;
  context: WorkbenchContext;
  open: boolean;
  busy: boolean;
  canOpenServerPath: boolean;
  canUploadWorkspace: boolean;
  error?: string;
  workspace?: WorkspaceUploadResult | null;
  onClose: () => void;
  onOpenPath: (path: string) => Promise<boolean> | boolean;
  onOpenUploadedProject: (path: string) => void;
  onUploadWorkspace: (files: File[], directoryUpload: boolean) => Promise<void>;
  onClearWorkspace: () => void;
}

interface LocalFileReview {
  files: File[];
  directoryUpload: boolean;
  archiveUpload: boolean;
  label: string;
}

function wellFormedXml(source: string): boolean {
  try {
    const document = new DOMParser().parseFromString(source, 'application/xml');
    return Boolean(document.documentElement) && document.getElementsByTagName('parsererror').length === 0;
  } catch {
    return false;
  }
}

export function ProjectOpenProjectDialog({
  language,
  backend,
  context,
  open,
  busy,
  canOpenServerPath,
  canUploadWorkspace,
  error,
  workspace,
  onClose,
  onOpenPath,
  onOpenUploadedProject,
  onUploadWorkspace,
  onClearWorkspace,
}: ProjectOpenProjectDialogProps) {
  const [serverPath, setServerPath] = useState('');
  const [quickImportBusy, setQuickImportBusy] = useState(false);
  const [review, setReview] = useState<LocalFileReview | null>(null);
  const [localReviewState, setLocalReviewState] = useState<string>();
  const [reviewing, setReviewing] = useState(false);
  const projectFileInput = useRef<HTMLInputElement>(null);
  const directoryInput = useRef<HTMLInputElement>(null);
  const archiveInput = useRef<HTMLInputElement>(null);
  const dialogBusy = busy || quickImportBusy;

  useEffect(() => {
    directoryInput.current?.setAttribute('webkitdirectory', '');
  }, []);

  if (!open) return null;

  const inspectSelection = async (selected: File[], directoryUpload: boolean): Promise<void> => {
    if (selected.length === 0) return;
    const archive = !directoryUpload && selected.length === 1 && selected[0].name.toLowerCase().endsWith('.zip');
    const projectFiles = selected.filter((file) => file.name.toLowerCase().endsWith('.ras'));
    if (!archive && projectFiles.length === 0) {
      setReview(null);
      setLocalReviewState(text(language, '所选内容中没有 .ras 工程文件。', 'No .ras project file was found in the selection.'));
      return;
    }
    setReviewing(true);
    setLocalReviewState(undefined);
    try {
      if (archive) {
        const file = selected[0];
        setReview({ files: selected, directoryUpload: false, archiveUpload: true, label: file.name });
        setLocalReviewState(text(language,
          `已选择 ZIP 压缩包，大小 ${file.size.toLocaleString()} 字节。上传后由 C# 服务解包并查找 .ras 工程。`,
          `ZIP archive selected (${file.size.toLocaleString()} bytes). The C# service will extract it and find .ras projects after upload.`));
        return;
      }
      if (directoryUpload) {
        setReview({ files: selected, directoryUpload, archiveUpload: false, label: selected[0].webkitRelativePath.split('/')[0] || selected[0].name });
        setLocalReviewState(text(language,
          `已选择 ${selected.length} 个文件，其中有 ${projectFiles.length} 个 .ras 工程。上传时会保留目录结构。`,
          `${selected.length} files selected, including ${projectFiles.length} .ras project(s). The directory structure will be preserved.`));
        return;
      }
      const file = projectFiles[0];
      const source = await file.text();
      setReview({ files: [file], directoryUpload: false, archiveUpload: false, label: file.name });
      const shape = wellFormedXml(source)
        ? text(language, 'XML 语法有效。', 'XML syntax is valid.')
        : text(language, 'XML 语法无效；仍会由 C# 工程模型解析器判定。', 'XML syntax is invalid; the C# Project parser will make the final determination.');
      setLocalReviewState(`${shape} ${file.size.toLocaleString()} ${text(language, '字节', 'bytes')} · ${new Date(file.lastModified).toLocaleString(language === 'zh' ? 'zh-CN' : 'en-US')}`);
    } catch (caught) {
      setReview(null);
      setLocalReviewState(caught instanceof Error ? caught.message : String(caught));
    } finally {
      setReviewing(false);
    }
  };

  const submitServerPath = (): void => {
    const path = serverPath.trim();
    if (!isAbsoluteServerPath(path) || !canOpenServerPath || dialogBusy) return;
    void onOpenPath(path);
  };

  const startUpload = (): void => {
    if (!review || !canUploadWorkspace || dialogBusy || reviewing) return;
    void onUploadWorkspace(review.files, review.directoryUpload);
    setReview(null);
  };

  return (
    <div className="project-modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget && !dialogBusy) onClose(); }}>
      <section className="project-open-modal" role="dialog" aria-modal="true" aria-labelledby="project-open-title">
        <header className="project-modal-header">
          <div>
            <div className="project-kicker">{text(language, '工程管理 / 打开', 'PROJECT EXPLORER / OPEN')}</div>
            <h2 id="project-open-title">{text(language, '打开工程', 'Open Project')}</h2>
          </div>
          <button type="button" className="project-icon-button" aria-label={text(language, '关闭', 'Close')} disabled={dialogBusy} onClick={onClose}>×</button>
        </header>

        <div className="project-modal-body project-open-body">
          <section className="project-open-section">
            <h3>{text(language, '服务器上的工程路径', 'Project path on the server')}</h3>
            <p>{text(language, '输入运行 C# 服务的本机上的绝对 .ras 路径，也可输入只包含一个 .ras 工程的目录。', 'Enter an absolute .ras path on the machine running the C# service, or a directory containing exactly one .ras project.')}</p>
            <div className="project-open-path-row">
              <input value={serverPath} disabled={dialogBusy} placeholder={text(language, '例如 /Users/name/Projects/Model/Model.ras', 'For example /Users/name/Projects/Model/Model.ras')} onChange={(event) => setServerPath(event.currentTarget.value)} onKeyDown={(event) => { if (event.key === 'Enter') submitServerPath(); }} />
              <button type="button" className="project-button project-button-primary" disabled={dialogBusy || !canOpenServerPath || !isAbsoluteServerPath(serverPath.trim())} onClick={submitServerPath}>{text(language, '打开路径', 'Open path')}</button>
            </div>
            {!canOpenServerPath && <small className="project-field-hint">{text(language, '当前后端未提供 project.open。', 'The connected backend does not provide project.open.')}</small>}
          </section>

          <QuickProjectImportControl
            language={language}
            backend={backend}
            context={context}
            disabled={dialogBusy}
            onBusyChange={setQuickImportBusy}
            onOpenProjectPath={async (path) => await onOpenPath(path)}
          />

          <section className="project-open-section">
            <h3>{text(language, '从此浏览器上传', 'Upload from this browser')}</h3>
            <p>{text(language, '单个 .ras 上传不包含其关联 HDF、地形、DSS 等文件；要保留依赖，请上传整个工程目录或包含工程的 ZIP 压缩包。上传会写入当前本机 C# 服务工作区。', 'A single .ras upload does not include related HDF, terrain, DSS, and other files. Upload the complete project directory or a ZIP archive that contains it. Files are sent to the local C# service workspace.')}</p>
            <div className="project-open-upload-actions">
              <button type="button" className="project-button project-button-secondary" disabled={dialogBusy || reviewing || !canUploadWorkspace} onClick={() => projectFileInput.current?.click()}>{text(language, '选择单个 .ras', 'Choose one .ras file')}</button>
              <button type="button" className="project-button project-button-secondary" disabled={dialogBusy || reviewing || !canUploadWorkspace} onClick={() => directoryInput.current?.click()}>{text(language, '选择整个工程目录', 'Choose project directory')}</button>
              <button type="button" className="project-button project-button-secondary" data-testid="project-upload-zip" disabled={dialogBusy || reviewing || !canUploadWorkspace} onClick={() => archiveInput.current?.click()}>{text(language, '选择 ZIP 压缩包', 'Choose ZIP archive')}</button>
            </div>
            <input ref={projectFileInput} className="project-hidden-input" type="file" accept=".ras,application/xml,text/xml" onChange={(event) => { void inspectSelection(Array.from(event.currentTarget.files ?? []), false); event.currentTarget.value = ''; }} />
            <input ref={directoryInput} className="project-hidden-input" type="file" multiple onChange={(event) => { void inspectSelection(Array.from(event.currentTarget.files ?? []), true); event.currentTarget.value = ''; }} />
            <input ref={archiveInput} className="project-hidden-input" data-testid="project-upload-zip-input" type="file" accept=".zip,application/zip,application/x-zip-compressed" onChange={(event) => { void inspectSelection(Array.from(event.currentTarget.files ?? []), false); event.currentTarget.value = ''; }} />
            {!canUploadWorkspace && <small className="project-field-hint">{text(language, '当前前端连接没有工作区上传能力。', 'Workspace upload is unavailable in the current frontend connection.')}</small>}
            {reviewing && <div className="project-open-review" role="status">{text(language, '正在检查所选工程文件…', 'Checking selected project files…')}</div>}
            {review && <div className="project-open-review">
              <strong>{review.label}</strong>
              <span>{localReviewState}</span>
              {review.archiveUpload && <small>{text(language, '解包后若包含多个 .ras 工程，可在上传工作区列表中选择要打开的工程。', 'If the archive contains multiple .ras projects, choose one from the uploaded workspace list after extraction.')}</small>}
              {!review.directoryUpload && !review.archiveUpload && <small>{text(language, '确认后仅上传此文件；后端会尝试实际打开，缺失依赖时会返回错误。', 'Only this file will be uploaded. The backend will attempt to open it and report missing dependencies.')}</small>}
              <div className="project-open-upload-actions">
                <button type="button" className="project-button project-button-primary" data-testid="project-upload-confirm" disabled={dialogBusy || !canUploadWorkspace} onClick={startUpload}>{text(language, '上传并打开', 'Upload and open')}</button>
                <button type="button" className="project-button project-button-secondary" disabled={dialogBusy} onClick={() => { setReview(null); setLocalReviewState(undefined); }}>{text(language, '取消', 'Cancel')}</button>
              </div>
            </div>}
          </section>

          {workspace && <section className="project-open-section project-uploaded-workspace" data-testid="project-uploaded-workspace">
            <div className="project-uploaded-heading"><h3>{text(language, '已上传工作区', 'Uploaded workspace')}</h3><button type="button" className="project-text-button" disabled={dialogBusy} onClick={onClearWorkspace}>{text(language, '清除', 'Clear')}</button></div>
            <p>{workspace.fileCount.toLocaleString()} {text(language, '个文件 ·', 'files ·')} <code>{workspace.rootPath}</code></p>
            {workspace.projects.length === 0 ? <div className="project-inline-error">{text(language, '工作区中没有发现 .ras 工程。', 'No .ras projects were found in the workspace.')}</div> : <ul className="project-upload-project-list">
              {workspace.projects.map((project) => <li key={project.path} data-testid="project-uploaded-project" data-project-path={project.path}>
                <span><strong>{project.name}</strong><small>{project.path}</small></span>
                <button type="button" className="project-button project-button-secondary" disabled={dialogBusy || !canOpenServerPath} onClick={() => onOpenUploadedProject(project.path)}>{text(language, '打开', 'Open')}</button>
              </li>)}
            </ul>}
          </section>}
          {error && <div className="project-form-error" role="alert"><span>!</span>{error}</div>}
        </div>
        <footer className="project-modal-footer">
          <div className="project-footer-hint">{text(language, '只在 C# 后端实际返回路径、XML 和修订后切换当前工程。', 'The active project changes only after C# returns its path, XML, and revision.')}</div>
          <div className="project-footer-actions"><button type="button" className="project-button project-button-secondary" disabled={dialogBusy} onClick={onClose}>{text(language, '关闭', 'Close')}</button></div>
        </footer>
      </section>
    </div>
  );
}
