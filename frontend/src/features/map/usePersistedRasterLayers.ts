import { useEffect, useMemo, useRef, useState } from 'react';
import { BackendOperationUnavailableError } from '../../shared/workbench';
import type { BackendGateway, Language, WorkbenchContext } from '../../shared/workbench';
import { projectXmlSha } from './ProjectTreeListener';
import {
  advanceGdbRasterVisibilityFrameCache,
  canReuseGdbRasterVisibilityFrame,
  rebindGdbRasterSourceIdentity,
} from './rasterVisibilityReuse.js';
import type { CatalogTreeNode, CatalogTreeNodeLocator, MapDocumentTab, MapExtent, MapVisibilityTransition } from './mapTypes';
import type { MapViewport, ViewSize } from './mapViewport';

/** Camera bounds and target render size accepted by MapRasterLayerService. */
export interface PersistedRasterLayerViewport extends MapExtent {
  pixelWidth: number;
  pixelHeight: number;
}

export interface PersistedRasterLayerImage {
  id: string;
  /** Exact saved tree row; render-frame IDs use a distinct node namespace. */
  treeNodeID: string;
  name: string;
  sourcePath: string;
  sourceRevision: string;
  projectSessionID: string;
  projectRevision: string | null;
  visualizationRevision: string;
  previousVisualizationRevision: string | null;
  width: number;
  height: number;
  /** PNG data URL made from the C# host's actual RGBA frame. */
  imageSrc: string;
  extent: MapExtent;
  /** Exact source descriptor already validated by the matching raster frame read. */
  hoverCapture: PersistedRasterHoverCapture;
}

export interface PersistedRasterHoverCapture {
  treeNodeID: string;
  node: CatalogTreeNodeLocator;
  kind: 'raster';
  sourcePath: string;
  sourceRevision: string;
  mapType: string;
  rasterSource: Record<string, unknown>;
}

export interface PersistedRasterLayerFailure {
  id: string;
  name: string;
  message: string;
  /** Original host or validation diagnostic, retained for details and logs. */
  diagnostic: string;
  unsupported?: boolean;
}

export interface PersistedRasterLayersState {
  status: 'idle' | 'loading' | 'ready' | 'partial' | 'error';
  layers: PersistedRasterLayerImage[];
  errors: PersistedRasterLayerFailure[];
}

export interface UsePersistedRasterLayersOptions {
  backend: BackendGateway;
  context: WorkbenchContext;
  document: MapDocumentTab | null;
  language: Language;
  /** Omit to ask the host for a full-source frame. */
  viewport?: PersistedRasterLayerViewport | null;
}

const rasterMapTypes = new Set([
  'SingleBandRasterMapLayer',
  'ImageRasterMapLayer',
  'DiscreteRasterMapLayer',
]);

// Keep aligned with MapTreeService.CheckableNodeTypes. Root and Folder nodes
// are not checkable, so their null/unknown state inherits parent visibility.
const hostCheckableNodeTypes = new Set([
  'Geometry', 'Result', 'MultiResult', 'Terrain', 'BoundaryCondition', 'WindLayer', 'PrecipitationLayer',
  'NValue', 'Classification', 'Feature', 'MapLayer', 'GdbDataset', 'GdbVectorLayer',
  'ConceptualMesh', 'FlowAreaLayer', 'CulvertBarrelLayer',
]);

interface RasterCandidate {
  node: CatalogTreeNode;
  locator: CatalogTreeNodeLocator | null;
  preflightError?: string;
}

interface InternalPersistedRasterLayersState extends PersistedRasterLayersState {
  backend: BackendGateway | null;
  requestKey: string;
}

interface GdbRasterFrameCache {
  backend: BackendGateway | null;
  scopeKey: string;
  frames: Map<string, PersistedRasterLayerImage>;
}

type RasterCandidateOutcome =
  | { kind: 'layer'; layer: PersistedRasterLayerImage }
  | { kind: 'error'; error: PersistedRasterLayerFailure }
  | null;

