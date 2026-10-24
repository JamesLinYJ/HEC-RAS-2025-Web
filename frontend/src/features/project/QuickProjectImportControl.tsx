import { useEffect, useRef, useState } from 'react';
import type { BackendGateway, Language, WorkbenchContext } from '../../shared/workbench';
import { errorDiagnostics, message, text } from '../../shared/workbench';
import {
  isQuickProjectImportAbsolutePath,
  isWorkspaceFileInspection,
  isWorkspacePreparedProject,
  quickProjectImportMode,
} from './QuickProjectImportWorkflow';
import type { WorkspacePreparedProject } from './QuickProjectImportWorkflow';

interface QuickProjectImportControlProps {
  language: Language;
  backend: BackendGateway;
  context: WorkbenchContext;
  disabled: boolean;
  onBusyChange: (busy: boolean) => void;
  onOpenProjectPath: (projectPath: string) => Promise<boolean>;
}

interface ImportProblem {
  message: string;
  details: string;
}

function sameProjectSource(left: WorkbenchContext, right: WorkbenchContext): boolean {
  return left.projectPath === right.projectPath &&
    left.projectSessionID === right.projectSessionID &&
    left.projectXml === right.projectXml &&
    left.projectRevision === right.projectRevision;
}

function rawDiagnosticText(error: unknown): string {
  const details = errorDiagnostics(error);
  if (details instanceof Error) return `${details.name}: ${details.message}${details.stack ? `\n${details.stack}` : ''}`;
  try { return JSON.stringify(details, null, 2) ?? String(details); }
  catch { return String(details); }
}

