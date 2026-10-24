import type { GeoFeature, GeoGeometry, GeoJsonLayer, GeoLine, GeoPolygon, MapExtent, XY } from './mapTypes';

type JsonObject = Record<string, unknown>;

function objectValue(value: unknown): JsonObject | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? value as JsonObject : null;
}

function parsePosition(value: unknown): XY {
  if (!Array.isArray(value) || value.length < 2 || typeof value[0] !== 'number' || typeof value[1] !== 'number' || !Number.isFinite(value[0]) || !Number.isFinite(value[1])) {
    throw new Error('GeoJSON contains a coordinate that is not a finite x/y pair.');
  }
  return [value[0], value[1]];
}

function parseLine(value: unknown): GeoLine {
  if (!Array.isArray(value)) throw new Error('GeoJSON line coordinates must be an array.');
  return value.map(parsePosition);
}

function parsePolygon(value: unknown): GeoPolygon {
  if (!Array.isArray(value)) throw new Error('GeoJSON polygon coordinates must be an array of rings.');
  return value.map(parseLine);
}

export function parseGeometry(value: unknown): GeoGeometry {
  const geometry = objectValue(value);
  if (!geometry || typeof geometry.type !== 'string') throw new Error('GeoJSON feature has no supported geometry.');
  switch (geometry.type) {
    case 'Point': return { type: 'Point', coordinates: parsePosition(geometry.coordinates) };
    case 'MultiPoint':
      if (!Array.isArray(geometry.coordinates)) throw new Error('GeoJSON MultiPoint coordinates must be an array.');
      return { type: 'MultiPoint', coordinates: geometry.coordinates.map(parsePosition) };
    case 'LineString': return { type: 'LineString', coordinates: parseLine(geometry.coordinates) };
    case 'MultiLineString':
      if (!Array.isArray(geometry.coordinates)) throw new Error('GeoJSON MultiLineString coordinates must be an array.');
      return { type: 'MultiLineString', coordinates: geometry.coordinates.map(parseLine) };
    case 'Polygon': return { type: 'Polygon', coordinates: parsePolygon(geometry.coordinates) };
    case 'MultiPolygon':
      if (!Array.isArray(geometry.coordinates)) throw new Error('GeoJSON MultiPolygon coordinates must be an array.');
      return { type: 'MultiPolygon', coordinates: geometry.coordinates.map(parsePolygon) };
    case 'GeometryCollection':
      if (!Array.isArray(geometry.geometries)) throw new Error('GeoJSON GeometryCollection requires a geometries array.');
      return { type: 'GeometryCollection', geometries: geometry.geometries.map(parseGeometry) };
    default: throw new Error('Unsupported GeoJSON geometry: ' + geometry.type + '.');
  }
}

function readCrs(root: JsonObject): string {
  if (root.crs === undefined || root.crs === null) return 'EPSG:4326';
  const crs = objectValue(root.crs);
  const properties = crs ? objectValue(crs.properties) : null;
  const name = properties?.name;
  if (typeof name !== 'string' || !name.trim()) return 'Unknown CRS';
  const normalized = name.trim().toUpperCase().replaceAll(' ', '');
  if (normalized.includes('CRS84') || normalized.endsWith('EPSG::4326') || normalized.endsWith('EPSG:4326') || normalized.endsWith('/4326')) return 'EPSG:4326';
  if (normalized.endsWith('EPSG::3857') || normalized.endsWith('EPSG:3857') || normalized.endsWith('/3857')) return 'EPSG:3857';
  return name.trim();
}

function featureName(properties: Record<string, unknown>, fallback: string): string {
  for (const key of ['name', 'Name', 'title', 'Title', 'id', 'ID']) {
    const value = properties[key];
    if (typeof value === 'string' && value.trim()) return value.trim();
    if (typeof value === 'number' && Number.isFinite(value)) return String(value);
  }
  return fallback;
}

function normalizeFeatures(root: JsonObject, language: 'zh' | 'en'): GeoFeature[] {
  const entries: unknown[] = root.type === 'FeatureCollection'
    ? Array.isArray(root.features) ? root.features : []
    : [root.type === 'Feature' ? root : { type: 'Feature', geometry: root, properties: {} }];
  const result: GeoFeature[] = [];
  entries.forEach((entry, index) => {
    const feature = objectValue(entry);
    if (!feature || feature.type !== 'Feature') throw new Error('GeoJSON FeatureCollection contains a non-Feature item.');
    if (feature.geometry === null) return;
    const geometry = parseGeometry(feature.geometry);
    const properties = objectValue(feature.properties) ?? {};
    const rawID = feature.id;
    const id = typeof rawID === 'string' || typeof rawID === 'number' ? String(rawID) : 'feature-' + (index + 1);
    const fallbackName = language === 'zh' ? `要素 ${index + 1}` : `Feature ${index + 1}`;
    result.push({ id: id + '-' + index, name: featureName(properties, fallbackName), geometry, properties });
  });
  if (result.length === 0) throw new Error('GeoJSON does not contain any drawable features.');
  return result;
}

