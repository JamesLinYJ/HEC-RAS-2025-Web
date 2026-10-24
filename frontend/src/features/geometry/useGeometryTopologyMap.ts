import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { BackendGateway, WorkbenchContext } from '../../shared/workbench';
import type { CatalogTreeNode, CatalogTreeNodeLocator, MapDocumentTab } from '../map/mapTypes';
import { loadMapDocumentCatalog, projectXmlSha } from '../map/ProjectTreeListener';
import { sameSourcePath } from '../tools/attributes/attributeTableTypes';
import type {
  GeometryModelRequest, GeometryRow, GeometryTopologyArcPointsSnapshot, GeometryTopologyMapCommand,
  GeometryTopologyMapRequestContext, GeometryTopologyMapTool,
  GeometryTopologySelectionMode, GeometryTopologySelectionScope, GeometryTopologyMarqueeHitMode,
  GeometryTopologySnapshot, GeometryTopologySpatialSelectionResult,
} from './geometryTypes';
import type {
  GeometryTopologyCanvasProps, GeometryTopologyNodeMove, GeometryTopologySpatialSelection,
  GeometryTopologyViewportCapture,
} from './GeometryTopologyCanvas';

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
}

interface GeometryMapSelection {
  node: number[];
  arc: number[];
  region: number[];
}

interface OwnerValue<T> { owner: string; value: T }
interface ArcPointsCapture {
  owner: string;
  arcFID: number;
  revision: string;
  draftState: string;
  draftIdentity: string;
  snapshot: GeometryTopologyArcPointsSnapshot;
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
  draftReady: boolean;
  draftDirty: boolean;
  onTableSelectionChange: (kind: 'node' | 'arc', rows: GeometryRow[]) => void;
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
  return { node: [], arc: [], region: [] };
}

