import type { BackendGateway, WorkbenchContext } from '../../../shared/workbench';
import { exactCoreLayerContext, hasUncapturedSessionReferences } from '../coreImportWorkflow';
import type { MapTreeSnapshot, MapDocumentReceipt } from '../../map/ProjectTreeListener';
import { projectXmlSha } from '../../map/ProjectTreeListener';
import type { MapDocumentTab } from '../../map/mapTypes';
import type { AttributeTableSourceFamilyCapture } from '../attributes/attributeTableTypes';
import type { AttributeTableSourceCapture } from '../attributes/attributeTableTypes';
import { isShapefileFamilyCapture, isWorkspaceCapture, sameSourcePath } from '../attributes/attributeTableTypes';

export type ShapefileFamilyCapture = AttributeTableSourceFamilyCapture;
export type ShapefileMapImportReceipt = MapDocumentReceipt;

export interface NewShapefileCreateReceipt {
  operationID: string;
  projectSessionID: string;
  mapID: string;
  projectPath: string;
  projectRevision: string;
  visualizationRevision: string | null;
  mapIndex: number | null;
  mapName: string | null;
  mapType: string;
  destinationPath: string;
  shapeType: 'Point' | 'Polyline' | 'Polygon';
  projectionChoice: 'Project' | 'Local';
  projectionWKT: string | null;
  family: ShapefileFamilyCapture;
  createdUtc: string;
}

export interface MapDocumentListSnapshot {
  projectRevision: string;
  visualizationRevision: string | null;
  maps: { mapIndex: number; mapName: string; mapType: string }[];
  activeMapIndex: number | null;
}

export interface HostMapDocumentRequest {
  projectPath: string;
  projectXml: string | null;
  projectXmlSHA: string | null;
  expectedVisualizationRevision: string | null;
  kind: string;
  mapID: string;
  mapIndex: number;
  mapName: string;
  mapType: string;
  coreLayerContext?: Record<string, unknown>;
}

export interface CapturedMapOwner {
  request: HostMapDocumentRequest;
  tree: MapTreeSnapshot;
  visualizationRevision: string;
}

export async function captureMapOwner(
  backend: BackendGateway,
  context: WorkbenchContext,
  target: MapDocumentTab,
  signal: AbortSignal,
): Promise<CapturedMapOwner> {
  if (!context.projectPath || !context.projectRevision || context.projectXml === null || !context.projectSessionID) {
    throw new Error('地图操作需要当前已保存工程、XML 快照、工程修订和会话身份。');
  }
  if (!backend.supports('map.document.list') || !backend.supports('map.tree.load')) {
    throw new Error('当前 C# 服务未同时发布地图目录与地图树读取能力，不能确认目标地图身份。');
  }
  const targetVisualizationRevision = target.visualizationRevision;
  if (!target.mapID || !Number.isInteger(target.mapIndex) || target.mapIndex < 0 ||
      !target.name.trim() || !target.mapType.trim() || typeof targetVisualizationRevision !== 'string' ||
      !/^[\da-f]{64}$/i.test(targetVisualizationRevision)) {
    throw new Error('目标地图缺少已读取的索引、名称、类型或可视化修订；请重新读取工程地图目录。');
  }
  if (hasUncapturedSessionReferences(context, context.selectedAsset)) {
    throw new Error('当前选中来源带有未确认的会话核心图层引用；请重新读取来源上下文后再操作。');
  }

  const projectXmlSHA = await projectXmlSha(context.projectXml);
  const listed = await backend.request<MapDocumentListSnapshot>('map.document.list', {
    projectPath: context.projectPath,
    projectXml: context.projectXml,
    projectXmlSHA,
    expectedProjectRevision: context.projectRevision,
  }, { signal });
  if (listed.projectRevision.toLocaleUpperCase() !== context.projectRevision.toLocaleUpperCase()) {
    throw new Error('地图目录回执与当前工程修订不一致；请重新读取工程地图目录。');
  }
  const listedTarget = listed.maps.find(map => map.mapIndex === target.mapIndex);
  if (!listedTarget || listedTarget.mapName !== target.name || listedTarget.mapType !== target.mapType ||
      (listed.visualizationRevision ?? '').toLocaleUpperCase() !== targetVisualizationRevision.toLocaleUpperCase()) {
    throw new Error('目标地图或可视化修订已变化；未提交操作，请重新读取工程地图目录。');
  }

  const coreLayerContext = exactCoreLayerContext(context, context.selectedAsset);
  const request: HostMapDocumentRequest = {
    projectPath: context.projectPath,
    projectXml: context.projectXml,
    projectXmlSHA,
    expectedVisualizationRevision: listed.visualizationRevision,
    kind: listedTarget.mapType,
    mapID: target.mapID,
    mapIndex: listedTarget.mapIndex,
    mapName: listedTarget.mapName,
    mapType: listedTarget.mapType,
    ...(coreLayerContext ? { coreLayerContext } : {}),
  };
  const tree = await backend.request<MapTreeSnapshot>('map.tree.load', {
    projectPath: request.projectPath,
    projectXml: request.projectXml,
    projectXmlSHA: request.projectXmlSHA,
    mapID: request.mapID,
    mapIndex: request.mapIndex,
    mapName: request.mapName,
    mapType: request.mapType,
    expectedVisualizationRevision: request.expectedVisualizationRevision,
    projectSessionID: context.projectSessionID,
    ...(coreLayerContext ? { externalCoreLayerReferences: coreLayerReferences(coreLayerContext) } : {}),
    ...(coreLayerContext ? { coreLayerContext } : {}),
  }, { signal });
  if (!Array.isArray(tree.roots) ||
      (tree.visualizationRevision ?? '').toLocaleUpperCase() !== (listed.visualizationRevision ?? '').toLocaleUpperCase()) {
    throw new Error('地图树结果与选定的地图修订不一致；未提交操作。');
  }
  return { request, tree, visualizationRevision: listed.visualizationRevision ?? '' };
}

