import { useEffect, useMemo, useRef, useState } from 'react';
import type { BackendGateway, Language, WorkbenchContext } from '../../../shared/workbench';
import { isShapefileFamilyCapture, sameSourcePath, type AttributeTableSourceFamilyCapture } from '../attributes/attributeTableTypes';
import { loadMapDocumentCatalog, projectXmlSha } from '../../map/ProjectTreeListener';
import type { CatalogTreeNode, CatalogTreeNodeLocator, MapDocumentTab } from '../../map/mapTypes';

const maximumSelectablePaths = 250;

export interface GeoprocessingPolylineInput {
  featureIdentity: string;
  pathIdentity: string;
  sourceFeatureIndex: number;
  pathIndex: number;
  coordinates: number[][];
  zValues?: string[];
  mValues?: string[];
}

export interface GeoprocessingPolygonInput {
  featureIdentity: string;
  pathIdentity: string;
  sourceFeatureIndex: number;
  pathIndex: number;
  coordinates: number[][];
  zValues?: string[];
  mValues?: string[];
  holes: number[][][];
  holeZValues: Array<string[] | null>;
  holeMValues: Array<string[] | null>;
}

export interface GeoprocessingVectorSourceCapture {
  owner: string;
  sourcePath: string;
  sourceRevision: string;
  sourceFamily: AttributeTableSourceFamilyCapture | null;
  sourceFamilyKind: 'geojson' | 'shapefile';
  targetProjectionWKT: string | null;
  projectionStatus: string;
  mapName: string;
  layerName: string;
  shapeType: 'Polyline' | 'Polygon';
  hasNullableOrdinates: boolean;
  lines: GeoprocessingPolylineInput[];
  polygons: GeoprocessingPolygonInput[];
}

export interface GeoprocessingSourceSelection {
  capture: GeoprocessingVectorSourceCapture | null;
  inputMode: 'All' | 'Selected';
  selectedPathIDs: string[];
}

type SourceCandidate = { node: CatalogTreeNode; locator: CatalogTreeNodeLocator; id: string };
type SourceFileRevision = { path: string; role: string; required: boolean; exists: boolean; revision: string | null };
type ParsedPart = {
  partOrdinal: number;
  polygonIndex: number | null;
  isHole: boolean | null;
  hasZ: boolean;
  hasM: boolean;
  coordinates: number[][];
  zValues?: string[];
  mValues?: string[];
  hasNullableOrdinates: boolean;
};

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value);
}

function isSha256(value: unknown): value is string {
  return typeof value === 'string' && /^[\da-f]{64}$/i.test(value);
}

