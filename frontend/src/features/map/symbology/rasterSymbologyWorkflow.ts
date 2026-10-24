import type { BackendGateway, WorkbenchContext } from '../../../shared/workbench';

export interface RasterMapSourceIdentity {
  id: string;
  projectPath: string;
  visualizationPath: string;
  visualizationRevision: string;
  mapIndex: number;
  mapName: string;
  mapType: string;
  nodePath: number[];
  nodeName: string;
  mapLayerType: string;
  filename: string | null;
  filenameRelative: string | null;
  sourcePath: string;
  sourceMapID: string | null;
  sourceRevision: string;
  gdbSource: unknown | null;
}

export interface RasterMapViewExtent {
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
  pixelWidth: number;
  pixelHeight: number;
}

export interface RasterSymbologyColorStop {
  id: string;
  color: string;
  value: number;
}

export type RasterSymbologyValueSource = 'Histogram' | 'Linear' | 'Manual';

export interface RasterHillshadeProperties {
  isEnabled: boolean;
  zFactor: number;
  azimuth: number;
  zenith: number;
}

export interface RasterContourProperties {
  isEnabled: boolean;
  autoInterval: boolean;
  interval: number;
  indexEvery: number;
  color: string;
  opacity: number;
  showLabels: boolean;
  density: number;
}

export interface RasterSymbologyDraft {
  paletteID: string | null;
  name: string;
  stops: RasterSymbologyColorStop[];
  valueSource: RasterSymbologyValueSource;
  grayscale: boolean;
  isEditing: boolean;
  updateHistogramWithView: boolean;
  canComputeHistogram: boolean;
  alpha: number;
  isSurfaceFillEnabled: boolean;
  smoothingRadius: number;
  resampleCellSizeRatio: number;
  hillshade: RasterHillshadeProperties;
  contours: RasterContourProperties;
}

export interface RasterSymbologyPalette {
  id: string;
  name: string;
  colors: string[];
  values: number[] | null;
  isCustom: boolean;
}

export interface RasterPlotOptionChoice { value: string; label: string }
export interface RasterPlotOptionDescriptor {
  propertyName: string;
  name: string;
  label: string;
  description: string;
  kind: 'bool' | 'enum';
  value: string;
  enumValues: RasterPlotOptionChoice[];
  isEnabled: boolean;
  enabledIf: string | null;
}

export interface RasterSymbologySnapshot {
  draft: RasterSymbologyDraft;
  palettes: RasterSymbologyPalette[];
  surfaceFillValues: number[] | null;
  plotOptions: RasterPlotOptionDescriptor[];
}

export interface RasterSymbologyInspectResult {
  previousVisualizationRevision: string;
  visualizationRevision: string;
  sourceRevision: string;
  snapshot: RasterSymbologySnapshot;
  raster: RasterSymbologyRasterPayload;
}

export interface RasterSymbologyRasterPayload {
  id: string;
  sourceMapID: string | null;
  name: string;
  width: number;
  height: number;
  rgba: string;
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
}

export interface RasterSymbologyApplyResult {
  previousVisualizationRevision: string;
  visualizationRevision: string;
  sourceRevision: string;
  raster: RasterSymbologyRasterPayload;
}

export interface RasterSymbologyPalettesResult {
  previousVisualizationRevision: string;
  visualizationRevision: string;
  sourceRevision: string;
  palettes: RasterSymbologyPalette[];
}

export interface RasterSymbologySnapshotResult {
  previousVisualizationRevision: string;
  visualizationRevision: string;
  sourceRevision: string;
  snapshot: RasterSymbologySnapshot;
}

export interface RasterSymbologyDraftResult { draft: RasterSymbologyDraft }

export const rasterSymbologyMethods = {
  inspect: 'rasterSymbology.inspect',
  histogram: 'rasterSymbology.histogram',
  selectPalette: 'rasterSymbology.selectPalette',
  apply: 'rasterSymbology.apply',
  savePalette: 'rasterSymbology.savePalette',
  deletePalette: 'rasterSymbology.deletePalette',
  reset: 'rasterSymbology.reset',
} as const;

