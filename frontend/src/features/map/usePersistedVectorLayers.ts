import { useEffect, useRef, useState } from 'react';
import { BackendOperationUnavailableError, errorDiagnostics, message } from '../../shared/workbench';
import type { BackendGateway, WorkbenchContext } from '../../shared/workbench';
import { projectXmlSha } from './ProjectTreeListener';
import type {
  CatalogTreeNode,
  CatalogTreeNodeLocator,
  GeoFeature,
  GeoGeometry,
  GeoJsonLayer,
  GeoLine,
  MapDocumentTab,
  MapExtent,
  XY,
} from './mapTypes';

type ExtentSnapshot = { minX: number; minY: number; maxX: number; maxY: number };
type AttributeSnapshot = {
  columnOrdinal: number;
  columnName: string;
  type: string;
  value: string | null;
  booleanValue: boolean | null;
};
type AttributeFieldSnapshot = {
  ordinal: number;
  name: string;
  sourceType: string;
  width: number;
  decimalCount: number;
  valueType: string;
};
type VertexSnapshot = {
  x: number;
  y: number;
  sourceX: number;
  sourceY: number;
  z: number | null;
  m: number | null;
};
type PartSnapshot = {
  partOrdinal: number;
  polygonIndex?: number | null;
  isHole?: boolean | null;
  vertices: VertexSnapshot[];
  hasZ: boolean;
  hasM: boolean;
};
type FeatureSnapshot = {
  sourceOrdinal: number;
  sourceID: string | null;
  shapeType: string;
  parts: PartSnapshot[];
  attributes: AttributeSnapshot[];
};
type SourceFamilyFile = {
  inputPath: string;
  path: string;
  role: string;
  required: boolean;
  exists: boolean;
  revision: string | null;
};
type SourceFamilyCapture = {
  family: string;
  sourcePath: string;
  revision: string;
  files: SourceFamilyFile[];
};
type MapSourceMapCapture = {
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
type MapSourceNodeCapture = {
  map: MapSourceMapCapture;
  locator: CatalogTreeNodeLocator;
  family: SourceFamilyCapture;
};
type MapVectorSourceData = {
  shapeType: 'Point' | 'Polyline' | 'Polygon';
  shapeTypeCode: number;
  count: number;
  sourceExtent: ExtentSnapshot | null;
  extent: ExtentSnapshot | null;
  projectionStatus: 'projected' | 'localProjectSourceCoordinates';
  sourceProjectionWKT: string | null;
  targetProjectionWKT: string | null;
  features: FeatureSnapshot[];
  attributeSchema?: AttributeFieldSnapshot[] | null;
};
type MapVectorSourceLoadResult = { source: MapSourceNodeCapture; data: MapVectorSourceData };

export interface PersistedVectorLayerCapture {
  layerID: string;
  nodeID: string;
  mapType: string;
  source: MapSourceNodeCapture;
  data: Omit<MapVectorSourceData, 'features'>;
}

export interface PersistedVectorLayerIssue {
  nodeID: string;
  name: string;
  mapType: string | null;
  message: string;
  diagnostic?: unknown;
}

export interface PersistedVectorLayersError {
  message: string;
  diagnostic: unknown;
}

export interface UsePersistedVectorLayersProps {
  backend: BackendGateway;
  context: WorkbenchContext;
  document: MapDocumentTab | null;
  language: 'zh' | 'en';
}

export interface UsePersistedVectorLayersResult {
  layers: GeoJsonLayer[];
  captures: PersistedVectorLayerCapture[];
  issues: PersistedVectorLayerIssue[];
  loading: boolean;
  error: PersistedVectorLayersError | null;
}

const EMPTY_RESULT: UsePersistedVectorLayersResult = {
  layers: [], captures: [], issues: [], loading: false, error: null,
};

interface VectorNodeCandidate {
  node: CatalogTreeNode;
  locator: CatalogTreeNodeLocator;
  mapType: string;
  extension: string;
  expectedSourceRevision: string;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isRevision(value: unknown): value is string {
  return typeof value === 'string' && /^[0-9a-f]{64}$/i.test(value);
}

function isFiniteNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value);
}

