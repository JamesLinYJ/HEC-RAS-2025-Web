import { useEffect, useMemo, useRef, useState } from 'react';
import type { BackendGateway, Language, WorkbenchContext } from '../../shared/workbench';
import type { CatalogTreeNode, CatalogTreeNodeLocator, MapDocumentTab, XY } from './mapTypes';
import type { MapToolID } from './MapToolsVM';
import type { MapViewport, ViewSize } from './mapViewport';
import { projectXmlSha } from './ProjectTreeListener';
import type { PersistedRasterHoverCapture } from './usePersistedRasterLayers';
import type { MapAssociationHoverCapture } from './associatedMapLayers';
import { mapResultSelectionOverridesFromCatalog } from './useMapResultFrames';
import type { MapResultFramesController, MapResultRenderLayer, MapResultSourceCatalog } from './useMapResultFrames';
import type { MapMeshElementHit, MapResultAnalysisCapture, MapResultMeshSourceCapture } from './analysis/mapAnalysisTypes';
import {
  resultHoverEntryMatchesCurrentFrame,
  resultHoverFramesMatchContext,
} from './mapResultHoverIdentity';
import type {
  ResultHoverFrameContext,
  ResultHoverSourceSelection,
} from './mapResultHoverIdentity';

export interface MapHoverIdentifyEntry {
  node: CatalogTreeNodeLocator;
  kind: 'raster' | 'result' | 'terrain' | 'nvalue' | 'classification' | 'mesh';
  label: string;
  /** Keep source values as strings so UInt32 categories retain their exact text. */
  value: string | null;
  valueType: string | null;
  sourcePath: string | null;
  sourceRevision: string | null;
  mapType: string | null;
  profileIndex: number | null;
  meshIndex: number | null;
  elementIndex: number | null;
  meshHit: MapMeshElementHit | null;
}

export interface MapHoverTooltip {
  screen: XY;
  entries: MapHoverIdentifyEntry[];
}

export interface MapHoverPointer {
  project: XY;
  screen: XY;
}

export interface UseMapHoverIdentifyOptions {
  backend: BackendGateway;
  context: WorkbenchContext;
  document: MapDocumentTab | null;
  language: Language;
  pointer: MapHoverPointer | null;
  activeTool: MapToolID;
  dragging: boolean;
  draftPointCount: number;
  viewport: MapViewport;
  size: ViewSize;
  /** True only when RasProjectMap currently draws its project-coordinate images. */
  imagesVisible: boolean;
  rasterCaptures: PersistedRasterHoverCapture[];
  associatedCaptures: MapAssociationHoverCapture[];
  resultMeshCapture: MapResultMeshSourceCapture | null;
  resultAnalysisCapture: MapResultAnalysisCapture | null;
  resultFrames: MapResultFramesController;
  selectedBackendNodeID: string | null;
}

interface MapHoverLayerCapturePayload {
  node: CatalogTreeNodeLocator;
  kind: 'raster' | 'result' | 'terrain' | 'nvalue' | 'classification' | 'mesh';
  sourcePath?: string;
  sourceRevision?: string;
  mapType?: string;
  profileIndex?: number;
  rasterSource?: Record<string, unknown>;
  resultSelections?: Array<{ selectionIndex: number; resultPath: string; resultRevision: string }>;
  associated?: Pick<MapAssociationHoverCapture, 'geometryNodes' | 'planNodes' | 'resultOwners' | 'dependencies'>;
  mesh?: MapResultMeshSourceCapture['hoverLayer']['mesh'];
}

interface HoverRequestPlan {
  key: string;
  pointer: MapHoverPointer;
  catalog: MapResultSourceCatalog;
  layers: MapHoverLayerCapturePayload[];
  profileSelections: Array<{ node: CatalogTreeNodeLocator; profileIndex: number }>;
  selectionOverrides: ReturnType<typeof mapResultSelectionOverridesFromCatalog>;
  resultSnapshot: ResultHoverSnapshotCapture | null;
  meshCapture: MapResultMeshSourceCapture | null;
  analysisCaptureKey: string | null;
}

