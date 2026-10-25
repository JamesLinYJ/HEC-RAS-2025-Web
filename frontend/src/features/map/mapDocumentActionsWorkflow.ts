import { BackendOperationUnavailableError } from '../../shared/workbench';
import type { BackendGateway, MapDocumentDeleteReceipt, MapDocumentIndexChange, MapVisualizationCommitReceipt, WorkbenchContext } from '../../shared/workbench';
import { projectXmlSha } from './ProjectTreeListener';
import type { MapDocumentTab } from './mapTypes';

const sha256Pattern = /^[\da-f]{64}$/i;

export type { MapDocumentDeleteReceipt, MapDocumentIndexChange } from '../../shared/workbench';
export type MapDocumentMutationReceipt = MapVisualizationCommitReceipt;

/** A validation or request-construction failure known to happen before POST. */
export class MapDocumentRequestNotSubmittedError extends Error {
  readonly causeValue: unknown;

  constructor(message: string, causeValue?: unknown) {
    super(message);
    this.name = 'MapDocumentRequestNotSubmittedError';
    this.causeValue = causeValue;
  }
}

async function prepareRequest(
  context: WorkbenchContext,
  target: MapDocumentTab,
  extra: Record<string, unknown> = {},
): Promise<Record<string, unknown>> {
  try {
    return await makeRequest(context, target, extra);
  } catch (error) {
    throw new MapDocumentRequestNotSubmittedError(
      error instanceof Error ? error.message : '地图文档请求校验失败。', error);
  }
}

function requireCapability(backend: BackendGateway, operation: string): void {
  try {
    if (!backend.supports(operation)) throw new BackendOperationUnavailableError(operation);
  } catch (error) {
    throw new MapDocumentRequestNotSubmittedError(
      error instanceof Error ? error.message : `当前服务不支持 ${operation}。`, error);
  }
}

export function expectedMapIndexChanges(
  documents: readonly MapDocumentTab[],
  target: MapDocumentTab,
): MapDocumentIndexChange[] {
  const sorted = [...documents].sort((left, right) => left.mapIndex - right.mapIndex);
  if (sorted.length !== documents.length || sorted.some((document, index) => document.mapIndex !== index) ||
      sorted.filter(document => sameDocumentIdentity(document, target)).length !== 1 ||
      !target.visualizationRevision || sorted.some(document =>
        document.visualizationRevision?.toUpperCase() !== target.visualizationRevision?.toUpperCase())) {
    throw new Error('当前地图目录索引不连续或目标地图不唯一，请重新读取地图目录。');
  }
  return sorted.filter(document => !sameDocumentIdentity(document, target)).map((document, mapIndex) => ({
    previousMapIndex: document.mapIndex,
    mapIndex,
    mapName: document.name,
    mapType: document.mapType,
  }));
}

export function mapDocumentsMatchExpected(
  beforeDocuments: readonly MapDocumentTab[],
  readbackDocuments: readonly MapDocumentTab[],
  action: 'rename' | 'delete',
  target: MapDocumentTab,
  newName?: string,
): boolean {
  const before = [...beforeDocuments].sort((left, right) => left.mapIndex - right.mapIndex);
  const after = [...readbackDocuments].sort((left, right) => left.mapIndex - right.mapIndex);
  if (before.some((document, index) => document.mapIndex !== index) ||
      after.some((document, index) => document.mapIndex !== index) ||
      before.filter(document => sameDocumentIdentity(document, target)).length !== 1) return false;
  let expected: MapDocumentTab[];
  if (action === 'rename') {
    if (!newName?.trim() || newName !== newName.trim() || newName === target.name) return false;
    if (before.some(document => !sameDocumentIdentity(document, target) &&
        document.name.toLowerCase() === newName.toLowerCase())) return false;
    expected = before.map(document => sameDocumentIdentity(document, target) ? { ...document, name: newName } : document);
  } else {
    expected = before.filter(document => !sameDocumentIdentity(document, target))
      .map((document, mapIndex) => ({ ...document, mapIndex }));
  }
  return expected.length === after.length && expected.every((document, index) =>
    document.mapIndex === after[index]?.mapIndex && document.name === after[index]?.name &&
    document.mapType === after[index]?.mapType);
}

