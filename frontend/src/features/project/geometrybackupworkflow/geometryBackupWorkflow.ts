import type { BackendGateway, WorkbenchContext } from '../../../shared/workbench';
import { exactCoreLayerContext, hasUncapturedSessionReferences, modelLoadRequest } from '../../tools/coreImportWorkflow';
import type { ModelSnapshot } from '../../editors/model';
import type { CatalogTreeNode } from '../CatalogTreeSnapshot';
import { isProjectTreeSHA256, projectTreeXmlSHA } from '../ProjectTreeModelActions';
import { isSameServerPath } from '../ExternalConnectionWorkflow';
import { isAbsoluteServerPath } from '../ProjectTypes';

export interface GeometryNamedBackupReceipt {
  projectPath: string;
  projectRevision: string;
  projectXmlFingerprint: string;
  geometryPath: string;
  geometryRevision: string;
  backupPath: string;
  backupRevision: string;
}

export interface GeometryBackupRestoreReceipt {
  projectPath: string;
  backupPath: string;
  backupRevision: string;
  parentGeometryPath: string;
  parentGeometryRevision: string;
  projectRevision: string;
  newAssetPath: string;
  newName: string;
  newAssetRevision: string;
  projectXmlFingerprint: string;
  cleanupWarning?: string | null;
}

export type GeometryBackupCommittedEvent =
  | { action: 'create'; receipt: GeometryNamedBackupReceipt }
  | { action: 'restore'; receipt: GeometryBackupRestoreReceipt };

export type GeometryBackupCommittedCallback = (
  source: WorkbenchContext,
  event: GeometryBackupCommittedEvent,
) => void | Promise<void>;

export interface GeometryBackupActionIdentity {
  context: WorkbenchContext;
  node: CatalogTreeNode;
  ownerKey: string;
  coreLayerContext?: Record<string, unknown>;
}

interface WorkspaceFileCapture {
  sourcePath: string;
  canonicalPath: string;
  revision: string;
  length: number;
}

function workspaceFileCaptureMatches(value: unknown, sourcePath: string): value is WorkspaceFileCapture {
  if (!value || typeof value !== 'object') return false;
  const capture = value as Partial<WorkspaceFileCapture>;
  return typeof capture.sourcePath === 'string' && isSameServerPath(capture.sourcePath, sourcePath) &&
    typeof capture.canonicalPath === 'string' && isAbsoluteServerPath(capture.canonicalPath) &&
    isProjectTreeSHA256(capture.revision) && Number.isSafeInteger(capture.length) && (capture.length ?? -1) >= 0;
}

export function geometryBackupOwnerKey(context: WorkbenchContext, node: CatalogTreeNode): string {
  const asset = context.selectedAsset;
  return JSON.stringify([
    context.projectSessionID,
    context.projectPath,
    context.projectXml,
    context.projectRevision,
    context.coreLayerContext ?? null,
    asset?.path ?? null,
    asset?.kind ?? null,
    asset?.revision ?? null,
    asset?.locator ?? null,
    node.id,
    node.sourceClass,
    node.name,
    node.assetID ?? null,
    node.locator ?? null,
    node.backupSummary ?? null,
  ]);
}

export function captureGeometryBackupIdentity(
  context: WorkbenchContext,
  node: CatalogTreeNode,
  hasUnsavedDrafts: boolean,
): GeometryBackupActionIdentity {
  if (hasUnsavedDrafts) {
    throw new Error('请先保存或处理未保存的模型草稿，再执行几何备份操作。');
  }
  if (!context.projectPath || !isAbsoluteServerPath(context.projectPath) ||
      context.projectPath.startsWith('browser-file://') || typeof context.projectXml !== 'string' ||
      !isProjectTreeSHA256(context.projectRevision) ||
      !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(context.projectSessionID)) {
    throw new Error('几何备份需要已保存的服务器工程、有效工程 SHA-256 和当前 GUID 会话。');
  }
  if (hasUncapturedSessionReferences(context, context.selectedAsset)) {
    throw new Error('当前工程包含尚未捕获的会话专属引用；请重新加载工程后再备份。');
  }
  const coreLayerContext = exactCoreLayerContext(context, context.selectedAsset);
  if (context.coreLayerContext && !coreLayerContext) {
    throw new Error('当前核心图层引用与工程会话不匹配；请重新加载工程后再备份。');
  }
  return { context, node, ownerKey: geometryBackupOwnerKey(context, node), coreLayerContext };
}

export function geometryBackupIdentityIsCurrent(
  identity: GeometryBackupActionIdentity,
  context: WorkbenchContext,
  node: CatalogTreeNode,
): boolean {
  return identity.ownerKey === geometryBackupOwnerKey(context, node);
}