export function QuickProjectImportControl({ language, backend, context, disabled, onBusyChange, onOpenProjectPath }: QuickProjectImportControlProps) {
  const [sourcePath, setSourcePath] = useState('');
  const [destinationDirectory, setDestinationDirectory] = useState('');
  const [busy, setBusy] = useState(false);
  const [preparedProject, setPreparedProject] = useState<WorkspacePreparedProject | null>(null);
  const [preparedForContext, setPreparedForContext] = useState<WorkbenchContext | null>(null);
  const [status, setStatus] = useState<string>();
  const [problem, setProblem] = useState<ImportProblem>();
  const generation = useRef(0);
  const activeRequest = useRef<AbortController | undefined>(undefined);
  const mounted = useRef(true);
  const contextRef = useRef(context);
  contextRef.current = context;

  const mode = quickProjectImportMode(sourcePath);
  const supportsInspection = backend.supports('workspace.file.inspect');
  const supportsArchive = backend.supports('workspace.import.archive');
  const supportsCoreHdf = backend.supports('workspace.import.coreHdf');
  const supportedImport = mode === 'archive' ? supportsArchive : mode === 'coreHdf' ? supportsCoreHdf : false;
  const canPrepare = !disabled && !busy && supportsInspection && supportedImport && mode !== null &&
    isQuickProjectImportAbsolutePath(sourcePath) && isQuickProjectImportAbsolutePath(destinationDirectory);

  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
      generation.current += 1;
      activeRequest.current?.abort();
      onBusyChange(false);
    };
  }, [onBusyChange]);

  const isCurrent = (id: number, signal: AbortSignal): boolean => generation.current === id && !signal.aborted;

  const openPreparedProject = async (project: WorkspacePreparedProject, sourceContext: WorkbenchContext): Promise<void> => {
    if (!sameProjectSource(contextRef.current, sourceContext)) {
      setStatus(text(language,
        '工程已准备完成，但准备期间当前工程会话发生变化。为避免切换到过期请求目标，未自动打开；请确认后再选择打开。',
        'The project is ready, but the active project session changed during preparation. It was not opened automatically; review the target before opening it.'));
      return;
    }
    try {
      const opened = await onOpenProjectPath(project.projectPath);
      if (!mounted.current) return;
      setStatus(opened
        ? text(language, '工程已准备并通过现有工程打开流程完成切换。', 'The project is prepared and has been opened through the existing project-open flow.')
        : text(language, '工程已准备，但现有工程打开流程未确认切换。处理未保存草稿或工程会话后，可从下方再次打开。', 'The project is prepared, but the existing project-open flow did not confirm the switch. Resolve any draft or session guard, then open it below.'));
    } catch (caught) {
      if (!mounted.current) return;
      setProblem({
        message: message(caught, language),
        details: rawDiagnosticText(caught),
      });
      setStatus(text(language, '工程已准备；打开请求未完成。可检查诊断后再尝试打开。', 'The project is prepared, but the open request did not complete. Review the diagnostics before retrying.'));
    }
  };

  const prepareAndOpen = async (): Promise<void> => {
    if (!canPrepare || mode === null) return;
    const source = sourcePath.trim();
    const destination = destinationDirectory.trim();
    if (!isQuickProjectImportAbsolutePath(source) || !isQuickProjectImportAbsolutePath(destination)) return;
    if (!source.toLocaleLowerCase('en-US').endsWith(mode === 'archive' ? '.zip' : '.h5')) return;

    const id = ++generation.current;
    const controller = new AbortController();
    activeRequest.current?.abort();
    activeRequest.current = controller;
    const sourceContext = { ...contextRef.current };
    setBusy(true);
    onBusyChange(true);
    setPreparedProject(null);
    setPreparedForContext(null);
    setProblem(undefined);
    setStatus(text(language, '正在校验来源 SHA-256…', 'Capturing the source SHA-256…'));

    try {
      if (!supportsInspection) throw new Error(text(language, '当前服务未提供来源文件身份检查，无法安全导入。', 'The connected service does not provide source-file identity inspection, so import cannot proceed safely.'));
      const captureRaw: unknown = await backend.request('workspace.file.inspect', { sourcePath: source }, { signal: controller.signal });
      if (!isCurrent(id, controller.signal)) return;
      if (!isWorkspaceFileInspection(captureRaw, source)) {
        throw new Error(text(language, '来源检查回执与输入路径不匹配，或缺少规范路径、SHA-256、文件长度。', 'The source inspection receipt does not match the entered path or is missing its canonical path, SHA-256, or file length.'));
      }
      const capture = captureRaw;
      const operation = mode === 'archive' ? 'workspace.import.archive' : 'workspace.import.coreHdf';
      if (!backend.supports(operation)) throw new Error(text(language, '当前服务未发布对应的 ZIP 或核心 HDF 工程导入能力。', 'The connected service has not advertised the matching ZIP or core-HDF project import operation.'));
      setStatus(text(language, '来源身份已捕获，正在新建隔离工程目录…', 'Source identity is captured. Preparing an isolated project directory…'));
      const importRaw: unknown = await backend.request(operation, {
        sourcePath: capture.sourcePath,
        destinationDirectory: destination,
        sourceRevision: capture.revision,
      }, { signal: controller.signal });
      if (!isCurrent(id, controller.signal)) return;
      if (!isWorkspacePreparedProject(importRaw, capture, destination, mode)) {
        throw new Error(text(language, '服务端导入回执与来源 SHA-256、目标目录或新工程路径不匹配；不会打开该目标。', 'The import receipt does not match the source SHA-256, destination, or new project path. The target will not be opened.'));
      }
      const prepared = importRaw;
      setPreparedProject(prepared);
      setPreparedForContext(sourceContext);
      setProblem(undefined);
      setStatus(text(language, '服务端已返回匹配的工程准备回执。正在通过现有工程打开流程切换…', 'The service returned a matching project-preparation receipt. Opening through the existing project-open flow…'));
      await openPreparedProject(prepared, sourceContext);
    } catch (caught) {
      if (controller.signal.aborted && generation.current === id) {
        setStatus(text(language, '已请求取消导入，且未触发工程切换。若服务端在取消生效前已完成操作，准备目录可能仍保留。', 'Import cancellation was requested and no project switch was started. If the service completed before cancellation took effect, the prepared directory may remain.'));
      } else if (generation.current === id) {
        setProblem({ message: message(caught, language), details: rawDiagnosticText(caught) });
        setStatus(undefined);
      }
    } finally {
      if (generation.current === id) {
        activeRequest.current = undefined;
        setBusy(false);
        onBusyChange(false);
      }
    }
  };

  const cancel = (): void => {
    if (!busy) return;
    setStatus(text(language, '已发送取消请求；取消结果尚未确认。没有发起工程切换。', 'Cancellation was requested; its final status is not yet confirmed. No project switch has been started.'));
    activeRequest.current?.abort();
  };

  const retryOpen = async (): Promise<void> => {
    if (!preparedProject || busy) return;
    const contextAtClick = { ...contextRef.current };
    if (preparedForContext && !sameProjectSource(contextRef.current, preparedForContext)) {
      setStatus(text(language, '当前工程会话已变化。下方操作会明确请求打开已准备工程，并仍经过现有未保存草稿保护。', 'The active project session has changed. Opening the prepared project below is an explicit request and still passes through existing unsaved-draft protection.'));
    }
    await openPreparedProject(preparedProject, contextAtClick);
  };

  return (
    <section className="project-open-section project-quick-import" data-testid="project-quick-import">
      <h3>{text(language, '从服务器路径准备新工程', 'Prepare a new project from a server path')}</h3>
      <p>{text(language,
        '输入当前服务可访问的 ZIP 或核心 HDF (.h5) 绝对路径，以及已存在的目标目录。服务会先捕获源文件 SHA-256，再在目标目录新建工程；不会覆盖已有工程。',
        'Enter an absolute path to a ZIP or core HDF (.h5) visible to the connected service, plus an existing destination directory. The service captures the source SHA-256 before creating a new project; existing projects are not replaced.')}</p>
      <label className="project-form-field">
        <span>{text(language, '来源文件绝对路径', 'Absolute source-file path')}</span>
        <input data-testid="project-quick-import-source-path" autoComplete="off" spellCheck={false} value={sourcePath} disabled={disabled || busy} placeholder={text(language, '例如 /Users/name/Downloads/model.zip 或 model.h5', 'For example /Users/name/Downloads/model.zip or model.h5')} onChange={(event) => { setSourcePath(event.currentTarget.value); setPreparedProject(null); setPreparedForContext(null); setStatus(undefined); setProblem(undefined); }} onKeyDown={(event) => { if (event.key === 'Enter') void prepareAndOpen(); }} />
      </label>
      <label className="project-form-field">
        <span>{text(language, '已存在的目标目录绝对路径', 'Absolute path to an existing destination directory')}</span>
        <input data-testid="project-quick-import-destination" autoComplete="off" spellCheck={false} value={destinationDirectory} disabled={disabled || busy} placeholder={text(language, '例如 /Users/name/Projects', 'For example /Users/name/Projects')} onChange={(event) => { setDestinationDirectory(event.currentTarget.value); setPreparedProject(null); setPreparedForContext(null); setStatus(undefined); setProblem(undefined); }} onKeyDown={(event) => { if (event.key === 'Enter') void prepareAndOpen(); }} />
      </label>
      {mode === null && sourcePath.trim().length > 0 && <small className="project-field-hint" role="status">{text(language, '来源必须是 ZIP 压缩包或核心 HDF .h5 文件。', 'The source must be a ZIP archive or a core-HDF .h5 file.')}</small>}
      {mode !== null && mode === 'coreHdf' && <small className="project-field-hint">{text(language, '核心 HDF 会按服务识别出的结果、几何或边界条件类型新建工程。', 'The service creates a project using the detected result, geometry, or boundary-condition type.')}</small>}
      {(!supportsInspection || (mode === 'archive' && !supportsArchive) || (mode === 'coreHdf' && !supportsCoreHdf)) && <small className="project-field-hint" role="status">{text(language, '当前服务未发布所需的来源检查或导入能力。', 'The connected service has not advertised the required source inspection or import operation.')}</small>}
      <div className="project-open-upload-actions">
        <button type="button" className="project-button project-button-primary" data-testid="project-quick-import-submit" disabled={!canPrepare} onClick={() => void prepareAndOpen()}>{busy ? text(language, '正在准备…', 'Preparing…') : text(language, '校验来源并准备工程', 'Verify source and prepare project')}</button>
        {busy && <button type="button" className="project-button project-button-secondary" data-testid="project-quick-import-cancel" onClick={cancel}>{text(language, '取消', 'Cancel')}</button>}
      </div>
      {status && <div className="project-open-review" data-testid="project-quick-import-status" role="status">{status}</div>}
      {problem && <div className="project-form-error" data-testid="project-quick-import-error" role="alert"><span>!</span>{problem.message}<details><summary>{text(language, '查看诊断信息', 'View diagnostics')}</summary><pre>{problem.details}</pre></details></div>}
      {preparedProject && <div className="project-open-review" data-testid="project-quick-import-receipt">
        <strong>{text(language, '已准备的工程', 'Prepared project')}</strong>
        <span>{preparedProject.projectName} · {preparedProject.importKind}</span>
        <code>{preparedProject.projectPath}</code>
        <small>{text(language, '来源 SHA-256', 'Source SHA-256')}: {preparedProject.sourceRevision}</small>
        {preparedProject.importedAssetPath && <small>{text(language, '导入文件', 'Imported file')}: {preparedProject.importedAssetPath} · {preparedProject.importedAssetRevision}</small>}
        <div className="project-open-upload-actions">
          <button type="button" className="project-button project-button-secondary" data-testid="project-quick-import-open-receipt" disabled={disabled || busy} onClick={() => void retryOpen()}>{text(language, '打开准备好的工程', 'Open prepared project')}</button>
          <button type="button" className="project-text-button" disabled={disabled || busy} onClick={() => { setPreparedProject(null); setPreparedForContext(null); setStatus(undefined); }}>{text(language, '清除回执', 'Clear receipt')}</button>
        </div>
      </div>}
    </section>
  );
}
