import { BackendOperationUnavailableError } from '../../../shared/workbench';
import type { BackendGateway, BackendRequestOptions, WorkbenchContext } from '../../../shared/workbench';
import { projectXmlSha } from '../ProjectTreeListener';
import type { SavedMapView, ViewIntent, ViewOwner } from './View';
import { isValidViewExtent, normalizeShortcut } from './View';

export const MAP_VIEWS_INSPECT = 'map.views.inspect';
export const MAP_VIEWS_COMMIT = 'map.views.commit';

export interface MapViewsInspectRequest {
  projectPath: string;
  projectXml: string;
  projectXmlSHA: string;
  projectRevision: string;
  projectSessionID: string;
  action: 'list' | ViewIntent['action'];
  targetName?: string;
  name?: string;
  extent?: ViewIntent['extent'];
  shortcut?: string;
  coreLayerContext?: unknown;
}

export interface MapViewsInspection {
  canCommit: boolean;
  validationMessage?: string | null;
  action: MapViewsInspectRequest['action'];
  projectPath: string;
  projectXml: string;
  projectRevision: string;
  projectXmlSHA: string;
  projectSessionID: string;
  visualizationRevision: string | null;
  viewsSHA256: string;
  views: SavedMapView[];
  target: SavedMapView | null;
}

export interface MapViewsCommitRequest extends MapViewsInspectRequest {
  action: ViewIntent['action'];
  expectedVisualizationRevision: string | null;
  expectedViewsSHA256: string;
  expectedEntrySHA256?: string;
}

export interface MapViewsCommitReceipt {
  success: boolean;
  action: ViewIntent['action'];
  projectPath: string;
  projectXml: string;
  projectRevision: string;
  projectXmlSHA: string;
  projectSessionID: string;
  previousVisualizationRevision: string | null;
  visualizationRevision: string;
  previousViewsSHA256: string;
  viewsSHA256: string;
  views: SavedMapView[];
  before: SavedMapView | null;
  after: SavedMapView | null;
}

export class StaleMapViewsError extends Error {
  constructor(message = '保存视图列表已变化，请刷新后重新操作。') {
    super(message);
    this.name = 'StaleMapViewsError';
  }
}

function isSHA(value: unknown): value is string {
  return typeof value === 'string' && /^[\da-f]{64}$/i.test(value);
}

export function sameServerPath(left: string, right: string): boolean {
  const normalize = (value: string) => value.replace(/\\/g, '/').replace(/\/+$/, '');
  const a = normalize(left);
  const b = normalize(right);
  const isWindowsPath = (value: string) => /^[A-Za-z]:\//.test(value) || value.startsWith('//');
  return isWindowsPath(a) && isWindowsPath(b) ? a.toLowerCase() === b.toLowerCase() : a === b;
}

function requireProjectIdentity(context: WorkbenchContext): Omit<MapViewsInspectRequest, 'action' | 'targetName' | 'name' | 'extent' | 'shortcut' | 'coreLayerContext'> {
  if (!context.projectPath || !context.projectXml || !isSHA(context.projectRevision) || !context.projectSessionID ||
      !/^[\da-f]{8}-[\da-f]{4}-[\da-f]{4}-[\da-f]{4}-[\da-f]{12}$/i.test(context.projectSessionID)) {
    throw new Error('保存工程地图视图需要有效的工程文件、修订和会话。');
  }
  return {
    projectPath: context.projectPath,
    projectXml: context.projectXml,
    projectXmlSHA: '',
    projectRevision: context.projectRevision,
    projectSessionID: context.projectSessionID,
  };
}

function isSavedView(value: unknown): value is SavedMapView {
  if (!value || typeof value !== 'object') return false;
  const view = value as Partial<SavedMapView>;
  return typeof view.name === 'string' && view.name.trim().length > 0 &&
    isValidViewExtent(view.extent) && typeof view.shortcut === 'string' &&
    isSHA(view.entrySHA256);
}

function validateViewCollection(value: unknown): value is SavedMapView[] {
  return Array.isArray(value) && value.every(isSavedView);
}

function assertInspection(value: MapViewsInspection, request: MapViewsInspectRequest, context: WorkbenchContext): void {
  if (!value || typeof value.canCommit !== 'boolean' || value.action !== request.action || !sameServerPath(value.projectPath, request.projectPath) ||
      value.projectXml !== request.projectXml ||
      !isSHA(value.projectRevision) || value.projectRevision.toUpperCase() !== request.projectRevision.toUpperCase() ||
      !isSHA(value.projectXmlSHA) || value.projectXmlSHA.toUpperCase() !== request.projectXmlSHA.toUpperCase() ||
      typeof value.projectSessionID !== 'string' || value.projectSessionID.toLowerCase() !== context.projectSessionID?.toLowerCase() ||
      !(value.visualizationRevision === null || isSHA(value.visualizationRevision)) ||
      !isSHA(value.viewsSHA256) || !validateViewCollection(value.views) ||
      !(value.target === null || isSavedView(value.target))) {
    throw new Error('工程服务返回的视图列表与当前工程会话或修订不匹配。');
  }
  if (request.targetName) {
    const target = value.views.filter((view) => view.name === request.targetName);
    if (target.length !== 1 || value.target?.entrySHA256.toUpperCase() !== target[0].entrySHA256.toUpperCase()) {
      throw new StaleMapViewsError('目标视图身份已变化，请刷新列表后重试。');
    }
  } else if (value.target !== null) {
    throw new Error('工程服务为无目标操作返回了目标视图。');
  }
}

