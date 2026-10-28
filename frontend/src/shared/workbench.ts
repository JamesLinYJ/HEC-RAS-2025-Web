/** Shared UI/session contracts. Backend operation names and payloads follow the retained C# Host. */
export type Language = 'zh' | 'en';
export type ThemeMode = 'light' | 'dark' | 'system';
export type FeatureId = 'project' | 'editors' | 'geometry' | 'map' | 'results' | 'tools' | 'compute';
export interface AssetReference {
  path: string;
  kind: string;
  name: string;
  revision?: string;
  locator?: object;
}
export interface WorkbenchContext {
  projectSessionID: string;
  projectPath: string | null;
  projectXml: string | null;
  /** Revision of the .ras file, independent of the selected model's revision. */
  projectRevision: string | null;
  selectedAsset: AssetReference | null;
  /** Verified Host capability for external sources in this open project session. */
  coreLayerContext?: Record<string, unknown> | null;
}
export interface BackendRequestOptions {
  signal?: AbortSignal;
  onProgress?: (progress: unknown) => void;
  /** Transport-layer status only; these events never represent solver progress. */
  onTransportState?: (state: BackendTransportState) => void;
}
export type BackendTransportState =
  | { state: 'reconnecting'; operationId: string; attempt: number; retryInMilliseconds: number }
  | { state: 'connected'; operationId: string; recoveredAfterAttempt: number }
  | { state: 'cancelRequested'; operationId: string }
  | { state: 'cancelRetrying'; operationId: string; attempt: number; retryInMilliseconds: number; code: string }
  | { state: 'cancelSent'; operationId: string; accepted: boolean }
  | { state: 'cancelFailed'; operationId: string; code: string; diagnosticMessage: string };
export interface BackendGateway {
  supports(method: string): boolean;
  request<T = unknown>(method: string, parameters: unknown, options?: BackendRequestOptions): Promise<T>;
  /** Follow an accepted HTTP task without posting it again. An explicit abort requests cancellation and still tracks its terminal state. */
  resumeOperation?<T = unknown>(operationId: string, options?: BackendRequestOptions): Promise<T>;
  /** Fires after the advertised operation set changes, without replacing the gateway instance. */
  subscribeCapabilities?(listener: (revision: number) => void): () => void;
  /** Upload a workspace archive/directory through the host's dedicated import endpoint. */
  uploadWorkspace?(files: File[], options?: Pick<BackendRequestOptions, 'signal'>): Promise<WorkspaceUploadResult>;
  /** Browser download URL for a successfully uploaded workspace; the server streams the archive. */
  workspaceArchiveUrl?(workspaceId: string): string;
  projectArchiveUrl?(cleanupToken: string): string;
  /** Copies selected source files into the owning project before a separate map import commits. */
  uploadMapSources?(context: WorkbenchContext, files: File[], options?: Pick<BackendRequestOptions, 'signal'> & { directory?: boolean }): Promise<MapSourceUploadResult>;
}
export interface UploadedMapSource {
  sourcePath: string;
  sourceCanonicalPath: string;
  sourceRevision: string;
  kind: 'geojson' | 'shapefile' | 'raster' | 'gdb';
  fileName: string;
}
export interface MapSourceUploadResult {
  uploadID: string;
  projectPath: string;
  projectRevision: string;
  projectSessionID: string;
  sources: UploadedMapSource[];
}
export interface WorkspaceUploadResult {
  workspaceId: string;
  rootPath: string;
  projects: { path: string; name: string }[];
  fileCount: number;
}
export interface ContextChangeOptions {
  /** Selecting a catalog row can retain the current page; opening it can name its target. */
  navigation?: 'auto' | 'preserve' | FeatureId;
}
/** A saved map captured before a cross-workspace visualization transaction. */
export interface MapVisualizationTarget {
  mapID: string;
  mapIndex: number;
  name: string;
  mapType: string;
  visualizationRevision: string | null;
  /** A document operation reserves its intended identity change with the old map. */
  documentMutation?:
    | { action: 'rename'; newName: string }
    | { action: 'delete'; mapIndexChanges: MapDocumentIndexChange[] };
}
export interface MapVisualizationCommitReceipt {
  mapIndex: number;
  mapName: string;
  mapType: string;
  previousVisualizationRevision: string | null;
  visualizationRevision: string;
}
export interface MapDocumentIndexChange {
  previousMapIndex: number;
  mapIndex: number;
  mapName: string;
  mapType: string;
}
export interface MapDocumentDeleteReceipt {
  removedMapIndex: number;
  removedMapName: string;
  removedMapType: string;
  previousVisualizationRevision: string | null;
  visualizationRevision: string;
  mapIndexChanges: MapDocumentIndexChange[];
}
/** Preserve the Host's actual document receipt rather than synthesizing a layer-write receipt. */
export type MapVisualizationAcknowledgement = MapVisualizationCommitReceipt
  | { documentAction: 'rename'; receipt: MapVisualizationCommitReceipt }
  | { documentAction: 'delete'; receipt: MapDocumentDeleteReceipt };
