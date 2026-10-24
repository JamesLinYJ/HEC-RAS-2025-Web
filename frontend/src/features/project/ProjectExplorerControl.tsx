import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import type { FeatureId, FeaturePageProps, WorkbenchContext } from '../../shared/workbench';
import { errorDiagnostics, message, text } from '../../shared/workbench';
import type { WorkspaceUploadResult } from '../../shared/workbench';
import type { CatalogTreeNode, CatalogTreeRequest, CatalogTreeSnapshot } from './CatalogTreeSnapshot';
import { catalogAssetKind } from './CatalogTreeSnapshot';
import {
  isProjectTreeSHA256,
  projectTreeModelActionIdentity,
  projectTreeXmlSHA,
} from './ProjectTreeModelActions';
import type {
  ProjectTreeModelActionInspection,
  ProjectTreeModelActionPerformReceipt,
  ProjectTreeModelActionRequest,
} from './ProjectTreeModelActions';
import { ProjectTreeMode } from './ProjectTreeMode';
import type { ProjectTreeAction, ProjectTreeModeName } from './ProjectTreeModeVM';
import { NewProjectPage } from './NewProjectPage';
import { ProjectExplorerMainPageTiled } from './ProjectExplorerMainPageTiled';
import { MigrateProjectControl } from './MigrateProjectControl';
import { NewGeometryDialog } from './NewGeometryDialog';
import { NewPlanDialog } from './NewPlanDialog';
import { NewBoundaryConditionDialog } from './NewBoundaryConditionDialog';
import type { CreatedModel } from './ProjectModelCreationWorkflow';
import type { RecentProjectRecord, SelectedProjectFile } from './ProjectTypes';
import { isAbsoluteServerPath, MAX_RECENT_PROJECTS, RECENT_PROJECTS_STORAGE_KEY } from './ProjectTypes';
import { ProjectOpenProjectDialog } from './ProjectOpenProjectDialog';
import { ExternalConnectionManager } from './ExternalConnectionManager';
import type { ExternalConnectionCommitReceipt } from './ExternalConnectionWorkflow';
import './project.css';

export type ProjectCommandKind = 'open' | 'new';

export interface PendingProjectCommand {
  id: string;
  kind: ProjectCommandKind;
}

export interface ProjectExplorerProps extends FeaturePageProps {
  onDirtyStateChange?: (owner: string, dirty: boolean) => void;
  pendingProjectCommand?: PendingProjectCommand | null;
  onProjectCommandHandled?: (id: string) => void;
}

interface PendingOpen {
  projectPath: string;
}

interface ProjectOpenResponse {
  success?: boolean;
  error?: string | null;
  projectPath?: string;
  projectXml?: string | null;
  revision?: string;
}

interface ProjectSaveResponse extends ProjectOpenResponse {}

interface UploadedWorkspaceState extends WorkspaceUploadResult {
  directoryUpload: boolean;
}

interface TreeErrors {
  project?: string;
  catalog?: string;
}

function readRecentProjects(): RecentProjectRecord[] {
  try {
    const serialized = localStorage.getItem(RECENT_PROJECTS_STORAGE_KEY);
    if (!serialized) return [];
    const parsed = JSON.parse(serialized) as unknown;
    if (!Array.isArray(parsed)) return [];
    return parsed.filter((item): item is RecentProjectRecord => Boolean(
      item && typeof item === 'object' &&
      typeof (item as RecentProjectRecord).id === 'string' &&
      typeof (item as RecentProjectRecord).projectPath === 'string' &&
      typeof (item as RecentProjectRecord).fileName === 'string' &&
      typeof (item as RecentProjectRecord).revision === 'string' &&
      typeof (item as RecentProjectRecord).openedAt === 'number',
    )).slice(0, MAX_RECENT_PROJECTS);
  } catch {
    return [];
  }
}

function safeSetRecentProjects(records: RecentProjectRecord[]): void {
  try {
    localStorage.setItem(RECENT_PROJECTS_STORAGE_KEY, JSON.stringify(records.slice(0, MAX_RECENT_PROJECTS)));
  } catch {
    // The browser may block storage. The current file remains open in memory.
  }
}

function createSessionId(): string {
  return globalThis.crypto?.randomUUID?.() ?? `project-${Date.now()}-${Math.random().toString(36).slice(2)}`;
}

function isWellFormedXml(source: string): boolean {
  try {
    const parsed = new DOMParser().parseFromString(source, 'application/xml');
    return parsed.documentElement !== null && parsed.getElementsByTagName('parsererror').length === 0;
  } catch {
    return false;
  }
}

function isBrowserFileIdentity(path: string | null): boolean {
  return path?.startsWith('browser-file://') === true;
}

