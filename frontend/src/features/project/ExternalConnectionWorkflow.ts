import type { BackendGateway, WorkbenchContext } from '../../shared/workbench';
import { validateSessionReferenceContext } from '../../shared/sessionReferences';
import { exactCoreLayerContext } from '../tools/coreImportWorkflow';
import { isProjectTreeSHA256, projectTreeXmlSHA } from './ProjectTreeModelActions';

export type ExternalConnectionAction = 'add' | 'update' | 'remove';
export type ExternalConnectionKind = 'file' | 'directory' | 'result';

export interface ExternalConnectionValues {
  action: ExternalConnectionAction;
  kind: ExternalConnectionKind;
  connectionName?: string;
  name?: string;
  path?: string;
  description?: string;
  searchPattern?: string;
  recurse?: boolean;
  resultName?: string;
}

export interface ExternalConnectionIdentity {
  name: string;
  type: string;
  path?: string | null;
  description?: string | null;
  connectionSHA256?: string | null;
  sourcePath?: string | null;
  sourceSHA256?: string | null;
  sourceExists?: boolean;
  searchPattern?: string;
  recurse?: boolean;
}

export interface ExternalConnectionInspectionRequest extends ExternalConnectionValues {
  projectPath: string;
  projectXml: string;
  projectXmlSHA: string;
  projectRevision: string;
  projectSessionID: string;
  coreLayerContext?: Record<string, unknown>;
}

export interface ExternalConnectionInspection extends ExternalConnectionValues {
  canCommit: boolean;
  validationMessage?: string | null;
  projectPath: string;
  projectRevision: string;
  projectXmlSHA: string;
  projectSessionID: string;
  canonicalPath: string | null;
  connectionSHA256?: string | null;
  sourceSHA256?: string | null;
  sourceExists: boolean;
  sourceFileCount: number;
  sourceReferencesSHA256: string;
  normalizedName?: string | null;
  before?: ExternalConnectionIdentity | null;
  after?: ExternalConnectionIdentity | null;
}

export interface ExternalConnectionProjectSnapshot {
  success: boolean;
  projectPath: string;
  projectXml: string;
  revision: string;
  projectXmlSHA: string;
  projectSessionID: string;
  coreLayerContext?: Record<string, unknown> | null;
}

export interface ExternalConnectionCommitReceipt {
  success: boolean;
  action: ExternalConnectionAction;
  kind: ExternalConnectionKind;
  project: ExternalConnectionProjectSnapshot;
  previousProjectRevision: string;
  before?: ExternalConnectionIdentity | null;
  after?: ExternalConnectionIdentity | null;
  sourceCapture?: { path: string | null; sha256: string | null; exists: boolean; fileCount: number } | null;
  previousSourceReferencesSHA256: string;
  sourceReferencesSHA256: string;
}

export interface ExternalConnectionCommitResult {
  inspection: ExternalConnectionInspection;
  receipt: ExternalConnectionCommitReceipt;
  source: WorkbenchContext;
  committed: WorkbenchContext;
}

/** The project write is known to have committed, but its rebased session capability cannot be adopted. */
export class ExternalConnectionCommittedContextError extends Error {
  constructor(readonly receipt: ExternalConnectionCommitReceipt, message: string) {
    super(message);
    this.name = 'ExternalConnectionCommittedContextError';
  }
}

export function isSameServerPath(left: string, right: string): boolean {
  const normalize = (value: string): string => value.replace(/\\/g, '/').replace(/\/+$/, '');
  const a = normalize(left);
  const b = normalize(right);
  const isWindowsPath = (value: string): boolean => /^[A-Za-z]:\//.test(value) || value.startsWith('//');
  return isWindowsPath(a) && isWindowsPath(b) ? a.toLowerCase() === b.toLowerCase() : a === b;
}

function requireProjectContext(context: WorkbenchContext): { projectPath: string; projectXml: string; projectRevision: string; projectSessionID: string } {
  const { projectPath, projectXml, projectRevision, projectSessionID } = context;
  if (!projectPath || !projectXml || !projectRevision || !/^[0-9a-f]{64}$/i.test(projectRevision) ||
      !projectSessionID || !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(projectSessionID)) {
    throw new Error('外部连接操作需要当前打开的服务器工程、XML SHA-256 与 GUID 工程会话。');
  }
  return { projectPath, projectXml, projectRevision, projectSessionID };
}

