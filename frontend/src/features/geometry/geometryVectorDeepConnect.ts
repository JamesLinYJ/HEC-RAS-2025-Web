import type { BackendGateway } from '../../shared/workbench';
import { sameSourcePath } from '../tools/attributes/attributeTableTypes';
import type { CatalogTreeNodeLocator } from '../map/mapTypes';
import type {
  GeometryModelRequest,
  GeometryTopologyCoordinate,
  GeometryTopologyDeepConnectAnchor,
  GeometryTopologyDeepConnectSource,
  GeometryTopologyMapCommand,
  GeometryTopologyMapRequestContext,
  GeometryTopologySnapshot,
} from './geometryTypes';
import type { MapAnalysisMapRequest } from '../map/analysis/mapAnalysisTypes';

/** Exact persisted map request; its index signature preserves all MapTreeRequest context fields. */
export type VectorDeepConnectMapRequest = MapAnalysisMapRequest;

export interface VectorSourceFamilyFile {
  inputPath: string;
  path: string;
  role: string;
  required: boolean;
  exists: boolean;
  revision: string | null;
}

export interface VectorSourceFamilyCapture {
  family: 'shapefile' | 'geojson';
  sourcePath: string;
  revision: string;
  files: VectorSourceFamilyFile[];
}

export interface VectorSourceInspectionCandidate {
  node: CatalogTreeNodeLocator;
  expectedFamily: VectorSourceFamilyCapture | null;
}

export interface TopologyVectorSourceInspectRequest {
  requestID: string;
  map: VectorDeepConnectMapRequest;
  sources: VectorSourceInspectionCandidate[];
}

export interface VectorSourceInspectionExpected {
  request: TopologyVectorSourceInspectRequest;
  projectRevision: string;
  expectedVisualizationPath?: string;
}

export interface VectorSourceInspectionOwner {
  requestID: string;
  projectPath: string;
  projectRevision: string;
  projectXmlSHA: string;
  projectSessionID: string;
  mapID: string;
  mapIndex: number;
  mapName: string;
  mapType: string;
  visualizationPath: string;
  visualizationRevision: string;
  coreLayerContextIdentityToken: string | null;
  mapRequest: VectorDeepConnectMapRequest;
  ownerKey: string;
}

export interface PersistedVectorDeepConnectComponent {
  key: string;
  featureFID: number;
  pathIndex: number;
  componentIdentity: string;
  points: GeometryTopologyCoordinate[];
  sourceFeatureOrdinal: number;
  sourcePartOrdinal: number;
  sourcePolygonIndex: number | null;
  sourceIsHole: boolean | null;
}

export interface PersistedVectorDeepConnectSource {
  key: string;
  node: CatalogTreeNodeLocator;
  checked: boolean;
  effectiveVisible: boolean;
  canDeepConnect: boolean;
  sourceKind: 'shapefile' | 'geojson' | 'gdb' | 'unsupported' | null;
  canonicalSourcePath: string | null;
  sourceFamily: VectorSourceFamilyCapture | null;
  sourceSHA256: string | null;
  components: PersistedVectorDeepConnectComponent[];
  unsupportedGeometryTypes: string[];
  oversizedComponentCount: number;
  diagnosticCode: string | null;
  diagnostic: string | null;
  mapRequest: VectorDeepConnectMapRequest;
  owner: VectorSourceInspectionOwner;
}

export interface TopologyVectorSourceInspection {
  requestID: string;
  owner: VectorSourceInspectionOwner;
  sources: PersistedVectorDeepConnectSource[];
}

export interface CreateVectorDeepConnectPreviewInput {
  model: GeometryModelRequest;
  topology: GeometryTopologySnapshot;
  mapRequestContext: GeometryTopologyMapRequestContext;
  mapCellSize: number;
  currentMapRequest: VectorDeepConnectMapRequest;
  currentProjectRevision: string;
  source: PersistedVectorDeepConnectSource;
  component: PersistedVectorDeepConnectComponent;
  anchors: GeometryTopologyDeepConnectAnchor[];
}

export interface PersistedVectorDeepConnectSourceCapture extends GeometryTopologyDeepConnectSource {
  sourceKind: 'vector';
  vectorSource: {
    map: VectorDeepConnectMapRequest;
    node: CatalogTreeNodeLocator;
    expectedFamily: VectorSourceFamilyCapture;
  };
  anchors: GeometryTopologyDeepConnectAnchor[];
}

export type PersistedVectorDeepConnectMapCommand = Omit<
  Extract<GeometryTopologyMapCommand, { action: 'deepConnect' }>,
  'deepConnectSource'
> & {
  deepConnectSource: PersistedVectorDeepConnectSourceCapture;
  /** Host preview hash, required for a confirmed command. */
  expectedPathSHA256?: string;
};

export interface PersistedVectorDeepConnectPreviewRequest {
  model: GeometryModelRequest;
  revision: string;
  draftState: string;
  mutation: {
    operation: 'mapCommand';
    featureKind: 'map';
    featureIDs: [];
    mapCommand: PersistedVectorDeepConnectMapCommand;
  };
  mapRequestContext: GeometryTopologyMapRequestContext;
}

export interface PersistedVectorDeepConnectPreview {
  request: PersistedVectorDeepConnectPreviewRequest;
  command: PersistedVectorDeepConnectMapCommand;
  source: PersistedVectorDeepConnectSource;
  component: PersistedVectorDeepConnectComponent;
  anchors: GeometryTopologyDeepConnectAnchor[];
  ownerKey: string;
  componentKey: string;
  geometryRevision: string;
  draftIdentity: string;
  mapRequestContext: GeometryTopologyMapRequestContext;
}

export interface ConfirmedVectorDeepConnectPreview {
  command: PersistedVectorDeepConnectMapCommand & { expectedPathSHA256: string };
  coordinates: GeometryTopologyCoordinate[];
  pathSHA256: string;
}

