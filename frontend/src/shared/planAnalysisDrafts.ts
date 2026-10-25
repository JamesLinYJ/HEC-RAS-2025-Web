import type { BackendGateway, WorkbenchContext } from './workbench';

/** Host PlanAnalysisPlanDraft. Values are the original editor strings, never normalized. */
export interface PlanAnalysisPlanDraft {
  assetPath: string;
  revision: string;
  fields: Record<string, string>;
  sourceState?: string | null;
  planTimeWindowEdits?: { id: string; fieldID: string; value: string }[] | null;
  editorID: string;
  ownerContextID: string;
}
export interface PlanAnalysisDraftSource {
  projectSessionID: string;
  projectPath: string | null;
  projectRevision: string | null;
  projectXml: string | null;
  coreLayerContext: Record<string, unknown> | null;
  backendIdentity: string;
  backendRevision: number;
}
export type PlanAnalysisDraftStatus = 'clean' | 'draft' | 'invalid' | 'busy' | 'unavailable' | 'unsupported' | 'uncertain';
export interface PlanSaveRecoveryAcknowledgement {
  editorID: string;
  ownerContextID: string;
  operationID: string;
  attemptID: string;
}
export interface PlanAnalysisDraftPublication {
  editorID: string;
  /** The exact key registered with FeaturePageProps.onDirtyStateChange. */
  ownerContextID: string;
  source: PlanAnalysisDraftSource;
  assetPath: string;
  revision: string | null;
  dirty: boolean;
  valid: boolean;
  busy: boolean;
  /** Actual source write, distinct from loading or validating the editor. */
  writePending?: boolean;
  /** Retires captures even when an invalid draft cannot be included in the Host payload. */
  draftRevision?: number;
  saveOperationID?: string | null;
  recoveryAttemptID?: string;
  status: PlanAnalysisDraftStatus;
  reason?: string;
  planDraft?: PlanAnalysisPlanDraft;
}
export interface PlanAnalysisDraftEntry extends PlanAnalysisDraftPublication { captureRevision: number }
export interface PlanAnalysisDraftRegistry {
  revision: number;
  captureKey: string;
  entries: readonly PlanAnalysisDraftEntry[];
  /** Root project/map/reference transactions, separate from consumer-owned export owners. */
  sessionBusy: boolean;
  /** Advances only for the root's explicit discard-and-continue action. */
  discardRevision: number;
}
export interface PlanAnalysisDraftBlocker { code: string; message: string; ownerContextID?: string; assetPath?: string }
export interface PlanAnalysisDraftCapture {
  context: WorkbenchContext;
  backend: BackendGateway;
  backendRevision?: number;
  registry?: PlanAnalysisDraftRegistry;
  /** Only the caller's own form/operation owners; never exclude another editor's Plan owner. */
  excludedDirtyOwners?: ReadonlySet<string>;
  hasUnsavedDrafts?: boolean;
}
export interface PlanAnalysisDraftResolution {
  ok: boolean;
  captureKey: string;
  planDrafts: PlanAnalysisPlanDraft[];
  /** Only exact dirty owners whose required Plan is safely captured. */
  capturedOwners: ReadonlySet<string>;
  blockers: PlanAnalysisDraftBlocker[];
  /** Publications belonging to the requested Plan paths, including blocked publications. */
  entries: readonly PlanAnalysisDraftEntry[];
}

const backends = new WeakMap<BackendGateway, string>();
let nextBackend = 0;
export function planAnalysisBackendIdentity(backend: BackendGateway): string {
  let key = backends.get(backend);
  if (!key) { key = `analysis-backend-${++nextBackend}`; backends.set(backend, key); }
  return key;
}

