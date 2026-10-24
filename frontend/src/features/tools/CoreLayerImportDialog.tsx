import { useEffect, useRef, useState } from 'react';
import type { AssetReference, BackendGateway, Language, WorkbenchContext } from '../../shared/workbench';
import { message, text } from '../../shared/workbench';
import { projectXmlSha } from '../map/ProjectTreeListener';
import type { MapTreeSnapshot } from '../map/ProjectTreeListener';
import type { CatalogTreeNode } from '../map/mapTypes';
import type { CoreLayerImportInspection, CoreLayerImportReceipt, CoreLayerImportRequest, WorkspaceFileInspection } from './coreImportWorkflow';
import { isSha256, sourceOwnerKey } from './coreImportWorkflow';
import type { CoreLayerMapChoice, CoreLayerReferenceCapture, CoreLayerRequestContext, SessionReferenceMutationCallbacks, WorkspaceReferencesComposeReceipt, WorkspaceReferencesComposeRequest } from './core-layer-import/coreLayerReferenceTypes';
import './coreImportWorkflow.css';

interface Props extends SessionReferenceMutationCallbacks {
  open: boolean;
  language: Language;
  context: WorkbenchContext;
  source: AssetReference | null;
  backend: BackendGateway;
  activeMapLabel?: string | null;
  hasUnsavedDrafts?: boolean;
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

interface CoreLayerMapCatalog {
  projectRevision: string;
  visualizationRevision: string | null;
  maps: CoreLayerMapChoice[];
  activeMapIndex: number | null;
}

interface CoreLayerReferenceSuccess {
  kind: 'geometry' | 'boundaryCondition';
  name: string;
  sourcePath: string;
  sourceRevision: string;
  mapName: string;
  previousVisualizationRevision: string;
  visualizationRevision: string;
}

function contextCoreLayerContext(context: WorkbenchContext): CoreLayerRequestContext | undefined {
  const raw = context.coreLayerContext;
  if (raw === undefined || raw === null) return undefined;
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) throw new Error('The active CoreLayerContext is malformed. Refresh the project session before importing a reference.');
  const value = raw as Partial<CoreLayerRequestContext>;
  if (value.projectPath !== context.projectPath || value.projectSessionID !== context.projectSessionID ||
      typeof value.identityToken !== 'string' || !value.identityToken.trim() || !Array.isArray(value.references) ||
      typeof value.projectRevision !== 'string' ||
      (value.sessionHdfReferences !== undefined && !Array.isArray(value.sessionHdfReferences)) ||
      value.projectRevision.toUpperCase() !== context.projectRevision?.toUpperCase()) {
    throw new Error('The active CoreLayerContext does not match the current project, session, or revision. Refresh the project session before importing a reference.');
  }
  return value as CoreLayerRequestContext;
}

function mapKey(map: CoreLayerMapChoice): string {
  return JSON.stringify([map.mapIndex, map.mapName, map.mapType]);
}

function supportedReferenceMap(map: CoreLayerMapChoice): boolean {
  return ['Project', 'Buildable', 'Geometry', 'Plan'].includes(map.mapType);
}

function validMapCatalog(value: unknown, expectedProjectRevision: string): value is CoreLayerMapCatalog {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const catalog = value as Partial<CoreLayerMapCatalog>;
  if (!(typeof catalog.projectRevision === 'string' &&
    catalog.projectRevision.toUpperCase() === expectedProjectRevision.toUpperCase() &&
    (catalog.visualizationRevision === null || (typeof catalog.visualizationRevision === 'string' && isSha256(catalog.visualizationRevision))) &&
    Array.isArray(catalog.maps) &&
    (catalog.activeMapIndex === null || (Number.isInteger(catalog.activeMapIndex) && (catalog.activeMapIndex as number) >= 0)) &&
    catalog.maps.every((map) => map && Number.isInteger(map.mapIndex) && map.mapIndex >= 0 &&
      typeof map.mapName === 'string' && !!map.mapName.trim() && typeof map.mapType === 'string' && !!map.mapType.trim()) &&
    (catalog.activeMapIndex === null || catalog.maps.some(map => map.mapIndex === catalog.activeMapIndex)) &&
    (catalog.maps.length === 0 || catalog.visualizationRevision !== null))) return false;
  return new Set(catalog.maps.map(map => map.mapIndex)).size === catalog.maps.length;
}

function normalizedPath(value: string): string {
  const normalized = value.replaceAll('\\', '/').replace(/\/+$/, '');
  return /^[a-z]:\//i.test(normalized) || value.startsWith('\\\\') ? normalized.toLocaleLowerCase('en-US') : normalized;
}

function sameSourcePath(candidate: string | null | undefined, sourcePath: string, canonicalPath: string): boolean {
  if (!candidate) return false;
  const normalized = normalizedPath(candidate);
  return normalized === normalizedPath(sourcePath) || normalized === normalizedPath(canonicalPath);
}

function flattenNodes(nodes: readonly CatalogTreeNode[]): CatalogTreeNode[] {
  const result: CatalogTreeNode[] = [];
  for (const node of nodes) {
    result.push(node);
    if (Array.isArray(node.children)) result.push(...flattenNodes(node.children));
  }
  return result;
}

