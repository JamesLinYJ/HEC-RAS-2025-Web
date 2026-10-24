import { lazy, Suspense, useEffect, useMemo, useRef, useState } from 'react';
import type { ChangeEvent as ReactChangeEvent, KeyboardEvent as ReactKeyboardEvent, MouseEvent as ReactMouseEvent, PointerEvent as ReactPointerEvent, WheelEvent as ReactWheelEvent } from 'react';
import { text } from '../../shared/workbench';
import { DEFAULT_BACKGROUND_MAPS_VM_STATE, isBackgroundMapDraftDirty } from './BackgroundMapsVM';
import type { BackgroundMapsVMState } from './BackgroundMapsVM';
import { MapDocumentTabs } from './MapDocumentTabs';
import { MapLayerTree } from './MapLayerTree';
import { MapSettingsPanel } from './MapSettingsPanel';
import { RasterSymbologyEditor } from './symbology/RasterSymbologyEditor';
import type { AppliedRasterLegend } from './symbology/RasterSymbologyEditor';
import { RasMapElementsOverlay } from './RasMapElementsOverlay';
import { ScaleBar, projectScaleUnitSystem } from './ScaleBar';
import { MapViewsPanel } from './views/MapViewsPanel';
import { VectorSymbologyEditor } from './vector-symbology/VectorSymbologyEditor';
import { GlobalSymbologyEditorPage } from './global-symbology/GlobalSymbologyEditorPage';
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
import { useMapVectorRendering } from './useMapVectorRendering';
import type { MapVectorPreview } from './useMapVectorRendering';
import { VectorLayerStyleEditor } from './vector-render/VectorLayerStyleEditor';
import type { VectorLayerSource } from './vector-render/vectorRenderTypes';
import { usePersistedGdbLayers } from './usePersistedGdbLayers';
import { useMapVelocityField } from './useMapVelocityField';
import { usePersistedRasterLayers, mapRasterViewportFromCamera } from './usePersistedRasterLayers';
import { useMapResultFrames } from './useMapResultFrames';
import { useAssociatedMapLayers } from './useAssociatedMapLayers';
import { mapCatalogMatchesDocument, sameMapID, sameMapLocator, stableMapValue } from './associatedMapLayers';
import { useMapHoverIdentify } from './useMapHoverIdentify';
import type { MapHoverPointer } from './useMapHoverIdentify';
import { mapResultSelectionOverridesFromCatalog } from './useMapResultFrames';
import type {
  MapAnalysisActiveNode,
  MapAnalysisPanelProps,
  MapAnalysisResultChoice,
  MapAnalysisResultSelection,
  MapMeshElementHit,
  MapResultAnalysisCapture,
  MapSampleLinePart,
  MapSampleLineSource,
} from './analysis/mapAnalysisTypes';
import { useMapResultMeshHoverCapture } from './analysis/useMapResultMeshHoverCapture';
import { captureVisibleTerrainForMeasurement } from './measure/terrainElevationCapture';
import { TerrainMeasurementReadout } from './measure/TerrainMeasurementReadout';
import { useTerrainElevationMeasurement } from './measure/useTerrainElevationMeasurement';
import { rgbaFrameImage } from './mapImages';
import { importMapSources } from './importMapSources';
import { MapSourceImportControl } from './MapSourceImportControl';
import { RasBackgroundMaps } from './RasBackgroundMaps';
import { RasMapPositionIndicator } from './RasMapPositionIndicator';
import { RasMapUndoRedo } from './RasMapUndoRedo';
import { ZoomToPointDialog } from './ZoomToPointDialog';
import { navigationViewport } from './mapNavigation';
import type { CatalogTreeNode, GeoFeature, GeoGeometry, GeoJsonLayer, MapDocumentTab, MapImageLayer, XY } from './mapTypes';
import type { MapFeaturePageProps } from './mapTypes';
import { RasLayerPropertiesPane } from './RasLayerPropertiesPane';
import { RasContextualActionsBar } from './RasContextualActionsBar';
import { mapCRS, mapDocumentKind, mapError } from './mapText';
import './map.css';

const MapMeshTimeSeriesPanel = lazy(() => import('./analysis/MapMeshTimeSeriesPanel')
  .then(module => ({ default: module.MapMeshTimeSeriesPanel })));
const MapCulvertAnalysisPanel = lazy(() => import('./analysis/MapCulvertAnalysisPanel')
  .then(module => ({ default: module.MapCulvertAnalysisPanel })));
const MapSampleLineAnalysisPanel = lazy(() => import('./analysis/MapSampleLineAnalysisPanel')
  .then(module => ({ default: module.MapSampleLineAnalysisPanel })));

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