function validateInspection(
  inspection: ExternalConnectionInspection,
  request: ExternalConnectionInspectionRequest,
): void {
  if (!inspection || inspection.projectPath !== request.projectPath ||
      !isProjectTreeSHA256(inspection.projectRevision) || inspection.projectRevision.toUpperCase() !== request.projectRevision.toUpperCase() ||
      !isProjectTreeSHA256(inspection.projectXmlSHA) || inspection.projectXmlSHA.toUpperCase() !== request.projectXmlSHA.toUpperCase() ||
      typeof inspection.projectSessionID !== 'string' || inspection.projectSessionID.toLowerCase() !== request.projectSessionID.toLowerCase() ||
      inspection.action !== request.action || inspection.kind !== request.kind ||
      !isProjectTreeSHA256(inspection.sourceReferencesSHA256)) {
    throw new Error('外部连接检查回执与当前工程会话、源修订或连接操作不匹配。');
  }
  if (!inspection.canCommit) return;
  if ((request.action !== 'remove' && !inspection.canonicalPath) || (inspection.canonicalPath?.includes('\0') ?? false)) {
    throw new Error('工程服务未返回连接的规范路径。');
  }
  if (request.action !== 'remove' && !isProjectTreeSHA256(inspection.sourceSHA256)) {
    throw new Error('工程服务未返回目标源文件的 SHA-256。');
  }
  if (request.action === 'add' && !inspection.normalizedName) {
    throw new Error('工程服务未返回新连接的规范名称。');
  }
  if (request.action !== 'add' && (!inspection.connectionSHA256 || !isProjectTreeSHA256(inspection.connectionSHA256))) {
    throw new Error('工程服务未返回当前连接的 XML 摘要。');
  }
}

function committedSelectedAsset(
  source: WorkbenchContext,
  project: ExternalConnectionProjectSnapshot,
  coreLayerContext: Record<string, unknown> | null,
): WorkbenchContext['selectedAsset'] {
  const asset = source.selectedAsset;
  if (!asset) return null;
  let locator = asset.locator;
  if ((locator && typeof locator === 'object' && !Array.isArray(locator)) || coreLayerContext) {
    const value = locator && typeof locator === 'object' && !Array.isArray(locator)
      ? locator as Record<string, unknown>
      : {};
    const next: Record<string, unknown> = { ...value, coreLayerContext };
    delete next.CoreLayerContext;
    locator = next;
  }
  if (asset.kind === 'ProjectFile' && isSameServerPath(asset.path, project.projectPath)) {
    return { ...asset, revision: project.revision, locator };
  }
  return locator === asset.locator ? asset : { ...asset, locator };
}

