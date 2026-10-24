import { useEffect, useRef, useState } from 'react';
import { BackendOperationUnavailableError, errorDiagnostics } from '../../shared/workbench';
import type { BackendGateway, Language, WorkbenchContext } from '../../shared/workbench';
import { projectXmlSha } from './ProjectTreeListener';
import type {
  CatalogTreeNode,
  CatalogTreeNodeLocator,
  GeoFeature,
  GeoGeometry,
  GeoLine,
  GeoJsonLayer,
  GeoPolygon,
  MapDocumentTab,
  MapExtent,
  XY,
} from './mapTypes';

type GdbSourceFileRevision = {
  relativePath: string;
  length: number;
  revision: string;
};

type GdbSourceIdentity = {
  sourcePath: string;
  revision: string;
  files: GdbSourceFileRevision[];
};

type GdbLayerDescriptor = {
  name: string;
  geometryType: string;
  geometryTypeCode: number;
  vectorType: string;
  featureCount: number;
  projectionWKT: string | null;
};

type GdbMapCapture = {
  projectPath: string;
  projectRevision: string;
  projectXmlSHA: string | null;
  visualizationPath: string;
  visualizationRevision: string | null;
  mapID: string | null;
  mapIndex: number | null;
  mapName: string | null;
  mapType: string;
  isPersisted: boolean;
};

type GdbVertex = {
  x: number;
  y: number;
  sourceX: number;
  sourceY: number;
  z: number | null;
  m: number | null;
};

type GdbRing = { role: string; vertices: GdbVertex[] };

type GdbGeometry = {
  geometryType: string;
  geometryTypeCode: number;
  vertices: GdbVertex[];
  rings: GdbRing[];
  children: GdbGeometry[];
};

type GdbAttribute = {
  columnOrdinal: number;
  columnName: string;
  type: string;
  value: string | null;
  booleanValue: boolean | null;
  isSet: boolean;
  isNull: boolean;
  timeZoneFlag: number | null;
};

type GdbFeature = {
  sourceOrdinal: number;
  sourceID: string | null;
  geometry: GdbGeometry;
  attributes: GdbAttribute[];
};

type GdbSourceData = {
  geometryType: string;
  geometryTypeCode: number;
  count: number;
  sourceExtent: MapExtent | null;
  extent: MapExtent | null;
  projectionStatus: 'projected' | 'localProjectSourceCoordinates';
  sourceProjectionWKT: string | null;
  targetProjectionWKT: string | null;
  features: GdbFeature[];
};

type GdbSourceLoadResult = {
  map: GdbMapCapture;
  node: CatalogTreeNodeLocator;
  source: GdbSourceIdentity;
  layer: GdbLayerDescriptor;
  data: GdbSourceData;
};

type GdbCatalogResult = { source: GdbSourceIdentity; layers: GdbLayerDescriptor[] };

export interface PersistedGdbLayerCapture {
  layerID: string;
  nodeID: string;
  map: GdbMapCapture;
  locator: CatalogTreeNodeLocator;
  source: GdbSourceIdentity;
  layer: GdbLayerDescriptor;
  /** Full C# capture, including recursive geometry, source/project XY, Z/M and typed field state. */
  data: GdbSourceData;
}

export interface PersistedGdbLayerIssue {
  nodeID: string;
  name: string;
  message: string;
  diagnostic?: unknown;
}

export interface PersistedGdbLayersError {
  message: string;
  diagnostic: unknown;
}

export interface UsePersistedGdbLayersProps {
  backend: BackendGateway;
  context: WorkbenchContext;
  document: MapDocumentTab | null;
  language: Language;
}

export interface UsePersistedGdbLayersResult {
  layers: GeoJsonLayer[];
  captures: PersistedGdbLayerCapture[];
  issues: PersistedGdbLayerIssue[];
  loading: boolean;
  error: PersistedGdbLayersError | null;
}

const EMPTY_RESULT: UsePersistedGdbLayersResult = {
  layers: [], captures: [], issues: [], loading: false, error: null,
};

// Keep aligned with MapTreeService.CheckableNodeTypes: GdbDataset is a
// checkable parent, so a checked child under an unchecked parent is hidden.
const checkableNodeTypes = new Set([
  'Geometry', 'Result', 'MultiResult', 'Terrain', 'BoundaryCondition', 'WindLayer', 'PrecipitationLayer',
  'NValue', 'Classification', 'Feature', 'MapLayer', 'GdbDataset', 'GdbVectorLayer',
  'ConceptualMesh', 'FlowAreaLayer', 'CulvertBarrelLayer',
]);

type GdbVectorCandidate = {
  node: CatalogTreeNode;
  locator: CatalogTreeNodeLocator;
  dataset: CatalogTreeNode;
  datasetLocator: CatalogTreeNodeLocator;
  sourcePath: string;
};

