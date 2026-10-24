import { useEffect, useMemo, useRef, useState } from 'react';
import type { BackendGateway, Language, WorkbenchContext } from '../../shared/workbench';
import type { CatalogTreeNodeLocator, MapDocumentTab } from './mapTypes';
import { mapError } from './mapText';
import { projectXmlSha } from './ProjectTreeListener';
import {
  isMapResultSourcePending,
  type MapResultActiveNode,
  type MapResultFramesController,
  type MapResultFramesViewport,
  type MapResultRenderLayer,
  type MapResultSourceChoice,
  type MapResultSourceCatalog,
} from './useMapResultFrames';

const MAX_VELOCITY_RASTER_SIDE = 384;
const VIEWPORT_DEBOUNCE_MS = 200;
type VelocityOperation = 'map.resultVelocityArrowsField' | 'map.resultVelocityField';

export interface MapResultVelocityVector {
  worldX: number;
  worldY: number;
  vx: number;
  vy: number;
  magnitude: number;
  column: number;
  row: number;
}

export interface MapResultVelocityFieldCapture {
  operation: VelocityOperation;
  requestID: string;
  contextToken: string;
  sourceID: string;
  sourceMapID: string;
  node: CatalogTreeNodeLocator;
  resultPath: string;
  revision: string;
  resultName: string;
  terrainPath: string;
  terrainRevision: string;
  mapType: string;
  selectionIndex: number;
  profileIndex: number;
  profileName: string;
  time: string;
  width: number;
  height: number;
  cellSize: number;
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
  noDataValue: number;
  forceHorizontalRendering: boolean;
  viewport: MapResultFramesViewport;
}

export interface UseMapVelocityFieldOptions {
  backend: BackendGateway;
  context: WorkbenchContext;
  document: MapDocumentTab | null;
  controller: MapResultFramesController;
  viewport: MapResultFramesViewport | null;
  enabled: boolean;
  language: Language;
}

export interface MapVelocityFieldState {
  vectors: MapResultVelocityVector[];
  loading: boolean;
  error: string | null;
  capture: MapResultVelocityFieldCapture | null;
}

interface VelocityFieldResponse extends MapResultVelocityFieldCapture {
  interleavedVelocity: string;
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null;
}

function requiredString(value: unknown, label: string): string {
  if (typeof value !== 'string' || !value.trim()) throw new Error(`The velocity field response is missing ${label}.`);
  return value;
}

function finiteNumber(value: unknown, label: string): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) throw new Error(`The velocity field response has an invalid ${label}.`);
  return value;
}

function integer(value: unknown, label: string, maximum: number): number {
  if (typeof value !== 'number' || !Number.isInteger(value) || value < 1 || value > maximum) {
    throw new Error(`The velocity field response has an invalid ${label}.`);
  }
  return value;
}

function base64Value(code: number): number {
  if (code >= 65 && code <= 90) return code - 65;
  if (code >= 97 && code <= 122) return code - 97 + 26;
  if (code >= 48 && code <= 57) return code - 48 + 52;
  if (code === 43) return 62;
  if (code === 47) return 63;
  return -1;
}

function isInterleavedBase64(value: string, byteLength: number): boolean {
  // Each pixel contains three float32 values (12 bytes), so the Host payload
  // is always a multiple of three bytes and has no base64 padding.
  if (!Number.isSafeInteger(byteLength) || byteLength < 0 || byteLength % 3 !== 0 ||
      value.length !== byteLength / 3 * 4 || value.length % 4 !== 0) return false;
  for (let index = 0; index < value.length; index++) {
    if (base64Value(value.charCodeAt(index)) < 0) return false;
  }
  return true;
}