export function geometryBackupTargetIsReady(
  action: 'create' | 'restore',
  context: WorkbenchContext,
  node: CatalogTreeNode,
): boolean {
  if (action === 'create') {
    return node.sourceClass === 'ObservableGeometry' && !!node.assetID && isAbsoluteServerPath(node.assetID) &&
      context.selectedAsset?.kind === 'Geometry' && isSameServerPath(context.selectedAsset.path, node.assetID);
  }
  const backup = node.backupSummary;
  return node.sourceClass === 'BackupNodeVM' && !!node.assetID && isAbsoluteServerPath(node.assetID) &&
    !!backup?.canSaveAsNewGeometry && !!backup.filename && isAbsoluteServerPath(backup.filename) &&
    isProjectTreeSHA256(backup.revision) && !!backup.parentGeometryPath && isAbsoluteServerPath(backup.parentGeometryPath) &&
    isProjectTreeSHA256(backup.parentGeometryRevision) && isProjectTreeSHA256(backup.projectRevision) &&
    context.projectRevision?.toUpperCase() === backup.projectRevision.toUpperCase();
}

export async function createNamedGeometryBackup(
  backend: BackendGateway,
  identity: GeometryBackupActionIdentity,
  name: string,
  isCurrent?: () => boolean,
): Promise<GeometryNamedBackupReceipt> {
  const { context, node, coreLayerContext } = identity;
  if (!backend.supports('workspace.file.inspect') || !backend.supports('model.load') ||
      !backend.supports('geometry.backup.createNamed')) {
    throw new Error('当前 C# 服务未同时提供 Geometry 读取和命名备份操作。');
  }
  const asset = context.selectedAsset;
  if (node.sourceClass !== 'ObservableGeometry' || !node.assetID || !isAbsoluteServerPath(node.assetID) ||
      !asset || asset.kind !== 'Geometry' || !isSameServerPath(asset.path, node.assetID)) {
    throw new Error('请在工程目录中选中与当前工程会话完全匹配的几何对象。');
  }
  if (!name || name !== name.trim() || name.includes('/') || name.includes('\\')) {
    throw new Error('请输入不带首尾空格或路径分隔符的备份名称。');
  }
  const localProjectFingerprint = await projectTreeXmlSHA(context.projectXml!);
  const geometryCaptureRaw: unknown = await backend.request('workspace.file.inspect', { sourcePath: node.assetID });
  if (!workspaceFileCaptureMatches(geometryCaptureRaw, node.assetID) || geometryCaptureRaw.length <= 0) {
    throw new Error('C# 服务没有返回匹配的几何规范路径、文件 SHA-256 和长度。');
  }
  if (isCurrent && !isCurrent()) throw new Error('工程或目录节点已切换；没有提交备份写入。');
  const modelRequest = modelLoadRequest(context, { ...asset, path: node.assetID, kind: 'Geometry', name: node.name });
  const model = await backend.request<ModelSnapshot>('model.load', modelRequest);
  if (isCurrent && !isCurrent()) throw new Error('工程或目录节点已切换；没有提交备份写入。');
  if (!isProjectTreeSHA256(model?.revision) || model.revision.toUpperCase() !== geometryCaptureRaw.revision.toUpperCase() ||
      (asset.revision && isProjectTreeSHA256(asset.revision) && asset.revision.toUpperCase() !== model.revision.toUpperCase())) {
    throw new Error('几何文件 SHA-256 与当前目录选择不一致；请刷新目录后重试。');
  }
  const request = {
    projectPath: context.projectPath,
    projectXml: context.projectXml,
    expectedProjectRevision: context.projectRevision,
    geometryPath: node.assetID,
    geometryCanonicalPath: geometryCaptureRaw.canonicalPath,
    expectedGeometryRevision: geometryCaptureRaw.revision,
    name,
    ...(coreLayerContext ? { coreLayerContext } : {}),
  };
  const raw: unknown = await backend.request('geometry.backup.createNamed', request);
  const receipt = raw as Partial<GeometryNamedBackupReceipt> | null;
  if (!receipt || !receipt.projectPath || !isSameServerPath(receipt.projectPath, context.projectPath!) ||
      !isProjectTreeSHA256(receipt.projectRevision) || receipt.projectRevision.toUpperCase() !== context.projectRevision!.toUpperCase() ||
      !isProjectTreeSHA256(receipt.projectXmlFingerprint) || receipt.projectXmlFingerprint.toUpperCase() !== localProjectFingerprint ||
      !receipt.geometryPath || !isSameServerPath(receipt.geometryPath, node.assetID) ||
      !isProjectTreeSHA256(receipt.geometryRevision) || receipt.geometryRevision.toUpperCase() !== model.revision.toUpperCase() ||
      !receipt.backupPath || !isAbsoluteServerPath(receipt.backupPath) ||
      !isProjectTreeSHA256(receipt.backupRevision)) {
    throw new Error('命名备份回执与当前工程、几何文件或 XML 指纹不匹配。');
  }
  return receipt as GeometryNamedBackupReceipt;
}