export function parseRasterMapSourceIdentity(value: Record<string, unknown>): RasterMapSourceIdentity {
  if (typeof value.id !== 'string' || typeof value.projectPath !== 'string'
    || typeof value.visualizationPath !== 'string' || typeof value.visualizationRevision !== 'string'
    || !Number.isInteger(value.mapIndex) || typeof value.mapName !== 'string'
    || typeof value.mapType !== 'string' || !isIntegerArray(value.nodePath)
    || typeof value.nodeName !== 'string' || typeof value.mapLayerType !== 'string'
    || !(value.filename === null || typeof value.filename === 'string' || value.filename === undefined)
    || !(value.filenameRelative === null || typeof value.filenameRelative === 'string' || value.filenameRelative === undefined)
    || typeof value.sourcePath !== 'string'
    || !(value.sourceMapID === null || typeof value.sourceMapID === 'string' || value.sourceMapID === undefined)
    || !(value.sourceRevision === null || typeof value.sourceRevision === 'string' || value.sourceRevision === undefined)
    || !(value.gdbSource === null || value.gdbSource === undefined || isRecord(value.gdbSource))) {
    throw new Error('Host 未提供完整的 RasterMapSourceIdentity。');
  }
  const identity: RasterMapSourceIdentity = {
    id: value.id,
    projectPath: value.projectPath,
    visualizationPath: value.visualizationPath,
    visualizationRevision: value.visualizationRevision,
    mapIndex: value.mapIndex as number,
    mapName: value.mapName,
    mapType: value.mapType,
    nodePath: value.nodePath,
    nodeName: value.nodeName,
    mapLayerType: value.mapLayerType,
    filename: typeof value.filename === 'string' ? value.filename : null,
    filenameRelative: typeof value.filenameRelative === 'string' ? value.filenameRelative : null,
    sourcePath: value.sourcePath,
    sourceMapID: typeof value.sourceMapID === 'string' ? value.sourceMapID : null,
    sourceRevision: typeof value.sourceRevision === 'string' ? value.sourceRevision : '',
    gdbSource: isRecord(value.gdbSource) ? value.gdbSource : null,
  };
  if (!identity.id || !identity.projectPath || !identity.visualizationPath || !identity.visualizationRevision
    || !identity.sourcePath || !identity.sourceRevision || identity.nodePath.length === 0) {
    throw new Error('Host 提供的栅格身份缺少路径、节点、源修订或可视化修订。');
  }
  return identity;
}

export function rasterSymbologyOwnerKey(source: RasterMapSourceIdentity): string {
  const { visualizationRevision: _visualizationRevision, gdbSource, ...owner } = source;
  let stableGdbSource = gdbSource;
  if (isRecord(gdbSource) && isRecord(gdbSource.map)) {
    const stableMap = { ...gdbSource.map };
    delete stableMap.expectedVisualizationRevision;
    stableGdbSource = { ...gdbSource, map: stableMap };
  }
  return JSON.stringify({ ...owner, gdbSource: stableGdbSource });
}

export function rasterSymbologyContextKey(context: WorkbenchContext): string {
  return JSON.stringify([
    context.projectSessionID, context.projectPath, context.projectRevision, context.projectXml,
  ]);
}

export function cloneRasterSymbologyDraft(draft: RasterSymbologyDraft): RasterSymbologyDraft {
  return { ...draft, stops: draft.stops.map(stop => ({ ...stop })),
    hillshade: { ...draft.hillshade }, contours: { ...draft.contours } };
}

export function parseRasterSymbologyInspect(value: unknown): RasterSymbologyInspectResult {
  if (!isRecord(value) || typeof value.previousVisualizationRevision !== 'string'
    || typeof value.visualizationRevision !== 'string' || typeof value.sourceRevision !== 'string') {
    throw new Error('C# Host 返回的栅格符号检查结果无效。');
  }
  const snapshot = parseSnapshot(value.snapshot);
  const raster = parseRasterPayload(value.raster);
  return {
    previousVisualizationRevision: value.previousVisualizationRevision,
    visualizationRevision: value.visualizationRevision,
    sourceRevision: value.sourceRevision,
    snapshot,
    raster,
  };
}

export function parseRasterSymbologyDraft(value: unknown): RasterSymbologyDraftResult {
  if (!isRecord(value)) throw new Error('C# Host 返回的颜色带草稿无效。');
  return { draft: parseDraft(value.draft) };
}

export function parseRasterSymbologySnapshot(value: unknown): RasterSymbologySnapshotResult {
  if (!isRecord(value) || typeof value.previousVisualizationRevision !== 'string'
    || typeof value.visualizationRevision !== 'string' || typeof value.sourceRevision !== 'string') {
    throw new Error('C# Host 返回的默认符号快照无效。');
  }
  return {
    previousVisualizationRevision: value.previousVisualizationRevision,
    visualizationRevision: value.visualizationRevision,
    sourceRevision: value.sourceRevision,
    snapshot: parseSnapshot(value.snapshot),
  };
}

