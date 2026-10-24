import type { WorkbenchContext } from '../../../shared/workbench';
import { loadMapDocumentCatalog, projectXmlSha } from '../../map/ProjectTreeListener';
import type { CatalogTreeNode, CatalogTreeNodeLocator, MapDocumentTab } from '../../map/mapTypes';
import type { RasterMapSourceIdentity, RasterMapViewExtent } from '../../map/symbology/rasterSymbologyWorkflow';

export interface ExportSessionHdfResultOwner {
  referenceID: string;
  projectSessionID: string;
  projectPath: string;
  resultPath: string;
  resultRevision: string;
  embeddedPlanName: string;
  coreLayerContextIdentityToken: string;
}

export interface ExportCatalogTreeNodeLocator extends CatalogTreeNodeLocator {
  parentBoundaryConditionAssetID?: string | null;
  parentBoundaryConditionNodePath?: number[] | null;
  parentBoundaryConditionNodeName?: string | null;
  sessionHdfResultOwner?: ExportSessionHdfResultOwner | null;
}

export interface ExportMapRequest {
  projectPath: string;
  projectXml: string | null;
  projectXmlSHA: string | null;
  mapID: string;
  mapIndex: number;
  mapName: string;
  mapType: string;
  expectedVisualizationRevision: string | null;
  projectSessionID: string;
}

export interface DepthExportOwner {
  node: ExportCatalogTreeNodeLocator;
  nodeKind: 'Result' | 'MultiResult';
  name: string;
  selectionIndex: 0;
  resultPath: string;
  resultRevision: string;
  resultName: string;
  currentProfileIndex: number | null;
  profiles: Array<{ index: number; name: string; time: string }>;
}

export interface DepthExportInspection {
  mapID: string;
  contextToken: string;
  defaultCellSize: number;
  defaultSimplifyDistance: number;
  owners: DepthExportOwner[];
}

export interface DepthExportPreview {
  mapID: string;
  contextToken: string;
  resultNode: ExportCatalogTreeNodeLocator;
  selectionIndex: 0;
  resultPath: string;
  resultRevision: string;
  profileIndex: number;
  profileName: string;
  profileTime: string;
  viewport: RasterMapViewExtent;
  cellSize: number;
  simplifyDistance: number;
  polygons: number[][][];
  polylines: number[][][];
}

export interface ExportArtifactReceipt { path: string; sizeBytes: number; sha256: string }

export interface DepthExportReceipt {
  state: 'completed' | 'cancelled';
  outputPath: string | null;
  mapID: string;
  contextToken: string;
  resultNode: ExportCatalogTreeNodeLocator;
  selectionIndex: 0;
  resultPath: string;
  resultRevision: string;
  profileIndex: number;
  profileName: string;
  profileTime: string;
  cellSize: number;
  simplifyDistance: number;
  polygonCount: number;
  artifacts: ExportArtifactReceipt[];
}

export interface RasterPlotOption {
  propertyName: string;
  name: string;
  label: string;
  description: string;
  kind: 'bool' | 'enum';
  value: string;
  enumValues: Array<{ value: string; label: string }>;
  isEnabled: boolean;
  enabledIf: string | null;
}

export interface RgbRasterInspection {
  sourceID: string;
  sourceName: string;
  sourceRevision: string;
  visualizationRevision: string;
  fullExtent: { minX: number; minY: number; maxX: number; maxY: number };
  currentView: RasterMapViewExtent | null;
  suggestedCellSize: number | null;
  plotOptions: RasterPlotOption[];
}

export interface RgbRasterPreview {
  sourceID: string;
  sourceRevision: string;
  visualizationRevision: string;
  cellSize: number;
  extent: { minX: number; minY: number; maxX: number; maxY: number };
  outputWidth: number;
  outputHeight: number;
  estimatedFileSizeBytes: number;
  raster: { id: string; sourceMapID: string | null; name: string; width: number; height: number; rgba: string; minX: number; minY: number; maxX: number; maxY: number };
}

