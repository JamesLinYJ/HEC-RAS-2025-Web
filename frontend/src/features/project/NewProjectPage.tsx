import { useEffect, useMemo, useRef, useState } from 'react';
import type { BackendGateway, Language, WorkbenchContext } from '../../shared/workbench';
import { message, text } from '../../shared/workbench';
import type { NewProjectDraft, ProjectionEvaluation } from './ProjectTypes';
import { createInitialProjectDraft, getProjectionRequest, isAbsoluteServerPath } from './ProjectTypes';
import { SelectProjectionPage } from './SelectProjectionPage';

interface NewProjectPageProps {
  language: Language;
  backend: BackendGateway;
  context: WorkbenchContext;
  open: boolean;
  resetKey: number;
  onClose: () => void;
  onDirtyChange: (dirty: boolean) => void;
  onCreated: (context: WorkbenchContext, sourceContext: WorkbenchContext) => boolean;
}

interface ProjectCreateResponse {
  success?: boolean;
  error?: string | null;
  projectPath?: string;
  projectXml?: string | null;
  revision?: string;
}

function hasFilenameCharacters(value: string): boolean {
  return !/[<>:"/\\|?*\u0000-\u001f]/.test(value);
}

export function NewProjectPage({ language, backend, context, open, resetKey, onClose, onDirtyChange, onCreated }: NewProjectPageProps) {
  const [draft, setDraft] = useState<NewProjectDraft>(createInitialProjectDraft);
  const [error, setError] = useState<string>();
  const [projectionResult, setProjectionResult] = useState<ProjectionEvaluation>();
  const [busy, setBusy] = useState(false);
  const [directoryWarningDismissed, setDirectoryWarningDismissed] = useState(false);
  const requestGeneration = useRef(0);
  const controllerRef = useRef<AbortController | undefined>(undefined);
  const contextRef = useRef(context);
  contextRef.current = context;
  const initialDraft = useMemo(createInitialProjectDraft, []);
  const dirty = Object.keys(initialDraft).some((key) => draft[key as keyof NewProjectDraft] !== initialDraft[key as keyof NewProjectDraft]);
  const createMethod = backend.supports('project.create') ? 'project.create' : null;
  const canEvaluate = backend.supports('projection.evaluate');
  const title = draft.projectTitle.trim();
  const titleValid = title.length >= 3 && hasFilenameCharacters(title);
  const createDirectory = draft.createDirectory.trim();
  const directoryValid = isAbsoluteServerPath(createDirectory);
  const projectionValid = draft.projectionMode === 'local' || draft.projectionText.trim().length > 0;
  const valid = titleValid && directoryValid && projectionValid;
  const directoryHasOneDrive = createDirectory.toLowerCase().includes('onedrive');

  useEffect(() => {
    onDirtyChange(dirty);
  }, [dirty, onDirtyChange]);

  useEffect(() => {
    setDraft(createInitialProjectDraft());
    setError(undefined);
    setProjectionResult(undefined);
    setDirectoryWarningDismissed(false);
  }, [resetKey]);

  useEffect(() => () => {
    requestGeneration.current += 1;
    controllerRef.current?.abort();
    onDirtyChange(false);
  }, [onDirtyChange]);

  const updateDraft = (next: NewProjectDraft): void => {
    setDraft(next);
    setProjectionResult(undefined);
    setError(undefined);
  };

  const evaluateProjection = async (): Promise<ProjectionEvaluation | undefined> => {
    if (!canEvaluate) {
      setError(text(language, '投影验证操作尚未接入。', 'Projection validation is not available in the connected backend.'));
      return undefined;
    }
    const generation = ++requestGeneration.current;
    const controller = new AbortController();
    controllerRef.current?.abort();
    controllerRef.current = controller;
    setBusy(true);
    setError(undefined);
    try {
      const evaluation = await backend.request<ProjectionEvaluation>('projection.evaluate', getProjectionRequest(draft), { signal: controller.signal });
      if (generation !== requestGeneration.current || controller.signal.aborted) return undefined;
      setProjectionResult(evaluation);
      if (!evaluation.isValid) setError(evaluation.error ?? text(language, '无法使用此投影定义。', 'This projection definition cannot be used.'));
      return evaluation;
    } catch (caught) {
      if (generation !== requestGeneration.current || controller.signal.aborted) return undefined;
      setError(message(caught));
      return undefined;
    } finally {
      if (generation === requestGeneration.current) setBusy(false);
    }
  };

  const createProject = async (): Promise<void> => {
    if (!createMethod || !valid || busy) return;
    const sourceContext = contextRef.current;
    const generation = ++requestGeneration.current;
    const controller = new AbortController();
    controllerRef.current?.abort();
    controllerRef.current = controller;
    setBusy(true);
    setError(undefined);
    try {
      let evaluation = projectionResult;
      if (canEvaluate && (!evaluation || !evaluation.isValid || evaluation.normalizedValue !== getProjectionRequest(draft).value)) {
        evaluation = await backend.request<ProjectionEvaluation>('projection.evaluate', getProjectionRequest(draft), { signal: controller.signal });
        if (generation !== requestGeneration.current || controller.signal.aborted) return;
        if (!sameProjectSource(contextRef.current, sourceContext)) {
          setError(text(language, '当前工程会话已变化，已取消新建工程请求。', 'The active project session changed, so project creation was canceled.'));
          return;
        }
        setProjectionResult(evaluation);
      }
      if (evaluation && !evaluation.isValid) {
        setError(evaluation.error ?? text(language, '无法使用此投影定义。', 'This projection definition cannot be used.'));
        return;
      }

      const result = await backend.request<ProjectCreateResponse>(createMethod, {
        projectTitle: title,
        description: draft.description,
        createDirectory,
        isUSC: draft.unitSystem === 'USCustomary',
        isLocalProj: draft.projectionMode === 'local',
        isEPSG: draft.projectionMode === 'epsg',
        isWktProj: draft.projectionMode === 'wkt',
        isPRJFile: draft.projectionMode === 'prj',
        projectionText: draft.projectionText,
        projectionDefinition: getProjectionRequest(draft),
      }, { signal: controller.signal });
      if (generation !== requestGeneration.current || controller.signal.aborted) return;
      if (result?.success !== true) {
        setError(result.error ?? text(language, '后端未确认创建工程。', 'The backend did not confirm project creation.'));
        return;
      }
      if (typeof result.projectPath !== 'string' || result.projectPath.length === 0 ||
          typeof result.projectXml !== 'string' || typeof result.revision !== 'string' || !/^[0-9a-f]{64}$/i.test(result.revision)) {
        setError(text(language, '后端响应未返回工程路径、XML 与修订，无法确认已创建工程。', 'The backend response did not include the project path, XML, and revision needed to confirm creation.'));
        return;
      }
      if (!sameProjectSource(contextRef.current, sourceContext)) {
        setError(text(language,
          `工程已在服务器创建（${result.projectPath}），但当前工程会话已变化；未自动切换，请从“打开工程”中手动打开。`,
          `The project was created on the server (${result.projectPath}), but the active project session changed. It was not opened automatically; open it from the Open Project dialog.`));
        return;
      }
      const nextContext: WorkbenchContext = {
        projectSessionID: globalThis.crypto?.randomUUID?.() ?? `project-${Date.now()}`,
        projectPath: result.projectPath,
        projectXml: result.projectXml,
        projectRevision: result.revision,
        selectedAsset: { path: result.projectPath, kind: 'ProjectFile', name: title, revision: result.revision },
      };
      if (!onCreated(nextContext, sourceContext)) {
        setError(text(language,
          '工程已在服务器创建，但当前工程会话已变化；未自动切换，请从“打开工程”中手动打开。',
          'The project was created on the server, but the active project session changed. It was not opened automatically; open it from the Open Project dialog.'));
        return;
      }
      setDraft(createInitialProjectDraft());
      setError(undefined);
    } catch (caught) {
      if (generation !== requestGeneration.current || controller.signal.aborted) return;
      setError(message(caught));
    } finally {
      if (generation === requestGeneration.current) setBusy(false);
    }
  };

  function sameProjectSource(left: WorkbenchContext, right: WorkbenchContext): boolean {
    return left.projectPath === right.projectPath &&
      left.projectSessionID === right.projectSessionID &&
      left.projectXml === right.projectXml &&
      left.projectRevision === right.projectRevision;
  }

  if (!open) return null;

  return (
    <div className="project-modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget && !busy) onClose(); }}>
      <section className="project-new-modal" role="dialog" aria-modal="true" aria-labelledby="project-new-title">
        <header className="project-modal-header">
          <div>
            <div className="project-kicker">{text(language, '工程管理 / 新建', 'PROJECT EXPLORER / NEW')}</div>
            <h2 id="project-new-title">{text(language, '新建工程', 'New Project')}</h2>
          </div>
          <button type="button" className="project-icon-button" aria-label={text(language, '关闭', 'Close')} disabled={busy} onClick={onClose}>×</button>
        </header>

        <div className="project-modal-body">
          <div className="project-form-grid">
            <label className="project-form-field project-span-two">
              <span>{text(language, '工程标题', 'Project title')} <b>*</b></span>
              <input autoFocus value={draft.projectTitle} disabled={busy} maxLength={120} onChange={(event) => updateDraft({ ...draft, projectTitle: event.currentTarget.value })} />
              {draft.projectTitle.length > 0 && !titleValid && <small className="project-inline-error">{title.length < 3 ? text(language, '至少需要 3 个字符。', 'Enter at least 3 characters.') : text(language, '标题包含不能用于文件名的字符。', 'The title contains characters that cannot be used in a filename.')}</small>}
            </label>
            <label className="project-form-field project-span-two">
              <span>{text(language, '说明', 'Description')}</span>
              <textarea rows={2} value={draft.description} disabled={busy} onChange={(event) => updateDraft({ ...draft, description: event.currentTarget.value })} />
            </label>
            <label className="project-form-field project-span-two">
              <span>{text(language, '服务器工程父目录', 'Server project parent folder')} <b>*</b></span>
              <input value={draft.createDirectory} disabled={busy} placeholder={text(language, '输入运行 C# 服务的机器上的绝对路径', 'Absolute path on the machine running the C# service')} onChange={(event) => { setDirectoryWarningDismissed(false); updateDraft({ ...draft, createDirectory: event.currentTarget.value }); }} />
              {createDirectory.length > 0 && !directoryValid && <small className="project-inline-error">{text(language, '请输入服务器上的绝对目录路径。', 'Enter an absolute directory path on the server.')}</small>}
              {createDirectory.length === 0 && <small className="project-field-hint">{text(language, '此路径属于 C# 服务端主机，不是浏览器本机路径。', 'This path is on the C# server host, not the browser computer.')}</small>}
              {directoryHasOneDrive && !directoryWarningDismissed && <small className="project-inline-warning">{text(language, '工程目录位于 OneDrive 中可能导致同步冲突。', 'A project folder inside OneDrive can cause synchronization conflicts.')}</small>}
            </label>
            <div className="project-span-two">
              <SelectProjectionPage language={language} backend={backend} draft={draft} disabled={busy} onDraftChange={updateDraft} />
            </div>
          </div>
          {projectionResult && <div className={`project-projection-evaluation ${projectionResult.isValid ? 'is-valid' : 'is-invalid'}`} role="status">
            <strong>{projectionResult.isValid ? text(language, '投影有效', 'Projection valid') : text(language, '投影无效', 'Projection invalid')}</strong>
            {projectionResult.isValid && <span>{projectionResult.name}{projectionResult.epsgCode ? ` · EPSG:${projectionResult.epsgCode}` : ''}{projectionResult.units ? ` · ${projectionResult.units}` : ''}</span>}
          </div>}
          {error && <div className="project-form-error" role="alert"><span>!</span>{error}</div>}
          {!createMethod && <div className="project-backend-note" role="note"><span>i</span>{text(language, '当前后端尚未提供新建工程操作；表单不会在前端伪造或保存工程文件。', 'The connected backend does not provide project.create; the frontend will not fabricate or save a project file.')}</div>}
          {draft.createDirectory.toLowerCase().includes('onedrive') && <button type="button" className="project-dismiss-warning" onClick={() => setDirectoryWarningDismissed(true)}>{text(language, '知道了', 'Dismiss warning')}</button>}
        </div>

        <footer className="project-modal-footer">
          <div className="project-footer-hint">{canEvaluate ? text(language, '投影定义将由 C# 后端验证。', 'Projection definitions are validated by the C# backend.') : text(language, '投影验证将在后端操作接入后可用。', 'Projection validation becomes available when the backend operation is connected.')}</div>
          <div className="project-footer-actions">
            <button type="button" className="project-button project-button-secondary" disabled={busy} onClick={onClose}>{text(language, '取消', 'Cancel')}</button>
            {canEvaluate && <button type="button" className="project-button project-button-secondary" disabled={busy || (!projectionValid && draft.projectionMode !== 'local')} onClick={() => void evaluateProjection()}>{text(language, '验证投影', 'Validate projection')}</button>}
            <button type="button" className="project-button project-button-primary" disabled={!createMethod || !valid || busy} onClick={() => void createProject()}>{busy ? text(language, '处理中…', 'Working…') : text(language, '创建工程', 'Create project')}</button>
          </div>
        </footer>
      </section>
    </div>
  );
}