export function parseRasterSymbologyApply(value: unknown): RasterSymbologyApplyResult {
  if (!isRecord(value) || typeof value.previousVisualizationRevision !== 'string'
    || typeof value.visualizationRevision !== 'string' || typeof value.sourceRevision !== 'string') {
    throw new Error('C# Host 返回的栅格符号回执无效。');
  }
  const raster = parseRasterPayload(value.raster);
  return {
    previousVisualizationRevision: value.previousVisualizationRevision,
    visualizationRevision: value.visualizationRevision,
    sourceRevision: value.sourceRevision,
    raster,
  };
}

export function parseRasterSymbologyPalettes(value: unknown): RasterSymbologyPalettesResult {
  if (!isRecord(value) || typeof value.previousVisualizationRevision !== 'string'
    || typeof value.visualizationRevision !== 'string' || typeof value.sourceRevision !== 'string'
    || !Array.isArray(value.palettes)) throw new Error('C# Host 返回的颜色调色板回执无效。');
  return {
    previousVisualizationRevision: value.previousVisualizationRevision,
    visualizationRevision: value.visualizationRevision,
    sourceRevision: value.sourceRevision,
    palettes: value.palettes.map(parsePalette),
  };
}

export function parseRasterHistogram(value: unknown): number[] {
  if (!Array.isArray(value) || value.length < 2 || value.length > 1024
    || !value.every(item => typeof item === 'number' && Number.isFinite(item))) {
    throw new Error('C# Host 返回的直方图值数量或数值无效。');
  }
  for (let index = 1; index < value.length; index++) {
    if (value[index] <= value[index - 1]) throw new Error('C# Host 返回的直方图值未严格递增。');
  }
  return value;
}

export function assertRasterSymbologyOwner(
  previousVisualizationRevision: string,
  sourceRevision: string,
  source: RasterMapSourceIdentity,
  action: string,
  nextVisualizationRevision?: string,
): void {
  if (!sameRevision(previousVisualizationRevision, source.visualizationRevision)
    || !sameRevision(sourceRevision, source.sourceRevision)) {
    throw new Error(`C# Host 的 ${action} 回执与当前可视化修订或栅格源修订不匹配。`);
  }
  if (nextVisualizationRevision !== undefined && (!nextVisualizationRevision
    || sameRevision(previousVisualizationRevision, nextVisualizationRevision))) {
    throw new Error(`C# Host 的 ${action} 回执没有提供新的可视化修订。`);
  }
}

export interface RasterSymbologyMutationReceipt {
  previousVisualizationRevision: string;
  visualizationRevision: string;
  sourceRevision: string;
}

/** Accept a verified Host mutation and advance both outer and GDB map revisions. */
export function advanceRasterSymbologyIdentity(
  source: RasterMapSourceIdentity,
  receipt: RasterSymbologyMutationReceipt,
  action: string,
): RasterMapSourceIdentity {
  assertRasterSymbologyOwner(receipt.previousVisualizationRevision, receipt.sourceRevision,
    source, action, receipt.visualizationRevision);
  let gdbSource = source.gdbSource;
  if (gdbSource !== null) {
    if (!isRecord(gdbSource) || !isRecord(gdbSource.map)
      || typeof gdbSource.map.expectedVisualizationRevision !== 'string'
      || !sameRevision(gdbSource.map.expectedVisualizationRevision, receipt.previousVisualizationRevision)) {
      throw new Error(`C# Host 的 ${action} 回执与当前 GDB 地图修订不匹配。`);
    }
    gdbSource = {
      ...gdbSource,
      map: { ...gdbSource.map, expectedVisualizationRevision: receipt.visualizationRevision },
    };
  }
  return { ...source, visualizationRevision: receipt.visualizationRevision, gdbSource };
}

export function makeRasterSymbologyRequest(source: RasterMapSourceIdentity, context: WorkbenchContext) {
  return { source, projectXml: context.projectXml };
}