export interface RgbRasterReceipt {
  state: 'completed' | 'cancelled';
  outputPath: string | null;
  sourceID: string;
  sourceRevision: string;
  visualizationRevision: string;
  cellSize: number;
  width: number;
  height: number;
  extent: { minX: number; minY: number; maxX: number; maxY: number };
  artifacts: ExportArtifactReceipt[];
}

export interface RasterCandidate {
  document: MapDocumentTab;
  node: CatalogTreeNode;
  locator: CatalogTreeNodeLocator;
  gdb: boolean;
}

export type ExportRasterSourceIdentity = Omit<RasterMapSourceIdentity, 'sourceRevision'> & { sourceRevision: string | null };

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export function requireRecord(value: unknown, description: string): Record<string, unknown> {
  if (!isRecord(value)) throw new Error(`${description}不是有效对象。`);
  return value;
}

function stringField(value: unknown, name: string, description: string): string {
  if (typeof value !== 'string' || !value.trim()) throw new Error(`${description}缺少 ${name}。`);
  return value;
}

function finiteField(value: unknown, name: string, description: string): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) throw new Error(`${description}中的 ${name} 无效。`);
  return value;
}

function integerField(value: unknown, name: string, description: string): number {
  const result = finiteField(value, name, description);
  if (!Number.isSafeInteger(result)) throw new Error(`${description}中的 ${name} 无效。`);
  return result;
}

function shaField(value: unknown, name: string, description: string): string {
  const result = stringField(value, name, description);
  if (!/^[\da-f]{64}$/i.test(result)) throw new Error(`${description}中的 ${name} 不是 SHA-256。`);
  return result;
}

function optionalStringField(value: unknown, name: string, description: string): string | null | undefined {
  if (value === undefined || value === null) return value;
  if (typeof value !== 'string') throw new Error(`${description}中的 ${name} 无效。`);
  return value;
}

function optionalPathField(value: unknown, name: string, description: string): number[] | null | undefined {
  if (value === undefined || value === null) return value;
  if (!Array.isArray(value) || value.some(index => typeof index !== 'number' || !Number.isSafeInteger(index) || index < 0)) {
    throw new Error(`${description}中的 ${name} 无效。`);
  }
  return [...value];
}

function parseLocator(value: unknown, description: string): ExportCatalogTreeNodeLocator {
  const row = requireRecord(value, description);
  const rootName = stringField(row.rootName, 'rootName', description);
  const nodeType = stringField(row.nodeType, 'nodeType', description);
  const nodeName = stringField(row.nodeName, 'nodeName', description);
  if (!Array.isArray(row.nodePath) || row.nodePath.length === 0 ||
      row.nodePath.some(index => typeof index !== 'number' || !Number.isSafeInteger(index) || index < 0)) {
    throw new Error(`${description}缺少有效的源节点序号。`);
  }
  const sessionOwner = row.sessionHdfResultOwner;
  let sessionHdfResultOwner: ExportSessionHdfResultOwner | null | undefined;
  if (sessionOwner === null || sessionOwner === undefined) sessionHdfResultOwner = sessionOwner;
  else {
    const owner = requireRecord(sessionOwner, `${description}会话 Result 来源`);
    sessionHdfResultOwner = {
      referenceID: stringField(owner.referenceID, 'referenceID', description),
      projectSessionID: stringField(owner.projectSessionID, 'projectSessionID', description),
      projectPath: stringField(owner.projectPath, 'projectPath', description),
      resultPath: stringField(owner.resultPath, 'resultPath', description),
      resultRevision: shaField(owner.resultRevision, 'resultRevision', description),
      embeddedPlanName: stringField(owner.embeddedPlanName, 'embeddedPlanName', description),
      coreLayerContextIdentityToken: stringField(owner.coreLayerContextIdentityToken, 'coreLayerContextIdentityToken', description),
    };
  }
  return {
    rootName,
    nodeType,
    nodeName,
    nodePath: [...row.nodePath],
    assetID: optionalStringField(row.assetID, 'assetID', description),
    mapType: optionalStringField(row.mapType, 'mapType', description),
    filename: optionalStringField(row.filename, 'filename', description),
    filenameRelative: optionalStringField(row.filenameRelative, 'filenameRelative', description),
    parentGeometryAssetID: optionalStringField(row.parentGeometryAssetID, 'parentGeometryAssetID', description),
    parentGeometryNodePath: optionalPathField(row.parentGeometryNodePath, 'parentGeometryNodePath', description),
    parentGeometryNodeName: optionalStringField(row.parentGeometryNodeName, 'parentGeometryNodeName', description),
    associationAssetID: optionalStringField(row.associationAssetID, 'associationAssetID', description),
    associationKind: optionalStringField(row.associationKind, 'associationKind', description),
    parentPlanAssetID: optionalStringField(row.parentPlanAssetID, 'parentPlanAssetID', description),
    parentPlanNodePath: optionalPathField(row.parentPlanNodePath, 'parentPlanNodePath', description),
    parentPlanNodeName: optionalStringField(row.parentPlanNodeName, 'parentPlanNodeName', description),
    parentResultNodeType: optionalStringField(row.parentResultNodeType, 'parentResultNodeType', description),
    parentResultAssetID: optionalStringField(row.parentResultAssetID, 'parentResultAssetID', description),
    parentResultNodePath: optionalPathField(row.parentResultNodePath, 'parentResultNodePath', description),
    parentResultNodeName: optionalStringField(row.parentResultNodeName, 'parentResultNodeName', description),
    parentResultRevision: optionalStringField(row.parentResultRevision, 'parentResultRevision', description),
    parentBoundaryConditionAssetID: optionalStringField(row.parentBoundaryConditionAssetID, 'parentBoundaryConditionAssetID', description),
    parentBoundaryConditionNodePath: optionalPathField(row.parentBoundaryConditionNodePath, 'parentBoundaryConditionNodePath', description),
    parentBoundaryConditionNodeName: optionalStringField(row.parentBoundaryConditionNodeName, 'parentBoundaryConditionNodeName', description),
    sessionHdfResultOwner,
  };
}

