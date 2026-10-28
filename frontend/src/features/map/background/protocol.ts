import type { BackendGateway, WorkbenchContext } from '../../../shared/workbench';
import { isAbsoluteServerPath } from '../../project/ProjectTypes';
import type { BackgroundMapsVMState } from '../BackgroundMapsVM';
import type { MapDocumentTab } from '../mapTypes';
import type { MapViewport, ViewSize } from '../mapViewport';
import type {
  BackgroundMapCoordinate, BackgroundMapFeature, BackgroundMapLabel, BackgroundMapProjectionRead,
  BackgroundMapRaster, BackgroundMapRenderRequest, BackgroundMapRenderResult, BackgroundMapSettings,
  BackgroundMapSourceID, BackgroundMapSourceStatus, BackgroundMapViewport,
} from './types';

const MAXIMUM_PIXELS = 2_097_152;
const MAXIMUM_SIDE = 2048;
const sourceIDs = ['shadedRelief', 'satellite', 'osm'] as const;
const revisionPattern = /^[\da-f]{64}$/i;

function record(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}
function finite(value: unknown): value is number { return typeof value === 'number' && Number.isFinite(value); }
function nonnegative(value: unknown): value is number { return finite(value) && value >= 0; }
function opacity(value: unknown): value is number { return finite(value) && value >= 0 && value <= 1; }
function nullableString(value: unknown): value is string | null { return value === null || typeof value === 'string'; }
function identity(value: unknown): value is string { return typeof value === 'string' && value.trim().length > 0; }
function coordinate(value: unknown): value is BackgroundMapCoordinate { return record(value) && finite(value.x) && finite(value.y); }
function sameNumber(left: number, right: number): boolean {
  return Math.abs(left - right) <= Number.EPSILON * 16 * Math.max(1, Math.abs(left), Math.abs(right));
}

/** Matches Project.LoadXML: a Project root, or its direct Project child. No descendant CRS guessing. */
export function readBackgroundMapProjection(xml: string | null): BackgroundMapProjectionRead {
  if (!xml?.trim()) return { projection: null, issue: 'missing' };
  try {
    // A project definition never needs a DTD; reject it instead of interpreting custom entities.
    if (/<!DOCTYPE\b/i.test(xml)) return { projection: null, issue: 'invalid' };
    const parsed = new DOMParser().parseFromString(xml, 'application/xml');
    if (!parsed.documentElement || parsed.getElementsByTagName('parsererror').length)
      return { projection: null, issue: 'invalid' };
    const root = parsed.documentElement;
    const roots = root.tagName.toLowerCase() === 'project' ? [root]
      : Array.from(root.children).filter(child => child.tagName.toLowerCase() === 'project');
    if (roots.length !== 1) return { projection: null, issue: 'invalid' };
    const nodes = Array.from(roots[0].children).filter(child => child.tagName.toLowerCase() === 'projection');
    if (nodes.length === 0) return { projection: null, issue: 'missing' };
    if (nodes.length !== 1 || nodes[0].children.length) return { projection: null, issue: 'invalid' };
    const rawType = nodes[0].getAttribute('Type')?.trim().toLowerCase();
    if (rawType === 'local' || rawType === 'none') return { projection: null, issue: 'local' };
    const type = ({ epsg: 'EPSG', proj: 'Proj', esri: 'ESRI', wkt: 'WKT' } as const)[rawType as 'epsg'];
    if (!type) return { projection: null, issue: 'unknown' };
    const value = nodes[0].textContent ?? '';
    if (!value.trim() || value.length > 4 * 1024 * 1024) return { projection: null, issue: 'invalid' };
    return { projection: { type, value }, issue: null };
  } catch {
    return { projection: null, issue: 'invalid' };
  }
}

export function backgroundMapSettings(state: BackgroundMapsVMState): BackgroundMapSettings {
  const result: BackgroundMapSettings = {
    areBackgroundMapsOn: state.AreBackgroundMapsOn, isOpenStreetMapOn: state.IsOpenStreetMapOn,
    isSatelliteImageryOn: state.IsSatelliteImageryOn, isShadedReliefOn: state.IsShadedReliefOn,
    osmStyle: state.OsmStyle, density: state.Density, showLabels: state.ShowLabels,
    satelliteOpacity: state.SatelliteMapOpacity / 255,
  };
  if (![result.areBackgroundMapsOn, result.isOpenStreetMapOn, result.isSatelliteImageryOn,
    result.isShadedReliefOn, result.showLabels].every(value => typeof value === 'boolean') ||
    !Number.isInteger(state.SatelliteMapOpacity) || !opacity(result.satelliteOpacity) ||
    !Number.isInteger(result.density) || result.density < -1 || result.density > 5 ||
    typeof result.osmStyle !== 'string' || !/^[A-Za-z0-9_-]{1,128}$/.test(result.osmStyle))
    throw new Error('底图设置无效，请重新选择来源、样式和透明度。');
  return result;
}