export function validateRasterViewport(value: RasterMapViewExtent | undefined): RasterMapViewExtent | null {
  if (!value || ![value.minX, value.minY, value.maxX, value.maxY].every(Number.isFinite)
    || value.maxX <= value.minX || value.maxY <= value.minY
    || !Number.isSafeInteger(value.pixelWidth) || !Number.isSafeInteger(value.pixelHeight)
    || value.pixelWidth < 1 || value.pixelWidth > 4096
    || value.pixelHeight < 1 || value.pixelHeight > 4096) return null;
  return value;
}

export function isValidRasterSymbologyDraft(draft: RasterSymbologyDraft): boolean {
  if (draft.stops.length < 2 || draft.stops.length > 1024) return false;
  const ids = new Set<string>();
  for (let index = 0; index < draft.stops.length; index++) {
    const stop = draft.stops[index];
    if (!isGuid(stop.id) || ids.has(stop.id) || !Number.isFinite(stop.value) || !isHexColor(stop.color)) return false;
    if (index > 0 && stop.value <= draft.stops[index - 1].value) return false;
    ids.add(stop.id);
  }
  return ['Histogram', 'Linear', 'Manual'].includes(draft.valueSource)
    && Number.isInteger(draft.alpha) && draft.alpha >= 0 && draft.alpha <= 255
    && typeof draft.isSurfaceFillEnabled === 'boolean'
    && Number.isFinite(draft.smoothingRadius) && draft.smoothingRadius >= 0 && draft.smoothingRadius <= 1_000_000
    && Number.isFinite(draft.resampleCellSizeRatio) && draft.resampleCellSizeRatio >= 0 && draft.resampleCellSizeRatio <= 1_000_000
    && isValidHillshade(draft.hillshade) && isValidContours(draft.contours);
}

function isValidHillshade(value: RasterHillshadeProperties): boolean {
  return typeof value.isEnabled === 'boolean'
    && Number.isFinite(value.zFactor) && value.zFactor >= 0 && value.zFactor <= 1_000_000
    && Number.isFinite(value.azimuth) && Math.abs(value.azimuth) <= 1_000_000
    && Number.isFinite(value.zenith) && Math.abs(value.zenith) <= 1_000_000;
}

function isValidContours(value: RasterContourProperties): boolean {
  return typeof value.isEnabled === 'boolean' && typeof value.autoInterval === 'boolean'
    && Number.isFinite(value.interval) && value.interval > 0 && value.interval <= 1_000_000
    && Number.isInteger(value.indexEvery) && value.indexEvery >= 0 && value.indexEvery <= 1_000_000
    && isHexColor(value.color)
    && Number.isInteger(value.opacity) && value.opacity >= 0 && value.opacity <= 100
    && typeof value.showLabels === 'boolean'
    && Number.isFinite(value.density) && value.density >= 0.025 && value.density <= 4;
}