export async function inspectMapViews(
  backend: BackendGateway,
  context: WorkbenchContext,
  action: MapViewsInspectRequest['action'] = 'list',
  intent?: Omit<ViewIntent, 'action'>,
  options?: Pick<BackendRequestOptions, 'signal'>,
  coreLayerContext?: unknown,
): Promise<MapViewsInspection> {
  if (!backend.supports(MAP_VIEWS_INSPECT)) throw new BackendOperationUnavailableError(MAP_VIEWS_INSPECT);
  const identity = requireProjectIdentity(context);
  const request: MapViewsInspectRequest = {
    ...identity,
    projectXmlSHA: await projectXmlSha(identity.projectXml) ?? '',
    action,
    ...intent,
    ...(coreLayerContext !== undefined ? { coreLayerContext } : {}),
  };
  if (!isSHA(request.projectXmlSHA)) throw new Error('无法验证工程 XML 修订。');
  if (options?.signal?.aborted) throw new DOMException('The request was aborted.', 'AbortError');
  const value = await backend.request<MapViewsInspection>(MAP_VIEWS_INSPECT, request, options);
  assertInspection(value, request, context);
  return value;
}

function sameView(left: SavedMapView | null, right: SavedMapView | null): boolean {
  if (!left || !right) return left === right;
  return left.name === right.name && left.entrySHA256.toUpperCase() === right.entrySHA256.toUpperCase() &&
    left.shortcut === right.shortcut && left.extent.left === right.extent.left && left.extent.top === right.extent.top &&
    left.extent.right === right.extent.right && left.extent.bottom === right.extent.bottom;
}

function validateReceipt(
  receipt: MapViewsCommitReceipt,
  inspection: MapViewsInspection,
  intent: ViewIntent,
  context: WorkbenchContext,
): void {
  if (!receipt || receipt.success !== true || receipt.action !== intent.action ||
      !sameServerPath(receipt.projectPath, inspection.projectPath) ||
      !isSHA(receipt.projectRevision) || receipt.projectRevision.toUpperCase() !== context.projectRevision?.toUpperCase() ||
      receipt.projectXml !== inspection.projectXml || !isSHA(receipt.projectXmlSHA) ||
      receipt.projectXmlSHA.toUpperCase() !== inspection.projectXmlSHA.toUpperCase() ||
      typeof receipt.projectSessionID !== 'string' || receipt.projectSessionID.toLowerCase() !== context.projectSessionID?.toLowerCase() ||
      receipt.previousVisualizationRevision !== inspection.visualizationRevision &&
        receipt.previousVisualizationRevision?.toUpperCase() !== inspection.visualizationRevision?.toUpperCase() ||
      !isSHA(receipt.previousViewsSHA256) || receipt.previousViewsSHA256.toUpperCase() !== inspection.viewsSHA256.toUpperCase() ||
      !isSHA(receipt.visualizationRevision) || !isSHA(receipt.viewsSHA256) ||
      !validateViewCollection(receipt.views) ||
      !(receipt.before === null || isSavedView(receipt.before)) ||
      !(receipt.after === null || isSavedView(receipt.after))) {
    throw new Error('保存视图回执与检查时的工程或视图身份不匹配。');
  }

  const target = intent.targetName ? inspection.views.find((view) => view.name === intent.targetName) ?? null : null;
  if (intent.action === 'create') {
    if (receipt.before !== null || receipt.after === null || receipt.after.name !== intent.name ||
        !receipt.views.some((view) => sameView(view, receipt.after)) || !intent.extent ||
        receipt.after.extent.left !== intent.extent.left || receipt.after.extent.top !== intent.extent.top ||
        receipt.after.extent.right !== intent.extent.right || receipt.after.extent.bottom !== intent.extent.bottom ||
        receipt.views.length !== inspection.views.length + 1 ||
        inspection.views.some((view) => !receipt.views.some((candidate) => sameView(candidate, view)))) {
      throw new Error('新视图回执未包含本次保存的视图。');
    }
  } else if (!sameView(receipt.before, target)) {
    throw new Error('回执中的原视图与检查时的目标不一致。');
  } else if (intent.action === 'delete') {
    if (receipt.after !== null || receipt.views.some((view) => view.name === intent.targetName) ||
        receipt.views.length !== inspection.views.length - 1 ||
        inspection.views.some((view) => view.name !== intent.targetName && !receipt.views.some((candidate) => sameView(candidate, view)))) {
      throw new Error('删除视图回执仍包含已删除视图。');
    }
  } else if (!receipt.after || !receipt.views.some((view) => sameView(view, receipt.after)) ||
      receipt.views.length !== inspection.views.length ||
      inspection.views.some((view) => view.name !== intent.targetName && !receipt.views.some((candidate) => sameView(candidate, view)))) {
    throw new Error('视图修改回执未包含更新后的目标。');
  } else if (intent.action === 'rename' && receipt.after.name !== intent.name) {
    throw new Error('重命名回执与请求名称不匹配。');
  } else if (intent.action === 'resetExtent' && (
    receipt.after.extent.left !== intent.extent?.left || receipt.after.extent.top !== intent.extent?.top ||
    receipt.after.extent.right !== intent.extent?.right || receipt.after.extent.bottom !== intent.extent?.bottom)) {
    throw new Error('视图范围回执与当前地图范围不匹配。');
  } else if (intent.action === 'shortcut' && receipt.after.shortcut !== (normalizeShortcut(intent.shortcut ?? '') ?? '')) {
    throw new Error('视图快捷键回执与请求不匹配。');
  }
}