function parseVelocityResponse(value: unknown, operation: VelocityOperation,
  expectedRequestID: string, viewport: MapResultFramesViewport): VelocityFieldResponse {
  const item = asRecord(value);
  if (!item) throw new Error('The velocity field response is invalid.');
  const width = integer(item.width, 'raster width', MAX_VELOCITY_RASTER_SIDE);
  const height = integer(item.height, 'raster height', MAX_VELOCITY_RASTER_SIDE);
  const noDataValue = finiteNumber(item.noDataValue, 'NoData value');
  const interleavedVelocity = requiredString(item.interleavedVelocity, 'the interleaved velocity field');
  if (!isInterleavedBase64(interleavedVelocity, width * height * 3 * 4)) {
    throw new Error('The velocity field response has an invalid interleaved float payload.');
  }
  const minX = finiteNumber(item.minX, 'minimum X');
  const minY = finiteNumber(item.minY, 'minimum Y');
  const maxX = finiteNumber(item.maxX, 'maximum X');
  const maxY = finiteNumber(item.maxY, 'maximum Y');
  if (minX >= maxX || minY >= maxY) throw new Error('The velocity field response has invalid physical bounds.');
  const cellSize = finiteNumber(item.cellSize, 'cell size');
  if (cellSize <= 0) throw new Error('The velocity field response has an invalid cell size.');
  const profileIndex = finiteNumber(item.profileIndex, 'profile index');
  const selectionIndex = finiteNumber(item.selectionIndex, 'selection index');
  if (!Number.isInteger(profileIndex) || profileIndex < 0 || !Number.isInteger(selectionIndex) || selectionIndex < 0) {
    throw new Error('The velocity field response has an invalid source slot or profile.');
  }
  const responseRequestID = requiredString(item.requestID, 'the request ID');
  if (responseRequestID !== expectedRequestID) throw new Error('The velocity field response belongs to a different request.');
  return {
    operation,
    requestID: responseRequestID,
    contextToken: requiredString(item.contextToken, 'the context token'),
    sourceID: requiredString(item.sourceID, 'the source ID'),
    sourceMapID: requiredString(item.sourceMapID, 'the map ID'),
    node: item.node as CatalogTreeNodeLocator,
    resultPath: requiredString(item.resultPath, 'the Result path'),
    revision: requiredString(item.revision, 'the Result revision'),
    resultName: requiredString(item.resultName, 'the Result name'),
    terrainPath: requiredString(item.terrainPath, 'the terrain path'),
    terrainRevision: requiredString(item.terrainRevision, 'the terrain revision'),
    mapType: requiredString(item.mapType, 'the map type'),
    selectionIndex,
    profileIndex,
    profileName: requiredString(item.profileName, 'the profile name'),
    time: requiredString(item.time, 'the profile time'),
    width,
    height,
    cellSize,
    minX,
    minY,
    maxX,
    maxY,
    noDataValue,
    forceHorizontalRendering: item.forceHorizontalRendering === true,
    viewport,
    interleavedVelocity,
  };
}

function normalizedLocator(value: unknown): string {
  const item = asRecord(value);
  if (!item) return '';
  const keys = [
    'rootName', 'nodeType', 'nodeName', 'nodePath', 'assetID', 'mapType', 'filename', 'filenameRelative',
    'parentGeometryAssetID', 'parentGeometryNodePath', 'parentGeometryNodeName', 'associationAssetID',
    'associationKind', 'parentPlanAssetID', 'parentPlanNodePath', 'parentPlanNodeName', 'parentResultNodeType',
    'parentResultAssetID', 'parentResultNodePath', 'parentResultNodeName', 'parentResultRevision',
    'parentBoundaryConditionAssetID', 'parentBoundaryConditionNodePath', 'parentBoundaryConditionNodeName',
    'sessionHdfResultOwner',
  ];
  return JSON.stringify(keys.map((key) => item[key] ?? null));
}

function canonicalPathEquals(left: string, right: string): boolean {
  return left.replace(/\\/g, '/').replace(/\/$/, '') === right.replace(/\\/g, '/').replace(/\/$/, '');
}

function selectedSlotZero(activeNode: MapResultActiveNode): MapResultSourceChoice | null {
  return activeNode.selections.find((selection) => selection.selectionIndex === 0)?.selectedResult ?? null;
}