export interface FeaturePageProps {
  /** Live map calculator layers belong only to this open project session. */
  calculatorSessions?: import('./rasterCalculatorSession').RasterCalculatorSessions;
  language: Language;
  /** User preference; system is resolved by the top-level window. */
  themeMode?: ThemeMode;
  onThemeModeChange?: (mode: ThemeMode) => void;
  context: WorkbenchContext;
  backend: BackendGateway;
  onContextChange: (context: WorkbenchContext, options?: ContextChangeOptions) => void;
  /** Adopt an acknowledged save only while its source is still the active context. */
  onContextCommit?: (source: WorkbenchContext, committed: WorkbenchContext,
    recovery?: import('./planAnalysisDrafts').PlanSaveRecoveryAcknowledgement) => boolean;
  onNavigate: (feature: FeatureId) => void;
  /** Reserve the current clean project while adding a source to a saved map. */
  onBeginSessionReferenceMutation?: (source: WorkbenchContext) => string | null;
  onCommitSessionReferences?: (source: WorkbenchContext, nextContext: Record<string, unknown>, leaseID: string) => boolean;
  onEndSessionReferenceMutation?: (leaseID: string) => void;
  /** Reserve the current project while a tool writes to one exact saved map. */
  onBeginMapVisualizationMutation?: (source: WorkbenchContext, target: MapVisualizationTarget, draftOwner: string) => string | null;
  onCommitMapVisualization?: (source: WorkbenchContext, target: MapVisualizationTarget, receipt: MapVisualizationAcknowledgement, leaseID: string) => boolean;
  onEndMapVisualizationMutation?: (leaseID: string) => void;
  /** Preserve one editor's draft while committing an acknowledged project-only change. */
  onBeginProjectContextMutation?: (source: WorkbenchContext, draftOwner: string) => string | null;
  onCommitProjectContextMutation?: (source: WorkbenchContext, committed: WorkbenchContext, leaseID: string) => boolean;
  onEndProjectContextMutation?: (leaseID: string) => void;
  /** Retain a Channel terrain's payloads until its original model.save has a confirmed outcome. */
  onBeginModelSave?: (source: WorkbenchContext, draftOwner: string, writeOwner: string, sourceRevision: string) => string | null;
  onTrackModelSave?: (leaseID: string, operationID: string) => boolean;
  onCommitModelSave?: (source: WorkbenchContext, committed: WorkbenchContext, leaseID: string, operationID: string) => boolean;
  /** Release only after a confirmed failure or cancellation; unknown outcomes retain the lease. */
  onEndModelSave?: (leaseID: string) => void;
  /** Invalidates saved-map captures after an acknowledged write in another workspace. */
  mapRevision?: number;
  onDirtyStateChange?: (owner: string, dirty: boolean) => void;
  /** Changes when the server's advertised operation set is refreshed. */
  backendRevision?: number;
  hasUnsavedDrafts?: boolean;
  /** Exact draft owners allow a save to exclude its own draft, while guarding other editors. */
  dirtyOwners?: ReadonlySet<string>;
  /** Immutable, source-bound Plan editor publications for read-only analysis and matrix generation. */
  planAnalysisDrafts?: import('./planAnalysisDrafts').PlanAnalysisDraftRegistry;
  onPlanAnalysisDraftChange?: (editorID: string, publication: import('./planAnalysisDrafts').PlanAnalysisDraftPublication | null) => void;
}
export class BackendOperationUnavailableError extends Error {
  readonly code = 'operationUnavailable';
  constructor(readonly method: string) {
    super(`后端尚未发布操作：${method}。`);
    this.name = 'BackendOperationUnavailableError';
  }

