import { parseGeometry, positionsInGeometry } from '../geoJson';
import { projectXmlSha } from '../ProjectTreeListener';
import type { GeoGeometry, MapDocumentTab } from '../mapTypes';
import type { BackendGateway, WorkbenchContext } from '../../../shared/workbench';
import type { FeatureQueryExportedSource, FeatureQueryExtent, FeatureQueryFormValue, FeatureQueryKind,
  FeatureQueryMapCapture, FeatureQueryPolygonSource, FeatureQuerySourceFamily } from './featureQueryTypes';

export const featureQueryMethods = {
  snapshot: 'featureQuery.snapshot', query: 'featureQuery.query', preview: 'featureQuery.preview',
  save: 'featureQuery.save', discard: 'featureQuery.discard',
} as const;
export type FeatureQueryFormat = 'GeoJson' | 'Shapefile';
export interface FeatureQueryMapRequest {
  projectPath: string; projectXml: string | null; projectXmlSHA: string | null;
  mapID: string; mapIndex: number; mapName: string; mapType: string;
  expectedVisualizationRevision: string | null; projectSessionID: string;
  coreLayerContext: WorkbenchContext['coreLayerContext'];
}
export interface FeatureQueryOwner {
  backend: BackendGateway; backendRevision: number; context: WorkbenchContext;
  document: MapDocumentTab; map: FeatureQueryMapRequest;
}
export interface FeatureQuerySnapshot { map: FeatureQueryMapCapture; polygonSources: FeatureQueryPolygonSource[]; selectedPolygonId: string | null }
export interface FeatureQueryLayer { name: string; sourcePath: string; sourceRevision: string; mapType: string;
  lineColor: string; fillColor: string; lineWidth: number; coordinateSpace: 'ProjectWorld' }
export interface FeatureQueryResult { map: FeatureQueryMapCapture; kind: FeatureQueryKind; resultId: string;
  hasFeatures: boolean; info: string; layers: FeatureQueryLayer[] }
export interface FeatureQueryPreviewFeature { geometry: GeoGeometry | null; properties: Record<string, unknown> | null; id?: unknown }
export interface FeatureQueryPreviewLayer {
  layerId: string; name: string; sourceRevision: string; mapType: string; lineColor: string; fillColor: string; lineWidth: number;
  features: FeatureQueryPreviewFeature[]; totalFeatureCount: number; returnedFeatureCount: number;
  totalCoordinateCount: number; returnedCoordinateCount: number; truncated: boolean;
}
export interface FeatureQueryPreview {
  map: FeatureQueryMapCapture; resultId: string; coordinateSpace: 'ProjectWorld'; layers: FeatureQueryPreviewLayer[];
  totalFeatureCount: number; returnedFeatureCount: number; totalCoordinateCount: number; returnedCoordinateCount: number;
  truncated: boolean; warnings: string[];
}
export interface FeatureQuerySaveReceipt { map: FeatureQueryMapCapture; sources: FeatureQueryExportedSource[]; info: string; warnings: string[] }

const record = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);
const sha = (value: unknown): value is string => typeof value === 'string' && /^[a-f\d]{64}$/i.test(value);
const guid = (value: unknown): value is string => typeof value === 'string' && /^[a-f\d]{32}$/i.test(value);
const equalSha = (a: string | null | undefined, b: string | null | undefined) => a == null ? b == null : b != null && a.toUpperCase() === b.toUpperCase();
const nonempty = (value: unknown): value is string => typeof value === 'string' && !!value.trim();
const count = (value: unknown): value is number => Number.isSafeInteger(value) && (value as number) >= 0;
const validColor = (value: unknown): value is string => typeof value === 'string' && /^#[\da-f]{8}$/i.test(value);
const validWidth = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value) && value >= 0;
const mapTypes = new Set(['PointsVector', 'PolylinesVector', 'PolygonsVector']);
const error = (message: string): never => { throw new Error(message); };