function matchingRenderLayer(controller: MapResultFramesController,
  source: MapResultSourceChoice): MapResultRenderLayer | null {
  const profileIndex = controller.selectedProfileIndex;
  return controller.renderSnapshot?.layers.find((layer) =>
    canonicalPathEquals(layer.resultPath, source.resultPath) &&
    layer.revision.toUpperCase() === source.revision.toUpperCase() &&
    layer.profileIndex === profileIndex) ?? null;
}

function viewportForService(viewport: MapResultFramesViewport): MapResultFramesViewport {
  if (![viewport.minX, viewport.minY, viewport.maxX, viewport.maxY,
    viewport.pixelWidth, viewport.pixelHeight].every(Number.isFinite) ||
      viewport.minX >= viewport.maxX || viewport.minY >= viewport.maxY ||
      viewport.pixelWidth <= 0 || viewport.pixelHeight <= 0) {
    throw new Error('The map viewport is invalid for velocity sampling.');
  }
  const scale = Math.min(1, MAX_VELOCITY_RASTER_SIDE / Math.max(viewport.pixelWidth, viewport.pixelHeight));
  return {
    minX: viewport.minX,
    minY: viewport.minY,
    maxX: viewport.maxX,
    maxY: viewport.maxY,
    pixelWidth: Math.max(1, Math.min(MAX_VELOCITY_RASTER_SIDE, Math.round(viewport.pixelWidth * scale))),
    pixelHeight: Math.max(1, Math.min(MAX_VELOCITY_RASTER_SIDE, Math.round(viewport.pixelHeight * scale))),
  };
}

function selectionOverrides(catalog: MapResultSourceCatalog): Record<string, unknown>[] {
  return catalog.activeNodes.flatMap((activeNode) => activeNode.selections.flatMap((selection) => {
    const selected = selection.selectedResult;
    if (!selected) return [];
    const mode = selection.selectionMode || 'Explicit';
    const override: Record<string, unknown> = {
      node: activeNode.node,
      selectionIndex: selection.selectionIndex,
      mode,
    };
    if (mode === 'Explicit') {
      override.resultPath = selected.resultPath;
      override.resultRevision = selected.revision;
    }
    return [override];
  }));
}

function decodeVectors(capture: MapResultVelocityFieldCapture, payload: string): MapResultVelocityVector[] {
  const binary = atob(payload);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index++) bytes[index] = binary.charCodeAt(index);
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const vectors: MapResultVelocityVector[] = [];
  const cellCount = capture.width * capture.height;
  for (let index = 0; index < cellCount; index++) {
    const offset = index * 12;
    const vx = view.getFloat32(offset, true);
    const vy = view.getFloat32(offset + 4, true);
    const magnitude = view.getFloat32(offset + 8, true);
    if (!Number.isFinite(vx) || !Number.isFinite(vy) || !Number.isFinite(magnitude) ||
        vx === capture.noDataValue || vy === capture.noDataValue || magnitude === capture.noDataValue || magnitude < 0) continue;
    const column = index % capture.width;
    const row = Math.floor(index / capture.width);
    vectors.push({
      worldX: capture.minX + (column + 0.5) * capture.cellSize,
      worldY: capture.maxY - (row + 0.5) * capture.cellSize,
      vx,
      vy,
      magnitude,
      column,
      row,
    });
  }
  return vectors;
}

function localizedError(language: Language, error: unknown): string {
  const raw = error instanceof Error ? error.message : String(error);
  if (language !== 'zh') return mapError(language, error);
  if (/comput(?:e|ing).*(?:writer|running)|(?:writer|running).*comput/i.test(raw)) {
    return '结果仍由计算任务写入，完成后才能读取速度向量。';
  }
  if (/SourceFileChangedException|source file changed|sources changed|map changed/i.test(raw)) {
    return '地图、Result 或地形修订已变化，请重新载入当前地图后再试。';
  }
  if (/not advertised|unavailable/i.test(raw)) return '当前 C# 服务尚未发布速度向量读取操作。';
  return `读取速度向量失败。服务端说明：${raw}`;
}

function requestID(generation: number): string {
  return globalThis.crypto?.randomUUID?.() ?? `map-velocity-${Date.now()}-${generation}`;
}

