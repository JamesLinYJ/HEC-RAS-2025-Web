import { useCallback, useEffect, useRef, useState } from 'react';
import type { BackendGateway, Language, WorkbenchContext } from '../../shared/workbench';
import { errorDiagnostics, message } from '../../shared/workbench';
import { exactCoreLayerContext, hasUncapturedSessionReferences, sourceOwnerKey } from '../tools/coreImportWorkflow';
import type { CatalogTreeNode, CatalogTreeSnapshot } from './CatalogTreeSnapshot';
import { walkCatalogTree } from './CatalogTreeSnapshot';
import type { CreatedModel } from './ProjectModelCreationWorkflow';
import { isProjectTreeSHA256 } from './ProjectTreeModelActions';

export type SurfaceCreationKind = 'newTerrain' | 'newClassification' | 'newNValue';

export interface SurfaceCreationInspection {
  revision: string;
  canExecute: boolean;
  validationMessage?: string | null;
  suggestedName: string;
  targetDirectory: string;
}

interface WorkspaceFileInspection {
  sourcePath: string;
  canonicalPath: string;
  revision: string;
  length: number;
}

export interface VerifiedSurfaceCreation {
  created: CreatedModel;
  asset: WorkspaceFileInspection;
  node: CatalogTreeNode;
}

interface Props {
  backend: BackendGateway;
  context: WorkbenchContext;
  language: Language;
  kind: SurfaceCreationKind;
  name: string;
  open: boolean;
  hasUnsavedDrafts: boolean;
  onContextCommit?: (source: WorkbenchContext, committed: WorkbenchContext) => boolean;
  onCreated: (source: WorkbenchContext, verified: VerifiedSurfaceCreation, selected: WorkbenchContext) => boolean;
}

export interface SurfaceCreationWorkflow {
  inspection: SurfaceCreationInspection | null;
  inspectedName?: string;
  busy: 'inspect' | 'create' | null;
  error?: string;
  details?: string;
  notice?: string;
  fileCreated?: CreatedModel;
  capturedAsset?: WorkspaceFileInspection;
  canCreate: boolean;
  inspect: (nameOverride?: string) => Promise<void>;
  create: () => Promise<void>;
  invalidate: () => void;
  cancel: () => void;
}

function sameServerPath(left: string, right: string): boolean {
  const normalize = (value: string): string => value.replace(/\\/g, '/').replace(/\/+$/, '');
  const a = normalize(left);
  const b = normalize(right);
  const windows = (value: string): boolean => /^[A-Za-z]:\//.test(value) || value.startsWith('//');
  return windows(a) && windows(b) ? a.toLocaleLowerCase('en-US') === b.toLocaleLowerCase('en-US') : a === b;
}

function ownerFingerprint(context: WorkbenchContext): string {
  return JSON.stringify([
    sourceOwnerKey(context),
    context.selectedAsset?.locator ?? null,
    context.coreLayerContext ?? null,
  ]);
}

function isWorkspaceFileInspection(value: unknown, requestedPath: string): value is WorkspaceFileInspection {
  if (!value || typeof value !== 'object') return false;
  const receipt = value as Partial<WorkspaceFileInspection>;
  return typeof receipt.sourcePath === 'string' && sameServerPath(receipt.sourcePath, requestedPath) &&
    typeof receipt.canonicalPath === 'string' && receipt.canonicalPath.length > 0 &&
    isProjectTreeSHA256(receipt.revision) && Number.isSafeInteger(receipt.length) && (receipt.length ?? -1) >= 0;
}

function isAbsoluteServerPath(value: string): boolean {
  return value.startsWith('/') || /^\\\\[^\\]+\\[^\\]+/.test(value) || /^[A-Za-z]:[\\/]/.test(value);
}

function expectedSection(kind: SurfaceCreationKind): string {
  return kind === 'newTerrain' ? 'Terrains' : 'Surface Layers';
}

function expectedSourceClass(kind: SurfaceCreationKind): string {
  if (kind === 'newTerrain') return 'ObservableTerrain';
  return kind === 'newClassification' ? 'ObservableClassificationLayer' : 'ObservableNValueLayer';
}

function expectedTargetDirectoryName(kind: SurfaceCreationKind): string {
  if (kind === 'newTerrain') return 'Terrains';
  return 'Surface Layers';
}