export function exportContextKey(context: WorkbenchContext): string {
  return JSON.stringify([context.projectSessionID, context.projectPath, context.projectRevision,
    context.projectXml, context.selectedAsset?.kind ?? null, context.selectedAsset?.path ?? null,
    context.selectedAsset?.revision ?? null]);
}

export async function loadExportMapDocuments(
  backend: Parameters<typeof loadMapDocumentCatalog>[0], context: WorkbenchContext, signal: AbortSignal,
): Promise<{ documents: MapDocumentTab[]; projectXmlSHA: string | null }> {
  if (!context.projectPath || !context.projectRevision || !context.projectSessionID) {
    throw new Error('请先打开带有服务器路径、修订和会话的已保存工程。');
  }
  const catalog = await loadMapDocumentCatalog(backend, context, { signal });
  if (signal.aborted) throw new DOMException('Map catalog request cancelled.', 'AbortError');
  if (catalog.projectRevision !== context.projectRevision) throw new Error('读取地图目录时工程修订已改变。');
  const xmlSHA = await projectXmlSha(context.projectXml);
  return { documents: catalog.documents, projectXmlSHA: xmlSHA };
}

export function createExportMapRequest(
  context: WorkbenchContext, document: MapDocumentTab, projectXmlSHA: string | null,
): ExportMapRequest {
  if (!context.projectPath || !context.projectSessionID || !document.visualizationRevision) {
    throw new Error('当前地图缺少已验证的工程会话或 .vis 修订。');
  }
  return {
    projectPath: context.projectPath,
    projectXml: context.projectXml,
    projectXmlSHA,
    mapID: document.mapID,
    mapIndex: document.mapIndex,
    mapName: document.name,
    mapType: document.mapType,
    expectedVisualizationRevision: document.visualizationRevision,
    projectSessionID: context.projectSessionID,
  };
}

export function parseResultSourceContext(
  value: unknown,
  expected: { context: WorkbenchContext; document: MapDocumentTab; projectXmlSHA: string | null },
): { contextToken: string } {
  const row = requireRecord(value, 'map.resultSources 响应');
  const context = expected.context;
  const document = expected.document;
  if (row.projectPath !== context.projectPath || row.projectRevision !== context.projectRevision ||
      row.projectXmlSHA !== expected.projectXmlSHA || row.visualizationRevision !== document.visualizationRevision ||
      row.mapID !== document.mapID || row.mapIndex !== document.mapIndex || row.mapName !== document.name ||
      row.mapType !== document.mapType) {
    throw new Error('map.resultSources 响应与当前工程或地图修订不匹配。');
  }
  return { contextToken: stringField(row.contextToken, 'contextToken', 'map.resultSources 响应') };
}