/** Asset selection is deliberately absent: a retained editor still owns its original Plan. */
export function capturePlanAnalysisSource(context: WorkbenchContext, backend: BackendGateway, backendRevision = 0): PlanAnalysisDraftSource {
  return {
    projectSessionID: context.projectSessionID, projectPath: context.projectPath,
    projectRevision: context.projectRevision, projectXml: context.projectXml,
    coreLayerContext: context.coreLayerContext ? structuredClone(context.coreLayerContext) : null,
    backendIdentity: planAnalysisBackendIdentity(backend), backendRevision,
  };
}

/** Stable in-memory identity only. Do not log this key: it includes source XML and draft values. */
export function planAnalysisValueKey(value: unknown): string {
  return JSON.stringify(value, (_key, item: unknown) => item && typeof item === 'object' && !Array.isArray(item)
    ? Object.fromEntries(Object.entries(item).sort(([a], [b]) => a.localeCompare(b))) : item);
}
export function emptyPlanAnalysisDraftRegistry(): PlanAnalysisDraftRegistry {
  return { revision: 0, captureKey: 'plan-drafts:0', entries: [], sessionBusy: false, discardRevision: 0 };
}

/** Root-only reducer: replace an editor atomically; duplicate Plan owners remain visible to the resolver. */
export function publishPlanAnalysisDraft(registry: PlanAnalysisDraftRegistry, editorID: string,
  publication: PlanAnalysisDraftPublication | null): PlanAnalysisDraftRegistry {
  if (publication && publication.editorID !== editorID) throw new Error('计算计划草稿的编辑器身份不一致。');
  const prior = registry.entries.find(entry => entry.editorID === editorID);
  if (!publication && prior && (prior.writePending || prior.status === 'uncertain')) {
    const { captureRevision: _captureRevision, planDraft: _planDraft, ...previous } = prior;
    publication = { ...previous, busy: false, writePending: false, valid: false, status: 'uncertain',
      reason: '计算计划编辑器已卸载，但此前写入尚未确认；请先核对同一操作的终态。' };
  }
  if (!publication && !prior) return registry;
  if (publication && prior) {
    const { captureRevision: _captureRevision, ...previous } = prior;
    if (planAnalysisValueKey(previous) === planAnalysisValueKey(publication)) return registry;
  }
  const revision = registry.revision + 1;
  const entries = registry.entries.filter(entry => entry.editorID !== editorID);
  if (publication) entries.push(deepFreeze({ ...structuredClone(publication), captureRevision: revision }));
  return { ...registry, revision, entries: Object.freeze(entries), captureKey: `plan-drafts:${revision}:${registry.discardRevision}` };
}
export function clearPlanAnalysisDraftRegistry(registry: PlanAnalysisDraftRegistry, discard = false): PlanAnalysisDraftRegistry {
  if (!registry.entries.length && !discard) return registry;
  const revision = registry.revision + 1;
  const discardRevision = registry.discardRevision + (discard ? 1 : 0);
  return { ...registry, revision, discardRevision, entries: [], captureKey: `plan-drafts:${revision}:${discardRevision}` };
}