function rawDiagnosticText(error: unknown): string {
  const details = errorDiagnostics(error);
  if (details instanceof Error) return `${details.name}: ${details.message}${details.stack ? `\n${details.stack}` : ''}`;
  try { return JSON.stringify(details, null, 2) ?? String(details); }
  catch { return String(details); }
}

function localizedFailure(error: unknown, language: Language, fallback: string): string {
  if (error && typeof error === 'object' && typeof (error as { localizedMessage?: unknown }).localizedMessage === 'function') {
    return message(error, language);
  }
  const raw = error instanceof Error ? error.message : String(error);
  if (language === 'zh' && /[\u3400-\u9fff]/.test(raw)) return raw;
  return language === 'zh' ? fallback : raw || fallback;
}

function validateInspection(value: unknown, kind: SurfaceCreationKind): asserts value is SurfaceCreationInspection {
  if (!value || typeof value !== 'object') throw new Error('The inspection response is missing.');
  const receipt = value as Partial<SurfaceCreationInspection>;
  if (!isProjectTreeSHA256(receipt.revision) || typeof receipt.canExecute !== 'boolean' ||
      typeof receipt.suggestedName !== 'string' || receipt.suggestedName.length === 0 ||
      typeof receipt.targetDirectory !== 'string' || !isAbsoluteServerPath(receipt.targetDirectory) ||
      pathBaseName(receipt.targetDirectory).toLocaleLowerCase('en-US') !== expectedTargetDirectoryName(kind).toLocaleLowerCase('en-US')) {
    throw new Error('The surface inspection did not return a valid revision, available name, or target folder.');
  }
}

function pathBaseName(value: string): string {
  return value.replace(/\\/g, '/').replace(/\/+$/, '').split('/').pop() ?? '';
}

function validateCreated(value: unknown, kind: SurfaceCreationKind, name: string, targetDirectory: string): asserts value is CreatedModel {
  if (!value || typeof value !== 'object') throw new Error('The service did not return a created-model receipt.');
  const receipt = value as Partial<CreatedModel>;
  if (typeof receipt.assetPath !== 'string' || !isAbsoluteServerPath(receipt.assetPath) ||
      typeof receipt.name !== 'string' || receipt.name !== name || receipt.section !== expectedSection(kind) ||
      !sameServerPath(pathParent(receipt.assetPath), targetDirectory) ||
      !sameServerPath(pathBaseName(receipt.assetPath), `${name}.h5`) ||
      !receipt.associations || typeof receipt.associations !== 'object') {
    throw new Error('The created-file receipt does not match the inspected name, section, or target folder.');
  }
}

function pathParent(value: string): string {
  const normalized = value.replace(/\\/g, '/').replace(/\/+$/, '');
  const index = normalized.lastIndexOf('/');
  return index <= 0 ? normalized.slice(0, Math.max(index, 1)) : normalized.slice(0, index);
}

function findCreatedNode(snapshot: CatalogTreeSnapshot, kind: SurfaceCreationKind, created: CreatedModel, canonicalPath: string): CatalogTreeNode {
  if (!snapshot || !Array.isArray(snapshot.roots)) throw new Error('project.tree.load did not return a project tree snapshot.');
  const section = snapshot.roots.find(node => node.name === expectedSection(kind));
  const matches = walkCatalogTree(section?.children ?? []).filter(node =>
    node.name === created.name && node.sourceClass === expectedSourceClass(kind) &&
    typeof node.assetID === 'string' && sameServerPath(node.assetID, canonicalPath));
  if (matches.length !== 1) {
    throw new Error(`project.tree.load did not read back exactly one ${expectedSourceClass(kind)} named “${created.name}” at ${canonicalPath}.`);
  }
  return matches[0];
}