export async function inspectAndCommitExternalConnection(
  backend: BackendGateway,
  context: WorkbenchContext,
  values: ExternalConnectionValues,
  signal?: AbortSignal,
  isSourceCurrent: () => boolean = () => true,
): Promise<ExternalConnectionCommitResult> {
  const source = context;
  const project = requireProjectContext(source);
  if (!backend.supports('project.connection.inspect') || !backend.supports('project.connection.commit')) {
    throw new Error('当前工程服务未同时提供 project.connection.inspect 与 project.connection.commit。');
  }
  const projectXmlSHA = await projectTreeXmlSHA(project.projectXml);
  if (signal?.aborted) throw new DOMException('The request was aborted.', 'AbortError');
  const carriedCore = carriedCoreLayerContext(source);
  const coreLayerContext = exactCoreLayerContext(source, source.selectedAsset);
  if (carriedCore != null && !coreLayerContext) {
    throw new Error('当前工程携带的外部来源身份与工程修订不匹配；未提交连接更改。请重新读取工程目录。');
  }
  if (coreLayerContext) validateSessionReferenceContext(coreLayerContext, source);
  const inspectionRequest: ExternalConnectionInspectionRequest = {
    ...project,
    projectXmlSHA,
    ...values,
    ...(coreLayerContext ? { coreLayerContext } : {}),
  };
  const inspection = await backend.request<ExternalConnectionInspection>(
    'project.connection.inspect', inspectionRequest, { signal });
  validateInspection(inspection, inspectionRequest);
  if (!isSourceCurrent() || signal?.aborted) throw new DOMException('The project context changed during inspection.', 'AbortError');
  if (!inspection.canCommit) throw new Error(inspection.validationMessage || '工程服务拒绝此连接变更。');

  const commitRequest = {
    ...inspectionRequest,
    path: inspection.canonicalPath,
    expectedConnectionSHA256: inspection.connectionSHA256 ?? null,
    expectedSourceSHA256: inspection.sourceSHA256 ?? null,
    expectedSourceReferencesSHA256: inspection.sourceReferencesSHA256,
    ...(values.action === 'add' ? { expectedNormalizedName: inspection.normalizedName } : {}),
  };
  // The captured source must still be current immediately before the irreversible project write.
  if (signal?.aborted || !isSourceCurrent()) throw new DOMException('The project context changed before commit.', 'AbortError');
  const receipt = await backend.request<ExternalConnectionCommitReceipt>(
    'project.connection.commit', commitRequest, { signal });
  // Once the Host has returned a commit receipt, validate it against the immutable captured
  // source even if the UI owner changed or the caller's signal was aborted meanwhile. The
  // parent must decide whether that acknowledged receipt can still be adopted.
  const committedCore = await validateCommitReceipt(receipt, source, values, inspection, coreLayerContext ?? null);
  const committed: WorkbenchContext = {
    ...source,
    projectXml: receipt.project.projectXml,
    projectRevision: receipt.project.revision,
    coreLayerContext: committedCore,
    selectedAsset: committedSelectedAsset(source, receipt.project, committedCore),
  };
  return { inspection, receipt, source, committed };
}

