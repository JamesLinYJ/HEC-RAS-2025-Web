import type { WorkbenchContext } from '../../shared/workbench';
import type { GeometryMeshSnapshot, GeometryModelRequest, GeometryTopologySnapshot } from './geometryTypes';

export const meshShapefileExportMethod = 'geometry.mesh.exportShapefile';
export type MeshShapefileExportKind = 'CellCenters' | 'PerimeterFaces';

export interface MeshExportSource {
  context: WorkbenchContext;
  model: GeometryModelRequest | null;
  mesh?: GeometryMeshSnapshot;
  topology?: GeometryTopologySnapshot;
  localOnly: boolean;
}
export interface MeshExportForm { flowAreaID: string; exportKind: MeshShapefileExportKind; outputPath: string }
export interface MeshShapefileExportRequest {
  operationID: string;
  model: GeometryModelRequest;
  revision: string;
  draftState: string;
  topologyDraftState: string | null;
  meshDraftSHA256: string;
  topologyDraftSHA256: string;
  flowAreaID: string;
  expectedFlowAreaName: string;
  exportKind: MeshShapefileExportKind;
  outputPath: string;
}
export interface MeshExportCapture {
  context: WorkbenchContext;
  sourceKey: string;
  cellCount: number;
  faceCount: number;
  request: MeshShapefileExportRequest;
}
export interface MeshExportArtifact { path: string; sizeBytes: number; sha256: string }
export interface MeshShapefileExportReceipt {
  operationID: string;
  state: 'completed' | 'cancelled';
  canonicalAssetPath: string;
  sourceRevision: string;
  flowAreaID: string;
  flowAreaName: string;
  exportKind: MeshShapefileExportKind;
  meshDraftSHA256: string;
  topologyDraftSHA256: string;
  outputPath: string | null;
  featureCount: number;
  projectionWKT: string | null;
  artifacts: MeshExportArtifact[];
}

const isSHA = (value: unknown): value is string => typeof value === 'string' && /^[0-9a-f]{64}$/i.test(value);
const sameSHA = (left: string, right: string): boolean => left.toUpperCase() === right.toUpperCase();
const isRecord = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value);
const isInteger = (value: unknown): value is number => Number.isSafeInteger(value) && (value as number) >= 0;

/** Includes the session and opaque draft text; a capability refresh alone does not change the source. */
export function meshExportSourceKey(source: MeshExportSource): string {
  const { context, model, mesh, topology } = source;
  return JSON.stringify([context.projectSessionID, context.projectPath, context.projectRevision, context.projectXml,
    context.coreLayerContext ?? null, context.selectedAsset, model, mesh?.revision, mesh?.draftState,
    mesh?.topologyFingerprint, mesh?.flowAreas.map(area => [area.id, area.name, area.hasMesh, area.cellCount, area.faceCount]),
    topology?.revision, topology?.draftState, source.localOnly]);
}

/** Do not resolve server paths in the browser or silently change the user's destination. */
export function validateMeshExportPath(value: string): string {
  const absolute = value.startsWith('/') || /^[A-Za-z]:[\\/]/.test(value) || /^\\\\[^\\]+\\[^\\]+\\/.test(value);
  const parts = value.replaceAll('\\', '/').split('/');
  const body = parts.slice(value.startsWith('\\\\') ? 2 : value.startsWith('/') ? 1 : 0);
  if (!absolute || value.trim() !== value || /[\0\r\n]/.test(value) || !/\.shp$/i.test(value) ||
      body.some(part => part === '.' || part === '..' || part === '') || !/[^\\/]+\.shp$/i.test(value)) {
    throw new Error('请填写服务器上明确的绝对 .shp 文件路径，目录必须已存在；路径不能包含 . 或 .. 段。');
  }
  return value;
}

function pathKey(value: string): string {
  return /^[A-Za-z]:[\\/]/.test(value) || value.startsWith('\\\\')
    ? value.replaceAll('/', '\\').toLowerCase() : value;
}