interface CSharpRasterPayload {
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

interface CSharpRasterReadResult {
  previousVisualizationRevision: string | null;
  visualizationRevision: string;
  sourceRevision: string;
  raster: CSharpRasterPayload;
}

interface GdbMapSourceIdentityPayload {
  sourcePath: string;
  revision: string;
  files: Array<{ relativePath: string; length: number; revision: string }>;
}

interface GdbRasterCatalogSnapshot {
  source: GdbMapSourceIdentityPayload;
  rasterSubdatasets: unknown[];
}

type GdbRasterCatalogCache = Map<string, Promise<GdbRasterCatalogSnapshot>>;

class RasterLayerReadError extends Error {
  constructor(message: string, readonly unsupported = false) {
    super(message);
    this.name = 'RasterLayerReadError';
  }
}

/** Convert the existing map camera into the exact world-space viewport used by the C# renderer. */
export function mapRasterViewportFromCamera(
  camera: MapViewport,
  size: ViewSize,
): PersistedRasterLayerViewport {
  if (!Number.isFinite(camera.scale) || camera.scale <= 0 ||
      !Number.isFinite(camera.offsetX) || !Number.isFinite(camera.offsetY) ||
      !Number.isFinite(size.width) || !Number.isFinite(size.height) ||
      size.width <= 0 || size.height <= 0) {
    throw new RangeError('The map camera cannot be converted to a finite raster viewport.');
  }

  // Both retained raster and Result renderers accept at most 1536 per side.
  // Scale both axes together so large desktop windows keep their aspect ratio.
  const pixelScale = Math.min(1, 1536 / Math.max(size.width, size.height),
    Math.sqrt(2_097_152 / (size.width * size.height)));
  let pixelWidth = Math.max(1, Math.floor(size.width * pixelScale));
  let pixelHeight = Math.max(1, Math.floor(size.height * pixelScale));
  while (pixelWidth * pixelHeight > 2_097_152) {
    if (pixelWidth >= pixelHeight && pixelWidth > 1) pixelWidth--;
    else if (pixelHeight > 1) pixelHeight--;
    else break;
  }

  return {
    minX: -camera.offsetX / camera.scale,
    minY: (camera.offsetY - size.height) / camera.scale,
    maxX: (size.width - camera.offsetX) / camera.scale,
    maxY: camera.offsetY / camera.scale,
    pixelWidth,
    pixelHeight,
  };
}

function isRasterTreeNode(node: CatalogTreeNode): boolean {
  const mapType = node.locator?.mapType;
  return (node.locator?.nodeType === 'MapLayer' && Boolean(mapType && rasterMapTypes.has(mapType))) ||
    /^(RasterMapLayer|ImageMapLayer|DiscreteRasterMapLayer)VisualContext$/.test(node.sourceClass);
}

function checkedRasterCandidates(document: MapDocumentTab): RasterCandidate[] {
  const candidates: RasterCandidate[] = [];
  const visit = (nodes: CatalogTreeNode[], ancestorsVisible: boolean): void => {
    for (const node of nodes) {
      const isCheckable = hostCheckableNodeTypes.has(node.locator?.nodeType ?? '');
      const nodeVisible = ancestorsVisible && (!isCheckable || node.checked === 'checked');
      if (ancestorsVisible && isRasterTreeNode(node)) {
        if (!isCheckable || node.checked === 'checked') {
          candidates.push({ node, locator: node.locator ?? null });
        } else if (node.checked !== 'unchecked') {
          // MapTreeService.IsEffectivelyVisible requires "checked" for a
          // checkable source. Keep a raster's unknown/null state visible as an
          // error, but do not issue a read for a row the Host considers hidden.
          candidates.push({
            node,
            locator: node.locator ?? null,
            preflightError: node.diagnosticReadError ?? node.diagnosticStatus ??
              'The Host did not confirm this checkable raster row as checked.',
          });
        }
      }
      if (node.children?.length) visit(node.children, nodeVisible);
    }
  };
  visit(document.roots, true);
  return candidates;
}

function allRasterTreeCandidates(document: MapDocumentTab): RasterCandidate[] {
  const candidates: RasterCandidate[] = [];
  const visit = (nodes: CatalogTreeNode[]): void => {
    for (const node of nodes) {
      if (isRasterTreeNode(node)) candidates.push({ node, locator: node.locator ?? null });
      if (node.children?.length) visit(node.children);
    }
  };
  visit(document.roots);
  return candidates;
}

function stableValue(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(stableValue);
  const item = value && typeof value === 'object' ? value as Record<string, unknown> : null;
  if (!item) return value;
  return Object.fromEntries(Object.keys(item).sort().map((key) => [key, stableValue(item[key])]));
}

function locatorIdentityKey(locator: CatalogTreeNodeLocator | null | undefined): string | null {
  return locator ? JSON.stringify(stableValue(locator)) : null;
}

function cloneLocator(locator: CatalogTreeNodeLocator): CatalogTreeNodeLocator {
  return {
    ...locator,
    ...(locator.nodePath ? { nodePath: [...locator.nodePath] } : {}),
    ...(locator.parentGeometryNodePath ? { parentGeometryNodePath: [...locator.parentGeometryNodePath] } : {}),
    ...(locator.parentPlanNodePath ? { parentPlanNodePath: [...locator.parentPlanNodePath] } : {}),
    ...(locator.parentResultNodePath ? { parentResultNodePath: [...locator.parentResultNodePath] } : {}),
  };
}

function visualizationPathFor(projectPath: string): string {
  const separator = Math.max(projectPath.lastIndexOf('/'), projectPath.lastIndexOf('\\'));
  const extension = projectPath.lastIndexOf('.');
  return extension > separator
    ? projectPath.slice(0, extension) + '.vis'
    : projectPath + '.vis';
}

function isGdbRaster(candidate: RasterCandidate): boolean {
  const locator = candidate.locator;
  const assetPath = candidate.node.assetID ?? '';
  const filename = locator?.filename ?? '';
  return locator?.nodeType === 'MapLayer' && Boolean(locator.mapType && rasterMapTypes.has(locator.mapType)) &&
    /\.gdb[\\/]?$/i.test(assetPath) && /^(?:GDB|OpenFileGDB):/i.test(filename);
}

function sourceID(mapIndex: number, nodePath: number[]): string {
  return `vis-map-${mapIndex}-node-${nodePath.join('.')}`;
}

function record(value: unknown, description: string): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    throw new Error(`${description} is not an object.`);
  }
  return value as Record<string, unknown>;
}

function finiteNumber(value: unknown, description: string): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) throw new Error(`${description} is invalid.`);
  return value;
}

function nonEmptyString(value: unknown, description: string): string {
  if (typeof value !== 'string' || !value.trim()) throw new Error(`${description} is missing.`);
  return value;
}