export function mapDocumentsMatchCaptured(
  captured: readonly MapDocumentTab[],
  readback: readonly MapDocumentTab[],
): boolean {
  const before = [...captured].sort((left, right) => left.mapIndex - right.mapIndex);
  const after = [...readback].sort((left, right) => left.mapIndex - right.mapIndex);
  return before.length === after.length && before.every((document, index) =>
    document.mapIndex === index && document.mapIndex === after[index]?.mapIndex &&
    document.name === after[index]?.name && document.mapType === after[index]?.mapType);
}

function sameDocumentIdentity(left: MapDocumentTab, right: MapDocumentTab): boolean {
  return left.mapIndex === right.mapIndex && left.name === right.name && left.mapType === right.mapType;
}

function record(value: unknown, description: string): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error(`${description}不是有效对象。`);
  return value as Record<string, unknown>;
}

function parseMutationReceipt(value: unknown, target: MapDocumentTab, newName: string): MapDocumentMutationReceipt {
  const receipt = record(value, '地图重命名回执');
  if (!Number.isInteger(receipt.mapIndex) || typeof receipt.mapName !== 'string' || typeof receipt.mapType !== 'string' ||
      typeof receipt.visualizationRevision !== 'string' || typeof receipt.previousVisualizationRevision !== 'string') {
    throw new Error('地图重命名回执字段不完整。');
  }
  const result: MapDocumentMutationReceipt = {
    mapIndex: receipt.mapIndex as number,
    mapName: receipt.mapName,
    mapType: receipt.mapType,
    previousVisualizationRevision: receipt.previousVisualizationRevision,
    visualizationRevision: receipt.visualizationRevision,
  };
  if (!Number.isInteger(result.mapIndex) || result.mapIndex !== target.mapIndex || result.mapName !== newName ||
      result.mapType !== target.mapType ||
      (result.previousVisualizationRevision ?? '').toUpperCase() !== (target.visualizationRevision ?? '').toUpperCase() ||
      !sha256Pattern.test(result.visualizationRevision) ||
      !result.previousVisualizationRevision || !sha256Pattern.test(result.previousVisualizationRevision)) {
    throw new Error('地图重命名回执与目标地图或旧 .vis 修订不匹配。');
  }
  if (result.mapName !== target.name && result.visualizationRevision.toUpperCase() === result.previousVisualizationRevision.toUpperCase()) {
    throw new Error('地图名称已变化，但 .vis 修订没有变化。');
  }
  return result;
}