function catalogHasNodeType(nodes: readonly CatalogTreeNode[], nodeID: string, nodeType: string): boolean {
  for (const node of nodes) {
    if (node.id === nodeID) return node.locator?.nodeType === nodeType;
    if (node.children?.length && catalogHasNodeType(node.children, nodeID, nodeType)) return true;
  }
  return false;
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

function findVisibleMapNode(
  nodes: import('./mapTypes').CatalogTreeNode[],
  locator: import('./mapTypes').CatalogTreeNodeLocator,
  ancestorsVisible = true,
): import('./mapTypes').CatalogTreeNode | null {
  for (const node of nodes) {
    const visible = ancestorsVisible && node.checked !== 'unchecked';
    if (!visible) continue;
    if (node.locator && sameMapLocator(node.locator, locator)) return node;
    const child = node.children?.length ? findVisibleMapNode(node.children, locator, visible) : null;
    if (child) return child;
  }
  return null;
}

function resultChoiceForAnalysis(choice: NonNullable<import('./useMapResultFrames').MapResultSelectionState['selectedResult']>): MapAnalysisResultChoice {
  return {
    resultPath: choice.resultPath,
    revision: choice.revision,
    resultName: choice.resultName,
    planName: choice.planName,
    endTime: choice.endTime,
    isComputing: choice.isComputing,
  };
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

function MapPage(props: MapFeaturePageProps) {
  const { language, context, backend, backendRevision, onDirtyStateChange } = props;
  const [documents, setDocuments] = useState<MapDocumentTab[]>([]);
  const [activeDocumentID, setActiveDocumentID] = useState<string | null>(null);
  const [mapCatalogLoading, setMapCatalogLoading] = useState(false);
  const [viewCommitBusy, setViewCommitBusy] = useState(false);
  const [vectorCommitBusy, setVectorCommitBusy] = useState(false);
  const mapMetadataCommitBusy = viewCommitBusy || vectorCommitBusy;
  const [vectorPreview, setVectorPreview] = useState<MapVectorPreview | null>(null);
  const [vectorSettingsEpoch, setVectorSettingsEpoch] = useState(0);
  const [mapCatalogError, setMapCatalogError] = useState<unknown>(null);
  const [visualizationRevision, setVisualizationRevision] = useState<string | null>(null);
  const [mapStateByOwner, setMapStateByOwner] = useState<Record<string, MapOwnerState>>({});
  const [backgroundSettingsByOwner, setBackgroundSettingsByOwner] = useState<Record<string, BackgroundMapsVMState>>({});
  const [viewport, setViewport] = useState<MapViewport>(INITIAL_VIEWPORT);
  const [locationDialog, setLocationDialog] = useState<{ owner: string; initialPoint: XY } | null>(null);
  const [size, setSize] = useState({ width: 880, height: 620 });
  const [activeTool, setActiveTool] = useState<MapToolID>('select');
  const [settings, setSettings] = useState<MapDisplaySettings>(DEFAULT_MAP_DISPLAY_SETTINGS);
  const [appliedLegend, setAppliedLegend] = useState<{ scope: string; legend: AppliedRasterLegend } | null>(null);
  const legendScopeRef = useRef('');
  const [selectedFeature, setSelectedFeature] = useState<SelectedFeature>(null);
  const [selectedBackendNode, setSelectedBackendNode] = useState<{ owner: string; node: GeoFeature | null; nodeID: string; name: string; sourceClass: string; diagnostic?: string } | null>(null);
  const [selectedMeshHit, setSelectedMeshHit] = useState<MapMeshElementHit | null>(null);
  const [meshAnalysisOpen, setMeshAnalysisOpen] = useState(false);
  const [culvertAnalysisOpen, setCulvertAnalysisOpen] = useState(false);
  const [sampleLineAnalysisOpen, setSampleLineAnalysisOpen] = useState(false);
  const [cursorScreen, setCursorScreen] = useState<XY | null>(null);
  const [hoverPointer, setHoverPointer] = useState<MapHoverPointer | null>(null);
  const [contextMenu, setContextMenu] = useState<FeatureContextMenu | null>(null);
  const [statusMessage, setStatusMessage] = useState('');
  const [measurementResult, setMeasurementResult] = useState<string | null>(null);
  const [measurementResultOwner, setMeasurementResultOwner] = useState<string | null>(null);
  const [measurementResultKind, setMeasurementResultKind] = useState<'length' | 'area' | null>(null);
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
  const loadedCatalogProjectRef = useRef<string | null>(null);
  const rememberedMapSelectionRef = useRef<{ projectKey: string; mapIndex: number; name: string; mapType: string } | null>(null);
  if (activeDocument && loadedCatalogProjectRef.current === projectContextKey) {
    rememberedMapSelectionRef.current = { projectKey: projectContextKey, mapIndex: activeDocument.mapIndex,
      name: activeDocument.name, mapType: activeDocument.mapType };
  }
  const symbologySourceRef = useRef<{
    projectKey: string; documentID: string; nodeID: string;
    acceptedVisualizationRevision: string; source: Record<string, unknown>;
  } | null>(null);
  const owner = activeDocument?.id ?? ('local:' + context.projectSessionID);
  const cameraOwnerScope = JSON.stringify([context.projectSessionID, context.projectPath, owner]);
  const manuallyControlledCameraOwnerRef = useRef<string | null>(null);
  const noteManualCameraControl = () => { manuallyControlledCameraOwnerRef.current = cameraOwnerScope; };
  const setUserViewport = (next: Parameters<typeof setViewport>[0]) => {
    noteManualCameraControl();
    setViewport(next);
  };
  useEffect(() => {
    // A different project or saved map gets its own initial fit opportunity.
    manuallyControlledCameraOwnerRef.current = null;
  }, [cameraOwnerScope]);
  useEffect(() => setLocationDialog(null), [owner]);
  const ownerState = mapStateByOwner[owner] ?? EMPTY_MAP_OWNER_STATE;
  const activeLayers = ownerState.layers;
  const draftPoints = ownerState.points;
  const isMeasurementTool = activeTool === 'measureLength' || activeTool === 'measureArea';
  const hasPersistentDraftPoints = draftPoints.length > 0 && !isMeasurementTool;
  const backgroundSettings = backgroundSettingsByOwner[owner] ?? DEFAULT_BACKGROUND_MAPS_VM_STATE;
  const backgroundDraftDirty = isBackgroundMapDraftDirty(backgroundSettings);
  const persistedVectors = usePersistedVectorLayers({ backend, context, document: activeDocument, language });
  const persistedGdb = usePersistedGdbLayers({ backend, context, document: activeDocument, language });
  const persistedRasters = usePersistedRasterLayers({ backend, context, document: activeDocument, language });
  const resultViewport = useMemo(() => mapRasterViewportFromCamera(viewport, size), [viewport, size]);
  const vectorCandidates = useMemo(() => {
    if (!activeDocument || !context.projectPath || !context.projectRevision || !context.projectXml) return [];
    const matching = (map: typeof persistedVectors.captures[number]['source']['map']) =>
      map.isPersisted && map.projectPath === context.projectPath && map.projectRevision === context.projectRevision &&
      map.mapID !== null && sameMapID(map.mapID, activeDocument.mapID) && map.mapIndex === activeDocument.mapIndex &&
      map.mapName === activeDocument.name && map.mapType === activeDocument.mapType &&
      map.visualizationRevision === activeDocument.visualizationRevision;
    const capture = (layerID: string, nodeID: string, map: typeof persistedVectors.captures[number]['source']['map'],
      node: VectorLayerSource['node'], expectedFamily: VectorLayerSource['expectedFamily'], sourceRevision: string) => {
      const source: VectorLayerSource = {
        key: JSON.stringify([projectContextKey, layerID, map, node, expectedFamily?.revision, sourceRevision]),
        map: { projectPath: context.projectPath!, projectXml: context.projectXml, projectXmlSHA: map.projectXmlSHA,
          mapID: activeDocument.mapID, mapIndex: activeDocument.mapIndex, mapName: activeDocument.name,
          mapType: activeDocument.mapType, expectedVisualizationRevision: map.visualizationRevision,
          projectSessionID: context.projectSessionID },
        node, layerID, expectedProjectRevision: context.projectRevision!, expectedFamily, sourceRevision,
      };
      return { nodeID, source };
    };
    return [
      ...persistedVectors.captures.flatMap(item => {
        if (!matching(item.source.map)) return [];
        const primary = item.source.family.files.find(file => file.role === 'source' && file.required &&
          file.exists && file.path === item.source.family.sourcePath && file.revision);
        return primary?.revision ? [capture(item.layerID, item.nodeID, item.source.map,
          item.source.locator, item.source.family, primary.revision)] : [];
      }),
      ...persistedGdb.captures.flatMap(item => matching(item.map)
        ? [capture(item.layerID, item.nodeID, item.map, item.locator, null, item.source.revision)] : []),
    ];
  }, [activeDocument, projectContextKey, context.projectPath, context.projectRevision, context.projectXml,
    context.projectSessionID, persistedVectors.captures, persistedGdb.captures]);
  const vectorSources = useMemo(() => vectorCandidates.map(candidate => candidate.source), [vectorCandidates]);
  useEffect(() => {
    setVectorPreview(current => current && !vectorSources.some(source => source.key === current.sourceKey) ? null : current);
  }, [vectorSources]);
  const vectorRendering = useMapVectorRendering({ backend, backendRevision, sources: vectorSources,
    viewport: resultViewport, preview: vectorPreview, settingsEpoch: vectorSettingsEpoch, language });
  const resultFrames = useMapResultFrames({ backend, context, document: activeDocument, language, viewport: resultViewport, backendRevision });
  const associatedLayers = useAssociatedMapLayers({ backend, context, document: activeDocument,
    viewport: resultViewport, controller: resultFrames, language, backendRevision });
  const selectedBackendNodeID = selectedBackendNode?.owner === owner ? selectedBackendNode.nodeID : null;
  const selectedTerrainNodeID = useMemo(() => selectedBackendNodeID && resultFrames.sources &&
    catalogHasNodeType(resultFrames.sources.treeRoots, selectedBackendNodeID, 'Terrain') ? selectedBackendNodeID : null,
  [resultFrames.sources, selectedBackendNodeID]);
  const selectedResultOwners = useMemo(() => resultFrames.sources?.activeNodes.map(active => {
    const slotZero = active.selections.find(selection => selection.selectionIndex === 0);
    const result = slotZero?.selectedResult;
    return {
      node: active.node,
      selectionIndex: 0,
      selectionMode: slotZero?.selectionMode ?? null,
      resultPath: result?.resultPath ?? null,
      resultRevision: result?.revision ?? null,
      isComputing: result?.isComputing ?? null,
    };
  }) ?? null, [resultFrames.sources?.activeNodes]);
  const terrainMeasurementScopeKey = useMemo(() => JSON.stringify(stableMapValue({
      owner,
      projectPath: context.projectPath,
      projectRevision: context.projectRevision,
      projectSessionID: context.projectSessionID,
      mapRequest: resultFrames.mapRequest,
      catalog: resultFrames.sources ? {
        contextToken: resultFrames.sources.contextToken,
        projectRevision: resultFrames.sources.projectRevision,
        projectXmlSHA: resultFrames.sources.projectXmlSHA,
        visualizationRevision: resultFrames.sources.visualizationRevision,
        treeSelectionOverrides: resultFrames.sources.treeSelectionOverrides,
        selectedResultOwners,
      } : null,
      selectedTerrainNodeID,
      coreLayerContextIdentity: context.coreLayerContext?.identityToken ?? null,
    })), [context.coreLayerContext?.identityToken, context.projectPath, context.projectRevision,
    context.projectSessionID, owner, resultFrames.mapRequest, resultFrames.sources, selectedResultOwners,
    selectedTerrainNodeID]);
  const terrainMeasurementCaptureState = useMemo(() => captureVisibleTerrainForMeasurement({
    backend,
    context,
    catalog: resultFrames.sources,
    document: activeDocument,
    map: resultFrames.mapRequest,
    layers: associatedLayers.layers,
    selectedNodeID: selectedTerrainNodeID,
  }), [activeDocument, associatedLayers.layers, backend, context, owner, resultFrames.mapRequest, resultFrames.sources,
    selectedTerrainNodeID, backendRevision]);
  const terrainElevationMeasurement = useTerrainElevationMeasurement(backend, terrainMeasurementCaptureState.capture,
    terrainMeasurementScopeKey);
  const clearMeasurement = () => {
    setMeasurementResult(null);
    setMeasurementResultOwner(null);
    setMeasurementResultKind(null);
    terrainElevationMeasurement.clear();
  };
  const mapTreeDocument = useMemo(() => activeDocument && mapCatalogMatchesDocument(resultFrames.sources, context, activeDocument)
    ? { ...activeDocument, roots: resultFrames.sources.treeRoots } : activeDocument,
  [activeDocument, context.projectPath, context.projectRevision, resultFrames.sources]);
  const velocityField = useMapVelocityField({ backend, context, document: activeDocument, controller: resultFrames,
    viewport: resultViewport, enabled: settings.IsMapOverlayVisible && settings.showVelocityArrows, language });
  const allMapLayers = useMemo(() => [...persistedVectors.layers, ...persistedGdb.layers, ...activeLayers], [persistedVectors.layers, persistedGdb.layers, activeLayers]);
  const selectedFeatureDetails = useMemo(() => {
    if (!selectedFeature || selectedFeature.owner !== owner) return null;
    const layer = allMapLayers.find((item) => item.id === selectedFeature.layerID);
    const feature = layer?.features.find((item) => item.id === selectedFeature.featureID);
    return layer && feature ? { layer, feature } : null;
  }, [allMapLayers, owner, selectedFeature]);
  const analysisCapture = useMemo<MapResultAnalysisCapture | null>(() => {
    const catalog = resultFrames.sources;
    const map = resultFrames.mapRequest;
    const activeNode = resultFrames.activeNode;
    if (!catalog || !map || !activeNode || !mapCatalogMatchesDocument(catalog, context, activeDocument) ||
        !catalog.contextToken || !context.projectPath || !context.projectRevision || !context.projectXml ||
        map.projectPath !== context.projectPath || map.projectXml !== context.projectXml ||
        map.projectXmlSHA.toUpperCase() !== catalog.projectXmlSHA.toUpperCase() ||
        map.projectSessionID !== context.projectSessionID ||
        (map.coreLayerContext?.identityToken ?? null) !== (context.coreLayerContext?.identityToken ?? null) ||
        !sameMapID(map.mapID, activeDocument!.mapID) || map.mapIndex !== activeDocument!.mapIndex ||
        map.mapName !== activeDocument!.name || map.mapType !== activeDocument!.mapType ||
        map.expectedVisualizationRevision !== activeDocument!.visualizationRevision ||
        activeNode.nodeKind !== activeNode.node.nodeType ||
        !['Result', 'MultiResult'].includes(activeNode.node.nodeType) ||
        !findVisibleMapNode(catalog.treeRoots, activeNode.node)) return null;

    const catalogNode = catalog.activeNodes.find(node => sameMapLocator(node.node, activeNode.node));
    const slotZero = activeNode.selections.find(selection => selection.selectionIndex === 0);
    const catalogSlotZero = catalogNode?.selections.find(selection => selection.selectionIndex === 0);
    if (!catalogNode || !slotZero?.selectedResult || !catalogSlotZero?.selectedResult ||
        slotZero.selectionMode !== catalogSlotZero.selectionMode ||
        slotZero.selectedResult.resultPath !== catalogSlotZero.selectedResult.resultPath ||
        slotZero.selectedResult.revision.toUpperCase() !== catalogSlotZero.selectedResult.revision.toUpperCase() ||
        slotZero.selectedResult.isComputing || catalogNode.selectedMapType !== activeNode.selectedMapType ||
        activeNode.selections.length !== catalogNode.selections.length ||
        activeNode.selections.some(selection => {
          const source = catalogNode.selections.find(candidate => candidate.selectionIndex === selection.selectionIndex);
          return !source || source.selectionMode !== selection.selectionMode ||
            source.selectedResult?.resultPath !== selection.selectedResult?.resultPath ||
            source.selectedResult?.revision.toUpperCase() !== selection.selectedResult?.revision.toUpperCase();
        })) return null;

    const sourceSelection = (selection: NonNullable<typeof slotZero>): MapAnalysisResultSelection => ({
      selectionIndex: selection.selectionIndex,
      selectionMode: selection.selectionMode,
      selectedResult: selection.selectedResult ? resultChoiceForAnalysis(selection.selectedResult) : null,
    });
    const selections = activeNode.selections.map(sourceSelection);
    const snapshot = resultFrames.renderSnapshot;
    const selectedProfileIndex = resultFrames.selectedProfileIndex;
    const frameMatches = resultFrames.status === 'ready' && snapshot !== null && selectedProfileIndex !== null &&
      snapshot.contextToken === catalog.contextToken && snapshot.selectedProfileIndex === selectedProfileIndex &&
      resultFrames.layers.length === snapshot.layers.length && snapshot.layers.length > 0 &&
      resultFrames.layers.every((frame, index) => {
        const saved = snapshot.layers[index];
        return Boolean(saved && frame.id === saved.id && frame.sourceID === saved.sourceID &&
          frame.sourceMapID === saved.sourceMapID && frame.resultPath === saved.resultPath &&
          frame.revision.toUpperCase() === saved.revision.toUpperCase() && frame.mapType === saved.mapType &&
          frame.profileIndex === saved.profileIndex && JSON.stringify(frame.selectionIndices) === JSON.stringify(saved.selectionIndices));
      });
    const profileIndex = frameMatches ? selectedProfileIndex : null;
    const profileTime = frameMatches ? resultFrames.selectedProfileTime : null;
    const renderCapture = frameMatches && snapshot ? {
      contextToken: snapshot.contextToken,
      selectedProfileIndex: snapshot.selectedProfileIndex,
      selectedProfileTime: snapshot.selectedProfileTime,
      sourceID: snapshot.sourceID,
    } : null;
    const selectionOverrides = mapResultSelectionOverridesFromCatalog(catalog);
    const ownerKey = JSON.stringify([
      context.projectSessionID, context.projectPath, context.projectRevision, map.projectXmlSHA,
      map.mapID, map.mapIndex, map.mapName, map.mapType, map.coreLayerContext?.identityToken ?? null,
      catalog.visualizationRevision, catalog.contextToken,
    ]);
    const captureKey = JSON.stringify([
      ownerKey, activeNode.node, activeNode.selectedMapType,
      selections.map(selection => [selection.selectionIndex, selection.selectionMode,
        selection.selectedResult?.resultPath ?? null, selection.selectedResult?.revision.toUpperCase() ?? null]),
      profileIndex, profileTime, snapshot?.sourceID ?? null,
    ]);
    const mappedActiveNode: MapAnalysisActiveNode = {
      node: activeNode.node,
      nodeKind: activeNode.nodeKind,
      name: activeNode.name,
      selections,
      selectedMapType: activeNode.selectedMapType,
      profileIndex,
    };
    return {
      ownerKey,
      map,
      projectRevision: catalog.projectRevision,
      projectXmlSHA: catalog.projectXmlSHA,
      visualizationRevision: catalog.visualizationRevision,
      contextToken: catalog.contextToken,
      selectionOverrides,
      activeNode: mappedActiveNode,
      treeRoots: catalog.treeRoots,
      slotZero: sourceSelection(slotZero),
      profileIndex,
      profileTime,
      renderSnapshot: renderCapture,
      captureKey,
    };
  }, [activeDocument, context.projectPath, context.projectRevision, context.projectSessionID, context.projectXml,
    resultFrames.activeNode, resultFrames.layers, resultFrames.mapRequest, resultFrames.renderSnapshot,
    resultFrames.selectedProfileIndex, resultFrames.selectedProfileTime, resultFrames.sources, resultFrames.status]);
  const resultMeshHover = useMapResultMeshHoverCapture(backend, analysisCapture);
  const currentMeshHit = selectedMeshHit && analysisCapture &&
    selectedMeshHit.ownerKey === analysisCapture.ownerKey && selectedMeshHit.captureKey === analysisCapture.captureKey
    ? selectedMeshHit : null;
  const sampleLineSource = useMemo<MapSampleLineSource | null>(() => {
    const capture = analysisCapture;
    const selected = selectedFeatureDetails;
    const catalog = resultFrames.sources;
    if (!capture || !selected || !selected.layer.visible || !catalog || persistedVectors.loading ||
        !mapCatalogMatchesDocument(catalog, context, activeDocument)) return null;
    const vector = persistedVectors.captures.find(item => item.layerID === selected.layer.id &&
      item.mapType === 'PolylinesVector' && item.source.map.isPersisted &&
      item.source.map.projectPath === capture.map.projectPath &&
      item.source.map.projectRevision.toUpperCase() === capture.projectRevision.toUpperCase() &&
      item.source.map.projectXmlSHA?.toUpperCase() === capture.projectXmlSHA.toUpperCase() &&
      item.source.map.visualizationRevision?.toUpperCase() === capture.visualizationRevision.toUpperCase() &&
      item.source.map.mapID !== null && sameMapID(item.source.map.mapID, capture.map.mapID) &&
      item.source.map.mapIndex === capture.map.mapIndex && item.source.map.mapName === capture.map.mapName &&
      item.source.map.mapType === capture.map.mapType);
    const persistedLayer = vector ? persistedVectors.layers.find(layer => layer.id === vector.layerID) : null;
    const persistedFeature = persistedLayer?.features.find(feature => feature.id === selected.feature.id);
    const vectorNode = vector ? findVisibleMapNode(catalog.treeRoots, vector.source.locator) : null;
    if (!vector || !persistedLayer || selected.layer !== persistedLayer || !persistedFeature ||
        !vectorNode || vectorNode.id !== vector.nodeID || vectorNode.checked !== 'checked') return null;
    const primary = vector.source.family.files.find(file => file.role === 'source' && file.required && file.exists &&
      file.path === vector.source.family.sourcePath && typeof file.revision === 'string' && /^[0-9a-f]{64}$/i.test(file.revision));
    if (!primary?.revision || selected.feature.id.length > 1024 || /[\u0000-\u001f\u007f]/.test(selected.feature.id)) return null;
    const coordinates: readonly (readonly XY[])[] = persistedFeature.geometry.type === 'LineString'
      ? [persistedFeature.geometry.coordinates]
      : persistedFeature.geometry.type === 'MultiLineString' ? persistedFeature.geometry.coordinates : [];
    const parts: MapSampleLinePart[] = coordinates.flatMap((line, partIndex) =>
      line.length >= 2 && line.every(point => Number.isFinite(point[0]) && Number.isFinite(point[1]))
        ? [{ partIndex, coordinates: line.map(point => [point[0], point[1]] as XY) }]
        : []);
    if (parts.length === 0) return null;
    return {
      ownerKey: capture.ownerKey,
      layerID: vector.layerID,
      nodeID: vector.nodeID,
      featureID: selected.feature.id,
      featureName: persistedFeature.name,
      mapType: 'PolylinesVector',
      vectorPath: vector.source.family.sourcePath,
      vectorRevision: primary.revision,
      parts,
    };
  }, [activeDocument, analysisCapture, context, persistedVectors.captures, persistedVectors.loading,
    resultFrames.sources, selectedFeatureDetails]);
  const analysisPanelProps: MapAnalysisPanelProps = {
    backend,
    language,
    capture: analysisCapture,
    meshHit: currentMeshHit,
    sampleLineSource,
  };
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
    resultMeshCapture: resultMeshHover.capture,
    resultAnalysisCapture: analysisCapture,
    associatedCaptures: associatedLayers.layers.filter(layer => currentHoverPointer &&
      currentHoverPointer.project[0] >= layer.extent.minX && currentHoverPointer.project[0] <= layer.extent.maxX &&
      currentHoverPointer.project[1] >= layer.extent.minY && currentHoverPointer.project[1] <= layer.extent.maxY)
      .map(layer => layer.hoverCapture),
    selectedBackendNodeID: selectedBackendNode?.owner === owner ? selectedBackendNode.nodeID : null,
  });
  const selectedLocalLayerID = selectedFeature?.owner === owner ? selectedFeature.layerID : null;
  useEffect(() => {
    if (!activeDocument || persistedVectors.loading || persistedGdb.loading || persistedRasters.status === 'loading' || resultFrames.status === 'loading' || associatedLayers.status === 'loading') return;
    // Late Result/source readiness must not replace a camera the user already
    // positioned for this exact project and saved map.
    if (manuallyControlledCameraOwnerRef.current === cameraOwnerScope) return;
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
    associatedLayers.status, associatedLayers.sourceExtents, size, cameraOwnerScope]);

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
    onDirtyStateChange(dirtyOwner, activeLayers.length > 0 || hasPersistentDraftPoints || backgroundDraftDirty);
    return () => onDirtyStateChange(dirtyOwner, false);
  }, [activeLayers.length, backgroundDraftDirty, hasPersistentDraftPoints, onDirtyStateChange, owner]);
  useEffect(() => {
    const writeOwner = 'map.write:' + projectContextKey;
    onDirtyStateChange?.(writeOwner, sourceImportBusy || busyNodeID !== null || mapMetadataCommitBusy);
    return () => onDirtyStateChange?.(writeOwner, false);
  }, [sourceImportBusy, busyNodeID, mapMetadataCommitBusy, projectContextKey, onDirtyStateChange]);

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
    const rememberedSelection = rememberedMapSelectionRef.current?.projectKey === requestContextKey
      ? rememberedMapSelectionRef.current : null;
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
      loadedCatalogProjectRef.current = requestContextKey;
      setDocuments(catalog.documents);
      setVisualizationRevision(catalog.visualizationRevision);
      const active = (rememberedSelection ? catalog.documents.find(document =>
        document.mapIndex === rememberedSelection.mapIndex && document.name === rememberedSelection.name &&
        document.mapType === rememberedSelection.mapType) : null) ??
        catalog.documents.find((document) => document.mapIndex === catalog.activeMapIndex) ?? catalog.documents[0] ?? null;
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
  }, [backend, backendRevision, projectContextKey, selectedResultIdentity, mapReloadKey, props.mapRevision]);

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
    if (extent) setUserViewport(fitExtent(extent, size, 72));
  };

  const visibleExtent = (() => {
    const top = visibleLayers[visibleLayers.length - 1];
    const key = (layer: GeoJsonLayer) => layer.coordinateSpace === 'project' ? 'project' : 'source:' + layer.crs;
    const fitLayers = top ? visibleLayers.filter((layer) => key(layer) === key(top)) : visibleLayers;
    // Backend image bounds describe a rendered camera frame, not the source dataset.
    // Fitting those bounds would repeatedly fit the current view to itself.
    const sourceImageExtents = [...persistedRasters.layers.map((layer) => layer.extent),
      ...associatedLayers.sourceExtents,
      ...(resultImages.layers.length && resultFrames.resultExtent ? [resultFrames.resultExtent] : [])];
    return unionExtents([...fitLayers.map((layer) => layer.extent),
      ...(!top || top.coordinateSpace === 'project' ? sourceImageExtents : [])]);
  })();
  const fitVisibleLayers = () => {
    if (visibleExtent) setUserViewport(fitExtent(visibleExtent, size, 72));
  };

  const handleSourceImport = async (files: File[], options?: { directory?: boolean }) => {
    if (mapMetadataCommitBusy || sourceImportBusy || mapCatalogLoading || mapCatalogError || !context.projectPath || (!activeDocument && documents.length > 0)) return;
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
      setUserViewport(fittedViewport);
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
    clearMeasurement();
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
      setMeasurementResultOwner(owner);
      setMeasurementResultKind('length');
      const latest = draftPoints[draftPoints.length - 1];
      const previous = draftPoints[draftPoints.length - 2];
      terrainElevationMeasurement.sample(latest, previous,
        Math.hypot(latest[0] - previous[0], latest[1] - previous[1]), terrainMeasurementCaptureState.reason);
      updateDraftPoints(() => []);
      setStatusMessage(text(language, '平面距离测量完成；单位沿用源坐标。', 'Planar distance measurement completed in source coordinate units.'));
    } else if (activeTool === 'measureArea' && draftPoints.length >= 3) {
      const value = measurement(draftPoints, true);
      setMeasurementResult(text(language, '面积（源坐标单位²）: ', 'Area (source units²): ') + numberLabel(value));
      setMeasurementResultOwner(owner);
      setMeasurementResultKind('area');
      terrainElevationMeasurement.clear();
      updateDraftPoints(() => []);
      setStatusMessage(text(language, '平面面积测量完成；单位沿用源坐标。', 'Planar area measurement completed in source coordinate units.'));
    } else {
      setStatusMessage(text(language, '需要更多顶点才能完成当前操作。', 'Add more vertices to finish this operation.'));
    }
  };

  const handleCanvasClick = (event: ReactPointerEvent<SVGSVGElement>) => {
    const clickedScreen = pointFromEvent(event);
    const hoveredMeshHit = mapHover.tooltip?.entries.find(entry => entry.kind === 'mesh')?.meshHit ?? null;
    const sameHoverPoint = mapHover.tooltip && Math.hypot(
      mapHover.tooltip.screen[0] - clickedScreen[0], mapHover.tooltip.screen[1] - clickedScreen[1]) <= 6;
    if (activeTool === 'select' && sameHoverPoint && hoveredMeshHit && analysisCapture &&
        hoveredMeshHit.ownerKey === analysisCapture.ownerKey && hoveredMeshHit.captureKey === analysisCapture.captureKey) {
      setSelectedMeshHit(hoveredMeshHit);
      setMeshAnalysisOpen(true);
      setStatusMessage(text(language,
        `已选中 ${hoveredMeshHit.elementKind === 'Cell' ? '单元' : '面'} ${hoveredMeshHit.elementIndex + 1}；来源为当前 Result 中的 Flow Area Mesh。`,
        `Selected ${hoveredMeshHit.elementKind} ${hoveredMeshHit.elementIndex + 1} from the current Result Flow Area Mesh.`));
    } else if (activeTool === 'select') {
      setSelectedMeshHit(null);
    }
    setHoverPointer(null);
    setContextMenu(null);
    if (activeTool === 'select') {
      setSelectedFeature(null);
      setSelectedBackendNode(null);
      return;
    }
    if (activeTool === 'pan') return;
    const coordinate = toWorld(event);
    noteManualCameraControl();
    if (activeTool === 'drawPoint') {
      appendDrawnGeometry({ type: 'Point', coordinates: coordinate });
      return;
    }
    updateDraftPoints((current) => [...current, coordinate]);
    clearMeasurement();
  };

  const handlePointerDown = (event: ReactPointerEvent<SVGSVGElement>) => {
    setHoverPointer(null);
    if (event.button === 1 || activeTool === 'pan' || (event.button === 0 && event.altKey)) {
      noteManualCameraControl();
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
      if (dx !== 0 || dy !== 0) setUserViewport((current) => panViewport(current, [dx, dy]));
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
    setUserViewport((current) => zoomAt(current, pointFromEvent(event), factor));
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
      clearMeasurement();
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
    clearMeasurement();
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
    if (extent) setUserViewport(fitExtent(extent, size, 110));
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
    if (extent) setUserViewport(fitExtent(extent, size, 100));
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
  const legendScope = JSON.stringify([projectContextKey, activeDocument?.id, selectedBackend?.nodeID]);
  legendScopeRef.current = legendScope;
  // A legend follows confirmed pixels, never a draft palette or an old map revision.
  const currentLegend = appliedLegend?.scope === legendScope && selectedRaster && activeDocument &&
    appliedLegend.legend.source.id === selectedRaster.hoverCapture.rasterSource.id &&
    appliedLegend.legend.source.projectPath === context.projectPath &&
    appliedLegend.legend.source.mapIndex === activeDocument.mapIndex &&
    appliedLegend.legend.source.mapName === activeDocument.name &&
    appliedLegend.legend.source.mapType === activeDocument.mapType &&
    appliedLegend.legend.source.visualizationRevision.toUpperCase() === activeDocument.visualizationRevision?.toUpperCase() &&
    appliedLegend.legend.source.sourceRevision.toUpperCase() === selectedRaster.sourceRevision.toUpperCase() &&
    mapImages.some(image => image.treeNodeID === selectedBackend?.nodeID &&
      image.sourcePath === appliedLegend.legend.source.sourcePath &&
      image.sourceRevision?.toUpperCase() === appliedLegend.legend.source.sourceRevision.toUpperCase())
    ? appliedLegend.legend : null;
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
  const selectedRenderSource = selectedBackend
    ? vectorCandidates.find(candidate => candidate.nodeID === selectedBackend.nodeID)?.source ?? null : null;
  const selectedVectorFile = selectedVectorCapture?.source.family.files.find(file =>
    file.role === 'source' && file.required && file.exists && file.revision &&
    file.path === selectedVectorCapture.source.family.sourcePath);
  const layerCount = visibleLayers.length + mapImages.length;
  const featureCount = visibleLayers.reduce((sum, layer) => sum + layer.features.length, 0);
  const zoomPercent = Number.isFinite(viewport.scale) ? Math.round(viewport.scale * 100) : 100;
  const sourceCRS = mapCRS(language, visibleLayers[visibleLayers.length - 1]?.crs ?? text(language, '无坐标系', 'No CRS'));
  const projectCoordinates = Boolean(activeDocument) && visibleLayers.every(layer => layer.coordinateSpace === 'project');
  const scaleUnitSystem = useMemo(() => projectScaleUnitSystem(context.projectXml, projectCoordinates), [context.projectXml, projectCoordinates]);
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
  // Measurement vertices are transient input, not an unsaved map artifact. Use the
  // same classification for the notice and the parent workbench dirty owner.
  const isLocalDirty = activeLayers.length > 0 || hasPersistentDraftPoints || backgroundDraftDirty;

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
            enabled={Boolean(context.projectPath && context.projectRevision && backend.uploadMapSources && !mapMetadataCommitBusy && !mapCatalogLoading && !mapCatalogError && (activeDocument || documents.length === 0))}
            onImport={handleSourceImport} />
          <button type="button" className="map-import-button" data-testid="map-refresh-sources" disabled={!context.projectPath || mapCatalogLoading || sourceImportBusy || mapMetadataCommitBusy}
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
        interactionDisabled={mapMetadataCommitBusy}
        loadError={mapCatalogError}
        onSelect={(id) => {
          if (sourceImportBusy || mapMetadataCommitBusy) return;
          requestGenerationRef.current += 1;
          setActiveDocumentID(id);
          setSelectedFeature(null);
          setSelectedBackendNode(null);
          setContextMenu(null);
        }}
        onCreated={handleCreateDocument}
        onCreationStateChange={busy => onDirtyStateChange?.('map.create:' + projectContextKey, busy)}
        language={language}
      />

      <div className="map-workspace-toolbar">
        <MapToolsToolbar
          activeTool={activeTool}
          onToolChange={handleToolChange}
          onZoomIn={() => setUserViewport((current) => zoomAt(current, [size.width / 2, size.height / 2], 1.5))}
          onZoomOut={() => setUserViewport((current) => zoomAt(current, [size.width / 2, size.height / 2], 1 / 1.5))}
          onFit={fitVisibleLayers}
          onZoomToPoint={() => setLocationDialog({ owner, initialPoint: screenToWorld([size.width / 2, size.height / 2], viewport) })}
          canFit={Boolean(visibleExtent)}
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

      {locationDialog?.owner === owner && <ZoomToPointDialog
        initialPoint={locationDialog.initialPoint} language={language} onClose={() => setLocationDialog(null)}
        onNavigate={target => { setUserViewport(navigationViewport(target, size, visibleExtent)); setLocationDialog(null); }} />}

      {(draftPoints.length > 0 || activeTool === 'measureLength' || activeTool === 'measureArea') && (
        <div className="map-draft-actions" style={draftPoints.length === 0 ? { visibility: 'hidden' } : undefined}
          aria-hidden={draftPoints.length === 0 ? 'true' : undefined}>
          <strong>{copy.draw}</strong>
          <span>{draftPoints.length} {text(language, '个顶点', 'vertices')}</span>
          {(activeTool === 'drawLine' || activeTool === 'drawPolygon' || activeTool === 'measureLength' || activeTool === 'measureArea') && <button type="button" onClick={finishSketch}>{copy.finish}</button>}
          <button type="button" disabled={draftPoints.length === 0} onClick={() => updateDraftPoints(() => [])}>{copy.cancel}</button>
        </div>
      )}
      {measurementResult && measurementResultOwner === owner && <div className="map-measurement-result" role="status">{measurementResult}</div>}
      <TerrainMeasurementReadout
        language={language}
        visible={activeTool === 'measureLength' || (measurementResultOwner === owner && measurementResultKind === 'length')}
        scopeKey={terrainMeasurementScopeKey}
        captureState={terrainMeasurementCaptureState}
        state={terrainElevationMeasurement.state}
      />
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
            if (layer) setUserViewport(fitExtent(layer.extent, size, 72));
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
              vectorFrames={vectorRendering.frames}
              velocityField={velocityField}
              viewport={viewport}
              onViewportChange={setUserViewport}
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
            <RasMapElementsOverlay key={legendScope} legend={currentLegend} language={language}
              visible={settings.IsMapOverlayVisible}>
              <ScaleBar pixelsPerUnit={viewport.scale} availableWidth={size.width - 32} unitSystem={scaleUnitSystem} language={language} />
            </RasMapElementsOverlay>
            {contextMenu && <RasContextualActionsBar key={`${contextMenu.layer.id}:${contextMenu.feature.id}`}
              name={contextMenu.feature.name} position={contextMenu} language={language}
              projectCoordinates={contextMenu.layer.coordinateSpace === 'project'}
              canHide={activeLayers.some(layer => layer.id === contextMenu.layer.id)}
              onZoom={zoomToContextFeature} onCopy={() => void copyContextFeature()}
              onHide={() => { updateActiveLayers(current => current.map(layer => layer.id === contextMenu.layer.id ? { ...layer, visible: false } : layer)); setContextMenu(null); }}
              onClose={() => setContextMenu(null)} />}

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
          <MapViewsPanel {...props} activeDocument={activeDocument} visualizationRevision={visualizationRevision}
            onCommitStateChange={setViewCommitBusy}
            currentExtent={activeDocument ? resultViewport : null} shortcutScopeRef={viewportRef}
            hasUnsavedDrafts={Boolean(props.hasUnsavedDrafts || isLocalDirty || sourceImportBusy || mapCatalogLoading || vectorCommitBusy)}
            onNavigateToExtent={extent => setUserViewport(fitExtent(extent, size, 0))}
            onVisualizationCommit={(source, receipt) => {
              if (projectContextKeyRef.current !== projectContextKey || source.projectPath !== context.projectPath ||
                  source.projectRevision !== context.projectRevision || source.projectSessionID !== context.projectSessionID ||
                  source.projectXml !== context.projectXml) return false;
              const current = activeDocumentRef.current;
              if (!activeDocument || !current || current.id !== activeDocument.id ||
                  (current.visualizationRevision ?? '').toUpperCase() !== (receipt.previousVisualizationRevision ?? '').toUpperCase()) return false;
              symbologySourceRef.current = null;
              setAppliedLegend(null);
              setSelectedBackendNode(null);
              // A saved-view edit changes only metadata. Re-read source captures
              // at the new revision while retaining the actual camera window.
              fittedSourceKey.current = JSON.stringify([owner, receipt.visualizationRevision, resultFrames.activeNodeKey,
                resultFrames.selectedSource?.resultPath, resultFrames.selectedSource?.revision]);
              setVisualizationRevision(receipt.visualizationRevision);
              setDocuments(previous => previous.map(document => ({ ...document,
                visualizationRevision: receipt.visualizationRevision, visibilityTransition: null })));
              setStatusMessage(text(language, '地图视图已保存。', 'Map view was saved.'));
              return true;
            }} />
          <details data-testid="map-analysis-mesh" onToggle={event => setMeshAnalysisOpen(event.currentTarget.open)}>
            <summary>{text(language, 'Mesh 单元 / 面时序', 'Mesh cell / face time series')}</summary>
            {meshAnalysisOpen && <p className="map-page-notice" data-testid="map-mesh-timeseries-side-effects">
              {text(language, '读取时会生成临时结果缓存，并在读取结束后清理；不会修改源 Result、Geometry 或工程 XML。',
                'Reading creates a temporary result cache and removes it when the read ends; it does not modify the source Result, Geometry, or project XML.')}
            </p>}
            {meshAnalysisOpen && <Suspense fallback={<p role="status">{text(language, '正在加载分析面板…', 'Loading analysis panel…')}</p>}>
              <MapMeshTimeSeriesPanel {...analysisPanelProps} />
            </Suspense>}
            {meshAnalysisOpen && resultMeshHover.loading && <p role="status">{text(language, '正在验证当前 Result 的 Flow Area 网格来源…', 'Verifying the current Result Flow Area mesh source…')}</p>}
            {meshAnalysisOpen && resultMeshHover.error && <p role="status">{resultMeshHover.error}</p>}
            {meshAnalysisOpen && !analysisCapture && <p className="map-page-notice">
              {text(language, '请先选择当前地图中已勾选的 Result 或 MultiResult，并等待结果来源完成捕获。',
                'Select a checked Result or MultiResult on this map and wait for its source capture.')}
            </p>}
          </details>
          <details data-testid="map-analysis-culvert" onToggle={event => setCulvertAnalysisOpen(event.currentTarget.open)}>
            <summary>{text(language, '涵洞剖面与时序', 'Culvert profile and time series')}</summary>
            {culvertAnalysisOpen && <Suspense fallback={<p role="status">{text(language, '正在加载分析面板…', 'Loading analysis panel…')}</p>}>
              <MapCulvertAnalysisPanel {...analysisPanelProps} />
            </Suspense>}
          </details>
          <details data-testid="map-analysis-sample-line" onToggle={event => setSampleLineAnalysisOpen(event.currentTarget.open)}>
            <summary>{text(language, '结果采样线', 'Result sample line')}</summary>
            {sampleLineAnalysisOpen && <Suspense fallback={<p role="status">{text(language, '正在加载分析面板…', 'Loading analysis panel…')}</p>}>
              <MapSampleLineAnalysisPanel {...analysisPanelProps} />
            </Suspense>}
          </details>
          <RasLayerPropertiesPane language={language} featureSelection={selectedFeatureDetails}
            backendSelection={selectedBackend} canZoomBackend={Boolean(selectedBackend && loadedLayerExtent(selectedBackend.nodeID))}
            onZoomFeature={fitFeature} onZoomBackend={zoomToLoadedLayer} />
          {symbologySource && selectedBackend && activeDocument && <RasterSymbologyEditor
            key={`${owner}:${selectedBackend.nodeID}`}
            backend={backend} context={context} language={language}
            source={symbologySource} viewport={resultViewport}
            onAppliedLegendChange={legend => {
              if (legendScopeRef.current !== legendScope) return;
              setAppliedLegend(current => legend ? { scope: legendScope, legend }
                : current?.scope === legendScope ? null : current);
            }}
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
          {selectedRenderSource && <VectorLayerStyleEditor
            backend={backend} language={language} sourceCapture={selectedRenderSource} viewport={resultViewport}
            backendRevision={backendRevision} settingsEpoch={vectorSettingsEpoch}
            onCommitStateChange={setVectorCommitBusy} onDirtyStateChange={onDirtyStateChange}
            hasUnsavedDrafts={Boolean(props.hasUnsavedDrafts || isLocalDirty || sourceImportBusy || mapCatalogLoading || viewCommitBusy)}
            dirtyOwners={props.dirtyOwners}
            onPreviewChange={(source, draft) => {
              if (!vectorSources.some(candidate => candidate.key === source.key)) return;
              setVectorPreview(current => draft ? { sourceKey: source.key, draft }
                : current?.sourceKey === source.key ? null : current);
            }}
            onReceipt={(source, receipt) => {
              const current = activeDocumentRef.current;
              if (projectContextKeyRef.current !== projectContextKey || !current || !activeDocument ||
                  current.id !== activeDocument.id || source.map.projectPath !== context.projectPath ||
                  source.expectedProjectRevision !== context.projectRevision || source.map.projectSessionID !== context.projectSessionID ||
                  source.map.projectXml !== context.projectXml ||
                  current.visualizationRevision?.toUpperCase() !== source.map.expectedVisualizationRevision?.toUpperCase() ||
                  !receipt.visualizationRevision || !vectorSources.some(candidate => candidate.key === source.key)) return false;
              setVectorPreview(null);
              symbologySourceRef.current = null;
              setAppliedLegend(null);
              fittedSourceKey.current = JSON.stringify([owner, receipt.visualizationRevision, resultFrames.activeNodeKey,
                resultFrames.selectedSource?.resultPath, resultFrames.selectedSource?.revision]);
              setVisualizationRevision(receipt.visualizationRevision);
              setDocuments(previous => previous.map(document => ({ ...document,
                visualizationRevision: receipt.visualizationRevision, visibilityTransition: null })));
              setStatusMessage(text(language, '矢量符号已保存。', 'Vector symbology was saved.'));
              return true;
            }} />}
          {(vectorRendering.loading || vectorRendering.errors.length > 0) && <div role="status" data-testid="map-vector-render-status">
            {vectorRendering.loading && <p>{text(language, '正在按当前视图绘制矢量符号…', 'Rendering vector styles for the current view…')}</p>}
            {vectorRendering.errors.map((issue, index) => <p key={`${issue.layerID}:${index}`}>{issue.message}</p>)}
          </div>}
          <MapSettingsPanel settings={settings} onChange={setSettings} language={language} />
          <details className="map-global-symbology-settings">
            <summary>{text(language, '全局矢量符号默认值', 'Global vector style defaults')}</summary>
            <GlobalSymbologyEditorPage backend={backend} backendRevision={props.backendRevision}
              language={language} onDirtyStateChange={onDirtyStateChange}
              onSaved={() => setVectorSettingsEpoch(value => value + 1)} />
          </details>
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