export async function commitMapViewAction(
  backend: BackendGateway,
  context: WorkbenchContext,
  owner: ViewOwner,
  intent: ViewIntent,
  baseline: Pick<MapViewsInspection, 'visualizationRevision' | 'viewsSHA256' | 'views'>,
  options?: Pick<BackendRequestOptions, 'signal'>,
): Promise<MapViewsCommitReceipt> {
  if (!backend.supports(MAP_VIEWS_COMMIT)) throw new BackendOperationUnavailableError(MAP_VIEWS_COMMIT);
  if (owner.projectPath !== context.projectPath || owner.projectSessionID !== context.projectSessionID ||
      owner.projectRevision.toUpperCase() !== context.projectRevision?.toUpperCase() ||
      owner.visualizationRevision !== baseline.visualizationRevision) {
    throw new StaleMapViewsError('当前地图目录修订与视图列表不一致，请先刷新。');
  }
  const inspection = await inspectMapViews(backend, context, intent.action, intent, options, owner.coreLayerContext);
  if ((baseline.visualizationRevision ?? '').toUpperCase() !== (inspection.visualizationRevision ?? '').toUpperCase() ||
      baseline.viewsSHA256.toUpperCase() !== inspection.viewsSHA256.toUpperCase()) {
    throw new StaleMapViewsError();
  }
  if (!inspection.canCommit) throw new Error(inspection.validationMessage || '工程服务当前不允许修改保存视图。');
  const targetName = intent.targetName;
  if (targetName) {
    const baselineTarget = baseline.views.find((view) => view.name === targetName);
    const latestTarget = inspection.views.find((view) => view.name === targetName);
    if (!baselineTarget || !latestTarget || baselineTarget.entrySHA256.toUpperCase() !== latestTarget.entrySHA256.toUpperCase()) {
      throw new StaleMapViewsError('目标视图内容已变化，请刷新列表后再操作。');
    }
  }
  const identity = requireProjectIdentity(context);
  const request: MapViewsCommitRequest = {
    ...identity,
    projectXmlSHA: inspection.projectXmlSHA,
    action: intent.action,
    ...(intent.targetName ? { targetName: intent.targetName } : {}),
    ...(intent.name !== undefined ? { name: intent.name } : {}),
    ...(intent.extent ? { extent: intent.extent } : {}),
    ...(intent.shortcut !== undefined ? { shortcut: normalizeShortcut(intent.shortcut) ?? undefined } : {}),
    ...(owner.coreLayerContext !== undefined ? { coreLayerContext: owner.coreLayerContext } : {}),
    expectedVisualizationRevision: inspection.visualizationRevision,
    expectedViewsSHA256: inspection.viewsSHA256,
    ...(inspection.target ? { expectedEntrySHA256: inspection.target.entrySHA256 } : {}),
  };
  if (intent.shortcut !== undefined && !request.shortcut) throw new Error('快捷键格式无效。');
  if (options?.signal?.aborted) throw new DOMException('The request was aborted.', 'AbortError');
  const receipt = await backend.request<MapViewsCommitReceipt>(MAP_VIEWS_COMMIT, request, options);
  validateReceipt(receipt, inspection, intent, context);
  if (owner.projectPath !== context.projectPath || owner.projectSessionID !== context.projectSessionID ||
      owner.projectRevision.toUpperCase() !== context.projectRevision?.toUpperCase() ||
      owner.visualizationRevision !== baseline.visualizationRevision) {
    throw new StaleMapViewsError('工程或地图视图修订已变化，服务已提交；请刷新地图列表确认当前视图。');
  }
  return receipt;
}
