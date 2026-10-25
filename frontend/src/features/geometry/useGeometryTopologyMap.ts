import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { BackendGateway, WorkbenchContext } from '../../shared/workbench';
import type { CatalogTreeNode, CatalogTreeNodeLocator, MapDocumentTab } from '../map/mapTypes';
import { loadMapDocumentCatalog, projectXmlSha } from '../map/ProjectTreeListener';
import type { MapTreeSnapshot } from '../map/ProjectTreeListener';
import type { MapAnalysisMapRequest } from '../map/analysis/mapAnalysisTypes';
import { sameSourcePath } from '../tools/attributes/attributeTableTypes';
import type {
  GeometryModelRequest, GeometryRow, GeometryTopologyArcPointsSnapshot, GeometryTopologyMapCommand,
  GeometryTopologyDeepConnectAnchor, GeometryTopologyMapRequestContext, GeometryTopologyMapTool,
  GeometryTopologySelectionMode, GeometryTopologySelectionScope, GeometryTopologyMarqueeHitMode,
  GeometryTopologySmartAddOptions, GeometryTopologySmartAddTerrainCapture, GeometryTopologySmartAddWindow,
  GeometryTopologyMapCommandPreviewResponse, GeometryTopologySnapshot, GeometryTopologySpatialSelectionResult,
} from './geometryTypes';
import type {
  GeometryTopologyCanvasProps, GeometryTopologyDeepConnectGesture, GeometryTopologyDeepConnectProgress,
  GeometryTopologyPathPreview, GeometryTopologyPathPreviewGesture, GeometryTopologyPathPreviewPriority,
  GeometryTopologyPathPreviewState,
  GeometryTopologyNodeMove, GeometryTopologySmartAddGesture, GeometryTopologySpatialSelection,
  GeometryTopologyViewportCapture,
} from './GeometryTopologyCanvas';
import { TOPOLOGY_SMART_ADD_DEFAULT_OPTIONS } from './TopologySmartAddControls';
import { createTopologyDeepConnectCommand } from './geometryDeepConnect';
import { useGeometryVectorSources } from './useGeometryVectorSources';
import { createPersistedVectorDeepConnectPreview, parseVectorDeepConnectPreview,
  deepConnectSourceEchoIdentity, persistedVectorSourceRevision, vectorDeepConnectComponentSelectionKey } from './geometryVectorDeepConnect';
import type { PersistedVectorDeepConnectComponent, PersistedVectorDeepConnectPreview, PersistedVectorDeepConnectSource,
  VectorSourceInspectionCandidate } from './geometryVectorDeepConnect';
import { emptyTopologySelection, resolveTopologyDiagnosticTarget, topologySelectionCamera, topologySelectionGeometry, topologySelectionSnapshotKey,
  topologyTableSelection, type TopologyDiagnosticTarget, type TopologySelectionFocus, type TopologySelectionKind } from './geometryTopologySelection';

interface MapTarget {
  key: string;
  label: string;
  document: MapDocumentTab;
  geometryNode: CatalogTreeNodeLocator;
  planNodes: CatalogTreeNodeLocator[];
}

interface MapGeometryCapture {
  owner: string;
  targetKey: string;
  mapID: string;
  contextToken: string;
  geometryRevision: string;
  geometryCanonicalPath: string;
  visualizationRevision: string;
  projectRevision: string;
  projectXmlSHA: string | null;
}

interface GeometryMapSelection {
  node: number[];
  arc: number[];
  region: number[];
}

interface OwnerValue<T> { owner: string; value: T }
interface SelectionEntry extends OwnerValue<GeometryMapSelection> { version: number }
interface ArcPointsCapture {
  owner: string;
  arcFID: number;
  revision: string;
  draftState: string;
  draftIdentity: string;
  snapshot: GeometryTopologyArcPointsSnapshot;
}

interface PathPreviewJob {
  id: number;
  event: GeometryTopologyPathPreviewGesture;
  priority: GeometryTopologyPathPreviewPriority;
  scopeKey: string;
  resolve: (value: GeometryTopologyMapCommandPreviewResponse | null) => void;
}

interface PathPreviewStateEntry {
  scopeKey: string;
  state: GeometryTopologyPathPreviewState;
}

interface PathPreviewErrorEntry {
  scopeKey: string;
  message: string;
}

export interface GeometryTopologyMapWorkspace {
  targets: Array<{ key: string; label: string }>;
  selectedTargetKey: string;
  onTargetChange: (targetKey: string) => void;
  loading: boolean;
  capturing: boolean;
  interactionBusy: boolean;
  error?: string;
  selectionScope: GeometryTopologySelectionScope;
  onSelectionScopeChange: (scope: GeometryTopologySelectionScope) => void;
  selectionMode: GeometryTopologySelectionMode;
  onSelectionModeChange: (mode: GeometryTopologySelectionMode) => void;
  marqueeHitMode: GeometryTopologyMarqueeHitMode;
  onMarqueeHitModeChange: (mode: GeometryTopologyMarqueeHitMode) => void;
  activeTool: GeometryTopologyMapTool;
  onActiveToolChange: (tool: GeometryTopologyMapTool) => void;
  splitMode: 'Anywhere' | 'AtVertex';
  onSplitModeChange: (mode: 'Anywhere' | 'AtVertex') => void;
  selectedArcFIDs: number[];
  arcPoints: GeometryTopologyArcPointsSnapshot | null;
  mapCellSize: number | null;
  terrainCapture: GeometryTopologySmartAddTerrainCapture | null;
  measuredWindow: GeometryTopologySmartAddWindow | null;
  smartAddOptions: GeometryTopologySmartAddOptions;
  canSmartAdd: boolean;
  onApplySmartAddOptions: (options: GeometryTopologySmartAddOptions) => void;
  deepConnectSourceFID: number | null;
  deepConnectAnchorCount: number;
  onCancelDeepConnect: () => void;
  vectorCandidates: VectorSourceInspectionCandidate[];
  vectorInspection: ReturnType<typeof useGeometryVectorSources>['inspection'];
  vectorSelectedComponent: ReturnType<typeof useGeometryVectorSources>['selectedComponent'];
  vectorSelectedComponentKey: string | null;
  vectorBusy: boolean;
  vectorCanInspect: boolean;
  vectorError?: string;
  onInspectVectorSources: () => void;
  onSelectVectorComponent: (source: PersistedVectorDeepConnectSource | null, component: PersistedVectorDeepConnectComponent | null) => void;
  onCancelVectorSource: () => void;
  deepConnectSourceKind: 'topology' | 'vector' | null;
  deepConnectSourceLabel: string | null;
  previewAvailable: boolean;
  previewScopeKey: string;
  previewState: GeometryTopologyPathPreviewState;
  pathPreview: GeometryTopologyPathPreview | null;
  draftReady: boolean;
  draftDirty: boolean;
  selectionSnapshotKey: string | null;
  selectionVersion: number;
  selectedFeatureCount: number;
  canZoomToSelection: boolean;
  onZoomToSelection: () => void;
  onLocateDiagnostic: (messageID: string, target: TopologyDiagnosticTarget) => TopologySelectionKind | null;
  onTableSelectionChange: (kind: TopologySelectionKind, rows: GeometryRow[]) => number | void;
  onMapCommand: (command: GeometryTopologyMapCommand) => Promise<void>;
  canvasProps: Omit<GeometryTopologyCanvasProps, 'language'>;
}

interface UseGeometryTopologyMapOptions {
  active: boolean;
  owner: string;
  backend: BackendGateway;
  backendRevision?: number;
  context: WorkbenchContext;
  model: GeometryModelRequest | null;
  topology?: GeometryTopologySnapshot;
  canEdit: boolean;
  busy: boolean;
  onLoadArcPoints: (arcFID: number) => Promise<GeometryTopologyArcPointsSnapshot>;
  onMapCommand: (expected: { revision: string; draftState: string; draftIdentity: string },
    mapOwner: Pick<GeometryTopologyMapRequestContext, 'mapID' | 'contextToken' | 'geometryRevision'>,
    command: GeometryTopologyMapCommand) => Promise<GeometryTopologySnapshot>;
}

interface MapGeometrySnapshot {
  mapID: string;
  mapIndex: number;
  mapName: string;
  mapType: string;
  projectRevision: string;
  projectXmlSHA: string | null;
  visualizationRevision: string;
  contextToken: string;
  geometryNode: CatalogTreeNodeLocator;
  geometryCanonicalPath: string;
  geometryRevision: string;
}

function requestID(): string {
  if (!globalThis.crypto?.randomUUID) throw new Error('此浏览器无法生成地图交互请求标识。');
  return globalThis.crypto.randomUUID();
}

function sameRevision(left: string | null | undefined, right: string | null | undefined): boolean {
  return !!left && !!right && left.toUpperCase() === right.toUpperCase();
}

function optionalContextString(value: Record<string, unknown> | null | undefined, key: string): string | null {
  const candidate = value?.[key];
  return typeof candidate === 'string' ? candidate : null;
}

function stableJSON(value: unknown): string {
  if (Array.isArray(value)) return `[${value.map(stableJSON).join(',')}]`;
  if (value && typeof value === 'object') {
    const record = value as Record<string, unknown>;
    return `{${Object.keys(record).sort().map((key) => `${JSON.stringify(key)}:${stableJSON(record[key])}`).join(',')}}`;
  }
  return JSON.stringify(value) ?? 'null';
}

function walk(nodes: CatalogTreeNode[]): CatalogTreeNode[] {
  const result: CatalogTreeNode[] = [];
  const visit = (node: CatalogTreeNode): void => {
    result.push(node);
    for (const child of node.children ?? []) visit(child);
  };
  nodes.forEach(visit);
  return result;
}

