import type { BackendGateway, MapVisualizationCommitReceipt, MapVisualizationTarget, WorkbenchContext } from '../../../shared/workbench';
import { loadMapDocumentCatalog } from '../ProjectTreeListener';
import type { LoadedMapDocumentCatalog, MapTreeSnapshot } from '../ProjectTreeListener';
import type { MapDocumentTab } from '../mapTypes';
import type { MapAnalysisMapRequest } from '../analysis/mapAnalysisTypes';
import { mapResultSelectionOverridesFromCatalog, parseMapResultSourceCatalog } from '../useMapResultFrames';
import type { MapResultSourceCatalog } from '../useMapResultFrames';
import { resultNodeIdentity } from './sharedKeyframe';

export const RESULT_MAP_TYPES = ['Depth', 'Water Surface', 'Velocity', 'D*V', 'D*V^2'] as const;
export type ResultMapType = typeof RESULT_MAP_TYPES[number];
export const normalizedResultMapType = (value: string | null): string | null => ({
  WaterSurfaceElevation: 'Water Surface', DepthTimesVelocity: 'D*V', DepthTimesVelocitySquared: 'D*V^2',
} as Record<string, string>)[value ?? ''] ?? value;

const sha = (value: unknown): value is string => typeof value === 'string' && /^[a-f\d]{64}$/i.test(value);
const sameRevision = (a: unknown, b: unknown) => sha(a) && sha(b) && a.toUpperCase() === b.toUpperCase();
const record = (value: unknown): Record<string, unknown> => value && typeof value === 'object' && !Array.isArray(value)
  ? value as Record<string, unknown> : {};
const groups = (catalog: MapResultSourceCatalog) => catalog.activeNodes
  .filter(node => node.nodeKind === 'MultiResult' && node.node.nodeType === 'MultiResult');
const sourceDigest = (catalog: MapResultSourceCatalog) => groups(catalog).map(node => ({
  node: resultNodeIdentity(node.node),
  selections: node.selections.map(selection => ({ index: selection.selectionIndex, mode: selection.selectionMode,
    path: selection.selectedResult?.resultPath ?? null, revision: selection.selectedResult?.revision.toUpperCase() ?? null })),
})).sort((a, b) => a.node.localeCompare(b.node));

export interface ResultLayerWriteCapture {
  scope: string;
  backend: BackendGateway;
  context: WorkbenchContext;
  document: MapDocumentTab;
  map: MapAnalysisMapRequest;
  catalog: MapResultSourceCatalog;
  type: ResultMapType;
  target: MapVisualizationTarget;
  leaseID: string;
  commit: (source: WorkbenchContext, target: MapVisualizationTarget, receipt: MapVisualizationCommitReceipt, leaseID: string) => boolean;
  end: (leaseID: string) => void;
}
export interface ResultLayerReadback { catalog: LoadedMapDocumentCatalog; document: MapDocumentTab; tree: MapTreeSnapshot }
export type ResultLayerWritePhase = 'writing' | 'unknown' | 'unverified' | 'adoption' | 'readback' | 'release' | 'terminal' | 'complete';
export interface ResultLayerWriteState {
  phase: ResultLayerWritePhase;
  message: string;
  operationID: string | null;
  diagnostic: string | null;
  busy: boolean;
}

/** Kept outside the page so navigating away cannot discard the original operation or its parent lease. */
export const pendingResultLayerWrites = new Map<string, ResultLayerWrite>();