export function isHexColor(value: string): boolean {
  const hex = value.trim().replace(/^#/, '');
  return /^[\da-fA-F]{6}(?:[\da-fA-F]{2})?$/.test(hex);
}

export function colorInputValue(value: string): string {
  const hex = value.trim().replace(/^#/, '');
  return `#${hex.length >= 6 ? hex.slice(0, 6) : '000000'}`;
}

function parseSnapshot(value: unknown): RasterSymbologySnapshot {
  if (!isRecord(value) || !Array.isArray(value.palettes) || !Array.isArray(value.plotOptions)
    || !(value.surfaceFillValues === null || isFiniteArray(value.surfaceFillValues))) {
    throw new Error('C# Host 返回的栅格符号快照结构无效。');
  }
  return {
    draft: parseDraft(value.draft),
    palettes: value.palettes.map(parsePalette),
    surfaceFillValues: value.surfaceFillValues as number[] | null,
    plotOptions: value.plotOptions.map(parsePlotOption),
  };
}

function parseRasterPayload(value: unknown): RasterSymbologyRasterPayload {
  if (!isRecord(value) || typeof value.id !== 'string' || typeof value.name !== 'string'
    || !(value.sourceMapID === null || typeof value.sourceMapID === 'string')
    || !Number.isSafeInteger(value.width) || !Number.isSafeInteger(value.height)
    || (value.width as number) < 1 || (value.height as number) < 1
    || typeof value.rgba !== 'string' || !isBase64Bytes(value.rgba, (value.width as number) * (value.height as number) * 4)
    || ![value.minX, value.minY, value.maxX, value.maxY].every(item => typeof item === 'number' && Number.isFinite(item))
    || (value.maxX as number) <= (value.minX as number) || (value.maxY as number) <= (value.minY as number)) {
    throw new Error('C# Host 返回的栅格预览回执无效。');
  }
  return {
    id: value.id,
    sourceMapID: value.sourceMapID,
    name: value.name,
    width: value.width as number,
    height: value.height as number,
    rgba: value.rgba,
    minX: value.minX as number,
    minY: value.minY as number,
    maxX: value.maxX as number,
    maxY: value.maxY as number,
  };
}

function parseDraft(value: unknown): RasterSymbologyDraft {
  if (!isRecord(value) || !(value.paletteID === null || typeof value.paletteID === 'string')
    || typeof value.name !== 'string' || !Array.isArray(value.stops)
    || !['Histogram', 'Linear', 'Manual'].includes(String(value.valueSource))
    || typeof value.grayscale !== 'boolean' || typeof value.isEditing !== 'boolean'
    || typeof value.updateHistogramWithView !== 'boolean' || typeof value.canComputeHistogram !== 'boolean') {
    throw new Error('C# Host 返回的 RasterSymbologyDraftContract 结构无效。');
  }
  const stops = value.stops.map(parseStop);
  const draft: RasterSymbologyDraft = {
    paletteID: value.paletteID,
    name: value.name,
    stops,
    valueSource: value.valueSource as RasterSymbologyValueSource,
    grayscale: value.grayscale,
    isEditing: value.isEditing,
    updateHistogramWithView: value.updateHistogramWithView,
    canComputeHistogram: value.canComputeHistogram,
    alpha: optionalInteger(value.alpha, 255, 0, 255, 'alpha'),
    isSurfaceFillEnabled: optionalBoolean(value.isSurfaceFillEnabled, true, 'isSurfaceFillEnabled'),
    smoothingRadius: optionalNumber(value.smoothingRadius, 15, 0, 1_000_000, 'smoothingRadius'),
    resampleCellSizeRatio: optionalNumber(value.resampleCellSizeRatio, 1, 0, 1_000_000, 'resampleCellSizeRatio'),
    hillshade: parseHillshade(value.hillshade),
    contours: parseContours(value.contours),
  };
  if (!isValidRasterSymbologyDraft(draft)) throw new Error('C# Host 返回的栅格符号设置超出有效范围。');
  return draft;
}

function parseHillshade(value: unknown): RasterHillshadeProperties {
  if (value === undefined || value === null) return { isEnabled: false, zFactor: 3, azimuth: 315, zenith: 45 };
  if (!isRecord(value)) throw new Error('C# Host 返回的 Hillshade 属性无效。');
  const result = {
    isEnabled: optionalBoolean(value.isEnabled, false, 'hillshade.isEnabled'),
    zFactor: optionalNumber(value.zFactor, 3, 0, 1_000_000, 'hillshade.zFactor'),
    azimuth: optionalNumber(value.azimuth, 315, -1_000_000, 1_000_000, 'hillshade.azimuth'),
    zenith: optionalNumber(value.zenith, 45, -1_000_000, 1_000_000, 'hillshade.zenith'),
  };
  if (!isValidHillshade(result)) throw new Error('C# Host 返回的 Hillshade 属性超出有效范围。');
  return result;
}

function parseContours(value: unknown): RasterContourProperties {
  if (value === undefined || value === null) return {
    isEnabled: false, autoInterval: true, interval: 10, indexEvery: 5,
    color: '000000FF', opacity: 70, showLabels: true, density: 1,
  };
  if (!isRecord(value)) throw new Error('C# Host 返回的 Contours 属性无效。');
  const result = {
    isEnabled: optionalBoolean(value.isEnabled, false, 'contours.isEnabled'),
    autoInterval: optionalBoolean(value.autoInterval, true, 'contours.autoInterval'),
    interval: optionalNumber(value.interval, 10, Number.MIN_VALUE, 1_000_000, 'contours.interval'),
    indexEvery: optionalInteger(value.indexEvery, 5, 0, 1_000_000, 'contours.indexEvery'),
    color: optionalColor(value.color, '000000FF', 'contours.color'),
    opacity: optionalInteger(value.opacity, 70, 0, 100, 'contours.opacity'),
    showLabels: optionalBoolean(value.showLabels, true, 'contours.showLabels'),
    density: optionalNumber(value.density, 1, 0.025, 4, 'contours.density'),
  };
  if (!isValidContours(result)) throw new Error('C# Host 返回的 Contours 属性超出有效范围。');
  return result;
}

function optionalBoolean(value: unknown, fallback: boolean, name: string): boolean {
  if (value === undefined || value === null) return fallback;
  if (typeof value !== 'boolean') throw new Error(`C# Host 返回的 ${name} 属性无效。`);
  return value;
}

function optionalNumber(value: unknown, fallback: number, min: number, max: number, name: string): number {
  if (value === undefined || value === null) return fallback;
  if (typeof value !== 'number' || !Number.isFinite(value) || value < min || value > max) {
    throw new Error(`C# Host 返回的 ${name} 属性无效。`);
  }
  return value;
}

function optionalInteger(value: unknown, fallback: number, min: number, max: number, name: string): number {
  const result = optionalNumber(value, fallback, min, max, name);
  if (!Number.isInteger(result)) throw new Error(`C# Host 返回的 ${name} 属性必须是整数。`);
  return result;
}

function optionalColor(value: unknown, fallback: string, name: string): string {
  if (value === undefined || value === null) return fallback;
  if (typeof value !== 'string' || !isHexColor(value)) throw new Error(`C# Host 返回的 ${name} 颜色无效。`);
  return value;
}

function parseStop(value: unknown): RasterSymbologyColorStop {
  if (!isRecord(value) || typeof value.id !== 'string' || !isGuid(value.id)
    || typeof value.color !== 'string' || !isHexColor(value.color)
    || typeof value.value !== 'number' || !Number.isFinite(value.value)) {
    throw new Error('C# Host 返回的颜色断点无效。');
  }
  return { id: value.id, color: value.color, value: value.value };
}

function parsePalette(value: unknown): RasterSymbologyPalette {
  if (!isRecord(value) || typeof value.id !== 'string' || typeof value.name !== 'string'
    || !Array.isArray(value.colors) || !value.colors.every(color => typeof color === 'string' && isHexColor(color))
    || !(value.values === null || isFiniteArray(value.values)) || typeof value.isCustom !== 'boolean') {
    throw new Error('C# Host 返回的 RasterSymbologyPalette 无效。');
  }
  return {
    id: value.id,
    name: value.name,
    colors: value.colors,
    values: value.values as number[] | null,
    isCustom: value.isCustom,
  };
}

function parsePlotOption(value: unknown): RasterPlotOptionDescriptor {
  if (!isRecord(value) || typeof value.propertyName !== 'string' || typeof value.name !== 'string'
    || typeof value.label !== 'string' || typeof value.description !== 'string'
    || (value.kind !== 'bool' && value.kind !== 'enum') || typeof value.value !== 'string'
    || !Array.isArray(value.enumValues) || typeof value.isEnabled !== 'boolean'
    || !(value.enabledIf === null || typeof value.enabledIf === 'string')) {
    throw new Error('C# Host 返回的 RasterPlotOptionDescriptor 无效。');
  }
  const enumValues = value.enumValues.map(option => {
    if (!isRecord(option) || typeof option.value !== 'string' || typeof option.label !== 'string') {
      throw new Error('C# Host 返回的绘图选项枚举值无效。');
    }
    return { value: option.value, label: option.label };
  });
  return {
    propertyName: value.propertyName,
    name: value.name,
    label: value.label,
    description: value.description,
    kind: value.kind,
    value: value.value,
    enumValues,
    isEnabled: value.isEnabled,
    enabledIf: value.enabledIf,
  };
}

function isGuid(value: string): boolean {
  return /^[\da-f]{8}-[\da-f]{4}-[\da-f]{4}-[\da-f]{4}-[\da-f]{12}$/i.test(value);
}

function sameRevision(left: string, right: string): boolean {
  return left.toLocaleUpperCase('en-US') === right.toLocaleUpperCase('en-US');
}

function isFiniteArray(value: unknown): value is number[] {
  return Array.isArray(value) && value.every(item => typeof item === 'number' && Number.isFinite(item));
}

function isBase64Bytes(value: string, byteLength: number): boolean {
  if (byteLength < 4 || byteLength % 4 !== 0 || value.length !== Math.ceil(byteLength / 3) * 4
    || !/^(?:[A-Za-z\d+/]{4})*(?:[A-Za-z\d+/]{2}==|[A-Za-z\d+/]{3}=)?$/.test(value)) return false;
  return true;
}

function isIntegerArray(value: unknown): value is number[] {
  return Array.isArray(value) && value.every(item => Number.isInteger(item) && item >= 0);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export type RasterSymbologyBackend = BackendGateway;