/** Accept only fully qualified server paths, including Windows drive and UNC paths. */
export function isAbsoluteServerPath(value: string): boolean {
  const path = value.trim();
  return path === value && (path.startsWith('/') && !path.startsWith('//') || /^[a-z]:[\\/]/i.test(path) || /^[/\\]{2}[^/\\]+[/\\][^/\\]+(?:[/\\]|$)/.test(path));
}
function canonicalLexicalPath(value: string): string | null {
  if (!isAbsoluteServerPath(value)) return null;
  const p = value.replace(/\\/g, '/');
  const unc = p.startsWith('//'), drive = /^[a-z]:\//i.test(p);
  const prefix = unc ? '//' : drive ? p.slice(0, 3).toLowerCase() : '/';
  const tail = unc ? p.slice(2) : drive ? p.slice(3) : p.slice(1);
  const parts: string[] = [];
  for (const part of tail.split('/')) {
    if (!part || part === '.') continue;
    if (part === '..') { if (!parts.length || unc && parts.length <= 2) return null; parts.pop(); }
    else parts.push(part);
  }
  const normalized = prefix + parts.join('/');
  return unc || drive ? normalized.toLowerCase() : normalized;
}
const samePath = (a: string, b: string) => canonicalLexicalPath(a) !== null && canonicalLexicalPath(a) === canonicalLexicalPath(b);
function parentPath(path: string): string {
  const normalized = path.replace(/\\/g, '/').replace(/\/+$/, '');
  const index = normalized.lastIndexOf('/');
  if (index === 0) return '/';
  if (index === 2 && /^[a-z]:/i.test(normalized)) return normalized.slice(0, 3);
  return normalized.slice(0, index);
}