function parseExtent(value: unknown, label: string): ExtentSnapshot {
  if (!isRecord(value) || !isFiniteNumber(value.minX) || !isFiniteNumber(value.minY) ||
      !isFiniteNumber(value.maxX) || !isFiniteNumber(value.maxY) ||
      value.minX > value.maxX || value.minY > value.maxY) {
    throw new Error(`${label} 无效。`);
  }
  return { minX: value.minX, minY: value.minY, maxX: value.maxX, maxY: value.maxY };
}

function parseFamily(value: unknown): SourceFamilyCapture {
  if (!isRecord(value) || !['shapefile', 'geojson'].includes(String(value.family)) || typeof value.sourcePath !== 'string' ||
      !isRevision(value.revision) || !Array.isArray(value.files)) {
    throw new Error('服务端返回的矢量来源快照无效。');
  }
  const files = value.files.map((item): SourceFamilyFile => {
    if (!isRecord(item) || typeof item.inputPath !== 'string' || typeof item.path !== 'string' ||
        typeof item.role !== 'string' || typeof item.required !== 'boolean' || typeof item.exists !== 'boolean' ||
        !(item.revision === null || isRevision(item.revision))) {
      throw new Error('服务端返回的矢量来源文件版本无效。');
    }
    if (item.required && (!item.exists || !isRevision(item.revision))) {
      throw new Error('矢量来源必需文件缺失或没有有效版本号。');
    }
    return {
      inputPath: item.inputPath,
      path: item.path,
      role: item.role,
      required: item.required,
      exists: item.exists,
      revision: item.revision as string | null,
    };
  });
  const sourceFile = files.find((file) => file.role === 'source' && file.required);
  if (!sourceFile || sourceFile.path !== value.sourcePath) throw new Error('矢量来源快照缺少有效的主文件身份。');
  if (value.family === 'shapefile') {
    const dbfFile = files.find((file) => file.role === 'dbf' && file.required);
    const shxFile = files.find((file) => file.role === 'shx' && file.required);
    if (!dbfFile || !shxFile) throw new Error('Shapefile 文件组缺少必需的 SHP、SHX 或 DBF 文件。');
  } else if (files.length !== 1 || !sourceFile.exists || !sourceFile.revision) {
    throw new Error('GeoJSON 来源快照必须只包含一个已验证的主文件。');
  }
  return { family: value.family as string, sourcePath: value.sourcePath, revision: value.revision, files };
}