function parseDeleteReceipt(
  value: unknown,
  target: MapDocumentTab,
  expectedChanges: readonly MapDocumentIndexChange[],
): MapDocumentDeleteReceipt {
  const receipt = record(value, '地图删除回执');
  if (!Array.isArray(receipt.mapIndexChanges)) throw new Error('地图删除回执缺少剩余地图索引映射。');
  const mapIndexChanges = receipt.mapIndexChanges.map((value, index) => {
    const change = record(value, `地图索引映射 ${index + 1}`);
    if (!Number.isInteger(change.previousMapIndex) || !Number.isInteger(change.mapIndex) ||
        typeof change.mapName !== 'string' || typeof change.mapType !== 'string') {
      throw new Error(`地图索引映射 ${index + 1} 字段不完整。`);
    }
    return {
      previousMapIndex: change.previousMapIndex as number,
      mapIndex: change.mapIndex as number,
      mapName: change.mapName as string,
      mapType: change.mapType as string,
    };
  });
  if (!Number.isInteger(receipt.removedMapIndex) || typeof receipt.removedMapName !== 'string' ||
      typeof receipt.removedMapType !== 'string' || typeof receipt.visualizationRevision !== 'string' ||
      typeof receipt.previousVisualizationRevision !== 'string') {
    throw new Error('地图删除回执字段不完整。');
  }
  const result: MapDocumentDeleteReceipt = {
    removedMapIndex: receipt.removedMapIndex as number,
    removedMapName: receipt.removedMapName as string,
    removedMapType: receipt.removedMapType as string,
    previousVisualizationRevision: receipt.previousVisualizationRevision,
    visualizationRevision: receipt.visualizationRevision,
    mapIndexChanges,
  };
  const mappingMatches = mapIndexChanges.length === expectedChanges.length && mapIndexChanges.every((change, index) => {
    const expected = expectedChanges[index];
    return expected && change.previousMapIndex === expected.previousMapIndex && change.mapIndex === expected.mapIndex &&
      change.mapName === expected.mapName && change.mapType === expected.mapType;
  });
  if (result.removedMapIndex !== target.mapIndex || result.removedMapName !== target.name ||
      result.removedMapType !== target.mapType ||
      (result.previousVisualizationRevision ?? '').toUpperCase() !== (target.visualizationRevision ?? '').toUpperCase() ||
      !result.previousVisualizationRevision || !sha256Pattern.test(result.previousVisualizationRevision) ||
      !sha256Pattern.test(result.visualizationRevision) ||
      result.visualizationRevision.toUpperCase() === result.previousVisualizationRevision.toUpperCase() || !mappingMatches) {
    throw new Error('地图删除回执与目标身份、旧 .vis 修订或剩余地图索引映射不匹配。');
  }
  return result;
}

async function makeRequest(
  context: WorkbenchContext,
  target: MapDocumentTab,
  extra: Record<string, unknown> = {},
): Promise<Record<string, unknown>> {
  if (!context.projectPath || !context.projectXml || !context.projectRevision || !context.projectSessionID.trim() ||
      !target.visualizationRevision || !sha256Pattern.test(target.visualizationRevision)) {
    throw new Error('地图文档操作需要当前已保存工程会话、工程 XML、工程修订和 .vis 修订。');
  }
  const projectXmlSHA = await projectXmlSha(context.projectXml);
  if (!projectXmlSHA) throw new Error('无法计算当前工程 XML 的 SHA-256。');
  return {
    projectPath: context.projectPath,
    projectXml: context.projectXml,
    projectXmlSHA,
    expectedVisualizationRevision: target.visualizationRevision,
    kind: target.mapType,
    mapIndex: target.mapIndex,
    mapName: target.name,
    mapType: target.mapType,
    coreLayerContext: context.coreLayerContext ?? null,
    ...extra,
  };
}

export async function renameMapDocument(
  backend: BackendGateway,
  context: WorkbenchContext,
  target: MapDocumentTab,
  newName: string,
): Promise<MapDocumentMutationReceipt> {
  requireCapability(backend, 'map.document.rename');
  const normalizedName = newName.trim();
  if (!normalizedName || normalizedName.length > 128 || /[\u0000-\u001f\u007f-\u009f]/.test(normalizedName)) {
    throw new MapDocumentRequestNotSubmittedError('请输入 1–128 个字符且不含控制字符的地图名称。');
  }
  const request = await prepareRequest(context, target, { newName: normalizedName });
  return parseMutationReceipt(await backend.request<unknown>('map.document.rename', request), target, normalizedName);
}

export async function deleteMapDocument(
  backend: BackendGateway,
  context: WorkbenchContext,
  target: MapDocumentTab,
  documents: readonly MapDocumentTab[],
): Promise<MapDocumentDeleteReceipt> {
  requireCapability(backend, 'map.document.delete');
  let changes: MapDocumentIndexChange[];
  try {
    changes = expectedMapIndexChanges(documents, target);
  } catch (error) {
    throw new MapDocumentRequestNotSubmittedError(
      error instanceof Error ? error.message : '地图删除请求校验失败。', error);
  }
  const request = await prepareRequest(context, target);
  return parseDeleteReceipt(await backend.request<unknown>('map.document.delete', request), target, changes);
}