export function parseDepthInspection(value: unknown): DepthExportInspection {
  const row = requireRecord(value, 'depthExport.inspect 响应');
  const ownersValue = row.owners;
  if (!Array.isArray(ownersValue)) throw new Error('Host 未返回可用的 Result 来源目录。');
  const owners: DepthExportOwner[] = ownersValue.map((item, index) => {
    const owner = requireRecord(item, `Result 来源 ${index + 1}`);
    const node = parseLocator(owner.node, `Result 来源 ${index + 1} 节点`);
    const nodeKind = stringField(owner.nodeKind, 'nodeKind', `Result 来源 ${index + 1}`);
    if ((nodeKind !== 'Result' && nodeKind !== 'MultiResult') || node.nodeType !== nodeKind || owner.selectionIndex !== 0) {
      throw new Error(`Result 来源 ${index + 1} 不符合深度等值线导出节点契约。`);
    }
    if (!Array.isArray(owner.profiles) || owner.profiles.length === 0) throw new Error(`Result 来源 ${index + 1} 没有可导出的已保存剖面。`);
    const profiles = owner.profiles.map((profileValue, profileIndex) => {
      const profile = requireRecord(profileValue, `Result 来源 ${index + 1} 剖面 ${profileIndex + 1}`);
      const profileID = integerField(profile.index, 'index', 'Result 剖面');
      if (profileID < 0) throw new Error('Result 剖面序号不能为负数。');
      return { index: profileID, name: stringField(profile.name, 'name', 'Result 剖面'), time: stringField(profile.time, 'time', 'Result 剖面') };
    });
    const currentProfile = owner.currentProfileIndex;
    return {
      node,
      nodeKind,
      name: stringField(owner.name, 'name', `Result 来源 ${index + 1}`),
      selectionIndex: 0,
      resultPath: stringField(owner.resultPath, 'resultPath', `Result 来源 ${index + 1}`),
      resultRevision: shaField(owner.resultRevision, 'resultRevision', `Result 来源 ${index + 1}`),
      resultName: stringField(owner.resultName, 'resultName', `Result 来源 ${index + 1}`),
      currentProfileIndex: typeof currentProfile === 'number' && Number.isSafeInteger(currentProfile) ? currentProfile : null,
      profiles,
    };
  });
  const cellSize = finiteField(row.defaultCellSize, 'defaultCellSize', 'depthExport.inspect');
  const simplifyDistance = finiteField(row.defaultSimplifyDistance, 'defaultSimplifyDistance', 'depthExport.inspect');
  if (cellSize <= 0 || simplifyDistance < 0) throw new Error('Host 返回的等值线参数范围无效。');
  return {
    mapID: stringField(row.mapID, 'mapID', 'depthExport.inspect'),
    contextToken: stringField(row.contextToken, 'contextToken', 'depthExport.inspect'),
    defaultCellSize: cellSize,
    defaultSimplifyDistance: simplifyDistance,
    owners,
  };
}

function parseViewport(value: unknown, description: string): RasterMapViewExtent {
  const row = requireRecord(value, description);
  const viewport = {
    minX: finiteField(row.minX, 'minX', description),
    minY: finiteField(row.minY, 'minY', description),
    maxX: finiteField(row.maxX, 'maxX', description),
    maxY: finiteField(row.maxY, 'maxY', description),
    pixelWidth: integerField(row.pixelWidth, 'pixelWidth', description),
    pixelHeight: integerField(row.pixelHeight, 'pixelHeight', description),
  };
  if (viewport.maxX <= viewport.minX || viewport.maxY <= viewport.minY ||
      viewport.pixelWidth < 1 || viewport.pixelHeight < 1) throw new Error(`${description}范围或像素尺寸无效。`);
  return viewport;
}