export function backgroundMapSourceEnabled(id: BackgroundMapSourceID, settings: BackgroundMapSettings): boolean {
  return settings.areBackgroundMapsOn && (id === 'osm' ? settings.isOpenStreetMapOn
    : id === 'satellite' ? settings.isSatelliteImageryOn : settings.isShadedReliefOn);
}

export function backgroundMapHasSource(settings: BackgroundMapSettings): boolean {
  return sourceIDs.some(id => backgroundMapSourceEnabled(id, settings));
}

export function backgroundMapHasProject(context: WorkbenchContext, document: MapDocumentTab | null): boolean {
  return Boolean(context.projectPath && isAbsoluteServerPath(context.projectPath) && context.projectSessionID.trim() &&
    context.projectXml?.trim() && context.projectRevision && revisionPattern.test(context.projectRevision) &&
    document?.mapID.trim() && !document.mapID.startsWith('local:') && Number.isInteger(document.mapIndex) &&
    document.mapIndex >= 0 && document.visualizationRevision && revisionPattern.test(document.visualizationRevision));
}

/** Exact XML is part of the key: an unsaved CRS edit must retire the accepted frame immediately. */
export function backgroundMapSourceKey(context: WorkbenchContext, document: MapDocumentTab | null,
  settings: BackgroundMapSettings | null, coordinatesAreProject: boolean, backendRevision: number): string {
  return JSON.stringify([context.projectSessionID, context.projectPath, context.projectRevision, context.projectXml,
    document?.id, document?.mapID, document?.mapIndex, document?.name, document?.mapType,
    document?.visualizationRevision, settings, coordinatesAreProject, backendRevision]);
}

/** Keep logical CSS-pixel scale for OSM styles even when raster target dimensions are reduced. */
export function backgroundMapViewportFromCamera(camera: MapViewport, size: ViewSize): BackgroundMapViewport {
  if (!finite(camera.scale) || camera.scale <= 0 || !finite(camera.offsetX) || !finite(camera.offsetY) ||
    !finite(size.width) || !finite(size.height) || size.width <= 0 || size.height <= 0)
    throw new Error('当前地图窗口或缩放比例无效，无法绘制底图。');
  const scale = Math.min(1, MAXIMUM_SIDE / Math.max(size.width, size.height),
    Math.sqrt(MAXIMUM_PIXELS / (size.width * size.height)));
  const viewport = {
    minX: -camera.offsetX / camera.scale, minY: (camera.offsetY - size.height) / camera.scale,
    maxX: (size.width - camera.offsetX) / camera.scale, maxY: camera.offsetY / camera.scale,
    pixelWidth: Math.max(1, Math.floor(size.width * scale)), pixelHeight: Math.max(1, Math.floor(size.height * scale)),
  };
  if (!validViewport(viewport)) throw new Error('地图窗口超出底图可表示的坐标范围。');
  return viewport;
}

function validViewport(value: unknown): value is BackgroundMapViewport {
  return record(value) && finite(value.minX) && finite(value.minY) && finite(value.maxX) && finite(value.maxY) &&
    value.maxX > value.minX && value.maxY > value.minY &&
    Number.isInteger(value.pixelWidth) && Number.isInteger(value.pixelHeight) &&
    (value.pixelWidth as number) >= 1 && (value.pixelWidth as number) <= MAXIMUM_SIDE &&
    (value.pixelHeight as number) >= 1 && (value.pixelHeight as number) <= MAXIMUM_SIDE &&
    (value.pixelWidth as number) * (value.pixelHeight as number) <= MAXIMUM_PIXELS;
}

/**
 * BackgroundMapService constructs RasterDefinition(extent, rows, cols).
 * That constructor preserves the upper-left origin and uses square cells:
 * max(width / cols, height / rows). Integer target dimensions can therefore
 * extend the right or bottom edge. The echoed viewport remains unchanged.
 */
