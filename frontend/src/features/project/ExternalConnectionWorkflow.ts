import type { BackendGateway, WorkbenchContext } from '../../shared/workbench';
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

function committedSelectedAsset(source: WorkbenchContext, project: ExternalConnectionProjectSnapshot): WorkbenchContext['selectedAsset'] {
  const asset = source.selectedAsset;
  if (!asset) return null;
  let locator = asset.locator;
  if (project.coreLayerContext && locator && typeof locator === 'object') {
    const value = locator as Record<string, unknown>;
    const next = { ...value };
    if ('CoreLayerContext' in value) next.CoreLayerContext = project.coreLayerContext;
    if ('coreLayerContext' in value || !('CoreLayerContext' in value)) next.coreLayerContext = project.coreLayerContext;
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
  const coreLayerContext = exactCoreLayerContext(source, source.selectedAsset);
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
  const receipt = await backend.request<ExternalConnectionCommitReceipt>(
    'project.connection.commit', commitRequest, { signal });
  if (signal?.aborted || !isSourceCurrent()) throw new DOMException('The project context changed during commit.', 'AbortError');
  await validateCommitReceipt(receipt, source, values, inspection);
  const committed: WorkbenchContext = {
    ...source,
    projectXml: receipt.project.projectXml,
    projectRevision: receipt.project.revision,
    selectedAsset: committedSelectedAsset(source, receipt.project),
  };
  return { inspection, receipt, source, committed };
}

async function validateCommitReceipt(
  receipt: ExternalConnectionCommitReceipt,
  source: WorkbenchContext,
  values: ExternalConnectionValues,
  inspection: ExternalConnectionInspection,
): Promise<void> {
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
}