  localizedMessage(language: Language): string {
    return language === 'zh'
      ? '当前 C# 服务暂不支持此项操作。'
      : `The backend has not advertised the ${this.method} operation.`;
  }
}

interface LocalizedError {
  localizedMessage?: (language: Language) => string;
}

/** User-facing error text. Backend adapters may localize while retaining diagnostics on the error object. */
export function message(error: unknown, language: Language = 'zh'): string {
  if (error && typeof error === 'object') {
    const localized = (error as LocalizedError).localizedMessage;
    if (typeof localized === 'function') {
      try { return localized.call(error, language); }
      catch { /* Fall through to the stable Error message. */ }
    }
  }
  return error instanceof Error ? error.message : String(error);
}

/** Raw diagnostics are for expandable details/logs, not primary UI copy. */
export function errorDiagnostics(error: unknown): unknown {
  if (!error || typeof error !== 'object') return error;
  const candidate = error as {
    code?: unknown; method?: unknown; status?: unknown; operation?: unknown; operationId?: unknown;
    diagnosticMessage?: unknown; details?: unknown; diagnosticCause?: unknown; cause?: unknown;
    terminalConfirmed?: unknown;
  };
  const diagnostics: Record<string, unknown> = {};
  if (typeof candidate.code === 'string') diagnostics.code = candidate.code;
  if (typeof candidate.method === 'string') diagnostics.method = candidate.method;
  if (typeof candidate.status === 'number') diagnostics.status = candidate.status;
  if (typeof candidate.operation === 'string') diagnostics.operation = candidate.operation;
  if (typeof candidate.operationId === 'string') diagnostics.operationId = candidate.operationId;
  if (typeof candidate.terminalConfirmed === 'boolean') diagnostics.terminalConfirmed = candidate.terminalConfirmed;
  if (typeof candidate.diagnosticMessage === 'string') diagnostics.message = candidate.diagnosticMessage;
  if (candidate.details !== undefined) diagnostics.details = candidate.details;
  if (candidate.diagnosticCause !== undefined) diagnostics.cause = candidate.diagnosticCause;
  else if (candidate.cause !== undefined) diagnostics.cause = candidate.cause;
  return Object.keys(diagnostics).length > 0 ? diagnostics : error;
}
export function text(language: Language, zh: string, en: string): string {
  return language === 'zh' ? zh : en;
}

export function assetKindLabel(kind: string, language: Language): string {
  if (language === 'en') return kind;
  return ({
    Project: '工程', ProjectFile: '工程文件', Plan: '计算计划', Geometry: '几何模型',
    Terrain: '地形', Result: '计算结果', Results: '计算结果',
    'Boundary Conditions': '边界条件', BoundaryCondition: '边界条件',
    'Surface Layers': '地表图层', NValues: '糙率图层', NValue: '糙率图层',
    Classification: '地表分类',
  } as Record<string, string>)[kind] ?? kind;
}