const sha256 = /^[0-9a-f]{64}$/i;
const guid = /^(?:[0-9a-f]{32}|[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$/i;
const locatorKeys = [
  'rootName', 'nodeType', 'nodeName', 'nodePath', 'assetID', 'mapType', 'filename', 'filenameRelative',
  'parentGeometryAssetID', 'parentGeometryNodePath', 'parentGeometryNodeName', 'associationAssetID',
  'associationKind', 'parentPlanAssetID', 'parentPlanNodePath', 'parentPlanNodeName', 'parentResultNodeType',
  'parentResultAssetID', 'parentResultNodePath', 'parentResultNodeName', 'parentResultRevision',
] as const;

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function requireRecord(value: unknown, label: string): Record<string, unknown> {
  if (!isRecord(value)) throw new Error(`${label} 无效。`);
  return value;
}

function requireString(value: unknown, label: string): string {
  if (typeof value !== 'string' || !value.trim()) throw new Error(`${label} 缺失。`);
  return value;
}

function requireRevision(value: unknown, label: string): string {
  if (typeof value !== 'string' || !sha256.test(value)) throw new Error(`${label} 不是有效的 SHA-256。`);
  return value;
}

function requireInteger(value: unknown, label: string): number {
  if (!Number.isSafeInteger(value) || (value as number) < 0) throw new Error(`${label} 无效。`);
  return value as number;
}

function requireLocator(value: unknown, label: string): CatalogTreeNodeLocator {
  const record = requireRecord(value, label);
  if (typeof record.rootName !== 'string' || !record.rootName.trim() ||
      typeof record.nodeType !== 'string' || !record.nodeType.trim() ||
      typeof record.nodeName !== 'string' || !record.nodeName.trim()) {
    throw new Error(`${label} 缺少根节点、节点类型或名称。`);
  }
  for (const key of locatorKeys) {
    const item = record[key];
    if (key.endsWith('Path')) {
      if (!(item === undefined || item === null ||
        (Array.isArray(item) && item.every((part) => Number.isSafeInteger(part) && (part as number) >= 0)))) {
        throw new Error(`${label} 的节点路径无效。`);
      }
    } else if (key !== 'rootName' && key !== 'nodeType' && key !== 'nodeName' &&
      !(item === undefined || item === null || typeof item === 'string')) {
      throw new Error(`${label} 包含无效的定位字段 ${key}。`);
    }
  }
  return record as unknown as CatalogTreeNodeLocator;
}

function requireMapRequest(value: unknown): VectorDeepConnectMapRequest {
  const map = requireRecord(value, '地图请求');
  if (typeof map.projectPath !== 'string' || !isAbsolutePath(map.projectPath) ||
      !(map.projectXml === null || typeof map.projectXml === 'string') ||
      !sha256.test(String(map.projectXmlSHA ?? '')) || typeof map.mapID !== 'string' || !map.mapID.trim() ||
      !Number.isSafeInteger(map.mapIndex) || (map.mapIndex as number) < 0 ||
      typeof map.mapName !== 'string' || !map.mapName.trim() || typeof map.mapType !== 'string' || !map.mapType.trim() ||
      !sha256.test(String(map.expectedVisualizationRevision ?? '')) ||
      typeof map.projectSessionID !== 'string' || !map.projectSessionID.trim() ||
      !(map.coreLayerContext === undefined || map.coreLayerContext === null || isRecord(map.coreLayerContext))) {
    throw new Error('矢量来源检查需要完整、已保存的工程与地图请求身份。');
  }
  return map as unknown as VectorDeepConnectMapRequest;
}

function isAbsolutePath(value: string): boolean {
  return value.startsWith('/') || /^[a-z]:[\\/]/i.test(value) || /^\\\\/.test(value);
}

function normalize(value: unknown): unknown {
  if (value === undefined || value === null) return null;
  if (Array.isArray(value)) return value.map(normalize);
  if (isRecord(value)) {
    return Object.fromEntries(Object.keys(value).sort().map((key) => [key, normalize(value[key])]));
  }
  return value;
}

function stableJSON(value: unknown): string {
  return JSON.stringify(normalize(value));
}

/** Stable identity used by the source picker to select one exact persisted map node. */
export function vectorSourceCandidateKey(candidate: VectorSourceInspectionCandidate): string {
  return stableJSON(locatorEchoIdentity(candidate.node));
}

/** Must stay identical to the active component key used by useGeometryVectorSources. */
export function vectorDeepConnectComponentSelectionKey(source: PersistedVectorDeepConnectSource,
  component: PersistedVectorDeepConnectComponent): string {
  return stableJSON([
    source.node,
    source.canonicalSourcePath,
    source.sourceSHA256,
    source.sourceFamily?.revision ?? null,
    component.featureFID,
    component.pathIndex,
    component.componentIdentity,
  ]);
}

function locatorEchoIdentity(value: unknown): Record<string, unknown> {
  const locator = requireLocator(value, 'DeepConnect 回执节点定位');
  return Object.fromEntries(locatorKeys.map((key) => [key, locator[key] ?? null]));
}

function sameLocator(left: CatalogTreeNodeLocator, right: CatalogTreeNodeLocator): boolean {
  return stableJSON(locatorEchoIdentity(left)) === stableJSON(locatorEchoIdentity(right));
}

function coreLayerContextEchoIdentity(value: unknown): unknown {
  if (value === null || value === undefined) return null;
  const context = requireRecord(value, 'DeepConnect 回执核心图层上下文');
  const references = context.references ?? [];
  const sessionReferences = context.sessionHdfReferences ?? [];
  if (!Array.isArray(references) || !Array.isArray(sessionReferences)) {
    throw new Error('DeepConnect 回执核心图层上下文的来源清单无效。');
  }
  return {
    projectSessionID: context.projectSessionID ?? null,
    projectPath: context.projectPath ?? null,
    projectRevision: context.projectRevision ?? null,
    references: references.map((value) => {
      const reference = requireRecord(value, 'DeepConnect 回执核心图层引用');
      return {
        referenceID: reference.referenceID ?? null,
        projectSessionID: reference.projectSessionID ?? null,
        projectPath: reference.projectPath ?? null,
        kind: reference.kind ?? null,
        sourcePath: reference.sourcePath ?? null,
        canonicalSourcePath: reference.canonicalSourcePath ?? null,
        sourceRevision: reference.sourceRevision ?? null,
        name: reference.name ?? null,
        sessionOnly: reference.sessionOnly ?? true,
      };
    }),
    identityToken: context.identityToken ?? null,
    sessionHdfReferences: sessionReferences.map((value) => {
      const reference = requireRecord(value, 'DeepConnect 回执会话 HDF 引用');
      return {
        referenceID: reference.referenceID ?? null,
        projectSessionID: reference.projectSessionID ?? null,
        projectPath: reference.projectPath ?? null,
        kind: reference.kind ?? null,
        sourcePath: reference.sourcePath ?? null,
        canonicalSourcePath: reference.canonicalSourcePath ?? null,
        sourceRevision: reference.sourceRevision ?? null,
        name: reference.name ?? null,
        resultPlanName: reference.resultPlanName ?? null,
        sessionOnly: reference.sessionOnly ?? true,
      };
    }),
  };
}

function mapRequestEchoIdentity(value: unknown): unknown {
  const map = requireRecord(value, 'DeepConnect 回执地图请求');
  const overrides = map.resultSelectionOverrides;
  const externalReferences = map.externalCoreLayerReferences;
  if (!(overrides === null || overrides === undefined || Array.isArray(overrides)) ||
      !(externalReferences === null || externalReferences === undefined || Array.isArray(externalReferences))) {
    throw new Error('DeepConnect 回执地图请求的来源上下文无效。');
  }
  return {
    projectPath: map.projectPath ?? null,
    projectXml: map.projectXml ?? null,
    projectXmlSHA: map.projectXmlSHA ?? null,
    mapID: map.mapID ?? null,
    mapIndex: map.mapIndex ?? null,
    mapName: map.mapName ?? null,
    mapType: map.mapType ?? null,
    expectedVisualizationRevision: map.expectedVisualizationRevision ?? null,
    resultSelectionOverrides: overrides == null ? null : overrides.map((value) => {
      const override = requireRecord(value, 'DeepConnect 回执地图结果选择');
      const resultOwner = override.sessionHdfResultOwner;
      const resultOwnerIdentity = resultOwner == null ? null : (() => {
        const owner = requireRecord(resultOwner, 'DeepConnect 回执会话结果 owner');
        return {
          referenceID: owner.referenceID ?? null,
          projectSessionID: owner.projectSessionID ?? null,
          projectPath: owner.projectPath ?? null,
          resultPath: owner.resultPath ?? null,
          resultRevision: owner.resultRevision ?? null,
          embeddedPlanName: owner.embeddedPlanName ?? null,
          coreLayerContextIdentityToken: owner.coreLayerContextIdentityToken ?? null,
        };
      })();
      return {
        planAssetID: override.planAssetID ?? null,
        planName: override.planName ?? null,
        selectedResultPath: override.selectedResultPath ?? null,
        selectedResultRevision: override.selectedResultRevision ?? null,
        sessionHdfResultOwner: resultOwnerIdentity,
        followLatest: override.followLatest ?? false,
      };
    }),
    computeReadLease: map.computeReadLease == null ? null : (() => {
      const lease = requireRecord(map.computeReadLease, 'DeepConnect 回执计算读取租约');
      return {
        projectSessionID: lease.projectSessionID ?? null,
        runID: lease.runID ?? null,
        projectPath: lease.projectPath ?? null,
        protectedSourcePaths: lease.protectedSourcePaths ?? null,
        callbackResultPath: lease.callbackResultPath ?? null,
      };
    })(),
    projectSessionID: map.projectSessionID ?? null,
    externalCoreLayerReferences: externalReferences == null ? null : externalReferences.map((value) => {
      const reference = requireRecord(value, 'DeepConnect 回执外部核心图层引用');
      return {
        referenceID: reference.referenceID ?? null,
        projectSessionID: reference.projectSessionID ?? null,
        projectPath: reference.projectPath ?? null,
        kind: reference.kind ?? null,
        sourcePath: reference.sourcePath ?? null,
        canonicalSourcePath: reference.canonicalSourcePath ?? null,
        sourceRevision: reference.sourceRevision ?? null,
        name: reference.name ?? null,
        sessionOnly: reference.sessionOnly ?? true,
      };
    }),
    coreLayerContext: coreLayerContextEchoIdentity(map.coreLayerContext),
  };
}

export function deepConnectSourceEchoIdentity(value: unknown): unknown {
  const source = requireRecord(value, 'DeepConnect 回执来源');
  const topologySource = source.topologySource == null ? null : (() => {
    const topology = requireRecord(source.topologySource, 'DeepConnect 回执拓扑来源');
    const model = requireRecord(topology.model, 'DeepConnect 回执拓扑模型');
    return {
      ...topology,
      model: {
        ...model,
        projectXml: model.projectXml ?? null,
        coreLayerContext: coreLayerContextEchoIdentity(model.coreLayerContext),
      },
    };
  })();
  const vectorSource = source.vectorSource == null ? null : (() => {
    const vector = requireRecord(source.vectorSource, 'DeepConnect 回执矢量来源');
    return {
      ...vector,
      map: mapRequestEchoIdentity(vector.map),
      node: locatorEchoIdentity(vector.node),
    };
  })();
  return { ...source, topologySource, vectorSource };
}

function sameRevision(left: string | null | undefined, right: string | null | undefined): boolean {
  return typeof left === 'string' && typeof right === 'string' && left.toUpperCase() === right.toUpperCase();
}

function sameFamily(left: VectorSourceFamilyCapture, right: VectorSourceFamilyCapture): boolean {
  if (left.family !== right.family || !sameSourcePath(left.sourcePath, right.sourcePath) ||
      !sameRevision(left.revision, right.revision) || left.files.length !== right.files.length) return false;
  const rightFiles = new Map(right.files.map((file) => [file.role, file]));
  return left.files.every((file) => {
    const next = rightFiles.get(file.role);
    return !!next && sameSourcePath(file.inputPath, next.inputPath) && sameSourcePath(file.path, next.path) &&
      file.required === next.required && file.exists === next.exists && sameRevision(file.revision, next.revision);
  });
}

function parseFamily(value: unknown): VectorSourceFamilyCapture {
  const family = requireRecord(value, 'Host 来源文件组');
  if ((family.family !== 'shapefile' && family.family !== 'geojson') ||
      typeof family.sourcePath !== 'string' || !isAbsolutePath(family.sourcePath) ||
      !sha256.test(String(family.revision ?? '')) || !Array.isArray(family.files)) {
    throw new Error('Host 来源文件组没有有效格式、路径、版本或文件清单。');
  }
  const files = family.files.map((item, index): VectorSourceFamilyFile => {
    const file = requireRecord(item, `Host 来源文件 ${index + 1}`);
    if (typeof file.inputPath !== 'string' || !file.inputPath || typeof file.path !== 'string' || !isAbsolutePath(file.path) ||
        typeof file.role !== 'string' || !file.role || typeof file.required !== 'boolean' ||
        typeof file.exists !== 'boolean' || !(file.revision === null || sha256.test(String(file.revision)))) {
      throw new Error(`Host 来源文件 ${index + 1} 身份无效。`);
    }
    if (file.required && (!file.exists || typeof file.revision !== 'string' || !sha256.test(file.revision))) {
      throw new Error(`Host 必需来源文件 ${file.role} 缺失或版本无效。`);
    }
    return {
      inputPath: file.inputPath,
      path: file.path,
      role: file.role,
      required: file.required,
      exists: file.exists,
      revision: file.revision as string | null,
    };
  });
  if (new Set(files.map((file) => file.role)).size !== files.length) {
    throw new Error('Host 来源文件组包含重复文件角色。');
  }
  const primary = files.filter((file) => file.role === 'source' && file.required);
  if (primary.length !== 1 || !primary[0] || !sameSourcePath(primary[0].path, family.sourcePath)) {
    throw new Error('Host 来源文件组缺少唯一且匹配的主文件。');
  }
  if (family.family === 'shapefile') {
    for (const role of ['shx', 'dbf']) {
      if (!files.some((file) => file.role === role && file.required && file.exists && file.revision)) {
        throw new Error(`Host Shapefile 文件组缺少必需的 ${role.toUpperCase()} 文件。`);
      }
    }
  } else if (files.length !== 1) {
    throw new Error('Host GeoJSON 来源必须只包含一个主文件。');
  }
  return {
    family: family.family,
    sourcePath: family.sourcePath,
    revision: family.revision as string,
    files,
  };
}

function coreIdentityToken(map: VectorDeepConnectMapRequest): string | null {
  const value = map.coreLayerContext?.identityToken ?? map.coreLayerContext?.IdentityToken;
  return typeof value === 'string' && value.length > 0 ? value : null;
}

function ownerKey(owner: Omit<VectorSourceInspectionOwner, 'ownerKey'>): string {
  return stableJSON([
    owner.requestID, owner.projectPath, owner.projectRevision, owner.projectXmlSHA, owner.projectSessionID,
    owner.mapID, owner.mapIndex, owner.mapName, owner.mapType, owner.visualizationPath,
    owner.visualizationRevision, owner.coreLayerContextIdentityToken, owner.mapRequest,
  ]);
}

function ownerIdentity(owner: VectorSourceInspectionOwner): Omit<VectorSourceInspectionOwner, 'ownerKey'> {
  const { ownerKey: _ownerKey, ...identity } = owner;
  return identity;
}

function parseOwner(raw: Record<string, unknown>, responseRequestID: string,
  expected: VectorSourceInspectionExpected): VectorSourceInspectionOwner {
  const { request } = expected;
  const map = request.map;
  const requestID = requireString(responseRequestID, '来源检查请求标识');
  const projectPath = requireString(raw.projectPath, '回执工程路径');
  const projectRevision = requireRevision(raw.projectRevision, '回执工程修订');
  const projectXmlSHA = requireRevision(raw.projectXmlSHA, '回执工程 XML SHA-256');
  const projectSessionID = requireString(raw.projectSessionID, '回执工程会话');
  const mapID = requireString(raw.mapID, '回执地图 ID');
  const mapIndex = requireInteger(raw.mapIndex, '回执地图序号');
  const mapName = requireString(raw.mapName, '回执地图名称');
  const mapType = requireString(raw.mapType, '回执地图类型');
  const visualizationPath = requireString(raw.visualizationPath, '回执 .vis 路径');
  const visualizationRevision = requireRevision(raw.visualizationRevision, '回执 .vis 修订');
  const tokenValue = raw.coreLayerContextIdentityToken;
  if (!(tokenValue === null || tokenValue === undefined || typeof tokenValue === 'string')) {
    throw new Error('回执核心图层会话身份无效。');
  }
  const token = typeof tokenValue === 'string' ? tokenValue : null;
  const expectedToken = coreIdentityToken(map);
  if (requestID !== request.requestID || !sameSourcePath(projectPath, map.projectPath) ||
      !sameRevision(projectRevision, expected.projectRevision) || !sameRevision(projectXmlSHA, map.projectXmlSHA) ||
      projectSessionID !== map.projectSessionID || mapID !== map.mapID || mapIndex !== map.mapIndex ||
      mapName !== map.mapName || mapType !== map.mapType ||
      !sameRevision(visualizationRevision, map.expectedVisualizationRevision) ||
      (expected.expectedVisualizationPath && !sameSourcePath(visualizationPath, expected.expectedVisualizationPath)) ||
      token !== expectedToken || !isAbsolutePath(visualizationPath)) {
    throw new Error('矢量来源回执与当前工程、会话、地图、.vis 修订或核心图层身份不匹配。');
  }
  const partial = {
    requestID,
    projectPath,
    projectRevision,
    projectXmlSHA,
    projectSessionID,
    mapID,
    mapIndex,
    mapName,
    mapType,
    visualizationPath,
    visualizationRevision,
    coreLayerContextIdentityToken: token,
    mapRequest: map,
  };
  return { ...partial, ownerKey: ownerKey(partial) };
}

function parseComponent(value: unknown, sourceRevision: string, sourceKey: string, index: number): PersistedVectorDeepConnectComponent {
  const component = requireRecord(value, `矢量组成线 ${index + 1}`);
  const featureFID = requireInteger(component.featureFID, 'Host 线要素 FID');
  const pathIndex = requireInteger(component.pathIndex, 'Host 线部件序号');
  const componentIdentity = requireString(component.componentIdentity, 'Host 线部件身份');
  if (componentIdentity !== `feature:${featureFID}:part:${pathIndex}` || !Array.isArray(component.points) ||
      component.points.length < 2 || component.points.length > 100_000) {
    throw new Error('Host 线部件身份或工程坐标路径不符合持久化矢量契约。');
  }
  const points = component.points.map((value, pointIndex): GeometryTopologyCoordinate => {
    const point = requireRecord(value, `Host 线部件点 ${pointIndex + 1}`);
    if (typeof point.x !== 'number' || !Number.isFinite(point.x) ||
        typeof point.y !== 'number' || !Number.isFinite(point.y)) {
      throw new Error('Host 矢量来源包含无效的工程坐标。');
    }
    return { x: point.x, y: point.y };
  });
  if (!points.some((point, pointIndex) => pointIndex > 0 &&
    (point.x !== points[pointIndex - 1]?.x || point.y !== points[pointIndex - 1]?.y))) {
    throw new Error('Host 矢量线部件没有非零线段。');
  }
  const sourceFeatureOrdinal = requireInteger(component.sourceFeatureOrdinal, '源要素序号');
  const sourcePartOrdinal = requireInteger(component.sourcePartOrdinal, '源部件序号');
  const polygonValue = component.sourcePolygonIndex;
  const holeValue = component.sourceIsHole;
  if (!(polygonValue === null || polygonValue === undefined ||
      (Number.isSafeInteger(polygonValue) && (polygonValue as number) >= 0)) ||
      !(holeValue === null || holeValue === undefined || typeof holeValue === 'boolean')) {
    throw new Error('Host 多边形来源部件分组元数据无效。');
  }
  return {
    key: stableJSON([sourceKey, sourceRevision, featureFID, pathIndex, componentIdentity]),
    featureFID,
    pathIndex,
    componentIdentity,
    points,
    sourceFeatureOrdinal,
    sourcePartOrdinal,
    sourcePolygonIndex: (polygonValue ?? null) as number | null,
    sourceIsHole: (holeValue ?? null) as boolean | null,
  };
}

function validateExpectedMapRequest(request: TopologyVectorSourceInspectRequest): void {
  if (typeof request.requestID !== 'string' || !guid.test(request.requestID)) {
    throw new Error('矢量来源检查需要有效的 Guid 请求标识。');
  }
  requireMapRequest(request.map);
  if (!Array.isArray(request.sources) || request.sources.length === 0 || request.sources.length > 128) {
    throw new Error('来源检查需包含 1 至 128 个当前地图中的精确矢量图层。');
  }
  const locators = request.sources.map((source, index) => {
    const node = requireLocator(source.node, `来源节点 ${index + 1}`);
    if (source.expectedFamily !== null) parseFamily(source.expectedFamily);
    return stableJSON(locatorEchoIdentity(node));
  });
  if (new Set(locators).size !== locators.length) throw new Error('矢量来源检查不能重复提交同一地图节点。');
}

/** Build the exact Host request. The caller owns AbortController and generation checks. */
export function createTopologyVectorSourceInspectRequest(input: TopologyVectorSourceInspectRequest): TopologyVectorSourceInspectRequest {
  validateExpectedMapRequest(input);
  return {
    requestID: input.requestID,
    map: { ...input.map },
    sources: input.sources.map((source) => ({
      node: { ...source.node },
      expectedFamily: source.expectedFamily ? {
        ...source.expectedFamily,
        files: source.expectedFamily.files.map((file) => ({ ...file })),
      } : null,
    })),
  };
}

/** Validate the complete Host response; never derive renderer FIDs in React. */
export function parseTopologyVectorSourceInspection(
  value: unknown,
  expected: VectorSourceInspectionExpected,
): TopologyVectorSourceInspection {
  validateExpectedMapRequest(expected.request);
  if (!sha256.test(expected.projectRevision)) throw new Error('无法验证当前工程修订。');
  const response = requireRecord(value, 'Host 矢量来源检查回执');
  const responseRequestID = requireString(response.requestID, 'Host 来源检查请求标识');
  const owner = parseOwner(requireRecord(response.owner, 'Host 地图来源检查 owner'), responseRequestID, expected);
  if (!Array.isArray(response.sources) || response.sources.length !== expected.request.sources.length) {
    throw new Error('Host 矢量来源回执数量与请求的地图节点不一致。');
  }
  const seenNodes = new Set<string>();
  let totalComponents = 0;
  let totalPoints = 0;
  const sources = response.sources.map((value, index): PersistedVectorDeepConnectSource => {
    const item = requireRecord(value, `矢量来源 ${index + 1}`);
    const node = requireLocator(item.node, `矢量来源 ${index + 1} locator`);
    const requestCandidate = expected.request.sources.find((candidate) => sameLocator(candidate.node, node));
    const locatorKey = stableJSON(locatorEchoIdentity(node));
    if (!requestCandidate || seenNodes.has(locatorKey)) {
      throw new Error('Host 矢量来源回执包含未请求或重复的地图节点。');
    }
    seenNodes.add(locatorKey);
    if (typeof item.checked !== 'boolean' || typeof item.effectiveVisible !== 'boolean' ||
        typeof item.canDeepConnect !== 'boolean' ||
        !(item.sourceKind === null || item.sourceKind === undefined ||
          ['shapefile', 'geojson', 'gdb', 'unsupported'].includes(String(item.sourceKind))) ||
        !(item.canonicalSourcePath === null || item.canonicalSourcePath === undefined || typeof item.canonicalSourcePath === 'string') ||
        !(item.sourceSHA256 === null || item.sourceSHA256 === undefined || sha256.test(String(item.sourceSHA256))) ||
        !Array.isArray(item.components) || !Array.isArray(item.unsupportedGeometryTypes) ||
        !item.unsupportedGeometryTypes.every((kind) => typeof kind === 'string') ||
        !Number.isSafeInteger(item.oversizedComponentCount) || (item.oversizedComponentCount as number) < 0 ||
        !(item.diagnosticCode === null || item.diagnosticCode === undefined || typeof item.diagnosticCode === 'string') ||
        !(item.diagnostic === null || item.diagnostic === undefined || typeof item.diagnostic === 'string')) {
      throw new Error(`Host 矢量来源 ${node.nodeName} 的状态或诊断字段无效。`);
    }
    const sourceFamily = item.sourceFamily === null || item.sourceFamily === undefined ? null : parseFamily(item.sourceFamily);
    const canonicalSourcePath = (item.canonicalSourcePath ?? null) as string | null;
    const sourceSHA256 = (item.sourceSHA256 ?? null) as string | null;
    const isSupportedFamily = sourceFamily?.family === 'shapefile' || sourceFamily?.family === 'geojson';
    if (sourceFamily && (!canonicalSourcePath || !sameSourcePath(canonicalSourcePath, sourceFamily.sourcePath))) {
      throw new Error(`Host 矢量来源 ${node.nodeName} 的路径与 source family 不匹配。`);
    }
    if (sourceFamily) {
      const primary = sourceFamily.files.filter((file) => file.role === 'source' && file.required);
      if (primary.length !== 1 || !primary[0]?.revision ||
          !sameRevision(sourceSHA256, primary[0].revision)) {
        throw new Error(`Host 矢量来源 ${node.nodeName} 的 sourceSHA256 不匹配主文件 revision。`);
      }
      if (requestCandidate.expectedFamily && !sameFamily(sourceFamily, requestCandidate.expectedFamily)) {
        throw new Error(`Host 矢量来源 ${node.nodeName} 的文件组与先前捕获不一致。`);
      }
    } else if (sourceSHA256 !== null ||
        (canonicalSourcePath !== null && !isAbsolutePath(canonicalSourcePath))) {
      throw new Error(`Host 矢量来源 ${node.nodeName} 缺少文件组但仍返回了无效路径或 SHA。`);
    }

    const sourceKey = stableJSON([owner.ownerKey, node, canonicalSourcePath, sourceSHA256,
      sourceFamily?.revision ?? null]);
    if (sourceFamily === null && item.components.length !== 0) {
      throw new Error(`Host 矢量来源 ${node.nodeName} 未提供经验证的文件组，却返回了可连接组件。`);
    }
    const components = item.components.map((component, componentIndex) =>
      parseComponent(component, sourceSHA256 ?? '', sourceKey, componentIndex));
    totalComponents += components.length;
    totalPoints += components.reduce((count, component) => count + component.points.length, 0);
    if (totalComponents > 100_000 || totalPoints > 1_000_000) {
      throw new Error('Host 矢量来源回执超过组件或坐标点上限。');
    }
    if (new Set(components.map((component) => component.key)).size !== components.length) {
      throw new Error(`Host 矢量来源 ${node.nodeName} 返回了重复的组件身份。`);
    }
    const canDeepConnect = item.canDeepConnect;
    const sourceKind = (item.sourceKind ?? null) as PersistedVectorDeepConnectSource['sourceKind'];
    if (sourceFamily && sourceKind !== sourceFamily.family) {
      throw new Error(`Host 矢量来源 ${node.nodeName} 的 sourceKind 与文件组不一致。`);
    }
    if (!canDeepConnect && components.length > 0) {
      throw new Error(`Host 将不可连接的来源 ${node.nodeName} 返回了可选组件。`);
    }
    if (canDeepConnect && (!item.checked || !item.effectiveVisible || !isSupportedFamily ||
        (node.mapType !== 'PolylinesVector' && node.mapType !== 'PolygonsVector') ||
        components.length === 0 || sourceFamily === null || sourceSHA256 === null || !canonicalSourcePath ||
      sourceKind === 'gdb' || sourceKind === 'unsupported' || sourceKind === null)) {
      throw new Error(`Host 将不可见、不支持或不完整的来源 ${node.nodeName} 标记为可连接。`);
    }
    if (canDeepConnect && components.some((component) =>
      component.componentIdentity !== `feature:${component.featureFID}:part:${component.pathIndex}`)) {
      throw new Error(`Host 矢量来源 ${node.nodeName} 的组件 FID 与 path identity 不一致。`);
    }
    return {
      key: sourceKey,
      node,
      checked: item.checked,
      effectiveVisible: item.effectiveVisible,
      canDeepConnect,
      sourceKind,
      canonicalSourcePath,
      sourceFamily,
      sourceSHA256,
      components,
      unsupportedGeometryTypes: item.unsupportedGeometryTypes as string[],
      oversizedComponentCount: item.oversizedComponentCount as number,
      diagnosticCode: item.diagnosticCode as string | null,
      diagnostic: item.diagnostic as string | null,
      mapRequest: owner.mapRequest,
      owner,
    };
  });
  if (seenNodes.size !== expected.request.sources.length) {
    throw new Error('Host 矢量来源回执遗漏了一个或多个请求节点。');
  }
  return { requestID: owner.requestID, owner, sources };
}

export async function inspectTopologyVectorSources(
  backend: BackendGateway,
  request: TopologyVectorSourceInspectRequest,
  expectedProjectRevision: string,
  signal: AbortSignal,
  expectedVisualizationPath?: string,
): Promise<TopologyVectorSourceInspection> {
  const stableRequest = createTopologyVectorSourceInspectRequest(request);
  if (!backend.supports('topology.vectorSource.inspect')) {
    throw new Error('当前 C# 服务未发布 topology.vectorSource.inspect。');
  }
  const result = await backend.request<unknown>('topology.vectorSource.inspect', stableRequest, { signal });
  if (signal.aborted) throw new DOMException('矢量来源检查已取消。', 'AbortError');
  return parseTopologyVectorSourceInspection(result, {
    request: stableRequest,
    projectRevision: expectedProjectRevision,
    expectedVisualizationPath,
  });
}

function validateTopologyCapture(topology: GeometryTopologySnapshot, model: GeometryModelRequest,
  mapRequestContext: GeometryTopologyMapRequestContext, currentMapRequest: VectorDeepConnectMapRequest,
  currentProjectRevision: string): string {
  const draftIdentity = topology.completion?.draftIdentity;
  if (!topology.canEdit || !topology.draftState || !sha256.test(topology.revision) ||
      topology.completion?.sourceRevision !== topology.revision || !draftIdentity || !/^[0-9a-f]{64}$/.test(draftIdentity)) {
    throw new Error('当前 Geometry 修订或拓扑草稿身份已失效。');
  }
  const map = requireMapRequest(currentMapRequest);
  if (!sha256.test(currentProjectRevision) || !sameSourcePath(model.projectPath ?? '', map.projectPath) ||
      model.projectXml !== map.projectXml ||
      stableJSON(model.coreLayerContext ?? null) !== stableJSON(map.coreLayerContext ?? null) ||
      mapRequestContext.mapID !== map.mapID || mapRequestContext.geometryRevision !== topology.revision ||
      mapRequestContext.expectedDraftIdentity !== draftIdentity || !guid.test(mapRequestContext.requestID) ||
      typeof mapRequestContext.contextToken !== 'string' || !mapRequestContext.contextToken.trim() ||
      currentProjectRevision.length !== 64) {
    throw new Error('当前 Geometry 草稿、工程或保存地图身份已变化。');
  }
  return draftIdentity;
}

function validateSourceCurrent(source: PersistedVectorDeepConnectSource,
  currentMapRequest: VectorDeepConnectMapRequest, currentProjectRevision: string): void {
  const ownerIsCurrent = source.owner.ownerKey === ownerKey(ownerIdentity(source.owner));
  if (!source.canDeepConnect || !source.checked || !source.effectiveVisible || !source.sourceFamily ||
      !source.sourceSHA256 || !source.canonicalSourcePath ||
      !ownerIsCurrent ||
      !sameRevision(source.owner.projectRevision, currentProjectRevision) ||
      stableJSON(source.mapRequest) !== stableJSON(currentMapRequest) ||
      !sameSourcePath(source.canonicalSourcePath, source.sourceFamily.sourcePath) ||
      !sameRevision(source.sourceSHA256, source.sourceFamily.files.find((file) =>
        file.role === 'source' && file.required)?.revision)) {
    throw new Error('所选矢量来源已过期、不可见、未勾选或与当前工程地图不匹配。');
  }
}

function anchorCoordinate(points: readonly GeometryTopologyCoordinate[], anchor: GeometryTopologyDeepConnectAnchor,
  index: number): GeometryTopologyCoordinate {
  if (!Number.isSafeInteger(anchor.segmentIndex) || anchor.segmentIndex < 0 ||
      anchor.segmentIndex >= points.length - 1 || !Number.isFinite(anchor.fraction) ||
      anchor.fraction < 0 || anchor.fraction > 1) {
    throw new Error(`第 ${index + 1} 个 DeepConnect 锚点不属于当前 Host 组件。`);
  }
  const first = points[anchor.segmentIndex];
  const second = points[anchor.segmentIndex + 1];
  if (!first || !second || (first.x === second.x && first.y === second.y)) {
    throw new Error(`第 ${index + 1} 个 DeepConnect 锚点位于无效线段。`);
  }
  const point = {
    x: first.x + (second.x - first.x) * anchor.fraction,
    y: first.y + (second.y - first.y) * anchor.fraction,
  };
  if (!Number.isFinite(point.x) || !Number.isFinite(point.y)) throw new Error(`第 ${index + 1} 个锚点坐标无效。`);
  return point;
}

function sameAnchor(left: GeometryTopologyDeepConnectAnchor, right: GeometryTopologyDeepConnectAnchor): boolean {
  return left.segmentIndex === right.segmentIndex && left.fraction === right.fraction;
}

function uniqueTopologyNodeAt(topology: GeometryTopologySnapshot, point: GeometryTopologyCoordinate): number | null {
  const rows = topology.nodes.rows.filter((row) => row.fid !== undefined && row.fid !== null &&
    Number.isSafeInteger(row.fid) && row.fid >= 0 && row.x === point.x && row.y === point.y);
  if (rows.length !== 1) return null;
  const nodeFID = rows[0]?.fid;
  if (nodeFID === undefined || nodeFID === null) return null;
  const features = topology.map?.features.filter((feature) => feature.featureKind === 'node' && feature.fid === nodeFID) ?? [];
  if (features.length !== 1) return null;
  const feature = features[0];
  const nodePath = feature?.paths.length === 1 ? feature.paths[0] : undefined;
  const nodePoint = nodePath?.length === 1 ? nodePath[0] : undefined;
  return feature?.geometryType === 'point' && nodePoint?.x === point.x && nodePoint.y === point.y ? nodeFID : null;
}

function componentBelongsToSource(source: PersistedVectorDeepConnectSource,
  component: PersistedVectorDeepConnectComponent): boolean {
  return source.canDeepConnect && source.components.some((candidate) => candidate.key === component.key &&
    candidate.featureFID === component.featureFID && candidate.pathIndex === component.pathIndex &&
    candidate.componentIdentity === component.componentIdentity && stableJSON(candidate.points) === stableJSON(component.points));
}

/** Prepare a read-only preview using the current Geometry edit envelope. */
export function createPersistedVectorDeepConnectPreview({
  model,
  topology,
  mapRequestContext,
  mapCellSize,
  currentMapRequest,
  currentProjectRevision,
  source,
  component,
  anchors,
}: CreateVectorDeepConnectPreviewInput): PersistedVectorDeepConnectPreview {
  const draftIdentity = validateTopologyCapture(topology, model, mapRequestContext, currentMapRequest, currentProjectRevision);
  validateSourceCurrent(source, currentMapRequest, currentProjectRevision);
  const sourcePath = source.canonicalSourcePath;
  const sourceRevision = source.sourceSHA256;
  const expectedFamily = source.sourceFamily;
  if (!sourcePath || !sourceRevision || !expectedFamily) {
    throw new Error('所选矢量来源没有完整的 Host 文件组捕获。');
  }
  if (!componentBelongsToSource(source, component)) throw new Error('矢量组件不属于当前已验证来源。');
  if (!Number.isFinite(mapCellSize) || mapCellSize <= 0) throw new Error('当前地图比例无效。');
  if (!Array.isArray(anchors) || anchors.length < 2 || anchors.length > 512) {
    throw new Error('DeepConnect 需要同一来源组件上至少两个、最多 512 个锚点。');
  }
  const copiedAnchors = anchors.map((anchor, index) => {
    if (!anchor || !Number.isSafeInteger(anchor.segmentIndex) || !Number.isFinite(anchor.fraction)) {
      throw new Error(`第 ${index + 1} 个 DeepConnect 锚点结构无效。`);
    }
    return { segmentIndex: anchor.segmentIndex, fraction: anchor.fraction };
  });
  const points = copiedAnchors.map((anchor, index) => anchorCoordinate(component.points, anchor, index));
  if (copiedAnchors.some((anchor, index) => index > 0 && sameAnchor(anchor, copiedAnchors[index - 1]!))) {
    throw new Error('相邻 DeepConnect 锚点不能重复。');
  }
  if (points.some((point, index) => index > 0 && point.x === points[index - 1]?.x && point.y === points[index - 1]?.y)) {
    throw new Error('相邻 DeepConnect 锚点不能重合。');
  }
  const firstAnchor = copiedAnchors[0];
  const lastAnchor = copiedAnchors[copiedAnchors.length - 1];
  const firstPoint = points[0];
  if (!firstAnchor || !lastAnchor || !firstPoint) throw new Error('DeepConnect 锚点序列不完整。');

  const deepConnectSource: PersistedVectorDeepConnectSourceCapture = {
    sourceKind: 'vector',
    sourceLocator: sourcePath,
    sourceRevision,
    featureFID: component.featureFID,
    pathIndex: component.pathIndex,
    componentIdentity: component.componentIdentity,
    points: component.points.map((point) => ({ ...point })),
    startSegmentIndex: firstAnchor.segmentIndex,
    startSegmentFraction: firstAnchor.fraction,
    endSegmentIndex: lastAnchor.segmentIndex,
    endSegmentFraction: lastAnchor.fraction,
    anchors: copiedAnchors,
    vectorSource: {
      map: source.mapRequest,
      node: source.node,
      expectedFamily,
    },
  };
  const startNodeFID = uniqueTopologyNodeAt(topology, firstPoint);
  const command: PersistedVectorDeepConnectMapCommand = {
    action: 'deepConnect',
    featureKind: 'arc',
    featureIDs: [],
    coordinates: null,
    mapCellSize,
    deepConnectSource,
    ...(startNodeFID !== null ? { startNodeFID } : {}),
  };
  const request: PersistedVectorDeepConnectPreviewRequest = {
    model,
    revision: topology.revision,
    draftState: topology.draftState,
    mutation: { operation: 'mapCommand', featureKind: 'map', featureIDs: [], mapCommand: command },
    mapRequestContext,
  };
  return {
    request,
    command,
    source,
    component,
    anchors: copiedAnchors,
    ownerKey: source.key,
    componentKey: component.key,
    geometryRevision: topology.revision,
    draftIdentity,
    mapRequestContext,
  };
}

export async function previewPersistedVectorDeepConnect(
  backend: BackendGateway,
  preview: PersistedVectorDeepConnectPreview,
  signal: AbortSignal,
): Promise<ConfirmedVectorDeepConnectPreview> {
  if (!backend.supports('topology.mapCommand.preview')) {
    throw new Error('当前 C# 服务未发布 topology.mapCommand.preview，尚不能安全确认矢量连接路径。');
  }
  const raw = await backend.request<unknown>('topology.mapCommand.preview', preview.request, { signal });
  if (signal.aborted) throw new DOMException('DeepConnect 路径预览已取消。', 'AbortError');
  return parseVectorDeepConnectPreview(raw, preview);
}

/** Validate the Host preview echo and return a command guarded by its exact path hash. */
export function parseVectorDeepConnectPreview(
  value: unknown,
  expected: PersistedVectorDeepConnectPreview,
): ConfirmedVectorDeepConnectPreview {
  const response = requireRecord(value, 'Host DeepConnect 预览回执');
  const requestContext = expected.mapRequestContext;
  const requestID = requireString(response.requestID, '预览请求 ID');
  const mapID = requireString(response.mapID, '预览地图 ID');
  const contextToken = requireString(response.contextToken, '预览地图上下文');
  const geometryRevision = requireRevision(response.geometryRevision, '预览 Geometry 修订');
  const revision = requireRevision(response.revision, '预览来源修订');
  const draftIdentity = requireRevision(response.draftIdentity, '预览草稿身份');
  const action = requireString(response.action, '预览操作');
  const pathSHA256 = requireRevision(response.pathSHA256, '预览路径 SHA-256');
  const sourceRevision = requireRevision(response.sourceRevision, '预览矢量来源 SHA-256');
  if (requestID !== requestContext.requestID || mapID !== requestContext.mapID || contextToken !== requestContext.contextToken ||
      !sameRevision(geometryRevision, expected.geometryRevision) || !sameRevision(revision, expected.geometryRevision) ||
      !sameRevision(draftIdentity, expected.draftIdentity) || action !== 'deepConnect' ||
      !sameRevision(sourceRevision, expected.source.sourceSHA256)) {
    throw new Error('Host DeepConnect 预览回执与当前地图、工程草稿、来源或操作不匹配。');
  }
  if (!Array.isArray(response.coordinates) || response.coordinates.length < 2) {
    throw new Error('Host DeepConnect 预览没有返回完整的工程坐标路径。');
  }
  const coordinates = response.coordinates.map((value, index): GeometryTopologyCoordinate => {
    const point = requireRecord(value, `预览路径点 ${index + 1}`);
    if (typeof point.x !== 'number' || !Number.isFinite(point.x) ||
        typeof point.y !== 'number' || !Number.isFinite(point.y)) {
      throw new Error('Host DeepConnect 预览包含无效坐标。');
    }
    return { x: point.x, y: point.y };
  });
  if (!isRecord(response.deepConnectSource) ||
      stableJSON(deepConnectSourceEchoIdentity(response.deepConnectSource)) !==
        stableJSON(deepConnectSourceEchoIdentity(expected.command.deepConnectSource))) {
    throw new Error('Host DeepConnect 预览没有回显完全相同的矢量来源、组件、锚点和文件组。');
  }
  return {
    command: { ...expected.command, expectedPathSHA256: pathSHA256 },
    coordinates,
    pathSHA256,
  };
}