function parseRasterReadResult(value: unknown): CSharpRasterReadResult {
  const result = record(value, 'The raster image load response');
  const raster = record(result.raster, 'The raster frame');
  const width = finiteNumber(raster.width, 'The raster frame width');
  const height = finiteNumber(raster.height, 'The raster frame height');
  const minX = finiteNumber(raster.minX, 'The raster frame minimum X');
  const minY = finiteNumber(raster.minY, 'The raster frame minimum Y');
  const maxX = finiteNumber(raster.maxX, 'The raster frame maximum X');
  const maxY = finiteNumber(raster.maxY, 'The raster frame maximum Y');
  const rgba = nonEmptyString(raster.rgba, 'The raster RGBA frame');

  if (!Number.isInteger(width) || width < 1 || !Number.isInteger(height) || height < 1 ||
      maxX <= minX || maxY <= minY) {
    throw new Error('The raster frame dimensions or world extent are invalid.');
  }
  if (typeof raster.sourceMapID !== 'string' || typeof raster.id !== 'string') {
    throw new Error('The raster frame is missing its saved map source identity.');
  }

  const previousVisualizationRevision = result.previousVisualizationRevision;
  if (previousVisualizationRevision !== null && typeof previousVisualizationRevision !== 'string') {
    throw new Error('The previous visualization revision is invalid.');
  }

  return {
    previousVisualizationRevision: previousVisualizationRevision as string | null,
    visualizationRevision: nonEmptyString(result.visualizationRevision, 'The visualization revision'),
    sourceRevision: nonEmptyString(result.sourceRevision, 'The source manifest revision'),
    raster: {
      id: raster.id,
      sourceMapID: raster.sourceMapID,
      name: nonEmptyString(raster.name, 'The raster layer name'),
      width,
      height,
      rgba,
      minX,
      minY,
      maxX,
      maxY,
    },
  };
}

function decodeBase64Rgba(encoded: string, width: number, height: number): Uint8ClampedArray {
  let binary: string;
  try {
    // System.Text.Json serializes C# byte[] as base64 with the Host's Web options.
    binary = atob(encoded);
  } catch (cause) {
    throw new Error('The C# host returned an invalid base64 RGBA frame.', { cause });
  }
  const expectedBytes = width * height * 4;
  if (!Number.isSafeInteger(expectedBytes) || binary.length !== expectedBytes) {
    throw new Error(`The RGBA frame contains ${binary.length} bytes; ${expectedBytes} were required.`);
  }
  const rgba = new Uint8ClampedArray(expectedBytes);
  for (let index = 0; index < expectedBytes; index++) rgba[index] = binary.charCodeAt(index);
  return rgba;
}

function rgbaToPngDataUrl(
  rgba: Uint8ClampedArray,
  width: number,
  height: number,
  isPremultiplied: boolean,
): string {
  const canvas = globalThis.document?.createElement('canvas');
  if (!canvas) throw new Error('A browser canvas is required to encode the raster frame as PNG.');
  canvas.width = width;
  canvas.height = height;
  const drawing = canvas.getContext('2d');
  if (!drawing) throw new Error('The browser could not create a 2D canvas for the raster frame.');
  const image = drawing.createImageData(width, height);
  if (isPremultiplied) {
    // MapSourceRasterRendering.Frame emits premultiplied RGBA; Canvas ImageData
    // expects straight-alpha channels. Single-band symbology frames are already
    // straight RGBA and skip this conversion.
    for (let offset = 0; offset < rgba.length; offset += 4) {
      const alpha = rgba[offset + 3];
      image.data[offset + 3] = alpha;
      if (alpha === 0) {
        image.data[offset] = 0;
        image.data[offset + 1] = 0;
        image.data[offset + 2] = 0;
        continue;
      }
      image.data[offset] = Math.min(255, Math.round(rgba[offset] * 255 / alpha));
      image.data[offset + 1] = Math.min(255, Math.round(rgba[offset + 1] * 255 / alpha));
      image.data[offset + 2] = Math.min(255, Math.round(rgba[offset + 2] * 255 / alpha));
    }
  } else {
    image.data.set(rgba);
  }
  drawing.putImageData(image, 0, 0);
  const result = canvas.toDataURL('image/png');
  if (!result.startsWith('data:image/png;base64,')) {
    throw new Error('The browser did not encode the raster frame as PNG.');
  }
  return result;
}