export function useSurfaceCreation({
  backend, context, language, kind, name, open, hasUnsavedDrafts, onContextCommit, onCreated,
}: Props): SurfaceCreationWorkflow {
  const [inspection, setInspection] = useState<SurfaceCreationInspection | null>(null);
  const [inspectedName, setInspectedName] = useState<string>();
  const [busy, setBusy] = useState<'inspect' | 'create' | null>(null);
  const [error, setError] = useState<string>();
  const [details, setDetails] = useState<string>();
  const [notice, setNotice] = useState<string>();
  const [fileCreated, setFileCreated] = useState<CreatedModel>();
  const [capturedAsset, setCapturedAsset] = useState<WorkspaceFileInspection>();
  const generation = useRef(0);
  const activeRequest = useRef<AbortController | undefined>(undefined);
  const contextRef = useRef(context);
  const nameRef = useRef(name);
  const ownerAtInspect = useRef<string | undefined>(undefined);
  contextRef.current = context;
  nameRef.current = name;

  const owner = ownerFingerprint(context);
  const supportsInspect = backend.supports('surfaceCreation.inspect');
  const supportsCreate = backend.supports('surfaceCreation.create');
  const supportsFileInspect = backend.supports('workspace.file.inspect');
  const supportsTreeReadback = backend.supports('project.tree.load');
  const validProjectOwner = Boolean(context.projectPath && isAbsoluteServerPath(context.projectPath) && context.projectXml &&
    isProjectTreeSHA256(context.projectRevision) && context.projectSessionID && !context.projectPath.startsWith('browser-file://'));
  const hasUncaptured = hasUncapturedSessionReferences(context, context.selectedAsset) ||
    Boolean(context.coreLayerContext && !exactCoreLayerContext(context, context.selectedAsset));
  const canInspect = open && validProjectOwner && supportsInspect && supportsFileInspect && !hasUncaptured && !hasUnsavedDrafts;
  const fingerprintMatches = Boolean(inspection && inspectedName === name && ownerAtInspect.current === owner);
  const canCreate = Boolean(canInspect && supportsCreate && supportsTreeReadback && onContextCommit && fingerprintMatches &&
    inspection?.canExecute && busy === null);

  const invalidate = useCallback((): void => {
    generation.current += 1;
    activeRequest.current?.abort();
    activeRequest.current = undefined;
    setBusy(null);
    setInspection(null);
    setInspectedName(undefined);
    ownerAtInspect.current = undefined;
    setError(undefined);
    setDetails(undefined);
    setNotice(undefined);
  }, []);

  const cancel = useCallback((): void => {
    const wasCreating = busy === 'create';
    invalidate();
    if (wasCreating) {
      setNotice(language === 'zh'
        ? '已停止等待本次回执。服务端可能已完成文件写入；请重新检查目录，不要重复创建同名项。'
        : 'Waiting for this receipt has stopped. The server may already have written the file; inspect the catalog before retrying with the same name.');
    }
  }, [busy, invalidate, language]);

  const isCurrent = (id: number, signal: AbortSignal, source: WorkbenchContext): boolean =>
    generation.current === id && !signal.aborted && ownerFingerprint(contextRef.current) === ownerFingerprint(source);

  const inspect = useCallback(async (nameOverride?: string): Promise<void> => {
    const source = contextRef.current;
    const requestedName = nameOverride ?? nameRef.current;
    const sourceOwner = ownerFingerprint(source);
    if (!canInspect) {
      setError(language === 'zh' ? '请先打开已保存的服务器工程、处理未保存草稿，并确认服务提供来源检查与目录读回。' : 'Open a saved server project, resolve unsaved drafts, and confirm source inspection and tree read-back are available.');
      return;
    }
    if (!requestedName || requestedName !== requestedName.trim()) {
      setError(language === 'zh' ? '请输入不带首尾空格的名称。' : 'Enter a name without leading or trailing spaces.');
      return;
    }
    if (hasUncapturedSessionReferences(source, source.selectedAsset)) {
      setError(language === 'zh' ? '当前资产包含未捕获的会话专属引用，无法安全创建。' : 'The selected asset has uncaptured session-only references, so creation cannot proceed safely.');
      return;
    }
    const id = ++generation.current;
    activeRequest.current?.abort();
    const controller = new AbortController();
    activeRequest.current = controller;
    setBusy('inspect');
    setInspection(null);
    setInspectedName(undefined);
    setError(undefined);
    setDetails(undefined);
    setNotice(undefined);
    setFileCreated(undefined);
    setCapturedAsset(undefined);
    const coreLayerContext = exactCoreLayerContext(source, source.selectedAsset);
    const request = {
      kind, projectPath: source.projectPath!, projectXml: source.projectXml!, name: requestedName,
      ...(coreLayerContext ? { coreLayerContext } : {}),
    };
    try {
      const projectCaptureRaw: unknown = await backend.request('workspace.file.inspect', { sourcePath: source.projectPath }, { signal: controller.signal });
      if (!isCurrent(id, controller.signal, source)) return;
      if (!isWorkspaceFileInspection(projectCaptureRaw, source.projectPath!) ||
          !sameServerPath(projectCaptureRaw.canonicalPath, source.projectPath!) ||
          projectCaptureRaw.revision.toUpperCase() !== source.projectRevision!.toUpperCase()) {
        throw new Error(language === 'zh' ? '当前工程文件路径或 SHA-256 已变化；请重新打开工程后再创建。' : 'The project file path or SHA-256 changed. Reopen the project before creating the layer.');
      }
      const resultRaw: unknown = await backend.request('surfaceCreation.inspect', request, { signal: controller.signal });
      if (!isCurrent(id, controller.signal, source)) return;
      validateInspection(resultRaw, kind);
      const result = resultRaw;
      setInspection(result);
      setInspectedName(requestedName);
      ownerAtInspect.current = sourceOwner;
      if (result.suggestedName !== requestedName) {
        setNotice(language === 'zh' ? `服务建议的可用名称是“${result.suggestedName}”。可采用后重新检查。` : `The service suggests the available name “${result.suggestedName}”. Apply it and inspect again.`);
      }
    } catch (caught) {
      if (generation.current !== id || controller.signal.aborted) return;
      setError(`${localizedFailure(caught, language, '服务无法完成名称与位置检查。')}${language === 'zh' ? ' 展开查看原始诊断。' : ' Expand to view raw diagnostics.'}`);
      setDetails(rawDiagnosticText(caught));
    } finally {
      if (generation.current === id && !controller.signal.aborted) {
        activeRequest.current = undefined;
        setBusy(null);
      }
    }
  }, [backend, canInspect, kind, language]);

  const create = useCallback(async (): Promise<void> => {
    if (!canCreate || !inspection || inspectedName !== nameRef.current) return;
    const source = contextRef.current;
    const sourceOwner = ownerFingerprint(source);
    const nameAtRequest = nameRef.current;
    const inspectionAtRequest = inspection;
    const id = ++generation.current;
    activeRequest.current?.abort();
    const controller = new AbortController();
    activeRequest.current = controller;
    setBusy('create');
    setError(undefined);
    setDetails(undefined);
    setNotice(undefined);
    setFileCreated(undefined);
    setCapturedAsset(undefined);
    const coreLayerContext = exactCoreLayerContext(source, source.selectedAsset);
    const request = {
      kind, projectPath: source.projectPath!, projectXml: source.projectXml!, name: nameAtRequest,
      revision: inspectionAtRequest.revision,
      ...(coreLayerContext ? { coreLayerContext } : {}),
    };
    const zh = language === 'zh';
    try {
      const projectCaptureRaw: unknown = await backend.request('workspace.file.inspect', { sourcePath: source.projectPath }, { signal: controller.signal });
      if (!isCurrent(id, controller.signal, source)) return;
      if (!isWorkspaceFileInspection(projectCaptureRaw, source.projectPath!) ||
          !sameServerPath(projectCaptureRaw.canonicalPath, source.projectPath!) ||
          projectCaptureRaw.revision.toUpperCase() !== source.projectRevision!.toUpperCase()) {
        throw new Error(zh ? '工程文件 SHA-256 已变化；没有发送创建请求。' : 'The project-file SHA-256 changed. No creation request was sent.');
      }
      const createdRaw: unknown = await backend.request('surfaceCreation.create', request, { signal: controller.signal });
      if (!isCurrent(id, controller.signal, source)) return;
      validateCreated(createdRaw, kind, nameAtRequest, inspectionAtRequest.targetDirectory);
      const created = createdRaw;
      setFileCreated(created);
      const assetCaptureRaw: unknown = await backend.request('workspace.file.inspect', { sourcePath: created.assetPath }, { signal: controller.signal });
      if (!isCurrent(id, controller.signal, source)) return;
      if (!isWorkspaceFileInspection(assetCaptureRaw, created.assetPath) ||
          !sameServerPath(assetCaptureRaw.canonicalPath, created.assetPath) || assetCaptureRaw.length <= 0) {
        throw new Error(zh ? 'HDF 文件已创建，但服务没有返回匹配的规范路径、SHA-256 和文件长度；尚未确认目录成员。' : 'The HDF file was created, but the service did not return a matching canonical path, SHA-256, and length. Catalog membership is not confirmed.');
      }
      setCapturedAsset(assetCaptureRaw);
      const projectAfterRaw: unknown = await backend.request('workspace.file.inspect', { sourcePath: source.projectPath }, { signal: controller.signal });
      if (!isCurrent(id, controller.signal, source)) return;
      if (!isWorkspaceFileInspection(projectAfterRaw, source.projectPath!) ||
          projectAfterRaw.revision.toUpperCase() !== source.projectRevision!.toUpperCase()) {
        throw new Error(zh ? 'HDF 已创建，但工程文件在操作期间发生变化；没有将新资产设为当前选择。' : 'The HDF was created, but the project file changed during the operation. The new asset was not selected.');
      }
      if (!backend.supports('project.tree.load')) throw new Error(zh ? '工程目录读回能力不可用；新资产尚未验证。' : 'Project tree read-back is unavailable; the new asset is not verified.');
      const readbackRaw: unknown = await backend.request<CatalogTreeSnapshot>('project.tree.load', {
        projectPath: source.projectPath,
        projectXml: source.projectXml,
        projectSessionID: source.projectSessionID,
        mode: 'Project',
        ...(coreLayerContext ? { coreLayerContext } : {}),
      }, { signal: controller.signal });
      if (!isCurrent(id, controller.signal, source)) return;
      const node = findCreatedNode(readbackRaw as CatalogTreeSnapshot, kind, created, assetCaptureRaw.canonicalPath);
      if (generation.current !== id || controller.signal.aborted || ownerFingerprint(contextRef.current) !== sourceOwner || nameRef.current !== nameAtRequest) return;
      if (!onContextCommit?.(source, source)) {
        setNotice(zh
          ? `已创建并读回“${created.name}”，但父级工程会话拒绝了原 context 确认；未切换当前选择。`
          : `Created and read back “${created.name}”, but the parent project session rejected the source-context check. The selection was not changed.`);
        return;
      }
      const kindLabel = kind === 'newTerrain' ? 'Terrain' : 'Surface Layers';
      const selectedContext: WorkbenchContext = {
        ...source,
        selectedAsset: {
          path: assetCaptureRaw.canonicalPath,
          kind: kindLabel,
          name: node.name,
          revision: assetCaptureRaw.revision,
          locator: node.locator ?? undefined,
        },
      };
      const accepted = onCreated(source, { created, asset: assetCaptureRaw, node }, selectedContext);
      if (!accepted) {
        setNotice(zh ? `“${created.name}”已通过目录读回，但当前工程上下文没有接受新的资产选择。` : `“${created.name}” was read back from the catalog, but the current project context did not accept the new asset selection.`);
        return;
      }
      if (generation.current !== id || controller.signal.aborted || ownerFingerprint(contextRef.current) !== sourceOwner) return;
      setNotice(zh ? `已创建“${created.name}”并通过工程目录读回核验。` : `Created “${created.name}” and verified it in the project catalog.`);
    } catch (caught) {
      if (generation.current !== id || controller.signal.aborted) return;
      setError(`${localizedFailure(caught, language, '服务未能完成创建或工程目录核验。')}${zh ? ' 若文件已写入，请核对下方路径与 SHA-256；展开查看原始诊断。' : ' If a file was written, verify the path and SHA-256 below; expand for raw diagnostics.'}`);
      setDetails(rawDiagnosticText(caught));
    } finally {
      if (generation.current === id && !controller.signal.aborted) {
        activeRequest.current = undefined;
        setBusy(null);
      }
    }
  }, [backend, canCreate, inspection, inspectedName, kind, language, onContextCommit, onCreated]);

  useEffect(() => {
    if (open) {
      invalidate();
      return;
    }
    generation.current += 1;
    activeRequest.current?.abort();
    activeRequest.current = undefined;
    setInspection(null);
    setInspectedName(undefined);
    setBusy(null);
    setError(undefined);
    setDetails(undefined);
    setNotice(undefined);
    setFileCreated(undefined);
    setCapturedAsset(undefined);
    ownerAtInspect.current = undefined;
  }, [context.projectPath, context.projectSessionID, context.projectRevision, context.projectXml,
    context.selectedAsset?.path, context.selectedAsset?.kind, context.selectedAsset?.revision,
    context.selectedAsset?.locator, context.coreLayerContext, invalidate, kind, open]);

  return { inspection, inspectedName, busy, error, details, notice, fileCreated, capturedAsset, canCreate, inspect, create, invalidate, cancel };
}