export function useMapVelocityField({ backend, context, document, controller, viewport, enabled, language }: UseMapVelocityFieldOptions): MapVelocityFieldState {
  const [vectors, setVectors] = useState<MapResultVelocityVector[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [capture, setCapture] = useState<MapResultVelocityFieldCapture | null>(null);
  const generationRef = useRef(0);
  const lastFrameIdentityRef = useRef<string | null>(null);
  const lastEnabledRef = useRef(enabled);
  const lastRequestIdentityRef = useRef<string | null>(null);
  const currentRequestIdentityRef = useRef<string | null>(null);

  const sources = controller.sources;
  const activeNode = controller.activeNode;
  const selected = activeNode ? selectedSlotZero(activeNode) : null;
  const profileIndex = controller.selectedProfileIndex;
  const frameLayer = activeNode && selected ? matchingRenderLayer(controller, selected) : null;
  const forceHorizontalRendering = frameLayer?.forceHorizontalRendering ?? false;
  const frameIdentity = useMemo(() => sources && activeNode && selected && profileIndex !== null
    ? JSON.stringify([
      context.projectPath, context.projectRevision, context.projectSessionID, context.projectXml,
      document?.mapID, document?.mapIndex, document?.name, document?.mapType, document?.visualizationRevision,
      sources.projectPath, sources.projectRevision, sources.projectXmlSHA, sources.visualizationRevision,
      sources.mapID, sources.mapIndex, sources.mapName, sources.mapType, sources.contextToken,
      selectionOverrides(sources), controller.status, controller.renderSnapshot?.sourceID,
      controller.renderSnapshot?.contextToken, controller.renderSnapshot?.selectedProfileIndex,
      frameLayer?.sourceID, frameLayer?.mapType, frameLayer?.terrainPath, frameLayer?.terrainRevision,
      normalizedLocator(activeNode.node), selected.resultPath, selected.revision.toUpperCase(),
      selected.isComputing, selected.computeState, selected.computeFailureReason,
      activeNode.selectedMapType, profileIndex, forceHorizontalRendering,
    ])
    : null,
  [activeNode, context.projectPath, context.projectRevision, context.projectSessionID, context.projectXml,
    document?.mapID, document?.mapIndex, document?.mapType, document?.name, document?.visualizationRevision,
    controller.renderSnapshot, controller.status, forceHorizontalRendering, frameLayer?.mapType,
    frameLayer?.sourceID, frameLayer?.terrainPath, frameLayer?.terrainRevision, profileIndex, selected, sources]);
  const normalizedViewport = useMemo(() => {
    if (!viewport) return null;
    try {
      return viewportForService(viewport);
    } catch {
      return null;
    }
  }, [viewport?.minX, viewport?.minY, viewport?.maxX, viewport?.maxY, viewport?.pixelWidth, viewport?.pixelHeight]);
  const viewportKey = normalizedViewport ? JSON.stringify(normalizedViewport) : 'no-valid-viewport';
  const requestIdentity = frameIdentity === null ? null : JSON.stringify([frameIdentity, viewportKey, enabled]);
  currentRequestIdentityRef.current = requestIdentity;

  useEffect(() => {
    const generation = ++generationRef.current;
    const abortController = new AbortController();
    const frameChanged = lastFrameIdentityRef.current !== frameIdentity;
    lastFrameIdentityRef.current = frameIdentity;
    const activationChanged = lastEnabledRef.current !== enabled;
    lastEnabledRef.current = enabled;
    const requestChanged = requestIdentity !== lastRequestIdentityRef.current;
    lastRequestIdentityRef.current = requestIdentity;

    if (!enabled) {
      setVectors([]);
      setCapture(null);
      setLoading(false);
      setError(null);
      return () => abortController.abort();
    }
    if (!context.projectPath || !context.projectXml || !document || !sources || !activeNode || !selected ||
        profileIndex === null || !normalizedViewport) {
      if (requestChanged) {
        setVectors([]);
        setCapture(null);
      }
      setLoading(false);
      setError(enabled && viewport && !normalizedViewport
        ? (language === 'zh' ? '当前地图范围无效，无法读取速度向量。' : 'The current map viewport is invalid for velocity sampling.')
        : null);
      return () => abortController.abort();
    }
    if (isMapResultSourcePending(selected)) {
      setVectors([]);
      setCapture(null);
      setLoading(false);
      setError(language === 'zh'
        ? `结果“${selected.resultName}”仍由计算任务写入，完成后才能读取速度向量。`
        : `Result “${selected.resultName}” is still being written by a compute task.`);
      return () => abortController.abort();
    }
    if (context.projectPath !== sources.projectPath || context.projectRevision !== sources.projectRevision ||
        document.mapID !== sources.mapID || document.mapIndex !== sources.mapIndex ||
        document.name !== sources.mapName || document.mapType !== sources.mapType ||
        document.visualizationRevision !== sources.visualizationRevision) {
      setVectors([]);
      setCapture(null);
      setLoading(false);
      setError(language === 'zh'
        ? '当前地图或工程与 Result 来源目录不一致，请重新载入地图结果。'
        : 'The active map or project does not match the Result source catalog. Reload map results before sampling.');
      return () => abortController.abort();
    }
    if (controller.status !== 'ready' || !controller.renderSnapshot ||
        controller.renderSnapshot.contextToken !== sources.contextToken ||
        controller.renderSnapshot.selectedProfileIndex !== profileIndex) {
      if (requestChanged) {
        setVectors([]);
        setCapture(null);
      }
      setLoading(false);
      setError(null);
      return () => abortController.abort();
    }
    if (activeNode.nodeKind !== 'Result' && activeNode.nodeKind !== 'MultiResult') {
      setVectors([]);
      setCapture(null);
      setLoading(false);
      setError(language === 'zh' ? '请选择地图中的 Result 或 MultiResult 节点后读取速度向量。' : 'Select a Result or MultiResult map node before reading velocity vectors.');
      return () => abortController.abort();
    }
    if (!frameLayer) {
      setVectors([]);
      setCapture(null);
      setLoading(false);
      setError(language === 'zh'
        ? '当前 Result 时刻尚未完成有效渲染，无法按当前地图视图读取速度向量。'
        : 'The selected Result frame is not rendered for the current map view, so velocity vectors cannot be sampled.');
      return () => abortController.abort();
    }
    if (!activeNode.selections.some((selection) => selection.selectionIndex === 0 &&
        selection.selectedResult?.resultPath === selected.resultPath &&
        selection.selectedResult.revision.toUpperCase() === selected.revision.toUpperCase()) ||
        !Number.isInteger(profileIndex) || profileIndex < 0) {
      setVectors([]);
      setCapture(null);
      setLoading(false);
      setError(language === 'zh' ? '当前 Result 源或时刻无效，无法读取速度向量。' : 'The selected Result source or profile is invalid.');
      return () => abortController.abort();
    }

    const operation: VelocityOperation | null = backend.supports('map.resultVelocityArrowsField')
      ? 'map.resultVelocityArrowsField'
      : backend.supports('map.resultVelocityField') && activeNode.selectedMapType === 'Velocity'
        ? 'map.resultVelocityField'
        : null;
    if (!operation) {
      setVectors([]);
      setCapture(null);
      setLoading(false);
      setError(language === 'zh'
        ? '当前 C# 服务未发布速度箭头读取操作；速度场接口仅适用于 Velocity 图层。'
        : 'The C# service has not advertised velocity arrows; the velocity field operation requires a Velocity map.');
      return () => abortController.abort();
    }
    if (operation === 'map.resultVelocityField' && activeNode.selectedMapType !== 'Velocity') {
      setVectors([]);
      setCapture(null);
      setLoading(false);
      setError(language === 'zh' ? '只有当前显示 Velocity 图层时才能读取速度场。' : 'The velocity field requires the selected map type to be Velocity.');
      return () => abortController.abort();
    }

    const selection = selectionOverrides(sources);
    const profile = profileIndex;
    const requestKey = requestIdentity!;
    const shouldDebounce = !frameChanged && !activationChanged;
    setError(null);
    setLoading(true);
    if (requestChanged) {
      setVectors([]);
      setCapture(null);
    }

    let timer: number | null = null;
    const execute = async () => {
      try {
        const xmlSha = await projectXmlSha(context.projectXml);
        if (abortController.signal.aborted || generation !== generationRef.current ||
            currentRequestIdentityRef.current !== requestKey) return;
        if (!xmlSha) throw new Error('The saved project XML snapshot is required to sample Result velocity.');
        if (sources.projectXmlSHA.toUpperCase() !== xmlSha.toUpperCase()) {
          throw new Error('The selected map source belongs to a different saved project XML snapshot.');
        }
        const map = {
          projectPath: context.projectPath,
          projectXml: context.projectXml,
          projectXmlSHA: xmlSha,
          mapID: document.mapID,
          mapIndex: document.mapIndex,
          mapName: document.name,
          mapType: document.mapType,
          expectedVisualizationRevision: document.visualizationRevision,
          projectSessionID: context.projectSessionID,
        };
        const id = requestID(generation);
        const raw = await backend.request<unknown>(operation, {
          map,
          selectionOverrides: selection,
          contextToken: sources.contextToken,
          node: activeNode.node,
          requestID: id,
          expectedResultPath: selected.resultPath,
          expectedResultRevision: selected.revision,
          profileIndex: profile,
          viewport: normalizedViewport,
          forceHorizontalRendering,
        }, { signal: abortController.signal });
        if (abortController.signal.aborted || generation !== generationRef.current ||
            currentRequestIdentityRef.current !== requestKey) return;
        const result = parseVelocityResponse(raw, operation, id, normalizedViewport);
        if (result.requestID !== id || result.contextToken !== sources.contextToken ||
            result.sourceID !== controller.renderSnapshot!.sourceID ||
            result.sourceMapID !== sources.mapID || normalizedLocator(result.node) !== normalizedLocator(activeNode.node) ||
            !canonicalPathEquals(result.resultPath, selected.resultPath) ||
            result.revision.toUpperCase() !== selected.revision.toUpperCase() ||
            result.profileIndex !== profile || result.selectionIndex !== 0 ||
            result.forceHorizontalRendering !== forceHorizontalRendering || result.mapType !== 'Velocity') {
          throw new Error('The velocity field response does not match the current map, Result source, profile, or view state.');
        }
        const matchingLayer = matchingRenderLayer(controller, selected);
        if (!matchingLayer || !canonicalPathEquals(result.terrainPath, matchingLayer.terrainPath) ||
            result.terrainRevision.toUpperCase() !== matchingLayer.terrainRevision.toUpperCase()) {
          throw new Error('The associated terrain changed while the Result velocity field was being sampled.');
        }
        const nextVectors = decodeVectors(result, result.interleavedVelocity);
        if (abortController.signal.aborted || generation !== generationRef.current ||
            currentRequestIdentityRef.current !== requestKey) return;
        const { interleavedVelocity: _payload, ...nextCapture } = result;
        setVectors(nextVectors);
        setCapture(nextCapture);
        setLoading(false);
      } catch (caught) {
        if (abortController.signal.aborted || generation !== generationRef.current ||
            currentRequestIdentityRef.current !== requestKey) return;
        setVectors([]);
        setCapture(null);
        setLoading(false);
        setError(localizedError(language, caught));
      }
    };

    if (shouldDebounce) {
      timer = window.setTimeout(() => {
        timer = null;
        void execute();
      }, VIEWPORT_DEBOUNCE_MS);
    } else {
      void execute();
    }
    return () => {
      if (timer !== null) window.clearTimeout(timer);
      abortController.abort();
      if (generationRef.current === generation) generationRef.current++;
    };
  }, [activeNode, backend, context.projectPath, context.projectSessionID, context.projectXml, controller.renderSnapshot,
    controller.status, document, enabled, forceHorizontalRendering, frameIdentity, language, normalizedViewport,
    profileIndex, requestIdentity, selected, sources, viewportKey]);

  return { vectors, loading, error, capture };
}