function validTreeNodes(value: unknown): value is CatalogTreeNode[] {
  if (!Array.isArray(value)) return false;
  return value.every(node => node && typeof node === 'object' && !Array.isArray(node) &&
    typeof node.id === 'string' && typeof node.sourceClass === 'string' && typeof node.name === 'string' &&
    (node.children === undefined || node.children === null || validTreeNodes(node.children)));
}

function validMapTreeSnapshot(value: unknown): value is MapTreeSnapshot {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const snapshot = value as Partial<MapTreeSnapshot>;
  return validTreeNodes(snapshot.roots) &&
    (snapshot.previousVisualizationRevision === null || isSha256(snapshot.previousVisualizationRevision)) &&
    isSha256(snapshot.visualizationRevision) &&
    Array.isArray(snapshot.expandedNodeIDs) && snapshot.expandedNodeIDs.every(id => typeof id === 'string') &&
    (snapshot.selectedNodeID === null || typeof snapshot.selectedNodeID === 'string') &&
    Array.isArray(snapshot.hiddenLayerIDs) && snapshot.hiddenLayerIDs.every(id => typeof id === 'string');
}

function validReferenceCapture(value: unknown, receipt: CoreLayerImportReceipt, kind: 'geometry' | 'boundaryCondition', payload: CoreLayerImportRequest): value is CoreLayerReferenceCapture {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const reference = value as Partial<CoreLayerReferenceCapture>;
  return typeof reference.referenceID === 'string' && !!reference.referenceID.trim() &&
    reference.projectSessionID === payload.projectSessionID && reference.projectPath === receipt.projectPath &&
    reference.kind === kind && reference.sourcePath === receipt.sourcePath &&
    reference.canonicalSourcePath === payload.expectedSourceCanonicalPath &&
    typeof reference.sourceRevision === 'string' && reference.sourceRevision.toUpperCase() === payload.sourceRevision.toUpperCase() &&
    reference.name === receipt.name && reference.sessionOnly === true;
}

function validComposedContext(
  value: unknown,
  sourceContext: WorkbenchContext,
  previous: CoreLayerRequestContext | undefined,
  reference: CoreLayerReferenceCapture,
): value is WorkspaceReferencesComposeReceipt {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const receipt = value as Partial<WorkspaceReferencesComposeReceipt>;
  const next = receipt.nextContext;
  if (receipt.projectPath !== sourceContext.projectPath || receipt.projectSessionID !== sourceContext.projectSessionID ||
      receipt.projectRevision?.toUpperCase() !== sourceContext.projectRevision?.toUpperCase() ||
      (previous ? receipt.previousContextIdentityToken !== previous.identityToken :
        (receipt.previousContextIdentityToken !== null && receipt.previousContextIdentityToken !== ''))) return false;
  if (!next || next.projectPath !== sourceContext.projectPath || next.projectSessionID !== sourceContext.projectSessionID ||
      next.projectRevision?.toUpperCase() !== sourceContext.projectRevision?.toUpperCase() ||
      typeof next.identityToken !== 'string' || !next.identityToken.trim() || !Array.isArray(next.references) ||
      (next.sessionHdfReferences !== undefined && !Array.isArray(next.sessionHdfReferences))) return false;
  const sameCoreReference = (left: Partial<CoreLayerReferenceCapture>, right: Partial<CoreLayerReferenceCapture>): boolean =>
    left.referenceID === right.referenceID && left.projectSessionID === right.projectSessionID &&
    left.projectPath === right.projectPath && left.kind === right.kind && left.sourcePath === right.sourcePath &&
    left.canonicalSourcePath === right.canonicalSourcePath &&
    typeof left.sourceRevision === 'string' && typeof right.sourceRevision === 'string' &&
    left.sourceRevision.toUpperCase() === right.sourceRevision.toUpperCase() &&
    left.name === right.name && left.sessionOnly === right.sessionOnly;
  const preservesPreviousCore = (previous?.references ?? []).every(oldReference =>
    next.references.some(candidate => candidate && typeof candidate === 'object' && sameCoreReference(oldReference, candidate)));
  const preservesPreviousHdf = (previous?.sessionHdfReferences ?? []).every(oldReference => {
    if (!oldReference || typeof oldReference !== 'object' || Array.isArray(oldReference)) return false;
    const old = oldReference as Record<string, unknown>;
    return next.sessionHdfReferences?.some(candidate => {
      if (!candidate || typeof candidate !== 'object' || Array.isArray(candidate)) return false;
      const value = candidate as Record<string, unknown>;
      return value.referenceID === old.referenceID && value.projectSessionID === old.projectSessionID &&
        value.projectPath === old.projectPath && value.kind === old.kind && value.sourcePath === old.sourcePath &&
        value.canonicalSourcePath === old.canonicalSourcePath && value.sourceRevision === old.sourceRevision &&
        value.name === old.name && value.resultPlanName === old.resultPlanName && value.sessionOnly === old.sessionOnly;
    }) ?? false;
  });
  const addedReferenceCount = next.references.filter((candidate) => candidate && typeof candidate === 'object' &&
    typeof candidate.referenceID === 'string' && candidate.referenceID === reference.referenceID &&
    candidate.projectSessionID === reference.projectSessionID && candidate.projectPath === reference.projectPath &&
    candidate.kind === reference.kind && candidate.sourcePath === reference.sourcePath &&
    candidate.canonicalSourcePath === reference.canonicalSourcePath &&
    typeof candidate.sourceRevision === 'string' &&
    candidate.sourceRevision.toUpperCase() === reference.sourceRevision.toUpperCase() &&
    candidate.name === reference.name && candidate.sessionOnly === true).length;
  return preservesPreviousCore && preservesPreviousHdf && addedReferenceCount === 1;
}