function pathExtension(value: string | null | undefined): string {
  if (!value) return '';
  const path = value.split(/[?#]/, 1)[0] ?? '';
  const leaf = path.split(/[\\/]/).at(-1) ?? '';
  const separator = leaf.lastIndexOf('.');
  return separator < 0 ? '' : leaf.slice(separator).toLocaleLowerCase();
}

function collectCandidates(roots: CatalogTreeNode[], shapeType: 'Polyline' | 'Polygon'): SourceCandidate[] {
  const candidates: SourceCandidate[] = [];
  const expectedMapType = shapeType === 'Polyline' ? 'PolylinesVector' : 'PolygonsVector';
  const visit = (node: CatalogTreeNode): void => {
    const locator = node.locator;
    const extension = pathExtension(locator?.filename) || pathExtension(locator?.filenameRelative);
    const isGeoJson = extension === '.geojson' || extension === '.json';
    const isShapefile = extension === '.shp';
    // The map-vector reader returns explicit PolygonIndex/IsHole only for
    // GeoJSON. Shapefile polygon rings stay unavailable until the Host reports
    // their grouping rather than asking the browser to guess it.
    if (locator?.nodeType === 'MapLayer' && locator.mapType === expectedMapType &&
        (isGeoJson || (shapeType === 'Polyline' && isShapefile))) {
      candidates.push({ node, locator, id: node.id });
    }
    for (const child of node.children ?? []) visit(child);
  };
  roots.forEach(visit);
  return candidates;
}

function sameLocator(left: CatalogTreeNodeLocator, right: CatalogTreeNodeLocator): boolean {
  return left.rootName === right.rootName && left.nodeType === right.nodeType && left.nodeName === right.nodeName &&
    left.mapType === right.mapType && left.assetID === right.assetID && left.filename === right.filename &&
    left.filenameRelative === right.filenameRelative && JSON.stringify(left.nodePath ?? null) === JSON.stringify(right.nodePath ?? null);
}

function parsePart(rawPart: unknown): ParsedPart {
  if (!isRecord(rawPart) || !Number.isInteger(rawPart.partOrdinal) || (rawPart.partOrdinal as number) < 0 ||
      typeof rawPart.hasZ !== 'boolean' || typeof rawPart.hasM !== 'boolean' || !Array.isArray(rawPart.vertices)) {
    throw new Error('服务端返回的向量路径身份无效。');
  }
  const vertices = rawPart.vertices;
  const coordinates: number[][] = [];
  const zValues: string[] = [];
  const mValues: string[] = [];
  let hasNullableOrdinates = false;
  for (const rawVertex of vertices) {
    if (!isRecord(rawVertex) || typeof rawVertex.x !== 'number' || !Number.isFinite(rawVertex.x) ||
        typeof rawVertex.y !== 'number' || !Number.isFinite(rawVertex.y) ||
        typeof rawVertex.sourceX !== 'number' || !Number.isFinite(rawVertex.sourceX) ||
        typeof rawVertex.sourceY !== 'number' || !Number.isFinite(rawVertex.sourceY) ||
        !(rawVertex.z === null || (typeof rawVertex.z === 'number' && Number.isFinite(rawVertex.z))) ||
        !(rawVertex.m === null || (typeof rawVertex.m === 'number' && Number.isFinite(rawVertex.m)))) {
      throw new Error('服务端返回的向量顶点缺少有限的来源坐标。');
    }
    coordinates.push([rawVertex.x, rawVertex.y]);
    if (rawPart.hasZ) {
      if (rawVertex.z === null) hasNullableOrdinates = true;
      zValues.push(rawVertex.z === null ? 'NaN' : String(rawVertex.z));
    }
    if (rawPart.hasM) {
      if (rawVertex.m === null) hasNullableOrdinates = true;
      mValues.push(rawVertex.m === null ? 'NaN' : String(rawVertex.m));
    }
  }
  return {
    partOrdinal: rawPart.partOrdinal as number,
    polygonIndex: Number.isInteger(rawPart.polygonIndex) && (rawPart.polygonIndex as number) >= 0
      ? rawPart.polygonIndex as number : null,
    isHole: typeof rawPart.isHole === 'boolean' ? rawPart.isHole : null,
    hasZ: rawPart.hasZ,
    hasM: rawPart.hasM,
    coordinates,
    ...(rawPart.hasZ ? { zValues } : {}),
    ...(rawPart.hasM ? { mValues } : {}),
    hasNullableOrdinates,
  };
}

function featureIdentity(sourceID: unknown, family: 'geojson' | 'shapefile', index: number): string {
  if (family === 'geojson' && typeof sourceID === 'string' && sourceID.trim()) return `geojson-feature-id:${sourceID}`;
  return `${family}-feature:${index}`;
}

function parseSourceLoad(value: unknown, context: WorkbenchContext, document: MapDocumentTab,
  projectXmlSHA: string | null, candidate: SourceCandidate, layerName: string, owner: string,
  expectedShapeType: 'Polyline' | 'Polygon'): GeoprocessingVectorSourceCapture {
  const response = isRecord(value) ? value : null;
  const sourceValue = response?.source;
  const dataValue = response?.data;
  if (!response || !isRecord(sourceValue) || !isRecord(sourceValue.map) ||
      !isRecord(sourceValue.family) || !isRecord(dataValue)) {
    throw new Error('服务端没有返回完整的向量来源读取结果。');
  }
  const source = sourceValue as Record<string, unknown>;
  const map = source.map as Record<string, unknown>;
  const family = source.family as Record<string, unknown>;
  const data = dataValue as Record<string, unknown>;
  if (!isRecord(source.locator)) throw new Error('服务端返回的地图图层定位信息无效。');
  const sourceLocator = source.locator as unknown as CatalogTreeNodeLocator;
  const familyName = family.family;
  const familySourcePath = family.sourcePath;
  const familyRevision = family.revision;
  const familyFiles = family.files;
  const dataFeatures = data.features;
  const targetProjectionWKT = data.targetProjectionWKT;
  const shapeType = data.shapeType;
  if (typeof map.projectPath !== 'string' || !isSha256(map.projectRevision) ||
      !(map.projectXmlSHA === null || isSha256(map.projectXmlSHA)) ||
      !(map.visualizationRevision === null || isSha256(map.visualizationRevision)) ||
      !(map.mapID === null || typeof map.mapID === 'string') ||
      !(map.mapIndex === null || (Number.isInteger(map.mapIndex) && (map.mapIndex as number) >= 0)) ||
      !(map.mapName === null || typeof map.mapName === 'string') || typeof map.mapType !== 'string' ||
      map.isPersisted !== true || typeof familyName !== 'string' || typeof familySourcePath !== 'string' ||
      !familySourcePath.trim() || !isSha256(familyRevision) || !Array.isArray(familyFiles) ||
      shapeType !== expectedShapeType || !Number.isInteger(data.count) || !Array.isArray(dataFeatures) ||
      data.count !== dataFeatures.length || typeof data.projectionStatus !== 'string' ||
      !(targetProjectionWKT === null || typeof targetProjectionWKT === 'string')) {
    throw new Error('服务端返回的工程、图层或向量来源身份无效。');
  }
  if (!context.projectPath || !context.projectRevision || map.projectPath !== context.projectPath ||
      (map.projectRevision as string).toUpperCase() !== context.projectRevision.toUpperCase() ||
      (map.projectXmlSHA as string | null)?.toUpperCase() !== projectXmlSHA?.toUpperCase() ||
      (map.visualizationRevision as string | null)?.toUpperCase() !== document.visualizationRevision?.toUpperCase() ||
      map.mapID !== document.mapID || map.mapIndex !== document.mapIndex || map.mapName !== document.name ||
      map.mapType !== document.mapType || !sameLocator(sourceLocator, candidate.locator)) {
    throw new Error('读取来源期间工程、地图、可视化文件或图层身份发生变化；请重新读取地图目录。');
  }
  const extension = pathExtension(familySourcePath);
  if (familyName === 'geojson' && extension !== '.geojson' && extension !== '.json') {
    throw new Error('GeoJSON 来源回执与实际文件路径不一致。');
  }
  if (familyName === 'shapefile' && extension !== '.shp') {
    throw new Error('Shapefile 来源回执与实际文件路径不一致。');
  }
  if (familyName !== 'geojson' && familyName !== 'shapefile') {
    throw new Error('当前地理处理只接收 Host 已读取的 GeoJSON 或 Shapefile 向量来源。');
  }
  const sourceFiles = familyFiles.filter((file): file is SourceFileRevision => isRecord(file) &&
    file.role === 'source' && file.required === true && file.exists === true && isSha256(file.revision) &&
    typeof file.path === 'string' && typeof file.inputPath === 'string');
  if (familyName === 'geojson') {
    if (familyFiles.length !== 1 || sourceFiles.length !== 1 || !sameSourcePath(sourceFiles[0]!.path, familySourcePath)) {
      throw new Error('GeoJSON 来源回执没有提供唯一且一致的主文件 SHA-256。');
    }
  } else if (sourceFiles.length !== 1 || !isShapefileFamilyCapture(family, familySourcePath, sourceFiles[0]?.revision ?? '')) {
    throw new Error('Host 未返回完整且匹配的 Shapefile 文件族；不能把单个 .shp 文件当作完整来源。');
  }
  if (typeof targetProjectionWKT === 'string' && !targetProjectionWKT.trim()) {
    throw new Error('工程投影回执为空，无法确认向量坐标空间。');
  }
  if (data.projectionStatus === 'projected' && typeof targetProjectionWKT !== 'string') {
    throw new Error('服务端没有返回已投影向量对应的工程 WKT。');
  }
  if (data.projectionStatus !== 'projected' && data.projectionStatus !== 'localProjectSourceCoordinates') {
    throw new Error('服务端返回了不支持的向量坐标空间。');
  }

  const lines: GeoprocessingPolylineInput[] = [];
  const polygons: GeoprocessingPolygonInput[] = [];
  let hasNullableOrdinates = false;
  const seenPathIDs = new Set<string>();
  for (const rawFeature of dataFeatures) {
    if (!isRecord(rawFeature) || !Number.isInteger(rawFeature.sourceOrdinal) || (rawFeature.sourceOrdinal as number) < 0 ||
        !(rawFeature.sourceID === null || typeof rawFeature.sourceID === 'string') ||
        rawFeature.shapeType !== expectedShapeType || !Array.isArray(rawFeature.parts)) {
      throw new Error('服务端返回的向量要素身份或几何类型无效。');
    }
    const featureIndex = rawFeature.sourceOrdinal as number;
    const featureID = featureIdentity(rawFeature.sourceID, familyName, featureIndex);
    const parts = rawFeature.parts.map(parsePart).sort((left, right) => left.partOrdinal - right.partOrdinal);
    if (parts.some((part, index) => index > 0 && parts[index - 1]!.partOrdinal === part.partOrdinal)) {
      throw new Error('服务端返回了重复的来源路径序号。');
    }
    parts.forEach(part => { hasNullableOrdinates ||= part.hasNullableOrdinates; });
    if (expectedShapeType === 'Polyline') {
      for (const part of parts) {
        if (part.coordinates.length < 2) throw new Error('来源包含少于两个顶点的折线路径，未提交不完整输入。');
        const pathIdentity = `${familyName}-path:${featureIndex}:${part.partOrdinal}`;
        if (seenPathIDs.has(pathIdentity)) throw new Error('服务端返回了重复的折线路径身份。');
        seenPathIDs.add(pathIdentity);
        lines.push({ featureIdentity: featureID, pathIdentity, sourceFeatureIndex: featureIndex,
          pathIndex: part.partOrdinal, coordinates: part.coordinates,
          ...(part.zValues ? { zValues: part.zValues } : {}), ...(part.mValues ? { mValues: part.mValues } : {}) });
      }
      continue;
    }

    if (familyName !== 'geojson') {
      throw new Error('Host 当前未返回 Shapefile 多边形外环与洞环分组；浏览器不会推测环的拓扑。');
    }
    const groups = new Map<number, ParsedPart[]>();
    for (const part of parts) {
      if (part.polygonIndex === null || part.isHole === null || part.coordinates.length < 4 ||
          part.coordinates[0]![0] !== part.coordinates[part.coordinates.length - 1]![0] ||
          part.coordinates[0]![1] !== part.coordinates[part.coordinates.length - 1]![1]) {
        throw new Error('GeoJSON 多边形环缺少 Host 提供的 PolygonIndex/IsHole，或环未闭合。');
      }
      const group = groups.get(part.polygonIndex) ?? [];
      group.push(part);
      groups.set(part.polygonIndex, group);
    }
    for (const [polygonIndex, ringParts] of [...groups.entries()].sort(([left], [right]) => left - right)) {
      const outerRings = ringParts.filter(part => !part.isHole);
      if (outerRings.length !== 1) throw new Error('Host 返回的每个多边形组件必须恰有一个外环。');
      const outer = outerRings[0]!;
      const holes = ringParts.filter(part => part.isHole).sort((left, right) => left.partOrdinal - right.partOrdinal);
      const pathIdentity = `geojson-polygon:${featureIndex}:${polygonIndex}`;
      if (seenPathIDs.has(pathIdentity)) throw new Error('服务端返回了重复的多边形组件身份。');
      seenPathIDs.add(pathIdentity);
      polygons.push({ featureIdentity: featureID, pathIdentity, sourceFeatureIndex: featureIndex,
        pathIndex: polygonIndex, coordinates: outer.coordinates,
        ...(outer.zValues ? { zValues: outer.zValues } : {}), ...(outer.mValues ? { mValues: outer.mValues } : {}),
        holes: holes.map(part => part.coordinates),
        holeZValues: holes.map(part => part.zValues ?? null),
        holeMValues: holes.map(part => part.mValues ?? null) });
    }
  }
  const pathCount = expectedShapeType === 'Polyline' ? lines.length : polygons.length;
  if (pathCount === 0 || seenPathIDs.size !== pathCount) {
    throw new Error(`已保存地图中没有可执行的 GeoJSON/Shapefile ${expectedShapeType === 'Polyline' ? '折线路径' : '多边形组件'}。`);
  }
  return {
    owner, sourcePath: familySourcePath, sourceRevision: sourceFiles[0]!.revision!,
    sourceFamily: familyName === 'shapefile' ? family as unknown as AttributeTableSourceFamilyCapture : null,
    sourceFamilyKind: familyName, targetProjectionWKT: targetProjectionWKT as string | null,
    projectionStatus: data.projectionStatus, mapName: document.name, layerName,
    shapeType: expectedShapeType, hasNullableOrdinates, lines, polygons,
  };
}

export function GeoprocessingVectorSourcePicker({ language, context, backend, owner, geometryKind, selection, onSelectionChange }:
  { language: Language; context: WorkbenchContext; backend: BackendGateway; owner: string;
    geometryKind: 'Polyline' | 'Polygon'; selection: GeoprocessingSourceSelection;
    onSelectionChange: (next: GeoprocessingSourceSelection) => void }) {
  const zh = language === 'zh';
  const [catalog, setCatalog] = useState<Awaited<ReturnType<typeof loadMapDocumentCatalog>> | null>(null);
  const [selectedMapID, setSelectedMapID] = useState('');
  const [selectedLayerID, setSelectedLayerID] = useState('');
  const [loadingMaps, setLoadingMaps] = useState(false);
  const [loadingSource, setLoadingSource] = useState(false);
  const [error, setError] = useState<string>();
  const [diagnostic, setDiagnostic] = useState<string>();
  const mapGeneration = useRef(0);
  const sourceGeneration = useRef(0);
  const mapRequest = useRef<AbortController | null>(null);
  const sourceRequest = useRef<AbortController | null>(null);
  const ownerRef = useRef(owner); ownerRef.current = owner;
  const currentDocument = catalog?.documents.find(document => document.id === selectedMapID);
  const candidates = useMemo(() => currentDocument ? collectCandidates(currentDocument.roots, geometryKind) : [], [currentDocument, geometryKind]);
  const selectedCandidate = candidates.find(candidate => candidate.id === selectedLayerID);

  useEffect(() => {
    mapGeneration.current += 1;
    sourceGeneration.current += 1;
    mapRequest.current?.abort(); mapRequest.current = null;
    sourceRequest.current?.abort(); sourceRequest.current = null;
    setCatalog(null); setSelectedMapID(''); setSelectedLayerID(''); setLoadingMaps(false); setLoadingSource(false);
    setError(undefined); setDiagnostic(undefined);
    onSelectionChange({ capture: null, inputMode: 'All', selectedPathIDs: [] });
  }, [owner, onSelectionChange]);
  useEffect(() => {
    sourceGeneration.current += 1;
    sourceRequest.current?.abort(); sourceRequest.current = null;
    setLoadingSource(false); setError(undefined); setDiagnostic(undefined);
    onSelectionChange({ capture: null, inputMode: 'All', selectedPathIDs: [] });
  }, [geometryKind, onSelectionChange]);
  useEffect(() => () => {
    mapRequest.current?.abort(); sourceRequest.current?.abort();
  }, []);
  useEffect(() => {
    if (candidates.length === 0) { setSelectedLayerID(''); return; }
    if (!candidates.some(candidate => candidate.id === selectedLayerID)) setSelectedLayerID(candidates[0]!.id);
  }, [candidates, selectedLayerID]);

  const isCurrent = (generation: number, expectedOwner: string, signal: AbortSignal): boolean =>
    generation === mapGeneration.current && ownerRef.current === expectedOwner && !signal.aborted;
  const loadMaps = async (): Promise<void> => {
    mapRequest.current?.abort();
    sourceRequest.current?.abort(); sourceRequest.current = null;
    onSelectionChange({ capture: null, inputMode: 'All', selectedPathIDs: [] });
    setCatalog(null); setSelectedMapID(''); setSelectedLayerID(''); setError(undefined); setDiagnostic(undefined);
    if (!context.projectPath || !context.projectXml || !context.projectRevision) {
      setError(zh ? '请先打开已有工程，再读取其已保存地图。' : 'Open a saved project before loading its saved maps.');
      return;
    }
    if (!backend.supports('map.document.list') || !backend.supports('map.tree.load') || !backend.supports('map.vector.source.load')) {
      setError(zh ? '当前服务未开放已保存地图目录或向量读取。' : 'The current service does not provide the saved-map catalog or vector reader.');
      return;
    }
    const controller = new AbortController(); mapRequest.current = controller;
    const expectedOwner = owner; const generation = ++mapGeneration.current; setLoadingMaps(true);
    try {
      const loaded = await loadMapDocumentCatalog(backend, context, { signal: controller.signal });
      if (!isCurrent(generation, expectedOwner, controller.signal)) return;
      if (loaded.projectRevision.toUpperCase() !== context.projectRevision.toUpperCase()) {
        throw new Error('工程修订在读取地图目录期间发生变化。');
      }
      setCatalog(loaded);
      const active = loaded.documents.find(document => document.mapIndex === loaded.activeMapIndex) ?? loaded.documents[0];
      setSelectedMapID(active?.id ?? '');
      if (!loaded.documents.length) setError(zh ? '当前工程没有已保存地图。' : 'The project has no saved maps.');
      else if (!loaded.documents.some(document => collectCandidates(document.roots, geometryKind).length > 0)) {
        setError(geometryKind === 'Polygon'
          ? (zh ? '没有可读的 GeoJSON 多边形图层。Shapefile 多边形缺少可核验的外环/洞环分组，暂不可用于此工具。' : 'No readable GeoJSON polygon layer is available. Shapefile polygon ring grouping is not reported by the service and remains unavailable.')
          : (zh ? '已保存地图中没有可读的 GeoJSON/Shapefile 折线图层。' : 'No readable GeoJSON or Shapefile polyline layer is available.'));
      }
    } catch (caught) {
      if (controller.signal.aborted || ownerRef.current !== expectedOwner) return;
      setError(zh ? '读取已保存地图失败。' : 'Could not load the saved maps.');
      setDiagnostic(caught instanceof Error ? caught.message : String(caught));
    } finally {
      if (generation === mapGeneration.current && ownerRef.current === expectedOwner) {
        setLoadingMaps(false);
        if (mapRequest.current === controller) mapRequest.current = null;
      }
    }
  };

  const changeMap = (id: string): void => {
    setSelectedMapID(id); setSelectedLayerID(''); setError(undefined); setDiagnostic(undefined);
    sourceRequest.current?.abort(); sourceRequest.current = null;
    onSelectionChange({ capture: null, inputMode: 'All', selectedPathIDs: [] });
  };

  const loadSource = async (): Promise<void> => {
    if (!selectedCandidate || !currentDocument || !context.projectPath || !context.projectXml || !context.projectRevision) return;
    sourceRequest.current?.abort();
    onSelectionChange({ capture: null, inputMode: 'All', selectedPathIDs: [] });
    setError(undefined); setDiagnostic(undefined);
    const controller = new AbortController(); sourceRequest.current = controller;
    const expectedOwner = owner; const generation = ++sourceGeneration.current; setLoadingSource(true);
    try {
      const xmlSHA = await projectXmlSha(context.projectXml);
      if (controller.signal.aborted || ownerRef.current !== expectedOwner) return;
      const map = {
        projectPath: context.projectPath, projectXml: context.projectXml, projectXmlSHA: xmlSHA,
        mapID: currentDocument.mapID, mapIndex: currentDocument.mapIndex, mapName: currentDocument.name,
        mapType: currentDocument.mapType, expectedVisualizationRevision: currentDocument.visualizationRevision,
        projectSessionID: context.projectSessionID,
      };
      const value = await backend.request<unknown>('map.vector.source.load', {
        map, node: selectedCandidate.locator, expectedFamily: null,
      }, { signal: controller.signal });
      if (controller.signal.aborted || ownerRef.current !== expectedOwner || generation !== sourceGeneration.current) return;
      const capture = parseSourceLoad(value, context, currentDocument, xmlSHA, selectedCandidate,
        selectedCandidate.node.name, expectedOwner, geometryKind);
      onSelectionChange({ capture, inputMode: 'All', selectedPathIDs: [] });
    } catch (caught) {
      if (controller.signal.aborted || ownerRef.current !== expectedOwner) return;
      setError(zh ? `未能读取并核验所选${geometryKind === 'Polygon' ? '多边形' : '折线'}。` : `The selected ${geometryKind.toLowerCase()} source could not be read and verified.`);
      setDiagnostic(caught instanceof Error ? caught.message : String(caught));
    } finally {
      if (generation === sourceGeneration.current && ownerRef.current === expectedOwner) {
        setLoadingSource(false);
        if (sourceRequest.current === controller) sourceRequest.current = null;
      }
    }
  };

  const pathIDs = selection.capture?.shapeType === 'Polyline'
    ? selection.capture.lines.map(line => line.pathIdentity)
    : selection.capture?.shapeType === 'Polygon' ? selection.capture.polygons.map(polygon => polygon.pathIdentity) : [];
  const selectAll = (checked: boolean): void => {
    if (!selection.capture) return;
    onSelectionChange({ ...selection, selectedPathIDs: checked ? pathIDs : [] });
  };
  const togglePath = (pathIdentity: string, checked: boolean): void => {
    const ids = new Set(selection.selectedPathIDs);
    if (checked) ids.add(pathIdentity); else ids.delete(pathIdentity);
    onSelectionChange({ ...selection, selectedPathIDs: [...ids] });
  };
  const count = selection.capture?.shapeType === 'Polyline' ? selection.capture.lines.length : selection.capture?.polygons.length ?? 0;

  return <section className="tools-input-details gp-source-picker">
    <div className="tools-subheading-row"><h4>{zh ? (geometryKind === 'Polygon' ? '多边形来源' : '折线来源') : `${geometryKind} source`}</h4>
      <span className="tools-badge">{zh ? '已保存地图' : 'Saved map'}</span></div>
    <p className="tools-field-help">{zh
      ? geometryKind === 'Polygon'
        ? '从当前工程的已保存地图中选择 GeoJSON 多边形。外环与洞环只按服务返回的 PolygonIndex/IsHole 分组；Shapefile 多边形在服务提供分组信息前保持禁用。'
        : '从当前工程已保存地图选择 GeoJSON 或 Shapefile 折线；Shapefile 必须捕获完整文件族，并由服务返回目标工程投影后才能执行。'
      : geometryKind === 'Polygon'
        ? 'Choose a saved GeoJSON polygon. Outer and hole rings are grouped only from the service PolygonIndex/IsHole fields; Shapefile polygons remain unavailable until the service reports their grouping.'
        : 'Choose a saved GeoJSON or Shapefile polyline. Shapefiles require a complete family capture and a service-reported target projection.'}</p>
    <div className="gp-source-picker-actions">
      <button className="tools-button tools-button-secondary" type="button" onClick={() => void loadMaps()} disabled={loadingMaps || loadingSource || !context.projectPath}>
        {loadingMaps ? (zh ? '读取地图中…' : 'Loading maps…') : (zh ? '读取已保存地图' : 'Load saved maps')}
      </button>
      {loadingMaps && <button className="tools-button tools-button-secondary" type="button" onClick={() => mapRequest.current?.abort()}>{zh ? '取消读取' : 'Cancel load'}</button>}
    </div>
    {catalog && catalog.documents.length > 0 && <div className="gp-source-picker-grid">
      <label className="tools-field"><span>{zh ? '地图' : 'Map'}</span><select value={selectedMapID} onChange={event => changeMap(event.currentTarget.value)} disabled={loadingSource}>
        {catalog.documents.map(document => <option value={document.id} key={document.id}>{document.name} · {document.mapType}</option>)}
      </select></label>
      <label className="tools-field"><span>{zh ? (geometryKind === 'Polygon' ? 'GeoJSON 多边形图层' : '折线图层') : `${geometryKind} map layer`}</span><select value={selectedLayerID} onChange={event => { setSelectedLayerID(event.currentTarget.value); onSelectionChange({ capture: null, inputMode: 'All', selectedPathIDs: [] }); }} disabled={loadingSource || candidates.length === 0}>
        {candidates.length === 0 ? <option value="">{zh ? '当前地图没有可读图层' : 'No readable layer in this map'}</option>
          : candidates.map(candidate => <option value={candidate.id} key={candidate.id}>{candidate.node.name}</option>)}
      </select></label>
      <button className="tools-button tools-button-secondary" type="button" onClick={() => void loadSource()} disabled={loadingSource || loadingMaps || !selectedCandidate}>
        {loadingSource ? (zh ? '读取来源中…' : 'Reading source…') : (zh ? '读取所选来源' : 'Read selected source')}
      </button>
      {loadingSource && <button className="tools-button tools-button-secondary" type="button" onClick={() => { sourceGeneration.current += 1; sourceRequest.current?.abort(); sourceRequest.current = null; setLoadingSource(false); }}>{zh ? '取消读取' : 'Cancel read'}</button>}
    </div>}
    {selection.capture && <>
      <div className="tools-context-card gp-source-capture-card"><span className="tools-file-mark">{selection.capture.sourceFamilyKind === 'geojson' ? 'GEO' : 'SHP'}</span><div>
        <strong>{selection.capture.layerName}</strong><small>{selection.capture.mapName} · {count} {zh ? (geometryKind === 'Polygon' ? '个多边形组件' : '条折线路径') : (geometryKind === 'Polygon' ? 'polygon components' : 'polyline paths')}</small>
        <small>{selection.capture.sourcePath}</small><small>{zh ? '来源文件 SHA-256' : 'Source SHA-256'} · {selection.capture.sourceRevision}</small>
        <small>{selection.capture.targetProjectionWKT ? (zh ? '已读取工程投影 WKT' : 'Project projection WKT captured') : (zh ? '本地工程坐标' : 'Local project coordinates')}</small>
        {selection.capture.sourceFamily && <small>{zh ? '完整 Shapefile 文件族修订' : 'Shapefile family revision'} · {selection.capture.sourceFamily.revision}</small>}
      </div></div>
      {selection.capture.sourceFamilyKind === 'shapefile' && !selection.capture.targetProjectionWKT &&
        <p className="tools-inline-error" role="status">{zh ? '服务端没有返回目标工程投影 WKT；此 Shapefile 来源暂不能用于当前地理处理，执行已锁定。' : 'The service did not return a target project WKT. This Shapefile source cannot be used for geoprocessing, so execution is locked.'}</p>}
      {selection.capture.hasNullableOrdinates && geometryKind === 'Polygon' &&
        <p className="tools-inline-error" role="status">{zh ? '来源包含空值 Z/M 顶点；当前服务端多边形输入不支持无损表达这些空值，执行已锁定。' : 'The source contains null Z/M ordinates. The current service polygon contract cannot express them losslessly, so execution is locked.'}</p>}
      <label className="tools-field tools-mode-field"><span>{zh ? (geometryKind === 'Polygon' ? '多边形组件范围' : '折线路径范围') : (geometryKind === 'Polygon' ? 'Polygon components' : 'Polyline paths')}</span><select value={selection.inputMode} onChange={event => onSelectionChange({ ...selection, inputMode: event.currentTarget.value as 'All' | 'Selected' })}>
        <option value="All">{zh ? `全部（${count}）` : `All (${count})`}</option>
        <option value="Selected" disabled={count > maximumSelectablePaths}>
          {count > maximumSelectablePaths
            ? (zh ? `所选（超过交互选择上限 ${maximumSelectablePaths}）` : `Selected (over interactive limit ${maximumSelectablePaths})`)
            : (zh ? `所选（${selection.selectedPathIDs.length}）` : `Selected (${selection.selectedPathIDs.length})`)}
        </option>
      </select></label>
      {count > maximumSelectablePaths && <small className="tools-field-help">{zh ? '来源路径较多时仍可提交全部；为避免截断选择清单，不开放部分选择。' : 'All source paths remain available; partial selection is disabled to avoid truncating a large selection.'}</small>}
      {selection.inputMode === 'Selected' && count <= maximumSelectablePaths && <div className="gp-source-path-selection">
        <div className="gp-source-path-selection-header"><strong>{zh ? '选择实际输入几何' : 'Choose input geometry'}</strong>
          <button type="button" className="tools-text-button" onClick={() => selectAll(selection.selectedPathIDs.length !== count)}>
            {selection.selectedPathIDs.length === count ? (zh ? '清除全选' : 'Clear all') : (zh ? '全选' : 'Select all')}
          </button>
        </div>
        {(selection.capture.shapeType === 'Polyline' ? selection.capture.lines : selection.capture.polygons).map(item =>
          <label className="gp-source-path-option" key={item.pathIdentity}>
            <input type="checkbox" checked={selection.selectedPathIDs.includes(item.pathIdentity)} onChange={event => togglePath(item.pathIdentity, event.currentTarget.checked)} />
            <span>{item.featureIdentity} · {zh ? '路径' : 'path'} {item.pathIndex + 1} · {item.coordinates.length} {zh ? '个顶点' : 'vertices'}{selection.capture!.shapeType === 'Polygon' && 'holes' in item && Array.isArray(item.holes) ? ` · ${item.holes.length} ${zh ? '个洞环' : 'holes'}` : ''}</span>
          </label>)}
        {selection.selectedPathIDs.length === 0 && <small role="status">{zh ? `至少选择一个${geometryKind === 'Polygon' ? '多边形组件' : '折线路径'}。` : `Select at least one ${geometryKind.toLowerCase()} source.`}</small>}
      </div>}
    </>}
    {error && <div className="tools-inline-error" role="alert">{error}{diagnostic && <details><summary>{zh ? '来源读取诊断' : 'Source read diagnostic'}</summary><pre>{diagnostic}</pre></details>}</div>}
    {!error && !catalog && <p className="tools-muted">{zh ? '尚未读取当前工程的已保存地图。' : 'Saved maps have not been loaded for the active project.'}</p>}
  </section>;
}