function parsePolylines(value: unknown, name: string): number[][][] {
  if (!Array.isArray(value)) throw new Error(`Host 返回的 ${name} 列表无效。`);
  return value.map((line, lineIndex) => {
    if (!Array.isArray(line) || line.length < 2) throw new Error(`Host 返回的 ${name} ${lineIndex + 1} 点列无效。`);
    return line.map((point, pointIndex) => {
      const coordinate = requireRecord(point, `Host ${name} ${lineIndex + 1} 坐标 ${pointIndex + 1}`);
      return [
        finiteField(coordinate.x, 'x', `${name} 坐标 ${lineIndex + 1}.${pointIndex + 1}`),
        finiteField(coordinate.y, 'y', `${name} 坐标 ${lineIndex + 1}.${pointIndex + 1}`),
      ];
    });
  });
}

export function parseDepthPreview(value: unknown): DepthExportPreview {
  const row = requireRecord(value, 'depthExport.preview 响应');
  return {
    mapID: stringField(row.mapID, 'mapID', '深度预览'),
    contextToken: stringField(row.contextToken, 'contextToken', '深度预览'),
    resultNode: parseLocator(row.resultNode, '深度预览 Result 节点'),
    selectionIndex: 0,
    resultPath: stringField(row.resultPath, 'resultPath', '深度预览'),
    resultRevision: shaField(row.resultRevision, 'resultRevision', '深度预览'),
    profileIndex: integerField(row.profileIndex, 'profileIndex', '深度预览'),
    profileName: stringField(row.profileName, 'profileName', '深度预览'),
    profileTime: stringField(row.profileTime, 'profileTime', '深度预览'),
    viewport: parseViewport(row.viewport, '深度预览视口'),
    cellSize: finiteField(row.cellSize, 'cellSize', '深度预览'),
    simplifyDistance: finiteField(row.simplifyDistance, 'simplifyDistance', '深度预览'),
    polygons: parsePolylines(row.polygons, '多边形'),
    polylines: parsePolylines(row.polylines, '折线'),
  };
}

function parseArtifacts(value: unknown, description: string): ExportArtifactReceipt[] {
  if (!Array.isArray(value)) throw new Error(`${description}没有文件回执列表。`);
  return value.map((item, index) => {
    const artifact = requireRecord(item, `${description}文件 ${index + 1}`);
    const sizeBytes = integerField(artifact.sizeBytes, 'sizeBytes', description);
    if (sizeBytes < 0) throw new Error(`${description}文件大小无效。`);
    return {
      path: stringField(artifact.path, 'path', description),
      sizeBytes,
      sha256: shaField(artifact.sha256, 'sha256', description),
    };
  });
}

export function parseDepthReceipt(value: unknown): DepthExportReceipt {
  const row = requireRecord(value, 'depthExport.execute 响应');
  if (row.state !== 'completed' && row.state !== 'cancelled') throw new Error('Host 未返回可识别的导出终态。');
  const output = row.outputPath;
  if (output !== null && typeof output !== 'string') throw new Error('深度等值线导出回执路径无效。');
  const polygonCount = integerField(row.polygonCount, 'polygonCount', '深度等值线回执');
  const artifacts = parseArtifacts(row.artifacts, '深度等值线回执');
  if (row.state === 'cancelled' && (output !== null || polygonCount !== 0 || artifacts.length !== 0)) {
    throw new Error('取消回执仍包含已提交文件或几何计数。');
  }
  if (row.state === 'completed' && (!output || polygonCount < 0 || artifacts.length < 3)) {
    throw new Error('完成回执缺少 Shapefile 族的实际文件清单。');
  }
  return {
    state: row.state,
    outputPath: output,
    mapID: stringField(row.mapID, 'mapID', '深度等值线回执'),
    contextToken: stringField(row.contextToken, 'contextToken', '深度等值线回执'),
    resultNode: parseLocator(row.resultNode, '深度等值线回执 Result 节点'),
    selectionIndex: 0,
    resultPath: stringField(row.resultPath, 'resultPath', '深度等值线回执'),
    resultRevision: shaField(row.resultRevision, 'resultRevision', '深度等值线回执'),
    profileIndex: integerField(row.profileIndex, 'profileIndex', '深度等值线回执'),
    profileName: stringField(row.profileName, 'profileName', '深度等值线回执'),
    profileTime: stringField(row.profileTime, 'profileTime', '深度等值线回执'),
    cellSize: finiteField(row.cellSize, 'cellSize', '深度等值线回执'),
    simplifyDistance: finiteField(row.simplifyDistance, 'simplifyDistance', '深度等值线回执'),
    polygonCount,
    artifacts,
  };
}