export function CoreLayerImportDialog({
  open, language, context, source, backend, activeMapLabel, hasUnsavedDrafts = false, onClose,
  onBeginSessionReferenceMutation, onCommitSessionReferences, onEndSessionReferenceMutation,
}: Props) {
  const zh = language === 'zh';
  const owner = sourceOwnerKey(context);
  const coreContextToken = (context.coreLayerContext as unknown as CoreLayerRequestContext | null | undefined)?.identityToken ?? '';
  const requestOwner = JSON.stringify([owner, coreContextToken]);
  const ownerRef = useRef(owner);
  const requestOwnerRef = useRef(requestOwner);
  ownerRef.current = owner;
  requestOwnerRef.current = requestOwner;
  const generation = useRef(0);
  const controller = useRef<AbortController | null>(null);
  const mapWriteStarted = useRef(false);
  const preserveNextCoreContextToken = useRef<string | null>(null);
  const [path, setPath] = useState(source?.path ?? '');
  const [capture, setCapture] = useState<WorkspaceFileInspection>();
  const [inspection, setInspection] = useState<CoreLayerImportInspection>();
  const [inspectionOwner, setInspectionOwner] = useState('');
  const [receipt, setReceipt] = useState<CoreLayerImportReceipt>();
  const [referenceReceipt, setReferenceReceipt] = useState<CoreLayerReferenceSuccess>();
  const [mapCatalog, setMapCatalog] = useState<CoreLayerMapCatalog>();
  const [selectedMapKey, setSelectedMapKey] = useState('');
  const [busy, setBusy] = useState<'inspect' | 'copy' | 'maps' | 'reference' | 'map-write' | null>(null);
  const [error, setError] = useState<string>();
  const [rawDiagnostic, setRawDiagnostic] = useState<string>();
  const [cancelNote, setCancelNote] = useState<string>();

  const projectReady = Boolean(context.projectPath && context.projectRevision && context.projectXml !== null && context.projectSessionID);
  const canInspect = projectReady && isAbsoluteServerPath(path.trim()) &&
    backend.supports('workspace.file.inspect') && backend.supports('workspace.import.coreLayer.inspect');
  const canCopy = Boolean(inspection && inspection.availableActions.includes('copyFile') &&
    backend.supports('workspace.import.coreLayer.perform') && ownerRef.current === owner && inspectionOwner === requestOwner);
  const selectedMap = mapCatalog?.maps.find(map => mapKey(map) === selectedMapKey);
  const hasReferenceCallbacks = Boolean(onBeginSessionReferenceMutation && onCommitSessionReferences && onEndSessionReferenceMutation);
  const canAddReference = Boolean(
    projectReady && inspection && capture && inspectionOwner === requestOwner && inspection.availableActions.includes('addReference') &&
    inspection.referenceIsSessionOnly && selectedMap && supportedReferenceMap(selectedMap) &&
    !hasUnsavedDrafts &&
    backend.supports('workspace.import.coreLayer.perform') && backend.supports('workspace.references.compose') &&
    backend.supports('map.document.list') && backend.supports('map.tree.load') && backend.supports('map.tree.perform') &&
    hasReferenceCallbacks && requestOwnerRef.current === requestOwner,
  );
  const capability = {
    inspect: backend.supports('workspace.import.coreLayer.inspect'),
    perform: backend.supports('workspace.import.coreLayer.perform'),
    compose: backend.supports('workspace.references.compose'),
    mapList: backend.supports('map.document.list'),
    mapTreeLoad: backend.supports('map.tree.load'),
    mapTreePerform: backend.supports('map.tree.perform'),
  };

  useEffect(() => {
    if (!open) return;
    if (preserveNextCoreContextToken.current && preserveNextCoreContextToken.current === coreContextToken) {
      preserveNextCoreContextToken.current = null;
      return;
    }
    preserveNextCoreContextToken.current = null;
    controller.current?.abort();
    generation.current += 1;
    setPath(source?.path ?? '');
    setCapture(undefined);
    setInspection(undefined);
    setInspectionOwner('');
    setReceipt(undefined);
    setReferenceReceipt(undefined);
    setMapCatalog(undefined);
    setSelectedMapKey('');
    setBusy(null);
    setError(undefined);
    setRawDiagnostic(undefined);
    setCancelNote(undefined);
  }, [open, owner, coreContextToken]);

  useEffect(() => () => {
    generation.current += 1;
    if (!mapWriteStarted.current) controller.current?.abort();
  }, []);

  if (!open) return null;

  const changePath = (value: string): void => {
    controller.current?.abort();
    generation.current += 1;
    setBusy(null);
    setPath(value);
    setCapture(undefined);
    setInspection(undefined);
    setInspectionOwner('');
    setReceipt(undefined);
    setReferenceReceipt(undefined);
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
    generation.current === id && ownerRef.current === owner && requestOwnerRef.current === requestOwner && !signal.aborted;

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
    const exactContext = contextCoreLayerContext(context);
    return {
      projectPath: context.projectPath,
      projectSessionID: context.projectSessionID,
      projectRevision: context.projectRevision,
      projectXml: context.projectXml,
      projectXmlRevision,
      sourcePath: path.trim(),
      expectedSourceCanonicalPath: sourceCapture.canonicalPath,
      sourceRevision: sourceCapture.revision.trim().toUpperCase(),
      ...(exactContext ? { coreLayerContext: exactContext as unknown as Record<string, unknown> } : {}),
    };
  };

  const inspect = async (): Promise<void> => {
    const request = begin();
    setBusy('inspect');
    setError(undefined);
    setRawDiagnostic(undefined);
    setCancelNote(undefined);
    setInspection(undefined);
    setInspectionOwner('');
    setReceipt(undefined);
    setReferenceReceipt(undefined);
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
          result.sourcePath !== payload.sourcePath || result.canonicalSourcePath !== payload.expectedSourceCanonicalPath || result.sourceRevision.toUpperCase() !== payload.sourceRevision.toUpperCase() ||
          result.fileSize !== sourceCapture.length ||
          !Array.isArray(result.availableActions) || !result.availableActions.includes('copyFile') ||
          !['geometry', 'boundary conditions', 'boundarycondition'].includes(result.kind.toLowerCase())) {
        throw new Error(text(language, '核心图层检查回执与当前来源或工程会话不匹配。', 'The inspection receipt does not match the current source or project session.'));
      }
      setInspection(result);
      setInspectionOwner(requestOwner);
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
      if (!inspection || inspectionOwner !== requestOwner || !backend.supports('workspace.import.coreLayer.perform')) {
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
      setInspectionOwner('');
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

  const readMapCatalog = async (): Promise<void> => {
    const request = begin();
    setBusy('maps');
    setError(undefined);
    setRawDiagnostic(undefined);
    setCancelNote(undefined);
    try {
      if (!projectReady || !backend.supports('map.document.list')) {
        throw new Error(text(language, '读取地图需要已保存工程和 map.document.list。', 'Reading maps requires a saved project and map.document.list.'));
      }
      const xmlRevision = await projectXmlSha(context.projectXml);
      if (!xmlRevision) throw new Error(text(language, '当前工程 XML 快照无法校验。', 'The current project XML snapshot cannot be verified.'));
      const listed = await backend.request<unknown>('map.document.list', {
        projectPath: context.projectPath,
        projectXml: context.projectXml,
        projectXmlSHA: xmlRevision,
        expectedProjectRevision: context.projectRevision,
      }, { signal: request.signal });
      if (!isCurrent(request.id, request.signal)) return;
      if (!validMapCatalog(listed, context.projectRevision!)) {
        throw new Error(text(language, '地图目录回执与当前工程修订不匹配。', 'The map catalog receipt does not match the active project revision.'));
      }
      setMapCatalog(listed);
      setSelectedMapKey('');
      if (!listed.maps.some(supportedReferenceMap)) {
        setError(text(language, '当前工程没有可添加 Geometry 或 Boundary Condition 的受支持地图。', 'The project has no map that accepts a Geometry or Boundary Condition reference.'));
      }
    } catch (caught) {
      if (request.signal.aborted && generation.current === request.id) {
        setCancelNote(text(language, '地图目录读取已取消，未保留部分回执。', 'Map-catalog reading was cancelled without retaining a partial receipt.'));
      } else if (isCurrent(request.id, request.signal)) {
        setError(message(caught, language));
        setRawDiagnostic(caught instanceof Error ? caught.message : String(caught));
      }
    } finally {
      if (generation.current === request.id) setBusy(null);
    }
  };

  const addReference = async (): Promise<void> => {
    const request = begin();
    setBusy('reference');
    setError(undefined);
    setRawDiagnostic(undefined);
    setCancelNote(undefined);
    setReferenceReceipt(undefined);
    let leaseID: string | null = null;
    try {
      if (!inspection || inspectionOwner !== requestOwner || !capture || !selectedMap || !supportedReferenceMap(selectedMap) ||
          hasUnsavedDrafts ||
          !onBeginSessionReferenceMutation || !onCommitSessionReferences || !onEndSessionReferenceMutation ||
          !backend.supports('workspace.import.coreLayer.perform') ||
          !backend.supports('workspace.references.compose') ||
          !backend.supports('map.document.list') || !backend.supports('map.tree.load') ||
          !backend.supports('map.tree.perform')) {
        throw new Error(text(language, '添加引用所需的检查、会话注册、地图目录和地图树操作尚未全部接通。', 'Inspection, session registration, map catalog, and map-tree capabilities are all required to add a reference.'));
      }
      if (!inspection.availableActions.includes('addReference') || !inspection.referenceIsSessionOnly) {
        throw new Error(text(language, '当前来源检查没有确认可添加的会话引用。', 'The inspection did not confirm an available session reference.'));
      }
      const sourceCapture = capture;
      if (!isFileInspection(sourceCapture, path.trim())) throw new Error(text(language, '来源路径捕获已失效，请重新检查。', 'The source path capture is stale. Inspect it again.'));
      const payload = await buildRequest(sourceCapture);
      if (inspection.projectPath !== payload.projectPath || inspection.projectSessionID !== payload.projectSessionID ||
          inspection.projectRevision.toUpperCase() !== payload.projectRevision.toUpperCase() ||
          inspection.projectXmlRevision.toUpperCase() !== payload.projectXmlRevision.toUpperCase() ||
          inspection.canonicalSourcePath !== payload.expectedSourceCanonicalPath ||
          inspection.sourceRevision.toUpperCase() !== payload.sourceRevision.toUpperCase()) {
        throw new Error(text(language, '检查后来源或工程上下文已变化，请重新检查。', 'The source or project context changed after inspection. Inspect it again.'));
      }
      const kind: 'geometry' | 'boundaryCondition' = inspection.kind.toLowerCase() === 'geometry'
        ? 'geometry'
        : inspection.kind.toLowerCase() === 'boundarycondition' || inspection.kind.toLowerCase() === 'boundary condition'
          ? 'boundaryCondition'
          : (() => { throw new Error(text(language, '检查回执不是 Geometry 或 Boundary Condition。', 'The inspection receipt is not a Geometry or Boundary Condition.')); })();

      // The parent lease keeps this exact project/session owner active until
      // the map-tree receipt has been accepted into the shared registry.
      leaseID = onBeginSessionReferenceMutation(context);
      if (!leaseID || !leaseID.trim()) throw new Error(text(language, '当前工程会话未授予引用事务租约。', 'The active project session did not grant a reference-mutation lease.'));

      const performed = await backend.request<CoreLayerImportReceipt>('workspace.import.coreLayer.perform', {
        ...payload,
        action: 'addReference',
      }, { signal: request.signal });
      if (!isCurrent(request.id, request.signal)) return;
      if (!performed || performed.action !== 'addReference' || performed.projectPath !== payload.projectPath ||
          performed.projectSessionID !== payload.projectSessionID ||
          performed.projectRevision.toUpperCase() !== payload.projectRevision.toUpperCase() ||
          performed.projectXmlRevision.toUpperCase() !== payload.projectXmlRevision.toUpperCase() ||
          performed.sourcePath !== payload.sourcePath ||
          performed.canonicalSourcePath !== payload.expectedSourceCanonicalPath ||
          performed.sourceRevision.toUpperCase() !== payload.sourceRevision.toUpperCase() ||
          performed.kind !== kind || performed.referenceIsSessionOnly !== true ||
          !validReferenceCapture(performed.reference, performed, kind, payload)) {
        throw new Error(text(language, 'Add Reference 回执与来源文件或当前工程会话不匹配。', 'The Add Reference receipt does not match the source file or active project session.'));
      }
      const reference = performed.reference as CoreLayerReferenceCapture;

      const capturedCore = contextCoreLayerContext(context);
      const composeRequest: WorkspaceReferencesComposeRequest = {
        projectPath: payload.projectPath,
        projectSessionID: payload.projectSessionID,
        projectRevision: payload.projectRevision,
        projectXml: payload.projectXml,
        projectXmlRevision: payload.projectXmlRevision,
        ...(capturedCore ? { coreLayerContext: capturedCore } : {}),
        addCoreReferences: [reference],
      };
      const composed = await backend.request<unknown>('workspace.references.compose', composeRequest, { signal: request.signal });
      if (!isCurrent(request.id, request.signal)) return;
      if (!validComposedContext(composed, context, payload.coreLayerContext as CoreLayerRequestContext | undefined, reference)) {
        throw new Error(text(language, '会话引用注册回执没有闭合当前工程身份、上下文令牌和来源捕获。', 'The session-reference composition receipt does not match the project, context token, and source capture.'));
      }
      const composedReceipt = composed as WorkspaceReferencesComposeReceipt;
      const nextContext = composedReceipt.nextContext;

      // Re-list and load the exact selected map with the newly composed
      // context immediately before performing the saved .vis map-tree action.
      const selectedIdentity = mapKey(selectedMap);
      const xmlRevision = await projectXmlSha(payload.projectXml);
      if (!xmlRevision) throw new Error(text(language, '当前工程 XML 快照无法校验。', 'The current project XML snapshot cannot be verified.'));
      const freshCatalog = await backend.request<unknown>('map.document.list', {
        projectPath: payload.projectPath,
        projectXml: payload.projectXml,
        projectXmlSHA: xmlRevision,
        expectedProjectRevision: payload.projectRevision,
      }, { signal: request.signal });
      if (!isCurrent(request.id, request.signal)) return;
      if (!validMapCatalog(freshCatalog, payload.projectRevision)) {
        throw new Error(text(language, '地图目录在检查后发生变化，请重新读取地图。', 'The map catalog changed after inspection. Read the maps again.'));
      }
      const exactMap = freshCatalog.maps.find(map => mapKey(map) === selectedIdentity);
      if (!exactMap || !supportedReferenceMap(exactMap) || !isSha256(freshCatalog.visualizationRevision)) {
        throw new Error(text(language, '所选地图已不存在、类型已变化或缺少可视化修订，请重新选择。', 'The selected map is gone, has changed type, or has no visualization revision. Select it again.'));
      }
      const mapRequest = {
        projectPath: payload.projectPath,
        projectXml: payload.projectXml,
        projectXmlSHA: xmlRevision,
        mapIndex: exactMap.mapIndex,
        mapName: exactMap.mapName,
        mapType: exactMap.mapType,
        expectedVisualizationRevision: freshCatalog.visualizationRevision,
        projectSessionID: payload.projectSessionID,
        externalCoreLayerReferences: nextContext.references,
        coreLayerContext: nextContext,
      };
      const loadedTree = await backend.request<MapTreeSnapshot>('map.tree.load', mapRequest, { signal: request.signal });
      if (!isCurrent(request.id, request.signal)) return;
      if (!validMapTreeSnapshot(loadedTree) ||
          loadedTree.visualizationRevision.toUpperCase() !== freshCatalog.visualizationRevision.toUpperCase()) {
        throw new Error(text(language, '所选地图树与刚读取的可视化修订不一致。', 'The selected map tree does not match the freshly read visualization revision.'));
      }

      if (!isCurrent(request.id, request.signal)) return;
      setBusy('map-write');
      mapWriteStarted.current = true;
      const action = kind === 'geometry' ? 'addGeometry' : 'addBoundaryCondition';
      const mapReceipt = await backend.request<MapTreeSnapshot>('map.tree.perform', {
        map: mapRequest,
        action,
        sourcePath: reference.sourcePath,
        sourceRevision: reference.sourceRevision,
      });
      const previousVisualizationRevision = loadedTree.visualizationRevision;
      if (!validMapTreeSnapshot(mapReceipt) ||
          typeof mapReceipt.previousVisualizationRevision !== 'string' ||
          mapReceipt.previousVisualizationRevision.toUpperCase() !== previousVisualizationRevision.toUpperCase() ||
          !isSha256(mapReceipt.visualizationRevision)) {
        throw new Error(text(language, '地图树操作没有返回匹配的可视化修订和树快照，不能确认引用已加入。', 'The map-tree action did not return a matching visualization revision and tree snapshot; the reference cannot be confirmed.'));
      }
      const expectedNodeTypes = kind === 'geometry'
        ? new Set(['ObservableGeometry', 'GeometryNode', 'GeometryNodeVM'])
        : new Set(['ObservableBoundaryCondition', 'BoundaryConditionNode', 'BoundaryConditionNodeVM']);
      const matchingNodes = flattenNodes(mapReceipt.roots).filter(node =>
        expectedNodeTypes.has(node.sourceClass) && node.name === reference.name &&
        sameSourcePath(node.assetID || node.locator?.assetID, reference.sourcePath, reference.canonicalSourcePath));
      if (matchingNodes.length !== 1) {
        throw new Error(text(language, '地图树回执没有唯一读回同名、同路径的来源节点，不能确认添加成功。', 'The map-tree receipt did not read back exactly one source node with the captured name and path; the add cannot be confirmed.'));
      }

      preserveNextCoreContextToken.current = nextContext.identityToken;
      const accepted = onCommitSessionReferences(context, nextContext as unknown as Record<string, unknown>, leaseID);
      if (!accepted) {
        preserveNextCoreContextToken.current = null;
        throw new Error(text(language,
          '地图树已返回添加回执，但当前工程上下文未接纳新的会话引用。请刷新项目会话和地图目录后再继续。',
          'The map tree returned an add receipt, but the active project context did not accept the new session reference. Refresh the project session and map catalog before continuing.'));
      }
      setReferenceReceipt({
        kind,
        name: reference.name,
        sourcePath: reference.sourcePath,
        sourceRevision: reference.sourceRevision,
        mapName: exactMap.mapName,
        previousVisualizationRevision,
        visualizationRevision: mapReceipt.visualizationRevision,
      });
      setMapCatalog({ ...freshCatalog, visualizationRevision: mapReceipt.visualizationRevision });
      setInspection(undefined);
      setInspectionOwner('');
    } catch (caught) {
      if (request.signal.aborted && generation.current === request.id && !mapWriteStarted.current) {
        setCancelNote(text(language, '添加引用已取消；没有确认地图树写入回执。', 'The reference request was cancelled without a confirmed map-tree write receipt.'));
      } else if (generation.current === request.id) {
        setError(message(caught, language));
        setRawDiagnostic(caught instanceof Error ? caught.message : String(caught));
      }
    } finally {
      if (leaseID && onEndSessionReferenceMutation) {
        try { onEndSessionReferenceMutation(leaseID); }
        catch (caught) {
          if (generation.current === request.id) {
            setError(text(language, '会话引用租约未能正常释放。', 'The session-reference lease could not be released cleanly.'));
            setRawDiagnostic(caught instanceof Error ? caught.message : String(caught));
          }
        }
      }
      mapWriteStarted.current = false;
      if (generation.current === request.id) setBusy(null);
    }
  };

  const requestCancel = (): void => {
    if (busy === 'map-write' || mapWriteStarted.current) return;
    controller.current?.abort();
    generation.current += 1;
    setBusy(null);
    setCancelNote(text(language, '已请求取消；写入操作开始前不会接纳迟到回执。若复制已开始，仍需核对服务端最终回执和工程目录。', 'Cancellation was requested. Receipts arriving before a write begins will be discarded. If copying already started, confirm the final server receipt and project directory.'));
  };

  const close = (): void => {
    if (busy === 'map-write' || mapWriteStarted.current) return;
    if (busy) requestCancel();
    else onClose();
  };

  return (
    <div className="ci-backdrop" role="presentation" onMouseDown={event => { if (event.target === event.currentTarget && !busy) onClose(); }}>
      <section className="ci-dialog" role="dialog" aria-modal="true" aria-labelledby="ci-title">
        <header className="ci-dialog-header">
          <div><span className="ci-kicker">{zh ? '核心图层 · CoreLayerImportDialog' : 'CORE LAYER · CoreLayerImportDialog'}</span><h2 id="ci-title">{text(language, '检查并导入核心图层', 'Inspect and import a core layer')}</h2></div>
          <button type="button" className="ci-icon-button" aria-label={text(language, '关闭', 'Close')} disabled={busy !== null} onClick={onClose}>×</button>
        </header>

        <p className="ci-explanation">{text(language,
          '检查来源后，可复制文件到工程，或将 Geometry / Boundary Condition 作为仅当前会话的来源加入所选地图。添加引用会登记精确来源捕获并更新地图树；不会复制源 HDF，也不会写入 .ras 工程文件。',
          'After inspection, copy the file into the project or add the Geometry / Boundary Condition as a session-only source to a selected map. Add Reference registers the exact source capture and updates the map tree; it does not copy the source HDF or write the .ras project file.')}</p>

        <div className="ci-context-grid">
          <div><span>{text(language, '工程', 'Project')}</span><strong>{context.projectPath ?? text(language, '未打开', 'Not open')}</strong><small>{text(language, '工程修订', 'Project revision')}: {context.projectRevision ?? '—'}</small></div>
          <div><span>{text(language, '地图目录', 'Map catalog')}</span><strong>{selectedMap?.mapName ?? activeMapLabel ?? text(language, '尚未选择地图', 'No map selected')}</strong><small>{text(language, '会话', 'Session')}: {context.projectSessionID || '—'}</small></div>
        </div>

        <label className="ci-field"><span>{text(language, 'C# 服务端可见的核心图层绝对路径', 'Absolute core-layer path visible to the C# server')}</span><input spellCheck={false} disabled={busy !== null} value={path} onChange={event => changePath(event.target.value)} placeholder="/project/Geometry/Geometry.hdf" /></label>
        <p className="ci-help">{text(language, 'Web 界面当前不会上传本机文件。点击“检查来源”时会由 C# 服务读取原路径、规范路径和 SHA-256；路径仍按原样发送，不会自动替换为规范路径。', 'The Web UI does not upload local files in this workflow. Clicking Inspect asks C# to capture the original path, canonical path, and SHA-256; requests retain the original path and do not silently replace it.')}</p>
        {capture && <div className="ci-result"><strong>{text(language, '已捕获来源文件身份', 'Source file identity captured')}</strong><span>{capture.sourcePath}</span><code>{capture.canonicalPath}</code><code>{capture.revision}</code><small>{bytes(capture.length, language)}</small></div>}

        <section className="ci-result" aria-label={text(language, '目标地图', 'Target map')}>
          <div className="ci-result-heading"><span className="ci-state-dot" /><strong>{text(language, '目标地图', 'Target map')}</strong></div>
          <p className="ci-help">{text(language, '添加引用前需从当前已保存工程读取地图目录，并明确选择一个地图。提交前会再次读取目录和地图树。', 'Read the saved project map catalog and choose a map before adding a reference. The catalog and map tree are reread before submission.')}</p>
          <div className="ci-actions">
            <button type="button" className="ci-button" data-testid="core-layer-map-refresh" disabled={!projectReady || !backend.supports('map.document.list') || busy !== null} onClick={() => void readMapCatalog()}>
              {busy === 'maps' ? text(language, '读取地图中…', 'Reading maps…') : text(language, mapCatalog ? '重新读取地图' : '读取地图目录', mapCatalog ? 'Refresh maps' : 'Read map catalog')}
            </button>
            {mapCatalog && <label className="ci-field"><span>{text(language, '选择地图', 'Choose a map')}</span>
              <select data-testid="core-layer-map-select" disabled={busy !== null} value={selectedMapKey} onChange={event => setSelectedMapKey(event.target.value)}>
                <option value="">{text(language, '请选择已保存地图', 'Select a saved map')}</option>
                {mapCatalog.maps.map(map => <option key={mapKey(map)} value={mapKey(map)} disabled={!supportedReferenceMap(map)}>
                  {map.mapName} · {map.mapType} · #{map.mapIndex}{supportedReferenceMap(map) ? '' : text(language, '（不支持添加核心图层）', ' (core layers unsupported)')}
                </option>)}
              </select>
            </label>}
          </div>
          {mapCatalog && <small>{text(language, '地图数', 'Maps')}: {mapCatalog.maps.length} · {text(language, '地图修订', 'Visualization revision')}: {mapCatalog.visualizationRevision ?? '—'}</small>}
        </section>

        <div className="ci-actions">
          <button type="button" className="ci-button ci-primary" disabled={!canInspect || busy !== null} onClick={() => void inspect()}>{busy === 'inspect' ? text(language, '检查中…', 'Inspecting…') : text(language, '检查来源', 'Inspect source')}</button>
          <button type="button" className="ci-button ci-primary" disabled={!canCopy || busy !== null} onClick={() => void copyToProject()}>{busy === 'copy' ? text(language, '复制中…', 'Copying…') : text(language, '复制到工程', 'Copy to project')}</button>
          <button type="button" className="ci-button ci-primary" data-testid="core-layer-add-reference" disabled={!canAddReference || busy !== null} onClick={() => void addReference()}>
            {busy === 'reference' || busy === 'map-write' ? text(language, '添加引用中…', 'Adding reference…') : text(language, '添加到所选地图', 'Add to selected map')}
          </button>
          {busy !== null && busy !== 'map-write' && <button type="button" className="ci-button ci-danger" onClick={requestCancel}>{text(language, '请求取消', 'Request cancellation')}</button>}
          <button type="button" className="ci-button" disabled={busy !== null} onClick={close}>{text(language, '关闭', 'Close')}</button>
        </div>
        {(!capability.inspect || !capability.perform) && <div className="ci-warning" role="status">{text(language, '当前 C# 服务尚未同时发布核心图层检查和导入操作。', 'The C# server has not advertised both core-layer operations.')}</div>}
        {inspection?.availableActions.includes('addReference') && (!capability.compose || !capability.mapList || !capability.mapTreeLoad || !capability.mapTreePerform || !hasReferenceCallbacks) && <div className="ci-warning" role="status">{text(language, '会话引用需要 Host 上下文合并、地图目录/地图树操作和父级会话租约回调；能力未齐备时不会执行。', 'Session references require Host context composition, map catalog/tree operations, and parent session-lease callbacks. The action stays disabled until all are available.')}</div>}
        {inspection?.availableActions.includes('addReference') && hasUnsavedDrafts && <div className="ci-warning" role="status">{text(language, '请先处理当前工程中的未保存草稿，再添加会话引用。', 'Resolve unsaved project drafts before adding a session reference.')}</div>}
        {inspection?.availableActions.includes('addReference') && !mapCatalog && <div className="ci-warning" role="status">{text(language, '请先读取并选择一个当前工程地图。', 'Read and select a map from the current project first.')}</div>}
        {selectedMap && !supportedReferenceMap(selectedMap) && <div className="ci-warning" role="status">{text(language, '此地图类型不支持添加 Geometry 或 Boundary Condition。', 'This map type does not accept Geometry or Boundary Condition entries.')}</div>}
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
        {referenceReceipt && <section className="ci-success" aria-live="polite">
          <strong>{text(language, '地图树已确认添加会话引用', 'Map tree confirmed the session reference')}</strong>
          <dl className="ci-details">
            <div><dt>{text(language, '类型', 'Kind')}</dt><dd>{referenceReceipt.kind === 'geometry' ? 'Geometry' : 'Boundary Condition'}</dd></div>
            <div><dt>{text(language, '名称', 'Name')}</dt><dd>{referenceReceipt.name}</dd></div>
            <div><dt>{text(language, '来源路径', 'Source path')}</dt><dd>{referenceReceipt.sourcePath}</dd></div>
            <div><dt>{text(language, '来源 SHA-256', 'Source SHA-256')}</dt><dd><code>{referenceReceipt.sourceRevision}</code></dd></div>
            <div><dt>{text(language, '地图', 'Map')}</dt><dd>{referenceReceipt.mapName}</dd></div>
            <div><dt>{text(language, '原地图树修订', 'Previous map-tree revision')}</dt><dd><code>{referenceReceipt.previousVisualizationRevision}</code></dd></div>
            <div><dt>{text(language, '地图树修订', 'Map-tree revision')}</dt><dd><code>{referenceReceipt.visualizationRevision}</code></dd></div>
          </dl>
          <p className="ci-help">{text(language, '此引用仅保存在当前会话上下文和地图可视化文件中。源 HDF 保持原路径，未复制；.ras 工程文件未改写。', 'This reference lives in the current session context and map visualization file. The source HDF remains at its original path and was not copied; the .ras project file was not rewritten.')}</p>
        </section>}
      </section>
    </div>
  );
}