export function meshExportSourceIssue(source: MeshExportSource, flowAreaID: string): string | null {
  const { context, model, mesh, topology } = source;
  if (source.localOnly) return '存在尚未进入几何草稿的本地表格修改。请先处理这些修改，再导出当前网格。';
  if (!context.projectSessionID || !context.projectPath || !context.projectXml || !isSHA(context.projectRevision) ||
      context.selectedAsset?.kind !== 'Geometry' || !model || model.kind !== 'Geometry' ||
      model.assetPath !== context.selectedAsset.path || model.projectPath !== context.projectPath || model.projectXml !== context.projectXml) {
    return '请先打开已保存工程中的几何模型，等待当前工程会话和几何来源载入。';
  }
  const core = model.coreLayerContext;
  if ((context.coreLayerContext && JSON.stringify(core) !== JSON.stringify(context.coreLayerContext)) ||
      (core && ((core.projectSessionID ?? core.ProjectSessionID) !== context.projectSessionID ||
        (core.projectPath ?? core.ProjectPath) !== context.projectPath ||
        typeof (core.identityToken ?? core.IdentityToken) !== 'string' || !(core.identityToken ?? core.IdentityToken)))) {
    return '当前几何来源与工程的 Core 会话身份不一致，请重新载入来源。';
  }
  if (!mesh || !topology || !isSHA(mesh.revision) || !isSHA(topology.revision) ||
      !sameSHA(mesh.revision, topology.revision) || !mesh.draftState || !topology.draftState ||
      (context.selectedAsset.revision && !sameSHA(mesh.revision, context.selectedAsset.revision))) {
    return '当前网格、拓扑草稿或 Geometry 源修订不完整或不一致，请等待载入完成。';
  }
  const areas = mesh.flowAreas.filter(area => area.id === flowAreaID);
  if (areas.length !== 1 || !areas[0].name || !areas[0].hasMesh || !isInteger(areas[0].cellCount) ||
      areas[0].cellCount <= 0 || !isInteger(areas[0].faceCount)) return '请选择包含真实网格的流动区域。';
  return null;
}

/** Captures synchronously before hashing, retaining the exact original draft strings and Core context. */
export function captureMeshExport(source: MeshExportSource, form: MeshExportForm, operationID: string): MeshExportCapture {
  const issue = meshExportSourceIssue(source, form.flowAreaID);
  if (issue) throw new Error(issue);
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(operationID)) throw new Error('无法创建有效的导出任务标识。');
  if (form.exportKind !== 'CellCenters' && form.exportKind !== 'PerimeterFaces') throw new Error('请选择单元中心或外围面导出。');
  const outputPath = validateMeshExportPath(form.outputPath);
  const mesh = source.mesh!;
  const area = mesh.flowAreas.find(item => item.id === form.flowAreaID)!;
  return {
    context: structuredClone(source.context), sourceKey: meshExportSourceKey(source), cellCount: area.cellCount, faceCount: area.faceCount,
    request: { operationID, model: structuredClone(source.model!), revision: mesh.revision, draftState: mesh.draftState,
      topologyDraftState: source.topology!.draftState, meshDraftSHA256: '', topologyDraftSHA256: '',
      flowAreaID: area.id, expectedFlowAreaName: area.name, exportKind: form.exportKind, outputPath },
  };
}

export async function hashMeshExportCapture(capture: MeshExportCapture): Promise<MeshExportCapture> {
  if (!globalThis.crypto?.subtle) throw new Error('当前环境无法计算草稿 SHA-256，不能发起导出。');
  const hash = async (value: string): Promise<string> => {
    const bytes = await globalThis.crypto.subtle.digest('SHA-256', new TextEncoder().encode(value));
    return Array.from(new Uint8Array(bytes), byte => byte.toString(16).padStart(2, '0')).join('').toUpperCase();
  };
  const [meshDraftSHA256, topologyDraftSHA256] = await Promise.all([
    hash(capture.request.draftState), hash(capture.request.topologyDraftState ?? ''),
  ]);
  return { ...capture, request: { ...capture.request, meshDraftSHA256, topologyDraftSHA256 } };
}