export class ResultLayerWrite {
  state: ResultLayerWriteState = { phase: 'writing', message: '正在保存结果组地图类型…', operationID: null, diagnostic: null, busy: true };
  private listeners = new Set<() => void>();
  private receipt: MapVisualizationCommitReceipt | null = null;
  private adopted = false;
  private terminalFailure = false;
  private conflictingID = false;
  private readback: ResultLayerReadback | null = null;
  private installer: ((readback: ResultLayerReadback, capture: ResultLayerWriteCapture) => boolean) | null = null;
  constructor(readonly capture: ResultLayerWriteCapture) {}
  subscribe(listener: () => void) { this.listeners.add(listener); return () => { this.listeners.delete(listener); }; }
  attach(installer: (readback: ResultLayerReadback, capture: ResultLayerWriteCapture) => boolean) { this.installer = installer; }
  detach(installer: (readback: ResultLayerReadback, capture: ResultLayerWriteCapture) => boolean) {
    if (this.installer === installer) this.installer = null;
  }
  private publish(patch: Partial<ResultLayerWriteState>) {
    this.state = { ...this.state, ...patch };
    for (const listener of this.listeners) listener();
  }
  private trackID(id: string) {
    if (!/^[a-f\d]{32}$/i.test(id) || (this.state.operationID && this.state.operationID !== id)) {
      this.conflictingID = true;
      return;
    }
    this.publish({ operationID: id });
  }
  private validateReceipt(raw: unknown): MapVisualizationCommitReceipt {
    const item = record(raw), target = this.capture.target;
    if (!Array.isArray(item.roots) || !sameRevision(item.previousVisualizationRevision, target.visualizationRevision) ||
        !sha(item.visualizationRevision) || sameRevision(item.visualizationRevision, target.visualizationRevision)) {
      throw new Error('地图写入回执与原修订不匹配或没有确认新修订。');
    }
    return { mapIndex: target.mapIndex, mapName: target.name, mapType: target.mapType,
      previousVisualizationRevision: item.previousVisualizationRevision as string, visualizationRevision: item.visualizationRevision };
  }
  private async read(revision: string, expectedTypes: 'after' | 'before'): Promise<ResultLayerReadback> {
    const c = this.capture;
    const listed = await loadMapDocumentCatalog(c.backend, c.context);
    if (!sameRevision(listed.visualizationRevision, revision)) throw new Error('重新读取的地图修订与操作回执不一致。');
    const matches = listed.documents.filter(doc => doc.mapID === c.target.mapID && doc.mapIndex === c.target.mapIndex &&
      doc.name === c.target.name && doc.mapType === c.target.mapType);
    if (matches.length !== 1) throw new Error('重新读取后无法唯一定位原地图。');
    const map = { ...c.map, expectedVisualizationRevision: revision,
      resultSelectionOverrides: c.catalog.treeSelectionOverrides };
    const tree = await c.backend.request<MapTreeSnapshot>('map.tree.load', map);
    if (!Array.isArray(tree?.roots) || !sameRevision(tree.visualizationRevision, revision) ||
        !sameRevision(tree.previousVisualizationRevision, revision)) throw new Error('地图树读回的修订不一致。');
    const parsed = parseMapResultSourceCatalog(await c.backend.request<unknown>('map.resultSources', {
      map, selectionOverrides: mapResultSelectionOverridesFromCatalog(c.catalog),
    }));
    if (parsed.projectPath !== c.catalog.projectPath || !sameRevision(parsed.projectRevision, c.catalog.projectRevision) ||
        !sameRevision(parsed.projectXmlSHA, c.catalog.projectXmlSHA) || !sameRevision(parsed.visualizationRevision, revision) ||
        parsed.mapID !== c.target.mapID || parsed.mapIndex !== c.target.mapIndex || parsed.mapName !== c.target.name ||
        parsed.mapType !== c.target.mapType || JSON.stringify(sourceDigest(parsed)) !== JSON.stringify(sourceDigest(c.catalog))) {
      throw new Error('结果组、来源或工程身份在读回时发生变化。');
    }
    const before = new Map(groups(c.catalog).map(node => [resultNodeIdentity(node.node), normalizedResultMapType(node.selectedMapType)]));
    if (!groups(parsed).length || groups(parsed).some(node => normalizedResultMapType(node.selectedMapType) !==
        (expectedTypes === 'after' ? c.type : before.get(resultNodeIdentity(node.node))))) {
      throw new Error('重新读取的活动结果组地图类型与预期不一致。');
    }
    const document = { ...matches[0], roots: tree.roots, visualizationRevision: revision, treeError: undefined };
    return { catalog: listed, document, tree };
  }
  private async finishReadback() {
    const c = this.capture;
    if (this.receipt && !this.adopted) {
      try { this.adopted = c.commit(c.context, c.target, this.receipt, c.leaseID); } catch { this.adopted = false; }
      if (!this.adopted) {
        this.publish({ phase: 'adoption', message: '服务已确认写入，但父工作区尚未接纳回执；写入保持锁定。', busy: false });
        return;
      }
    }
    const revision = this.receipt?.visualizationRevision ?? c.target.visualizationRevision;
    if (!revision || (!this.receipt && !this.terminalFailure)) throw new Error('原操作尚未得到终态确认。');
    this.publish({ phase: 'readback', message: '正在重新读取地图树和活动结果组…' });
    this.readback = await this.read(revision, this.receipt ? 'after' : 'before');
    if (!this.installer || !this.installer(this.readback, c)) {
      this.publish({ phase: 'readback', message: '地图已读回，等待原地图界面接纳；写入保持锁定。', busy: false });
      return;
    }
    this.publish({ phase: 'release', message: '已核对地图，正在释放写入保护…' });
    c.end(c.leaseID);
    pendingResultLayerWrites.delete(c.scope);
    this.publish({ phase: 'complete', busy: false, diagnostic: null,
      message: this.receipt ? '结果组地图类型已保存并重新读取。' : '原操作已确认失败，地图未改变。' });
  }
  private async follow(resume: boolean) {
    const c = this.capture;
    this.publish({ busy: true, phase: 'writing', message: resume ? '正在续查原地图操作…' : '正在保存结果组地图类型…', diagnostic: null });
    try {
      const options = { onTransportState: (state: { operationId: string }) => this.trackID(state.operationId) };
      const raw = resume ? await c.backend.resumeOperation!<unknown>(this.state.operationID!, options)
        : await c.backend.request<unknown>('map.tree.perform', {
          map: { ...c.map, resultSelectionOverrides: c.catalog.treeSelectionOverrides },
          action: 'setResultMapType', name: c.type,
        }, options);
      if (this.conflictingID) throw new Error('传输任务编号不一致。');
      try { this.receipt = this.validateReceipt(raw); }
      catch (error) {
        this.publish({ phase: 'unverified', busy: false, message: '服务回执无法验证；保留租约，不能重复提交。', diagnostic: String(error) });
        return;
      }
      await this.finishReadback();
    } catch (error) {
      const meta = record(error), errorID = typeof meta.operationId === 'string' ? meta.operationId : null;
      if (errorID) this.trackID(errorID);
      if (!this.receipt && !this.conflictingID && meta.terminalConfirmed === true &&
          (!this.state.operationID || errorID === this.state.operationID)) {
        this.terminalFailure = true;
        this.publish({ phase: 'terminal', message: '原操作已结束，正在核对原地图是否未变。', diagnostic: String(error) });
        try { await this.finishReadback(); return; } catch (readError) {
          this.publish({ phase: 'terminal', busy: false, message: '原操作已结束，但原地图尚未核对；写入保持锁定。', diagnostic: String(readError) });
          return;
        }
      }
      this.publish({ phase: this.receipt ? 'readback' : 'unknown', busy: false,
        message: this.receipt ? '写入已确认，但读回或接纳尚未完成；写入保持锁定。'
          : '原操作结果尚未确认；保留任务编号和写入保护，不会重复提交。', diagnostic: String(error) });
    }
  }
  start() { void this.follow(false); }
  async resume() {
    if (this.state.busy || this.state.phase === 'complete' || this.state.phase === 'unverified') return;
    if (this.receipt || this.terminalFailure) {
      this.publish({ busy: true, diagnostic: null });
      try { await this.finishReadback(); }
      catch (error) { this.publish({ phase: this.receipt ? 'readback' : 'terminal', busy: false,
        message: '重新核对尚未完成；写入保持锁定。', diagnostic: String(error) }); }
      return;
    }
    if (!this.state.operationID || !this.capture.backend.resumeOperation || this.conflictingID) return;
    await this.follow(true);
  }
  get canResume() {
    return !this.state.busy && this.state.phase !== 'unverified' &&
      (!!this.receipt || this.terminalFailure || (!!this.state.operationID && !!this.capture.backend.resumeOperation && !this.conflictingID));
  }
}