type GdbCandidateGroup = { dataset: CatalogTreeNode; sourcePath: string; candidates: GdbVectorCandidate[] };

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isRevision(value: unknown): value is string {
  return typeof value === 'string' && /^[0-9a-f]{64}$/i.test(value);
}

function isFiniteNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value);
}

function isOptionalString(value: unknown): value is string | null | undefined {
  return value === null || value === undefined || typeof value === 'string';
}

function objectValue(value: unknown, label: string): Record<string, unknown> {
  if (!isRecord(value)) throw new Error(`${label} 无效。`);
  return value;
}

function optionalNull(value: unknown): boolean {
  return value === null || value === undefined;
}

function pathKey(value: string): string {
  return value.replaceAll('\\', '/').replace(/\/+$/, '');
}

function samePath(left: string, right: string): boolean {
  const leftKey = pathKey(left);
  const rightKey = pathKey(right);
  const isWindowsPath = /^[a-z]:\//i.test(leftKey) || leftKey.startsWith('//') ||
    /^[a-z]:\//i.test(rightKey) || rightKey.startsWith('//');
  return isWindowsPath
    ? leftKey.toLocaleLowerCase('en-US') === rightKey.toLocaleLowerCase('en-US')
    : leftKey === rightKey;
}

function isAbsolutePath(value: string): boolean {
  return value.startsWith('/') || /^[a-z]:[\\/]/i.test(value) || value.startsWith('\\\\');
}

function isGdbDirectoryPath(value: string): boolean {
  return /\.gdb$/i.test(pathKey(value));
}

function basename(value: string): string {
  return pathKey(value).split('/').pop() ?? value;
}

function parseExtent(value: unknown, label: string): MapExtent | null {
  if (optionalNull(value)) return null;
  const extent = objectValue(value, label);
  if (!isFiniteNumber(extent.minX) || !isFiniteNumber(extent.minY) ||
      !isFiniteNumber(extent.maxX) || !isFiniteNumber(extent.maxY) ||
      extent.minX > extent.maxX || extent.minY > extent.maxY) {
    throw new Error(`${label} 无效。`);
  }
  return { minX: extent.minX, minY: extent.minY, maxX: extent.maxX, maxY: extent.maxY };
}

function parseSourceIdentity(value: unknown): GdbSourceIdentity {
  const source = objectValue(value, 'GDB 目录来源快照');
  if (typeof source.sourcePath !== 'string' || !isAbsolutePath(source.sourcePath) ||
      !isGdbDirectoryPath(source.sourcePath) || !isRevision(source.revision) || !Array.isArray(source.files) ||
      source.files.length === 0) {
    throw new Error('服务端返回的 GDB 目录来源快照无效。');
  }
  let previous = '';
  const seen = new Set<string>();
  const files = source.files.map((item): GdbSourceFileRevision => {
    const file = objectValue(item, 'GDB 文件目录清单');
    if (typeof file.relativePath !== 'string' || file.relativePath.length === 0 ||
        file.relativePath.startsWith('/') || file.relativePath.includes('\\') ||
        file.relativePath.split('/').some((part) => part === '' || part === '.' || part === '..') ||
        !Number.isSafeInteger(file.length) || (file.length as number) < 0 || !isRevision(file.revision)) {
      throw new Error('服务端返回的 GDB 文件清单项无效。');
    }
    const relativePath = file.relativePath;
    if (relativePath <= previous || seen.has(relativePath) ||
        /(?:\.lock|\.lck)$/i.test(relativePath) || /(^|\/)\.~lock\./i.test(relativePath)) {
      throw new Error('GDB 文件清单未按完整、稳定的目录快照排序。');
    }
    previous = relativePath;
    seen.add(relativePath);
    return { relativePath, length: file.length as number, revision: file.revision };
  });
  return { sourcePath: source.sourcePath, revision: source.revision, files };
}

function sameSourceIdentity(left: GdbSourceIdentity, right: GdbSourceIdentity): boolean {
  return samePath(left.sourcePath, right.sourcePath) &&
    left.revision.toUpperCase() === right.revision.toUpperCase() && left.files.length === right.files.length &&
    left.files.every((file, index) => {
      const other = right.files[index];
      return file.relativePath === other.relativePath && file.length === other.length &&
        file.revision.toUpperCase() === other.revision.toUpperCase();
    });
}

function parseLayerDescriptor(value: unknown): GdbLayerDescriptor {
  const layer = objectValue(value, 'GDB 矢量图层目录项');
  if (typeof layer.name !== 'string' || layer.name.length === 0 ||
      typeof layer.geometryType !== 'string' || !Number.isSafeInteger(layer.geometryTypeCode) ||
      typeof layer.vectorType !== 'string' || !Number.isSafeInteger(layer.featureCount) ||
      (layer.featureCount as number) < 0 || !isOptionalString(layer.projectionWKT)) {
    throw new Error('服务端返回的 GDB 图层目录项无效。');
  }
  return {
    name: layer.name,
    geometryType: layer.geometryType,
    geometryTypeCode: layer.geometryTypeCode as number,
    vectorType: layer.vectorType,
    featureCount: layer.featureCount as number,
    projectionWKT: layer.projectionWKT as string | null | undefined ?? null,
  };
}