/** User navigation/discard must not retire a write. A matching save acknowledgement is a separate path. */
export function hasProtectedSourceWrite(owners: ReadonlySet<string>, registry: PlanAnalysisDraftRegistry,
  acknowledgedSource?: WorkbenchContext, recovery?: PlanSaveRecoveryAcknowledgement): boolean {
  return [...owners].some(owner => owner.startsWith('map.write:') || owner.startsWith('map.create:')
    || owner.startsWith('map.raster-write:') || owner.startsWith('geometry.mesh-export:') || owner.startsWith('tools.r2r-export:')
    || owner.startsWith('tools.stable-channel-write:') || owner.startsWith('project.projection-export:')
    || owner.startsWith('tools.project-archive:'))
    || registry.entries.some(entry => entry.status === 'uncertain'
      ? !matchesPlanSaveRecoveryAcknowledgement(entry, acknowledgedSource, recovery)
      : entry.writePending && !matchesAcknowledgedPlanSource(entry, acknowledgedSource));
}
export function matchesPlanSaveRecoveryAcknowledgement(entry: PlanAnalysisDraftEntry,
  source: WorkbenchContext | undefined, recovery: PlanSaveRecoveryAcknowledgement | undefined): boolean {
  return !!recovery && entry.status === 'uncertain' && entry.busy
    && entry.editorID === recovery.editorID && entry.ownerContextID === recovery.ownerContextID
    && !!entry.saveOperationID && entry.saveOperationID === recovery.operationID
    && !!entry.recoveryAttemptID && entry.recoveryAttemptID === recovery.attemptID
    && matchesAcknowledgedPlanSource(entry, source);
}
function matchesAcknowledgedPlanSource(entry: PlanAnalysisDraftEntry, source: WorkbenchContext | undefined): boolean {
  return !!source && entry.source.projectSessionID === source.projectSessionID
    && entry.source.projectPath === source.projectPath && entry.source.projectRevision === source.projectRevision
    && entry.source.projectXml === source.projectXml
    && planAnalysisValueKey(entry.source.coreLayerContext) === planAnalysisValueKey(source.coreLayerContext ?? null)
    && entry.assetPath === source.selectedAsset?.path
    && (!source.selectedAsset.revision || entry.revision?.toUpperCase() === source.selectedAsset.revision.toUpperCase());
}

/** Conservative matching: the server remains authoritative for canonical filesystem identity. */
export function planAnalysisPathKey(path: string): string { return path.replaceAll('\\', '/'); }