function diagnosticText(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

function localizedFailureMessage(error: unknown, language: Language, layerName: string): string {
  if (error instanceof RasterLayerReadError && error.unsupported) {
    return language === 'zh'
      ? `文件地理数据库栅格图层“${layerName}”不满足当前 Host 的读取条件。`
      : `File geodatabase raster layer “${layerName}” does not meet the Host's raster read requirements.`;
  }
  const localize = error && typeof error === 'object'
    ? (error as { localizedMessage?: (language: Language) => string }).localizedMessage
    : undefined;
  if (typeof localize === 'function') {
    try { return localize.call(error, language); } catch { /* Use the stable localized fallback below. */ }
  }
  return language === 'zh'
    ? `栅格图层“${layerName}”读取失败。`
    : `Raster layer “${layerName}” could not be read.`;
}

function failureFor(
  candidate: RasterCandidate,
  error: unknown,
  language: Language,
): PersistedRasterLayerFailure {
  const layerName = candidate.node.name || candidate.node.id;
  return {
    id: candidate.node.id,
    name: layerName,
    message: localizedFailureMessage(error, language, layerName),
    diagnostic: diagnosticText(error),
    ...(error instanceof RasterLayerReadError && error.unsupported ? { unsupported: true } : {}),
  };
}

function createReadRequest(
  candidate: RasterCandidate,
  context: WorkbenchContext,
  document: MapDocumentTab,
  viewport: PersistedRasterLayerViewport | null,
): { source: Record<string, unknown>; projectXml: string | null; viewport?: PersistedRasterLayerViewport } {
  const locator = candidate.locator;
  if (!locator || locator.nodeType !== 'MapLayer' || !locator.mapType || !rasterMapTypes.has(locator.mapType) ||
      !locator.nodePath?.length || locator.nodePath.some((index) => !Number.isInteger(index) || index < 0) ||
      !locator.filename || !candidate.node.assetID) {
    throw new Error('The saved raster tree node is missing its exact source locator or canonical source path.');
  }
  if (!context.projectPath) throw new Error('A saved .ras project path is required to read its raster map layer.');
  if (!document.visualizationRevision) throw new Error('The saved map has no visualization revision. Reload its map tree before reading raster pixels.');

  const source: Record<string, unknown> = {
    id: sourceID(document.mapIndex, locator.nodePath),
    projectPath: context.projectPath,
    visualizationPath: visualizationPathFor(context.projectPath),
    visualizationRevision: document.visualizationRevision,
    mapIndex: document.mapIndex,
    mapName: document.name,
    mapType: document.mapType,
    nodePath: locator.nodePath,
    nodeName: locator.nodeName,
    mapLayerType: locator.mapType,
    filename: locator.filename,
    filenameRelative: locator.filenameRelative ?? null,
    sourcePath: candidate.node.assetID,
    sourceMapID: document.mapID,
    // The saved tree supplies a source manifest for some source classes. The
    // load response provides the authoritative current manifest for raster rows.
    sourceRevision: candidate.node.diagnosticSourceRevision ?? null,
  };

  return {
    source,
    projectXml: context.projectXml,
    ...(viewport ? { viewport } : {}),
  };
}

function sameRevision(left: string, right: string): boolean {
  return left.toUpperCase() === right.toUpperCase();
}

function validateGdbSourceIdentity(
  value: unknown,
  expectedPath: string,
): GdbMapSourceIdentityPayload {
  const source = record(value, 'The GDB source manifest');
  const sourcePath = nonEmptyString(source.sourcePath, 'The GDB manifest directory');
  if (sourcePath !== expectedPath) {
    throw new Error('The GDB manifest directory does not match the saved raster layer parent.');
  }
  const revision = nonEmptyString(source.revision, 'The GDB manifest revision');
  if (!Array.isArray(source.files) || source.files.length === 0) {
    throw new Error('The GDB source manifest did not include its file revisions.');
  }
  const files = source.files.map((value, index) => {
    const file = record(value, `GDB manifest file ${index + 1}`);
    const relativePath = nonEmptyString(file.relativePath, `GDB manifest file ${index + 1} path`);
    const length = finiteNumber(file.length, `GDB manifest file ${index + 1} length`);
    const fileRevision = nonEmptyString(file.revision, `GDB manifest file ${index + 1} revision`);
    if (!Number.isSafeInteger(length) || length < 0) {
      throw new Error(`GDB manifest file ${index + 1} length is invalid.`);
    }
    return { relativePath, length, revision: fileRevision };
  });
  return { sourcePath, revision, files };
}

function loadGdbCatalog(
  candidate: RasterCandidate,
  backend: BackendGateway,
  signal: AbortSignal,
  cache: GdbRasterCatalogCache,
): Promise<GdbRasterCatalogSnapshot> {
  const sourcePath = nonEmptyString(candidate.node.assetID, 'The GDB parent directory');
  const expectedRevision = nonEmptyString(
    candidate.node.diagnosticSourceRevision,
    'The saved GDB source revision',
  );
  const existing = cache.get(sourcePath);
  if (existing) return existing;
  if (!backend.supports('map.gdb.catalog')) {
    throw new BackendOperationUnavailableError('map.gdb.catalog');
  }

  const pending = (async (): Promise<GdbRasterCatalogSnapshot> => {
    const raw = await backend.request<unknown>('map.gdb.catalog', {
      sourcePath,
      expectedSourceRevision: expectedRevision,
    }, { signal });
    if (signal.aborted) throw new Error('The raster source read was cancelled.');
    const catalog = record(raw, 'The GDB catalog response');
    const source = validateGdbSourceIdentity(catalog.source, sourcePath);
    if (!sameRevision(source.revision, expectedRevision)) {
      throw new Error('The GDB source revision changed since the saved map tree was read.');
    }
    if (!Array.isArray(catalog.rasterSubdatasets)) {
      throw new Error('The GDB catalog did not return its raster subdataset list.');
    }
    return { source, rasterSubdatasets: catalog.rasterSubdatasets };
  })();
  cache.set(sourcePath, pending);
  return pending;
}

function validateGdbRasterChild(
  candidate: RasterCandidate,
  catalog: GdbRasterCatalogSnapshot,
): GdbMapSourceIdentityPayload {
  const locator = candidate.locator;
  if (!locator || locator.nodeType !== 'MapLayer' || !locator.mapType || !rasterMapTypes.has(locator.mapType) ||
      !locator.nodePath || locator.nodePath.length < 2 ||
      !locator.rootName || !locator.filename || !locator.filenameRelative ||
      locator.assetID !== candidate.node.assetID) {
    throw new Error('The saved GDB raster row is missing its exact parent or child identity.');
  }
  const expectedRevision = nonEmptyString(
    candidate.node.diagnosticSourceRevision,
    'The saved GDB source revision',
  );
  if (!sameRevision(catalog.source.revision, expectedRevision)) {
    throw new Error('The GDB source revision does not match this saved raster tree row.');
  }
  const matches = catalog.rasterSubdatasets.filter((value) => {
    if (typeof value !== 'object' || value === null || Array.isArray(value)) return false;
    return (value as Record<string, unknown>).connectionString === locator.filename;
  });
  if (matches.length !== 1) {
    throw new Error('The saved GDB raster connection no longer matches one exact catalog child.');
  }
  const child = record(matches[0], 'The GDB raster subdataset');
  const mapType = child.mapType;
  const bandCount = child.bandCount;
  const dataType = child.dataType;
  const supportedShape = mapType === 'SingleBandRasterMapLayer'
    ? bandCount === 1 && (dataType === 'float32' || dataType === 'float64')
    : mapType === 'ImageRasterMapLayer'
      ? (bandCount === 3 || bandCount === 4) && dataType === 'System.Byte'
      : mapType === 'DiscreteRasterMapLayer'
        ? bandCount === 1 && ['System.Byte', 'System.Int16', 'System.UInt16', 'System.Int32', 'System.UInt32'].includes(String(dataType))
        : false;
  if (child.name !== candidate.node.name || mapType !== locator.mapType || child.status !== 'available' ||
      child.canRender !== true || !supportedShape ||
      typeof child.width !== 'number' || !Number.isInteger(child.width) || child.width < 1 ||
      typeof child.height !== 'number' || !Number.isInteger(child.height) || child.height < 1) {
    throw new RasterLayerReadError(
      'The saved GDB raster child is no longer an available, renderable subdataset of its saved map type.',
      true,
    );
  }
  return catalog.source;
}

function gdbRasterCacheScopeKey(
  context: WorkbenchContext,
  document: MapDocumentTab,
  language: Language,
): string {
  return JSON.stringify([
    context.projectSessionID,
    context.projectPath,
    context.projectRevision,
    context.projectXml,
    document.id,
    document.mapID,
    document.mapIndex,
    document.name,
    document.mapType,
    document.treeError ?? null,
    language,
  ]);
}

function gdbRasterCacheIdentity(candidate: RasterCandidate): string | null {
  const locator = candidate.locator;
  const sourcePath = candidate.node.assetID;
  const sourceRevision = candidate.node.diagnosticSourceRevision;
  if (!isGdbRaster(candidate) || !locator?.mapType || !sourcePath || !sourceRevision?.trim()) return null;
  return JSON.stringify([
    candidate.node.id,
    candidate.node.sourceClass,
    candidate.node.name,
    sourcePath,
    locator.mapType,
    stableValue(locator),
    sourceRevision.toUpperCase(),
  ]);
}

function rebindGdbRasterFrameForVisibilityTransition(
  layer: PersistedRasterLayerImage,
  candidate: RasterCandidate,
  context: WorkbenchContext,
  document: MapDocumentTab,
  proof: MapVisibilityTransition,
  sameScope: boolean,
): PersistedRasterLayerImage | null {
  const locator = candidate.locator;
  const sourcePath = candidate.node.assetID;
  const expectedSourceRevision = candidate.node.diagnosticSourceRevision;
  const locatorKey = locatorIdentityKey(locator);
  if (!locator || !locator.mapType || !sourcePath || !expectedSourceRevision || !locatorKey ||
      layer.treeNodeID !== candidate.node.id || layer.hoverCapture.treeNodeID !== candidate.node.id ||
      layer.projectSessionID !== context.projectSessionID || layer.projectRevision !== context.projectRevision ||
      !sameRevision(layer.visualizationRevision, proof.previousVisualizationRevision) ||
      !sameRevision(document.visualizationRevision ?? '', proof.visualizationRevision) ||
      !sameRevision(layer.sourceRevision, expectedSourceRevision)) {
    return null;
  }

  try {
    const rasterSource = record(layer.hoverCapture.rasterSource, 'The cached raster source identity');
    const gdbSource = record(rasterSource.gdbSource, 'The cached GDB raster identity');
    const map = record(gdbSource.map, 'The cached saved map request');
    const source = validateGdbSourceIdentity(gdbSource.source, sourcePath);
    const frameNodeKey = locatorIdentityKey(layer.hoverCapture.node);
    const gdbNodeKey = locatorIdentityKey(gdbSource.node as CatalogTreeNodeLocator);
    const sourceIDExpected = locator.nodePath?.length ? sourceID(document.mapIndex, locator.nodePath) : '';
    if (!frameNodeKey || !gdbNodeKey || frameNodeKey !== locatorKey || gdbNodeKey !== locatorKey ||
        !sourceIDExpected || layer.id !== sourceIDExpected || rasterSource.id !== sourceIDExpected ||
        rasterSource.sourceMapID !== document.mapID ||
        rasterSource.projectPath !== context.projectPath ||
        rasterSource.mapIndex !== document.mapIndex || rasterSource.mapName !== document.name ||
        rasterSource.mapType !== document.mapType || rasterSource.nodeName !== locator.nodeName ||
        JSON.stringify(stableValue(rasterSource.nodePath)) !== JSON.stringify(stableValue(locator.nodePath)) ||
        rasterSource.filename !== locator.filename ||
        (rasterSource.filenameRelative ?? null) !== (locator.filenameRelative ?? null) ||
        rasterSource.sourcePath !== sourcePath || rasterSource.sourceRevision !== source.revision ||
        rasterSource.mapLayerType !== locator.mapType ||
        !sameRevision(String(rasterSource.visualizationRevision ?? ''), proof.previousVisualizationRevision) ||
        layer.hoverCapture.sourcePath !== sourcePath || layer.hoverCapture.sourceRevision !== source.revision ||
        layer.hoverCapture.mapType !== locator.mapType ||
        map.projectPath !== context.projectPath || map.projectXml !== context.projectXml ||
        map.projectSessionID !== context.projectSessionID || map.mapID !== document.mapID ||
        map.mapIndex !== document.mapIndex || map.mapName !== document.name || map.mapType !== document.mapType ||
        !sameRevision(String(map.expectedVisualizationRevision ?? ''), proof.previousVisualizationRevision)) {
      return null;
    }
    if (!canReuseGdbRasterVisibilityFrame({
      proof,
      sameScope,
      currentVisualizationRevision: document.visualizationRevision,
      frameVisualizationRevision: layer.visualizationRevision,
      candidateSourceRevision: expectedSourceRevision,
      manifestRevision: source.revision,
      frameSourceRevision: layer.sourceRevision,
      frameMapSourceRevision: rasterSource.sourceRevision as string,
      candidateMapType: locator.mapType,
      frameMapType: layer.hoverCapture.mapType,
      candidateSourcePath: sourcePath,
      manifestSourcePath: source.sourcePath,
      frameSourcePath: layer.sourcePath,
      candidateLocatorKey: locatorKey,
      frameLocatorKey: frameNodeKey,
    })) return null;

    const node = cloneLocator(locator);
    const nextRasterSource = rebindGdbRasterSourceIdentity({
      ...rasterSource,
      sourcePath,
      sourceMapID: document.mapID,
      sourceRevision: source.revision,
      gdbSource: { ...gdbSource, source },
    }, node, proof, document.mapID);
    return {
      ...layer,
      visualizationRevision: proof.visualizationRevision,
      previousVisualizationRevision: proof.previousVisualizationRevision,
      hoverCapture: {
        ...layer.hoverCapture,
        node,
        sourcePath,
        sourceRevision: source.revision,
        mapType: locator.mapType,
        rasterSource: nextRasterSource,
      },
    };
  } catch {
    return null;
  }
}

function rebindGdbRasterFrameCache(
  cache: GdbRasterFrameCache,
  backend: BackendGateway,
  scopeKey: string,
  candidates: RasterCandidate[],
  context: WorkbenchContext,
  document: MapDocumentTab,
  proof: MapVisibilityTransition | null | undefined,
): Map<string, PersistedRasterLayerImage> {
  const sameScope = cache.backend === backend && cache.scopeKey === scopeKey;
  const currentCandidates = new Map<string, RasterCandidate>();
  for (const candidate of candidates) {
    const identity = gdbRasterCacheIdentity(candidate);
    if (identity) currentCandidates.set(identity, candidate);
  }
  const retained = advanceGdbRasterVisibilityFrameCache({
    cachedFrames: cache.frames,
    currentCandidates,
    proof,
    currentVisualizationRevision: document.visualizationRevision,
    scopeMatches: sameScope,
    rebind: (prior, candidate) => proof
      ? rebindGdbRasterFrameForVisibilityTransition(prior, candidate, context, document, proof, sameScope)
      : null,
  });
  cache.backend = backend;
  cache.scopeKey = scopeKey;
  cache.frames = retained;
  return retained;
}

async function readCandidate(
  candidate: RasterCandidate,
  backend: BackendGateway,
  context: WorkbenchContext,
  document: MapDocumentTab,
  viewport: PersistedRasterLayerViewport | null,
  mapRequest: (() => Promise<Record<string, unknown>>) | null,
  gdbCatalogs: GdbRasterCatalogCache,
  signal: AbortSignal,
): Promise<PersistedRasterLayerImage> {
  if (candidate.preflightError) throw new RasterLayerReadError(candidate.preflightError);
  const gdbBacked = isGdbRaster(candidate);
  let operation: string;
  let parameters: Record<string, unknown>;
  let expectedGdbRevision: string | null = null;
  let sourcePath: string;
  if (gdbBacked) {
    const locator = candidate.locator;
    if (!locator?.mapType || !rasterMapTypes.has(locator.mapType)) {
      throw new RasterLayerReadError('The saved GDB raster row has an unsupported map type.', true);
    }
    const gdbOperation = locator.mapType === 'SingleBandRasterMapLayer'
      ? 'rasterMap.load'
      : 'map.raster.layer.load';
    if (!backend.supports(gdbOperation)) throw new BackendOperationUnavailableError(gdbOperation);
    if (!mapRequest) throw new Error('The saved map request is unavailable for the GDB raster read.');
    const map = await mapRequest();
    if (signal.aborted) throw new Error('The raster source read was cancelled.');
    const catalog = await loadGdbCatalog(candidate, backend, signal, gdbCatalogs);
    if (signal.aborted) throw new Error('The raster source read was cancelled.');
    const gdbSource = validateGdbRasterChild(candidate, catalog);
    const childLocator = candidate.locator!;
    const source: Record<string, unknown> = {
      id: sourceID(document.mapIndex, childLocator.nodePath!),
      projectPath: context.projectPath,
      visualizationPath: visualizationPathFor(context.projectPath!),
      visualizationRevision: document.visualizationRevision,
      mapIndex: document.mapIndex,
      mapName: document.name,
      mapType: document.mapType,
      nodePath: childLocator.nodePath,
      nodeName: childLocator.nodeName,
      mapLayerType: childLocator.mapType,
      filename: childLocator.filename,
      filenameRelative: childLocator.filenameRelative,
      sourcePath: gdbSource.sourcePath,
      sourceMapID: document.mapID,
      sourceRevision: gdbSource.revision,
      gdbSource: {
        map,
        node: childLocator,
        source: gdbSource,
      },
    };
    operation = gdbOperation;
    parameters = { source, projectXml: context.projectXml };
    expectedGdbRevision = gdbSource.revision;
    sourcePath = gdbSource.sourcePath;
  } else {
    if (!backend.supports('map.raster.layer.load')) {
      throw new BackendOperationUnavailableError('map.raster.layer.load');
    }
    const request = createReadRequest(candidate, context, document, viewport);
    operation = 'map.raster.layer.load';
    parameters = request;
    sourcePath = String(request.source.sourcePath);
  }

  const rawFrame = await backend.request<unknown>(operation, parameters, { signal });
  if (signal.aborted) throw new Error('The raster source read was cancelled.');
  const result = parseRasterReadResult(rawFrame);
  const expectedID = String((parameters.source as Record<string, unknown>).id);
  if (result.raster.id !== expectedID || result.raster.sourceMapID !== document.mapID) {
    throw new Error('The loaded raster frame belongs to a different saved map layer.');
  }
  if (result.visualizationRevision !== document.visualizationRevision) {
    throw new Error('The saved map visualization changed while its raster frame was being read.');
  }
  if (expectedGdbRevision && !sameRevision(result.sourceRevision, expectedGdbRevision)) {
    throw new Error('The GDB source manifest changed while its raster frame was being read.');
  }
  const rgba = decodeBase64Rgba(result.raster.rgba, result.raster.width, result.raster.height);
  const mapType = candidate.locator?.mapType;
  const imageSrc = rgbaToPngDataUrl(
    rgba,
    result.raster.width,
    result.raster.height,
    mapType !== 'SingleBandRasterMapLayer',
  );
  const sourceIdentity = record(parameters.source, 'The requested raster source identity');
  const node = candidate.locator;
  if (!node || !node.mapType) throw new Error('The loaded raster no longer has its exact saved map-tree locator.');
  const rasterSource: Record<string, unknown> = {
    ...sourceIdentity,
    sourceRevision: result.sourceRevision,
  };
  return {
    id: result.raster.id,
    treeNodeID: candidate.node.id,
    name: result.raster.name,
    sourcePath,
    sourceRevision: result.sourceRevision,
    projectSessionID: context.projectSessionID,
    projectRevision: context.projectRevision,
    visualizationRevision: result.visualizationRevision,
    previousVisualizationRevision: result.previousVisualizationRevision,
    width: result.raster.width,
    height: result.raster.height,
    imageSrc,
    extent: {
      minX: result.raster.minX,
      minY: result.raster.minY,
      maxX: result.raster.maxX,
      maxY: result.raster.maxY,
    },
    hoverCapture: {
      treeNodeID: candidate.node.id,
      node: {
        ...node,
        ...(node.nodePath ? { nodePath: [...node.nodePath] } : {}),
        ...(node.parentGeometryNodePath ? { parentGeometryNodePath: [...node.parentGeometryNodePath] } : {}),
        ...(node.parentPlanNodePath ? { parentPlanNodePath: [...node.parentPlanNodePath] } : {}),
        ...(node.parentResultNodePath ? { parentResultNodePath: [...node.parentResultNodePath] } : {}),
      },
      kind: 'raster',
      sourcePath,
      sourceRevision: result.sourceRevision,
      mapType: node.mapType,
      rasterSource,
    },
  };
}

/** Reads checked, persisted raster map layers through the C# Host and rejects stale responses. */
export function usePersistedRasterLayers({
  backend,
  context,
  document,
  language,
  viewport = null,
}: UsePersistedRasterLayersOptions): PersistedRasterLayersState {
  const [state, setState] = useState<InternalPersistedRasterLayersState>({
    status: 'idle', layers: [], errors: [], backend: null, requestKey: '',
  });
  const generationRef = useRef(0);
  const gdbFrameCacheRef = useRef<GdbRasterFrameCache>({ backend: null, scopeKey: '', frames: new Map() });
  const candidates = useMemo(
    () => document ? checkedRasterCandidates(document) : [],
    [document],
  );
  const sourceKey = JSON.stringify(candidates.map(({ node }) => [
    node.id, node.name, node.sourceClass, node.checked, node.assetID ?? null,
    node.diagnosticStatus ?? null, node.diagnosticReadError ?? null,
    node.locator?.nodeType ?? null, node.locator?.nodeName ?? null,
    node.locator?.mapType ?? null, node.locator?.nodePath ?? null,
    node.locator?.filename ?? null, node.locator?.filenameRelative ?? null,
    node.diagnosticSourceRevision ?? null,
  ]));
  const contextKey = JSON.stringify([
    context.projectSessionID,
    context.projectPath,
    context.projectRevision,
    context.projectXml,
  ]);
  const documentKey = JSON.stringify(document ? [
    document.id, document.mapID, document.mapIndex, document.name,
    document.mapType, document.visualizationRevision,
    document.visibilityTransition?.previousVisualizationRevision ?? null,
    document.visibilityTransition?.visualizationRevision ?? null,
    document.treeError ?? null,
  ] : null);
  // GDB symbology load returns a full-source frame and accepts no viewport.
  const viewportKey = viewport && candidates.some((candidate) => !isGdbRaster(candidate))
    ? JSON.stringify(viewport)
    : null;
  const requestKey = JSON.stringify([contextKey, documentKey, sourceKey, language, viewportKey]);

  useEffect(() => {
    const generation = ++generationRef.current;
    const controller = new AbortController();
    const isCurrent = (): boolean => generationRef.current === generation && !controller.signal.aborted;
    const publish = (next: PersistedRasterLayersState): void => {
      setState({ ...next, backend, requestKey });
    };

    if (!document || !context.projectPath || !context.projectRevision || !context.projectXml) {
      gdbFrameCacheRef.current.backend = backend;
      gdbFrameCacheRef.current.scopeKey = '';
      gdbFrameCacheRef.current.frames.clear();
      publish({ status: 'idle', layers: [], errors: [] });
      return () => {
        generationRef.current++;
        controller.abort();
      };
    }

    if (document.treeError) {
      gdbFrameCacheRef.current.backend = backend;
      gdbFrameCacheRef.current.scopeKey = '';
      gdbFrameCacheRef.current.frames.clear();
      publish({
        status: 'error',
        layers: [],
        errors: [{
          id: 'map-tree',
          name: document.name,
          message: language === 'zh' ? '无法读取当前保存地图的图层树。' : 'The saved map layer tree could not be read.',
          diagnostic: document.treeError,
        }],
      });
      return () => {
        generationRef.current++;
        controller.abort();
      };
    }

    const gdbCache = gdbFrameCacheRef.current;
    const gdbCacheScope = gdbRasterCacheScopeKey(context, document, language);
    const allCandidates = allRasterTreeCandidates(document);
    const reboundFrames = rebindGdbRasterFrameCache(
      gdbCache,
      backend,
      gdbCacheScope,
      allCandidates,
      context,
      document,
      document.visibilityTransition,
    );
    const reusableByCandidate = new Map<RasterCandidate, PersistedRasterLayerImage>();
    const missingCandidates: RasterCandidate[] = [];
    for (const candidate of candidates) {
      const identity = candidate.preflightError ? null : gdbRasterCacheIdentity(candidate);
      const reusable = identity ? reboundFrames.get(identity) : null;
      if (reusable) reusableByCandidate.set(candidate, reusable);
      else missingCandidates.push(candidate);
    }

    if (candidates.length === 0) {
      // An empty ready result is valid only after the Host's checkability and
      // ancestor visibility rules leave no checked raster map layers to read.
      // A visibility proof still advances cached hidden frames above.
      publish({ status: 'ready', layers: [], errors: [] });
      return () => {
        generationRef.current++;
        controller.abort();
      };
    }

    const reusedLayers = candidates.flatMap((candidate) => {
      const layer = reusableByCandidate.get(candidate);
      return layer ? [layer] : [];
    });
    if (missingCandidates.length === 0) {
      publish({ status: 'ready', layers: reusedLayers, errors: [] });
      return () => {
        generationRef.current++;
        controller.abort();
      };
    }

    publish({ status: 'loading', layers: reusedLayers, errors: [] });
    const hasGdbCandidates = missingCandidates.some((candidate) => isGdbRaster(candidate) && !candidate.preflightError);
    let mapRequestPromise: Promise<Record<string, unknown>> | null = null;
    // Build and observe the shared snapshot only from a checked, supported
    // candidate. Closing a project or failing preflight must not leave a
    // detached rejecting promise while a catalog request is still pending.
    const mapRequest: (() => Promise<Record<string, unknown>>) | null = hasGdbCandidates
      ? () => mapRequestPromise ??= projectXmlSha(context.projectXml).then((projectXmlSHA) => {
        if (!context.projectPath || !context.projectXml || !projectXmlSHA) {
          throw new Error('A saved project XML snapshot is required to read a GDB raster.');
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
      })
      : null;
    const gdbCatalogs: GdbRasterCatalogCache = new Map();
    void Promise.all(missingCandidates.map(async (candidate): Promise<RasterCandidateOutcome> => {
      try {
        const layer = await readCandidate(candidate, backend, context, document, viewport,
          mapRequest, gdbCatalogs, controller.signal);
        const identity = gdbRasterCacheIdentity(candidate);
        if (identity && !controller.signal.aborted &&
            sameRevision(layer.sourceRevision, candidate.node.diagnosticSourceRevision ?? '')) {
          gdbCache.frames.set(identity, layer);
        }
        return { kind: 'layer', layer };
      } catch (error) {
        if (controller.signal.aborted) return null;
        return { kind: 'error', error: failureFor(candidate, error, language) };
      }
    })).then((missingResults) => {
      if (!isCurrent()) return;
      const missingByCandidate = new Map<RasterCandidate, RasterCandidateOutcome>();
      missingCandidates.forEach((candidate, index) => {
        missingByCandidate.set(candidate, missingResults[index] ?? null);
      });
      const results = candidates.map((candidate) => {
        const reused = reusableByCandidate.get(candidate);
        return reused ? { kind: 'layer' as const, layer: reused } : missingByCandidate.get(candidate) ?? null;
      });
      const layers: PersistedRasterLayerImage[] = [];
      const errors: PersistedRasterLayerFailure[] = [];
      for (const outcome of results) {
        if (outcome?.kind === 'layer') layers.push(outcome.layer);
        else if (outcome?.kind === 'error') errors.push(outcome.error);
      }
      const status: PersistedRasterLayersState['status'] = errors.length === 0
        ? 'ready'
        : layers.length === 0 ? 'error' : 'partial';
      publish({ status, layers, errors });
    }).catch((error: unknown) => {
      if (!isCurrent()) return;
      const first = missingCandidates[0] ?? candidates[0];
      publish({
        status: 'error',
        layers: [],
        errors: first ? [failureFor(first, error, language)] : [],
      });
    });

    return () => {
      generationRef.current++;
      controller.abort();
    };
  // The serialized key tracks source/context/revision changes without rerunning
  // on parent-only object identity churn.
  }, [backend, requestKey]);

  if (!document || !context.projectPath || !context.projectRevision || !context.projectXml) return { status: 'idle', layers: [], errors: [] };
  // Effects run after commit. Hide prior-map frames during the render that first
  // observes a new document/context/camera, before the replacement request starts.
  if (state.backend !== backend || state.requestKey !== requestKey) {
    return { status: 'loading', layers: [], errors: [] };
  }
  return { status: state.status, layers: state.layers, errors: state.errors };
}