function parseCatalog(value: unknown): GdbCatalogResult {
  const catalog = objectValue(value, 'map.gdb.catalog 响应');
  if (!Array.isArray(catalog.layers)) throw new Error('服务端返回的 GDB 矢量目录无效。');
  const source = parseSourceIdentity(catalog.source);
  const layers = catalog.layers.map(parseLayerDescriptor);
  if (new Set(layers.map((layer) => layer.name)).size !== layers.length) {
    throw new Error('GDB 矢量目录包含重复图层名称。');
  }
  return { source, layers };
}

function parseMapCapture(value: unknown): GdbMapCapture {
  const map = objectValue(value, 'GDB 地图来源身份');
  if (typeof map.projectPath !== 'string' || !isRevision(map.projectRevision) ||
      !isOptionalString(map.projectXmlSHA) || (map.projectXmlSHA !== null && map.projectXmlSHA !== undefined && !isRevision(map.projectXmlSHA)) ||
      typeof map.visualizationPath !== 'string' ||
      !isOptionalString(map.visualizationRevision) ||
      (map.visualizationRevision !== null && map.visualizationRevision !== undefined && !isRevision(map.visualizationRevision)) ||
      !isOptionalString(map.mapID) ||
      !(map.mapIndex === null || map.mapIndex === undefined || (Number.isInteger(map.mapIndex) && (map.mapIndex as number) >= 0)) ||
      !isOptionalString(map.mapName) || typeof map.mapType !== 'string' || typeof map.isPersisted !== 'boolean') {
    throw new Error('服务端返回的 GDB 地图来源身份无效。');
  }
  return {
    projectPath: map.projectPath,
    projectRevision: map.projectRevision,
    projectXmlSHA: map.projectXmlSHA as string | null | undefined ?? null,
    visualizationPath: map.visualizationPath,
    visualizationRevision: map.visualizationRevision as string | null | undefined ?? null,
    mapID: map.mapID as string | null | undefined ?? null,
    mapIndex: map.mapIndex as number | null | undefined ?? null,
    mapName: map.mapName as string | null | undefined ?? null,
    mapType: map.mapType,
    isPersisted: map.isPersisted,
  };
}

function parseLocator(value: unknown): CatalogTreeNodeLocator {
  const locator = objectValue(value, 'GDB 图层定位信息');
  if (typeof locator.rootName !== 'string' || typeof locator.nodeType !== 'string' ||
      typeof locator.nodeName !== 'string' || !Array.isArray(locator.nodePath) ||
      !locator.nodePath.every((part) => Number.isInteger(part) && (part as number) >= 0) ||
      !isOptionalString(locator.assetID) || !isOptionalString(locator.mapType) ||
      !isOptionalString(locator.filename) || !isOptionalString(locator.filenameRelative)) {
    throw new Error('服务端返回的 GDB 图层定位信息无效。');
  }
  return locator as unknown as CatalogTreeNodeLocator;
}

function parseVertex(value: unknown): GdbVertex {
  const vertex = objectValue(value, 'GDB 源顶点');
  if (!isFiniteNumber(vertex.x) || !isFiniteNumber(vertex.y) ||
      !isFiniteNumber(vertex.sourceX) || !isFiniteNumber(vertex.sourceY) ||
      !(optionalNull(vertex.z) || isFiniteNumber(vertex.z)) ||
      !(optionalNull(vertex.m) || isFiniteNumber(vertex.m))) {
    throw new Error('GDB 图层包含无效的源坐标、工程坐标或 Z/M 值。');
  }
  return {
    x: vertex.x,
    y: vertex.y,
    sourceX: vertex.sourceX,
    sourceY: vertex.sourceY,
    z: vertex.z as number | null | undefined ?? null,
    m: vertex.m as number | null | undefined ?? null,
  };
}

function parseGeometry(value: unknown, depth = 0): GdbGeometry {
  if (depth > 64) throw new Error('GDB 几何集合嵌套超过支持范围。');
  const geometry = objectValue(value, 'GDB 几何');
  if (typeof geometry.geometryType !== 'string' || !Number.isSafeInteger(geometry.geometryTypeCode) ||
      !Array.isArray(geometry.vertices) || !Array.isArray(geometry.rings) || !Array.isArray(geometry.children)) {
    throw new Error('服务端返回的 GDB 几何结构无效。');
  }
  const vertices = geometry.vertices.map(parseVertex);
  const rings = geometry.rings.map((item): GdbRing => {
    const ring = objectValue(item, 'GDB 多边形环');
    if (ring.role !== 'exterior' && ring.role !== 'interior' || !Array.isArray(ring.vertices)) {
      throw new Error('服务端返回的 GDB 多边形环类型无效。');
    }
    return { role: ring.role, vertices: ring.vertices.map(parseVertex) };
  });
  const children = geometry.children.map((child) => parseGeometry(child, depth + 1));
  return {
    geometryType: geometry.geometryType,
    geometryTypeCode: geometry.geometryTypeCode as number,
    vertices,
    rings,
    children,
  };
}

