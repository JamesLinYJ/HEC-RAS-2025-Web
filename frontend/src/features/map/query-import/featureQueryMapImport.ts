import type { BackendGateway, FeaturePageProps, WorkbenchContext } from '../../../shared/workbench';
import { errorDiagnostics } from '../../../shared/workbench';
import type { CatalogTreeNode, CatalogTreeNodeLocator, MapDocumentTab } from '../mapTypes';
import type { MapDocumentReceipt, MapTreeSnapshot } from '../ProjectTreeListener';
import { isWorkspaceCapture, sameSourcePath } from '../../tools/attributes/attributeTableTypes';
import { captureMapOwner, inspectShapefileFamily, isShapefileMapImportReceipt,
  validShapefileFamily, type CapturedMapOwner } from '../../tools/vector-import/vectorImportTypes';
import type { FeatureQueryExportedSource, FeatureQueryMapImportReport, FeatureQuerySourceFamily } from './featureQueryTypes';

export interface FeatureQueryMapImportOptions extends Pick<FeaturePageProps, 'onBeginMapVisualizationMutation' |
  'onCommitMapVisualization' | 'onEndMapVisualizationMutation'> {
  dirtyOwner: string;
  /** Called only after the parent accepted the receipt and this exact saved layer was read back. */
  onReadback?: (document: MapDocumentTab, tree: MapTreeSnapshot) => boolean;
}

const sha = (value: unknown): value is string => typeof value === 'string' && /^[\da-f]{64}$/i.test(value);
const equalSha = (left: unknown, right: unknown): boolean => sha(left) && sha(right) && left.toUpperCase() === right.toUpperCase();
const absolute = (value: unknown): value is string => typeof value === 'string' &&
  (value.startsWith('/') || /^[a-z]:[\\/]/i.test(value) || /^\\\\[^\\]/.test(value));
const record = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);
const description = (error: unknown): string => error instanceof Error ? error.message : String(error);
const terminalDescription = (error: unknown): string => {
  const summary = description(error), diagnostics = errorDiagnostics(error);
  const detail = record(diagnostics) && typeof diagnostics.message === 'string' ? diagnostics.message : '';
  return detail && detail !== summary ? `${summary}（${detail}）` : summary;
};
const vectorTypes = new Set(['PointsVector', 'PolylinesVector', 'PolygonsVector']);

function validFamily(source: FeatureQueryExportedSource): source is FeatureQueryExportedSource & { family: FeatureQuerySourceFamily } {
  const family = source.family;
  if (!absolute(source.path) || !sha(source.revision) || !vectorTypes.has(source.mapType) ||
      !Number.isSafeInteger(source.featureCount) || source.featureCount < 0 || !family ||
      !absolute(family.sourcePath) || !sha(family.revision) || !Array.isArray(family.files)) return false;
  const kind = /\.shp$/i.test(source.path) ? 'shapefile' : /\.(geojson|json)$/i.test(source.path) ? 'geojson' : null;
  if (!kind || family.family !== kind) return false;
  const roles = new Map<string, FeatureQuerySourceFamily['files'][number]>();
  for (const file of family.files) {
    if (!file || !absolute(file.inputPath) || !absolute(file.path) || typeof file.role !== 'string' || !file.role ||
        typeof file.required !== 'boolean' || typeof file.exists !== 'boolean' ||
        !(file.revision === null || sha(file.revision)) || file.exists !== (file.revision !== null) || roles.has(file.role)) return false;
    roles.set(file.role, file);
  }
  const primary = roles.get('source');
  if (!primary || !primary.required || !primary.exists || !sameSourcePath(primary.inputPath, source.path) ||
      !sameSourcePath(primary.path, family.sourcePath) || !equalSha(primary.revision, source.revision)) return false;
  if (kind === 'geojson') return roles.size === 1;
  if (!validShapefileFamily(family, family.sourcePath, source.revision) || roles.size !== 4) return false;
  const base = source.path.replace(/\.shp$/i, '');
  return ['shx', 'dbf', 'prj'].every(role => {
    const file = roles.get(role);
    return !!file && sameSourcePath(file.inputPath, base + '.' + role) && file.required === (role !== 'prj') &&
      (role === 'prj' || file.exists);
  });
}

