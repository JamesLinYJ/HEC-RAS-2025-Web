import { useEffect, useMemo, useRef, useState } from 'react';
import type { ChangeEvent as ReactChangeEvent, KeyboardEvent as ReactKeyboardEvent, MouseEvent as ReactMouseEvent, PointerEvent as ReactPointerEvent, WheelEvent as ReactWheelEvent } from 'react';
import { text } from '../../shared/workbench';
import { DEFAULT_BACKGROUND_MAPS_VM_STATE, isBackgroundMapDraftDirty } from './BackgroundMapsVM';
import type { BackgroundMapsVMState } from './BackgroundMapsVM';
import { MapDocumentTabs } from './MapDocumentTabs';
import { MapLayerTree } from './MapLayerTree';
import { MapSettingsPanel } from './MapSettingsPanel';
import { RasterSymbologyEditor } from './symbology/RasterSymbologyEditor';
import { VectorSymbologyEditor } from './vector-symbology/VectorSymbologyEditor';
import { DEFAULT_MAP_DISPLAY_SETTINGS } from './MapSettings';
import type { MapDisplaySettings } from './MapSettings';
import { MapTimeAxis } from './MapTimeAxis';
import { MapToolsToolbar } from './MapToolsToolbar';
import type { MapToolID } from './MapToolsVM';
import { extentOfFeatures, parseGeoJsonFile, unionExtents } from './geoJson';
import { hasProject, loadMapDocumentCatalog, setMapNodeVisibility } from './ProjectTreeListener';
import { fitExtent, INITIAL_VIEWPORT, panViewport, screenToWorld, zoomAt } from './mapViewport';
import type { MapViewport } from './mapViewport';
import { RasProjectMap } from './RasProjectMap';
import { usePersistedVectorLayers } from './usePersistedVectorLayers';
import { usePersistedGdbLayers } from './usePersistedGdbLayers';
import { useMapVelocityField } from './useMapVelocityField';
import { usePersistedRasterLayers, mapRasterViewportFromCamera } from './usePersistedRasterLayers';
import { useMapResultFrames } from './useMapResultFrames';
import { useAssociatedMapLayers } from './useAssociatedMapLayers';
import { mapCatalogMatchesDocument, sameMapID } from './associatedMapLayers';
import { useMapHoverIdentify } from './useMapHoverIdentify';
import type { MapHoverPointer } from './useMapHoverIdentify';
import { rgbaFrameImage } from './mapImages';
import { importMapSources } from './importMapSources';
import { MapSourceImportControl } from './MapSourceImportControl';
import { RasBackgroundMaps } from './RasBackgroundMaps';
import { RasMapPositionIndicator } from './RasMapPositionIndicator';
import { RasMapUndoRedo } from './RasMapUndoRedo';
import type { GeoFeature, GeoGeometry, GeoJsonLayer, MapDocumentTab, MapImageLayer, XY } from './mapTypes';
import type { MapFeaturePageProps } from './mapTypes';
import { localizeProjectTreeSourceClass } from '../project/ProjectTreeModeSectionTitles';
import { mapCRS, mapDocumentKind, mapError, mapGeometryType } from './mapText';
import './map.css';

type SelectedFeature = { owner: string; layerID: string; featureID: string } | null;

interface DragState {
  pointerID: number;
  x: number;
  y: number;
}

interface FeatureContextMenu {
  x: number;
  y: number;
  layer: GeoJsonLayer;
  feature: GeoFeature;
}

interface MapEditSnapshot {
  layers: GeoJsonLayer[];
  points: XY[];
}

interface MapOwnerState extends MapEditSnapshot {
  undo: MapEditSnapshot[];
  redo: MapEditSnapshot[];
}

const EMPTY_MAP_OWNER_STATE: MapOwnerState = { layers: [], points: [], undo: [], redo: [] };
const MAX_MAP_EDIT_HISTORY = 100;

function numberLabel(value: number): string {
  if (!Number.isFinite(value)) return '—';
  return Number(value.toPrecision(7)).toString();
}

function pointFromEvent(event: ReactPointerEvent<SVGSVGElement> | ReactWheelEvent<SVGSVGElement>): XY {
  const rect = event.currentTarget.getBoundingClientRect();
  const width = Math.max(rect.width, 1);
  const height = Math.max(rect.height, 1);
  return [
    (event.clientX - rect.left) * (event.currentTarget.viewBox.baseVal.width / width),
    (event.clientY - rect.top) * (event.currentTarget.viewBox.baseVal.height / height),
  ];
}

function closeRing(points: readonly XY[]): XY[] {
  if (points.length === 0) return [];
  const ring = [...points];
  const first = ring[0];
  const last = ring[ring.length - 1];
  if (first[0] !== last[0] || first[1] !== last[1]) ring.push(first);
  return ring;
}

function makeDrawnLayer(geometry: GeoGeometry, crs: string, index: number, language: 'zh' | 'en'): GeoJsonLayer | null {
  const kind = geometry.type === 'Point' ? text(language, '点', 'Point')
    : geometry.type === 'LineString' ? text(language, '线', 'Line')
      : text(language, '面', 'Area');
  const name = text(language, `${kind}绘制草稿`, `${kind} draft`);
  const feature: GeoFeature = {
    id: crypto.randomUUID(),
    name: name + ' ' + (index + 1),
    geometry,
    properties: { source: text(language, '浏览器绘制草稿', 'Browser drawing draft') },
  };
  const extent = extentOfFeatures([feature]);
  if (!extent) return null;
  return {
    id: crypto.randomUUID(),
    name: feature.name,
    sourceName: text(language, '未保存的绘制', 'Unsaved drawing'),
    crs,
    features: [feature],
    extent,
    visible: true,
    opacity: 1,
  };
}

function measurement(points: readonly XY[], area: boolean): number {
  if (area) {
    let twiceArea = 0;
    for (let index = 0; index < points.length; index += 1) {
      const current = points[index];
      const next = points[(index + 1) % points.length];
      twiceArea += current[0] * next[1] - next[0] * current[1];
    }
    return Math.abs(twiceArea) / 2;
  }
  let length = 0;
  for (let index = 1; index < points.length; index += 1) {
    length += Math.hypot(points[index][0] - points[index - 1][0], points[index][1] - points[index - 1][1]);
  }
  return length;
}

function mapToolHelp(tool: MapToolID, language: 'zh' | 'en'): string {
  const zh: Record<MapToolID, string> = {
    select: '点击要素可查看属性；右键打开要素操作。',
    pan: '拖动地图平移，滚轮围绕指针缩放。',
    drawPoint: '在地图上点击放置点；按 Escape 取消。',
    drawLine: '逐点绘制线；按 Enter 完成、Backspace 撤销顶点、Escape 取消。',
    drawPolygon: '逐点绘制面边界；按 Enter 完成、Backspace 撤销顶点、Escape 取消。',
    measureLength: '点击添加测距顶点；按 Enter 完成测量、Backspace 撤销、Escape 清除。',
    measureArea: '点击添加测面积边界点；按 Enter 完成、Backspace 撤销、Escape 清除。',
  };
  const en: Record<MapToolID, string> = {
    select: 'Select a feature to inspect its properties; right-click for feature actions.',
    pan: 'Drag to pan. Scroll to zoom around the pointer.',
    drawPoint: 'Click the map to place a point. Press Escape to cancel.',
    drawLine: 'Add line vertices. Enter finishes, Backspace undoes a vertex, Escape cancels.',
    drawPolygon: 'Add boundary vertices. Enter finishes, Backspace undoes a vertex, Escape cancels.',
    measureLength: 'Click to add measurement vertices. Enter finishes, Backspace undoes, Escape clears.',
    measureArea: 'Click to add area vertices. Enter finishes, Backspace undoes, Escape clears.',
  };
  return language === 'zh' ? zh[tool] : en[tool];
}