function parseAttribute(value: unknown): GdbAttribute {
  const attribute = objectValue(value, 'GDB 属性字段');
  if (!Number.isInteger(attribute.columnOrdinal) || (attribute.columnOrdinal as number) < 0 ||
      typeof attribute.columnName !== 'string' || typeof attribute.type !== 'string' ||
      !isOptionalString(attribute.value) ||
      !(optionalNull(attribute.booleanValue) || typeof attribute.booleanValue === 'boolean') ||
      typeof attribute.isSet !== 'boolean' || typeof attribute.isNull !== 'boolean' ||
      (attribute.timeZoneFlag !== undefined && attribute.timeZoneFlag !== null && !Number.isInteger(attribute.timeZoneFlag))) {
    throw new Error('服务端返回的 GDB 属性字段无效。');
  }
  const isSet = attribute.isSet;
  const isNull = attribute.isNull;
  const rawValue = attribute.value as string | null | undefined ?? null;
  const booleanValue = attribute.booleanValue as boolean | null | undefined ?? null;
  if ((isNull && !isSet) || ((!isSet || isNull) && (rawValue !== null || booleanValue !== null)) ||
      (isSet && !isNull && (rawValue === null) === (booleanValue === null))) {
    throw new Error('GDB 属性的未设置、空值和实际值状态不一致。');
  }
  return {
    columnOrdinal: attribute.columnOrdinal as number,
    columnName: attribute.columnName,
    type: attribute.type,
    value: rawValue,
    booleanValue,
    isSet,
    isNull,
    timeZoneFlag: attribute.timeZoneFlag as number | null | undefined ?? null,
  };
}

function parseFeature(value: unknown): GdbFeature {
  const feature = objectValue(value, 'GDB 要素');
  if (!Number.isInteger(feature.sourceOrdinal) || (feature.sourceOrdinal as number) < 0 ||
      !isOptionalString(feature.sourceID) || !Array.isArray(feature.attributes)) {
    throw new Error('服务端返回的 GDB 要素结构无效。');
  }
  const attributes = feature.attributes.map(parseAttribute).sort((left, right) => left.columnOrdinal - right.columnOrdinal);
  if (new Set(attributes.map((attribute) => attribute.columnOrdinal)).size !== attributes.length) {
    throw new Error('GDB 属性列序号重复。');
  }
  return {
    sourceOrdinal: feature.sourceOrdinal as number,
    sourceID: feature.sourceID as string | null | undefined ?? null,
    geometry: parseGeometry(feature.geometry),
    attributes,
  };
}

function parseSourceData(value: unknown): GdbSourceData {
  const data = objectValue(value, 'GDB 矢量来源数据');
  if (typeof data.geometryType !== 'string' || !Number.isSafeInteger(data.geometryTypeCode) ||
      !Number.isInteger(data.count) || (data.count as number) < 0 ||
      !['projected', 'localProjectSourceCoordinates'].includes(String(data.projectionStatus)) ||
      !isOptionalString(data.sourceProjectionWKT) || !isOptionalString(data.targetProjectionWKT) ||
      !Array.isArray(data.features)) {
    throw new Error('服务端返回的 GDB 几何数据或投影信息无效。');
  }
  const features = data.features.map(parseFeature);
  if (features.length !== data.count || new Set(features.map((feature) => feature.sourceOrdinal)).size !== features.length) {
    throw new Error('GDB 图层目录记录数与读取到的实际要素数不一致。');
  }
  const sourceExtent = parseExtent(data.sourceExtent, 'GDB 源坐标范围');
  const extent = parseExtent(data.extent, 'GDB 工程坐标范围');
  if (data.projectionStatus === 'projected' &&
      (!(data.sourceProjectionWKT as string | null)?.trim() || !(data.targetProjectionWKT as string | null)?.trim())) {
    throw new Error('GDB 图层投影转换缺少源坐标系或工程坐标系定义。');
  }
  if (data.projectionStatus === 'localProjectSourceCoordinates' && data.targetProjectionWKT != null) {
    throw new Error('本地工程坐标的 GDB 图层不能带有目标投影定义。');
  }
  if (features.some((feature) => feature.geometry.geometryType !== 'None') && extent === null) {
    throw new Error('GDB 图层包含几何要素，但服务端没有返回可绘制的工程坐标范围。');
  }
  return {
    geometryType: data.geometryType,
    geometryTypeCode: data.geometryTypeCode as number,
    count: data.count as number,
    sourceExtent,
    extent,
    projectionStatus: data.projectionStatus as GdbSourceData['projectionStatus'],
    sourceProjectionWKT: data.sourceProjectionWKT as string | null | undefined ?? null,
    targetProjectionWKT: data.targetProjectionWKT as string | null | undefined ?? null,
    features,
  };
}