export async function restoreGeometryBackupAsNew(
  backend: BackendGateway,
  identity: GeometryBackupActionIdentity,
  isCurrent?: () => boolean,
): Promise<GeometryBackupRestoreReceipt> {
  const { context, node, coreLayerContext } = identity;
  if (!backend.supports('workspace.file.inspect') || !backend.supports('geometry.backup.restore')) {
    throw new Error('当前 C# 服务尚未同时发布文件检查和 Geometry 备份恢复操作。');
  }
  const backup = node.backupSummary;
  if (node.sourceClass !== 'BackupNodeVM' || !node.assetID || !isAbsoluteServerPath(node.assetID) ||
      !backup?.canSaveAsNewGeometry || !backup.filename || !isAbsoluteServerPath(backup.filename) ||
      !isProjectTreeSHA256(backup.revision) || !backup.parentGeometryPath || !isAbsoluteServerPath(backup.parentGeometryPath) ||
      !isProjectTreeSHA256(backup.parentGeometryRevision) || !isProjectTreeSHA256(backup.projectRevision) ||
      backup.projectRevision.toUpperCase() !== context.projectRevision!.toUpperCase()) {
    throw new Error('目录尚未提供可验证的备份、父级几何和工程修订；请刷新目录后重试。');
  }
  const localProjectFingerprint = await projectTreeXmlSHA(context.projectXml!);
  if (isCurrent && !isCurrent()) throw new Error('工程或目录节点已切换；没有提交恢复写入。');
  const [backupCaptureRaw, parentCaptureRaw] = await Promise.all([
    backend.request<unknown>('workspace.file.inspect', { sourcePath: backup.filename }),
    backend.request<unknown>('workspace.file.inspect', { sourcePath: backup.parentGeometryPath }),
  ]);
  if (!workspaceFileCaptureMatches(backupCaptureRaw, backup.filename) || backupCaptureRaw.length <= 0 ||
      backupCaptureRaw.revision.toUpperCase() !== backup.revision.toUpperCase() ||
      !isSameServerPath(backupCaptureRaw.canonicalPath, node.assetID) ||
      !workspaceFileCaptureMatches(parentCaptureRaw, backup.parentGeometryPath) ||
      parentCaptureRaw.revision.toUpperCase() !== backup.parentGeometryRevision.toUpperCase() ||
      !isSameServerPath(parentCaptureRaw.canonicalPath, backup.parentGeometryPath)) {
    throw new Error('备份或父级几何的当前文件 SHA-256 与目录快照不一致；请刷新目录后重试。');
  }
  if (isCurrent && !isCurrent()) throw new Error('工程或目录节点已切换；没有提交恢复写入。');
  const request = {
    projectPath: context.projectPath,
    projectXml: context.projectXml,
    backupPath: backup.filename,
    backupCanonicalPath: backupCaptureRaw.canonicalPath,
    expectedBackupRevision: backup.revision,
    parentGeometryPath: parentCaptureRaw.canonicalPath,
    expectedParentGeometryRevision: backup.parentGeometryRevision,
    expectedProjectRevision: context.projectRevision,
    ...(coreLayerContext ? { coreLayerContext } : {}),
  };
  const raw: unknown = await backend.request('geometry.backup.restore', request);
  const receipt = raw as Partial<GeometryBackupRestoreReceipt> | null;
  if (!receipt || !receipt.projectPath || !isSameServerPath(receipt.projectPath, context.projectPath!) ||
      !receipt.backupPath || !isSameServerPath(receipt.backupPath, backup.filename) ||
      !isProjectTreeSHA256(receipt.backupRevision) || receipt.backupRevision.toUpperCase() !== backup.revision.toUpperCase() ||
      !receipt.parentGeometryPath || !isSameServerPath(receipt.parentGeometryPath, backup.parentGeometryPath) ||
      !isProjectTreeSHA256(receipt.parentGeometryRevision) || receipt.parentGeometryRevision.toUpperCase() !== backup.parentGeometryRevision.toUpperCase() ||
      !isProjectTreeSHA256(receipt.projectRevision) || receipt.projectRevision.toUpperCase() !== context.projectRevision!.toUpperCase() ||
      !receipt.newAssetPath || !isAbsoluteServerPath(receipt.newAssetPath) ||
      isSameServerPath(receipt.newAssetPath, backup.parentGeometryPath) || isSameServerPath(receipt.newAssetPath, backup.filename) ||
      !receipt.newName || !isProjectTreeSHA256(receipt.newAssetRevision) || receipt.newAssetRevision.toUpperCase() !== backup.revision.toUpperCase() ||
      !isProjectTreeSHA256(receipt.projectXmlFingerprint) || receipt.projectXmlFingerprint.toUpperCase() !== localProjectFingerprint) {
    throw new Error('恢复回执与当前备份、父级几何、工程修订或 XML 指纹不匹配。');
  }
  return receipt as GeometryBackupRestoreReceipt;
}