export function resolveAnalysisDrafts(capture: PlanAnalysisDraftCapture, dirtyOwners: ReadonlySet<string> | undefined,
  requiredPlans: readonly (string | { assetPath: string; revision?: string | null })[]): PlanAnalysisDraftResolution {
  const registry = capture.registry ?? emptyPlanAnalysisDraftRegistry();
  const owners = dirtyOwners ?? new Set<string>();
  const excluded = capture.excludedDirtyOwners ?? new Set<string>();
  const effectiveOwners = [...owners].filter(owner => !excluded.has(owner)
    || registry.entries.some(entry => entry.ownerContextID === owner));
  const unknownDirtyOwner = !!capture.hasUnsavedDrafts && owners.size === 0 && !registry.sessionBusy;
  const source = capturePlanAnalysisSource(capture.context, capture.backend, capture.backendRevision);
  const blockers: PlanAnalysisDraftBlocker[] = [];
  const capturedOwners = new Set<string>();
  const planDrafts: PlanAnalysisPlanDraft[] = [];
  const required = new Map<string, { assetPath: string; revision?: string | null }>();
  for (const raw of requiredPlans) {
    const item = typeof raw === 'string' ? { assetPath: raw } : raw;
    const key = planAnalysisPathKey(item.assetPath);
    const existing = required.get(key);
    if (existing?.revision && item.revision && existing.revision.toUpperCase() !== item.revision.toUpperCase())
      blockers.push({ code: 'requiredRevisionConflict', message: '同一计算计划的来源修订不一致，请重新读取选项。', assetPath: item.assetPath });
    required.set(key, existing?.revision ? existing : item);
  }
  const entries = registry.entries.filter(entry => required.has(planAnalysisPathKey(entry.assetPath)));
  const add = (entry: PlanAnalysisDraftEntry, code: string, message: string): void => {
    blockers.push({ code, message, ownerContextID: entry.ownerContextID, assetPath: entry.assetPath });
  };
  if (!source.projectSessionID || !source.projectPath || !source.projectRevision || !source.projectXml)
    blockers.push({ code: 'missingProject', message: '请先打开具有完整来源身份的已保存工程。' });
  if (registry.sessionBusy) blockers.push({ code: 'sessionBusy', message: '工程来源正在写入，请等待事务完成后分析。' });
  if (unknownDirtyOwner)
    blockers.push({ code: 'unknownDirtyOwner', message: '存在未明确归属的未保存状态，请返回编辑器确认后重试。' });

  for (const [path, requiredPlan] of required) {
    const matches = entries.filter(entry => planAnalysisPathKey(entry.assetPath) === path);
    if (matches.length > 1) {
      for (const entry of matches) add(entry, 'duplicatePlanOwner', '同一计算计划有多个编辑器来源，无法确定唯一草稿。');
      continue;
    }
    const entry = matches[0];
    if (!entry) continue; // An unopened, clean Plan is read by the Host; any unmatched dirty owner blocks below.
    if (planAnalysisValueKey(entry.source) !== planAnalysisValueKey(source)) {
      add(entry, 'sourceMismatch', '计算计划草稿所属的工程、会话或服务来源已变化，请重新载入该编辑器。'); continue;
    }
    if (!entry.revision || (requiredPlan.revision && entry.revision.toUpperCase() !== requiredPlan.revision.toUpperCase())) {
      add(entry, 'revisionMismatch', '计算计划草稿与所选来源修订不一致，请重新读取来源。'); continue;
    }
    if (entry.busy || !entry.valid || !['clean', 'draft'].includes(entry.status)) {
      add(entry, entry.status, entry.reason || '计算计划草稿尚不能安全捕获，请先处理编辑器中的状态。'); continue;
    }
    if (!entry.dirty) {
      if (entry.status !== 'clean' || owners.has(entry.ownerContextID)) add(entry, 'ownerStateMismatch', '计算计划编辑器与草稿登记状态不一致，请等待状态同步后重试。');
      continue;
    }
    const draft = entry.planDraft;
    if (entry.status !== 'draft' || !owners.has(entry.ownerContextID) || !draft
      || draft.ownerContextID !== entry.ownerContextID || draft.editorID !== entry.editorID
      || planAnalysisPathKey(draft.assetPath) !== path || draft.revision.toUpperCase() !== entry.revision.toUpperCase()
      || !draft.fields || Object.values(draft.fields).some(value => typeof value !== 'string')
      || draft.planTimeWindowEdits?.some(edit => !edit || !edit.id || !edit.fieldID || typeof edit.value !== 'string')) {
      add(entry, 'uncapturableOwner', '未保存计算计划缺少完整且唯一的草稿回执，不能改用已保存版本。'); continue;
    }
    if (registry.entries.some(other => other.editorID !== entry.editorID && other.ownerContextID === entry.ownerContextID)) {
      add(entry, 'duplicateDirtyOwner', '草稿归属被多个编辑器占用，无法安全分析。'); continue;
    }
    capturedOwners.add(entry.ownerContextID);
    planDrafts.push(structuredClone(draft));
  }
  // A publisher may arrive before its separate dirty-owner effect; never treat that gap as clean.
  for (const entry of registry.entries) {
    if (entry.dirty && !owners.has(entry.ownerContextID)) add(entry, 'unregisteredDirtyOwner', '计算计划草稿正在同步归属，请稍后重试。');
    if ((entry.busy || entry.status === 'uncertain') && !entries.includes(entry))
      add(entry, entry.status, entry.reason || '另一计算计划仍有正在进行或未确认的操作。');
  }
  for (const owner of owners) {
    if (capturedOwners.has(owner)) continue;
    // Even an explicit exclusion cannot suppress a published Plan editor's owner.
    if (excluded.has(owner) && !registry.entries.some(entry => entry.ownerContextID === owner)) continue;
    blockers.push({ code: 'uncapturedDirtyOwner', message: '还有未被本次分析完整捕获的未保存编辑或写入，请先处理。', ownerContextID: owner });
  }
  const captureKey = planAnalysisValueKey({ source, registry, required: [...required.values()],
    owners: effectiveOwners.sort(), unknownDirtyOwner });
  return { ok: blockers.length === 0, captureKey, planDrafts: blockers.length ? [] : planDrafts, capturedOwners, blockers, entries };
}

function deepFreeze<T>(value: T): T {
  if (value && typeof value === 'object') {
    for (const child of Object.values(value)) deepFreeze(child);
    Object.freeze(value);
  }
  return value;
}