function coreLayerReferences(value: Record<string, unknown>): unknown[] {
  const references = value.references ?? value.References;
  return Array.isArray(references) ? references : [];
}

export function sameMapSourceFamily(left: ShapefileFamilyCapture, right: ShapefileFamilyCapture): boolean {
  if (left.family !== right.family || !sameSourcePath(left.sourcePath, right.sourcePath) ||
      left.revision.toLocaleUpperCase() !== right.revision.toLocaleUpperCase() || left.files.length !== right.files.length) return false;
  const rightFiles = new Map(right.files.map(file => [file.role, file]));
  return left.files.every(file => {
    const other = rightFiles.get(file.role);
    return Boolean(other && sameSourcePath(file.inputPath, other.inputPath) && sameSourcePath(file.path, other.path) &&
      file.required === other.required && file.exists === other.exists &&
      (file.revision ?? '').toLocaleUpperCase() === (other.revision ?? '').toLocaleUpperCase());
  });
}

export function validShapefileFamily(value: unknown, sourcePath: string, sourceRevision: string): value is ShapefileFamilyCapture {
  return isShapefileFamilyCapture(value, sourcePath, sourceRevision);
}

export async function inspectShapefileFamily(backend: BackendGateway, sourcePath: string, signal: AbortSignal): Promise<{
  source: AttributeTableSourceCapture;
  family: ShapefileFamilyCapture;
}> {
  if (!backend.supports('workspace.file.inspect') || !backend.supports('map.vector.captureShapefile')) {
    throw new Error('当前 C# 服务未发布自动文件检查和完整 Shapefile 文件族捕获，不能验证此来源。');
  }
  const sourceRaw: unknown = await backend.request('workspace.file.inspect', { sourcePath }, { signal });
  if (!isWorkspaceCapture(sourceRaw, sourcePath) || !sourceRaw.canonicalPath.toLocaleLowerCase().endsWith('.shp')) {
    throw new Error('服务未为所选 .shp 返回匹配的规范路径和 SHA-256。');
  }
  const familyRaw: unknown = await backend.request('map.vector.captureShapefile',
    { sourcePath: sourceRaw.canonicalPath }, { signal });
  if (!validShapefileFamily(familyRaw, sourceRaw.canonicalPath, sourceRaw.revision)) {
    throw new Error('服务未返回与主文件 SHA 一致的完整 .shp/.shx/.dbf/可选 .prj 来源身份。');
  }
  return { source: sourceRaw, family: familyRaw };
}

export function isNewShapefileCreateReceipt(value: unknown): value is NewShapefileCreateReceipt {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const receipt = value as Partial<NewShapefileCreateReceipt>;
  const familyRaw = (value as Record<string, unknown>).family;
  if (!familyRaw || typeof familyRaw !== 'object' || Array.isArray(familyRaw)) return false;
  const family = familyRaw as Partial<ShapefileFamilyCapture>;
  if (typeof family.sourcePath !== 'string' || !Array.isArray(family.files)) return false;
  const sourceRevision = family.files.find(file => file?.role === 'source')?.revision;
  return typeof receipt.operationID === 'string' && typeof receipt.projectSessionID === 'string' &&
    typeof receipt.mapID === 'string' && typeof receipt.projectPath === 'string' &&
    typeof receipt.projectRevision === 'string' &&
    (receipt.visualizationRevision === null || typeof receipt.visualizationRevision === 'string') &&
    (receipt.mapIndex === null || (typeof receipt.mapIndex === 'number' && Number.isInteger(receipt.mapIndex))) &&
    (receipt.mapName === null || typeof receipt.mapName === 'string') && typeof receipt.mapType === 'string' &&
    typeof receipt.destinationPath === 'string' &&
    (receipt.shapeType === 'Point' || receipt.shapeType === 'Polyline' || receipt.shapeType === 'Polygon') &&
    (receipt.projectionChoice === 'Project' || receipt.projectionChoice === 'Local') &&
    (receipt.projectionWKT === null || typeof receipt.projectionWKT === 'string') &&
    typeof receipt.createdUtc === 'string' && typeof sourceRevision === 'string' &&
    validShapefileFamily(familyRaw, family.sourcePath, sourceRevision);
}

export function isShapefileMapImportReceipt(value: unknown, target: MapDocumentTab,
  previousVisualizationRevision: string): value is ShapefileMapImportReceipt {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const receipt = value as Partial<ShapefileMapImportReceipt>;
  return receipt.mapIndex === target.mapIndex && receipt.mapName === target.name && receipt.mapType === target.mapType &&
    typeof receipt.previousVisualizationRevision === 'string' &&
    receipt.previousVisualizationRevision.toLocaleUpperCase() === previousVisualizationRevision.toLocaleUpperCase() &&
    typeof receipt.visualizationRevision === 'string' && /^[\da-f]{64}$/i.test(receipt.visualizationRevision);
}