/** A terminal transport response is not an export receipt until its source and whole file family agree. */
export function parseMeshExportReceipt(value: unknown, capture: MeshExportCapture): MeshShapefileExportReceipt {
  if (!isRecord(value)) throw new Error('服务端未返回可验证的网格导出回执。');
  const request = capture.request;
  const expected: Record<string, string> = { operationID: request.operationID, canonicalAssetPath: request.model.assetPath,
    flowAreaID: request.flowAreaID, flowAreaName: request.expectedFlowAreaName, exportKind: request.exportKind };
  if (Object.entries(expected).some(([key, expectedValue]) => value[key] !== expectedValue) ||
      !isSHA(value.sourceRevision) || !sameSHA(value.sourceRevision, request.revision) ||
      !isSHA(value.meshDraftSHA256) || !sameSHA(value.meshDraftSHA256, request.meshDraftSHA256) ||
      !isSHA(value.topologyDraftSHA256) || !sameSHA(value.topologyDraftSHA256, request.topologyDraftSHA256)) {
    throw new Error('导出回执与任务捕获的 Geometry、流动区域或草稿身份不匹配，输出状态尚未确认。');
  }
  if ((value.state !== 'completed' && value.state !== 'cancelled') || !isInteger(value.featureCount) ||
      !Array.isArray(value.artifacts) || (value.projectionWKT !== null && (typeof value.projectionWKT !== 'string' || !value.projectionWKT.trim()))) {
    throw new Error('服务端导出状态、数量或投影回执无效。');
  }
  if (value.state === 'cancelled') {
    if (value.outputPath !== null || value.featureCount !== 0 || value.artifacts.length !== 0) throw new Error('取消回执包含已发布文件或非零数量，不能确认取消。');
  } else {
    if (typeof value.outputPath !== 'string' || pathKey(value.outputPath) !== pathKey(request.outputPath) ||
        (request.exportKind === 'CellCenters' ? value.featureCount !== capture.cellCount : value.featureCount > capture.faceCount)) {
      throw new Error('服务端导出的目标路径或要素数量与捕获来源不一致。');
    }
  }
  const extensions = new Set<string>();
  const base = request.outputPath.slice(0, -4);
  const artifacts = value.artifacts.map((item: unknown): MeshExportArtifact => {
    if (!isRecord(item) || typeof item.path !== 'string' || !isInteger(item.sizeBytes) || item.sizeBytes <= 0 || !isSHA(item.sha256)) {
      throw new Error('导出文件回执缺少有效路径、文件大小或 SHA-256。');
    }
    const extension = item.path.slice(-4).toLowerCase();
    if (!['.shp', '.shx', '.dbf', '.prj', '.cpg'].includes(extension) || extensions.has(extension) ||
        pathKey(item.path) !== pathKey(base + extension)) throw new Error('导出回执包含重复文件或目标 Shapefile 文件族以外的路径。');
    extensions.add(extension);
    return { path: item.path, sizeBytes: item.sizeBytes, sha256: item.sha256 };
  });
  if (value.state === 'completed' && (['.shp', '.shx', '.dbf'].some(ext => !extensions.has(ext)) ||
      extensions.has('.prj') !== (value.projectionWKT !== null))) throw new Error('导出文件族不完整，或 .prj 与实际投影回执不一致。');
  return { operationID: request.operationID, state: value.state, canonicalAssetPath: value.canonicalAssetPath as string,
    sourceRevision: value.sourceRevision, flowAreaID: request.flowAreaID, flowAreaName: request.expectedFlowAreaName,
    exportKind: request.exportKind, meshDraftSHA256: value.meshDraftSHA256, topologyDraftSHA256: value.topologyDraftSHA256,
    outputPath: value.outputPath as string | null, featureCount: value.featureCount, projectionWKT: value.projectionWKT as string | null, artifacts };
}