export function useGeometryTopologyMap({ active, owner, backend, backendRevision, context, model, topology,
  canEdit, busy, onLoadArcPoints, onMapCommand: editMapCommand }: UseGeometryTopologyMapOptions): GeometryTopologyMapWorkspace {
  const [targetsEntry, setTargetsEntry] = useState<OwnerValue<MapTarget[]> | null>(null);
  const [selectedTargetKey, setSelectedTargetKey] = useState('');
  const [captureEntry, setCaptureEntry] = useState<OwnerValue<MapGeometryCapture> | null>(null);
  const [viewportCapture, setViewportCapture] = useState<GeometryTopologyViewportCapture | null>(null);
  const [selectionEntry, setSelectionEntry] = useState<OwnerValue<GeometryMapSelection> | null>(null);
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
  const [mapCellSize, setMapCellSize] = useState<number | null>(null);
  const catalogGenerationRef = useRef(0);
  const mapCaptureGenerationRef = useRef(0);
  const spatialGenerationRef = useRef(0);
  const spatialControllerRef = useRef<AbortController | null>(null);
  const moveGenerationRef = useRef(0);
  const mapCommandInFlightRef = useRef(false);
  const arcPointsGenerationRef = useRef(0);
  const onLoadArcPointsRef = useRef(onLoadArcPoints); onLoadArcPointsRef.current = onLoadArcPoints;
  const topologyRef = useRef(topology); topologyRef.current = topology;
  const captureRef = useRef(captureEntry); captureRef.current = captureEntry;
  const targetKeyRef = useRef(selectedTargetKey); targetKeyRef.current = selectedTargetKey;
  const selectionRef = useRef(selectionEntry); selectionRef.current = selectionEntry;
  const ownerRef = useRef(owner); ownerRef.current = owner;

  useEffect(() => {
    if (!topology) return;
    const current = selectionRef.current;
    if (current?.owner !== owner) return;
    const available = {
      node: new Set(topology.nodes.rows.flatMap((row) => Number.isInteger(row.fid) && row.fid! >= 0 ? [row.fid!] : [])),
      arc: new Set(topology.arcs.rows.flatMap((row) => Number.isInteger(row.fid) && row.fid! >= 0 ? [row.fid!] : [])),
      region: new Set(topology.regions.rows.flatMap((row) => Number.isInteger(row.fid) && row.fid! >= 0 ? [row.fid!] : [])),
    };
    const value = {
      node: current.value.node.filter((fid) => available.node.has(fid)),
      arc: current.value.arc.filter((fid) => available.arc.has(fid)),
      region: current.value.region.filter((fid) => available.region.has(fid)),
    };
    const unchanged = (kind: keyof GeometryMapSelection): boolean => value[kind].length === current.value[kind].length &&
      value[kind].every((fid, index) => fid === current.value[kind][index]);
    if (unchanged('node') && unchanged('arc') && unchanged('region')) return;
    const entry = { owner, value };
    selectionRef.current = entry;
    setSelectionEntry(entry);
  }, [owner, topology?.nodes.rows, topology?.arcs.rows, topology?.regions.rows]);

  const ownerContextKey = JSON.stringify([owner, context.projectRevision, context.projectXml,
    context.coreLayerContext ?? null, context.projectSessionID, context.projectPath]);
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
  const capture = captureEntry?.owner === ownerContextKey && captureEntry.value.targetKey === selectedTarget?.key
    ? captureEntry.value : undefined;
  const selection = selectionEntry?.owner === owner ? selectionEntry.value : emptySelection();
  const draftIdentity = topology?.completion?.draftIdentity;
  const mapCaptureReady = !!active && !!capture && !!topology && !!draftIdentity &&
    topology.revision === capture.geometryRevision && sameRevision(topology.completion?.sourceRevision, topology.revision);
  const selectedArcFIDs = selection.arc;
  const selectedArcFID = selectedArcFIDs.length === 1 ? selectedArcFIDs[0] : null;
  const arcPoints = arcPointsEntry && topology && draftIdentity &&
    arcPointsEntry.owner === owner && arcPointsEntry.arcFID === selectedArcFID &&
    arcPointsEntry.revision === topology.revision && arcPointsEntry.draftState === topology.draftState &&
    arcPointsEntry.draftIdentity === draftIdentity
      ? arcPointsEntry.snapshot : null;
  const draftReady = mapCaptureReady && canEdit && !busy && !loading && !capturing && !spatialBusy &&
    !moveBusy && !arcPointsLoading && mapCellSize != null && Number.isFinite(mapCellSize) && mapCellSize > 0;

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
            latestSelection?.owner !== expected.owner || latestSelection.value.arc.length !== 1 ||
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
  }, [active, draftIdentity, owner, selectedArcFID, topology?.draftState, topology?.revision]);

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
    setMapCellSize(null);
    setArcPointsEntry(null);
    setArcPointsLoading(false);
    setActiveTool('select');
    setSelectedTargetKey('');
    setSelectionEntry((current) => current?.owner === owner ? current : { owner, value: emptySelection() });
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
    if (!active || !selectedTarget || !model || !context.projectPath || !viewportCapture || !topology || !draftIdentity) return;
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
    selectedTarget, topology, viewportCapture]);

  const onViewportReady = useCallback((next: GeometryTopologyViewportCapture): void => {
    if (!Number.isFinite(next.mapCellSize) || next.mapCellSize <= 0) return;
    setMapCellSize((current) => current === next.mapCellSize ? current : next.mapCellSize);
    if (captureRef.current?.owner === ownerContextKey && captureRef.current.value.targetKey === targetKeyRef.current) return;
    setViewportCapture(next);
  }, [ownerContextKey]);

  const onTableSelectionChange = useCallback((kind: 'node' | 'arc', rows: GeometryRow[]): void => {
    const fids = [...new Set(rows.flatMap((row) => Number.isInteger(row.fid) && row.fid! >= 0 ? [row.fid!] : []))];
    const current = selectionRef.current?.owner === owner ? selectionRef.current.value : emptySelection();
    const next = { ...current, [kind]: fids };
    selectionRef.current = { owner, value: next };
    setSelectionEntry({ owner, value: next });
  }, [owner]);

  const onSpatialSelect = useCallback((event: GeometryTopologySpatialSelection): void => {
    if (!active || !mapCaptureReady || !model || !topology || !capture || !draftIdentity ||
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
        if (generation !== spatialGenerationRef.current || ownerRef.current !== owner ||
            targetKeyRef.current !== selectedTargetKeyAtStart || latest?.revision !== expected.revision ||
            latest.draftState !== expected.draftState || latest.completion?.draftIdentity !== expected.draftIdentity ||
            currentCapture?.owner !== ownerContextKey || currentCapture.value.targetKey !== selectedTargetKeyAtStart ||
            result.requestID !== request.requestID || result.mapID !== mapOwner.mapID ||
            result.contextToken !== mapOwner.contextToken || result.geometryRevision !== mapOwner.geometryRevision ||
            result.revision !== expected.revision || result.draftIdentity !== expected.draftIdentity) return;
        const entry = { owner, value: selectionFromResult(result) };
        selectionRef.current = entry;
        setSelectionEntry(entry);
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
    selection, selectionMode, selectionScope, selectedTarget?.key, topology]);

  const runMapCommand = useCallback(async (command: GeometryTopologyMapCommand): Promise<void> => {
    const current = topologyRef.current;
    const currentCapture = captureRef.current?.owner === ownerContextKey &&
      captureRef.current.value.targetKey === targetKeyRef.current ? captureRef.current.value : undefined;
    const currentDraftIdentity = current?.completion?.draftIdentity;
    const currentSelection = selectionRef.current?.owner === owner ? selectionRef.current.value : emptySelection();
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
      await editMapCommand(expected, mapOwner, command);
    } catch (caught) {
      if (generation === moveGenerationRef.current && !(caught instanceof DOMException && caught.name === 'AbortError')) {
        setError(caught instanceof Error ? caught.message : String(caught));
      }
      throw caught;
    } finally {
      mapCommandInFlightRef.current = false;
      if (generation === moveGenerationRef.current) setMoveBusy(false);
    }
  }, [active, arcPoints, arcPointsEntry, busy, canEdit, editMapCommand, moveBusy, owner, ownerContextKey, spatialBusy]);

  const onMoveNode = useCallback((event: GeometryTopologyNodeMove): void => {
    const currentSelection = selectionRef.current?.owner === owner ? selectionRef.current.value : emptySelection();
    const selectedNodeFIDs = currentSelection.node.includes(event.nodeFID) ? currentSelection.node : [event.nodeFID];
    const command: GeometryTopologyMapCommand = {
      action: 'moveNode', featureKind: 'node', featureIDs: [], nodeFID: event.nodeFID,
      nodeFIDs: selectedNodeFIDs, coordinates: [event.end], mapCellSize: event.mapCellSize,
    };
    void runMapCommand(command).catch(() => undefined);
  }, [owner, runMapCommand]);

  const canvasProps = useMemo<Omit<GeometryTopologyCanvasProps, 'language'>>(() => ({
    sourceKey: JSON.stringify([owner, selectedTarget?.key ?? null]),
    tool: activeTool,
    splitMode,
    currentDraftIdentity: draftIdentity ?? null,
    map: topology?.map ?? null,
    arcPoints,
    selectedFIDByKind: selection,
    canSelect: backend.supports('topology.spatialSelect'),
    canMoveNodes: canEdit && mapCaptureReady,
    busy: busy || loading || capturing || spatialBusy || moveBusy || arcPointsLoading,
    mapCaptureReady,
    selectionScope,
    selectionMode,
    marqueeHitMode,
    onViewportReady,
    onSpatialSelect,
    onMoveNode,
    onMapCommand: runMapCommand,
  }), [activeTool, arcPoints, arcPointsLoading, backend, canEdit, capturing, draftIdentity, loading, splitMode,
    mapCaptureReady, marqueeHitMode, moveBusy, onMoveNode, onViewportReady, onSpatialSelect, owner, runMapCommand,
    selection, selectionMode, selectionScope, selectedTarget?.key, spatialBusy, topology?.map, busy]);

  return {
    targets: targets.map(({ key, label }) => ({ key, label })),
    selectedTargetKey: selectedTarget?.key ?? '',
    onTargetChange: setSelectedTargetKey,
    loading,
    capturing,
    interactionBusy: spatialBusy || moveBusy || arcPointsLoading,
    error,
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
    draftReady,
    draftDirty: !!topology?.isDirty,
    onTableSelectionChange,
    onMapCommand: runMapCommand,
    canvasProps,
  };
}