interface ResultHoverSnapshotCapture {
  context: ResultHoverFrameContext;
  frames: MapResultRenderLayer[];
  selections: ResultHoverSourceSelection[];
}

interface CurrentTooltip extends MapHoverTooltip {
  key: string;
}

const HOVER_DWELL_MS = 140;

function record(value: unknown, description: string): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    throw new Error(`${description}不是有效对象。`);
  }
  return value as Record<string, unknown>;
}

function requiredString(value: unknown, description: string): string {
  if (typeof value !== 'string' || !value.trim()) throw new Error(`${description}缺失。`);
  return value;
}

function optionalString(value: unknown, description: string): string | null {
  if (value === null || value === undefined) return null;
  if (typeof value !== 'string') throw new Error(`${description}无效。`);
  return value;
}

function sameRevision(left: string, right: string): boolean {
  return left.toUpperCase() === right.toUpperCase();
}

function sameMapID(left: string, right: string): boolean {
  return left.replace(/[{}]/g, '').toUpperCase() === right.replace(/[{}]/g, '').toUpperCase();
}

function stableValue(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(stableValue);
  const item = value && typeof value === 'object' ? value as Record<string, unknown> : null;
  if (!item) return value;
  return Object.fromEntries(Object.keys(item).sort().map((key) => [key, stableValue(item[key])]));
}

function locatorKey(locator: CatalogTreeNodeLocator): string {
  return JSON.stringify(stableValue(locator));
}

function sameLocator(left: CatalogTreeNodeLocator, right: CatalogTreeNodeLocator): boolean {
  return locatorKey(left) === locatorKey(right);
}

function findVisibleNode(
  nodes: CatalogTreeNode[],
  locator: CatalogTreeNodeLocator,
  ancestorsVisible = true,
): CatalogTreeNode | null {
  for (const node of nodes) {
    const visible = ancestorsVisible && (node.checked == null || node.checked === 'checked');
    if (!visible) continue;
    if (node.locator && sameLocator(node.locator, locator)) return node;
    const child = node.children?.length ? findVisibleNode(node.children, locator, visible) : null;
    if (child) return child;
  }
  return null;
}

function currentResultCapture(
  document: MapDocumentTab,
  catalog: MapResultSourceCatalog,
  controller: MapResultFramesController,
): {
  capture: MapHoverLayerCapturePayload;
  profileSelection: { node: CatalogTreeNodeLocator; profileIndex: number };
  snapshot: ResultHoverSnapshotCapture;
} | null {
  const activeNode = controller.activeNode;
  const snapshot = controller.renderSnapshot;
  const profileIndex = controller.selectedProfileIndex;
  const catalogNode = activeNode
    ? catalog.activeNodes.find((node) => sameLocator(node.node, activeNode.node))
    : null;
  if (controller.status !== 'ready' || !activeNode || !snapshot || profileIndex == null ||
      !catalogNode || catalogNode.selectedMapType !== activeNode.selectedMapType ||
      JSON.stringify(stableValue(catalogNode.selections)) !== JSON.stringify(stableValue(activeNode.selections)) ||
      snapshot.contextToken !== catalog.contextToken || snapshot.selectedProfileIndex !== profileIndex ||
      !activeNode.selectedMapType || !Number.isInteger(profileIndex) || profileIndex < 0 ||
      !findVisibleNode(document.roots, activeNode.node)) return null;

  const selected: ResultHoverSourceSelection[] = activeNode.selections.flatMap((selection) => selection.selectedResult
    ? [{ selectionIndex: selection.selectionIndex, resultPath: selection.selectedResult.resultPath,
      resultRevision: selection.selectedResult.revision }]
    : []);
  if (selected.length === 0) return null;
  const frameContext: ResultHoverFrameContext = {
    sourceID: snapshot.sourceID,
    mapID: document.mapID,
    mapType: activeNode.selectedMapType,
    nodeType: activeNode.node.nodeType,
    timelineProfileIndex: profileIndex,
  };
  const sourceMatches = resultHoverFramesMatchContext(snapshot.layers, selected, frameContext);
  const currentFramesMatch = controller.layers.length === snapshot.layers.length &&
    controller.layers.every((layer, index) => layer.id === snapshot.layers[index]?.id &&
      layer.sourceID === snapshot.layers[index]?.sourceID &&
      layer.sourceMapID === snapshot.layers[index]?.sourceMapID &&
      layer.resultPath === snapshot.layers[index]?.resultPath &&
      sameRevision(layer.revision, snapshot.layers[index]?.revision ?? '') &&
      layer.mapType === snapshot.layers[index]?.mapType &&
      layer.profileIndex === snapshot.layers[index]?.profileIndex &&
      JSON.stringify(layer.selectionIndices) === JSON.stringify(snapshot.layers[index]?.selectionIndices));
  if (!sourceMatches || !currentFramesMatch) return null;

  return {
    capture: {
      node: activeNode.node,
      kind: 'result',
      mapType: activeNode.selectedMapType,
      profileIndex,
      resultSelections: selected,
    },
    profileSelection: { node: activeNode.node, profileIndex },
    snapshot: { context: frameContext, frames: snapshot.layers, selections: selected },
  };
}