function parseFeature(value: unknown, expectedShape: string): FeatureSnapshot {
  if (!isRecord(value) || !Number.isInteger(value.sourceOrdinal) || (value.sourceOrdinal as number) < 0 ||
      !(value.sourceID === null || typeof value.sourceID === 'string') || value.shapeType !== expectedShape ||
      !Array.isArray(value.parts) || !Array.isArray(value.attributes)) {
    throw new Error('服务端返回的矢量要素结构无效。');
  }
  const parts = value.parts.map((part): PartSnapshot => {
    if (!isRecord(part) || !Number.isInteger(part.partOrdinal) || (part.partOrdinal as number) < 0 ||
        !Array.isArray(part.vertices) || typeof part.hasZ !== 'boolean' || typeof part.hasM !== 'boolean') {
      throw new Error('服务端返回的矢量几何部件无效。');
    }
    const hasPolygonIndex = Number.isInteger(part.polygonIndex) && (part.polygonIndex as number) >= 0;
    const hasHoleFlag = typeof part.isHole === 'boolean';
    const hasNoPolygonMetadata = (part.polygonIndex === undefined || part.polygonIndex === null) &&
      (part.isHole === undefined || part.isHole === null);
    if (!hasNoPolygonMetadata && !(hasPolygonIndex && hasHoleFlag)) {
      throw new Error('服务端返回的多边形环分组无效。');
    }
    const vertices = part.vertices.map((vertex): VertexSnapshot => {
      if (!isRecord(vertex) || !isFiniteNumber(vertex.x) || !isFiniteNumber(vertex.y) ||
          !isFiniteNumber(vertex.sourceX) || !isFiniteNumber(vertex.sourceY) ||
          !(vertex.z === null || isFiniteNumber(vertex.z)) || !(vertex.m === null || isFiniteNumber(vertex.m))) {
        throw new Error('服务端返回了无效的源坐标或工程坐标。');
      }
      return {
        x: vertex.x,
        y: vertex.y,
        sourceX: vertex.sourceX,
        sourceY: vertex.sourceY,
        z: vertex.z as number | null,
        m: vertex.m as number | null,
      };
    });
    return {
      partOrdinal: part.partOrdinal as number,
      polygonIndex: part.polygonIndex as number | null | undefined,
      isHole: part.isHole as boolean | null | undefined,
      vertices,
      hasZ: part.hasZ,
      hasM: part.hasM,
    };
  });
  const attributes = value.attributes.map((attribute): AttributeSnapshot => {
    if (!isRecord(attribute) || !Number.isInteger(attribute.columnOrdinal) || (attribute.columnOrdinal as number) < 0 ||
        typeof attribute.columnName !== 'string' || typeof attribute.type !== 'string' ||
        !(attribute.value === null || typeof attribute.value === 'string') ||
        !(attribute.booleanValue === null || typeof attribute.booleanValue === 'boolean')) {
      throw new Error('服务端返回的 DBF 属性无效。');
    }
    return {
      columnOrdinal: attribute.columnOrdinal as number,
      columnName: attribute.columnName,
      type: attribute.type,
      value: attribute.value as string | null,
      booleanValue: attribute.booleanValue as boolean | null,
    };
  }).sort((left, right) => left.columnOrdinal - right.columnOrdinal);
  if (new Set(attributes.map((attribute) => attribute.columnOrdinal)).size !== attributes.length) {
    throw new Error('服务端返回的 DBF 属性列序号重复。');
  }
  return {
    sourceOrdinal: value.sourceOrdinal as number,
    sourceID: value.sourceID as string | null,
    shapeType: value.shapeType,
    parts,
    attributes,
  };
}