function MapPage({ language, context, backend, backendRevision, onDirtyStateChange }: MapFeaturePageProps) {
  const [documents, setDocuments] = useState<MapDocumentTab[]>([]);
  const [activeDocumentID, setActiveDocumentID] = useState<string | null>(null);
  const [mapCatalogLoading, setMapCatalogLoading] = useState(false);
  const [mapCatalogError, setMapCatalogError] = useState<unknown>(null);
  const [visualizationRevision, setVisualizationRevision] = useState<string | null>(null);
  const [mapStateByOwner, setMapStateByOwner] = useState<Record<string, MapOwnerState>>({});
  const [backgroundSettingsByOwner, setBackgroundSettingsByOwner] = useState<Record<string, BackgroundMapsVMState>>({});
  const [viewport, setViewport] = useState<MapViewport>(INITIAL_VIEWPORT);
  const [size, setSize] = useState({ width: 880, height: 620 });
  const [activeTool, setActiveTool] = useState<MapToolID>('select');
  const [settings, setSettings] = useState<MapDisplaySettings>(DEFAULT_MAP_DISPLAY_SETTINGS);
  const [selectedFeature, setSelectedFeature] = useState<SelectedFeature>(null);
  const [selectedBackendNode, setSelectedBackendNode] = useState<{ owner: string; node: GeoFeature | null; nodeID: string; name: string; sourceClass: string; diagnostic?: string } | null>(null);
  const [cursorScreen, setCursorScreen] = useState<XY | null>(null);
  const [hoverPointer, setHoverPointer] = useState<MapHoverPointer | null>(null);
  const [contextMenu, setContextMenu] = useState<FeatureContextMenu | null>(null);
  const [statusMessage, setStatusMessage] = useState('');
  const [measurementResult, setMeasurementResult] = useState<string | null>(null);
  const [importError, setImportError] = useState('');
  const [busyNodeID, setBusyNodeID] = useState<string | null>(null);
  const [sourceImportBusy, setSourceImportBusy] = useState(false);
  const sourceImportRef = useRef<AbortController | null>(null);
  const fittedSourceKey = useRef<string | null>(null);
  const viewportRef = useRef<HTMLDivElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const dragRef = useRef<DragState | null>(null);
  const requestGenerationRef = useRef(0);
  const mapCatalogGenerationRef = useRef(0);
  const activeOwnerRef = useRef<string | null>(null);
  const projectContextKey = JSON.stringify([context.projectSessionID, context.projectPath, context.projectRevision, context.projectXml]);
  const projectContextKeyRef = useRef(projectContextKey);
  projectContextKeyRef.current = projectContextKey;
  const [mapReloadKey, setMapReloadKey] = useState(0);
  const selectedResultIdentity = context.selectedAsset?.kind === 'Result'
    ? JSON.stringify([context.selectedAsset.path, context.selectedAsset.revision]) : null;
  const activeDocument = documents.find((document) => document.id === activeDocumentID) ?? null;
  const activeDocumentRef = useRef(activeDocument);
  activeDocumentRef.current = activeDocument;
  const symbologySourceRef = useRef<{
    projectKey: string; documentID: string; nodeID: string;
    acceptedVisualizationRevision: string; source: Record<string, unknown>;
  } | null>(null);
  const owner = activeDocument?.id ?? ('local:' + context.projectSessionID);
  const ownerState = mapStateByOwner[owner] ?? EMPTY_MAP_OWNER_STATE;
  const activeLayers = ownerState.layers;
  const draftPoints = ownerState.points;
  const backgroundSettings = backgroundSettingsByOwner[owner] ?? DEFAULT_BACKGROUND_MAPS_VM_STATE;
  const backgroundDraftDirty = isBackgroundMapDraftDirty(backgroundSettings);
  const persistedVectors = usePersistedVectorLayers({ backend, context, document: activeDocument, language });
  const persistedGdb = usePersistedGdbLayers({ backend, context, document: activeDocument, language });
  const persistedRasters = usePersistedRasterLayers({ backend, context, document: activeDocument, language });
  const resultViewport = useMemo(() => mapRasterViewportFromCamera(viewport, size), [viewport, size]);
  const resultFrames = useMapResultFrames({ backend, context, document: activeDocument, language, viewport: resultViewport, backendRevision });
  const associatedLayers = useAssociatedMapLayers({ backend, context, document: activeDocument,
    viewport: resultViewport, controller: resultFrames, language, backendRevision });
  const mapTreeDocument = useMemo(() => activeDocument && mapCatalogMatchesDocument(resultFrames.sources, context, activeDocument)
    ? { ...activeDocument, roots: resultFrames.sources.treeRoots } : activeDocument,
  [activeDocument, context.projectPath, context.projectRevision, resultFrames.sources]);
  const velocityField = useMapVelocityField({ backend, context, document: activeDocument, controller: resultFrames,
    viewport: resultViewport, enabled: settings.IsMapOverlayVisible && settings.showVelocityArrows, language });
  const allMapLayers = useMemo(() => [...persistedVectors.layers, ...persistedGdb.layers, ...activeLayers], [persistedVectors.layers, persistedGdb.layers, activeLayers]);
  const resultImages = useMemo((): { layers: MapImageLayer[]; error: unknown } => {
    try {
      return { layers: resultFrames.layers.map((layer) => ({ id: layer.id, name: layer.resultName,
        imageSrc: rgbaFrameImage(layer.rgba, layer.width, layer.height),
        sourceKind: 'result', sourcePath: layer.resultPath, profileIndex: layer.profileIndex,
        extent: { minX: layer.minX, minY: layer.minY, maxX: layer.maxX, maxY: layer.maxY } })), error: null };
    } catch (error) { return { layers: [], error }; }
  }, [resultFrames.layers]);
  const mapImages = useMemo<MapImageLayer[]>(() => [
    ...associatedLayers.layers,
    ...persistedRasters.layers.map((layer) => ({ ...layer, sourceKind: 'raster' as const,
      sampling: layer.hoverCapture.mapType === 'DiscreteRasterMapLayer' ? 'nearest' as const : undefined })),
    ...resultImages.layers,
  ], [associatedLayers.layers, persistedRasters.layers, resultImages.layers]);
  const visibleLayers = allMapLayers.filter((layer) => layer.visible);
  const mapImagesVisible = visibleLayers.length === 0 || visibleLayers[visibleLayers.length - 1]?.coordinateSpace === 'project';
  const currentHoverPointer = hoverPointer && hoverPointer.screen[0] >= 0 && hoverPointer.screen[0] <= size.width &&
    hoverPointer.screen[1] >= 0 && hoverPointer.screen[1] <= size.height
    ? { screen: hoverPointer.screen, project: screenToWorld(hoverPointer.screen, viewport) }
    : null;
  const cursorCoordinate = cursorScreen ? screenToWorld(cursorScreen, viewport) : null;
  const mapHover = useMapHoverIdentify({
    backend,
    context,
    document: activeDocument,
    language,
    pointer: currentHoverPointer,
    activeTool,
    dragging: Boolean(dragRef.current),
    draftPointCount: draftPoints.length,
    viewport,
    size,
    imagesVisible: mapImagesVisible,
    rasterCaptures: persistedRasters.layers.filter((layer) => currentHoverPointer &&
      currentHoverPointer.project[0] >= layer.extent.minX && currentHoverPointer.project[0] <= layer.extent.maxX &&
      currentHoverPointer.project[1] >= layer.extent.minY && currentHoverPointer.project[1] <= layer.extent.maxY)
      .map((layer) => layer.hoverCapture),
    resultFrames,
    associatedCaptures: associatedLayers.layers.filter(layer => currentHoverPointer &&
      currentHoverPointer.project[0] >= layer.extent.minX && currentHoverPointer.project[0] <= layer.extent.maxX &&
      currentHoverPointer.project[1] >= layer.extent.minY && currentHoverPointer.project[1] <= layer.extent.maxY)
      .map(layer => layer.hoverCapture),
    selectedBackendNodeID: selectedBackendNode?.owner === owner ? selectedBackendNode.nodeID : null,
  });
  const selectedLocalLayerID = selectedFeature?.owner === owner ? selectedFeature.layerID : null;
  const selectedFeatureDetails = useMemo(() => {
    if (!selectedFeature || selectedFeature.owner !== owner) return null;
    const layer = allMapLayers.find((item) => item.id === selectedFeature.layerID);
    const feature = layer?.features.find((item) => item.id === selectedFeature.featureID);
    return layer && feature ? { layer, feature } : null;
  }, [allMapLayers, owner, selectedFeature]);

  useEffect(() => {
    if (!activeDocument || persistedVectors.loading || persistedGdb.loading || persistedRasters.status === 'loading' || resultFrames.status === 'loading' || associatedLayers.status === 'loading') return;
    const key = JSON.stringify([owner, activeDocument.visualizationRevision, resultFrames.activeNodeKey,
      resultFrames.selectedSource?.resultPath, resultFrames.selectedSource?.revision]);
    if (fittedSourceKey.current === key) return;
    const extent = resultFrames.resultExtent ?? unionExtents([...persistedVectors.layers.map((layer) => layer.extent),
      ...persistedGdb.layers.map((layer) => layer.extent),
      ...persistedRasters.layers.map((layer) => layer.extent), ...associatedLayers.sourceExtents]);
    if (!extent) return;
    fittedSourceKey.current = key;
    setViewport(fitExtent(extent, size, 72));
  }, [activeDocument, owner, persistedVectors.layers, persistedVectors.loading, persistedGdb.layers, persistedGdb.loading, persistedRasters.layers,
    persistedRasters.status, resultFrames.activeNodeKey, resultFrames.selectedSource, resultFrames.resultExtent, resultFrames.status,
    associatedLayers.status, associatedLayers.sourceExtents, size]);

  useEffect(() => {
    sourceImportRef.current?.abort();
    sourceImportRef.current = null;
    setSourceImportBusy(false);
    return () => sourceImportRef.current?.abort();
  }, [projectContextKey]);

  useEffect(() => {
    const host = viewportRef.current;
    if (!host) return;
    const update = () => {
      const bounds = host.getBoundingClientRect();
      setSize({ width: Math.max(1, bounds.width), height: Math.max(1, bounds.height) });
    };
    update();
    const observer = new ResizeObserver(update);
    observer.observe(host);
    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    if (!onDirtyStateChange) return;
    const dirtyOwner = 'map:' + owner;
    onDirtyStateChange(dirtyOwner, activeLayers.length > 0 || draftPoints.length > 0 || backgroundDraftDirty || sourceImportBusy);
    return () => onDirtyStateChange(dirtyOwner, false);
  }, [activeLayers.length, backgroundDraftDirty, draftPoints.length, onDirtyStateChange, owner, sourceImportBusy]);

  useEffect(() => {
    if (activeOwnerRef.current === null) {
      activeOwnerRef.current = owner;
      return;
    }
    if (activeOwnerRef.current !== owner) {
      activeOwnerRef.current = owner;
      requestGenerationRef.current += 1;
    }
  }, [owner]);

  useEffect(() => {
    requestGenerationRef.current += 1;
    setDocuments([]);
    setActiveDocumentID(null);
    setVisualizationRevision(null);
    setMapCatalogLoading(Boolean(context.projectPath));
    setMapCatalogError(null);
    setSelectedFeature(null);
    setSelectedBackendNode(null);
    setBusyNodeID(null);
    setMapStateByOwner({});
    setBackgroundSettingsByOwner({});
  }, [context.projectPath, context.projectSessionID]);

  useEffect(() => {
    const generation = ++mapCatalogGenerationRef.current;
    const requestContextKey = projectContextKey;
    const isCurrent = () => generation === mapCatalogGenerationRef.current && projectContextKeyRef.current === requestContextKey;
    setMapCatalogError(null);
    setDocuments([]);
    setActiveDocumentID(null);
    setVisualizationRevision(null);
    if (!context.projectPath) {
      setMapCatalogLoading(false);
      return () => { mapCatalogGenerationRef.current += 1; };
    }

    const controller = new AbortController();
    setMapCatalogLoading(true);
    void loadMapDocumentCatalog(backend, context, { signal: controller.signal }).then((catalog) => {
      if (!isCurrent()) return;
      setDocuments(catalog.documents);
      setVisualizationRevision(catalog.visualizationRevision);
      const active = catalog.documents.find((document) => document.mapIndex === catalog.activeMapIndex) ?? catalog.documents[0] ?? null;
      setActiveDocumentID(active?.id ?? null);
      setMapCatalogLoading(false);
    }).catch((error: unknown) => {
      if (!isCurrent() || controller.signal.aborted) return;
      setMapCatalogError(error);
      setMapCatalogLoading(false);
    });

    return () => {
      controller.abort();
      mapCatalogGenerationRef.current += 1;
    };
  }, [backend, backendRevision, projectContextKey, selectedResultIdentity, mapReloadKey]);

  const updateActiveLayers = (update: (layers: GeoJsonLayer[]) => GeoJsonLayer[]) => {
    setMapStateByOwner((current) => {
      const state = current[owner] ?? EMPTY_MAP_OWNER_STATE;
      return { ...current, [owner]: { ...state, layers: update(state.layers), undo: [], redo: [] } };
    });
  };

  const editMapDraft = (update: (state: MapOwnerState) => MapOwnerState) => {
    setMapStateByOwner((current) => {
      const state = current[owner] ?? EMPTY_MAP_OWNER_STATE;
      const next = update(state);
      if (next === state || (next.layers === state.layers && next.points === state.points)) return current;
      const snapshot: MapEditSnapshot = { layers: state.layers, points: state.points };
      return {
        ...current,
        [owner]: {
          ...next,
          undo: [...state.undo, snapshot].slice(-MAX_MAP_EDIT_HISTORY),
          redo: [],
        },
      };
    });
  };

  const updateDraftPoints = (update: (points: XY[]) => XY[]) => {
    editMapDraft((state) => {
      const points = update(state.points);
      const isSame = points.length === state.points.length && points.every((point, index) =>
        point[0] === state.points[index][0] && point[1] === state.points[index][1]);
      return isSame ? state : { ...state, points };
    });
  };

  const undoMapDraft = () => {
    setMapStateByOwner((current) => {
      const state = current[owner] ?? EMPTY_MAP_OWNER_STATE;
      const previous = state.undo[state.undo.length - 1];
      if (!previous) return current;
      const currentSnapshot: MapEditSnapshot = { layers: state.layers, points: state.points };
      return {
        ...current,
        [owner]: {
          ...previous,
          undo: state.undo.slice(0, -1),
          redo: [...state.redo, currentSnapshot],
        },
      };
    });
    setSelectedFeature(null);
  };

  const redoMapDraft = () => {
    setMapStateByOwner((current) => {
      const state = current[owner] ?? EMPTY_MAP_OWNER_STATE;
      const next = state.redo[state.redo.length - 1];
      if (!next) return current;
      const currentSnapshot: MapEditSnapshot = { layers: state.layers, points: state.points };
      return {
        ...current,
        [owner]: {
          ...next,
          undo: [...state.undo, currentSnapshot].slice(-MAX_MAP_EDIT_HISTORY),
          redo: state.redo.slice(0, -1),
        },
      };
    });
    setSelectedFeature(null);
  };

  const updateBackgroundDraft = (next: BackgroundMapsVMState) => {
    setBackgroundSettingsByOwner((current) => ({ ...current, [owner]: next }));
  };

  const loadedLayerExtent = (nodeID: string) =>
    associatedLayers.layers.find(image => image.treeNodeID === nodeID)?.sourceExtent ??
    persistedRasters.layers.find((image) => image.treeNodeID === nodeID)?.extent ??
    mapImages.find((image) => image.id === nodeID)?.extent ??
    allMapLayers.find((layer) => layer.id === nodeID ||
      layer.id === `persisted-vector:${activeDocument?.id}:${nodeID}` ||
      layer.id === `persisted-gdb-vector:${activeDocument?.id}:${nodeID}`)?.extent ?? null;

  const zoomToLoadedLayer = (nodeID: string) => {
    const extent = loadedLayerExtent(nodeID);
    if (extent) setViewport(fitExtent(extent, size, 72));
  };

  const fitVisibleLayers = () => {
    const top = visibleLayers[visibleLayers.length - 1];
    const key = (layer: GeoJsonLayer) => layer.coordinateSpace === 'project' ? 'project' : layer.crs;
    const fitLayers = top ? visibleLayers.filter((layer) => key(layer) === key(top)) : visibleLayers;
    const extent = unionExtents([...fitLayers.map((layer) => layer.extent),
      ...(!top || top.coordinateSpace === 'project' ? mapImages.map((image) => image.extent) : [])]);
    if (extent) setViewport(fitExtent(extent, size, 72));
  };

  const handleSourceImport = async (files: File[], options?: { directory?: boolean }) => {
    if (sourceImportBusy || mapCatalogLoading || mapCatalogError || !context.projectPath || (!activeDocument && documents.length > 0)) return;
    const controller = new AbortController();
    const capturedKey = projectContextKey;
    sourceImportRef.current = controller;
    setSourceImportBusy(true);
    setStatusMessage(text(language, '正在上传并导入工程图层…', 'Uploading and importing project layers…'));
    const current = () => sourceImportRef.current === controller && !controller.signal.aborted && projectContextKeyRef.current === capturedKey;
    try {
      const report = await importMapSources(backend, context, activeDocument, visualizationRevision, files, controller.signal, options);
      if (!current()) return;
      const catalog = await loadMapDocumentCatalog(backend, context, { signal: controller.signal });
      if (!current()) return;
      const last = report.receipts.at(-1);
      const active = catalog.documents.find((item) => item.mapIndex === (last?.mapIndex ?? activeDocument?.mapIndex)) ?? catalog.documents[0] ?? null;
      setDocuments(catalog.documents);
      setVisualizationRevision(catalog.visualizationRevision);
      setActiveDocumentID(active?.id ?? null);
      setMapCatalogError(null);
      setStatusMessage(report.failures.length > 0
        ? text(language, `已导入 ${report.imported.length} 个来源；${report.failures[0].fileName} 未能完成：`, `Imported ${report.imported.length} sources; ${report.failures[0].fileName} could not complete: `) + mapError(language, report.failures[0].error)
        : text(language, `已保存 ${report.imported.length} 个工程图层来源。`, `Saved ${report.imported.length} project layer sources.`));
    } catch (error) {
      if (current()) setStatusMessage(mapError(language, error));
    } finally {
      if (sourceImportRef.current === controller) { sourceImportRef.current = null; setSourceImportBusy(false); }
    }
  };

  const handleCreateDocument = (document: MapDocumentTab) => {
    requestGenerationRef.current += 1;
    setVisualizationRevision(document.visualizationRevision);
    setDocuments((current) => [
      ...current.map((item) => ({ ...item, visualizationRevision: document.visualizationRevision })),
      document,
    ]);
    setActiveDocumentID(document.id);
    setSelectedFeature(null);
    setSelectedBackendNode(null);
    setStatusMessage(text(language, '已在工程 .vis 中创建地图文档：', 'Saved a map document in the project .vis file: ') + document.name);
    if (document.treeError) {
      setStatusMessage(text(language, '地图已保存，但图层树暂不可用：', 'Map saved, but its layer tree is unavailable: ') + mapError(language, document.treeError));
      const generation = ++mapCatalogGenerationRef.current;
      const requestContextKey = projectContextKey;
      const isCurrent = () => generation === mapCatalogGenerationRef.current && projectContextKeyRef.current === requestContextKey;
      setMapCatalogError(null);
      setMapCatalogLoading(true);
      void loadMapDocumentCatalog(backend, context).then((catalog) => {
        if (!isCurrent()) return;
        setDocuments(catalog.documents);
        setVisualizationRevision(catalog.visualizationRevision);
        const active = catalog.documents.find((item) => item.mapIndex === catalog.activeMapIndex) ?? catalog.documents[0] ?? null;
        setActiveDocumentID(active?.id ?? null);
        setMapCatalogLoading(false);
      }).catch((error: unknown) => {
        if (!isCurrent()) return;
        setMapCatalogError(error);
        setMapCatalogLoading(false);
      });
    }
  };

  const handleImport = async (event: ReactChangeEvent<HTMLInputElement>) => {
    const file = event.currentTarget.files?.[0];
    event.currentTarget.value = '';
    if (!file) return;
    const requestOwner = owner;
    const requestGeneration = requestGenerationRef.current;
    setImportError('');
    try {
      const source = await file.text();
      if (requestOwner !== activeOwnerRef.current || requestGeneration !== requestGenerationRef.current) return;
      const layer = parseGeoJsonFile(file.name, source, language);
      const fittedViewport = fitExtent(layer.extent, size, 72);
      updateActiveLayers((current) => [...current, layer]);
      setSelectedFeature(null);
      setSelectedBackendNode(null);
      setViewport(fittedViewport);
      setStatusMessage(text(language, '已读取 ' + layer.features.length + ' 个要素；当前为本地预览。', 'Read ' + layer.features.length + ' features; this is a local preview.'));
    } catch (error) {
      if (requestOwner === activeOwnerRef.current && requestGeneration === requestGenerationRef.current) setImportError(mapError(language, error));
    }
  };

  const toggleBackendNode = async (node: import('./mapTypes').CatalogTreeNode, checked: boolean) => {
    if (sourceImportBusy) return;
    if (!activeDocument || !node.locator) return;
    const selectedDocument = activeDocument;
    const requestContextKey = projectContextKey;
    setBusyNodeID(node.id);
    setStatusMessage('');
    try {
      const selections = mapCatalogMatchesDocument(resultFrames.sources, context, selectedDocument)
        ? resultFrames.sources.treeSelectionOverrides : undefined;
      const result = await setMapNodeVisibility(backend, context, selectedDocument, node.locator, checked, selections);
      if (projectContextKeyRef.current !== requestContextKey) return;
      const previousVisualizationRevision = result.previousVisualizationRevision;
      const selectedVisualizationRevision = selectedDocument.visualizationRevision;
      const nextVisualizationRevision = result.visualizationRevision;
      const visibilityTransition = previousVisualizationRevision && selectedVisualizationRevision && nextVisualizationRevision &&
        previousVisualizationRevision.trim() && selectedVisualizationRevision.trim() && nextVisualizationRevision.trim() &&
        previousVisualizationRevision.toUpperCase() === selectedVisualizationRevision.toUpperCase() &&
        previousVisualizationRevision.toUpperCase() !== nextVisualizationRevision.toUpperCase()
        ? { previousVisualizationRevision, visualizationRevision: nextVisualizationRevision }
        : null;
      setVisualizationRevision(result.visualizationRevision);
      setDocuments((current) => current.map((document) => document.id === selectedDocument.id
        ? { ...document, roots: result.roots, visualizationRevision: result.visualizationRevision,
          visibilityTransition, treeError: undefined }
        : { ...document, visualizationRevision: result.visualizationRevision, visibilityTransition: null }));
      setStatusMessage(text(language, '图层可见性已保存到地图文档。', 'Layer visibility was saved to the map document.'));
    } catch (error) {
      if (projectContextKeyRef.current !== requestContextKey) return;
      let recoveryMessage = '';
      try {
        const refreshed = await loadMapDocumentCatalog(backend, context);
        if (projectContextKeyRef.current === requestContextKey) {
          setDocuments(refreshed.documents);
          setVisualizationRevision(refreshed.visualizationRevision);
          recoveryMessage = text(language, ' 已重新读取工程中的所有地图。', ' All saved project maps were reloaded.');
        }
      } catch (refreshError) {
        if (projectContextKeyRef.current === requestContextKey) recoveryMessage = ' ' + mapError(language, refreshError);
      }
      if (projectContextKeyRef.current === requestContextKey) setStatusMessage(mapError(language, error) + recoveryMessage);
    } finally {
      if (projectContextKeyRef.current === requestContextKey) setBusyNodeID(null);
    }
  };

  const toWorld = (event: ReactPointerEvent<SVGSVGElement>): XY => screenToWorld(pointFromEvent(event), viewport);

  const appendDrawnGeometry = (geometry: GeoGeometry) => {
    const crs = visibleLayers[visibleLayers.length - 1]?.crs ?? 'Unknown CRS';
    const layer = makeDrawnLayer(geometry, crs, activeLayers.length, language);
    if (!layer) return;
    if (visibleLayers[visibleLayers.length - 1]?.coordinateSpace === 'project' ||
        visibleLayers.length === 0 && mapImages.length > 0) layer.coordinateSpace = 'project';
    editMapDraft((state) => ({ ...state, layers: [...state.layers, layer], points: [] }));
    setSelectedFeature({ owner, layerID: layer.id, featureID: layer.features[0].id });
    setSelectedBackendNode(null);
    setMeasurementResult(null);
    setStatusMessage(text(language, '绘制草稿已加入本地预览，尚未保存到工程。', 'The drawing draft was added to the local preview and has not been saved to the project.'));
  };

  const finishSketch = () => {
    if (activeTool === 'drawLine' && draftPoints.length >= 2) {
      appendDrawnGeometry({ type: 'LineString', coordinates: [...draftPoints] });
    } else if (activeTool === 'drawPolygon' && draftPoints.length >= 3) {
      appendDrawnGeometry({ type: 'Polygon', coordinates: [closeRing(draftPoints)] });
    } else if (activeTool === 'measureLength' && draftPoints.length >= 2) {
      const value = measurement(draftPoints, false);
      setMeasurementResult(text(language, '长度（源坐标单位）: ', 'Length (source units): ') + numberLabel(value));
      updateDraftPoints(() => []);
      setStatusMessage(text(language, '平面距离测量完成；单位沿用源坐标。', 'Planar distance measurement completed in source coordinate units.'));
    } else if (activeTool === 'measureArea' && draftPoints.length >= 3) {
      const value = measurement(draftPoints, true);
      setMeasurementResult(text(language, '面积（源坐标单位²）: ', 'Area (source units²): ') + numberLabel(value));
      updateDraftPoints(() => []);
      setStatusMessage(text(language, '平面面积测量完成；单位沿用源坐标。', 'Planar area measurement completed in source coordinate units.'));
    } else {
      setStatusMessage(text(language, '需要更多顶点才能完成当前操作。', 'Add more vertices to finish this operation.'));
    }
  };

  const handleCanvasClick = (event: ReactPointerEvent<SVGSVGElement>) => {
    setHoverPointer(null);
    setContextMenu(null);
    if (activeTool === 'select') {
      setSelectedFeature(null);
      setSelectedBackendNode(null);
      return;
    }
    if (activeTool === 'pan') return;
    const coordinate = toWorld(event);
    if (activeTool === 'drawPoint') {
      appendDrawnGeometry({ type: 'Point', coordinates: coordinate });
      return;
    }
    updateDraftPoints((current) => [...current, coordinate]);
    setMeasurementResult(null);
  };

  const handlePointerDown = (event: ReactPointerEvent<SVGSVGElement>) => {
    setHoverPointer(null);
    if (event.button === 1 || activeTool === 'pan' || (event.button === 0 && event.altKey)) {
      event.preventDefault();
      event.currentTarget.setPointerCapture(event.pointerId);
      dragRef.current = { pointerID: event.pointerId, x: event.clientX, y: event.clientY };
    }
  };

  const handlePointerMove = (event: ReactPointerEvent<SVGSVGElement>) => {
    const screen = pointFromEvent(event);
    setCursorScreen(screen);
    const drag = dragRef.current;
    if (drag && drag.pointerID === event.pointerId) {
      setHoverPointer(null);
      const dx = event.clientX - drag.x;
      const dy = event.clientY - drag.y;
      dragRef.current = { ...drag, x: event.clientX, y: event.clientY };
      setViewport((current) => panViewport(current, [dx, dy]));
      return;
    }
    const project = screenToWorld(screen, viewport);
    setHoverPointer({ screen, project });
  };

  const handlePointerLeave = () => {
    setHoverPointer(null);
    setCursorScreen(null);
  };

  const handlePointerUp = (event: ReactPointerEvent<SVGSVGElement>) => {
    if (dragRef.current?.pointerID === event.pointerId) dragRef.current = null;
    if (event.currentTarget.hasPointerCapture(event.pointerId)) event.currentTarget.releasePointerCapture(event.pointerId);
  };

  const handleWheel = (event: ReactWheelEvent<SVGSVGElement>) => {
    event.preventDefault();
    setHoverPointer(null);
    const factor = Math.exp(-event.deltaY * 0.0015);
    setViewport((current) => zoomAt(current, pointFromEvent(event), factor));
  };

  const handleCanvasKey = (event: ReactKeyboardEvent<HTMLDivElement>) => {
    const target = event.target;
    if (!(target instanceof Element) || target.closest('input, textarea, select, [contenteditable]')) return;
    const isUndoShortcut = (event.metaKey || event.ctrlKey) && !event.altKey && event.key.toLowerCase() === 'z';
    const isRedoShortcut = (event.metaKey || event.ctrlKey) && !event.altKey && (event.key.toLowerCase() === 'y' || (event.key.toLowerCase() === 'z' && event.shiftKey));
    if (isUndoShortcut && !event.shiftKey && ownerState.undo.length > 0) {
      event.preventDefault();
      undoMapDraft();
      return;
    }
    if (isRedoShortcut && ownerState.redo.length > 0) {
      event.preventDefault();
      redoMapDraft();
      return;
    }
    if (!target.closest('svg.map-canvas')) return;
    if (event.key === 'Escape') {
      event.preventDefault();
      updateDraftPoints(() => []);
      setMeasurementResult(null);
      setStatusMessage(text(language, '已取消当前绘制或测量草稿。', 'The current drawing or measurement draft was cancelled.'));
    } else if (event.key === 'Backspace') {
      event.preventDefault();
      if (draftPoints.length > 0) updateDraftPoints((current) => current.slice(0, -1));
    } else if (event.key === 'Enter' && draftPoints.length > 0) {
      event.preventDefault();
      finishSketch();
    }
  };

  const handleToolChange = (tool: MapToolID) => {
    setHoverPointer(null);
    if (draftPoints.length > 0 && tool !== activeTool) setStatusMessage(text(language, '切换工具已取消未完成草稿。', 'Changing tools cancelled the unfinished draft.'));
    if (draftPoints.length > 0) updateDraftPoints(() => []);
    setMeasurementResult(null);
    setActiveTool(tool);
    setContextMenu(null);
  };

  const showFeatureContext = (layer: GeoJsonLayer, feature: GeoFeature, event: ReactMouseEvent<SVGGElement>) => {
    setSelectedFeature({ owner, layerID: layer.id, featureID: feature.id });
    setSelectedBackendNode(null);
    const bounds = viewportRef.current?.getBoundingClientRect();
    if (!bounds) return;
    setContextMenu({
      x: Math.max(8, Math.min(event.clientX - bounds.left, bounds.width - 190)),
      y: Math.max(8, Math.min(event.clientY - bounds.top, bounds.height - 132)),
      layer,
      feature,
    });
  };

  const zoomToContextFeature = () => {
    const extent = extentOfFeatures([contextMenu!.feature]);
    if (extent) setViewport(fitExtent(extent, size, 110));
    setContextMenu(null);
  };

  const copyContextFeature = async () => {
    if (!contextMenu) return;
    const feature = {
      type: 'Feature',
      id: contextMenu.feature.id,
      geometry: contextMenu.feature.geometry,
      properties: contextMenu.feature.properties,
    };
    const projectCoordinates = contextMenu.layer.coordinateSpace === 'project';
    const data = projectCoordinates
      ? { coordinateSystem: { space: 'project', definition: contextMenu.layer.crs }, feature }
      : feature;
    try {
      await navigator.clipboard.writeText(JSON.stringify(data, null, 2));
      setStatusMessage(projectCoordinates
        ? text(language, '工程坐标要素 JSON 已复制，包含坐标系说明。', 'Project-coordinate feature JSON copied with its coordinate-system definition.')
        : text(language, '要素 GeoJSON 已复制。', 'Feature GeoJSON copied.'));
    } catch (error) {
      setStatusMessage(mapError(language, error));
    }
    setContextMenu(null);
  };

  const moveLocalLayer = (layerID: string, direction: -1 | 1) => {
    updateActiveLayers((current) => {
      const from = current.findIndex((layer) => layer.id === layerID);
      const to = from + direction;
      if (from < 0 || to < 0 || to >= current.length) return current;
      const reordered = [...current];
      const [layer] = reordered.splice(from, 1);
      reordered.splice(to, 0, layer);
      return reordered;
    });
  };

  const removeLocalLayer = (layerID: string) => {
    updateActiveLayers((current) => current.filter((layer) => layer.id !== layerID));
    if (selectedFeature?.layerID === layerID) setSelectedFeature(null);
    setStatusMessage(text(language, '已移除本地预览图层。', 'Removed the local preview layer.'));
  };

  const fitFeature = (layer: GeoJsonLayer, feature: GeoFeature) => {
    if (!visibleLayers.some((item) => item.id === layer.id)) return;
    const extent = extentOfFeatures([feature]);
    if (extent) setViewport(fitExtent(extent, size, 100));
  };

  const selectedBackend = selectedBackendNode?.owner === owner ? selectedBackendNode : null;
  const selectedRaster = selectedBackend && activeDocument
    ? persistedRasters.layers.find(layer => layer.treeNodeID === selectedBackend.nodeID &&
      layer.visualizationRevision === activeDocument.visualizationRevision &&
      layer.hoverCapture.mapType === 'SingleBandRasterMapLayer')
    : undefined;
  if (selectedRaster && selectedBackend && activeDocument?.visualizationRevision) {
    symbologySourceRef.current = { projectKey: projectContextKey, documentID: activeDocument.id,
      nodeID: selectedBackend.nodeID, acceptedVisualizationRevision: activeDocument.visualizationRevision,
      source: selectedRaster.hoverCapture.rasterSource };
  }
  const symbologySource = selectedBackend && activeDocument &&
    symbologySourceRef.current?.projectKey === projectContextKey &&
    symbologySourceRef.current.documentID === activeDocument.id &&
    symbologySourceRef.current.nodeID === selectedBackend.nodeID &&
    symbologySourceRef.current.acceptedVisualizationRevision === activeDocument.visualizationRevision
    ? symbologySourceRef.current.source : undefined;
  const selectedVectorCapture = selectedBackend && activeDocument
    ? persistedVectors.captures.find(capture => capture.nodeID === selectedBackend.nodeID &&
      capture.source.family.family === 'geojson' &&
      capture.source.map.projectPath === context.projectPath &&
      capture.source.map.projectRevision === context.projectRevision &&
      capture.source.map.visualizationRevision === activeDocument.visualizationRevision &&
      capture.source.map.mapID !== null && sameMapID(capture.source.map.mapID, activeDocument.mapID) &&
      capture.source.map.mapIndex === activeDocument.mapIndex &&
      capture.source.map.mapName === activeDocument.name && capture.source.map.mapType === activeDocument.mapType)
    : undefined;
  const selectedVectorFile = selectedVectorCapture?.source.family.files.find(file =>
    file.role === 'source' && file.required && file.exists && file.revision &&
    file.path === selectedVectorCapture.source.family.sourcePath);
  const layerCount = visibleLayers.length + mapImages.length;
  const featureCount = visibleLayers.reduce((sum, layer) => sum + layer.features.length, 0);
  const zoomPercent = Number.isFinite(viewport.scale) ? Math.round(viewport.scale * 100) : 100;
  const sourceCRS = mapCRS(language, visibleLayers[visibleLayers.length - 1]?.crs ?? text(language, '无坐标系', 'No CRS'));
  const copy = {
    zh: {
      title: '地图工作区',
      subtitle: '地图视图、工具、图层和时间控制',
      import: '导入 GeoJSON',
      importHint: '本地浏览器预览',
      inspector: '属性',
      inspectorEmpty: '选择地图要素以查看属性。',
      backendSelected: '工程图层',
      backendNoRender: '工程图层按已保存来源读取；勾选后显示支持的矢量、栅格和结果数据，读取问题会单独提示。',
      zoom: '缩放',
      coordinates: '坐标',
      source: '源坐标',
      features: '要素',
      layers: '可见图层',
      noProject: '没有已打开的工程。仍可导入 GeoJSON 并在本地预览。',
      dirty: '当前地图包含未保存的本地预览或绘制草稿。',
      noDirty: '地图工作区就绪',
      property: '属性',
      noProperties: '此要素没有属性。',
      draw: '草稿操作',
      finish: '完成',
      cancel: '取消草稿',
      saveUnavailable: '本地预览和绘制尚未保存；源文件可使用“导入工程图层”另行保存到工程。',
    },
    en: {
      title: 'Map workspace',
      subtitle: 'Map view, tools, layers, and time controls',
      import: 'Import GeoJSON',
      importHint: 'Local browser preview',
      inspector: 'Properties',
      inspectorEmpty: 'Select a map feature to inspect its properties.',
      backendSelected: 'Project layer',
      backendNoRender: 'Checked project sources display supported vector, raster and result data; source reading issues are reported separately.',
      zoom: 'Zoom',
      coordinates: 'Coordinates',
      source: 'Source coordinates',
      features: 'Features',
      layers: 'Visible layers',
      noProject: 'No project is open. You can still import GeoJSON for a local preview.',
      dirty: 'This map contains an unsaved local preview or drawing draft.',
      noDirty: 'Map workspace ready',
      property: 'Properties',
      noProperties: 'This feature has no properties.',
      draw: 'Draft actions',
      finish: 'Finish',
      cancel: 'Cancel draft',
      saveUnavailable: 'Local previews and drawings are unsaved. Source files can be saved separately using Import project layers.',
    },
  }[language];
  const isLocalDirty = activeLayers.length > 0 || draftPoints.length > 0 || backgroundDraftDirty;

  return (
    <div className="map-feature-page" onKeyDown={handleCanvasKey}>
      <header className="map-feature-header">
        <div>
          <div className="map-feature-eyebrow"><span />HEC-RAS 2025 / {text(language, '工程地图', 'Project map')}</div>
          <h1>{copy.title}</h1>
          <p>{copy.subtitle}</p>
        </div>
        <div className="map-file-action">
          <input ref={fileInputRef} className="sr-only" type="file" accept=".geojson,.json,application/geo+json,application/json" onChange={(event) => void handleImport(event)} />
          <button type="button" className="map-import-button" onClick={() => fileInputRef.current?.click()}>
            <span aria-hidden="true">↥</span>{copy.import}
          </button>
          <small>{copy.importHint}</small>
          <MapSourceImportControl language={language} busy={sourceImportBusy}
            enabled={Boolean(context.projectPath && context.projectRevision && backend.uploadMapSources && !mapCatalogLoading && !mapCatalogError && (activeDocument || documents.length === 0))}
            onImport={handleSourceImport} />
          <button type="button" className="map-import-button" data-testid="map-refresh-sources" disabled={!context.projectPath || mapCatalogLoading || sourceImportBusy}
            onClick={() => setMapReloadKey((value) => value + 1)}>{text(language, '刷新图层', 'Refresh layers')}</button>
        </div>
      </header>

      {!hasProject(context) && <div className="map-page-notice" role="status"><span aria-hidden="true">ⓘ</span>{copy.noProject}</div>}
      {isLocalDirty && <div className="map-page-notice map-dirty-notice" role="status"><span aria-hidden="true">●</span>{copy.dirty}<small>{copy.saveUnavailable}</small></div>}
      {importError && <div className="map-page-notice map-error-notice" role="alert">{importError}</div>}

      <MapDocumentTabs
        context={context}
        backend={backend}
        documents={documents}
        activeID={activeDocumentID}
        visualizationRevision={visualizationRevision}
        loading={mapCatalogLoading || sourceImportBusy}
        loadError={mapCatalogError}
        onSelect={(id) => {
          if (sourceImportBusy) return;
          requestGenerationRef.current += 1;
          setActiveDocumentID(id);
          setSelectedFeature(null);
          setSelectedBackendNode(null);
          setContextMenu(null);
        }}
        onCreated={handleCreateDocument}
        language={language}
      />

      <div className="map-workspace-toolbar">
        <MapToolsToolbar
          activeTool={activeTool}
          onToolChange={handleToolChange}
          onZoomIn={() => setViewport((current) => zoomAt(current, [size.width / 2, size.height / 2], 1.5))}
          onZoomOut={() => setViewport((current) => zoomAt(current, [size.width / 2, size.height / 2], 1 / 1.5))}
          onFit={fitVisibleLayers}
          canFit={visibleLayers.length > 0 || mapImages.length > 0}
          language={language}
        />
        <RasMapUndoRedo
          canUndo={ownerState.undo.length > 0}
          canRedo={ownerState.redo.length > 0}
          language={language}
          onUndo={undoMapDraft}
          onRedo={redoMapDraft}
        />
        <div className="map-tool-help">{mapToolHelp(activeTool, language)}</div>
      </div>

      {draftPoints.length > 0 && (
        <div className="map-draft-actions">
          <strong>{copy.draw}</strong>
          <span>{draftPoints.length} {text(language, '个顶点', 'vertices')}</span>
          {(activeTool === 'drawLine' || activeTool === 'drawPolygon' || activeTool === 'measureLength' || activeTool === 'measureArea') && <button type="button" onClick={finishSketch}>{copy.finish}</button>}
          <button type="button" onClick={() => updateDraftPoints(() => [])}>{copy.cancel}</button>
        </div>
      )}
      {measurementResult && <div className="map-measurement-result" role="status">{measurementResult}</div>}
      {statusMessage && <div className="map-status-message" role="status">{statusMessage}</div>}
      {(persistedVectors.error != null || persistedGdb.error != null || persistedVectors.issues.length > 0 || persistedGdb.issues.length > 0 || persistedRasters.errors.length > 0 || associatedLayers.errors.length > 0 || resultImages.error != null || velocityField.error != null) && <div className="map-inline-error" role="alert">
        {persistedVectors.error != null && <div>{persistedVectors.error.message}</div>}
        {persistedGdb.error != null && <div>{persistedGdb.error.message}</div>}
        {[...persistedVectors.issues, ...persistedGdb.issues, ...persistedRasters.errors, ...associatedLayers.errors].map((issue, index) => <div key={issue.name + ':' + index}>{issue.name}: {issue.message}</div>)}
        {resultImages.error != null && <div>{mapError(language, resultImages.error)}</div>}
        {velocityField.error != null && <div>{velocityField.error}</div>}
      </div>}

      <div className="map-workspace-layout">
        <MapLayerTree
          document={mapTreeDocument}
          localLayers={activeLayers}
          selectedLocalLayerID={selectedLocalLayerID}
          selectedBackendNodeID={selectedBackend?.nodeID ?? null}
          busyNodeID={busyNodeID}
          onSelectLocalLayer={(id) => {
            setSelectedFeature(null);
            setSelectedBackendNode(null);
            const layer = activeLayers.find((item) => item.id === id);
            if (layer) setViewport(fitExtent(layer.extent, size, 72));
          }}
          onSelectBackendNode={(node) => {
            setSelectedFeature(null);
            setSelectedBackendNode({ owner, node: null, nodeID: node.id, name: node.name, sourceClass: node.sourceClass, diagnostic: node.diagnosticReadError ?? undefined });
          }}
          onZoomBackendNode={(node) => zoomToLoadedLayer(node.id)}
          onToggleLocalLayer={(id, visible) => updateActiveLayers((current) => current.map((layer) => layer.id === id ? { ...layer, visible } : layer))}
          onChangeOpacity={(id, opacity) => updateActiveLayers((current) => current.map((layer) => layer.id === id ? { ...layer, opacity } : layer))}
          onMoveLocalLayer={moveLocalLayer}
          onRemoveLocalLayer={removeLocalLayer}
          onToggleBackendNode={(node, checked) => void toggleBackendNode(node, checked)}
          language={language}
        />

        <section className="map-center-pane" aria-label={text(language, '地图画布', 'Map canvas')}>
          <div className="map-canvas-head">
            <div>
              <span className="map-canvas-title-dot" />
              <strong>{activeDocument?.name ?? text(language, '本地预览', 'Local preview')}</strong>
              <span className="map-canvas-separator">/</span>
              <span>{activeDocument ? mapDocumentKind(language, activeDocument.mapType) : 'GeoJSON'}</span>
            </div>
            <span className="map-canvas-live-indicator"><i />{text(language, '就绪', 'Ready')}</span>
          </div>
          <div className="map-viewport-shell" ref={viewportRef}>
            <RasProjectMap
              layers={allMapLayers}
              images={mapImages}
              velocityField={velocityField}
              viewport={viewport}
              size={size}
              activeTool={activeTool}
              selectedFeature={selectedFeatureDetails ? { layerID: selectedFeatureDetails.layer.id, featureID: selectedFeatureDetails.feature.id } : null}
              draftPoints={draftPoints}
              settings={settings}
              onPointerDown={handlePointerDown}
              onPointerMove={handlePointerMove}
              onPointerLeave={handlePointerLeave}
              onPointerUp={handlePointerUp}
              onClick={handleCanvasClick}
              onDoubleClick={(event) => {
                if (activeTool === 'drawLine' || activeTool === 'drawPolygon' || activeTool === 'measureLength' || activeTool === 'measureArea') {
                  event.preventDefault();
                  finishSketch();
                }
              }}
              onWheel={handleWheel}
              onFeatureSelect={(layer, feature) => {
                setSelectedFeature({ owner, layerID: layer.id, featureID: feature.id });
                setSelectedBackendNode(null);
                setContextMenu(null);
              }}
              onFeatureContext={showFeatureContext}
              language={language}
              hoverTooltip={mapHover.tooltip}
            />
            {contextMenu && (
              <div className="map-feature-context-menu" role="menu" style={{ left: contextMenu.x, top: contextMenu.y }}>
                <strong>{contextMenu.feature.name}</strong>
                <button type="button" role="menuitem" onClick={zoomToContextFeature}>{text(language, '缩放到要素', 'Zoom to feature')}</button>
                <button type="button" role="menuitem" onClick={() => void copyContextFeature()}>{contextMenu.layer.coordinateSpace === 'project'
                  ? text(language, '复制工程坐标要素 JSON', 'Copy project-coordinate feature JSON')
                  : text(language, '复制 GeoJSON', 'Copy GeoJSON')}</button>
                <button type="button" role="menuitem" disabled={!activeLayers.some((layer) => layer.id === contextMenu.layer.id)} title={contextMenu.layer.coordinateSpace === 'project' ? text(language, '请在工程图层树中切换显隐。', 'Change visibility in the project layer tree.') : undefined} onClick={() => { updateActiveLayers((current) => current.map((layer) => layer.id === contextMenu.layer.id ? { ...layer, visible: false } : layer)); setContextMenu(null); }}>{text(language, '隐藏图层', 'Hide layer')}</button>
                <button type="button" role="menuitem" onClick={() => setContextMenu(null)}>{text(language, '关闭', 'Close')}</button>
              </div>
            )}
          </div>
          <div className="map-canvas-status">
            <RasMapPositionIndicator point={cursorCoordinate} crs={sourceCRS} language={language} />
            <span>{copy.source}: {sourceCRS}</span>
            <span>{copy.zoom}: {zoomPercent}%</span>
            <span>{copy.layers}: {layerCount}</span>
            <span>{copy.features}: {featureCount}</span>
          </div>
          <MapTimeAxis language={language} controller={resultFrames} />
        </section>

        <aside className="map-inspector-pane" aria-label={copy.inspector}>
          <section className="map-inspector-section">
            <div className="map-pane-heading">
              <div><span className="map-pane-kicker">02 / {copy.inspector}</span><h2>{copy.inspector}</h2></div>
              {selectedFeatureDetails && <span className="map-selection-pill">{text(language, '已选择', 'Selected')}</span>}
            </div>
            {selectedFeatureDetails ? (
              <>
                <div className="map-property-feature">
                  <span className="map-property-type">{mapGeometryType(language, selectedFeatureDetails.feature.geometry.type)}</span>
                  <strong>{selectedFeatureDetails.feature.name}</strong>
                  <span>{selectedFeatureDetails.layer.name}</span>
                </div>
                <div className="map-feature-properties">
                  <div className="map-property-row"><span>{text(language, '坐标系', 'CRS')}</span><code>{mapCRS(language, selectedFeatureDetails.layer.crs)}</code></div>
                  {Object.entries(selectedFeatureDetails.feature.properties).map(([key, value]) => (
                    <div className="map-property-row" key={key}><span>{key}</span><code>{typeof value === 'string' ? value : JSON.stringify(value)}</code></div>
                  ))}
                  {Object.keys(selectedFeatureDetails.feature.properties).length === 0 && <p className="map-inspector-empty">{copy.noProperties}</p>}
                  <button type="button" className="map-zoom-feature" onClick={() => fitFeature(selectedFeatureDetails.layer, selectedFeatureDetails.feature)}>{text(language, '缩放到要素', 'Zoom to feature')} ↗</button>
                </div>
              </>
            ) : selectedBackend ? (
              <div className="map-backend-inspector">
                <span className="map-property-type">{localizeProjectTreeSourceClass(language, selectedBackend.sourceClass)}</span>
                <strong>{selectedBackend.name}</strong>
                <p>{copy.backendNoRender}</p>
                {loadedLayerExtent(selectedBackend.nodeID) && <button type="button" className="map-zoom-feature"
                  onClick={() => zoomToLoadedLayer(selectedBackend.nodeID)}>{text(language, '缩放到图层', 'Zoom to layer')} ↗</button>}
                {selectedBackend.diagnostic && <details><summary>{text(language, '查看诊断详情', 'View diagnostic details')}</summary><pre>{mapError(language, selectedBackend.diagnostic)}</pre></details>}
              </div>
            ) : (
              <div className="map-inspector-empty-state">
                <span className="map-inspector-crosshair" aria-hidden="true">＋</span>
                <p>{copy.inspectorEmpty}</p>
              </div>
            )}
          </section>
          {symbologySource && selectedBackend && activeDocument && <RasterSymbologyEditor
            key={`${owner}:${selectedBackend.nodeID}`}
            backend={backend} context={context} language={language}
            source={symbologySource} viewport={resultViewport}
            onDirtyStateChange={onDirtyStateChange}
            onVisualizationCommitted={(previousRevision, nextRevision) => {
              if (projectContextKeyRef.current !== projectContextKey) return;
              const current = activeDocumentRef.current;
              if (!current || current.id !== activeDocument.id ||
                  current.visualizationRevision?.toUpperCase() !== previousRevision.toUpperCase()) return;
              // Keep only this editor's confirmed source capture while fresh
              // pixels load. Palette saves must not discard its unapplied
              // symbology draft; old raster images are never reused here.
              symbologySourceRef.current = { projectKey: projectContextKey, documentID: activeDocument.id,
                nodeID: selectedBackend.nodeID, acceptedVisualizationRevision: nextRevision,
                source: symbologySource };
              setVisualizationRevision(nextRevision);
              setDocuments(previous => previous.map(document => ({ ...document,
                visualizationRevision: nextRevision, visibilityTransition: null })));
              setStatusMessage(text(language, '栅格符号已保存。', 'Raster symbology was saved.'));
            }}
          />}
          <MapSettingsPanel settings={settings} onChange={setSettings} language={language} />
          {selectedVectorFile?.revision && selectedBackend && activeDocument && <VectorSymbologyEditor
            backend={backend} context={context} language={language}
            vectorPath={selectedVectorFile.path} revision={selectedVectorFile.revision}
            ownerID={JSON.stringify([projectContextKey, activeDocument.id,
              activeDocument.visualizationRevision, selectedBackend.nodeID])}
            onDirtyStateChange={onDirtyStateChange}
          />}
          {settings.showVelocityArrows && <p className="map-velocity-status" role="status" data-testid="map-velocity-status">
            {velocityField.loading ? text(language, '正在读取当前时刻的速度场…', 'Reading velocity at the current output time…')
              : velocityField.capture ? text(language,
                `当前时刻：${velocityField.vectors.filter((vector) => vector.vx !== 0 || vector.vy !== 0).length} 个非零速度采样点`,
                `Current output: ${velocityField.vectors.filter((vector) => vector.vx !== 0 || vector.vy !== 0).length} nonzero velocity samples`)
                : text(language, '选择已完成计算的结果后显示速度箭头。', 'Select a completed Result to display velocity arrows.')}
          </p>}
          <RasBackgroundMaps
            state={backgroundSettings}
            onDraftChange={updateBackgroundDraft}
            crs={sourceCRS}
            backendRenderMethodAvailable={backend.supports('map.background.render')}
            overlayRendererConnected={false}
            language={language}
          />
        </aside>
      </div>
      <footer className="map-workspace-footer">
        <span><i className={isLocalDirty ? 'map-footer-dot is-dirty' : 'map-footer-dot'} />{isLocalDirty ? text(language, '本地草稿', 'Local draft') : copy.noDirty}</span>
        <span>{activeDocument ? mapDocumentKind(language, activeDocument.mapType) + ' · ' + activeDocument.name : text(language, '未连接工程地图文档', 'No project map document connected')}</span>
        <span>{cursorCoordinate ? copy.coordinates + ': ' + numberLabel(cursorCoordinate[0]) + ', ' + numberLabel(cursorCoordinate[1]) : sourceCRS}</span>
      </footer>
    </div>
  );
}

export default function MapFeaturePage(props: MapFeaturePageProps) {
  // A project session owns the complete map catalog, camera and loaded-layer
  // state. Remount before painting another session so old tree actions cannot
  // observe old vector extents during the passive-effect cleanup interval.
  return <MapPage key={JSON.stringify([props.context.projectSessionID, props.context.projectPath])} {...props} />;
}