export function backgroundMapRasterGrid(viewport: BackgroundMapViewport) {
  if (!validViewport(viewport)) throw new Error('底图请求的栅格网格尺寸无效。');
  const cellSize = Math.max((viewport.maxX - viewport.minX) / viewport.pixelWidth,
    (viewport.maxY - viewport.minY) / viewport.pixelHeight);
  const grid = { minX: viewport.minX, maxY: viewport.maxY,
    maxX: viewport.minX + viewport.pixelWidth * cellSize,
    minY: viewport.maxY - viewport.pixelHeight * cellSize, cellSize };
  if (!finite(cellSize) || cellSize <= 0 || !finite(grid.maxX) || !finite(grid.minY) ||
    grid.maxX <= grid.minX || grid.maxY <= grid.minY)
    throw new Error('底图请求的方形像元网格超出可表示的坐标范围。');
  return grid;
}

/** Host colors use ARGB; CSS eight-digit hex puts the alpha channel last. */
export function backgroundArgbToCss(value: unknown): string | null {
  if (value === null) return null;
  if (typeof value !== 'string' || !/^#[\da-f]{8}$/i.test(value))
    throw new Error('底图服务返回了无效的 ARGB 颜色。');
  return '#' + value.slice(3) + value.slice(1, 3);
}

export function backgroundColorVisible(value: string | null): boolean { return Boolean(value && !value.endsWith('00')); }

function parseSource(value: unknown, settings: BackgroundMapSettings): BackgroundMapSourceStatus {
  if (!record(value) || !sourceIDs.includes(value.id as BackgroundMapSourceID) ||
    !['available', 'disabled', 'outsideCoverage', 'failed'].includes(value.state as string) ||
    !nullableString(value.message) || !nullableString(value.attribution))
    throw new Error('底图服务返回了无效的来源状态。');
  const source = value as unknown as BackgroundMapSourceStatus;
  if (backgroundMapSourceEnabled(source.id, settings) === (source.state === 'disabled'))
    throw new Error('底图来源状态与当前启用的来源不一致。');
  return { ...source };
}

function parseRaster(value: unknown, viewport: BackgroundMapViewport): BackgroundMapRaster {
  const grid = backgroundMapRasterGrid(viewport);
  if (!record(value) || !['shadedRelief', 'satellite'].includes(value.id as string) || !identity(value.source) ||
    value.width !== viewport.pixelWidth || value.height !== viewport.pixelHeight ||
    !finite(value.minX) || !finite(value.minY) || !finite(value.maxX) || !finite(value.maxY) ||
    !sameNumber(value.minX, grid.minX) || !sameNumber(value.minY, grid.minY) ||
    !sameNumber(value.maxX, grid.maxX) || !sameNumber(value.maxY, grid.maxY) ||
    !opacity(value.opacity) || typeof value.rgba !== 'string' ||
    value.rgba.length !== Math.ceil(viewport.pixelWidth * viewport.pixelHeight * 4 / 3) * 4 ||
    /[^A-Za-z0-9+/=]/.test(value.rgba))
    throw new Error('底图服务返回的栅格尺寸、范围或像素格式无效。');
  if (atob(value.rgba).length !== viewport.pixelWidth * viewport.pixelHeight * 4)
    throw new Error('底图服务返回的 RGBA 字节数量与尺寸不一致。');
  return { ...value } as unknown as BackgroundMapRaster;
}

function points(value: unknown): value is BackgroundMapCoordinate[] { return Array.isArray(value) && value.every(coordinate); }

function parseFeature(value: unknown): BackgroundMapFeature {
  if (!record(value) || !identity(value.id) || !identity(value.sourceLayer) || !identity(value.styleLayer) ||
    !['LineString', 'Polygon'].includes(value.geometryType as string) || !Array.isArray(value.parts) || value.parts.length === 0 ||
    !value.parts.every(part => record(part) && points(part.points) && typeof part.isClosed === 'boolean' &&
      part.points.length >= (value.geometryType === 'Polygon' ? 3 : 2) && (value.geometryType !== 'Polygon' || part.isClosed)) ||
    !opacity(value.strokeOpacity) || !opacity(value.fillOpacity) || !nonnegative(value.strokeWidth) ||
    !nonnegative(value.lineWidth) || !nonnegative(value.lineGapWidth) || !Number.isInteger(value.zIndex) ||
    typeof value.isDashed !== 'boolean' || !Array.isArray(value.dash) || !value.dash.every(nonnegative) ||
    (value.isDashed && (value.dash.length === 0 || !value.dash.some(item => item > 0))) ||
    !points(value.clipPolygon) || value.clipPolygon.length < 3 || typeof value.isTranslated !== 'boolean' ||
    !finite(value.translationX) || !finite(value.translationY))
    throw new Error('底图服务返回了无效或尚不支持的 OSM 路径。');
  if (value.lineGapWidth > 0 && !sameNumber(value.strokeWidth, value.lineGapWidth + 2 * value.lineWidth))
    throw new Error('OSM 线宽与中间留白宽度不一致。');
  return { ...value, strokeColor: backgroundArgbToCss(value.strokeColor), fillColor: backgroundArgbToCss(value.fillColor) } as unknown as BackgroundMapFeature;
}

function parseLabel(value: unknown): BackgroundMapLabel {
  if (!record(value) || !identity(value.id) || !identity(value.text) ||
    (value.point !== null && !coordinate(value.point)) || !points(value.line) ||
    (value.point === null && value.line.length < 2) || !nullableString(value.fontFamily) ||
    !finite(value.fontSize) || value.fontSize <= 0 || !finite(value.rotation) || !nonnegative(value.haloWidth) ||
    !Number.isInteger(value.zIndex) || typeof value.bold !== 'boolean' || typeof value.italic !== 'boolean' ||
    !Number.isInteger(value.maximumWidth) || (value.maximumWidth as number) < 0)
    throw new Error('底图服务返回了无效的文字或标注位置。');
  return { ...value, textColor: backgroundArgbToCss(value.textColor), haloColor: backgroundArgbToCss(value.haloColor) } as unknown as BackgroundMapLabel;
}

export function parseBackgroundMapResult(value: unknown, request: BackgroundMapRenderRequest): BackgroundMapRenderResult {
  if (!record(value) || value.requestID !== request.requestID || value.mapID !== request.mapID ||
    value.projectSessionID !== request.projectSessionID || !validViewport(value.viewport) ||
    !(Object.keys(request.viewport) as Array<keyof BackgroundMapViewport>).every(key => value.viewport &&
      (value.viewport as BackgroundMapViewport)[key] === request.viewport[key]) ||
    !Array.isArray(value.rasters) || !Array.isArray(value.features) || !Array.isArray(value.labels) || !Array.isArray(value.sources))
    throw new Error('底图回包与当前请求、工程会话、地图或相机窗口不一致。');
  const sources = value.sources.map(source => parseSource(source, request.settings));
  if (sources.length !== sourceIDs.length || new Set(sources.map(source => source.id)).size !== sourceIDs.length)
    throw new Error('底图来源状态缺失或重复。');
  const rasters = value.rasters.map(raster => parseRaster(raster, request.viewport));
  if (new Set(rasters.map(raster => raster.id)).size !== rasters.length || rasters.some(raster =>
    sources.find(source => source.id === raster.id)?.state !== 'available') ||
    sources.some(source => source.id !== 'osm' && source.state === 'available' && !rasters.some(raster => raster.id === source.id)))
    throw new Error('底图栅格与来源成功状态不一致。');
  const features = value.features.map(parseFeature);
  const labels = value.labels.map(parseLabel);
  if (new Set(features.map(feature => feature.id)).size !== features.length || new Set(labels.map(label => label.id)).size !== labels.length)
    throw new Error('OSM 路径或标注标识重复。');
  const osmAvailable = sources.find(source => source.id === 'osm')?.state === 'available';
  if ((!osmAvailable && (features.length > 0 || labels.length > 0)) || (!request.settings.showLabels && labels.length > 0))
    throw new Error('OSM 图形或标注与当前来源状态不一致。');
  const color = backgroundArgbToCss(value.osmBackgroundColor ?? null);
  return { requestID: request.requestID, mapID: request.mapID, projectSessionID: request.projectSessionID,
    viewport: { ...request.viewport }, rasters, features, labels, sources, osmBackgroundColor: osmAvailable ? color : null };
}

/** The last guard is evaluated after asynchronous work, including gateways that ignore abort. */
export class BackgroundMapRequestGate {
  private generation = 0;
  begin(backend: BackendGateway, sourceKey: string, viewportKey: string) {
    return { generation: ++this.generation, backend, sourceKey, viewportKey };
  }
  invalidate(): void { this.generation++; }
  accepts(capture: { generation: number; backend: BackendGateway; sourceKey: string; viewportKey: string },
    current: { backend: BackendGateway; sourceKey: string; viewportKey: string }, signal: AbortSignal): boolean {
    return !signal.aborted && capture.generation === this.generation && capture.backend === current.backend &&
      capture.sourceKey === current.sourceKey && capture.viewportKey === current.viewportKey;
  }
}