export function findRasterCandidates(documents: MapDocumentTab[]): RasterCandidate[] {
  const candidates: RasterCandidate[] = [];
  const supported = new Set(['SingleBandRasterMapLayer']);
  const visit = (document: MapDocumentTab, nodes: CatalogTreeNode[]): void => {
    for (const node of nodes) {
      const locator = node.locator;
      if (locator?.nodeType === 'MapLayer' && locator.mapType && supported.has(locator.mapType) &&
          Array.isArray(locator.nodePath) && locator.nodePath.length > 0 && node.assetID) {
        const gdb = /\.gdb[\\/]?$/i.test(node.assetID) && /^(?:GDB|OpenFileGDB):/i.test(locator.filename ?? '');
        candidates.push({ document, node, locator, gdb });
      }
      if (node.children?.length) visit(document, node.children);
    }
  };
  for (const document of documents) visit(document, document.roots);
  return candidates;
}

export function rasterSourceIdentity(
  context: WorkbenchContext,
  document: MapDocumentTab,
  candidate: RasterCandidate,
  projectXmlSHA: string | null,
  gdbSource?: Record<string, unknown>,
): ExportRasterSourceIdentity {
  const projectPath = context.projectPath;
  const visualizationRevision = document.visualizationRevision;
  const locator = candidate.locator;
  const nodePath = locator.nodePath;
  const mapLayerType = locator.mapType;
  if (!projectPath || !visualizationRevision || !nodePath?.length || !mapLayerType) {
    throw new Error('所选栅格不含完整的已保存地图节点身份。');
  }
  const sourcePath = stringField(candidate.node.assetID, 'assetID', 'Host 地图目录栅格源');
  const treeRevision = candidate.node.diagnosticSourceRevision;
  const sourceRevision = treeRevision ? shaField(treeRevision, 'diagnosticSourceRevision', 'Host 地图目录栅格源') : null;
  const mapRequest = createExportMapRequest(context, document, projectXmlSHA);
  const source: ExportRasterSourceIdentity = {
    id: `vis-map-${document.mapIndex}-node-${nodePath.join('.')}`,
    projectPath,
    visualizationPath: changeExtension(projectPath, '.vis'),
    visualizationRevision,
    mapIndex: document.mapIndex,
    mapName: document.name,
    mapType: document.mapType,
    nodePath: [...nodePath],
    nodeName: locator.nodeName,
    mapLayerType,
    filename: locator.filename ?? null,
    filenameRelative: locator.filenameRelative ?? null,
    sourcePath,
    sourceMapID: document.mapID,
    sourceRevision,
    gdbSource: gdbSource ? { map: mapRequest, node: locator, source: gdbSource } : null,
  };
  if (candidate.gdb && !gdbSource) throw new Error('此栅格需要 Host 返回的完整 File Geodatabase 清单。');
  return source;
}

function changeExtension(path: string, extension: string): string {
  const slash = Math.max(path.lastIndexOf('/'), path.lastIndexOf('\\'));
  const dot = path.lastIndexOf('.');
  return dot > slash ? path.slice(0, dot) + extension : path + extension;
}