export function featureQueryOwnerKey(context: WorkbenchContext, document: MapDocumentTab | null, includeRevision = true): string {
  return JSON.stringify([context.projectSessionID, context.projectPath, context.projectRevision, context.projectXml,
    context.coreLayerContext ?? null, document?.id, document?.mapID, document?.mapIndex, document?.name, document?.mapType,
    includeRevision ? document?.visualizationRevision : null]);
}
export function ownerCurrent(owner: FeatureQueryOwner, context: WorkbenchContext, document: MapDocumentTab | null,
  backend: BackendGateway, backendRevision = 0, includeRevision = true): boolean {
  return owner.backend === backend && owner.backendRevision === backendRevision &&
    featureQueryOwnerKey(owner.context, owner.document, includeRevision) === featureQueryOwnerKey(context, document, includeRevision);
}
export async function captureOwner(context: WorkbenchContext, document: MapDocumentTab | null,
  backend: BackendGateway, backendRevision = 0): Promise<FeatureQueryOwner> {
  if (!context.projectPath || !sha(context.projectRevision) || !context.projectSessionID || !document ||
      !document.mapID || !count(document.mapIndex) || !document.name || !document.mapType || !sha(document.visualizationRevision))
    return error('请先打开具有有效工程和地图版本的已保存地图。');
  const capturedContext = structuredClone(context), capturedDocument = structuredClone(document);
  const xmlSHA = await projectXmlSha(capturedContext.projectXml);
  return { backend, backendRevision, context: capturedContext, document: capturedDocument, map: {
    projectPath: capturedContext.projectPath!, projectXml: capturedContext.projectXml, projectXmlSHA: xmlSHA,
    mapID: capturedDocument.mapID, mapIndex: capturedDocument.mapIndex, mapName: capturedDocument.name,
    mapType: capturedDocument.mapType, expectedVisualizationRevision: capturedDocument.visualizationRevision,
    projectSessionID: capturedContext.projectSessionID, coreLayerContext: capturedContext.coreLayerContext,
  } };
}
function parseMap(value: unknown): FeatureQueryMapCapture {
  if (!record(value) || !nonempty(value.projectPath) || !isAbsoluteServerPath(value.projectPath) || !sha(value.projectRevision) ||
      !(value.projectXmlSHA === null || sha(value.projectXmlSHA)) || !nonempty(value.visualizationPath) ||
      !isAbsoluteServerPath(value.visualizationPath) || !(value.visualizationRevision === null || sha(value.visualizationRevision)) ||
      !nonempty(value.mapID) || !count(value.mapIndex) || !nonempty(value.mapName) || !nonempty(value.mapType) || value.isPersisted !== true)
    return error('服务返回的工程或地图身份不完整。');
  return value as unknown as FeatureQueryMapCapture;
}
export function sameMap(a: FeatureQueryMapCapture, b: FeatureQueryMapCapture): boolean {
  return samePath(a.projectPath, b.projectPath) && equalSha(a.projectRevision, b.projectRevision) && equalSha(a.projectXmlSHA, b.projectXmlSHA) &&
    samePath(a.visualizationPath, b.visualizationPath) && equalSha(a.visualizationRevision, b.visualizationRevision) &&
    a.mapID === b.mapID && a.mapIndex === b.mapIndex && a.mapName === b.mapName && a.mapType === b.mapType && a.isPersisted === b.isPersisted;
}
function assertOwnerMap(map: FeatureQueryMapCapture, owner: FeatureQueryOwner): void {
  // The Host returns a canonical project path, which may differ from a symlink alias in the request.
  if (!equalSha(map.projectRevision, owner.context.projectRevision) || !equalSha(map.projectXmlSHA, owner.map.projectXmlSHA) ||
      !equalSha(map.visualizationRevision, owner.document.visualizationRevision) || map.mapID !== owner.document.mapID ||
      map.mapIndex !== owner.document.mapIndex || map.mapName !== owner.document.name || map.mapType !== owner.document.mapType || !map.isPersisted)
    error('服务回执所属的工程或地图版本与查询来源不一致。');
}
export function parseSnapshot(value: unknown, owner: FeatureQueryOwner): FeatureQuerySnapshot {
  if (!record(value) || !Array.isArray(value.polygonSources) || !(value.selectedPolygonId === null || typeof value.selectedPolygonId === 'string'))
    return error('服务返回的多边形来源列表无效。');
  const map = parseMap(value.map); assertOwnerMap(map, owner);
  const sources = value.polygonSources.map(source => {
    if (!record(source) || !nonempty(source.id) || !nonempty(source.name)) return error('服务返回的多边形来源无效。');
    return { id: source.id, name: source.name };
  });
  if (new Set(sources.map(source => source.id)).size !== sources.length ||
      value.selectedPolygonId !== null && !sources.some(source => source.id === value.selectedPolygonId))
    return error('服务返回的多边形来源身份不一致。');
  return { map, polygonSources: sources, selectedPolygonId: value.selectedPolygonId };
}
export function queryInputError(kind: FeatureQueryKind, value: FeatureQueryFormValue, sources: FeatureQueryPolygonSource[],
  extent: FeatureQueryExtent | null, coordinatesAreProject: boolean,
  projectionIssue?: 'local' | 'missing' | 'invalid' | 'unknown' | null): string | null {
  if (projectionIssue === 'local') return '当前工程使用 Local 局部坐标，无法定位在线数据；请先设置有效的工程投影。';
  if (projectionIssue) return '工程中的投影定义缺失、无效或无法识别；请先设置有效的工程投影。';
  const needsExtent = kind !== 'NSI' || !value.useBoundaryPolygon;
  if (needsExtent && !coordinatesAreProject) return '当前地图坐标尚未确认属于工程坐标系，无法按此范围查询。';
  if (needsExtent && (!extent || ![extent.minX, extent.minY, extent.maxX, extent.maxY].every(Number.isFinite) ||
      extent.maxX <= extent.minX || extent.maxY <= extent.minY)) return '当前地图范围不可用，请先定位到有效范围。';
  if (kind === 'NSI' && value.useBoundaryPolygon && !sources.some(source => source.id === value.polygonSourceId))
    return '请选择当前地图快照中的多边形来源。';
  if (kind === 'NLD' && !['AlignmentLines', 'Embankments', 'Floodwalls', 'SystemRoutes'].includes(value.nldLayerId))
    return '请选择 NLD 要素类型。';
  if (kind === 'OSM' && !value.rawQl.trim()) return '请填写 OpenStreetMap 查询语句。';
  return null;
}
export function parseQuery(value: unknown, owner: FeatureQueryOwner, snapshot: FeatureQuerySnapshot, kind: FeatureQueryKind): FeatureQueryResult {
  if (!record(value) || value.kind !== kind || !guid(value.resultId) || typeof value.hasFeatures !== 'boolean' ||
      typeof value.info !== 'string' || !Array.isArray(value.layers)) return error('服务返回的查询结果无效。');
  const map = parseMap(value.map); assertOwnerMap(map, owner);
  if (!sameMap(map, snapshot.map)) return error('查询结果与所选地图快照不一致。');
  const layers = value.layers.map(raw => {
    if (!record(raw) || !nonempty(raw.name) || !nonempty(raw.sourcePath) || !isAbsoluteServerPath(raw.sourcePath) ||
        !sha(raw.sourceRevision) || !nonempty(raw.mapType) || !mapTypes.has(raw.mapType) ||
        !validColor(raw.lineColor) || !validColor(raw.fillColor) || !validWidth(raw.lineWidth) || raw.coordinateSpace !== 'ProjectWorld')
      return error('查询图层身份、样式或坐标系无效。');
    return raw as unknown as FeatureQueryLayer;
  });
  if (value.hasFeatures !== (layers.length > 0) || new Set(layers.map(layer => layer.sourcePath)).size !== layers.length)
    return error('查询图层数量与查询状态不一致。');
  return { map, kind, resultId: value.resultId, hasFeatures: value.hasFeatures, info: value.info, layers };
}
export function parsePreview(value: unknown, result: FeatureQueryResult): FeatureQueryPreview {
  if (!record(value) || value.resultId !== result.resultId || value.coordinateSpace !== 'ProjectWorld' || !Array.isArray(value.layers) ||
      !Array.isArray(value.warnings) || !value.warnings.every(item => typeof item === 'string') || !record(value.budget) ||
      !count(value.totalFeatureCount) || !count(value.returnedFeatureCount) || !count(value.totalCoordinateCount) ||
      !count(value.returnedCoordinateCount) || typeof value.truncated !== 'boolean') return error('服务返回的查询预览无效。');
  const map = parseMap(value.map);
  if (!sameMap(map, result.map) || value.layers.length !== result.layers.length) return error('预览与查询暂存的地图或图层不一致。');
  const layers = value.layers.map((raw, index): FeatureQueryPreviewLayer => {
    const expected = result.layers[index];
    if (!record(raw) || !nonempty(raw.layerId) || raw.layerId !== expected.sourcePath.split(/[\\/]/).pop() || raw.name !== expected.name ||
        !sha(raw.sourceRevision) || !equalSha(raw.sourceRevision, expected.sourceRevision) || raw.mapType !== expected.mapType ||
        raw.lineColor !== expected.lineColor || raw.fillColor !== expected.fillColor || raw.lineWidth !== expected.lineWidth ||
        !record(raw.featureCollection) || raw.featureCollection.type !== 'FeatureCollection' || !Array.isArray(raw.featureCollection.features) ||
        !count(raw.totalFeatureCount) || !count(raw.returnedFeatureCount) || !count(raw.totalCoordinateCount) || !count(raw.returnedCoordinateCount) ||
        raw.returnedFeatureCount > raw.totalFeatureCount || raw.returnedCoordinateCount > raw.totalCoordinateCount ||
        typeof raw.truncated !== 'boolean' || raw.truncated !== (raw.returnedFeatureCount !== raw.totalFeatureCount) ||
        raw.returnedFeatureCount !== raw.featureCollection.features.length) return error('查询预览图层的身份或数量不一致。');
    let coordinates = 0;
    const features = raw.featureCollection.features.map((feature): FeatureQueryPreviewFeature => {
      if (!record(feature) || feature.type !== 'Feature' || !(feature.properties === null || record(feature.properties)) ||
          !(feature.geometry === null || record(feature.geometry))) return error('查询预览包含无效的 GeoJSON 要素。');
      const geometry = feature.geometry === null ? null : parseGeometry(feature.geometry);
      if (geometry) coordinates += positionsInGeometry(geometry).length;
      return { geometry, properties: feature.properties as Record<string, unknown> | null, id: feature.id };
    });
    if (coordinates !== raw.returnedCoordinateCount) return error('查询预览坐标数量与服务回执不一致。');
    return { layerId: raw.layerId, name: expected.name, sourceRevision: expected.sourceRevision, mapType: expected.mapType,
      lineColor: expected.lineColor, fillColor: expected.fillColor, lineWidth: expected.lineWidth, features,
      totalFeatureCount: raw.totalFeatureCount, returnedFeatureCount: raw.returnedFeatureCount,
      totalCoordinateCount: raw.totalCoordinateCount, returnedCoordinateCount: raw.returnedCoordinateCount, truncated: raw.truncated };
  });
  const totals = layers.reduce((sum, layer) => [sum[0] + layer.totalFeatureCount, sum[1] + layer.returnedFeatureCount,
    sum[2] + layer.totalCoordinateCount, sum[3] + layer.returnedCoordinateCount], [0, 0, 0, 0]);
  if (totals[0] !== value.totalFeatureCount || totals[1] !== value.returnedFeatureCount || totals[2] !== value.totalCoordinateCount ||
      totals[3] !== value.returnedCoordinateCount || value.truncated !== layers.some(layer => layer.truncated) ||
      !count(value.budget.maxFeatures) || !count(value.budget.maxCoordinates) || !count(value.budget.maxPayloadBytes) || !count(value.budget.maxSourceBytes) ||
      value.returnedFeatureCount > value.budget.maxFeatures || value.returnedCoordinateCount > value.budget.maxCoordinates)
    return error('查询预览总数或共享预算与回执不一致。');
  return { map, resultId: result.resultId, coordinateSpace: 'ProjectWorld', layers,
    totalFeatureCount: value.totalFeatureCount, returnedFeatureCount: value.returnedFeatureCount,
    totalCoordinateCount: value.totalCoordinateCount, returnedCoordinateCount: value.returnedCoordinateCount,
    truncated: value.truncated, warnings: value.warnings as string[] };
}
function parseFamily(raw: unknown, source: Record<string, unknown>, format: FeatureQueryFormat): FeatureQuerySourceFamily {
  if (!record(raw) || raw.family !== (format === 'Shapefile' ? 'shapefile' : 'geojson') || !nonempty(raw.sourcePath) ||
      !isAbsoluteServerPath(raw.sourcePath) || !sha(raw.revision) || !Array.isArray(raw.files)) return error('导出文件族回执无效。');
  const roles = new Map<string, Record<string, unknown>>();
  for (const file of raw.files) {
    if (!record(file) || !nonempty(file.inputPath) || !isAbsoluteServerPath(file.inputPath) || !nonempty(file.path) ||
        !isAbsoluteServerPath(file.path) || !nonempty(file.role) || typeof file.required !== 'boolean' || typeof file.exists !== 'boolean' ||
        !(file.revision === null || sha(file.revision)) || (file.exists !== (file.revision !== null)) || roles.has(file.role))
      return error('导出文件族的成员身份无效。');
    roles.set(file.role, file);
  }
  const primary = roles.get('source');
  if (!primary || primary.required !== true || primary.exists !== true || !samePath(primary.inputPath as string, source.path as string) ||
      !samePath(primary.path as string, raw.sourcePath) || !equalSha(primary.revision as string, source.revision as string))
    return error('导出文件与主文件族回执不一致。');
  if (format === 'GeoJson' && roles.size !== 1) return error('GeoJSON 文件族包含未声明的成员。');
  if (format === 'Shapefile') {
    if (roles.size !== 4 || !['shx', 'dbf', 'prj'].every(role => roles.has(role))) return error('Shapefile 文件族缺少成员。');
    for (const role of ['shx', 'dbf', 'prj']) {
      const file = roles.get(role)!;
      const base = (source.path as string).replace(/\.shp$/i, '');
      if (!samePath(file.inputPath as string, base + '.' + role) || file.required !== (role !== 'prj') ||
          role !== 'prj' && file.exists !== true) return error('Shapefile 文件族成员不完整。');
    }
  }
  return raw as unknown as FeatureQuerySourceFamily;
}
export function parseSaveReceipt(value: unknown, result: FeatureQueryResult, format: FeatureQueryFormat, directory: string,
  preview?: FeatureQueryPreview | null, mergedAlignments = false): FeatureQuerySaveReceipt {
  if (!record(value) || !Array.isArray(value.sources) || typeof value.info !== 'string' ||
      !(value.warnings === null || Array.isArray(value.warnings) && value.warnings.every(item => typeof item === 'string')))
    return error('导出回执格式无效，无法确认写入结果。');
  const map = parseMap(value.map);
  if (!sameMap(map, result.map)) return error('导出回执所属工程地图与查询暂存不一致。');
  const seen = new Set<string>();
  const sources = value.sources.map((source): FeatureQueryExportedSource => {
    if (!record(source) || !nonempty(source.path) || !isAbsoluteServerPath(source.path) ||
        !samePath(parentPath(source.path), directory) || !source.path.toLowerCase().endsWith(format === 'Shapefile' ? '.shp' : '.geojson') ||
        !sha(source.revision) || !nonempty(source.name) || source.name !== source.path.split(/[\\/]/).pop()!.replace(/\.(?:geojson|shp)$/i, '') ||
        !nonempty(source.mapType) || !mapTypes.has(source.mapType) || !count(source.featureCount) || source.featureCount === 0 ||
        !validColor(source.lineColor) || !validColor(source.fillColor) || !validWidth(source.lineWidth))
      return error('导出文件路径、版本或属性与请求不一致。');
    const pathKey = canonicalLexicalPath(source.path)!;
    if (seen.has(pathKey)) return error('导出回执包含重复路径。');
    seen.add(pathKey);
    const family = parseFamily(source.family, source, format);
    return { path: source.path, revision: source.revision, name: source.name, mapType: source.mapType,
      featureCount: source.featureCount, lineColor: source.lineColor, fillColor: source.fillColor, lineWidth: source.lineWidth, family };
  });
  if (mergedAlignments) {
    if (result.kind !== 'NLD' || sources.length > 1 || sources.some(source => source.mapType !== 'PolylinesVector'))
      return error('合并堤线的导出结果与查询类型不一致。');
  } else {
    if (sources.length > result.layers.length || sources.length < result.layers.length && !(value.warnings as unknown[] | null)?.length)
      return error('导出图层数量与查询和警告回执不一致。');
    const remaining = result.layers.map((layer, index) => ({ type: layer.mapType, count: preview?.layers[index]?.totalFeatureCount }));
    for (const source of sources) {
      const index = remaining.findIndex(layer => layer.type === source.mapType && (layer.count === undefined || layer.count === source.featureCount));
      if (index < 0) return error('导出图层类型或要素数量与查询结果不一致。');
      remaining.splice(index, 1);
    }
  }
  return { map, sources, info: value.info, warnings: (value.warnings ?? []) as string[] };
}
export function assertDiscard(value: unknown, resultId: string): void {
  if (!record(value) || value.resultId !== resultId || typeof value.discarded !== 'boolean' || typeof value.alreadyMissing !== 'boolean' ||
      value.discarded === value.alreadyMissing) error('查询暂存清理尚未得到有效确认。');
}