function parseSourceLoad(value: unknown): MapVectorSourceLoadResult {
  if (!isRecord(value) || !isRecord(value.source) || !isRecord(value.source.map) ||
      !isRecord(value.source.locator) || !isRecord(value.data)) {
    throw new Error('服务端返回的矢量来源响应无效。');
  }
  const mapValue = value.source.map;
  if (typeof mapValue.projectPath !== 'string' || !isRevision(mapValue.projectRevision) ||
      !(mapValue.projectXmlSHA === null || isRevision(mapValue.projectXmlSHA)) ||
      typeof mapValue.visualizationPath !== 'string' ||
      !(mapValue.visualizationRevision === null || isRevision(mapValue.visualizationRevision)) ||
      !(mapValue.mapID === null || typeof mapValue.mapID === 'string') ||
      !(mapValue.mapIndex === null || (Number.isInteger(mapValue.mapIndex) && (mapValue.mapIndex as number) >= 0)) ||
      !(mapValue.mapName === null || typeof mapValue.mapName === 'string') || typeof mapValue.mapType !== 'string' ||
      typeof mapValue.isPersisted !== 'boolean') {
    throw new Error('服务端返回的地图来源身份无效。');
  }
  const locatorValue = value.source.locator;
  if (typeof locatorValue.rootName !== 'string' || typeof locatorValue.nodeType !== 'string' ||
      typeof locatorValue.nodeName !== 'string' || !Array.isArray(locatorValue.nodePath) ||
      !locatorValue.nodePath.every((part) => Number.isInteger(part) && part >= 0) ||
      typeof locatorValue.mapType !== 'string' || typeof locatorValue.filename !== 'string' ||
      typeof locatorValue.filenameRelative !== 'string') {
    throw new Error('服务端返回的地图图层定位信息无效。');
  }
  const family = parseFamily(value.source.family);
  const dataValue = value.data;
  if (!['Point', 'Polyline', 'Polygon'].includes(String(dataValue.shapeType)) ||
      !Number.isInteger(dataValue.shapeTypeCode) || !Number.isInteger(dataValue.count) ||
      (dataValue.count as number) < 0 ||
      !(dataValue.sourceExtent === null || isRecord(dataValue.sourceExtent)) ||
      !(dataValue.extent === null || isRecord(dataValue.extent)) ||
      !['projected', 'localProjectSourceCoordinates'].includes(String(dataValue.projectionStatus)) ||
      !(dataValue.sourceProjectionWKT === null || typeof dataValue.sourceProjectionWKT === 'string') ||
      !(dataValue.targetProjectionWKT === null || typeof dataValue.targetProjectionWKT === 'string') ||
      !Array.isArray(dataValue.features)) {
    throw new Error('服务端返回的矢量数据或投影信息无效。');
  }
  const shapeType = dataValue.shapeType as MapVectorSourceData['shapeType'];
  const features = dataValue.features.map((feature) => parseFeature(feature, shapeType));
  if (features.length !== dataValue.count ||
      new Set(features.map((feature) => feature.sourceOrdinal)).size !== features.length) {
    throw new Error('服务端矢量记录数与实际要素数不一致。');
  }
  const sourceExtent = dataValue.sourceExtent === null ? null : parseExtent(dataValue.sourceExtent, '源坐标范围');
  const extent = dataValue.extent === null ? null : parseExtent(dataValue.extent, '工程坐标范围');
  if (dataValue.projectionStatus === 'localProjectSourceCoordinates' && dataValue.targetProjectionWKT !== null) {
    throw new Error('本地工程坐标不能带有目标投影定义。');
  }
  const attributeSchema = dataValue.attributeSchema == null ? dataValue.attributeSchema : (() => {
    if (!Array.isArray(dataValue.attributeSchema)) throw new Error('服务端返回的 DBF 字段架构无效。');
    const fields = dataValue.attributeSchema.map((field): AttributeFieldSnapshot => {
      if (!isRecord(field) || !Number.isInteger(field.ordinal) || (field.ordinal as number) < 0 ||
          typeof field.name !== 'string' || typeof field.sourceType !== 'string' ||
          !Number.isInteger(field.width) || !Number.isInteger(field.decimalCount) || typeof field.valueType !== 'string') {
        throw new Error('服务端返回的 DBF 字段架构无效。');
      }
      return {
        ordinal: field.ordinal as number,
        name: field.name,
        sourceType: field.sourceType,
        width: field.width as number,
        decimalCount: field.decimalCount as number,
        valueType: field.valueType,
      };
    });
    if (new Set(fields.map((field) => field.ordinal)).size !== fields.length) {
      throw new Error('服务端返回的 DBF 字段序号重复。');
    }
    return fields;
  })();
  return {
    source: {
      map: {
        projectPath: mapValue.projectPath,
        projectRevision: mapValue.projectRevision,
        projectXmlSHA: mapValue.projectXmlSHA as string | null,
        visualizationPath: mapValue.visualizationPath,
        visualizationRevision: mapValue.visualizationRevision as string | null,
        mapID: mapValue.mapID as string | null,
        mapIndex: mapValue.mapIndex as number | null,
        mapName: mapValue.mapName as string | null,
        mapType: mapValue.mapType,
        isPersisted: mapValue.isPersisted,
      },
      locator: locatorValue as unknown as CatalogTreeNodeLocator,
      family,
    },
    data: {
      shapeType,
      shapeTypeCode: dataValue.shapeTypeCode as number,
      count: dataValue.count as number,
      sourceExtent,
      extent,
      projectionStatus: dataValue.projectionStatus as MapVectorSourceData['projectionStatus'],
      sourceProjectionWKT: dataValue.sourceProjectionWKT as string | null,
      targetProjectionWKT: dataValue.targetProjectionWKT as string | null,
      features,
      attributeSchema,
    },
  };
}

function locatorExtension(locator: CatalogTreeNodeLocator): string {
  const path = locator.filename ?? locator.filenameRelative ?? locator.assetID ?? '';
  const name = path.split(/[\\/]/).pop() ?? '';
  const separator = name.lastIndexOf('.');
  return separator < 0 ? '' : name.slice(separator).toLowerCase();
}