function parseEntries(
  value: unknown,
  request: Record<string, unknown>,
  plan: HoverRequestPlan,
): MapHoverIdentifyEntry[] {
  const response = record(value, '地图悬停识别响应');
  const map = record(request.map, '地图请求');
  const point = record(request.point, '查询坐标');
  if (response.requestID !== request.requestID || response.projectPath !== map.projectPath ||
      response.projectRevision !== plan.catalog.projectRevision ||
      typeof response.projectXmlSHA !== 'string' ||
      !sameRevision(response.projectXmlSHA, String(map.projectXmlSHA)) ||
      typeof response.visualizationRevision !== 'string' ||
      !sameRevision(response.visualizationRevision, String(map.expectedVisualizationRevision)) ||
      typeof response.mapID !== 'string' || !sameMapID(response.mapID, String(map.mapID)) ||
      response.mapIndex !== map.mapIndex || response.mapName !== map.mapName || response.mapType !== map.mapType ||
      response.contextToken !== request.contextToken || typeof response.point !== 'object' ||
        response.point === null || Array.isArray(response.point)) {
    throw new Error('地图悬停响应已不属于当前工程或地图。');
  }
  const responsePoint = record(response.point, '地图悬停响应坐标');
  if (responsePoint.x !== point.x || responsePoint.y !== point.y || !Array.isArray(response.entries)) {
    throw new Error('地图悬停响应坐标或图层标签无效。');
  }

  return response.entries.map((entryValue, index): MapHoverIdentifyEntry => {
    const entry = record(entryValue, `图层标签 ${index + 1}`);
    const node = record(entry.node, `图层标签 ${index + 1} 来源`);
    if (!['raster', 'result', 'terrain', 'nvalue', 'classification', 'mesh'].includes(String(entry.kind))) throw new Error('服务端返回了未请求的悬停来源类型。');
    const kind = entry.kind as MapHoverIdentifyEntry['kind'];
    const capture = plan.layers.find((candidate) => candidate.kind === kind &&
      sameLocator(candidate.node, node as unknown as CatalogTreeNodeLocator));
    if (!capture) throw new Error('服务端返回的图层标签不属于当前可见图层。');
    const sourcePath = optionalString(entry.sourcePath, '悬停来源路径');
    const sourceRevision = optionalString(entry.sourceRevision, '悬停来源修订');
    const mapType = optionalString(entry.mapType, '悬停图层类型');
    const profileIndex = entry.profileIndex == null ? null : entry.profileIndex;
    if (profileIndex !== null && (typeof profileIndex !== 'number' || !Number.isInteger(profileIndex))) {
      throw new Error('悬停结果时刻无效。');
    }
    let meshHit: MapMeshElementHit | null = null;
    const meshIndex = entry.meshIndex == null ? null : entry.meshIndex;
    const elementIndex = entry.elementIndex == null ? null : entry.elementIndex;
    if ((meshIndex !== null && (typeof meshIndex !== 'number' || !Number.isInteger(meshIndex) || meshIndex < 0)) ||
        (elementIndex !== null && (typeof elementIndex !== 'number' || !Number.isInteger(elementIndex) || elementIndex < 0))) {
      throw new Error('Mesh 命中索引无效。');
    }
    if (kind === 'mesh') {
      const meshCapture = plan.meshCapture;
      const resultLayer = plan.layers.find(layer => layer.kind === 'result' &&
        sameLocator(layer.node, meshCapture?.resultOwner.resultNode ?? capture.node));
      const selectedSlotZero = resultLayer?.resultSelections?.find(selection => selection.selectionIndex === 0);
      if (!meshCapture || capture.node.nodeType !== 'FlowAreaLayer' ||
          !sameLocator(capture.node, meshCapture.flowAreaNode) ||
          sourcePath !== meshCapture.geometryPath || sourceRevision === null ||
          !sameRevision(sourceRevision, meshCapture.geometryRevision) || mapType !== null ||
          profileIndex !== null || meshIndex === null || elementIndex === null ||
          (entry.valueType !== 'Cell' && entry.valueType !== 'Face') ||
          !plan.analysisCaptureKey ||
          meshCapture.resultOwner.contextToken !== request.contextToken ||
          meshCapture.resultOwner.selectionIndex !== 0 ||
          !resultLayer || !sameLocator(meshCapture.resultOwner.resultNode, resultLayer.node) ||
          !selectedSlotZero || selectedSlotZero.resultPath !== meshCapture.resultOwner.selectedResultPath ||
          !sameRevision(selectedSlotZero.resultRevision, meshCapture.resultOwner.selectedResultRevision)) {
        throw new Error('Mesh 悬停标签未匹配当前 Flow Area、Result 来源和元素类型。');
      }
      meshHit = {
        ownerKey: meshCapture.ownerKey,
        captureKey: plan.analysisCaptureKey,
        contextToken: String(request.contextToken),
        mapID: String(map.mapID),
        mapIndex: Number(map.mapIndex),
        visualizationRevision: String(map.expectedVisualizationRevision),
        resultNode: meshCapture.resultOwner.resultNode,
        resultPath: meshCapture.resultOwner.selectedResultPath,
        resultRevision: meshCapture.resultOwner.selectedResultRevision,
        selectionIndex: 0,
        geometryNode: meshCapture.geometryNode,
        geometryContextToken: meshCapture.geometryContextToken,
        geometryPath: meshCapture.geometryPath,
        geometryRevision: meshCapture.geometryRevision,
        flowAreaNode: meshCapture.flowAreaNode,
        flowAreaIndex: meshIndex,
        elementIndex,
        elementKind: entry.valueType,
      };
    } else if (kind !== 'result') {
      if (sourcePath !== capture.sourcePath || sourceRevision === null ||
          !sameRevision(sourceRevision, capture.sourceRevision ?? '') ||
          (kind === 'raster' && mapType !== capture.mapType)) {
        throw new Error('图层悬停标签的来源修订已变化。');
      }
    } else {
      const selected = capture.resultSelections ?? [];
      const snapshot = plan.resultSnapshot;
      if (sourcePath === null || sourceRevision === null ||
          !selected.some((choice) => choice.resultPath === sourcePath && sameRevision(choice.resultRevision, sourceRevision)) ||
          mapType === null || mapType !== capture.mapType || profileIndex === null || !snapshot ||
          !resultHoverEntryMatchesCurrentFrame(snapshot.frames, snapshot.selections, snapshot.context, {
            resultPath: sourcePath,
            resultRevision: sourceRevision,
            mapType,
            profileIndex,
          })) {
        throw new Error('Result 悬停标签不再匹配当前来源或时刻。');
      }
    }
    return {
      node: node as unknown as CatalogTreeNodeLocator,
      kind,
      label: requiredString(entry.label, '悬停标签'),
      value: optionalString(entry.value, '悬停原始值'),
      valueType: optionalString(entry.valueType, '悬停值类型'),
      sourcePath,
      sourceRevision,
      mapType,
      profileIndex,
      meshIndex,
      elementIndex,
      meshHit,
    };
  });
}