function parseSourceLoad(value: unknown): GdbSourceLoadResult {
  const result = objectValue(value, 'map.gdb.source.load 响应');
  return {
    map: parseMapCapture(result.map),
    node: parseLocator(result.node),
    source: parseSourceIdentity(result.source),
    layer: parseLayerDescriptor(result.layer),
    data: parseSourceData(result.data),
  };
}

function nodeType(node: CatalogTreeNode): string {
  if (node.locator?.nodeType) return node.locator.nodeType;
  if (node.sourceClass === 'GdbDatasetVisualContext') return 'GdbDataset';
  if (node.sourceClass === 'GdbVectorLayerVisualContext') return 'GdbVectorLayer';
  return '';
}

function sameLocator(left: CatalogTreeNodeLocator, right: CatalogTreeNodeLocator): boolean {
  return left.rootName === right.rootName && left.nodeType === right.nodeType && left.nodeName === right.nodeName &&
    left.mapType === right.mapType && left.assetID === right.assetID && left.filename === right.filename &&
    left.filenameRelative === right.filenameRelative &&
    JSON.stringify(left.nodePath ?? null) === JSON.stringify(right.nodePath ?? null);
}

function validChildLocator(node: CatalogTreeNode, dataset: CatalogTreeNode): string | null {
  const locator = node.locator;
  const datasetLocator = dataset.locator;
  if (!locator || !datasetLocator || locator.nodeType !== 'GdbVectorLayer' || locator.mapType !== 'GdbVectorLayer' ||
      datasetLocator.nodeType !== 'GdbDataset' || datasetLocator.mapType !== 'GdbDataset' ||
      !Array.isArray(locator.nodePath) || !Array.isArray(datasetLocator.nodePath) ||
      locator.nodePath.length !== datasetLocator.nodePath.length + 1 ||
      !datasetLocator.nodePath.every((part, index) => locator.nodePath?.[index] === part) ||
      !locator.nodePath.every((part) => Number.isInteger(part) && part >= 0) ||
      locator.nodeName !== node.name || locator.nodeName.length === 0 ||
      !locator.assetID || !datasetLocator.assetID || !locator.filename || !datasetLocator.filename ||
      !locator.filenameRelative || !datasetLocator.filenameRelative ||
      !samePath(locator.assetID, datasetLocator.assetID) ||
      !samePath(locator.filename, datasetLocator.filename) ||
      locator.filenameRelative !== datasetLocator.filenameRelative ||
      !isAbsolutePath(datasetLocator.assetID) || !isGdbDirectoryPath(datasetLocator.assetID)) {
    return null;
  }
  return datasetLocator.assetID;
}

function collectCandidates(roots: CatalogTreeNode[]): { groups: GdbCandidateGroup[]; issues: PersistedGdbLayerIssue[] } {
  const groups = new Map<string, GdbCandidateGroup>();
  const issues: PersistedGdbLayerIssue[] = [];
  const addIssue = (node: CatalogTreeNode, text: string) => issues.push({ nodeID: node.id, name: node.name, message: text });

  const visit = (node: CatalogTreeNode, ancestorsVisible: boolean, parent: CatalogTreeNode | null) => {
    const type = nodeType(node);
    const isCheckable = checkableNodeTypes.has(type);
    const visible = ancestorsVisible && (!isCheckable || node.checked === 'checked');
    if (type === 'GdbVectorLayer' && ancestorsVisible && node.checked !== 'unchecked') {
      if (node.checked !== 'checked') {
        addIssue(node, '地图树没有确认此 GDB 矢量图层已勾选，未读取。');
      } else if (!parent || nodeType(parent) !== 'GdbDataset' || parent.checked !== 'checked') {
        addIssue(node, 'GDB 矢量图层所属的数据集未通过勾选校验，未读取。');
      } else {
        const sourcePath = validChildLocator(node, parent);
        if (!sourcePath || !node.locator || !parent.locator) {
          addIssue(node, '地图树没有提供可验证的 GDB 目录和精确子图层定位信息，未读取。');
        } else {
          const groupKey = `${parent.id}\u0000${pathKey(sourcePath)}`;
          let group = groups.get(groupKey);
          if (!group) {
            group = { dataset: parent, sourcePath, candidates: [] };
            groups.set(groupKey, group);
          }
          group.candidates.push({ node, locator: node.locator, dataset: parent, datasetLocator: parent.locator, sourcePath });
        }
      }
    }
    for (const child of node.children ?? []) visit(child, visible, node);
  };

  for (const root of roots) visit(root, true, null);
  return { groups: [...groups.values()], issues };
}