export function parseRgbInspection(value: unknown): RgbRasterInspection {
  const row = requireRecord(value, 'rasterExport.inspect 响应');
  const extent = {
    minX: finiteField(row.fullMinX, 'fullMinX', 'RGB 栅格检查'),
    minY: finiteField(row.fullMinY, 'fullMinY', 'RGB 栅格检查'),
    maxX: finiteField(row.fullMaxX, 'fullMaxX', 'RGB 栅格检查'),
    maxY: finiteField(row.fullMaxY, 'fullMaxY', 'RGB 栅格检查'),
  };
  if (extent.maxX <= extent.minX || extent.maxY <= extent.minY) throw new Error('Host 返回的栅格范围无效。');
  if (!Array.isArray(row.plotOptions)) throw new Error('Host 未返回栅格渲染选项。');
  const plotOptions = row.plotOptions.map((item, index): RasterPlotOption => {
    const option = requireRecord(item, `渲染选项 ${index + 1}`);
    const kind = stringField(option.kind, 'kind', '栅格渲染选项');
    if (kind !== 'bool' && kind !== 'enum') throw new Error('Host 返回了不支持的栅格渲染选项。');
    if (!Array.isArray(option.enumValues)) throw new Error('Host 栅格渲染选项缺少枚举值。');
    const enumValues = option.enumValues.map((choice, choiceIndex) => {
      const row = requireRecord(choice, `渲染选项 ${index + 1} 枚举 ${choiceIndex + 1}`);
      return { value: stringField(row.value, 'value', '栅格枚举'), label: stringField(row.label, 'label', '栅格枚举') };
    });
    if (kind === 'enum' && !enumValues.some(choice => choice.value === option.value)) throw new Error('Host 当前枚举值不在其选项列表中。');
    if (kind === 'bool' && option.value !== 'true' && option.value !== 'false') throw new Error('Host 返回的布尔渲染值无效。');
    return {
      propertyName: stringField(option.propertyName, 'propertyName', '栅格渲染选项'),
      name: stringField(option.name, 'name', '栅格渲染选项'),
      label: stringField(option.label, 'label', '栅格渲染选项'),
      description: typeof option.description === 'string' ? option.description : '',
      kind,
      value: option.value as string,
      enumValues,
      isEnabled: option.isEnabled === true,
      enabledIf: typeof option.enabledIf === 'string' ? option.enabledIf : null,
    };
  });
  const suggestedCellSize = row.suggestedCellSize === null || row.suggestedCellSize === undefined
    ? null : finiteField(row.suggestedCellSize, 'suggestedCellSize', 'RGB 栅格检查');
  if (suggestedCellSize !== null && suggestedCellSize <= 0) throw new Error('Host 建议的像元大小无效。');
  return {
    sourceID: stringField(row.sourceID, 'sourceID', 'RGB 栅格检查'),
    sourceName: stringField(row.sourceName, 'sourceName', 'RGB 栅格检查'),
    sourceRevision: shaField(row.sourceRevision, 'sourceRevision', 'RGB 栅格检查'),
    visualizationRevision: shaField(row.visualizationRevision, 'visualizationRevision', 'RGB 栅格检查'),
    fullExtent: extent,
    currentView: row.currentView ? parseViewport(row.currentView, 'Host 当前视口') : null,
    suggestedCellSize,
    plotOptions,
  };
}