function displayFileName(path: string): string {
  const cleanPath = path.split(/[?#]/, 1)[0];
  const value = cleanPath.split(/[\\/]/).pop() ?? cleanPath;
  try {
    return decodeURIComponent(value);
  } catch {
    return value;
  }
}

function sameProjectSource(left: WorkbenchContext, right: WorkbenchContext): boolean {
  return left.projectPath === right.projectPath &&
    left.projectSessionID === right.projectSessionID &&
    left.projectXml === right.projectXml &&
    left.projectRevision === right.projectRevision;
}

function sameServerPath(left: string, right: string): boolean {
  const normalize = (value: string): string => value.replace(/\\/g, '/').replace(/\/+$/, '');
  const normalizedLeft = normalize(left);
  const normalizedRight = normalize(right);
  const isWindowsPath = (value: string): boolean => /^[A-Za-z]:\//.test(value) || value.startsWith('//');
  return isWindowsPath(normalizedLeft) && isWindowsPath(normalizedRight)
    ? normalizedLeft.toLowerCase() === normalizedRight.toLowerCase()
    : normalizedLeft === normalizedRight;
}

function rawDiagnosticText(error: unknown): string {
  const details = errorDiagnostics(error);
  if (details instanceof Error) return `${details.name}: ${details.message}${details.stack ? `\n${details.stack}` : ''}`;
  try { return JSON.stringify(details, null, 2) ?? String(details); }
  catch { return String(details); }
}

function userErrorText(error: unknown, language: FeaturePageProps['language'], fallback: string): string {
  if (error && typeof error === 'object' && typeof (error as { localizedMessage?: unknown }).localizedMessage === 'function') {
    return message(error, language);
  }
  return fallback;
}

function featureForNode(node: CatalogTreeNode): FeatureId {
  const kind = catalogAssetKind(node);
  if (kind === 'Geometry') return 'geometry';
  if (kind === 'Result') return 'results';
  if (['Plan', 'Terrain', 'Boundary Conditions', 'Surface Layers'].includes(kind)) return 'editors';
  return 'project';
}

export function ProjectExplorerControl(props: ProjectExplorerProps) {
  const { language, context, backend, onNavigate, onContextChange } = props;
  const [recentProjects, setRecentProjects] = useState<RecentProjectRecord[]>(readRecentProjects);
  const [currentFile, setCurrentFile] = useState<SelectedProjectFile | null>(null);
  const [fileBusy, setFileBusy] = useState(false);
  const [libraryImportBusy, setLibraryImportBusyState] = useState(false);
  const [fileError, setFileError] = useState<string>();
  const [fileErrorDetails, setFileErrorDetails] = useState<string>();
  const [notice, setNotice] = useState<string>();
  const [newProjectOpen, setNewProjectOpen] = useState(false);
  const [migrationOpen, setMigrationOpen] = useState(false);
  const [modelCreationDialog, setModelCreationDialog] = useState<'newGeometry' | 'newPlan' | 'newBoundary' | null>(null);
  const [openProjectDialog, setOpenProjectDialog] = useState(false);
  const [externalConnectionsOpen, setExternalConnectionsOpen] = useState(false);
  const [uploadedWorkspace, setUploadedWorkspace] = useState<UploadedWorkspaceState | null>(null);
  const [savingProject, setSavingProject] = useState(false);
  const [newProjectDirty, setNewProjectDirty] = useState(false);
  const [draftResetKey, setDraftResetKey] = useState(0);
  const [pendingOpen, setPendingOpen] = useState<PendingOpen | null>(null);
  const [pendingCloseDraft, setPendingCloseDraft] = useState(false);
  const [treeMode, setTreeMode] = useState<ProjectTreeModeName>('project');
  const [projectSnapshot, setProjectSnapshot] = useState<CatalogTreeSnapshot | null>(null);
  const [catalogSnapshot, setCatalogSnapshot] = useState<CatalogTreeSnapshot | null>(null);
  const [treeLoading, setTreeLoading] = useState(false);
  const [treeErrors, setTreeErrors] = useState<TreeErrors>({});
  const [treeReloadKey, setTreeReloadKey] = useState(0);
  const [pendingModelActionNodeId, setPendingModelActionNodeId] = useState<string | null>(null);
  const projectOpenGeneration = useRef(0);
  const projectOpenAbort = useRef<AbortController | undefined>(undefined);
  const workspaceUploadGeneration = useRef(0);
  const workspaceUploadAbort = useRef<AbortController | undefined>(undefined);
  const projectSaveGeneration = useRef(0);
  const projectSaveAbort = useRef<AbortController | undefined>(undefined);
  const treeRequestGeneration = useRef(0);
  const treeAbort = useRef<AbortController | undefined>(undefined);
  const treeActionGeneration = useRef(0);
  const treeActionBusy = useRef(false);
  const treeActionAbort = useRef<AbortController | undefined>(undefined);
  const handledProjectCommand = useRef<string | undefined>(undefined);
  const contextRef = useRef(context);
  contextRef.current = context;
  const draftProtectionRef = useRef({ newProjectDirty: false, hasUnsavedDrafts: false });
  draftProtectionRef.current = { newProjectDirty, hasUnsavedDrafts: Boolean(props.hasUnsavedDrafts) };
  const libraryImportBusyRef = useRef(false);
  libraryImportBusyRef.current = libraryImportBusy;

  const hasCurrentProject = Boolean(context.projectPath);
  const hasProjectRevision = /^[0-9a-f]{64}$/i.test(context.projectRevision ?? '');
  const hasUnsavedDrafts = Boolean(props.hasUnsavedDrafts);
  const canSaveProject = Boolean(context.projectPath) && backend.supports('project.save') &&
    !isBrowserFileIdentity(context.projectPath) && hasProjectRevision && !hasUnsavedDrafts;
  const hasBackendFileIdentity = Boolean(context.projectPath) && !isBrowserFileIdentity(context.projectPath);
  const modelCreationDisabledReason = !hasBackendFileIdentity || !context.projectXml || !hasProjectRevision
    ? text(language, '请先打开具有工程 XML 和 SHA-256 修订的服务器工程。', 'Open a server project with project XML and a SHA-256 revision first.')
    : !backend.supports('modelCreation.inspect')
      ? text(language, '当前 C# 后端未发布 modelCreation.inspect。', 'The C# backend has not advertised modelCreation.inspect.')
      : !backend.supports('modelCreation.commit')
        ? text(language, '当前 C# 后端尚未发布带工程回执的 modelCreation.commit。', 'The C# backend has not advertised project-committing modelCreation.commit.')
        : !backend.supports('project.tree.load')
          ? text(language, '当前 C# 后端未提供 project.tree.load，无法读回核验新模型。', 'The C# backend has not advertised project.tree.load, so a new model cannot be verified by read-back.')
        : !props.onContextCommit
          ? text(language, '父级工程会话没有提供提交回执校验。', 'The parent project session does not provide receipt validation.')
          : newProjectDirty
            ? text(language, '新建工程表单有未保存草稿；请先保存或关闭该表单。', 'The new-project form has an unsaved draft; save or close it first.')
            : hasUnsavedDrafts
              ? text(language, '存在未保存的模型编辑草稿；请先保存或处理草稿。', 'Unsaved model-editor drafts exist; save or resolve them first.')
              : undefined;
  const title = currentFile?.fileName ?? (context.projectPath ? displayFileName(context.projectPath) : null);
  const downloadableWorkspace = uploadedWorkspace && context.projectPath &&
    backend.workspaceArchiveUrl &&
    /^[a-f0-9]{32}$/.test(uploadedWorkspace.workspaceId) &&
    uploadedWorkspace.projects.some((project) => sameServerPath(project.path, context.projectPath!))
    ? uploadedWorkspace
    : null;
  const workspaceArchiveUrl = downloadableWorkspace
    ? backend.workspaceArchiveUrl?.(downloadableWorkspace.workspaceId)
    : undefined;

  useEffect(() => {
    safeSetRecentProjects(recentProjects);
  }, [recentProjects]);

  useEffect(() => {
    const selectedAsset = context.selectedAsset;
    if (currentFile && currentFile.identity !== context.projectPath) setCurrentFile(null);
    if (selectedAsset?.kind === 'ProjectFile' && selectedAsset.path !== context.projectPath) {
      setCurrentFile(null);
    }
  }, [context.projectPath, context.selectedAsset, currentFile]);

  const publishSelectedAsset = useCallback((selectedAsset: WorkbenchContext['selectedAsset']): void => {
    onContextChange({ ...contextRef.current, selectedAsset });
  }, [onContextChange]);

  const selectNode = useCallback((node: CatalogTreeNode | null): void => {
    if (!node?.assetID) {
      publishSelectedAsset(null);
      return;
    }
    publishSelectedAsset({
      path: node.assetID,
      kind: catalogAssetKind(node),
      name: node.name,
      locator: node.locator ?? undefined,
    });
  }, [publishSelectedAsset]);

  const commitOpenServerProject = useCallback(async (requestedPath: string): Promise<boolean> => {
    if (!backend.supports('project.open')) {
      setFileError(text(language, '当前后端未提供 project.open。', 'The connected backend does not provide project.open.'));
      setFileErrorDetails(undefined);
      return false;
    }
    if (!isAbsoluteServerPath(requestedPath.trim())) {
      setFileError(text(language, '请输入运行 C# 服务的机器上的绝对工程路径。', 'Enter an absolute project path on the machine running the C# service.'));
      setFileErrorDetails(undefined);
      return false;
    }
    const generation = ++projectOpenGeneration.current;
    const sourceContext = contextRef.current;
    const controller = new AbortController();
    projectOpenAbort.current?.abort();
    projectOpenAbort.current = controller;
    setFileBusy(true);
    setFileError(undefined);
    setFileErrorDetails(undefined);
    setNotice(undefined);
    try {
      const response = await backend.request<ProjectOpenResponse>('project.open', { projectPath: requestedPath.trim() }, { signal: controller.signal });
      if (generation !== projectOpenGeneration.current || controller.signal.aborted) return false;
      if (!sameProjectSource(contextRef.current, sourceContext)) return false;
      if (response?.success !== true) throw new Error(response?.error || text(language, 'C# 后端未确认打开工程。', 'The C# backend did not confirm opening the project.'));
      const revision = response.revision;
      if (typeof response.projectPath !== 'string' || response.projectPath.length === 0 ||
          typeof response.projectXml !== 'string' || typeof revision !== 'string' || !/^[0-9a-f]{64}$/i.test(revision)) {
        throw new Error(text(language, '后端响应缺少服务器工程路径、XML 或修订。', 'The backend response is missing the server project path, XML, or revision.'));
      }
      const projectPath = response.projectPath;
      const xmlSource = response.projectXml;
      const fileName = displayFileName(projectPath);
      const sessionID = createSessionId();
      const selected: SelectedProjectFile = {
        identity: projectPath,
        fileName,
        byteLength: new TextEncoder().encode(xmlSource).byteLength,
        lastModified: null,
        digest: revision,
        xmlSource,
        isWellFormedXml: isWellFormedXml(xmlSource),
      };
      const recent: RecentProjectRecord = {
        id: projectPath,
        projectPath,
        fileName,
        revision,
        openedAt: Date.now(),
      };
      setCurrentFile(selected);
      setRecentProjects((current) => [recent, ...current.filter((item) => item.projectPath !== projectPath)].slice(0, MAX_RECENT_PROJECTS));
      setTreeMode('project');
      setTreeErrors({});
      setProjectSnapshot(null);
      setCatalogSnapshot(null);
      onContextChange({
        projectSessionID: sessionID,
        projectPath,
        projectXml: xmlSource,
        projectRevision: revision,
        selectedAsset: {
          path: projectPath,
          kind: 'ProjectFile',
          name: fileName,
          revision,
        },
      });
      setOpenProjectDialog(false);
      setNotice(text(language, 'C# 后端工程模型已从服务器路径打开，并返回实际 XML 与修订。', 'C# Project opened the server path and returned the actual XML and revision.'));
      return true;
    } catch (error) {
      if (generation === projectOpenGeneration.current && !controller.signal.aborted) {
        setFileError(userErrorText(error, language, text(language, 'C# 后端打开工程失败。展开查看原始诊断。', 'C# Project could not open the project. Expand the raw diagnostics for details.')));
        setFileErrorDetails(rawDiagnosticText(error));
      }
      return false;
    } finally {
      if (generation === projectOpenGeneration.current && !controller.signal.aborted) setFileBusy(false);
    }
  }, [backend, language, onContextChange]);

  const requestProjectPathOpen = useCallback(async (projectPath: string): Promise<boolean> => {
    setFileError(undefined);
    setFileErrorDetails(undefined);
    if (newProjectDirty) {
      setPendingOpen({ projectPath });
      setOpenProjectDialog(false);
      return false;
    }
    setNewProjectOpen(false);
    return commitOpenServerProject(projectPath);
  }, [commitOpenServerProject, newProjectDirty]);

  const updateLibraryImportBusy = useCallback((busy: boolean): void => {
    libraryImportBusyRef.current = busy;
    setLibraryImportBusyState(busy);
  }, []);

  const openLibraryProject = useCallback(async (projectPath: string, sourceContext: WorkbenchContext): Promise<boolean> => {
    if (!sameProjectSource(contextRef.current, sourceContext)) {
      setNotice(text(language, '工程会话在示例准备期间发生变化；为避免打开过期目标，请重新选择。', 'The project session changed while the example was being prepared. Select the example again to avoid opening a stale target.'));
      return false;
    }
    const drafts = draftProtectionRef.current;
    if (drafts.newProjectDirty || drafts.hasUnsavedDrafts) {
      setNotice(text(language, '存在未保存的工程或模型草稿。请先处理草稿，再导入示例工程。', 'There are unsaved project or model drafts. Resolve them before importing an example project.'));
      return false;
    }
    return requestProjectPathOpen(projectPath);
  }, [language, requestProjectPathOpen]);

  const uploadWorkspace = useCallback(async (files: File[], directoryUpload: boolean): Promise<void> => {
    if (!backend.uploadWorkspace) {
      setFileError(text(language, '当前后端连接没有工作区上传能力。', 'The connected backend does not provide workspace upload.'));
      setFileErrorDetails(undefined);
      return;
    }
    const sourceContext = contextRef.current;
    const generation = ++workspaceUploadGeneration.current;
    const controller = new AbortController();
    workspaceUploadAbort.current?.abort();
    workspaceUploadAbort.current = controller;
    setFileBusy(true);
    setFileError(undefined);
    setFileErrorDetails(undefined);
    setNotice(undefined);
    try {
      const result = await backend.uploadWorkspace(files, { signal: controller.signal });
      if (generation !== workspaceUploadGeneration.current || controller.signal.aborted) return;
      if (!result || typeof result.rootPath !== 'string' || !Array.isArray(result.projects) || !Number.isFinite(result.fileCount)) {
        throw new Error(text(language, '上传服务返回了无效工作区信息。', 'The upload service returned an invalid workspace response.'));
      }
      const workspace: UploadedWorkspaceState = { ...result, directoryUpload };
      setUploadedWorkspace(workspace);
      if (!sameProjectSource(contextRef.current, sourceContext)) {
        setNotice(text(language,
          '工作区已上传，但当前工程会话已变化；为避免切换到旧请求的目标，请从此工作区列表手动打开工程。',
          'The workspace uploaded, but the active project session changed. Open a project from this workspace list when you are ready.'));
        return;
      }
      if (workspace.projects.length === 1) {
        await requestProjectPathOpen(workspace.projects[0].path);
      } else if (workspace.projects.length === 0) {
        setFileError(text(language, '上传完成，但没有发现 .ras 工程。', 'Upload completed, but no .ras project was found.'));
        setFileErrorDetails(undefined);
      } else {
        setNotice(text(language, `已上传工作区，发现 ${workspace.projects.length} 个工程；请选择要打开的工程。`, `Workspace uploaded with ${workspace.projects.length} projects; choose one to open.`));
      }
    } catch (error) {
      if (generation === workspaceUploadGeneration.current && !controller.signal.aborted) {
        setFileError(userErrorText(error, language, text(language, '工作区上传或工程发现失败。展开查看原始诊断。', 'Workspace upload or project discovery failed. Expand the raw diagnostics for details.')));
        setFileErrorDetails(rawDiagnosticText(error));
      }
    } finally {
      if (generation === workspaceUploadGeneration.current && !controller.signal.aborted) setFileBusy(false);
    }
  }, [backend, language, requestProjectPathOpen]);

  const runProjectCommand = useCallback((kind: ProjectCommandKind): void => {
    if (libraryImportBusyRef.current) {
      setNotice(text(language, '示例工程正在准备中，请稍候再切换工程操作。', 'An example project is being prepared. Wait before starting another project action.'));
      return;
    }
    if (kind === 'open') setOpenProjectDialog(true);
    else setNewProjectOpen(true);
  }, [language]);

  useLayoutEffect(() => {
    const pending = props.pendingProjectCommand;
    if (!pending || handledProjectCommand.current === pending.id) return;
    handledProjectCommand.current = pending.id;
    runProjectCommand(pending.kind);
    props.onProjectCommandHandled?.(pending.id);
  }, [props.pendingProjectCommand, props.onProjectCommandHandled, runProjectCommand]);

  useEffect(() => {
    const handleProjectCommand = (event: Event): void => {
      const kind = (event as CustomEvent<{ kind?: string }>).detail?.kind;
      if (kind === 'open' || kind === 'new') runProjectCommand(kind);
    };
    window.addEventListener('hecras:project-command', handleProjectCommand);
    return () => window.removeEventListener('hecras:project-command', handleProjectCommand);
  }, [runProjectCommand]);

  useEffect(() => {
    treeActionGeneration.current += 1;
    treeActionAbort.current?.abort();
    treeActionBusy.current = false;
    setPendingModelActionNodeId(null);
    return () => {
      treeActionGeneration.current += 1;
      treeActionAbort.current?.abort();
    };
  }, [context.projectPath, context.projectSessionID]);

  const acceptDraftAndOpen = (): void => {
    const request = pendingOpen;
    setPendingOpen(null);
    setPendingCloseDraft(false);
    setNewProjectOpen(false);
    setNewProjectDirty(false);
    setDraftResetKey((key) => key + 1);
    if (request) void commitOpenServerProject(request.projectPath);
  };

  const closeDraft = (): void => {
    setNewProjectOpen(false);
    setNewProjectDirty(false);
    setDraftResetKey((key) => key + 1);
    setPendingCloseDraft(false);
  };

  const handleDraftDirty = useCallback((dirty: boolean): void => {
    setNewProjectDirty(dirty);
    props.onDirtyStateChange?.('project.new-project', dirty);
  }, [props.onDirtyStateChange]);

  const requestCloseNewProject = (): void => {
    if (newProjectDirty) setPendingCloseDraft(true);
    else closeDraft();
  };

  const onCreatedProject = (nextContext: WorkbenchContext, sourceContext: WorkbenchContext): boolean => {
    if (!sameProjectSource(contextRef.current, sourceContext)) return false;
    projectOpenGeneration.current += 1;
    projectOpenAbort.current?.abort();
    props.onDirtyStateChange?.('project.new-project', false);
    setNewProjectDirty(false);
    onContextChange(nextContext);
    setNewProjectOpen(false);
    const xmlSource = nextContext.projectXml ?? '';
    const projectPath = nextContext.projectPath ?? '';
    const revision = nextContext.projectRevision ?? '';
    if (projectPath && xmlSource && /^[0-9a-f]{64}$/i.test(revision)) {
      const fileName = displayFileName(projectPath);
      setCurrentFile({ identity: projectPath, fileName, byteLength: new TextEncoder().encode(xmlSource).byteLength, lastModified: null, digest: revision, xmlSource, isWellFormedXml: isWellFormedXml(xmlSource) });
      const recent: RecentProjectRecord = { id: projectPath, projectPath, fileName, revision, openedAt: Date.now() };
      setRecentProjects((current) => [recent, ...current.filter((item) => item.projectPath !== projectPath)].slice(0, MAX_RECENT_PROJECTS));
    } else setCurrentFile(null);
    setNotice(text(language, 'C# 后端已返回新工程路径。', 'The C# backend returned the new project path.'));
    return true;
  };

  useEffect(() => {
    const path = context.projectPath;
    const generation = ++treeRequestGeneration.current;
    treeAbort.current?.abort();
    const controller = new AbortController();
    treeAbort.current = controller;
    setProjectSnapshot(null);
    setCatalogSnapshot(null);
    setTreeErrors({});
    if (!path || isBrowserFileIdentity(path)) {
      setTreeLoading(false);
      return () => {
        controller.abort();
      };
    }

    const methods: Array<{ method: string; mode: string; target: 'project' | 'catalog' }> = [];
    if (backend.supports('project.tree.load')) methods.push({ method: 'project.tree.load', mode: 'Project', target: 'project' });
    if (backend.supports('catalog.tree.load')) methods.push({ method: 'catalog.tree.load', mode: 'Catalog', target: 'catalog' });
    if (methods.length === 0) {
      setTreeLoading(false);
      setTreeErrors({
        project: text(language, '当前后端未提供 project.tree.load。', 'The current backend does not provide project.tree.load.'),
        catalog: text(language, '当前后端未提供 catalog.tree.load。', 'The current backend does not provide catalog.tree.load.'),
      });
      return () => controller.abort();
    }

    setTreeLoading(true);
    const request: Omit<CatalogTreeRequest, 'mode'> = {
      projectPath: path,
      projectXml: context.projectXml,
      projectSessionID: context.projectSessionID,
    };
    void Promise.allSettled(methods.map(async ({ method, mode, target }) => ({
      target,
      snapshot: await backend.request<CatalogTreeSnapshot>(method, { ...request, mode }, { signal: controller.signal }),
    }))).then((results) => {
      if (generation !== treeRequestGeneration.current || controller.signal.aborted) return;
      const errors: TreeErrors = {};
      for (const result of results) {
        if (result.status === 'fulfilled') {
          if (result.value.target === 'project') setProjectSnapshot(result.value.snapshot);
          else setCatalogSnapshot(result.value.snapshot);
        } else {
          const method = methods[results.indexOf(result)]?.target;
          if (method) errors[method] = message(result.reason);
        }
      }
      setTreeErrors(errors);
    }).finally(() => {
      if (generation === treeRequestGeneration.current && !controller.signal.aborted) setTreeLoading(false);
    });

    return () => {
      controller.abort();
    };
  }, [backend, context.projectPath, context.projectXml, context.projectSessionID, language, props.backendRevision, treeReloadKey]);

  const retryTrees = (): void => setTreeReloadKey((key) => key + 1);

  const onModelCreated = useCallback((source: WorkbenchContext, committed: WorkbenchContext, created: CreatedModel): void => {
    const projectPath = committed.projectPath;
    const projectXml = committed.projectXml;
    const projectRevision = committed.projectRevision;
    if (!source.projectPath || !projectPath || committed.projectSessionID !== source.projectSessionID ||
        !sameServerPath(projectPath, source.projectPath) || typeof projectXml !== 'string' ||
        !isProjectTreeSHA256(projectRevision)) {
      setNotice(text(language, '模型创建已返回，但项目回执不完整；请重新打开工程目录核对。', 'Model creation returned, but its project receipt is incomplete. Reopen the project catalog to verify it.'));
      return;
    }
    const active = contextRef.current;
    // The parent accepts the receipt synchronously, but React may not have
    // rendered these new props yet. Both exact source and committed snapshots
    // belong to that accepted transaction; a third context does not.
    if (!sameProjectSource(active, source) && !sameProjectSource(active, committed)) {
      setNotice(text(language, `模型“${created.name}”已提交到工程，但当前工作台已经切换；请返回该工程目录核对。`, `Model “${created.name}” was committed, but the workbench has switched. Return to that project catalog to verify it.`));
      return;
    }
    contextRef.current = committed;
    const refreshedFile: SelectedProjectFile = {
      identity: projectPath,
      fileName: displayFileName(projectPath),
      byteLength: new TextEncoder().encode(projectXml).byteLength,
      lastModified: currentFile?.identity === projectPath ? currentFile.lastModified : null,
      digest: projectRevision,
      xmlSource: projectXml,
      isWellFormedXml: isWellFormedXml(projectXml),
    };
    setCurrentFile(refreshedFile);
    const recent: RecentProjectRecord = {
      id: projectPath,
      projectPath,
      fileName: refreshedFile.fileName,
      revision: projectRevision,
      openedAt: Date.now(),
    };
    setRecentProjects((current) => [recent, ...current.filter((item) => item.projectPath !== projectPath)].slice(0, MAX_RECENT_PROJECTS));
    setNotice(text(language, `已创建“${created.name}”，并提交工程 XML 与修订回执。`, `Created “${created.name}” and committed the project XML and revision receipt.`));
    setModelCreationDialog(null);
    retryTrees();
  }, [currentFile, language]);

  const openModelCreationDialog = (kind: 'newGeometry' | 'newPlan' | 'newBoundary'): void => {
    if (modelCreationDisabledReason) {
      setNotice(modelCreationDisabledReason);
      return;
    }
    setNotice(undefined);
    setModelCreationDialog(kind);
  };

  const openNode = (node: CatalogTreeNode): void => {
    selectNode(node);
    onNavigate(featureForNode(node));
  };

  const performTreeAction = async (action: ProjectTreeAction, node: CatalogTreeNode): Promise<void> => {
    const identity = projectTreeModelActionIdentity(node);
    const source = contextRef.current;
    if (treeActionBusy.current) return;
    if (action !== 'duplicate' || !identity || treeMode !== 'project' ||
        !backend.supports('project.modelAction.inspect') || !backend.supports('project.modelAction.perform') ||
        !source.projectPath || isBrowserFileIdentity(source.projectPath) || !source.projectXml ||
        !source.projectRevision || !isProjectTreeSHA256(source.projectRevision) || !node.assetID) {
      setNotice(text(language, '当前工程或 C# 服务不满足复制模型的条件。', 'The current project or C# service cannot copy this model.'));
      return;
    }
    const controller = new AbortController();
    treeActionAbort.current?.abort();
    treeActionAbort.current = controller;
    treeActionBusy.current = true;
    const generation = ++treeActionGeneration.current;
    setPendingModelActionNodeId(node.id);
    const projectPath = source.projectPath;
    const projectXml = source.projectXml;
    const projectRevision = source.projectRevision;
    const projectSessionID = source.projectSessionID;
    const isCurrentRequest = (): boolean => generation === treeActionGeneration.current &&
      !controller.signal.aborted && sameProjectSource(contextRef.current, source);
    try {
      setNotice(props.hasUnsavedDrafts
        ? text(language, '正在按工程中已保存的模型文件复制；编辑器里的未保存草稿会保留，且不会写入新副本。', 'Copying the saved project model; unsaved editor drafts will be preserved and will not be included in the copy.')
        : text(language, '正在核对已保存模型的版本并复制…', 'Checking the saved model revision and copying…'));
      const projectXmlSHA = await projectTreeXmlSHA(projectXml);
      if (!isCurrentRequest()) return;
      const request: ProjectTreeModelActionRequest = {
        projectPath,
        projectXml,
        projectXmlSHA,
        projectRevision,
        projectSessionID,
        action: 'duplicate',
        kind: identity.kind,
        nodeID: node.id,
        rootSourceClass: identity.rootSourceClass,
        sourceClass: identity.sourceClass,
        nodeName: node.name,
        assetPath: node.assetID,
      };
      const inspection = await backend.request<ProjectTreeModelActionInspection>(
        'project.modelAction.inspect', request, { signal: controller.signal });
      if (!isCurrentRequest()) return;
      if (!inspection || inspection.action !== 'duplicate' || inspection.kind !== identity.kind ||
          !sameServerPath(inspection.projectPath, projectPath) ||
          inspection.projectSessionID !== projectSessionID ||
          !isProjectTreeSHA256(inspection.projectRevision) ||
          inspection.projectRevision.toUpperCase() !== projectRevision.toUpperCase() ||
          inspection.projectXmlSHA?.toUpperCase() !== projectXmlSHA.toUpperCase() ||
          inspection.nodeID !== node.id || inspection.rootSourceClass !== identity.rootSourceClass ||
          inspection.sourceClass !== identity.sourceClass || inspection.nodeName !== node.name ||
          !sameServerPath(inspection.assetPath, node.assetID) ||
          !isProjectTreeSHA256(inspection.assetRevision) || !inspection.availableActions?.includes('duplicate')) {
        throw new Error(text(language, '后端检查回执与当前模型身份或修订不匹配。', 'The backend inspection did not match the selected model identity or revision.'));
      }
      const receipt = await backend.request<ProjectTreeModelActionPerformReceipt>(
        'project.modelAction.perform', { ...request, expectedAssetSHA: inspection.assetRevision },
        { signal: controller.signal });
      if (!isCurrentRequest()) return;
      if (!receipt || receipt.action !== 'duplicate' || receipt.kind !== identity.kind ||
          !sameServerPath(receipt.projectPath, projectPath) || receipt.projectSessionID !== projectSessionID ||
          !sameServerPath(receipt.originalAssetPath, node.assetID) ||
          receipt.originalAssetSHA?.toUpperCase() !== inspection.assetRevision.toUpperCase() ||
          typeof receipt.projectXml !== 'string' || !isProjectTreeSHA256(receipt.projectRevision) ||
          !isProjectTreeSHA256(receipt.projectXmlSHA) || !isProjectTreeSHA256(receipt.newAssetSHA) ||
          !receipt.newAssetPath || sameServerPath(receipt.newAssetPath, node.assetID) || !receipt.newName ||
          typeof receipt.assetPublished !== 'boolean' || typeof receipt.projectMetadataCommitted !== 'boolean') {
        throw new Error(text(language, '后端没有返回可核验的模型复制回执。', 'The backend did not return a verifiable model copy receipt.'));
      }
      const receiptXmlSHA = await projectTreeXmlSHA(receipt.projectXml);
      if (!isCurrentRequest()) return;
      if (receiptXmlSHA !== receipt.projectXmlSHA.toUpperCase()) {
        throw new Error(text(language, '工程文件回执的校验值不匹配。', 'The project XML receipt checksum did not match.'));
      }
      if (receipt.success !== true || !receipt.assetPublished || !receipt.projectMetadataCommitted) {
        const partialNotice = receipt.assetPublished
          ? text(language, `后端报告副本“${receipt.newName}”已写入，但工程提交没有完整确认。请核对工程文件和新模型后再继续。${receipt.warning ? ` ${receipt.warning}` : ''}`, `The backend reports that “${receipt.newName}” was written, but the project commit was not fully confirmed. Check the project and copied model before continuing. ${receipt.warning ?? ''}`)
          : text(language, `后端未确认副本已写入。${receipt.warning ? ` ${receipt.warning}` : ''}`, `The backend did not confirm that the copy was written. ${receipt.warning ?? ''}`);
        setNotice(partialNotice);
        return;
      }
      const currentContext = contextRef.current;
      if (!sameProjectSource(currentContext, source)) return;
      const committed: WorkbenchContext = {
        ...currentContext,
        projectXml: receipt.projectXml,
        projectRevision: receipt.projectRevision,
        selectedAsset: currentContext.selectedAsset?.kind === 'ProjectFile' &&
          sameServerPath(currentContext.selectedAsset.path, projectPath)
          ? { ...currentContext.selectedAsset, revision: receipt.projectRevision }
          : currentContext.selectedAsset,
      };
      const accepted = props.onContextCommit
        ? props.onContextCommit(currentContext, committed)
        : (onContextChange(committed), true);
      if (!accepted) {
        setNotice(text(language, `模型“${receipt.newName}”已复制，但当前会话没有接受工程回执；请刷新工程目录核对。`, `Model “${receipt.newName}” was copied, but the active session did not accept the project receipt. Refresh the project catalog to verify it.`));
        return;
      }
      const refreshedFile: SelectedProjectFile = {
        identity: projectPath,
        fileName: displayFileName(projectPath),
        byteLength: new TextEncoder().encode(receipt.projectXml).byteLength,
        lastModified: currentFile?.lastModified ?? null,
        digest: receipt.projectRevision,
        xmlSource: receipt.projectXml,
        isWellFormedXml: isWellFormedXml(receipt.projectXml),
      };
      setCurrentFile(refreshedFile);
      const recent: RecentProjectRecord = {
        id: projectPath, projectPath, fileName: refreshedFile.fileName,
        revision: receipt.projectRevision, openedAt: Date.now(),
      };
      setRecentProjects((current) => [recent, ...current.filter((item) => item.projectPath !== projectPath)].slice(0, MAX_RECENT_PROJECTS));
      const draftNotice = props.hasUnsavedDrafts
        ? text(language, ' 编辑器中的未保存草稿已保留；副本基于工程中已保存的模型文件。', ' Unsaved editor drafts were preserved; the copy uses the saved project model.')
        : '';
      setNotice(receipt.warning
        ? text(language, `已复制模型“${receipt.newName}”。${receipt.warning}${draftNotice}`, `Copied model “${receipt.newName}”. ${receipt.warning}${draftNotice}`)
        : text(language, `已复制模型“${receipt.newName}”，并提交工程关联。${draftNotice}`, `Copied model “${receipt.newName}” and committed its project associations.${draftNotice}`));
      retryTrees();
    } catch (error) {
      if (!isCurrentRequest()) return;
      setNotice(`${text(language, '复制模型失败', 'Model copy failed')}: ${message(error)} ${text(language, '如后端没有返回回执，请刷新工程并核对新模型文件。', 'If the backend returned no receipt, refresh the project and check for the copied model.')}`);
    } finally {
      if (generation === treeActionGeneration.current) {
        treeActionBusy.current = false;
        setPendingModelActionNodeId(null);
      }
    }
  };

  const saveProject = async (): Promise<void> => {
    if (!canSaveProject || !context.projectPath) return;
    const source = contextRef.current;
    const projectPath = source.projectPath;
    const projectSessionID = source.projectSessionID;
    const revision = source.projectRevision;
    if (!projectPath || !revision || !/^[0-9a-f]{64}$/i.test(revision)) {
      setNotice(text(language, '缺少工程源修订，已禁用保存以防覆盖外部修改。', 'The project source revision is missing; save is disabled to avoid overwriting external changes.'));
      return;
    }
    const controller = new AbortController();
    projectSaveAbort.current?.abort();
    projectSaveAbort.current = controller;
    const generation = ++projectSaveGeneration.current;
    setSavingProject(true);
    setFileError(undefined);
    setFileErrorDetails(undefined);
    try {
      const response = await backend.request<ProjectSaveResponse>('project.save', {
        projectPath,
        projectXml: source.projectXml,
        projectSessionID,
        revision,
      }, { signal: controller.signal });
      if (generation !== projectSaveGeneration.current || controller.signal.aborted) return;
      const currentContext = contextRef.current;
      if (currentContext.projectPath !== projectPath || currentContext.projectSessionID !== projectSessionID ||
          currentContext.projectXml !== source.projectXml || currentContext.projectRevision !== source.projectRevision) return;
      const savedXml = response?.projectXml;
      const savedRevision = response?.revision;
      if (response?.success !== true || response.projectPath !== projectPath || typeof savedXml !== 'string' ||
          typeof savedRevision !== 'string' || !/^[0-9a-f]{64}$/i.test(savedRevision)) {
        throw new Error(response?.error || text(language, '后端没有返回匹配工程路径、XML 和修订的保存回执。', 'The backend did not return a save receipt matching the project path, XML, and revision.'));
      }
      const committed: WorkbenchContext = {
        ...currentContext,
        projectXml: savedXml,
        projectRevision: savedRevision,
        selectedAsset: currentContext.selectedAsset?.kind === 'ProjectFile' && currentContext.selectedAsset.path === projectPath
          ? { ...currentContext.selectedAsset, revision: savedRevision }
          : currentContext.selectedAsset,
      };
      const accepted = props.onContextCommit
        ? props.onContextCommit(currentContext, committed)
        : (onContextChange(committed), true);
      if (!accepted) {
        setNotice(text(language, '保存完成，但工程会话已变化；未将旧回执写回当前上下文。', 'Save completed, but the active project session changed; the old receipt was not written back.'));
        return;
      }
      const refreshedFile: SelectedProjectFile = {
        identity: projectPath,
        fileName: displayFileName(projectPath),
        byteLength: new TextEncoder().encode(savedXml).byteLength,
        lastModified: currentFile?.lastModified ?? null,
        digest: savedRevision,
        xmlSource: savedXml,
        isWellFormedXml: isWellFormedXml(savedXml),
      };
      setCurrentFile(refreshedFile);
      const recent: RecentProjectRecord = { id: projectPath, projectPath, fileName: refreshedFile.fileName, revision: savedRevision, openedAt: Date.now() };
      setRecentProjects((current) => [recent, ...current.filter((item) => item.projectPath !== projectPath)].slice(0, MAX_RECENT_PROJECTS));
      setNotice(text(language, 'C# 后端已保存并返回新的工程修订。', 'The C# backend saved the project and returned a new revision.'));
      retryTrees();
    } catch (error) {
      if (generation !== projectSaveGeneration.current || controller.signal.aborted ||
          contextRef.current.projectPath !== projectPath || contextRef.current.projectSessionID !== projectSessionID ||
          contextRef.current.projectXml !== source.projectXml || contextRef.current.projectRevision !== source.projectRevision) return;
      setNotice(`${text(language, '保存失败', 'Save failed')}: ${message(error)}`);
    } finally {
      if (generation === projectSaveGeneration.current && !controller.signal.aborted) setSavingProject(false);
    }
  };

  const onExternalConnectionsCommitted = (
    source: WorkbenchContext,
    committed: WorkbenchContext,
    _receipt: ExternalConnectionCommitReceipt,
  ): void => {
    if ((!sameProjectSource(contextRef.current, source) && !sameProjectSource(contextRef.current, committed)) ||
        !committed.projectPath || !committed.projectXml || !committed.projectRevision) return;
    const refreshedFile: SelectedProjectFile = {
      identity: committed.projectPath,
      fileName: currentFile?.fileName ?? displayFileName(committed.projectPath),
      byteLength: new TextEncoder().encode(committed.projectXml).byteLength,
      lastModified: currentFile?.lastModified ?? null,
      digest: committed.projectRevision,
      xmlSource: committed.projectXml,
      isWellFormedXml: isWellFormedXml(committed.projectXml),
    };
    setCurrentFile(refreshedFile);
    const recent: RecentProjectRecord = {
      id: committed.projectPath,
      projectPath: committed.projectPath,
      fileName: refreshedFile.fileName,
      revision: committed.projectRevision,
      openedAt: Date.now(),
    };
    setRecentProjects((current) => [recent, ...current.filter((item) => item.projectPath !== committed.projectPath)].slice(0, MAX_RECENT_PROJECTS));
    retryTrees();
  };

  const treeError = treeMode === 'project' ? treeErrors.project : treeErrors.catalog;
  const snapshotForCurrentMode = treeMode === 'project' ? projectSnapshot : catalogSnapshot;
  const currentProjectSummary = useMemo(() => currentFile, [currentFile]);
  const projectStatus = fileBusy
    ? text(language, '正在读取工程文件…', 'Reading project file…')
    : libraryImportBusy
      ? text(language, '正在准备示例工程…', 'Preparing example project…')
      : !hasCurrentProject
        ? text(language, '未打开工程', 'No project open')
        : isBrowserFileIdentity(context.projectPath)
          ? text(language, '本地文件已选 · 等待后端解析', 'Local file selected · backend parsing pending')
          : snapshotForCurrentMode
            ? text(language, '工程目录已由 C# 后端加载', 'Catalog loaded by the C# backend')
            : text(language, '工程已打开 · 目录待加载', 'Project open · catalog pending');

  const headerAction = canSaveProject ? (
    <button type="button" className="project-button project-button-secondary" data-testid="project-save" disabled={savingProject} onClick={() => void saveProject()}>{savingProject ? text(language, '保存中…', 'Saving…') : text(language, '保存', 'Save')}</button>
  ) : (
    <button type="button" className="project-button project-button-secondary" data-testid="project-save" disabled title={hasUnsavedDrafts
      ? text(language, '存在未保存的模型编辑。工程保存只保存工程文件，不包含模型草稿；请先保存模型编辑。', 'There are unsaved model edits. Project save only saves the project file, not model drafts; save the model edits first.')
      : !backend.supports('project.save')
        ? text(language, '当前后端未提供工程保存操作。', 'The current backend does not provide project save.')
        : !hasProjectRevision
          ? text(language, '缺少工程源修订，无法安全保存。', 'The project source revision is missing; save cannot proceed safely.')
          : text(language, '当前工程路径不可由服务端保存。', 'The current project path cannot be saved by the server.')}>{text(language, '保存', 'Save')}</button>
  );

  return (
    <div className="project-feature">
      <div className="project-feature-toolbar">
        <div className="project-toolbar-breadcrumb"><span className="project-toolbar-mark">RAS</span><span>{text(language, '工程管理器', 'Project Explorer')}</span>{title && <><i>/</i><strong title={title}>{title}</strong></>}</div>
        <div className="project-toolbar-actions">
          {headerAction}
          {hasBackendFileIdentity && <button type="button" className="project-button project-button-secondary" data-testid="project-external-connections-open" onClick={() => setExternalConnectionsOpen(true)}>{text(language, '外部连接', 'Connections')}</button>}
          {workspaceArchiveUrl && downloadableWorkspace && <a className="project-button project-button-secondary" data-testid="project-download-workspace" aria-label={text(language, '请求下载当前工程所属的完整上传工作区 ZIP', 'Request the complete uploaded workspace ZIP that contains the current project')} href={workspaceArchiveUrl} download title={text(language, '请求下载当前工程所属的完整上传工作区 ZIP。', 'Request the complete uploaded workspace ZIP that contains the current project.')}>{text(language, '下载工作区 ZIP', 'Download workspace ZIP')}</a>}
          <button type="button" className="project-button project-button-secondary" disabled={fileBusy || libraryImportBusy} onClick={() => setOpenProjectDialog(true)}>{text(language, '打开工程', 'Open project')}</button>
          <button type="button" className="project-button project-button-secondary" disabled={fileBusy || libraryImportBusy || !backend.supports('projectMigration.inspect')} title={!backend.supports('projectMigration.inspect') ? text(language, '当前 C# 后端未发布工程迁移检查。', 'The C# backend has not advertised project migration inspection.') : undefined} onClick={() => setMigrationOpen(true)}>{text(language, '迁移 v6 工程', 'Migrate v6 project')}</button>
          <button type="button" className="project-button project-button-primary" disabled={libraryImportBusy} onClick={() => setNewProjectOpen(true)}>{text(language, '新建工程', 'New project')}</button>
        </div>
      </div>

      {fileError && <div className="project-global-notice is-error" role="alert"><span>!</span><div className="project-global-error-copy">{fileError}{fileErrorDetails && <details><summary>{text(language, '原始诊断', 'Raw diagnostics')}</summary><pre>{fileErrorDetails}</pre></details>}</div><button type="button" onClick={() => { setFileError(undefined); setFileErrorDetails(undefined); }}>×</button></div>}
      {notice && <div className="project-global-notice" role="status"><span>i</span>{notice}<button type="button" onClick={() => setNotice(undefined)}>×</button></div>}

      {!hasCurrentProject ? (
        <ProjectExplorerMainPageTiled
          language={language}
          backend={backend}
          context={context}
          backendRevision={props.backendRevision}
          currentProject={null}
          recents={recentProjects}
          busy={fileBusy}
          importBusy={libraryImportBusy}
          importBlockedReason={newProjectDirty
            ? text(language, '存在未保存的新建工程表单。请先保存或放弃草稿，再导入示例工程。', 'The new-project form has unsaved edits. Save or discard the draft before importing an example.')
            : hasUnsavedDrafts
              ? text(language, '存在未保存的模型草稿。请先保存模型编辑，再导入示例工程。', 'There are unsaved model drafts. Save the model edits before importing an example.')
              : undefined}
          onOpen={() => setOpenProjectDialog(true)}
          onNew={() => setNewProjectOpen(true)}
          onOpenRecent={(recent) => requestProjectPathOpen(recent.projectPath)}
          onClearRecents={() => setRecentProjects([])}
          onImportBusyChange={updateLibraryImportBusy}
          onOpenLibraryProject={openLibraryProject}
        />
      ) : (
        <div className="project-active-layout">
          <ProjectExplorerMainPageTiled
            language={language}
            backend={backend}
            context={context}
            backendRevision={props.backendRevision}
            currentProject={currentProjectSummary}
            externalProjectPath={currentProjectSummary ? null : context.projectPath}
            recents={recentProjects}
            busy={fileBusy}
            importBusy={libraryImportBusy}
            importBlockedReason={newProjectDirty
              ? text(language, '存在未保存的新建工程表单。请先保存或放弃草稿，再导入示例工程。', 'The new-project form has unsaved edits. Save or discard the draft before importing an example.')
              : hasUnsavedDrafts
                ? text(language, '存在未保存的模型草稿。请先保存模型编辑，再导入示例工程。', 'There are unsaved model drafts. Save the model edits before importing an example.')
                : undefined}
            onOpen={() => setOpenProjectDialog(true)}
            onNew={() => setNewProjectOpen(true)}
            onOpenRecent={(recent) => requestProjectPathOpen(recent.projectPath)}
            onClearRecents={() => setRecentProjects([])}
            onImportBusyChange={updateLibraryImportBusy}
            onOpenLibraryProject={openLibraryProject}
            modelCreationDisabledReason={modelCreationDisabledReason}
            onCreateModel={openModelCreationDialog}
          />
          {hasBackendFileIdentity && <ProjectTreeMode
            key={JSON.stringify([context.projectSessionID, context.projectPath])}
            language={language}
            backend={backend}
            mode={treeMode}
            projectSnapshot={projectSnapshot}
            catalogSnapshot={catalogSnapshot}
            loading={treeLoading}
            error={treeError}
            backendSupportsModelActions={backend.supports('project.modelAction.inspect') && backend.supports('project.modelAction.perform')}
            pendingModelActionNodeId={pendingModelActionNodeId}
            selectedAssetPath={context.selectedAsset?.path}
            onModeChange={setTreeMode}
            onSelect={selectNode}
            onOpen={openNode}
            onPerformAction={(action, node) => void performTreeAction(action, node)}
            onRetry={retryTrees}
          />}
        </div>
      )}

      <footer className="project-feature-statusbar"><span><i className={`project-status-dot ${treeLoading || fileBusy || libraryImportBusy ? 'is-busy' : ''}`} />{projectStatus}</span><span>{context.projectSessionID ? `${text(language, '会话', 'Session')} ${context.projectSessionID.slice(0, 8)}` : text(language, '无工程会话', 'No project session')}</span></footer>

      <NewProjectPage
        language={language}
        backend={backend}
        context={context}
        open={newProjectOpen}
        resetKey={draftResetKey}
        onClose={requestCloseNewProject}
        onDirtyChange={handleDraftDirty}
        onCreated={onCreatedProject}
      />

      <ProjectOpenProjectDialog
        language={language}
        open={openProjectDialog}
        busy={fileBusy}
        canOpenServerPath={backend.supports('project.open')}
        canUploadWorkspace={Boolean(backend.uploadWorkspace)}
        error={fileError}
        workspace={uploadedWorkspace}
        onClose={() => { setOpenProjectDialog(false); setFileError(undefined); setFileErrorDetails(undefined); }}
        onOpenPath={requestProjectPathOpen}
        onOpenUploadedProject={requestProjectPathOpen}
        onUploadWorkspace={uploadWorkspace}
        onClearWorkspace={() => setUploadedWorkspace(null)}
      />

      <ExternalConnectionManager
        language={language}
        backend={backend}
        context={context}
        snapshot={catalogSnapshot}
        open={externalConnectionsOpen}
        hasUnsavedDrafts={hasUnsavedDrafts}
        onContextCommit={props.onContextCommit}
        onClose={() => setExternalConnectionsOpen(false)}
        onCommitted={onExternalConnectionsCommitted}
      />

      <MigrateProjectControl
        language={language}
        backend={backend}
        context={context}
        open={migrationOpen}
        hasUnsavedDrafts={hasUnsavedDrafts}
        onClose={() => setMigrationOpen(false)}
        onBusyChange={setFileBusy}
        onContextCommit={props.onContextCommit}
        onOpenProjectPath={requestProjectPathOpen}
      />

      <NewGeometryDialog
        language={language} backend={backend} context={context} open={modelCreationDialog === 'newGeometry'}
        blockedReason={modelCreationDisabledReason} hasUnsavedDrafts={hasUnsavedDrafts}
        onClose={() => setModelCreationDialog(null)} onContextCommit={props.onContextCommit} onCreated={onModelCreated}
      />
      <NewPlanDialog
        language={language} backend={backend} context={context} open={modelCreationDialog === 'newPlan'}
        blockedReason={modelCreationDisabledReason} hasUnsavedDrafts={hasUnsavedDrafts}
        onClose={() => setModelCreationDialog(null)} onContextCommit={props.onContextCommit} onCreated={onModelCreated}
      />
      <NewBoundaryConditionDialog
        language={language} backend={backend} context={context} open={modelCreationDialog === 'newBoundary'}
        blockedReason={modelCreationDisabledReason} hasUnsavedDrafts={hasUnsavedDrafts}
        onClose={() => setModelCreationDialog(null)} onContextCommit={props.onContextCommit} onCreated={onModelCreated}
      />

      {(pendingOpen || pendingCloseDraft) && <div className="project-modal-backdrop project-guard-backdrop" role="presentation">
        <section className="project-guard-dialog" role="alertdialog" aria-modal="true" aria-labelledby="project-draft-guard-title">
          <div className="project-guard-icon">!</div>
          <div className="project-kicker">{text(language, '未保存的工程草稿', 'UNSAVED PROJECT DRAFT')}</div>
          <h2 id="project-draft-guard-title">{text(language, '放弃新建工程草稿？', 'Discard the new project draft?')}</h2>
          <p>{text(language, '当前表单有未提交的标题、说明或投影设置。继续操作会清除这些具体表单内容。', 'The title, description, or projection form has unsaved edits. Continuing will clear those form values.')}</p>
          <div className="project-footer-actions">
            <button type="button" className="project-button project-button-secondary" onClick={() => { setPendingOpen(null); setPendingCloseDraft(false); }}>{text(language, '继续编辑', 'Keep editing')}</button>
            <button type="button" className="project-button project-button-primary" onClick={pendingOpen ? acceptDraftAndOpen : closeDraft}>{pendingOpen ? text(language, '放弃并打开文件', 'Discard and open file') : text(language, '放弃草稿', 'Discard draft')}</button>
          </div>
        </section>
      </div>}
    </div>
  );
}