function checkedVisibleVectorCandidates(roots: CatalogTreeNode[] | null | undefined): VectorSourceInspectionCandidate[] {
  const candidates: VectorSourceInspectionCandidate[] = [];
  const visit = (node: CatalogTreeNode, ancestorsChecked: boolean): void => {
    const checked = ancestorsChecked && node.checked !== 'unchecked';
    const locator = node.locator;
    if (checked && node.checked === 'checked' && locator &&
        ((locator.nodeType === 'MapLayer' && (locator.mapType === 'PolylinesVector' || locator.mapType === 'PolygonsVector')) ||
          locator.nodeType === 'GdbVectorLayer' || locator.mapType === 'GdbVectorLayer')) {
      candidates.push({ node: locator, expectedFamily: null, expectedGdbSource: null });
    }
    for (const child of node.children ?? []) visit(child, checked);
  };
  roots?.forEach((root) => visit(root, true));
  const seen = new Set<string>();
  return candidates.filter((candidate) => {
    const key = stableJSON(candidate.node);
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}

function sameNodePath(left?: number[] | null, right?: number[] | null): boolean {
  if (left == null || right == null) return left == null && right == null;
  return left.length === right.length && left.every((value, index) => value === right[index]);
}

function planLocators(document: MapDocumentTab, geometry: CatalogTreeNodeLocator): CatalogTreeNodeLocator[] | null {
  if (document.mapType !== 'Plan') return [];
  const plans = walk(document.roots).flatMap((node) => {
    const locator = node.locator;
    return locator?.nodeType === 'Plan' && locator.rootName === 'Focused Plans' ? [locator] : [];
  });
  if (geometry.rootName === 'Focused Plans') {
    const matches = plans.filter((plan) => plan.assetID === geometry.parentPlanAssetID &&
      sameNodePath(plan.nodePath, geometry.parentPlanNodePath) && plan.nodeName === geometry.parentPlanNodeName);
    return matches.length === 1 ? matches : null;
  }
  if (geometry.rootName === 'Matching Layers' && plans.length >= 2) return plans;
  return null;
}

function targetsFor(documents: MapDocumentTab[], assetPath: string): MapTarget[] {
  const targets: MapTarget[] = [];
  for (const document of documents) {
    for (const node of walk(document.roots)) {
      const locator = node.locator;
      if (locator?.nodeType !== 'Geometry' || !locator.assetID || !sameSourcePath(locator.assetID, assetPath)) continue;
      const planNodes = planLocators(document, locator);
      if (!planNodes) continue;
      const key = stableJSON([document.mapID, locator]);
      targets.push({
        key,
        label: `${document.name} · ${node.name} · ${document.mapType}`,
        document,
        geometryNode: locator,
        planNodes,
      });
    }
  }
  return targets;
}

function validateMapGeometrySnapshot(value: unknown, target: MapTarget, projectRevision: string,
  projectXmlSHA: string | null, assetPath: string): MapGeometrySnapshot {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('地图 Geometry 读取没有返回有效快照。');
  const snapshot = value as Partial<MapGeometrySnapshot>;
  if (snapshot.mapID !== target.document.mapID || snapshot.mapIndex !== target.document.mapIndex ||
      snapshot.mapName !== target.document.name || snapshot.mapType !== target.document.mapType ||
      snapshot.projectRevision !== projectRevision || snapshot.projectXmlSHA !== projectXmlSHA ||
      snapshot.visualizationRevision !== target.document.visualizationRevision ||
      !snapshot.geometryNode || stableJSON(snapshot.geometryNode) !== stableJSON(target.geometryNode) ||
      typeof snapshot.geometryCanonicalPath !== 'string' || !sameSourcePath(snapshot.geometryCanonicalPath, assetPath) ||
      typeof snapshot.geometryRevision !== 'string' || snapshot.geometryRevision === 'missing' ||
      typeof snapshot.contextToken !== 'string' || !snapshot.contextToken.trim()) {
    throw new Error('地图 Geometry 回执与当前保存地图、来源定位或工程修订不匹配。');
  }
  return snapshot as MapGeometrySnapshot;
}

interface ExpectedMapCommandPreview {
  mapRequestContext: GeometryTopologyMapRequestContext;
  revision: string;
  draftIdentity: string;
  action: 'smartAdd' | 'deepConnect';
  sourceRevision: string;
  command: GeometryTopologyMapCommand;
}

function validateMapCommandPreview(value: unknown, expected: ExpectedMapCommandPreview): GeometryTopologyMapCommandPreviewResponse {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('Host 路径预览回执格式无效。');
  const raw = value as Record<string, unknown>;
  const context = expected.mapRequestContext;
  const strings = ['requestID', 'mapID', 'contextToken', 'geometryRevision', 'revision', 'draftIdentity', 'action', 'pathSHA256', 'sourceRevision'];
  if (strings.some((key) => typeof raw[key] !== 'string' || !String(raw[key]).trim()) ||
      raw.requestID !== context.requestID || raw.mapID !== context.mapID || raw.contextToken !== context.contextToken ||
      !sameRevision(String(raw.geometryRevision), expected.revision) || !sameRevision(String(raw.revision), expected.revision) ||
      !sameRevision(String(raw.draftIdentity), expected.draftIdentity) || raw.action !== expected.action ||
      !sameRevision(String(raw.sourceRevision), expected.sourceRevision) ||
      !/^[0-9a-f]{64}$/i.test(String(raw.pathSHA256)) || !Array.isArray(raw.coordinates) || raw.coordinates.length < 2) {
    throw new Error('Host 路径预览回执与当前地图、几何草稿、来源修订或操作不匹配。');
  }
  const coordinates = raw.coordinates.map((value, index) => {
    if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error(`Host 路径预览第 ${index + 1} 个坐标无效。`);
    const point = value as Record<string, unknown>;
    if (typeof point.x !== 'number' || !Number.isFinite(point.x) || typeof point.y !== 'number' || !Number.isFinite(point.y)) {
      throw new Error(`Host 路径预览第 ${index + 1} 个坐标无效。`);
    }
    return { x: point.x, y: point.y };
  });
  const command = expected.command;
  if (command.action === 'smartAdd' &&
      (!raw.smartAddTerrainCapture || stableJSON(raw.smartAddTerrainCapture) !== stableJSON(command.smartAddSource.terrainCapture))) {
    throw new Error('Host 路径预览没有回显完全相同的 Terrain 捕获。');
  }
  if (command.action === 'deepConnect' &&
      (!raw.deepConnectSource || stableJSON(deepConnectSourceEchoIdentity(raw.deepConnectSource)) !==
        stableJSON(deepConnectSourceEchoIdentity(command.deepConnectSource)))) {
    throw new Error('Host 路径预览没有回显完全相同的 DeepConnect 来源或锚点。');
  }
  return { ...raw, coordinates } as unknown as GeometryTopologyMapCommandPreviewResponse;
}

function selectionFromResult(result: GeometryTopologySpatialSelectionResult): GeometryMapSelection {
  const selection: GeometryMapSelection = { node: [], arc: [], region: [] };
  for (const feature of result.selections) {
    if (!Number.isInteger(feature.fid) || feature.fid < 0) throw new Error('空间选集回执包含无效的源要素 FID。');
    if (feature.featureKind === 'node') selection.node.push(feature.fid);
    else if (feature.featureKind === 'arc') selection.arc.push(feature.fid);
    else if (feature.featureKind === 'region') selection.region.push(feature.fid);
    else throw new Error('空间选集回执包含未知要素类型。');
  }
  for (const kind of ['node', 'arc', 'region'] as const) {
    if (new Set(selection[kind]).size !== selection[kind].length) throw new Error('空间选集回执包含重复的源要素 FID。');
  }
  return selection;
}

function emptySelection(): GeometryMapSelection {
  return emptyTopologySelection();
}

export function useGeometryTopologyMap({ active, owner, backend, backendRevision, context, model, topology,
  canEdit, busy, onLoadArcPoints, onMapCommand: editMapCommand }: UseGeometryTopologyMapOptions): GeometryTopologyMapWorkspace {
  const [targetsEntry, setTargetsEntry] = useState<OwnerValue<MapTarget[]> | null>(null);
  const [selectedTargetKey, setSelectedTargetKey] = useState('');
  const [captureEntry, setCaptureEntry] = useState<OwnerValue<MapGeometryCapture> | null>(null);
  const [viewportCapture, setViewportCapture] = useState<GeometryTopologyViewportCapture | null>(null);
  const [measuredViewportCapture, setMeasuredViewportCapture] = useState<GeometryTopologyViewportCapture | null>(null);
  const [selectionEntry, setSelectionEntry] = useState<SelectionEntry | null>(null);
  const selectionVersionRef = useRef(0);
  const [selectionFocus, setSelectionFocus] = useState<TopologySelectionFocus | null>(null);
  const focusRequestRef = useRef(0);
  const [arcPointsEntry, setArcPointsEntry] = useState<ArcPointsCapture | null>(null);
  const [arcPointsLoading, setArcPointsLoading] = useState(false);
  const [loading, setLoading] = useState(false);
  const [capturing, setCapturing] = useState(false);
  const [spatialBusy, setSpatialBusy] = useState(false);
  const [moveBusy, setMoveBusy] = useState(false);
  const [error, setError] = useState<string | undefined>();
  const [selectionScope, setSelectionScope] = useState<GeometryTopologySelectionScope>('All');
  const [selectionMode, setSelectionMode] = useState<GeometryTopologySelectionMode>('Replace');
  const [marqueeHitMode, setMarqueeHitMode] = useState<GeometryTopologyMarqueeHitMode>('Intersects');
  const [activeTool, setActiveTool] = useState<GeometryTopologyMapTool>('select');
  const [splitMode, setSplitMode] = useState<'Anywhere' | 'AtVertex'>('Anywhere');
  const [smartAddOptions, setSmartAddOptions] = useState<GeometryTopologySmartAddOptions>(TOPOLOGY_SMART_ADD_DEFAULT_OPTIONS);
  const [deepConnectProgressEntry, setDeepConnectProgressEntry] = useState<OwnerValue<GeometryTopologyDeepConnectProgress | null> | null>(null);
  const [mapCellSize, setMapCellSize] = useState<number | null>(null);
  const [pathPreviewEntry, setPathPreviewEntry] = useState<GeometryTopologyPathPreview | null>(null);
  const [previewStateEntry, setPreviewStateEntry] = useState<PathPreviewStateEntry | null>(null);
  const [pathPreviewErrorEntry, setPathPreviewErrorEntry] = useState<PathPreviewErrorEntry | null>(null);
  const catalogGenerationRef = useRef(0);
  const mapCaptureGenerationRef = useRef(0);
  const spatialGenerationRef = useRef(0);
  const spatialControllerRef = useRef<AbortController | null>(null);
  const moveGenerationRef = useRef(0);
  const mapCommandInFlightRef = useRef(false);
  const arcPointsGenerationRef = useRef(0);
  const pathPreviewGenerationRef = useRef(0);
  const pathPreviewActiveRef = useRef<{ job: PathPreviewJob; controller: AbortController } | null>(null);
  const pathPreviewPendingRef = useRef<PathPreviewJob | null>(null);
  const pathPreviewLatestRef = useRef<GeometryTopologyPathPreview | null>(null);
  const lastPreviewPointerRef = useRef<{ scopeKey: string; action: string; point: { x: number; y: number }; cellSize: number } | null>(null);
  const pathPreviewTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const pathPreviewExecutorRef = useRef<(job: PathPreviewJob, signal: AbortSignal) => Promise<GeometryTopologyMapCommandPreviewResponse>>(
    async () => { throw new Error('路径预览执行器尚未初始化。'); },
  );
  const onLoadArcPointsRef = useRef(onLoadArcPoints); onLoadArcPointsRef.current = onLoadArcPoints;
  const topologyRef = useRef(topology); topologyRef.current = topology;
  const captureRef = useRef(captureEntry); captureRef.current = captureEntry;
  const targetKeyRef = useRef(selectedTargetKey); targetKeyRef.current = selectedTargetKey;
  const selectionRef = useRef(selectionEntry); selectionRef.current = selectionEntry;
  const ownerRef = useRef(owner); ownerRef.current = owner;
  const measuredViewportRef = useRef<GeometryTopologyViewportCapture | null>(null);

  const ownerContextKey = JSON.stringify([owner, context.projectRevision, context.projectXml,
    context.coreLayerContext ?? null, context.projectSessionID, context.projectPath]);
  const selectionSnapshotKey = topologySelectionSnapshotKey(JSON.stringify([
    ownerContextKey, backendRevision ?? null, model?.assetPath ?? null, model?.coreLayerContext ?? null,
  ]), topology);
  const selectionSnapshotKeyRef = useRef(selectionSnapshotKey); selectionSnapshotKeyRef.current = selectionSnapshotKey;
  useEffect(() => {
    if (selectionRef.current?.owner !== selectionSnapshotKey) {
      const version = ++selectionVersionRef.current;
      const next = selectionSnapshotKey ? { owner: selectionSnapshotKey, value: emptySelection(), version } : null;
      selectionRef.current = next;
      setSelectionEntry(next);
    }
    setSelectionFocus((current) => current?.snapshotKey === selectionSnapshotKey ? current : null);
  }, [selectionSnapshotKey]);
  const mapContext = useMemo<WorkbenchContext>(() => ({
    projectSessionID: context.projectSessionID,
    projectPath: context.projectPath,
    projectXml: context.projectXml,
    projectRevision: context.projectRevision,
    selectedAsset: context.selectedAsset,
    coreLayerContext: model?.coreLayerContext ?? context.coreLayerContext,
  }), [context.projectSessionID, context.projectPath, context.projectXml, context.projectRevision,
    context.selectedAsset, context.coreLayerContext, model?.coreLayerContext]);

  const targets = targetsEntry?.owner === ownerContextKey ? targetsEntry.value : [];
  const selectedTarget = targets.find((target) => target.key === selectedTargetKey) ?? targets[0];
  const mapSourceKey = JSON.stringify([ownerContextKey, backendRevision ?? null, selectedTarget?.key ?? null]);
  const capture = captureEntry?.owner === ownerContextKey && captureEntry.value.targetKey === selectedTarget?.key
    ? captureEntry.value : undefined;
  const selection = selectionSnapshotKey && selectionEntry?.owner === selectionSnapshotKey ? selectionEntry.value : emptySelection();
  const selectionGeometry = useMemo(() => topologySelectionGeometry(topology, selection), [topology, selection]);
  const selectedFeatureCount = selection.node.length + selection.arc.length + selection.region.length;
  const draftIdentity = topology?.completion?.draftIdentity;
  const mapCaptureReady = !!active && !!capture && !!topology && !!draftIdentity &&
    topology.revision === capture.geometryRevision && sameRevision(topology.completion?.sourceRevision, topology.revision);
  const currentMeasuredViewport = measuredViewportCapture?.sourceKey === mapSourceKey ? measuredViewportCapture : null;
  const measuredWindow: GeometryTopologySmartAddWindow | null = currentMeasuredViewport
    ? { extent: currentMeasuredViewport.viewport, rows: currentMeasuredViewport.rows, columns: currentMeasuredViewport.columns }
    : null;
  const terrainCapture = topology?.smartAddTerrainCapture ?? null;
  const smartAddCaptureReady = !!terrainCapture && !!model && !!topology && !!capture && !!draftIdentity &&
    typeof terrainCapture.geometryAssetPath === 'string' && typeof terrainCapture.geometryRevision === 'string' &&
    typeof terrainCapture.projectPath === 'string' && typeof terrainCapture.canonicalTerrainPath === 'string' &&
    typeof terrainCapture.terrainName === 'string' && typeof terrainCapture.sourceRevision === 'string' &&
    sameSourcePath(terrainCapture.geometryAssetPath, model.assetPath) && sameRevision(terrainCapture.geometryRevision, topology.revision) &&
    typeof model.projectPath === 'string' && sameSourcePath(terrainCapture.projectPath, model.projectPath) &&
    (terrainCapture.projectSessionID ?? null) === optionalContextString(model.coreLayerContext, 'projectSessionID') &&
    (terrainCapture.coreLayerContextIdentityToken ?? null) === optionalContextString(model.coreLayerContext, 'identityToken') &&
    !!terrainCapture.canonicalTerrainPath.trim() && !!terrainCapture.terrainName.trim() && !!terrainCapture.sourceRevision.trim();
  const selectedArcFIDs = selection.arc;
  const selectedArcFID = selectedArcFIDs.length === 1 ? selectedArcFIDs[0] : null;
  const arcPoints = arcPointsEntry && topology && draftIdentity &&
    arcPointsEntry.owner === owner && arcPointsEntry.arcFID === selectedArcFID &&
    arcPointsEntry.revision === topology.revision && arcPointsEntry.draftState === topology.draftState &&
    arcPointsEntry.draftIdentity === draftIdentity
      ? arcPointsEntry.snapshot : null;
  const draftReady = mapCaptureReady && canEdit && !busy && !loading && !capturing && !spatialBusy &&
    !moveBusy && !arcPointsLoading && mapCellSize != null && Number.isFinite(mapCellSize) && mapCellSize > 0 &&
    !!currentMeasuredViewport && currentMeasuredViewport.mapCellSize === mapCellSize;
  const canSmartAdd = draftReady && smartAddCaptureReady && !!measuredWindow && backend.supports('topology.edit');
  const progressKey = JSON.stringify([owner, selectedTarget?.key ?? null, topology?.revision ?? null,
    topology?.draftState ?? null, draftIdentity ?? null]);
  const deepConnectProgress = deepConnectProgressEntry?.owner === progressKey ? deepConnectProgressEntry.value : null;
  const vectorMapRequest = useMemo<MapAnalysisMapRequest | null>(() => {
    if (!capture || !selectedTarget || !context.projectPath || context.projectXml == null || !capture.projectXmlSHA ||
        !selectedTarget.document.visualizationRevision || !context.projectSessionID) return null;
    return {
      projectPath: context.projectPath,
      projectXml: context.projectXml,
      projectXmlSHA: capture.projectXmlSHA,
      mapID: selectedTarget.document.mapID,
      mapIndex: selectedTarget.document.mapIndex,
      mapName: selectedTarget.document.name,
      mapType: selectedTarget.document.mapType,
      expectedVisualizationRevision: selectedTarget.document.visualizationRevision,
      projectSessionID: context.projectSessionID,
      ...(mapContext.coreLayerContext ? { coreLayerContext: mapContext.coreLayerContext } : {}),
    };
  }, [capture, context.projectPath, context.projectSessionID, context.projectXml, mapContext.coreLayerContext, selectedTarget]);
  const vectorMapTree = useMemo<MapTreeSnapshot | null>(() => {
    const document = selectedTarget?.document;
    if (!document?.visualizationRevision) return null;
    return {
      roots: document.roots,
      previousVisualizationRevision: null,
      visualizationRevision: document.visualizationRevision,
      expandedNodeIDs: [],
      selectedNodeID: null,
      hiddenLayerIDs: [],
    };
  }, [selectedTarget]);
  const vectorCandidates = useMemo(() => checkedVisibleVectorCandidates(selectedTarget?.document.roots), [selectedTarget]);
  const vectorSources = useGeometryVectorSources({
    active: active && activeTool === 'deepConnect',
    backend,
    backendRevision,
    mapRequest: vectorMapRequest,
    tree: vectorMapTree,
    projectRevision: context.projectRevision,
    geometryModel: model,
    geometryRevision: topology?.revision ?? null,
    topologyDraftState: topology?.draftState ?? null,
    topologyDraftIdentity: draftIdentity ?? null,
    targetGeometryNode: selectedTarget?.geometryNode ?? null,
    targetGeometryCanonicalPath: capture?.geometryCanonicalPath ?? null,
    targetGeometryRevision: capture?.geometryRevision ?? null,
  });
  const pathPreviewScopeKey = JSON.stringify([
    active, backendRevision ?? null, owner, ownerContextKey, mapSourceKey, capture?.mapID ?? null, capture?.contextToken ?? null,
    capture?.geometryRevision ?? null, topology?.revision ?? null, topology?.draftState ?? null, draftIdentity ?? null,
    activeTool, activeTool === 'smartAdd' ? smartAddOptions : null,
    activeTool === 'smartAdd' ? measuredWindow : null,
    activeTool === 'smartAdd' ? terrainCapture : null,
    activeTool === 'deepConnect' ? vectorSources.selectedComponent?.key ?? null : null,
    vectorSources.inspectionOwnerKey,
  ]);
  const previewAvailable = backend.supports('topology.mapCommand.preview') && mapCaptureReady && draftReady &&
    ((activeTool === 'smartAdd' && canSmartAdd) || (activeTool === 'deepConnect' && canEdit));
  const pathPreview = pathPreviewEntry?.scopeKey === pathPreviewScopeKey ? pathPreviewEntry : null;
  const previewState = previewStateEntry?.scopeKey === pathPreviewScopeKey
    ? previewStateEntry.state : previewAvailable ? 'idle' : 'unavailable';
  const pathPreviewError = pathPreviewErrorEntry?.scopeKey === pathPreviewScopeKey
    ? pathPreviewErrorEntry.message : undefined;
  const pathPreviewScopeRef = useRef(pathPreviewScopeKey);
  pathPreviewScopeRef.current = pathPreviewScopeKey;
  const startPathPreviewJobRef = useRef<(job: PathPreviewJob) => void>(() => undefined);
  const cancelPathPreview = useCallback((): void => {
    pathPreviewGenerationRef.current += 1;
    if (pathPreviewTimerRef.current) clearTimeout(pathPreviewTimerRef.current);
    pathPreviewTimerRef.current = null;
    pathPreviewActiveRef.current?.controller.abort();
    pathPreviewActiveRef.current = null;
    const pending = pathPreviewPendingRef.current;
    pathPreviewPendingRef.current = null;
    pending?.resolve(null);
    lastPreviewPointerRef.current = null;
    pathPreviewLatestRef.current = null;
    setPathPreviewEntry(null);
    setPreviewStateEntry({ scopeKey: pathPreviewScopeRef.current, state: 'idle' });
    setPathPreviewErrorEntry(null);
  }, []);

  pathPreviewExecutorRef.current = async (job, signal) => {
    const latest = topologyRef.current;
    const capturedMap = captureRef.current?.owner === ownerContextKey &&
      captureRef.current.value.targetKey === targetKeyRef.current ? captureRef.current.value : undefined;
    const currentDraftIdentity = latest?.completion?.draftIdentity;
    const viewport = measuredViewportRef.current?.sourceKey === mapSourceKey ? measuredViewportRef.current : null;
    if (signal.aborted || !active || !model || !latest || !capturedMap || !currentDraftIdentity || !viewport || !draftReady) {
      throw new DOMException('路径预览的地图、Geometry 草稿或相机捕获已过期。', 'AbortError');
    }
    const assertCurrent = (): void => {
      const currentTopology = topologyRef.current;
      const currentMapCapture = captureRef.current?.owner === ownerContextKey &&
        captureRef.current.value.targetKey === targetKeyRef.current ? captureRef.current.value : undefined;
      const currentViewport = measuredViewportRef.current?.sourceKey === mapSourceKey ? measuredViewportRef.current : null;
      if (signal.aborted || job.scopeKey !== pathPreviewScopeRef.current || !active || !draftReady ||
          currentTopology?.revision !== latest.revision || currentTopology.draftState !== latest.draftState ||
          currentTopology.completion?.draftIdentity !== currentDraftIdentity ||
          !sameRevision(currentTopology.completion?.sourceRevision, currentTopology.revision) ||
          currentMapCapture?.mapID !== capturedMap.mapID || currentMapCapture.contextToken !== capturedMap.contextToken ||
          currentMapCapture.geometryRevision !== capturedMap.geometryRevision ||
          currentViewport?.mapCellSize !== viewport.mapCellSize || job.event.mapCellSize !== viewport.mapCellSize) {
        throw new DOMException('路径预览的地图、Geometry 草稿或相机捕获已过期。', 'AbortError');
      }
    };
    assertCurrent();

    const mapRequestContext: GeometryTopologyMapRequestContext = {
      requestID: requestID(),
      mapID: capturedMap.mapID,
      contextToken: capturedMap.contextToken,
      geometryRevision: capturedMap.geometryRevision,
      expectedDraftIdentity: currentDraftIdentity,
    };
    let command: GeometryTopologyMapCommand;
    let sourceRevision: string;
    let body: unknown;
    let vectorPreview: PersistedVectorDeepConnectPreview | null = null;

    if (job.event.action === 'smartAdd') {
      const event = job.event;
      const currentTerrain = latest.smartAddTerrainCapture;
      const currentWindow = measuredViewportRef.current?.sourceKey === mapSourceKey
        ? measuredViewportRef.current : null;
      const path = event.coordinates;
      if (!canSmartAdd || !currentTerrain || !currentWindow || event.coordinates.length < 2 ||
          event.coordinates.length > 512 || event.coordinates.some((point) => !Number.isFinite(point.x) || !Number.isFinite(point.y)) ||
          stableJSON(event.window) !== stableJSON({ extent: currentWindow.viewport, rows: currentWindow.rows, columns: currentWindow.columns }) ||
          stableJSON(event.window) !== stableJSON(measuredWindow) ||
          (event.startNodeFID != null && (latest.nodes.rows.filter((row) => row.fid === event.startNodeFID).length !== 1 ||
            latest.nodes.rows.find((row) => row.fid === event.startNodeFID)?.x !== path[0]?.x ||
            latest.nodes.rows.find((row) => row.fid === event.startNodeFID)?.y !== path[0]?.y)) ||
          (event.startArcTarget != null && (event.splitMode !== 'Anywhere' ||
            !latest.map?.features.some((feature) => feature.featureKind === 'arc' && feature.fid === event.startArcTarget!.arcFID &&
              feature.paths.length === 1 && event.startArcTarget!.sourceSegmentIndex >= 0 &&
              event.startArcTarget!.sourceSegmentIndex < feature.paths[0]!.length - 1 &&
              feature.paths[0]!.some((point) => Number.isFinite(point.x) && Number.isFinite(point.y))) ||
            path[0]?.x !== event.startArcTarget.point.x || path[0]?.y !== event.startArcTarget.point.y))) {
        throw new DOMException('SmartAdd 预览需要当前的实测视野、Terrain 捕获和有效途经点。', 'AbortError');
      }
      if (path.some((point, index) => index > 0 && point.x === path[index - 1]?.x && point.y === path[index - 1]?.y)) {
        throw new Error('相邻 SmartAdd 途经点不能重合。');
      }
      sourceRevision = currentTerrain.sourceRevision;
      command = {
        action: 'smartAdd', featureKind: 'arc', featureIDs: [], coordinates: path.map((point) => ({ ...point })),
        mapCellSize: event.mapCellSize,
        smartAddSource: { terrainCapture: currentTerrain, window: event.window, options: smartAddOptions },
        ...(event.startNodeFID != null ? { startNodeFID: event.startNodeFID } : {}),
        ...(event.startArcTarget ? { startArcTarget: event.startArcTarget, splitMode: 'Anywhere' as const } : {}),
      };
      body = {
        model: model!, revision: latest.revision, draftState: latest.draftState,
        mutation: { operation: 'mapCommand', featureKind: 'map', featureIDs: [], mapCommand: command },
        mapRequestContext,
      };
    } else {
      const event = job.event;
      if (event.anchors.length < 2 || event.anchors.length > 512) {
        throw new Error('DeepConnect 需要同一精确来源上的 2 至 512 个锚点。');
      }
      sourceRevision = latest.revision;
      if (event.sourceKind === 'topology') {
        const feature = latest.map?.features.filter((item) => item.featureKind === 'arc' && item.fid === event.sourceFID) ?? [];
        const sourcePath = feature.length === 1 && feature[0]?.geometryType === 'line' && feature[0].paths.length === 1
          ? feature[0].paths[0] : undefined;
        if (event.componentKey || !sourcePath || sourcePath.length < 2 ||
            !latest.arcs.rows.some((row) => row.fid === event.sourceFID) ||
            event.sourceKey !== `topology:arc:${event.sourceFID}`) {
          throw new DOMException('拓扑 DeepConnect 来源不再是当前唯一弧段。', 'AbortError');
        }
        const first = event.anchors[0]!;
        const last = event.anchors[event.anchors.length - 1]!;
        const base = createTopologyDeepConnectCommand({
          model: model!, topology: latest, mapRequestContext, mapCellSize: event.mapCellSize,
          arcFID: event.sourceFID, startAnchor: first, endAnchor: last,
        });
        if (base.action !== 'deepConnect') throw new Error('拓扑 DeepConnect 命令类型无效。');
        command = {
          ...base,
          deepConnectSource: {
            ...base.deepConnectSource,
            anchors: event.anchors.map((anchor) => ({ ...anchor })),
          },
        };
        body = {
          model: model!, revision: latest.revision, draftState: latest.draftState,
          mutation: { operation: 'mapCommand', featureKind: 'map', featureIDs: [], mapCommand: command },
          mapRequestContext,
        };
      } else {
        const selected = vectorSources.selectedComponent;
        if (!selected || selected.key !== event.componentKey || selected.component.featureFID !== event.sourceFID ||
            event.sourceKey !== `vector:${selected.key}` || !vectorMapRequest || !context.projectRevision) {
          throw new DOMException('矢量 DeepConnect 来源组件已变更或过期。', 'AbortError');
        }
        vectorPreview = createPersistedVectorDeepConnectPreview({
          model: model!, topology: latest, mapRequestContext, mapCellSize: event.mapCellSize,
          currentMapRequest: vectorMapRequest, currentProjectRevision: context.projectRevision,
          source: selected.source, component: selected.component, anchors: event.anchors,
        });
        command = vectorPreview.command;
        body = vectorPreview.request;
        sourceRevision = persistedVectorSourceRevision(selected.source) ?? '';
      }
    }

    if (!backend.supports('topology.mapCommand.preview')) {
      throw new Error('当前 C# 服务没有发布 topology.mapCommand.preview。');
    }
    const raw = await backend.request<unknown>('topology.mapCommand.preview', body, { signal });
    assertCurrent();
    if (job.event.action === 'deepConnect' && job.event.sourceKind === 'vector') {
      if (!vectorPreview) throw new Error('矢量来源预览捕获缺失。');
      const parsed = parseVectorDeepConnectPreview(raw, vectorPreview);
      const response = validateMapCommandPreview(raw, {
        mapRequestContext, revision: latest.revision, draftIdentity: currentDraftIdentity,
        action: 'deepConnect', sourceRevision, command,
      });
      if (parsed.pathSHA256 !== response.pathSHA256 || stableJSON(parsed.coordinates) !== stableJSON(response.coordinates)) {
        throw new Error('矢量 DeepConnect 两层预览校验结果不一致。');
      }
      return response;
    }
    return validateMapCommandPreview(raw, {
      mapRequestContext, revision: latest.revision, draftIdentity: currentDraftIdentity,
      action: job.event.action, sourceRevision, command,
    });
  };

  const startPathPreviewJob = useCallback((job: PathPreviewJob): void => {
    if (job.scopeKey !== pathPreviewScopeRef.current) { job.resolve(null); return; }
    const controller = new AbortController();
    pathPreviewActiveRef.current = { job, controller };
    setPathPreviewErrorEntry(null);
    setPreviewStateEntry({ scopeKey: job.scopeKey, state: 'loading' });
    void pathPreviewExecutorRef.current(job, controller.signal)
      .then((response) => {
        const current = !controller.signal.aborted && job.scopeKey === pathPreviewScopeRef.current &&
          job.id === pathPreviewGenerationRef.current;
        if (!current) { job.resolve(null); return; }
        const entry = { scopeKey: job.scopeKey, response };
        pathPreviewLatestRef.current = entry;
        setPathPreviewEntry(entry);
        setPathPreviewErrorEntry(null);
        setPreviewStateEntry({ scopeKey: job.scopeKey, state: 'ready' });
        job.resolve(response);
      })
      .catch((caught: unknown) => {
        const current = !controller.signal.aborted && job.scopeKey === pathPreviewScopeRef.current &&
          job.id === pathPreviewGenerationRef.current;
        if (current && !(caught instanceof DOMException && caught.name === 'AbortError')) {
          const message = caught instanceof Error ? caught.message :
            typeof caught === 'string' ? caught : String(caught);
          setPathPreviewErrorEntry({
            scopeKey: job.scopeKey,
            message: message.trim() || '路径预览失败，Host 未返回诊断。',
          });
          setPreviewStateEntry({ scopeKey: job.scopeKey, state: 'failed' });
        }
        job.resolve(null);
      })
      .finally(() => {
        if (pathPreviewActiveRef.current?.job.id !== job.id) return;
        pathPreviewActiveRef.current = null;
        const pending = pathPreviewPendingRef.current;
        if (!pending) return;
        pathPreviewPendingRef.current = null;
        if (pending.priority === 'pointer') {
          pathPreviewPendingRef.current = pending;
          if (pathPreviewTimerRef.current) clearTimeout(pathPreviewTimerRef.current);
          pathPreviewTimerRef.current = setTimeout(() => {
            pathPreviewTimerRef.current = null;
            const next = pathPreviewPendingRef.current;
            pathPreviewPendingRef.current = null;
            if (next) startPathPreviewJobRef.current(next);
          }, 80);
        } else startPathPreviewJobRef.current(pending);
      });
  }, []);
  startPathPreviewJobRef.current = startPathPreviewJob;

  const onPathPreview = useCallback((event: GeometryTopologyPathPreviewGesture,
    priority: GeometryTopologyPathPreviewPriority): Promise<GeometryTopologyMapCommandPreviewResponse | null> => {
    if (!previewAvailable) return Promise.resolve(null);
    const scopeKey = pathPreviewScopeKey;
    if (priority !== 'pointer') {
      // A committed waypoint/anchor changes the path input even when the
      // pointer itself has not moved far enough to pass the throttle.
      lastPreviewPointerRef.current = null;
      pathPreviewLatestRef.current = null;
    }
    const lastPointer = lastPreviewPointerRef.current;
    const thresholdPixels = event.action === 'smartAdd' ? smartAddOptions.previewThrottlePixels : 1.5;
    if (priority === 'pointer' && lastPointer?.scopeKey === scopeKey && lastPointer.action === event.action &&
        lastPointer.cellSize === event.mapCellSize && Math.hypot(lastPointer.point.x - event.pointer.x,
          lastPointer.point.y - event.pointer.y) < thresholdPixels * event.mapCellSize) {
      const latest = pathPreviewLatestRef.current;
      return Promise.resolve(latest?.scopeKey === scopeKey && latest.response.action === event.action ? latest.response : null);
    }
    lastPreviewPointerRef.current = { scopeKey, action: event.action, point: { ...event.pointer }, cellSize: event.mapCellSize };
    const id = ++pathPreviewGenerationRef.current;
    const jobPromise = new Promise<GeometryTopologyMapCommandPreviewResponse | null>((resolve) => {
      const job: PathPreviewJob = { id, event, priority, scopeKey, resolve };
      setPathPreviewEntry(null);
      setPreviewStateEntry({ scopeKey, state: 'loading' });
      if (priority !== 'pointer') {
        if (pathPreviewTimerRef.current) clearTimeout(pathPreviewTimerRef.current);
        pathPreviewTimerRef.current = null;
        pathPreviewActiveRef.current?.controller.abort();
        pathPreviewActiveRef.current = null;
        pathPreviewPendingRef.current?.resolve(null);
        pathPreviewPendingRef.current = null;
        startPathPreviewJobRef.current(job);
      } else if (pathPreviewActiveRef.current) {
        pathPreviewPendingRef.current?.resolve(null);
        pathPreviewPendingRef.current = job;
      } else {
        pathPreviewPendingRef.current?.resolve(null);
        pathPreviewPendingRef.current = job;
        if (!pathPreviewTimerRef.current) {
          pathPreviewTimerRef.current = setTimeout(() => {
            pathPreviewTimerRef.current = null;
            const next = pathPreviewPendingRef.current;
            pathPreviewPendingRef.current = null;
            if (next) startPathPreviewJobRef.current(next);
          }, 80);
        }
      }
    });
    return jobPromise;
  }, [pathPreviewScopeKey, previewAvailable, smartAddOptions.previewThrottlePixels]);

  useEffect(() => {
    cancelPathPreview();
    return cancelPathPreview;
  }, [pathPreviewScopeKey, cancelPathPreview]);

  useEffect(() => {
    spatialGenerationRef.current += 1;
    spatialControllerRef.current?.abort();
    spatialControllerRef.current = null;
    setSpatialBusy(false);
    return () => {
      spatialGenerationRef.current += 1;
      spatialControllerRef.current?.abort();
      spatialControllerRef.current = null;
    };
  }, [active, backend, backendRevision, owner, selectedTarget?.key, topology?.revision, topology?.draftState, draftIdentity]);

  useEffect(() => {
    const generation = ++arcPointsGenerationRef.current;
    setArcPointsEntry(null);
    setArcPointsLoading(false);
    if (!active || selectedArcFID == null || !topology || !draftIdentity) return;
    const expected = {
      owner,
      selectionSnapshotKey,
      arcFID: selectedArcFID,
      revision: topology.revision,
      draftState: topology.draftState,
      draftIdentity,
    };
    setArcPointsLoading(true);
    void onLoadArcPointsRef.current(expected.arcFID)
      .then((snapshot) => {
        const latest = topologyRef.current;
        const latestSelection = selectionRef.current;
        if (generation !== arcPointsGenerationRef.current || ownerRef.current !== expected.owner ||
            latest?.revision !== expected.revision || latest.draftState !== expected.draftState ||
            latest.completion?.draftIdentity !== expected.draftIdentity ||
            selectionSnapshotKeyRef.current !== expected.selectionSnapshotKey ||
            latestSelection?.owner !== expected.selectionSnapshotKey || latestSelection.value.arc.length !== 1 ||
            latestSelection.value.arc[0] !== expected.arcFID) return;
        if (snapshot.arcFID !== expected.arcFID || snapshot.revision !== expected.revision ||
            snapshot.draftIdentity !== expected.draftIdentity || snapshot.rows.some((point) =>
              point.pointKind === 'internal' && (!Number.isInteger(point.internalPointIndex) || point.internalPointIndex! < 0))) {
          throw new Error('弧段点快照与当前来源弧段、修订或草稿不匹配。');
        }
        setArcPointsEntry({ ...expected, snapshot });
      })
      .catch((caught: unknown) => {
        if (generation === arcPointsGenerationRef.current && !(caught instanceof DOMException && caught.name === 'AbortError')) {
          setError(caught instanceof Error ? caught.message : String(caught));
        }
      })
      .finally(() => { if (generation === arcPointsGenerationRef.current) setArcPointsLoading(false); });
    return () => {
      if (arcPointsGenerationRef.current === generation) arcPointsGenerationRef.current += 1;
    };
  }, [active, draftIdentity, owner, selectedArcFID, selectionSnapshotKey, topology?.draftState, topology?.revision]);

  useEffect(() => {
    if (!active) return;
    const generation = ++catalogGenerationRef.current;
    const controller = new AbortController();
    const isCurrent = (): boolean => catalogGenerationRef.current === generation && ownerRef.current === owner && !controller.signal.aborted;
    setLoading(true);
    setError(undefined);
    setTargetsEntry(null);
    setCaptureEntry(null);
    setViewportCapture(null);
    setMeasuredViewportCapture(null);
    measuredViewportRef.current = null;
    setMapCellSize(null);
    setArcPointsEntry(null);
    setArcPointsLoading(false);
    setActiveTool('select');
    setSelectedTargetKey('');
    setSmartAddOptions(TOPOLOGY_SMART_ADD_DEFAULT_OPTIONS);
    setDeepConnectProgressEntry(null);
    if (!model || !context.projectPath || !context.projectRevision) {
      setLoading(false);
      return () => { controller.abort(); };
    }
    void (async () => {
      try {
        const catalog = await loadMapDocumentCatalog(backend, mapContext, { signal: controller.signal });
        if (!isCurrent()) return;
        const nextTargets = targetsFor(catalog.documents, model.assetPath);
        setTargetsEntry({ owner: ownerContextKey, value: nextTargets });
        setSelectedTargetKey((previous) => nextTargets.some((target) => target.key === previous)
          ? previous : nextTargets[0]?.key ?? '');
        if (nextTargets.length === 0) setError('当前工程保存的地图中，没有指向所选几何文件的精确 Geometry 图层。');
      } catch (caught) {
        if (isCurrent()) setError(caught instanceof Error ? caught.message : String(caught));
      } finally {
        if (isCurrent()) setLoading(false);
      }
    })();
    return () => { controller.abort(); if (catalogGenerationRef.current === generation) catalogGenerationRef.current += 1; };
  }, [active, backend, backendRevision, context.projectPath, context.projectRevision, mapContext, model?.assetPath, owner, ownerContextKey]);

  useEffect(() => {
    if (active) return;
    setDeepConnectProgressEntry(null);
    setActiveTool('select');
  }, [active]);

  useEffect(() => {
    if (!active || !selectedTarget || !model || !context.projectPath || !viewportCapture || viewportCapture.sourceKey !== mapSourceKey || !topology || !draftIdentity) return;
    if (capture && capture.targetKey === selectedTarget.key && sameRevision(capture.geometryRevision, topology.revision)) return;
    if (!backend.supports('map.geometry.load')) {
      setError('当前 C# 服务未发布地图几何读取操作。');
      return;
    }
    const generation = ++mapCaptureGenerationRef.current;
    const controller = new AbortController();
    const target = selectedTarget;
    const sourceOwner = owner;
    const sourceDraftState = topology.draftState;
    const sourceDraftIdentity = draftIdentity;
    const sourceRevision = topology.revision;
    let projectXmlSHA: string | null = null;
    setCapturing(true);
    setError(undefined);
    void (async () => {
      try {
        projectXmlSHA = await projectXmlSha(context.projectXml);
        if (!isMapLoadCurrent()) return;
        const request = {
          map: {
            projectPath: context.projectPath,
            projectXml: context.projectXml,
            projectXmlSHA,
            mapID: target.document.mapID,
            mapIndex: target.document.mapIndex,
            mapName: target.document.name,
            mapType: target.document.mapType,
            expectedVisualizationRevision: target.document.visualizationRevision,
            projectSessionID: context.projectSessionID,
            ...(mapContext.coreLayerContext ? { coreLayerContext: mapContext.coreLayerContext } : {}),
          },
          geometryNode: target.geometryNode,
          viewport: viewportCapture.viewport,
          pageSize: 1,
          skipTopologyPage: true,
          skipFlowAreasPage: true,
          skipCulvertsPage: true,
          ...(target.planNodes.length ? { planNodes: target.planNodes } : {}),
        };
        const raw = await backend.request<unknown>('map.geometry.load', request, { signal: controller.signal });
        if (!isMapLoadCurrent()) return;
        const snapshot = validateMapGeometrySnapshot(raw, target, context.projectRevision!, projectXmlSHA, model.assetPath);
        if (!sameRevision(snapshot.geometryRevision, sourceRevision) ||
            (context.selectedAsset?.revision && !sameRevision(snapshot.geometryRevision, context.selectedAsset.revision))) {
          throw new Error('保存地图中的几何源修订与当前拓扑草稿不一致，请刷新几何后重试。');
        }
        setCaptureEntry({ owner: ownerContextKey, value: {
          owner: sourceOwner,
          targetKey: target.key,
          mapID: snapshot.mapID,
          contextToken: snapshot.contextToken,
          geometryRevision: snapshot.geometryRevision,
          geometryCanonicalPath: snapshot.geometryCanonicalPath,
          visualizationRevision: snapshot.visualizationRevision,
          projectRevision: snapshot.projectRevision,
          projectXmlSHA,
        } });
      } catch (caught) {
        if (isMapLoadCurrent() && !(caught instanceof DOMException && caught.name === 'AbortError')) {
          setError(caught instanceof Error ? caught.message : String(caught));
        }
      } finally {
        if (isMapLoadCurrent()) setCapturing(false);
      }

      function isMapLoadCurrent(): boolean {
        const latest = topologyRef.current;
        return mapCaptureGenerationRef.current === generation && ownerRef.current === sourceOwner &&
          targetKeyRef.current === target.key && !controller.signal.aborted &&
          latest?.revision === sourceRevision && latest.draftState === sourceDraftState &&
          latest.completion?.draftIdentity === sourceDraftIdentity;
      }
    })();
    return () => { controller.abort(); if (mapCaptureGenerationRef.current === generation) mapCaptureGenerationRef.current += 1; };
  }, [active, backend, context.coreLayerContext, context.projectPath, context.projectRevision, context.projectSessionID,
    context.projectXml, context.selectedAsset?.revision, mapContext.coreLayerContext, capture, draftIdentity, model, owner, ownerContextKey,
    selectedTarget, topology, viewportCapture, mapSourceKey]);

  const onViewportReady = useCallback((next: GeometryTopologyViewportCapture): void => {
    if (next.sourceKey !== mapSourceKey || !Number.isFinite(next.mapCellSize) || next.mapCellSize <= 0 ||
        !Number.isSafeInteger(next.rows) || !Number.isSafeInteger(next.columns) || next.rows < 2 || next.columns < 2 ||
        next.rows * next.columns > 8_000_000) return;
    measuredViewportRef.current = next;
    setMeasuredViewportCapture((current) => current?.sourceKey === next.sourceKey && stableJSON(current) === stableJSON(next) ? current : next);
    setMapCellSize((current) => current === next.mapCellSize ? current : next.mapCellSize);
    const currentMapCapture = captureRef.current;
    if (currentMapCapture?.owner === ownerContextKey && currentMapCapture.value.targetKey === selectedTarget?.key) return;
    setViewportCapture((current) => current?.sourceKey === next.sourceKey && stableJSON(current) === stableJSON(next) ? current : next);
  }, [mapSourceKey, ownerContextKey, selectedTarget?.key]);

  const onApplySmartAddOptions = useCallback((options: GeometryTopologySmartAddOptions): void => {
    setSmartAddOptions(options);
    setActiveTool('smartAdd');
  }, []);

  const onDeepConnectProgress = useCallback((progress: GeometryTopologyDeepConnectProgress | null): void => {
    setDeepConnectProgressEntry({ owner: progressKey, value: progress });
  }, [progressKey]);

  const onCancelDeepConnect = useCallback((): void => {
    cancelPathPreview();
    setDeepConnectProgressEntry({ owner: progressKey, value: null });
    setActiveTool('select');
  }, [cancelPathPreview, progressKey]);

  const onInspectVectorSources = useCallback((): void => {
    setError(undefined);
    vectorSources.reload();
  }, [vectorSources.reload]);

  const onSelectVectorComponent = useCallback((source: PersistedVectorDeepConnectSource | null,
    component: PersistedVectorDeepConnectComponent | null): void => {
    if (!source || !component) {
      vectorSources.onSelectedComponentChange(null);
      setDeepConnectProgressEntry({ owner: progressKey, value: null });
      cancelPathPreview();
      return;
    }
    const key = vectorDeepConnectComponentSelectionKey(source, component);
    if (!vectorSources.components.some((item) => item.key === key && item.source.key === source.key)) {
      setError('所选矢量组件不属于当前 Host 已验证的地图来源。');
      vectorSources.onSelectedComponentChange(null);
      return;
    }
    vectorSources.onSelectedComponentChange(key);
    cancelPathPreview();
  }, [cancelPathPreview, progressKey, vectorSources.components, vectorSources.onSelectedComponentChange]);

  const onCancelVectorSource = useCallback((): void => {
    vectorSources.onSelectedComponentChange(null);
    setDeepConnectProgressEntry({ owner: progressKey, value: null });
    cancelPathPreview();
  }, [cancelPathPreview, progressKey, vectorSources.onSelectedComponentChange]);

  const cancelSpatialSelection = useCallback((): void => {
    spatialGenerationRef.current += 1;
    spatialControllerRef.current?.abort();
    spatialControllerRef.current = null;
    setSpatialBusy(false);
  }, []);

  const onTableSelectionChange = useCallback((kind: TopologySelectionKind, rows: GeometryRow[]): number | void => {
    const current = topologyRef.current;
    if (!current || !selectionSnapshotKey || selectionSnapshotKeyRef.current !== selectionSnapshotKey) return;
    cancelSpatialSelection();
    // Source ActiveFeatures UI selection clears the other two feature kinds.
    const entry = { owner: selectionSnapshotKey, value: topologyTableSelection(current, kind, rows), version: ++selectionVersionRef.current };
    selectionRef.current = entry;
    setSelectionEntry(entry);
    setSelectionFocus(null);
    return entry.version;
  }, [cancelSpatialSelection, selectionSnapshotKey]);

  const onZoomToSelection = useCallback((): void => {
    if (!selectionSnapshotKey || selectionSnapshotKeyRef.current !== selectionSnapshotKey ||
        selectionRef.current?.owner !== selectionSnapshotKey) return;
    const geometry = topologySelectionGeometry(topologyRef.current, selectionRef.current.value);
    if (geometry.extent) setSelectionFocus({ snapshotKey: selectionSnapshotKey, requestID: ++focusRequestRef.current, extent: geometry.extent });
  }, [selectionSnapshotKey]);

  const onLocateDiagnostic = useCallback((messageID: string, target: TopologyDiagnosticTarget): TopologySelectionKind | null => {
    if (!selectionSnapshotKey || selectionSnapshotKeyRef.current !== selectionSnapshotKey) return null;
    const resolved = resolveTopologyDiagnosticTarget(topologyRef.current, messageID, target);
    if (!resolved) return null;
    cancelSpatialSelection();
    const entry = { owner: selectionSnapshotKey, value: resolved.selection, version: ++selectionVersionRef.current };
    selectionRef.current = entry;
    setSelectionEntry(entry);
    setActiveTool('select');
    setSelectionFocus({ snapshotKey: selectionSnapshotKey, requestID: ++focusRequestRef.current, extent: resolved.extent });
    return resolved.kind;
  }, [cancelSpatialSelection, selectionSnapshotKey]);

  const onTargetChange = useCallback((key: string): void => {
    if (key === selectedTarget?.key) return;
    cancelSpatialSelection();
    setSelectionFocus(null);
    setSelectedTargetKey(key);
  }, [cancelSpatialSelection, selectedTarget?.key]);

  const onSpatialSelect = useCallback((event: GeometryTopologySpatialSelection): void => {
    if (!active || !mapCaptureReady || !model || !topology || !capture || !draftIdentity || !selectionSnapshotKey ||
        !backend.supports('topology.spatialSelect')) return;
    if (!Number.isFinite(event.mapCellSize) || event.mapCellSize <= 0) {
      setError('当前地图视野未能提供有效的源坐标单位/像素比例。');
      return;
    }
    spatialControllerRef.current?.abort();
    const controller = new AbortController();
    spatialControllerRef.current = controller;
    const generation = ++spatialGenerationRef.current;
    const expected = { revision: topology.revision, draftState: topology.draftState, draftIdentity };
    const selectedTargetKeyAtStart = selectedTarget?.key;
    const mapOwner = { mapID: capture.mapID, contextToken: capture.contextToken,
      geometryRevision: capture.geometryRevision };
    const mode = event.selectionMode ?? selectionMode;
    const scope = event.gesture === 'Click' ? 'All' : event.selectionScope ?? selectionScope;
    const request = {
      requestID: requestID(),
      model,
      revision: expected.revision,
      draftState: expected.draftState,
      draftIdentity: expected.draftIdentity,
      mapID: mapOwner.mapID,
      contextToken: mapOwner.contextToken,
      geometryRevision: mapOwner.geometryRevision,
      selectionScope: scope,
      selectionMode: mode,
      gesture: event.gesture,
      worldPoints: event.worldPoints,
      mapCellSize: event.mapCellSize,
      marqueeHitMode: event.gesture === 'Extent' ? event.marqueeHitMode ?? marqueeHitMode : null,
      currentArcFIDs: selection.arc,
      currentNodeFIDs: selection.node,
      currentRegionFIDs: selection.region,
    };
    setSpatialBusy(true);
    setError(undefined);
    void backend.request<GeometryTopologySpatialSelectionResult>('topology.spatialSelect', request, { signal: controller.signal })
      .then((result) => {
        const latest = topologyRef.current;
        const currentCapture = captureRef.current;
        if (generation !== spatialGenerationRef.current || ownerRef.current !== owner || selectionSnapshotKeyRef.current !== selectionSnapshotKey ||
            targetKeyRef.current !== selectedTargetKeyAtStart || latest?.revision !== expected.revision ||
            latest.draftState !== expected.draftState || latest.completion?.draftIdentity !== expected.draftIdentity ||
            currentCapture?.owner !== ownerContextKey || currentCapture.value.targetKey !== selectedTargetKeyAtStart ||
            result.requestID !== request.requestID || result.mapID !== mapOwner.mapID ||
            result.contextToken !== mapOwner.contextToken || result.geometryRevision !== mapOwner.geometryRevision ||
            result.revision !== expected.revision || result.draftIdentity !== expected.draftIdentity) return;
        const entry = { owner: selectionSnapshotKey, value: selectionFromResult(result), version: ++selectionVersionRef.current };
        selectionRef.current = entry;
        setSelectionEntry(entry);
        setSelectionFocus(null);
      })
      .catch((caught: unknown) => {
        if (generation === spatialGenerationRef.current && !(caught instanceof DOMException && caught.name === 'AbortError')) {
          setError(caught instanceof Error ? caught.message : String(caught));
        }
      })
      .finally(() => {
        if (generation === spatialGenerationRef.current) {
          spatialControllerRef.current = null;
          setSpatialBusy(false);
        }
      });
  }, [active, backend, capture, draftIdentity, mapCaptureReady, marqueeHitMode, model, owner, ownerContextKey,
    selection, selectionMode, selectionScope, selectionSnapshotKey, selectedTarget?.key, topology]);

  const runMapCommand = useCallback(async (command: GeometryTopologyMapCommand): Promise<void> => {
    const current = topologyRef.current;
    const currentCapture = captureRef.current?.owner === ownerContextKey &&
      captureRef.current.value.targetKey === targetKeyRef.current ? captureRef.current.value : undefined;
    const currentDraftIdentity = current?.completion?.draftIdentity;
    const currentSelection = selectionRef.current?.owner === selectionSnapshotKeyRef.current ? selectionRef.current.value : emptySelection();
    if (!active || !canEdit || busy || spatialBusy || moveBusy || mapCommandInFlightRef.current || !current || !currentCapture || !currentDraftIdentity ||
        current.revision !== currentCapture.geometryRevision || !sameRevision(current.completion?.sourceRevision, current.revision)) {
      throw new DOMException('当前几何地图或拓扑草稿已过期，未提交地图操作。', 'AbortError');
    }
    if (!Number.isFinite(command.mapCellSize) || command.mapCellSize <= 0) {
      throw new Error('地图操作需要当前有效的源坐标单位/像素比例。');
    }
    const map = current.map;
    const arcFeatures = (fid: number) => map?.features.filter((feature) => feature.featureKind === 'arc' && feature.fid === fid) ?? [];
    const nodeFeatures = (fid: number) => map?.features.filter((feature) => feature.featureKind === 'node' && feature.fid === fid) ?? [];
    const arcFeature = (fid: number) => {
      const matches = arcFeatures(fid);
      return matches.length === 1 ? matches[0] : undefined;
    };
    const nodeFeature = (fid: number) => {
      const matches = nodeFeatures(fid);
      return matches.length === 1 ? matches[0] : undefined;
    };
    const nodePoint = (fid: number) => {
      const feature = nodeFeature(fid);
      const path = feature?.paths.length === 1 ? feature.paths[0] : undefined;
      const point = path?.length === 1 ? path[0] : undefined;
      return feature?.geometryType === 'point' && point && Number.isFinite(point.x) && Number.isFinite(point.y)
        ? point : undefined;
    };
    const validArc = (fid: number): boolean => Number.isSafeInteger(fid) && fid >= 0 && !!arcFeature(fid) &&
      current.arcs.rows.some((row) => row.fid === fid);
    const validNode = (fid: number): boolean => Number.isSafeInteger(fid) && fid >= 0 && !!nodeFeature(fid) &&
      current.nodes.rows.some((row) => row.fid === fid);
    const validSegment = (fid: number, segment: number): boolean => {
      const feature = arcFeature(fid);
      const path = feature?.paths.length === 1 ? feature.paths[0] : null;
      return !!path && path.length >= 2 && Number.isInteger(segment) && segment >= 0 && segment < path.length - 1;
    };
    const validCoordinate = (point: { x: number; y: number }): boolean => Number.isFinite(point.x) && Number.isFinite(point.y);
    const currentViewport = measuredViewportRef.current?.sourceKey === mapSourceKey ? measuredViewportRef.current : null;
    const sameCapturedMapScale = !!currentViewport && command.mapCellSize === currentViewport.mapCellSize;
    const sameCoordinate = (left: { x: number; y: number }, right: { x: number; y: number }): boolean =>
      left.x === right.x && left.y === right.y;
    const currentArcPointsCapture = (fid: number): boolean => !!arcPoints &&
      arcPointsEntry?.owner === owner && arcPointsEntry.arcFID === fid && arcPointsEntry.revision === current.revision &&
      arcPointsEntry.draftState === current.draftState && arcPointsEntry.draftIdentity === currentDraftIdentity &&
      arcPointsEntry.snapshot === arcPoints && arcPoints.arcFID === fid && arcPoints.revision === current.revision &&
      arcPoints.draftIdentity === currentDraftIdentity;
    if (command.action === 'moveNode') {
      const uniqueNodes = [...new Set(command.nodeFIDs)];
      if (!validNode(command.nodeFID) || !uniqueNodes.length || uniqueNodes.length !== command.nodeFIDs.length ||
          !uniqueNodes.includes(command.nodeFID) ||
          uniqueNodes.some((fid) => !validNode(fid) || (!currentSelection.node.includes(fid) && fid !== command.nodeFID)) ||
          !command.coordinates.every(validCoordinate)) {
        throw new DOMException('节点编辑目标或坐标已不在当前地图拓扑快照中。', 'AbortError');
      }
    } else if (command.action === 'addArc') {
      if (command.coordinates.length < 2 || command.coordinates.some((point) => !validCoordinate(point)) ||
          (command.startNodeFID != null && !validNode(command.startNodeFID)) ||
          (command.startArcTarget != null && (!validArc(command.startArcTarget.arcFID) ||
            !validSegment(command.startArcTarget.arcFID, command.startArcTarget.sourceSegmentIndex) ||
            !validCoordinate(command.startArcTarget.point)))) {
        throw new DOMException('绘制弧段的节点、源弧段或坐标已不在当前地图快照中。', 'AbortError');
      }
    } else if (command.action === 'smartAdd') {
      const source = command.smartAddSource;
      const currentTerrain = current.smartAddTerrainCapture;
      const window = source?.window;
      if (!sameCapturedMapScale || !canSmartAdd || !currentViewport || !source || !currentTerrain ||
          stableJSON(source.terrainCapture) !== stableJSON(currentTerrain) ||
          stableJSON(window) !== stableJSON({ extent: currentViewport.viewport, rows: currentViewport.rows, columns: currentViewport.columns }) ||
          stableJSON(source.options ?? null) !== stableJSON(smartAddOptions) ||
          command.coordinates.length < 2 || command.coordinates.length > 512 ||
          command.coordinates.some((point) => !validCoordinate(point)) ||
          command.coordinates.some((point, index) => index > 0 && sameCoordinate(point, command.coordinates[index - 1]!)) ||
          !/^[0-9a-f]{64}$/.test(command.expectedPathSHA256 ?? '') ||
          (command.startNodeFID != null && (!validNode(command.startNodeFID) || !nodePoint(command.startNodeFID) ||
            !sameCoordinate(command.coordinates[0], nodePoint(command.startNodeFID)!))) ||
          (command.startArcTarget != null && (!validArc(command.startArcTarget.arcFID) ||
            !validSegment(command.startArcTarget.arcFID, command.startArcTarget.sourceSegmentIndex) ||
            !validCoordinate(command.startArcTarget.point) ||
            !sameCoordinate(command.coordinates[0], command.startArcTarget.point) || command.splitMode !== 'Anywhere')) ||
          (command.startNodeFID != null && command.startArcTarget != null)) {
        throw new DOMException('SmartAdd 的地形捕获、实测窗口、起点或地图比例已过期。', 'AbortError');
      }
    } else if (command.action === 'deepConnect') {
      const source = command.deepConnectSource;
      const sourceOwner = source?.topologySource;
      const mapOwner = currentCapture;
      const anchors = source?.anchors?.length ? source.anchors : source
        ? [{ segmentIndex: source.startSegmentIndex, fraction: source.startSegmentFraction },
          { segmentIndex: source.endSegmentIndex, fraction: source.endSegmentFraction }]
        : [];
      const firstAnchor = anchors[0];
      const lastAnchor = anchors[anchors.length - 1];
      const currentVector = vectorSources.selectedComponent;
      if (!sameCapturedMapScale || !source || !mapOwner || command.coordinates !== null ||
          !/^[0-9a-f]{64}$/.test(command.expectedPathSHA256 ?? '') || anchors.length < 2 || anchors.length > 512 ||
          source.startSegmentIndex !== firstAnchor?.segmentIndex || source.startSegmentFraction !== firstAnchor?.fraction ||
          source.endSegmentIndex !== lastAnchor?.segmentIndex || source.endSegmentFraction !== lastAnchor?.fraction ||
          source.anchors && stableJSON(source.anchors) !== stableJSON(anchors)) {
        throw new DOMException('DeepConnect 的源弧段、锚点或地图草稿捕获已过期。', 'AbortError');
      }
      if (source.sourceKind === 'topology') {
        const sourcePath = arcFeature(source.featureFID)?.paths[0];
        const mapRequest = sourceOwner?.mapRequestContext;
        const validAnchor = (anchor: GeometryTopologyDeepConnectAnchor): boolean => Number.isInteger(anchor.segmentIndex) &&
          anchor.segmentIndex >= 0 && !!sourcePath && anchor.segmentIndex < sourcePath.length - 1 &&
          Number.isFinite(anchor.fraction) && anchor.fraction >= 0 && anchor.fraction <= 1;
        if (source.vectorSource != null || !sourceOwner || stableJSON(sourceOwner.model) !== stableJSON(model) ||
            sourceOwner.featureKind !== 'arc' || sourceOwner.revision !== current.revision ||
            sourceOwner.draftState !== current.draftState || sourceOwner.draftIdentity !== currentDraftIdentity ||
            sourceOwner.featureID !== `arc:${source.featureFID}` || !mapRequest || mapRequest.mapID !== mapOwner.mapID ||
            mapRequest.contextToken !== mapOwner.contextToken || mapRequest.geometryRevision !== mapOwner.geometryRevision ||
            mapRequest.expectedDraftIdentity !== currentDraftIdentity || !mapRequest.requestID ||
            source.sourceLocator !== `arc:${source.featureFID}` || source.sourceRevision !== current.revision ||
            source.pathIndex !== 0 || source.componentIdentity !== `arc:${source.featureFID}:path:0` ||
            !validArc(source.featureFID) || !Array.isArray(source.points) || !sourcePath || sourcePath.length !== source.points.length ||
            sourcePath.some((point, index) => !sameCoordinate(point, source.points[index]!)) || anchors.some((anchor) => !validAnchor(anchor))) {
          throw new DOMException('拓扑 DeepConnect 的弧段来源、完整折线或草稿捕获已过期。', 'AbortError');
        }
        const expected = createTopologyDeepConnectCommand({
          model: model!, topology: current,
          mapRequestContext: mapRequest,
          mapCellSize: command.mapCellSize,
          arcFID: source.featureFID,
          startAnchor: firstAnchor!, endAnchor: lastAnchor!,
        });
        if (expected.action !== 'deepConnect' ||
            stableJSON(expected.deepConnectSource && { ...expected.deepConnectSource, anchors: anchors.map((anchor) => ({ ...anchor })) }) !== stableJSON(source) ||
            expected.startNodeFID !== command.startNodeFID || stableJSON(expected.startArcTarget ?? null) !== stableJSON(command.startArcTarget ?? null) ||
            expected.splitMode !== command.splitMode || source.vectorSource != null) {
          throw new DOMException('拓扑 DeepConnect 的确认命令与当前来源捕获不一致。', 'AbortError');
        }
      } else if (source.sourceKind === 'vector') {
        if (!currentVector || !vectorMapRequest || !context.projectRevision || source.topologySource != null ||
            source.sourceRevision !== persistedVectorSourceRevision(currentVector.source) ||
            source.featureFID !== currentVector.component.featureFID || source.pathIndex !== currentVector.component.pathIndex ||
            source.componentIdentity !== currentVector.component.componentIdentity ||
            stableJSON(source.points) !== stableJSON(currentVector.component.points)) {
          throw new DOMException('矢量 DeepConnect 的 Host 来源组件或修订已变更。', 'AbortError');
        }
        const expected = createPersistedVectorDeepConnectPreview({
          model: model!, topology: current,
          mapRequestContext: {
            requestID: requestID(), mapID: mapOwner.mapID, contextToken: mapOwner.contextToken,
            geometryRevision: mapOwner.geometryRevision, expectedDraftIdentity: currentDraftIdentity,
          },
          mapCellSize: command.mapCellSize, currentMapRequest: vectorMapRequest,
          currentProjectRevision: context.projectRevision,
          source: currentVector.source, component: currentVector.component, anchors,
        });
        if (stableJSON(expected.command.deepConnectSource) !== stableJSON(source) ||
            expected.command.startNodeFID !== command.startNodeFID || command.startArcTarget != null || command.splitMode != null) {
          throw new DOMException('矢量 DeepConnect 确认命令与当前来源组件不一致。', 'AbortError');
        }
      } else {
        throw new DOMException('DeepConnect 来源类型不受支持。', 'AbortError');
      }
    }
    if (command.action === 'moveArcPoint' || command.action === 'deleteArcPoint') {
      if (!validArc(command.arcFID) || !Number.isInteger(command.pointIndex) || command.pointIndex < 0 ||
          !currentSelection.arc.includes(command.arcFID) || !arcPoints || !currentArcPointsCapture(command.arcFID) ||
          !arcPoints.rows.some((point) => point.pointKind === 'internal' && point.internalPointIndex === command.pointIndex)) {
        throw new DOMException('弧段点快照已过期或目标不是当前选中弧段的内部点。', 'AbortError');
      }
      if (command.action === 'moveArcPoint' && command.coordinates.some((point) => !validCoordinate(point))) {
        throw new Error('弧段内点坐标必须是有限源坐标。');
      }
    }
    if (command.action === 'insertArcPoint' && (!validArc(command.arcFID) || !currentSelection.arc.includes(command.arcFID) ||
        !currentArcPointsCapture(command.arcFID) || !validSegment(command.arcFID, command.sourceSegmentIndex) ||
        command.coordinates.some((point) => !validCoordinate(point)))) {
      throw new DOMException('插入点目标已不在当前弧段地图快照中。', 'AbortError');
    }
    if (command.action === 'splitArc' && (!validArc(command.arcFID) || !validCoordinate(command.coordinates[0]))) {
      throw new DOMException('分割目标已不在当前弧段地图快照中。', 'AbortError');
    }
    if (command.action === 'reverseArc' || command.action === 'filterArc' || command.action === 'smoothArc') {
      const targetFIDs = [...new Set(command.featureIDs)];
      if (targetFIDs.length === 0 || targetFIDs.length !== command.featureIDs.length ||
          targetFIDs.length !== currentSelection.arc.length ||
          targetFIDs.some((fid) => !currentSelection.arc.includes(fid) || !validArc(fid))) {
        throw new DOMException('弧段变换目标与当前稳定 FID 选集不一致。', 'AbortError');
      }
    }
    const expected = { revision: current.revision, draftState: current.draftState, draftIdentity: currentDraftIdentity };
    const mapOwner = { mapID: currentCapture.mapID, contextToken: currentCapture.contextToken,
      geometryRevision: currentCapture.geometryRevision };
    const generation = ++moveGenerationRef.current;
    mapCommandInFlightRef.current = true;
    setMoveBusy(true);
    setError(undefined);
    try {
      const result = await editMapCommand(expected, mapOwner, command);
      if ((command.action === 'smartAdd' || command.action === 'deepConnect') && result.draftState === expected.draftState) {
        throw new Error(command.action === 'smartAdd'
          ? 'Host 未在拓扑草稿中确认 SmartAdd 路径；输入预览仍保留。'
          : 'Host 未在拓扑草稿中确认 DeepConnect 结果；锚点仍保留。');
      }
    } catch (caught) {
      if (generation === moveGenerationRef.current && !(caught instanceof DOMException && caught.name === 'AbortError')) {
        setError(caught instanceof Error ? caught.message : String(caught));
      }
      throw caught;
    } finally {
      mapCommandInFlightRef.current = false;
      if (generation === moveGenerationRef.current) setMoveBusy(false);
    }
  }, [active, arcPoints, arcPointsEntry, busy, canEdit, canSmartAdd, editMapCommand, mapSourceKey,
    model, moveBusy, owner, ownerContextKey, spatialBusy, smartAddOptions, context.projectRevision,
    vectorMapRequest, vectorSources.selectedComponent]);

  const onSmartAdd = useCallback(async (event: GeometryTopologySmartAddGesture): Promise<void> => {
    const current = topologyRef.current;
    const capturedWindow = measuredViewportRef.current?.sourceKey === mapSourceKey
      ? measuredViewportRef.current : null;
    const currentTerrain = current?.smartAddTerrainCapture;
    if (!active || !current || !currentTerrain || !capturedWindow || !canSmartAdd ||
        event.coordinates.length < 2 || event.coordinates.length > 512 ||
        !/^[0-9a-f]{64}$/.test(event.expectedPathSHA256) ||
        event.mapCellSize !== capturedWindow.mapCellSize ||
        stableJSON(event.window) !== stableJSON({ extent: capturedWindow.viewport, rows: capturedWindow.rows, columns: capturedWindow.columns }) ||
        stableJSON(event.window) !== stableJSON(measuredWindow)) {
      throw new DOMException('SmartAdd 必须使用本次点击时的真实地图相机范围与地形捕获。', 'AbortError');
    }
    const command: GeometryTopologyMapCommand = {
      action: 'smartAdd',
      featureKind: 'arc',
      featureIDs: [],
      coordinates: event.coordinates,
      mapCellSize: event.mapCellSize,
      smartAddSource: { terrainCapture: currentTerrain, window: event.window, options: smartAddOptions },
      expectedPathSHA256: event.expectedPathSHA256,
      ...(event.startNodeFID != null ? { startNodeFID: event.startNodeFID } : {}),
      ...(event.startArcTarget ? { startArcTarget: event.startArcTarget, splitMode: 'Anywhere' as const } : {}),
    };
    await runMapCommand(command);
  }, [active, canSmartAdd, mapSourceKey, measuredWindow, runMapCommand, smartAddOptions]);

  const onDeepConnect = useCallback(async (event: GeometryTopologyDeepConnectGesture): Promise<void> => {
    const current = topologyRef.current;
    const currentCapture = captureRef.current?.owner === ownerContextKey &&
      captureRef.current.value.targetKey === targetKeyRef.current ? captureRef.current.value : undefined;
    const currentDraftIdentity = current?.completion?.draftIdentity;
    const currentViewport = measuredViewportRef.current?.sourceKey === mapSourceKey ? measuredViewportRef.current : null;
    if (!active || !model || !current || !currentCapture || !currentDraftIdentity || !currentViewport ||
        event.anchors.length < 2 || event.anchors.length > 512 || !/^[0-9a-f]{64}$/.test(event.expectedPathSHA256) ||
        !canEdit || !sameRevision(current.revision, currentCapture.geometryRevision) ||
        !sameRevision(current.completion?.sourceRevision, current.revision) || event.mapCellSize !== currentViewport.mapCellSize) {
      throw new DOMException('DeepConnect 当前地图捕获或拓扑草稿已过期。', 'AbortError');
    }
    const mapRequestContext: GeometryTopologyMapRequestContext = {
      requestID: requestID(), mapID: currentCapture.mapID, contextToken: currentCapture.contextToken,
      geometryRevision: currentCapture.geometryRevision, expectedDraftIdentity: currentDraftIdentity,
    };
    let command: GeometryTopologyMapCommand;
    if (event.sourceKind === 'topology') {
      const first = event.anchors[0]!;
      const last = event.anchors[event.anchors.length - 1]!;
      const base = createTopologyDeepConnectCommand({
        model, topology: current, mapRequestContext, mapCellSize: event.mapCellSize,
        arcFID: event.sourceFID, startAnchor: first, endAnchor: last,
      });
      if (base.action !== 'deepConnect') throw new Error('拓扑 DeepConnect 命令类型无效。');
      command = {
        ...base,
        deepConnectSource: {
          ...base.deepConnectSource,
          anchors: event.anchors.map((anchor) => ({ ...anchor })),
        },
        expectedPathSHA256: event.expectedPathSHA256,
      };
    } else {
      const selected = vectorSources.selectedComponent;
      if (!selected || selected.key !== event.componentKey || selected.component.featureFID !== event.sourceFID ||
          event.sourceKey !== `vector:${selected.key}` || !vectorMapRequest || !context.projectRevision) {
        throw new DOMException('矢量 DeepConnect 来源组件已变更或过期。', 'AbortError');
      }
      const preview = createPersistedVectorDeepConnectPreview({
        model, topology: current, mapRequestContext, mapCellSize: event.mapCellSize,
        currentMapRequest: vectorMapRequest, currentProjectRevision: context.projectRevision,
        source: selected.source, component: selected.component, anchors: event.anchors,
      });
      command = { ...preview.command, expectedPathSHA256: event.expectedPathSHA256 };
    }
    await runMapCommand(command);
  }, [active, canEdit, context.projectRevision, mapSourceKey, model, ownerContextKey, runMapCommand, vectorMapRequest,
    vectorSources.selectedComponent]);

  const onMoveNode = useCallback((event: GeometryTopologyNodeMove): void => {
    const currentSelection = selectionRef.current?.owner === selectionSnapshotKeyRef.current ? selectionRef.current.value : emptySelection();
    const selectedNodeFIDs = currentSelection.node.includes(event.nodeFID) ? currentSelection.node : [event.nodeFID];
    const command: GeometryTopologyMapCommand = {
      action: 'moveNode', featureKind: 'node', featureIDs: [], nodeFID: event.nodeFID,
      nodeFIDs: selectedNodeFIDs, coordinates: [event.end], mapCellSize: event.mapCellSize,
    };
    void runMapCommand(command).catch(() => undefined);
  }, [owner, runMapCommand]);

  const canvasProps = useMemo<Omit<GeometryTopologyCanvasProps, 'language'>>(() => ({
    sourceKey: mapSourceKey,
    tool: activeTool,
    splitMode,
    currentDraftIdentity: draftIdentity ?? null,
    map: topology?.map ?? null,
    arcPoints,
    previewScopeKey: pathPreviewScopeKey,
    previewAvailable,
    previewState,
    pathPreview,
    deepConnectVectorComponent: vectorSources.selectedComponent,
    selectedFIDByKind: selection,
    selectedMapFeatureIDs: selectionGeometry.featureIDs,
    selectionSnapshotKey,
    selectionFocus: selectionFocus?.snapshotKey === selectionSnapshotKey ? selectionFocus : null,
    canSelect: backend.supports('topology.spatialSelect'),
    canMoveNodes: canEdit && mapCaptureReady,
    canSmartAdd,
    busy: busy || loading || capturing || spatialBusy || moveBusy || arcPointsLoading,
    mapCaptureReady,
    selectionScope,
    selectionMode,
    marqueeHitMode,
    onViewportReady,
    onSpatialSelect,
    onMoveNode,
    onMapCommand: runMapCommand,
    onSmartAdd,
    onDeepConnect,
    onPathPreview,
    onCancelPathPreview: cancelPathPreview,
    onDeepConnectProgress,
  }), [activeTool, arcPoints, arcPointsLoading, backend, canEdit, capturing, draftIdentity, loading, splitMode,
    canSmartAdd, mapCaptureReady, marqueeHitMode, moveBusy, onDeepConnect, onDeepConnectProgress, onMoveNode,
    onSmartAdd, onPathPreview, cancelPathPreview, onViewportReady, onSpatialSelect, mapSourceKey, runMapCommand, selection, selectionMode, selectionScope,
    selectionSnapshotKey, selectionFocus, selectionGeometry.featureIDs, spatialBusy, topology?.map, busy, pathPreviewScopeKey, previewAvailable, previewState, pathPreview,
    vectorSources.selectedComponent]);

  return {
    targets: targets.map(({ key, label }) => ({ key, label })),
    selectedTargetKey: selectedTarget?.key ?? '',
    onTargetChange,
    loading,
    capturing,
    interactionBusy: spatialBusy || moveBusy || arcPointsLoading,
    error: pathPreviewError ? [error, `路径预览：${pathPreviewError}`].filter(Boolean).join('\n') : error,
    selectionScope,
    onSelectionScopeChange: setSelectionScope,
    selectionMode,
    onSelectionModeChange: setSelectionMode,
    marqueeHitMode,
    onMarqueeHitModeChange: setMarqueeHitMode,
    activeTool,
    onActiveToolChange: setActiveTool,
    splitMode,
    onSplitModeChange: setSplitMode,
    selectedArcFIDs,
    arcPoints,
    mapCellSize,
    terrainCapture,
    measuredWindow,
    smartAddOptions,
    canSmartAdd,
    onApplySmartAddOptions,
    deepConnectSourceFID: deepConnectProgress?.sourceKind === 'topology' ? deepConnectProgress.sourceFID : null,
    deepConnectAnchorCount: deepConnectProgress?.anchorCount ?? 0,
    deepConnectSourceKind: deepConnectProgress?.sourceKind ?? null,
    deepConnectSourceLabel: deepConnectProgress?.sourceLabel ?? null,
    onCancelDeepConnect,
    vectorCandidates,
    vectorInspection: vectorSources.inspection,
    vectorSelectedComponent: vectorSources.selectedComponent,
    vectorSelectedComponentKey: vectorSources.selectedComponentKey,
    vectorBusy: vectorSources.loading,
    vectorCanInspect: !!vectorMapRequest && !!vectorMapTree && mapCaptureReady && canEdit && !busy,
    ...(vectorSources.error ? { vectorError: vectorSources.error } : {}),
    onInspectVectorSources,
    onSelectVectorComponent,
    onCancelVectorSource,
    previewAvailable,
    previewScopeKey: pathPreviewScopeKey,
    previewState,
    pathPreview,
    draftReady,
    draftDirty: !!topology?.isDirty,
    selectionSnapshotKey,
    selectionVersion: selectionEntry?.owner === selectionSnapshotKey ? selectionEntry.version : selectionVersionRef.current,
    selectedFeatureCount,
    canZoomToSelection: !!selectionGeometry.extent && !!topologySelectionCamera(selectionGeometry.extent, topology?.map?.extent ?? null, { width: 1, height: 1 }),
    onZoomToSelection,
    onLocateDiagnostic,
    onTableSelectionChange,
    onMapCommand: runMapCommand,
    canvasProps,
  };
}