function sameFamily(left: FeatureQuerySourceFamily, right: unknown): boolean {
  if (!record(right) || left.family !== right.family || typeof right.sourcePath !== 'string' ||
      !sameSourcePath(left.sourcePath, right.sourcePath) || !equalSha(left.revision, right.revision) ||
      !Array.isArray(right.files) || left.files.length !== right.files.length) return false;
  const files = new Map<string, Record<string, unknown>>();
  for (const file of right.files) {
    if (!record(file) || typeof file.role !== 'string' || files.has(file.role)) return false;
    files.set(file.role, file);
  }
  return left.files.every(file => {
    const other = files.get(file.role);
    return !!other && typeof other.inputPath === 'string' && typeof other.path === 'string' &&
      sameSourcePath(file.inputPath, other.inputPath) && sameSourcePath(file.path, other.path) &&
      file.required === other.required && file.exists === other.exists &&
      (file.revision === null ? other.revision === null : equalSha(file.revision, other.revision));
  });
}

function matchingNodes(roots: CatalogTreeNode[], source: FeatureQueryExportedSource): CatalogTreeNode[] {
  const matches: CatalogTreeNode[] = [];
  const visit = (nodes: CatalogTreeNode[]): void => {
    for (const node of nodes) {
      if (node.locator?.nodeType === 'MapLayer' && node.locator.mapType === source.mapType &&
          typeof node.assetID === 'string' && (sameSourcePath(node.assetID, source.path) ||
            !!source.family && sameSourcePath(node.assetID, source.family.sourcePath))) matches.push(node);
      if (Array.isArray(node.children)) visit(node.children);
    }
  };
  visit(roots);
  return matches;
}

function sameLocator(value: unknown, expected: CatalogTreeNodeLocator): boolean {
  if (!record(value) || !Array.isArray(value.nodePath) || !Array.isArray(expected.nodePath) ||
      value.nodePath.length !== expected.nodePath.length ||
      !expected.nodePath.every((part, index) => Number.isInteger(part) && part >= 0 && value.nodePath instanceof Array && value.nodePath[index] === part)) return false;
  return value.rootName === expected.rootName && value.nodeType === expected.nodeType && value.nodeName === expected.nodeName &&
    value.mapType === expected.mapType && (value.assetID ?? null) === (expected.assetID ?? null) &&
    (value.filename ?? null) === (expected.filename ?? null) && (value.filenameRelative ?? null) === (expected.filenameRelative ?? null);
}

function treeRequest(owner: CapturedMapOwner, context: WorkbenchContext): Record<string, unknown> {
  const request = owner.request;
  const core = request.coreLayerContext;
  const references = core?.references ?? core?.References;
  return { projectPath: request.projectPath, projectXml: request.projectXml, projectXmlSHA: request.projectXmlSHA,
    mapID: request.mapID, mapIndex: request.mapIndex, mapName: request.mapName, mapType: request.mapType,
    expectedVisualizationRevision: request.expectedVisualizationRevision, projectSessionID: context.projectSessionID,
    ...(core ? { coreLayerContext: core, externalCoreLayerReferences: Array.isArray(references) ? references : [] } : {}) };
}

interface Pending {
  source: FeatureQueryExportedSource & { family: FeatureQuerySourceFamily };
  target: MapDocumentTab;
  owner: CapturedMapOwner;
  projectCanonicalPath: string;
  lease: string;
  operationID: string | null;
  receipt?: MapDocumentReceipt;
  rawReceipt?: unknown;
  verified?: { document: MapDocumentTab; tree: MapTreeSnapshot };
  terminalMessage?: string;
  phase: 'operation' | 'adoption' | 'readback' | 'release' | 'terminal' | 'unverified';
}