export function parseRgbPreview(value: unknown): RgbRasterPreview {
  const row = requireRecord(value, 'rasterExport.preview 响应');
  const raster = requireRecord(row.raster, 'RGB TIFF 预览像素');
  const extent = {
    minX: finiteField(row.minX, 'minX', 'RGB TIFF 预览'),
    minY: finiteField(row.minY, 'minY', 'RGB TIFF 预览'),
    maxX: finiteField(row.maxX, 'maxX', 'RGB TIFF 预览'),
    maxY: finiteField(row.maxY, 'maxY', 'RGB TIFF 预览'),
  };
  const width = integerField(raster.width, 'width', 'RGB TIFF 预览');
  const height = integerField(raster.height, 'height', 'RGB TIFF 预览');
  const outputWidth = integerField(row.outputWidth, 'outputWidth', 'RGB TIFF 预览');
  const outputHeight = integerField(row.outputHeight, 'outputHeight', 'RGB TIFF 预览');
  const estimatedFileSizeBytes = integerField(row.estimatedFileSizeBytes, 'estimatedFileSizeBytes', 'RGB TIFF 预览');
  const rasterRgba = stringField(raster.rgba, 'rgba', 'RGB TIFF 预览');
  if (width < 1 || height < 1 || width * height > 2_097_152 || outputWidth < 1 || outputHeight < 1 ||
      estimatedFileSizeBytes < 0 || extent.maxX <= extent.minX || extent.maxY <= extent.minY) {
    throw new Error('Host 返回的 RGB 预览范围或像素尺寸无效。');
  }
  return {
    sourceID: stringField(row.sourceID, 'sourceID', 'RGB TIFF 预览'),
    sourceRevision: shaField(row.sourceRevision, 'sourceRevision', 'RGB TIFF 预览'),
    visualizationRevision: shaField(row.visualizationRevision, 'visualizationRevision', 'RGB TIFF 预览'),
    cellSize: finiteField(row.cellSize, 'cellSize', 'RGB TIFF 预览'),
    extent,
    outputWidth,
    outputHeight,
    estimatedFileSizeBytes,
    raster: {
      id: stringField(raster.id, 'id', 'RGB TIFF 预览像素'),
      sourceMapID: typeof raster.sourceMapID === 'string' ? raster.sourceMapID : null,
      name: stringField(raster.name, 'name', 'RGB TIFF 预览像素'),
      width,
      height,
      rgba: rasterRgba,
      minX: finiteField(raster.minX, 'minX', 'RGB TIFF 预览像素'),
      minY: finiteField(raster.minY, 'minY', 'RGB TIFF 预览像素'),
      maxX: finiteField(raster.maxX, 'maxX', 'RGB TIFF 预览像素'),
      maxY: finiteField(raster.maxY, 'maxY', 'RGB TIFF 预览像素'),
    },
  };
}

export function parseRgbReceipt(value: unknown): RgbRasterReceipt {
  const row = requireRecord(value, 'rasterExport.execute 响应');
  if (row.state !== 'completed' && row.state !== 'cancelled') throw new Error('Host 未返回可识别的 RGB TIFF 终态。');
  const output = row.outputPath;
  if (output !== null && typeof output !== 'string') throw new Error('RGB TIFF 导出回执路径无效。');
  const width = integerField(row.width, 'width', 'RGB TIFF 回执');
  const height = integerField(row.height, 'height', 'RGB TIFF 回执');
  const artifacts = parseArtifacts(row.artifacts, 'RGB TIFF 回执');
  const extent = {
    minX: finiteField(row.minX, 'minX', 'RGB TIFF 回执'),
    minY: finiteField(row.minY, 'minY', 'RGB TIFF 回执'),
    maxX: finiteField(row.maxX, 'maxX', 'RGB TIFF 回执'),
    maxY: finiteField(row.maxY, 'maxY', 'RGB TIFF 回执'),
  };
  if (row.state === 'cancelled' && (output !== null || artifacts.length !== 0 || width !== 0 || height !== 0 ||
      extent.minX !== 0 || extent.minY !== 0 || extent.maxX !== 0 || extent.maxY !== 0)) {
    throw new Error('取消回执仍包含已提交 TIFF 文件或像素尺寸。');
  }
  if (row.state === 'completed' && (!output || artifacts.length !== 1 || width < 1 || height < 1 ||
      extent.maxX <= extent.minX || extent.maxY <= extent.minY)) {
    throw new Error('完成回执缺少唯一 GeoTIFF 文件或有效尺寸。');
  }
  return {
    state: row.state,
    outputPath: output,
    sourceID: stringField(row.sourceID, 'sourceID', 'RGB TIFF 回执'),
    sourceRevision: shaField(row.sourceRevision, 'sourceRevision', 'RGB TIFF 回执'),
    visualizationRevision: shaField(row.visualizationRevision, 'visualizationRevision', 'RGB TIFF 回执'),
    cellSize: finiteField(row.cellSize, 'cellSize', 'RGB TIFF 回执'),
    width,
    height,
    extent,
    artifacts,
  };
}

export function isServerAbsolutePath(value: string): boolean {
  const path = value.trim();
  return path.startsWith('/') || path.startsWith('\\\\') || /^[a-z]:[\\/]/i.test(path);
}

export function normalizeServerPath(value: string): string {
  return value.replace(/\\/g, '/').replace(/\/+$/, '');
}

export function pathFileName(value: string): string {
  const parts = value.replace(/\\/g, '/').split('/');
  return parts.at(-1) ?? '';
}