export function useMapHoverIdentify({
  backend,
  context,
  document,
  language,
  pointer,
  activeTool,
  dragging,
  draftPointCount,
  viewport,
  size,
  imagesVisible,
  rasterCaptures,
  associatedCaptures,
  resultMeshCapture,
  resultAnalysisCapture,
  resultFrames,
  selectedBackendNodeID,
}: UseMapHoverIdentifyOptions): { tooltip: MapHoverTooltip | null; loading: boolean } {
  const [current, setCurrent] = useState<CurrentTooltip | null>(null);
  const generationRef = useRef(0);
  const activeKeyRef = useRef<string | null>(null);

  const plan = useMemo<HoverRequestPlan | null>(() => {
    const catalog = resultFrames.sources;
    if (!pointer || activeTool !== 'select' || dragging || draftPointCount > 0 || !imagesVisible ||
        !context.projectPath || !context.projectRevision || !context.projectXml || !document ||
        !document.visualizationRevision || document.treeError || !catalog?.contextToken ||
        !backend.supports('map.hover.identify') || !Number.isFinite(pointer.project[0]) ||
        !Number.isFinite(pointer.project[1]) || !Number.isFinite(viewport.scale) || viewport.scale <= 0) return null;

    if (catalog.projectPath !== context.projectPath || catalog.projectRevision !== context.projectRevision ||
        !sameMapID(catalog.mapID, document.mapID) || catalog.mapIndex !== document.mapIndex ||
        catalog.mapName !== document.name || catalog.mapType !== document.mapType ||
        !sameRevision(catalog.visualizationRevision, document.visualizationRevision)) return null;

    let layers: MapHoverLayerCapturePayload[] = associatedCaptures.filter(capture =>
      findVisibleNode(catalog.treeRoots, capture.node)).map(capture => ({
      node: capture.node, kind: capture.kind, sourcePath: capture.sourcePath, sourceRevision: capture.sourceRevision,
      ...(capture.kind === 'terrain' ? {} : { associated: { geometryNodes: capture.geometryNodes,
        planNodes: capture.planNodes, resultOwners: capture.resultOwners, dependencies: capture.dependencies } }),
    }));
    layers = [...layers, ...rasterCaptures.map((capture): MapHoverLayerCapturePayload => ({
      node: capture.node,
      kind: 'raster',
      sourcePath: capture.sourcePath,
      sourceRevision: capture.sourceRevision,
      mapType: capture.mapType,
      rasterSource: capture.rasterSource,
    }))];
    const resultCapture = currentResultCapture(document, catalog, resultFrames);
    if (resultCapture) layers = [...layers, resultCapture.capture];
    const exactMeshCapture = resultMeshCapture && resultAnalysisCapture && resultCapture &&
      resultAnalysisCapture.contextToken === catalog.contextToken &&
      resultAnalysisCapture.ownerKey === resultMeshCapture.ownerKey &&
      sameLocator(resultAnalysisCapture.activeNode.node, resultCapture.capture.node) &&
      resultAnalysisCapture.profileIndex === resultCapture.snapshot.context.timelineProfileIndex &&
      resultMeshCapture.resultOwner.contextToken === catalog.contextToken &&
      sameLocator(resultMeshCapture.resultOwner.resultNode, resultCapture.capture.node) &&
      resultCapture.capture.resultSelections?.some(selection => selection.selectionIndex === 0 &&
        selection.resultPath === resultMeshCapture.resultOwner.selectedResultPath &&
        sameRevision(selection.resultRevision, resultMeshCapture.resultOwner.selectedResultRevision)) &&
      findVisibleNode(catalog.treeRoots, resultMeshCapture.flowAreaNode)
      ? resultMeshCapture : null;
    const analysisCaptureKey = exactMeshCapture && resultAnalysisCapture
      ? resultAnalysisCapture.captureKey : null;
    if (exactMeshCapture) layers = [...layers, {
      node: exactMeshCapture.hoverLayer.node,
      kind: 'mesh',
      sourcePath: exactMeshCapture.geometryPath,
      sourceRevision: exactMeshCapture.geometryRevision,
      mesh: exactMeshCapture.hoverLayer.mesh,
    }];
    if (layers.length === 0 || layers.length > 256) return null;

    // The last drawn project image is topmost. The selected project tree row
    // takes the same precedence as C# ToolTipAnnotation's active render layer.
    layers = [...layers].reverse();
    if (selectedBackendNodeID) {
      const selectedIndex = layers.findIndex((layer) => {
        if (layer.kind === 'raster') {
          return rasterCaptures.some((capture) => capture.treeNodeID === selectedBackendNodeID &&
            sameLocator(capture.node, layer.node));
        }
        return catalog.treeRoots.some((root) => findVisibleNode([root], layer.node)?.id === selectedBackendNodeID);
      });
      if (selectedIndex > 0) layers = [layers[selectedIndex], ...layers.slice(0, selectedIndex), ...layers.slice(selectedIndex + 1)];
    }

    const overrides = mapResultSelectionOverridesFromCatalog(catalog);
    const selectionKey = JSON.stringify(stableValue(overrides));
    const layerKey = JSON.stringify(layers.map((layer) => [
      locatorKey(layer.node), layer.kind, layer.sourcePath ?? null, layer.sourceRevision ?? null,
      layer.mapType ?? null,
      layer.kind === 'raster' ? (layer.rasterSource?.id ?? null) :
        (layer.resultSelections ?? []).map((choice) => [choice.selectionIndex, choice.resultPath, choice.resultRevision]),
    ]));
    const resultFrameKey = resultCapture?.snapshot.frames.map((frame) => [
      frame.id, frame.sourceID, frame.sourceMapID, frame.resultPath, frame.revision,
      frame.mapType, frame.profileIndex, frame.selectionIndices,
    ]) ?? [];
    const key = JSON.stringify([
      context.projectSessionID, context.projectPath, context.projectRevision, context.projectXml,
      document.id, document.mapID, document.mapIndex, document.name, document.mapType, document.visualizationRevision,
      catalog.projectRevision, catalog.projectXmlSHA, catalog.contextToken, selectionKey,
      context.coreLayerContext?.identityToken ?? null,
      resultFrames.status, resultFrames.activeNode?.node ? locatorKey(resultFrames.activeNode.node) : null,
      resultFrames.selectedProfileIndex, resultFrames.renderSnapshot?.selectedProfileIndex ?? null,
      resultFrames.renderSnapshot?.selectedProfileTime ?? null, resultFrameKey,
      associatedCaptures.map(capture => [capture.frameID, capture.kind, capture.sourcePath, capture.sourceRevision,
        capture.dependencies, capture.resultOwners]),
      exactMeshCapture?.captureKey ?? null, analysisCaptureKey,
      viewport.scale, viewport.offsetX, viewport.offsetY, size.width, size.height,
      pointer.project, pointer.screen, layerKey, activeTool, dragging, draftPointCount,
    ]);
    return {
      key,
      pointer,
      catalog,
      layers,
      selectionOverrides: overrides,
      profileSelections: resultCapture ? [resultCapture.profileSelection] : [],
      resultSnapshot: resultCapture?.snapshot ?? null,
      meshCapture: exactMeshCapture,
      analysisCaptureKey,
    };
  }, [activeTool, backend, context.projectPath, context.projectRevision, context.projectSessionID,
    context.projectXml, document, dragging, draftPointCount, imagesVisible, pointer, rasterCaptures, associatedCaptures,
    resultMeshCapture,
    resultAnalysisCapture,
    resultFrames.activeNode, resultFrames.renderSnapshot, resultFrames.selectedProfileIndex,
    resultFrames.sources, resultFrames.status, selectedBackendNodeID, size.height, size.width,
    context.coreLayerContext?.identityToken,
    viewport.offsetX, viewport.offsetY, viewport.scale]);

  activeKeyRef.current = plan?.key ?? null;

  useEffect(() => {
    const generation = ++generationRef.current;
    if (!plan) {
      setCurrent(null);
      return () => { generationRef.current++; };
    }

    setCurrent(null);
    const controller = new AbortController();
    const requestID = `map-hover-${generation}-${Date.now().toString(36)}`;
    let dwellTimer: number | null = window.setTimeout(() => {
      dwellTimer = null;
      void (async () => {
        try {
          const xmlSHA = await projectXmlSha(context.projectXml);
          if (controller.signal.aborted || generation !== generationRef.current ||
              activeKeyRef.current !== plan.key || !xmlSHA || !sameRevision(xmlSHA, plan.catalog.projectXmlSHA)) return;
          const map = {
            projectPath: context.projectPath,
            projectXml: context.projectXml,
            projectXmlSHA: xmlSHA,
            mapID: document!.mapID,
            mapIndex: document!.mapIndex,
            mapName: document!.name,
            mapType: document!.mapType,
            expectedVisualizationRevision: document!.visualizationRevision,
            projectSessionID: context.projectSessionID,
            coreLayerContext: context.coreLayerContext ?? null,
          };
          const point = { x: plan.pointer.project[0], y: plan.pointer.project[1] };
          const worldUnitsPerScreenPoint = 1 / viewport.scale;
          const request: Record<string, unknown> = {
            map,
            contextToken: plan.catalog.contextToken,
            selectionOverrides: plan.selectionOverrides,
            requestID,
            point,
            probePoint: { x: point.x + worldUnitsPerScreenPoint, y: point.y },
            worldUnitsPerScreenPoint,
            profileSelections: plan.profileSelections,
            layers: plan.layers,
          };
          const raw = await backend.request<unknown>('map.hover.identify', request, { signal: controller.signal });
          if (controller.signal.aborted || generation !== generationRef.current || activeKeyRef.current !== plan.key) return;
          const entries = parseEntries(raw, request, plan);
          if (entries.length > 0) setCurrent({ key: plan.key, screen: plan.pointer.screen, entries });
        } catch {
          // A stale or failed source capture must stay silent. It cannot replace
          // a fresh label or turn a failed layer read into a synthetic value.
          if (controller.signal.aborted || generation !== generationRef.current || activeKeyRef.current !== plan.key) return;
          setCurrent(null);
        }
      })();
    }, HOVER_DWELL_MS);

    return () => {
      if (dwellTimer !== null) window.clearTimeout(dwellTimer);
      controller.abort();
      if (generationRef.current === generation) generationRef.current++;
    };
  }, [backend, context.projectPath, context.projectRevision, context.projectSessionID,
    context.projectXml, document, language, plan?.key, viewport.scale]);

  const tooltip = current && current.key === plan?.key
    ? { screen: current.screen, entries: current.entries }
    : null;
  return { tooltip, loading: Boolean(plan && !tooltip) };
}