function sameMapCapture(
  map: GdbMapCapture,
  context: WorkbenchContext,
  document: MapDocumentTab,
  projectXmlSHA: string,
): boolean {
  const expectedVisualization = context.projectPath ? visualizationPathFor(context.projectPath) : '';
  return samePath(map.projectPath, context.projectPath ?? '') &&
    map.projectRevision.toUpperCase() === (context.projectRevision ?? '').toUpperCase() &&
    map.projectXmlSHA?.toUpperCase() === projectXmlSHA.toUpperCase() &&
    samePath(map.visualizationPath, expectedVisualization) &&
    map.visualizationRevision?.toUpperCase() === document.visualizationRevision?.toUpperCase() &&
    map.mapID === document.mapID && map.mapIndex === document.mapIndex && map.mapName === document.name &&
    map.mapType === document.mapType && map.isPersisted;
}

function visualizationPathFor(projectPath: string): string {
  const separator = Math.max(projectPath.lastIndexOf('/'), projectPath.lastIndexOf('\\'));
  const extension = projectPath.lastIndexOf('.');
  return extension > separator ? projectPath.slice(0, extension) + '.vis' : projectPath + '.vis';
}

function verifyLayerDescriptor(left: GdbLayerDescriptor, right: GdbLayerDescriptor): boolean {
  return left.name === right.name && left.geometryType === right.geometryType &&
    left.geometryTypeCode === right.geometryTypeCode && left.vectorType === right.vectorType &&
    left.featureCount === right.featureCount && left.projectionWKT === right.projectionWKT;
}

function verifySourceLoad(
  result: GdbSourceLoadResult,
  context: WorkbenchContext,
  document: MapDocumentTab,
  projectXmlSHA: string,
  candidate: GdbVectorCandidate,
  catalogSource: GdbSourceIdentity,
  catalogLayer: GdbLayerDescriptor,
): void {
  if (!sameMapCapture(result.map, context, document, projectXmlSHA) ||
      !sameLocator(result.node, candidate.locator) ||
      !samePath(result.source.sourcePath, candidate.sourcePath) ||
      !sameSourceIdentity(result.source, catalogSource) ||
      !verifyLayerDescriptor(result.layer, catalogLayer) || result.layer.name !== candidate.node.name ||
      result.data.geometryType !== result.layer.vectorType ||
      result.data.geometryTypeCode !== result.layer.geometryTypeCode ||
      result.data.count !== result.layer.featureCount) {
    throw new Error('读取 GDB 图层期间，工程、地图、`.vis`、目录清单或子图层身份发生变化。');
  }
}

function geometryCodeIsCollection(code: number): boolean {
  const normalized = code & 0x7fffffff;
  return normalized === 7 || normalized % 1000 === 7;
}

function point(vertex: GdbVertex): XY {
  return [vertex.x, vertex.y];
}

function polygonCoordinates(geometry: GdbGeometry): GeoPolygon {
  if (geometry.geometryType !== 'Polygon' || geometry.vertices.length !== 0 || geometry.children.length !== 0) {
    throw new Error('GDB 多面图层包含与多边形环结构不一致的几何。');
  }
  const exterior = geometry.rings.filter((ring) => ring.role === 'exterior');
  const interiors = geometry.rings.filter((ring) => ring.role === 'interior');
  if (exterior.length !== 1 || geometry.rings.length !== exterior.length + interiors.length ||
      geometry.rings.some((ring) => ring.vertices.length < 3)) {
    throw new Error('GDB 多边形没有唯一外环，或其洞环数据不完整。');
  }
  return [exterior[0].vertices.map(point), ...interiors.map((ring) => ring.vertices.map(point))];
}