export function positionsInGeometry(geometry: GeoGeometry): XY[] {
  switch (geometry.type) {
    case 'Point': return [geometry.coordinates];
    case 'MultiPoint': return [...geometry.coordinates];
    case 'LineString': return [...geometry.coordinates];
    case 'MultiLineString': return geometry.coordinates.flatMap((line) => [...line]);
    case 'Polygon': return geometry.coordinates.flatMap((ring) => [...ring]);
    case 'MultiPolygon': return geometry.coordinates.flatMap((polygon) => polygon.flatMap((ring) => [...ring]));
    case 'GeometryCollection': return geometry.geometries.flatMap(positionsInGeometry);
  }
}

export function extentOfPositions(positions: readonly XY[]): MapExtent | null {
  if (positions.length === 0) return null;
  let minX = Infinity;
  let minY = Infinity;
  let maxX = -Infinity;
  let maxY = -Infinity;
  for (const [x, y] of positions) {
    if (!Number.isFinite(x) || !Number.isFinite(y)) continue;
    minX = Math.min(minX, x);
    minY = Math.min(minY, y);
    maxX = Math.max(maxX, x);
    maxY = Math.max(maxY, y);
  }
  return Number.isFinite(minX) ? { minX, minY, maxX, maxY } : null;
}

export function extentOfFeatures(features: readonly GeoFeature[]): MapExtent | null {
  return extentOfPositions(features.flatMap((feature) => positionsInGeometry(feature.geometry)));
}

export function unionExtents(extents: readonly MapExtent[]): MapExtent | null {
  if (extents.length === 0) return null;
  return extents.reduce<MapExtent>((result, extent) => ({
    minX: Math.min(result.minX, extent.minX),
    minY: Math.min(result.minY, extent.minY),
    maxX: Math.max(result.maxX, extent.maxX),
    maxY: Math.max(result.maxY, extent.maxY),
  }), { ...extents[0] });
}

export function parseGeoJsonFile(sourceName: string, text: string, language: 'zh' | 'en' = 'en'): GeoJsonLayer {
  let parsed: unknown;
  try {
    parsed = JSON.parse(text) as unknown;
  } catch (error) {
    throw new Error('Could not read GeoJSON: ' + (error instanceof Error ? error.message : String(error)));
  }
  const root = objectValue(parsed);
  if (!root || !['FeatureCollection', 'Feature', 'Point', 'MultiPoint', 'LineString', 'MultiLineString', 'Polygon', 'MultiPolygon', 'GeometryCollection'].includes(String(root.type))) {
    throw new Error('Choose a GeoJSON Feature, FeatureCollection, or Geometry document.');
  }
  const features = normalizeFeatures(root, language);
  const extent = extentOfFeatures(features);
  if (!extent) throw new Error('GeoJSON has no finite coordinates to display.');
  return {
    id: crypto.randomUUID(),
    name: sourceName.replace(/\.geojson$/i, '').replace(/\.json$/i, '') || (language === 'zh' ? 'GeoJSON 图层' : 'GeoJSON'),
    sourceName,
    crs: readCrs(root),
    features,
    extent,
    visible: true,
    opacity: 1,
  };
}

export function geometryToPathData(geometry: GeoGeometry, project: (point: XY) => XY): string[] {
  const linePath = (line: GeoLine, close: boolean): string => {
    const points = line.map(project);
    if (points.length === 0) return '';
    return points.map(([x, y], index) => (index === 0 ? 'M ' : 'L ') + x + ' ' + y).join(' ') + (close ? ' Z' : '');
  };
  switch (geometry.type) {
    case 'Point':
    case 'MultiPoint': return [];
    case 'LineString': return [linePath(geometry.coordinates, false)];
    case 'MultiLineString': return geometry.coordinates.map((line) => linePath(line, false));
    case 'Polygon': return [geometry.coordinates.map((ring) => linePath(ring, true)).filter(Boolean).join(' ')];
    case 'MultiPolygon': return [geometry.coordinates.map((polygon) => polygon.map((ring) => linePath(ring, true)).filter(Boolean).join(' ')).join(' ')];
    case 'GeometryCollection': return geometry.geometries.flatMap((child) => geometryToPathData(child, project));
  }
}

export function pointsInGeometry(geometry: GeoGeometry): XY[] {
  switch (geometry.type) {
    case 'Point': return [geometry.coordinates];
    case 'MultiPoint': return [...geometry.coordinates];
    case 'GeometryCollection': return geometry.geometries.flatMap(pointsInGeometry);
    default: return [];
  }
}

export function pathsInGeometry(geometry: GeoGeometry, project: (point: XY) => XY): Array<{ d: string; fill: boolean }> {
  switch (geometry.type) {
    case 'Point':
    case 'MultiPoint': return [];
    case 'LineString': return [{ d: geometryToPathData(geometry, project)[0] ?? '', fill: false }];
    case 'MultiLineString': return geometryToPathData(geometry, project).map((d) => ({ d, fill: false }));
    case 'Polygon':
    case 'MultiPolygon': return geometryToPathData(geometry, project).map((d) => ({ d, fill: true }));
    case 'GeometryCollection': return geometry.geometries.flatMap((child) => pathsInGeometry(child, project));
  }
}

export function isKnownCrs(crs: string): boolean {
  return crs === 'EPSG:4326' || crs === 'EPSG:3857';
}