function collectVectorNodes(roots: CatalogTreeNode[]): { candidates: VectorNodeCandidate[]; issues: PersistedVectorLayerIssue[] } {
  const candidates: VectorNodeCandidate[] = [];
  const issues: PersistedVectorLayerIssue[] = [];
  const supportedMapTypes = new Set(['PointsVector', 'PolylinesVector', 'PolygonsVector']);

  const visit = (node: CatalogTreeNode, ancestorsVisible: boolean) => {
    const visible = ancestorsVisible && node.checked !== 'unchecked';
    const locator = node.locator;
    if (visible && locator?.nodeType === 'MapLayer') {
      const mapType = locator.mapType ?? '';
      const isVector = supportedMapTypes.has(mapType);
      if (isVector && node.checked === 'checked') {
        {
          const extension = locatorExtension(locator);
          if (extension === '.shp' || extension === '.json' || extension === '.geojson') {
            const expectedSourceRevision = (node as CatalogTreeNode & { diagnosticSourceRevision?: unknown }).diagnosticSourceRevision;
            if (!isRevision(expectedSourceRevision)) {
              issues.push({ nodeID: node.id, name: node.name, mapType, message: '地图树没有提供可验证的矢量源文件版本，未载入。' });
            } else {
              candidates.push({ node, locator, mapType, extension, expectedSourceRevision });
            }
          } else {
            issues.push({ nodeID: node.id, name: node.name, mapType, message: '当前画布只支持读取 .shp、.json 和 .geojson 矢量图层。' });
          }
        }
      } else if (supportedMapTypes.has(mapType) && node.checked === null) {
        issues.push({
          nodeID: node.id,
          name: node.name,
          mapType,
          message: node.diagnosticReadError
            ? `图层未通过来源校验，未载入：${node.diagnosticReadError}`
            : '图层没有已验证的勾选状态，未载入。',
          diagnostic: node.diagnosticReadError,
        });
      }
    }
    for (const child of node.children ?? []) visit(child, visible);
  };

  for (const root of roots) visit(root, true);
  return { candidates, issues };
}

function sameLocator(left: CatalogTreeNodeLocator, right: CatalogTreeNodeLocator): boolean {
  return left.rootName === right.rootName && left.nodeType === right.nodeType && left.nodeName === right.nodeName &&
    left.mapType === right.mapType && left.assetID === right.assetID && left.filename === right.filename &&
    left.filenameRelative === right.filenameRelative &&
    JSON.stringify(left.nodePath ?? null) === JSON.stringify(right.nodePath ?? null);
}

function verifySourceIdentity(
  result: MapVectorSourceLoadResult,
  context: WorkbenchContext,
  document: MapDocumentTab,
  projectXmlSHA: string | null,
  candidate: VectorNodeCandidate,
): void {
  const map = result.source.map;
  if (!context.projectRevision || map.projectRevision.toUpperCase() !== context.projectRevision.toUpperCase() ||
      map.projectXmlSHA?.toUpperCase() !== projectXmlSHA?.toUpperCase() ||
      map.visualizationRevision?.toUpperCase() !== document.visualizationRevision?.toUpperCase() ||
      map.mapID !== document.mapID || map.mapIndex !== document.mapIndex || map.mapName !== document.name ||
      map.mapType !== document.mapType || !map.isPersisted ||
      !sameLocator(result.source.locator, candidate.locator) ||
      result.source.family.family !== (candidate.extension === '.shp' ? 'shapefile' : 'geojson')) {
    throw new Error('读取矢量期间工程、地图、.vis 文件或图层来源发生变化。');
  }
  const sourceFile = result.source.family.files.find((file) => file.role === 'source' && file.required);
  if (!sourceFile?.revision || sourceFile.revision.toUpperCase() !== candidate.expectedSourceRevision.toUpperCase()) {
    throw new Error('矢量源文件版本与地图树读取时的版本不一致。');
  }
  if (candidate.extension !== '.shp' && result.source.family.files.length !== 1) {
    throw new Error('GeoJSON 来源快照包含了未声明的依赖文件。');
  }
}

function propertyValue(attribute: AttributeSnapshot): unknown {
  return attribute.booleanValue !== null ? attribute.booleanValue : attribute.value;
}