function geometryFromGdb(geometry: GdbGeometry, depth = 0): GeoGeometry {
  if (depth > 64) throw new Error('GDB 几何集合嵌套超过支持范围。');
  switch (geometry.geometryType) {
    case 'None':
      return { type: 'GeometryCollection', geometries: [] };
    case 'Point':
      if (geometry.vertices.length !== 1 || geometry.rings.length || geometry.children.length) {
        throw new Error('GDB 点要素的坐标结构无效。');
      }
      return { type: 'Point', coordinates: point(geometry.vertices[0]) };
    case 'PointCollection': {
      if (geometry.vertices.length || geometry.rings.length) throw new Error('GDB 多点要素的坐标结构无效。');
      const coordinates: XY[] = [];
      for (const child of geometry.children) {
        const parsed = geometryFromGdb(child, depth + 1);
        if (parsed.type !== 'Point') throw new Error('GDB 多点要素包含非点子几何。');
        coordinates.push(parsed.coordinates);
      }
      return { type: 'MultiPoint', coordinates };
    }
    case 'Line':
      if (geometry.rings.length || geometry.children.length) throw new Error('GDB 线要素的坐标结构无效。');
      return { type: 'LineString', coordinates: geometry.vertices.map(point) };
    case 'LineCollection': {
      if (geometry.vertices.length || geometry.rings.length) throw new Error('GDB 多线要素的坐标结构无效。');
      const coordinates: GeoLine[] = [];
      for (const child of geometry.children) {
        const parsed = geometryFromGdb(child, depth + 1);
        if (parsed.type !== 'LineString') throw new Error('GDB 多线要素包含非线子几何。');
        coordinates.push(parsed.coordinates);
      }
      return { type: 'MultiLineString', coordinates };
    }
    case 'Polygon':
      return { type: 'Polygon', coordinates: polygonCoordinates(geometry) };
    case 'PolygonCollection': {
      if (geometry.vertices.length || geometry.rings.length) throw new Error('GDB 多面要素的坐标结构无效。');
      const coordinates: GeoPolygon[] = [];
      for (const child of geometry.children) {
        const parsed = geometryFromGdb(child, depth + 1);
        if (parsed.type !== 'Polygon') throw new Error('GDB 多面要素包含非多边形子几何。');
        coordinates.push(parsed.coordinates);
      }
      return { type: 'MultiPolygon', coordinates };
    }
    case 'Unknown':
      if (geometryCodeIsCollection(geometry.geometryTypeCode)) {
        return { type: 'GeometryCollection', geometries: geometry.children.map((child) => geometryFromGdb(child, depth + 1)) };
      }
      throw new Error(`GDB 图层包含不支持的几何类型代码 ${geometry.geometryTypeCode}。`);
    default:
      throw new Error(`GDB 图层包含不支持的几何类型 ${geometry.geometryType}。`);
  }
}

function propertyValue(attribute: GdbAttribute): unknown {
  if (!attribute.isSet || attribute.isNull) return null;
  return attribute.booleanValue !== null ? attribute.booleanValue : attribute.value;
}

function featureProperties(attributes: GdbAttribute[]): Record<string, unknown> {
  const properties: Record<string, unknown> = Object.create(null) as Record<string, unknown>;
  for (const attribute of attributes) {
    const previous = properties[attribute.columnName];
    const value = propertyValue(attribute);
    if (previous === undefined) properties[attribute.columnName] = value;
    else if (Array.isArray(previous)) properties[attribute.columnName] = [...previous, value];
    else properties[attribute.columnName] = [previous, value];
  }
  return properties;
}

function featureName(properties: Record<string, unknown>, layerName: string, ordinal: number): string {
  for (const key of ['name', 'Name', 'title', 'Title', 'id', 'ID']) {
    const value = properties[key];
    if (typeof value === 'string' && value.trim()) return value.trim();
    if (typeof value === 'number' && Number.isFinite(value)) return String(value);
  }
  return `${layerName} · 要素 ${ordinal + 1}`;
}

function createLayer(
  result: GdbSourceLoadResult,
  document: MapDocumentTab,
  candidate: GdbVectorCandidate,
): { layer: GeoJsonLayer; capture: PersistedGdbLayerCapture } {
  const { data, source, map, layer: descriptor } = result;
  if (data.extent === null) throw new Error('GDB 图层没有可绘制的工程坐标，未载入画布。');
  const layerID = `persisted-gdb-vector:${document.id}:${candidate.node.id}`;
  const features: GeoFeature[] = data.features.map((feature) => {
    const properties = featureProperties(feature.attributes);
    const identity = feature.sourceID === null
      ? `record:${feature.sourceOrdinal}`
      : `source:${feature.sourceID}:${feature.sourceOrdinal}`;
    return {
      id: `${layerID}:${identity}`,
      name: featureName(properties, candidate.node.name, feature.sourceOrdinal),
      geometry: geometryFromGdb(feature.geometry),
      properties,
    };
  });
  const sourceName = `${basename(source.sourcePath)} · ${descriptor.name}`;
  const layerValue: GeoJsonLayer = {
    id: layerID,
    name: candidate.node.name,
    sourceName,
    crs: data.projectionStatus === 'projected'
      ? data.targetProjectionWKT!
      : '工程本地坐标',
    coordinateSpace: 'project',
    features,
    extent: data.extent,
    visible: true,
    opacity: 1,
  };
  return {
    layer: layerValue,
    capture: {
      layerID,
      nodeID: candidate.node.id,
      map,
      locator: result.node,
      source,
      layer: descriptor,
      data,
    },
  };
}

function issueFor(node: CatalogTreeNode, error: unknown): PersistedGdbLayerIssue {
  const rawMessage = error instanceof Error ? error.message : String(error ?? '');
  const message = error instanceof BackendOperationUnavailableError
    ? error.localizedMessage('zh')
    : /[\u3400-\u9fff]/.test(rawMessage)
      ? rawMessage
      : 'GDB 矢量图层未能通过来源校验，未载入；请查看诊断信息。';
  return {
    nodeID: node.id,
    name: node.name,
    message,
    diagnostic: errorDiagnostics(error),
  };
}