/** Imports already exported server files; no upload or second export is performed. */
export async function importFeatureQuerySourcesToMap(backend: BackendGateway, sourceContext: WorkbenchContext,
  target: MapDocumentTab, sources: FeatureQueryExportedSource[], isCurrent: () => boolean,
  options: FeatureQueryMapImportOptions): Promise<FeatureQueryMapImportReport> {
  // All recovery continues against the exact connection, source, target and lease captured here.
  const context = structuredClone(sourceContext);
  const queue = structuredClone(sources);
  let currentTarget = structuredClone(target);
  let index = 0;
  const imported: string[] = [];
  let pending: Pending | null = null;
  let finished: FeatureQueryMapImportReport | null = null;
  let resuming: Promise<FeatureQueryMapImportReport> | null = null;
  const signal = new AbortController().signal;

  const report = (status: FeatureQueryMapImportReport['status'], path: string, message: string,
    operationID: string | null = null): FeatureQueryMapImportReport => ({
    status, imported: [...imported], failures: path ? [{ path, message }] : [], message,
    ...(operationID ? { operationID } : {}),
  });
  const stop = (path: string, message: string): FeatureQueryMapImportReport => {
    finished = report(imported.length ? 'partial' : 'failed', path, message);
    return finished;
  };
  const end = (lease: string): void => { options.onEndMapVisualizationMutation?.(lease); };
  const ensureCurrent = (): void => { if (!isCurrent()) throw new Error('来源会话或目标地图已切换；未提交后续图层。'); };
  const unknown = (message: string): FeatureQueryMapImportReport => {
    const held = pending!;
    const value = report('unknown', held.source.path, message, held.operationID);
    if (held.phase !== 'operation' || held.operationID && backend.resumeOperation) value.resume = resume;
    return value;
  };

  async function verifyReadback(held: Pending, owner: CapturedMapOwner, revision: string): Promise<void> {
    const nodes = matchingNodes(owner.tree.roots, held.source);
    if (nodes.length !== 1 || !nodes[0].locator || nodes[0].checked !== 'checked' ||
        !Array.isArray(nodes[0].locator.nodePath) || nodes[0].locator.nodePath.length === 0 ||
        nodes[0].diagnosticSourceRevision && !equalSha(nodes[0].diagnosticSourceRevision, held.source.revision) ||
        nodes[0].diagnosticSourceIdentity && !sameSourcePath(nodes[0].diagnosticSourceIdentity, held.source.family.sourcePath)) {
      throw new Error('保存后的地图树未读回唯一、启用且来源匹配的图层。');
    }
    const node = nodes[0];
    const raw = await backend.request<unknown>('map.vector.source.load', {
      map: treeRequest(owner, context), node: node.locator, expectedFamily: held.source.family,
    }, { signal });
    if (!record(raw) || !record(raw.source) || !record(raw.source.map) || !record(raw.data))
      throw new Error('地图来源读取回执无效。');
    const map = raw.source.map;
    const shape = held.source.mapType === 'PointsVector' ? 'Point' : held.source.mapType === 'PolylinesVector' ? 'Polyline' : 'Polygon';
    if (typeof map.projectPath !== 'string' || !sameSourcePath(map.projectPath, held.projectCanonicalPath) ||
        !equalSha(map.projectRevision, context.projectRevision) || !equalSha(map.projectXmlSHA, owner.request.projectXmlSHA) ||
        !equalSha(map.visualizationRevision, revision) || map.mapID !== held.target.mapID ||
        map.mapIndex !== held.target.mapIndex || map.mapName !== held.target.name || map.mapType !== held.target.mapType || map.isPersisted !== true ||
        !sameLocator(raw.source.locator, node.locator!) || !sameFamily(held.source.family, raw.source.family) ||
        raw.data.shapeType !== shape || raw.data.count !== held.source.featureCount) {
      throw new Error('地图节点来源、文件族、工程或修订与导出回执不一致。');
    }
  }

  async function readback(held: Pending): Promise<{ document: MapDocumentTab; tree: MapTreeSnapshot }> {
    const receipt = held.receipt!;
    const next = { ...held.target, visualizationRevision: receipt.visualizationRevision, visibilityTransition: null };
    const owner = await captureMapOwner(backend, context, next, signal);
    await verifyReadback(held, owner, receipt.visualizationRevision);
    return { document: { ...next, roots: owner.tree.roots, treeError: undefined }, tree: owner.tree };
  }

  async function acceptReceipt(held: Pending, raw: unknown): Promise<FeatureQueryMapImportReport> {
    if (!isShapefileMapImportReceipt(raw, held.target, held.owner.visualizationRevision) ||
        equalSha(raw.visualizationRevision, held.owner.visualizationRevision)) {
      held.rawReceipt = raw;
      held.phase = 'unverified';
      return unknown('服务返回的地图回执与原目标或修订不匹配；已保留原回执与租约，不能重新提交。可续查目录与来源进行核对。');
    }
    held.receipt = raw;
    held.phase = 'adoption';
    return adoptReceipt(held);
  }

  async function adoptReceipt(held: Pending): Promise<FeatureQueryMapImportReport> {
    let adopted = false;
    try { adopted = options.onCommitMapVisualization?.(context, held.target, held.receipt!, held.lease) ?? false; }
    catch { adopted = false; }
    if (!adopted) {
      return unknown('服务已确认地图写入，但父工作区未接纳回执；保留原租约并停止后续图层，可续查接纳与读回。');
    }
    held.phase = 'readback';
    return finishReadback(held);
  }

  async function finishReadback(held: Pending): Promise<FeatureQueryMapImportReport> {
    let verified: { document: MapDocumentTab; tree: MapTreeSnapshot };
    try { verified = await readback(held); }
    catch (error) {
      held.phase = 'readback';
      return unknown(`地图写入已确认，但来源读回未确认：${description(error)}；未继续后续图层，可续查读回。`);
    }
    try {
      if (!isCurrent() || options.onReadback?.(verified.document, verified.tree) === false) {
        return unknown('地图来源已读回，但当前界面无法接纳该地图修订；保留租约并停止后续图层，可续查读回。');
      }
    } catch (error) {
      return unknown(`地图来源已读回，但界面未能接纳：${description(error)}；保留租约并停止后续图层。`);
    }
    held.verified = verified;
    held.phase = 'release';
    return finishRelease(held);
  }

  async function finishRelease(held: Pending): Promise<FeatureQueryMapImportReport> {
    try { end(held.lease); }
    catch (error) { return unknown(`地图来源已读回，但释放父租约失败：${description(error)}；未继续后续图层。`); }
    currentTarget = held.verified!.document;
    imported.push(held.source.path);
    pending = null;
    index += 1;
    return advance();
  }

  function finishTerminal(held: Pending): FeatureQueryMapImportReport {
    try { end(held.lease); }
    catch (error) { return unknown(`原操作已确认失败，但释放父租约失败：${description(error)}；请继续核对。`); }
    pending = null;
    return stop(held.source.path, held.terminalMessage!);
  }

  async function reconcileUnverified(held: Pending): Promise<FeatureQueryMapImportReport> {
    // An unrelated edit can also change .vis. Even a matching node cannot turn an
    // invalid original receipt into a parent acknowledgement or justify another POST.
    try {
      const listed = await backend.request<unknown>('map.document.list', { projectPath: context.projectPath,
        projectXml: context.projectXml, projectXmlSHA: held.owner.request.projectXmlSHA,
        expectedProjectRevision: context.projectRevision }, { signal });
      if (!record(listed) || !equalSha(listed.projectRevision, context.projectRevision) || !sha(listed.visualizationRevision) ||
          !Array.isArray(listed.maps) || !listed.maps.some(item => record(item) && item.mapIndex === held.target.mapIndex &&
            item.mapName === held.target.name && item.mapType === held.target.mapType)) throw new Error('地图目录身份不一致。');
      const readTarget = { ...held.target, visualizationRevision: listed.visualizationRevision };
      const readOwner = await captureMapOwner(backend, context, readTarget, signal);
      await verifyReadback(held, readOwner, listed.visualizationRevision);
      return unknown('目录中可读到相同来源，但原写入回执不匹配，无法证明本次操作并取得父工作区确认；保留租约，未继续。');
    } catch (error) { return unknown(`目录或来源核对未完成：${description(error)}；保留原回执和租约，未重新提交。`); }
  }

  async function resumeOnce(): Promise<FeatureQueryMapImportReport> {
    if (finished) return finished;
    const held = pending;
    if (!held) return stop('', '没有可续查的地图操作。');
    if (held.phase === 'adoption') return adoptReceipt(held);
    if (held.phase === 'readback') return finishReadback(held);
    if (held.phase === 'release') return finishRelease(held);
    if (held.phase === 'terminal') return finishTerminal(held);
    if (held.phase === 'unverified') return reconcileUnverified(held);
    if (!held.operationID || !backend.resumeOperation) return unknown('原操作没有可续查编号或连接；请保留此状态并核对服务端，不能重新提交。');
    try {
      const raw = await backend.resumeOperation<unknown>(held.operationID);
      return await acceptReceipt(held, raw);
    } catch (error) {
      const meta = record(error) ? error : {};
      if (held.phase === 'operation' && meta.terminalConfirmed === true && meta.operationId === held.operationID) {
        held.phase = 'terminal';
        held.terminalMessage = `原地图操作已确认失败：${terminalDescription(error)}`;
        return finishTerminal(held);
      }
      if (!pending) pending = held;
      return unknown(`原地图操作状态仍未确认：${description(error)}；请继续续查同一操作。`);
    }
  }
  function resume(): Promise<FeatureQueryMapImportReport> {
    if (!resuming) resuming = resumeOnce().finally(() => { resuming = null; });
    return resuming;
  }

  async function advance(): Promise<FeatureQueryMapImportReport> {
    if (finished) return finished;
    if (index >= queue.length) {
      finished = report('complete', '', `已加入并读回 ${imported.length} 个地图来源。`);
      return finished;
    }
    const source = queue[index];
    let lease: string | null = null;
    try {
      ensureCurrent();
      if (!options.onBeginMapVisualizationMutation || !options.onCommitMapVisualization || !options.onEndMapVisualizationMutation || !options.dirtyOwner)
        throw new Error('父工作区未提供完整的地图写入租约，不能加入地图。');
      if (!validFamily(source)) throw new Error('导出来源的路径、修订或完整文件族回执无效。');
      for (const method of ['workspace.file.inspect', 'map.document.list', 'map.tree.load', 'map.vector.source.load',
        source.family.family === 'shapefile' ? 'map.vector.importShapefile' : 'map.vector.import']) {
        if (!backend.supports(method)) throw new Error(`当前服务未提供 ${method}，不能验证并加入地图。`);
      }
      lease = options.onBeginMapVisualizationMutation(context, currentTarget, options.dirtyOwner);
      if (!lease) throw new Error('无法取得当前工程和地图的写入租约；请处理其他未保存操作。');
      const sourceInspect = await backend.request<unknown>('workspace.file.inspect', { sourcePath: source.path }, { signal });
      ensureCurrent();
      if (!isWorkspaceCapture(sourceInspect, source.path) || !sameSourcePath(sourceInspect.canonicalPath, source.family.sourcePath) ||
          !equalSha(sourceInspect.revision, source.revision)) throw new Error('导出来源路径或主文件修订已变化。');
      if (source.family.family === 'shapefile') {
        const inspected = await inspectShapefileFamily(backend, source.path, signal);
        ensureCurrent();
        if (!sameFamily(source.family, inspected.family)) throw new Error('导出后的 Shapefile 文件族已变化。');
      }
      const projectInspect = await backend.request<unknown>('workspace.file.inspect', { sourcePath: context.projectPath }, { signal });
      ensureCurrent();
      if (!context.projectPath || !isWorkspaceCapture(projectInspect, context.projectPath) ||
          !equalSha(projectInspect.revision, context.projectRevision)) throw new Error('工程文件身份或修订已变化。');
      const owner = await captureMapOwner(backend, context, currentTarget, signal);
      ensureCurrent();
      if (matchingNodes(owner.tree.roots, source).length) throw new Error('该来源和类型已存在于目标地图，未重复加入。');
      const held: Pending = { source, target: currentTarget, owner, projectCanonicalPath: projectInspect.canonicalPath,
        lease, operationID: null, phase: 'operation' };
      pending = held;
      const method = source.family.family === 'shapefile' ? 'map.vector.importShapefile' : 'map.vector.import';
      const parameters = source.family.family === 'shapefile'
        ? { map: owner.request, sourcePath: source.path, expectedProjectRevision: context.projectRevision, expectedFamily: source.family }
        : { map: owner.request, sourcePath: source.path, sourceRevision: source.revision,
          expectedProjectRevision: context.projectRevision, expectedSourceCanonicalPath: source.family.sourcePath };
      let conflictingOperation = false;
      try {
        const raw = await backend.request<unknown>(method, parameters, { onTransportState: state => {
          if (held.operationID && held.operationID !== state.operationId) conflictingOperation = true;
          else held.operationID = state.operationId;
        } });
        if (conflictingOperation) { held.rawReceipt = raw; held.phase = 'unverified'; return unknown('传输任务编号发生冲突；已保留原回执与租约，不能重新提交。'); }
        return await acceptReceipt(held, raw);
      } catch (error) {
        const meta = record(error) ? error : {};
        const errorID = typeof meta.operationId === 'string' && meta.operationId.trim() ? meta.operationId : null;
        if (held.phase !== 'operation') {
          if (!pending) pending = held;
          return unknown(`地图写入后的核对未完成：${description(error)}；保留原租约，不会重新提交。`);
        }
        if (held.operationID && errorID && held.operationID !== errorID) conflictingOperation = true;
        if (!held.operationID) held.operationID = errorID;
        if (!conflictingOperation && meta.terminalConfirmed === true && (!held.operationID || errorID === held.operationID)) {
          held.phase = 'terminal';
          held.terminalMessage = `地图操作已确认失败：${terminalDescription(error)}`;
          return finishTerminal(held);
        }
        if (conflictingOperation) held.operationID = null;
        return unknown(`地图操作状态未确认：${description(error)}；保留原租约，不会重新提交。`);
      }
    } catch (error) {
      const path = source?.path ?? '';
      const message = `未提交地图来源：${description(error)}`;
      if (lease) {
        const heldLease = lease;
        const release = (): FeatureQueryMapImportReport => {
          try { end(heldLease); return stop(path, message); }
          catch (releaseError) {
            return { ...report('unknown', path, `${message}；释放父租约失败：${description(releaseError)}。`),
              resume: async () => finished ?? release() };
          }
        };
        return release();
      }
      return stop(path, message);
    }
  }

  if (queue.length === 0) return stop('', '没有可加入地图的导出来源。');
  return advance();
}