function carriedCoreLayerContext(context: WorkbenchContext): unknown {
  if (context.coreLayerContext !== undefined) return context.coreLayerContext;
  const locator = context.selectedAsset?.locator;
  if (!locator || typeof locator !== 'object' || Array.isArray(locator)) return undefined;
  const value = locator as Record<string, unknown>;
  return value.coreLayerContext ?? value.CoreLayerContext;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function coreCaptures(core: Record<string, unknown>): string {
  const read = (lower: string, upper: string): unknown => core[lower] ?? core[upper];
  const normalize = (value: unknown): unknown[] => Array.isArray(value)
    ? value.map((item) => {
      if (!isRecord(item)) return item;
      const field = (lower: string, upper: string) => item[lower] ?? item[upper] ?? null;
      return [
        field('referenceID', 'ReferenceID'),
        field('projectSessionID', 'ProjectSessionID'),
        field('projectPath', 'ProjectPath'),
        field('kind', 'Kind'),
        field('sourcePath', 'SourcePath'),
        field('canonicalSourcePath', 'CanonicalSourcePath'),
        field('sourceRevision', 'SourceRevision'),
        field('name', 'Name'),
        field('sessionOnly', 'SessionOnly'),
      ];
    }).sort((left, right) => JSON.stringify(left).localeCompare(JSON.stringify(right)))
    : [];
  return JSON.stringify([normalize(read('references', 'References')), normalize(read('sessionHdfReferences', 'SessionHdfReferences'))]);
}

async function validateCommitReceipt(
  receipt: ExternalConnectionCommitReceipt,
  source: WorkbenchContext,
  values: ExternalConnectionValues,
  inspection: ExternalConnectionInspection,
  sourceCore: Record<string, unknown> | null,
): Promise<Record<string, unknown> | null> {
  const project = receipt?.project;
  if (!receipt || receipt.success !== true || receipt.action !== values.action || receipt.kind !== values.kind ||
      receipt.previousProjectRevision?.toUpperCase() !== source.projectRevision?.toUpperCase() ||
      !project || project.success !== true || project.projectPath !== source.projectPath ||
      project.projectSessionID?.toLowerCase() !== source.projectSessionID.toLowerCase() ||
      typeof project.projectXml !== 'string' || !isProjectTreeSHA256(project.revision) ||
      !isProjectTreeSHA256(project.projectXmlSHA) || !isProjectTreeSHA256(receipt.previousProjectRevision)) {
    throw new Error('工程服务未返回与当前工程会话和源修订匹配的连接提交回执。');
  }
  if (!inspection.sourceReferencesSHA256 || !receipt.sourceCapture ||
      receipt.previousSourceReferencesSHA256?.toUpperCase() !== inspection.sourceReferencesSHA256.toUpperCase() ||
      !isProjectTreeSHA256(receipt.sourceReferencesSHA256)) {
    throw new Error('连接提交回执缺少源文件捕获信息。');
  }
  if (values.action !== 'add' && (!receipt.before || receipt.before.name !== values.connectionName ||
      receipt.before.connectionSHA256?.toUpperCase() !== inspection.connectionSHA256?.toUpperCase())) {
    throw new Error('连接提交回执中的原连接身份与所选连接不匹配。');
  }
  const capturePath = receipt.sourceCapture.path;
  if (capturePath && inspection.canonicalPath && !isSameServerPath(capturePath, inspection.canonicalPath)) {
    throw new Error('连接提交回执捕获了不同的源路径。');
  }
  if (receipt.sourceCapture.sha256?.toUpperCase() !== inspection.sourceSHA256?.toUpperCase() ||
      receipt.sourceCapture.exists !== inspection.sourceExists || receipt.sourceCapture.fileCount !== inspection.sourceFileCount) {
    throw new Error('连接提交回执的源文件捕获与检查结果不匹配。');
  }
  if (values.action === 'remove') {
    if (receipt.after !== null && receipt.after !== undefined) throw new Error('删除连接回执仍包含提交后的连接。');
  } else {
    const expectedName = values.action === 'add' ? inspection.normalizedName : values.name;
    const expectedType = values.kind === 'directory' ? 'Directory' : 'File';
    if (!receipt.after || receipt.after.name !== expectedName ||
        receipt.after.type !== expectedType ||
        !receipt.after.path || !inspection.canonicalPath || !isSameServerPath(receipt.after.path, inspection.canonicalPath) ||
        !isProjectTreeSHA256(receipt.after.connectionSHA256) ||
        receipt.after.sourceSHA256?.toUpperCase() !== inspection.sourceSHA256?.toUpperCase() ||
        (values.description ?? '') !== (receipt.after.description ?? '')) {
      throw new Error('连接提交回执中的连接名称或规范路径与检查结果不匹配。');
    }
  }
  const actualXmlSha = await projectTreeXmlSHA(project.projectXml);
  if (actualXmlSha.toUpperCase() !== project.projectXmlSHA.toUpperCase()) {
    throw new Error('工程 XML SHA-256 不匹配。');
  }

  const committedProjectContext: WorkbenchContext = {
    ...source,
    projectXml: project.projectXml,
    projectRevision: project.revision,
  };
  if (project.coreLayerContext == null) {
    if (sourceCore) {
      throw new ExternalConnectionCommittedContextError(receipt,
        '工程连接已提交，但回执没有返回重基后的外部来源身份。为保留现有引用，当前会话未接纳新工程修订；请重新读取工程目录核对。');
    }
    return null;
  }
  if (!isRecord(project.coreLayerContext)) {
    throw new ExternalConnectionCommittedContextError(receipt,
      '工程连接已提交，但回执中的外部来源身份格式无效。当前会话未接纳新工程修订；请重新读取工程目录核对。');
  }
  try {
    validateSessionReferenceContext(project.coreLayerContext, committedProjectContext);
    if (sourceCore && coreCaptures(project.coreLayerContext) !== coreCaptures(sourceCore)) {
      throw new Error('工程连接回执改变了已捕获的外部来源身份。');
    }
  } catch (error) {
    const detail = error instanceof Error ? ` ${error.message}` : '';
    throw new ExternalConnectionCommittedContextError(receipt,
      `工程连接已提交，但回执中的重基外部来源身份未通过校验。当前会话未接纳新工程修订；请重新读取工程目录核对。${detail}`);
  }
  return project.coreLayerContext;
}