function makeError(error: unknown): PersistedGdbLayersError {
  const operationMessage = error instanceof BackendOperationUnavailableError
    ? error.localizedMessage('zh')
    : '读取已保存的 GDB 矢量图层失败；请查看诊断信息。';
  return { message: operationMessage, diagnostic: errorDiagnostics(error) };
}

/**
 * Loads checked GdbVectorLayer children from their exact persisted map-tree rows.
 * The catalog's Source.Revision is the full .gdb directory manifest revision;
 * child diagnostic fields do not contain a GDB source revision.
 */
export function usePersistedGdbLayers({
  backend,
  context,
  document,
  language,
}: UsePersistedGdbLayersProps): UsePersistedGdbLayersResult {
  const [state, setState] = useState<UsePersistedGdbLayersResult>(EMPTY_RESULT);
  const [backendRevision, setBackendRevision] = useState(0);
  const requestGeneration = useRef(0);

  useEffect(() => backend.subscribeCapabilities?.(setBackendRevision), [backend]);

  useEffect(() => {
    const generation = ++requestGeneration.current;
    const controller = new AbortController();
    const current = () => generation === requestGeneration.current && !controller.signal.aborted;
    if (!document) {
      setState(EMPTY_RESULT);
      return () => {
        controller.abort();
        requestGeneration.current += 1;
      };
    }

    setState({ ...EMPTY_RESULT, loading: true });
    let discoveredIssues: PersistedGdbLayerIssue[] = [];
    void (async () => {
      if (!context.projectPath || !context.projectRevision || !isRevision(context.projectRevision) ||
          typeof context.projectXml !== 'string' || !context.projectXml.trim()) {
        throw new Error('请先打开具有有效版本信息的已保存工程，再读取 GDB 矢量图层。');
      }
      if (!document.visualizationRevision || !isRevision(document.visualizationRevision)) {
        throw new Error('当前地图没有可校验的 `.vis` 文件版本，无法安全读取 GDB 来源。');
      }
      const projectXmlSHA = await projectXmlSha(context.projectXml);
      if (!projectXmlSHA) throw new Error('当前浏览器无法校验工程 XML 快照，未读取 GDB 来源。');
      if (!current()) return;

      const { groups, issues } = collectCandidates(document.roots);
      discoveredIssues = issues;
      if (groups.length === 0) {
        if (current()) setState({ layers: [], captures: [], issues, loading: false, error: null });
        return;
      }
      if (!backend.supports('map.gdb.catalog')) {
        throw new BackendOperationUnavailableError('map.gdb.catalog');
      }
      if (!backend.supports('map.gdb.source.load')) {
        throw new BackendOperationUnavailableError('map.gdb.source.load');
      }

      const map = {
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
      const layers: GeoJsonLayer[] = [];
      const captures: PersistedGdbLayerCapture[] = [];

      for (const group of groups) {
        if (!current()) return;
        let catalog: GdbCatalogResult;
        try {
          const rawCatalog = await backend.request<unknown>('map.gdb.catalog', {
            sourcePath: group.sourcePath,
          }, { signal: controller.signal });
          if (!current()) return;
          catalog = parseCatalog(rawCatalog);
          if (pathKey(catalog.source.sourcePath) !== pathKey(group.sourcePath)) {
            throw new Error('GDB 目录响应指向了另一个数据集。');
          }
        } catch (error) {
          if (!current()) return;
          for (const candidate of group.candidates) issues.push(issueFor(candidate.node, error));
          continue;
        }

        for (const candidate of group.candidates) {
          if (!current()) return;
          const catalogMatches = catalog.layers.filter((layer) => layer.name === candidate.locator.nodeName);
          if (catalogMatches.length !== 1) {
            issues.push(issueFor(candidate.node, new Error('GDB 目录中没有唯一匹配的已保存矢量子图层。')));
            continue;
          }
          const catalogLayer = catalogMatches[0];
          try {
            const raw = await backend.request<unknown>('map.gdb.source.load', {
              map,
              node: candidate.locator,
              expectedSourceRevision: catalog.source.revision,
            }, { signal: controller.signal });
            if (!current()) return;
            const result = parseSourceLoad(raw);
            verifySourceLoad(result, context, document, projectXmlSHA, candidate, catalog.source, catalogLayer);
            const loaded = createLayer(result, document, candidate);
            layers.push(loaded.layer);
            captures.push(loaded.capture);
          } catch (error) {
            if (!current()) return;
            issues.push(issueFor(candidate.node, error));
          }
        }
      }

      if (current()) setState({ layers, captures, issues, loading: false, error: null });
    })().catch((error: unknown) => {
      if (!current()) return;
      setState({ layers: [], captures: [], issues: discoveredIssues, loading: false, error: makeError(error) });
    });

    return () => {
      controller.abort();
      requestGeneration.current += 1;
    };
  }, [backend, backendRevision, context.projectPath, context.projectRevision, context.projectSessionID,
    context.projectXml, document, language]);

  return state;
}