function toProperties(attributes: AttributeSnapshot[]): Record<string, unknown> {
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

function pointValue(vertex: VertexSnapshot): XY {
  return [vertex.x, vertex.y];
}

function geometryFromSource(feature: FeatureSnapshot, mapType: string): GeoGeometry {
  const sourceParts = feature.parts.slice().sort((left, right) => left.partOrdinal - right.partOrdinal);
  const parts = sourceParts.map((part) => part.vertices.map(pointValue)).filter((vertices) => vertices.length > 0);
  if (parts.length === 0) return { type: 'GeometryCollection', geometries: [] };
  if (mapType === 'PointsVector') {
    const points = parts.flat();
    return points.length === 1 ? { type: 'Point', coordinates: points[0] } : { type: 'MultiPoint', coordinates: points };
  }
  if (mapType === 'PolylinesVector') {
    return parts.length === 1
      ? { type: 'LineString', coordinates: parts[0] as GeoLine }
      : { type: 'MultiLineString', coordinates: parts as GeoLine[] };
  }
  const hasPolygonMetadata = sourceParts.some((part) => part.polygonIndex != null || part.isHole != null);
  if (hasPolygonMetadata && sourceParts.some((part) => part.polygonIndex == null || part.isHole == null)) {
    throw new Error('服务端返回的多边形环分组不完整。');
  }
  const polygons = new Map<number, GeoLine[]>();
  const holeFlags = new Map<number, boolean[]>();
  sourceParts.forEach((part) => {
    const ring = part.vertices.map(pointValue);
    if (ring.length === 0) return;
    const polygonIndex = part.polygonIndex ?? 0;
    const rings = polygons.get(polygonIndex) ?? [];
    rings.push(ring);
    polygons.set(polygonIndex, rings);
    const holes = holeFlags.get(polygonIndex) ?? [];
    holes.push(part.isHole ?? false);
    holeFlags.set(polygonIndex, holes);
  });
  const grouped = [...polygons.entries()].sort(([left], [right]) => left - right).map(([index, rings]) => {
    if (!hasPolygonMetadata) return rings;
    const holes = holeFlags.get(index) ?? [];
    const exteriorIndices = holes.flatMap((isHole, ringIndex) => isHole ? [] : [ringIndex]);
    if (exteriorIndices.length !== 1) throw new Error('服务端返回的多边形没有唯一外环。');
    const exterior = rings[exteriorIndices[0]];
    return [exterior, ...rings.filter((_ring, ringIndex) => ringIndex !== exteriorIndices[0])];
  });
  return grouped.length > 1
    ? { type: 'MultiPolygon', coordinates: grouped }
    : { type: 'Polygon', coordinates: grouped[0] ?? [] };
}

function layerFromSource(
  result: MapVectorSourceLoadResult,
  document: MapDocumentTab,
  candidate: VectorNodeCandidate,
): { layer: GeoJsonLayer; capture: PersistedVectorLayerCapture } {
  const { data, source } = result;
  const expectedShape = candidate.mapType === 'PointsVector' ? 'Point'
    : candidate.mapType === 'PolylinesVector' ? 'Polyline' : 'Polygon';
  if (data.shapeType !== expectedShape) throw new Error('矢量几何类型与地图图层类型不一致。');
  if (data.projectionStatus === 'projected' &&
      (!data.sourceProjectionWKT?.trim() || !data.targetProjectionWKT?.trim())) {
    throw new Error('服务端没有返回完整的源坐标系和工程坐标系定义。');
  }
  if (data.features.length > 0 && data.extent === null) throw new Error('服务端没有返回矢量要素的工程坐标范围。');

  const sourceNamePath = candidate.locator.filename ?? candidate.locator.filenameRelative ?? source.family.sourcePath;
  const sourceName = sourceNamePath.split(/[\\/]/).pop() || candidate.node.name;
  const layerID = `persisted-vector:${document.id}:${candidate.node.id}`;
  const features: GeoFeature[] = data.features.map((feature) => {
    const properties = toProperties(feature.attributes);
    const featureIdentity = feature.sourceID === null
      ? `record:${feature.sourceOrdinal}`
      : `source:${feature.sourceID}:${feature.sourceOrdinal}`;
    return {
      id: `${layerID}:${featureIdentity}`,
      name: featureName(properties, candidate.node.name, feature.sourceOrdinal),
      geometry: geometryFromSource(feature, candidate.mapType),
      properties,
    };
  });
  if (data.extent === null) throw new Error('矢量图层没有可绘制的工程坐标。');
  const extent: MapExtent = data.extent;
  const crs = data.projectionStatus === 'projected'
    ? data.targetProjectionWKT!
    : '工程本地坐标';
  const layer: GeoJsonLayer = {
    id: layerID,
    name: candidate.node.name,
    sourceName,
    crs,
    coordinateSpace: 'project',
    features,
    extent,
    visible: true,
    opacity: 1,
  };
  const { features: _features, ...capturedData } = data;
  return {
    layer,
    capture: {
      layerID,
      nodeID: candidate.node.id,
      mapType: candidate.mapType,
      source,
      data: capturedData,
    },
  };
}

function makeError(error: unknown, language: 'zh' | 'en'): PersistedVectorLayersError {
  const translated = message(error, language);
  return {
    message: language === 'zh' ? translated : message(error, 'zh'),
    diagnostic: errorDiagnostics(error),
  };
}

/**
 * Reads checked, persisted Shapefile and GeoJSON map layers through the retained C# source reader.
 * File vectors are handled here; file geodatabase layers use usePersistedGdbLayers.
 */
export function usePersistedVectorLayers({ backend, context, document, language }: UsePersistedVectorLayersProps): UsePersistedVectorLayersResult {
  const [state, setState] = useState<UsePersistedVectorLayersResult>(EMPTY_RESULT);
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
    let discoveredIssues: PersistedVectorLayerIssue[] = [];
    void (async () => {
      if (!context.projectPath || !context.projectRevision?.trim()) {
        throw new Error('请先打开具有有效版本信息的已保存工程，再读取地图矢量图层。');
      }
      if (!document.visualizationRevision?.trim()) {
        throw new Error('当前地图没有可校验的 .vis 文件版本，无法安全读取矢量来源。');
      }
      const sourceSnapshotSHA = await projectXmlSha(context.projectXml);
      if (!current()) return;
      const { candidates, issues } = collectVectorNodes(document.roots);
      discoveredIssues = issues;
      if (candidates.length === 0) {
        if (current()) setState({ layers: [], captures: [], issues, loading: false, error: null });
        return;
      }
      if (!backend.supports('map.vector.source.load')) {
        throw new BackendOperationUnavailableError('map.vector.source.load');
      }
      const map = {
        projectPath: context.projectPath,
        projectXml: context.projectXml,
        projectXmlSHA: sourceSnapshotSHA,
        mapID: document.mapID,
        mapIndex: document.mapIndex,
        mapName: document.name,
        mapType: document.mapType,
        expectedVisualizationRevision: document.visualizationRevision,
        projectSessionID: context.projectSessionID,
      };
      const layers: GeoJsonLayer[] = [];
      const captures: PersistedVectorLayerCapture[] = [];
      for (const candidate of candidates) {
        if (!current()) return;
        const raw = await backend.request<unknown>('map.vector.source.load', {
          map,
          node: candidate.locator,
          expectedFamily: null,
        }, { signal: controller.signal });
        if (!current()) return;
        const result = parseSourceLoad(raw);
        verifySourceIdentity(result, context, document, sourceSnapshotSHA, candidate);
        const loaded = layerFromSource(result, document, candidate);
        layers.push(loaded.layer);
        captures.push(loaded.capture);
      }
      if (current()) setState({ layers, captures, issues, loading: false, error: null });
    })().catch((error: unknown) => {
      if (!current()) return;
      setState({
        layers: [],
        captures: [],
        issues: discoveredIssues,
        loading: false,
        error: makeError(error, language),
      });
    });

    return () => {
      controller.abort();
      requestGeneration.current += 1;
    };
  }, [backend, backendRevision, context.projectPath, context.projectRevision, context.projectSessionID, context.projectXml, document, language]);

  return state;
}
