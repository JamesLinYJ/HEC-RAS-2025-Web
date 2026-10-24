import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { BackendGateway } from '../../shared/workbench';
import type { CatalogTreeNode, CatalogTreeNodeLocator } from '../map/mapTypes';
import type { MapTreeSnapshot } from '../map/ProjectTreeListener';
import { sameSourcePath } from '../tools/attributes/attributeTableTypes';
import type { GeometryModelRequest } from './geometryTypes';
import {
  createTopologyVectorSourceInspectRequest,
  hasConnectableVectorCapture,
  parseTopologyVectorSourceInspection,
  vectorDeepConnectComponentSelectionKey,
} from './geometryVectorDeepConnect';
import type {
  PersistedVectorDeepConnectComponent,
  PersistedVectorDeepConnectSource,
  TopologyVectorSourceInspection,
  TopologyVectorSourceInspectRequest,
  VectorDeepConnectMapRequest,
} from './geometryVectorDeepConnect';

export interface GeometryVectorComponentOption {
  /** Stable selection identity for the exact file-family/GDB capture and Host component. */
  key: string;
  source: PersistedVectorDeepConnectSource;
  component: PersistedVectorDeepConnectComponent;
}

export interface UseGeometryVectorSourcesOptions {
  /** False while DeepConnect is closed; this aborts the read and drops its capture. */
  active: boolean;
  backend: BackendGateway;
  backendRevision?: number;
  /** Complete request used to load the selected, saved map tree. */
  mapRequest: VectorDeepConnectMapRequest | null;
  tree: MapTreeSnapshot | null;
  projectRevision: string | null;
  geometryModel: GeometryModelRequest | null;
  geometryRevision: string | null;
  topologyDraftState: string | null;
  topologyDraftIdentity: string | null;
  targetGeometryNode: CatalogTreeNodeLocator | null;
  targetGeometryCanonicalPath: string | null;
  targetGeometryRevision: string | null;
  expectedVisualizationPath?: string | null;
}

export interface UseGeometryVectorSourcesResult {
  /** Includes Host diagnostics for inspected GDB and otherwise unsupported layers. */
  inspection: TopologyVectorSourceInspection | null;
  sources: PersistedVectorDeepConnectSource[];
  /** Only exact Host-confirmed, checked, visible SHP/GeoJSON or GDB-line captures. */
  connectableSources: PersistedVectorDeepConnectSource[];
  components: GeometryVectorComponentOption[];
  selectedComponentKey: string | null;
  selectedComponent: GeometryVectorComponentOption | null;
  onSelectedComponentChange: (key: string | null) => void;
  /** Bounds calculated from the selected Host component's project-space points. */
  extent: { minX: number; minY: number; maxX: number; maxY: number } | null;
  /** Composite owner for the geometry draft/target and selected saved map. */
  ownerKey: string | null;
  /** Host-validated map-source inspection owner. */
  inspectionOwnerKey: string | null;
  loading: boolean;
  error?: string;
  reload: () => void;
}

interface InspectionEntry {
  scopeKey: string;
  inspection: TopologyVectorSourceInspection;
}

interface SelectionEntry {
  scopeKey: string;
  stableKey: string;
}

interface ErrorEntry { scopeKey: string; message: string }

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function stableJSON(value: unknown): string {
  if (Array.isArray(value)) return `[${value.map(stableJSON).join(',')}]`;
  if (isRecord(value)) {
    return `{${Object.keys(value).sort().map((key) => `${JSON.stringify(key)}:${stableJSON(value[key])}`).join(',')}}`;
  }
  return JSON.stringify(value) ?? 'null';
}

function isSHA256(value: unknown): value is string {
  return typeof value === 'string' && /^[0-9a-f]{64}$/i.test(value);
}

function locatorKey(locator: CatalogTreeNodeLocator): string {
  return stableJSON(locator);
}

function checkedVisibleLocators(roots: CatalogTreeNode[], hiddenLayerIDs: string[]): CatalogTreeNodeLocator[] {
  const hidden = new Set(hiddenLayerIDs);
  const result: CatalogTreeNodeLocator[] = [];
  const seen = new Set<string>();

  const visit = (node: CatalogTreeNode, ancestorsChecked: boolean, ancestorsHidden: boolean): void => {
    const visibleByTree = ancestorsChecked && node.checked !== 'unchecked';
    const hiddenByTree = ancestorsHidden || hidden.has(node.id);
    const locator = node.locator;
    if (visibleByTree && !hiddenByTree && node.checked === 'checked' && locator &&
        ((locator.nodeType === 'MapLayer' &&
          (locator.mapType === 'PolylinesVector' || locator.mapType === 'PolygonsVector')) ||
          locator.nodeType === 'GdbVectorLayer' || locator.mapType === 'GdbVectorLayer')) {
      const key = locatorKey(locator);
      if (!seen.has(key)) {
        seen.add(key);
        result.push(locator);
      }
    }
    for (const child of node.children ?? []) visit(child, visibleByTree, hiddenByTree);
  };

  roots.forEach((root) => visit(root, true, false));
  return result;
}

function selectedTargetExists(roots: CatalogTreeNode[], target: CatalogTreeNodeLocator): boolean {
  let found = false;
  const visit = (node: CatalogTreeNode): void => {
    if (node.locator && locatorKey(node.locator) === locatorKey(target)) found = true;
    if (!found) (node.children ?? []).forEach(visit);
  };
  roots.forEach(visit);
  return found;
}

function extentOf(component: PersistedVectorDeepConnectComponent | null):
  { minX: number; minY: number; maxX: number; maxY: number } | null {
  if (!component || component.points.length === 0) return null;
  const first = component.points[0];
  if (!first || !Number.isFinite(first.x) || !Number.isFinite(first.y)) return null;
  let minX = first.x;
  let maxX = first.x;
  let minY = first.y;
  let maxY = first.y;
  for (const point of component.points.slice(1)) {
    if (!Number.isFinite(point.x) || !Number.isFinite(point.y)) return null;
    minX = Math.min(minX, point.x);
    maxX = Math.max(maxX, point.x);
    minY = Math.min(minY, point.y);
    maxY = Math.max(maxY, point.y);
  }
  return { minX, minY, maxX, maxY };
}

function requestID(): string {
  if (!globalThis.crypto?.randomUUID) throw new Error('当前浏览器无法生成矢量来源检查请求标识。');
  return globalThis.crypto.randomUUID();
}

function currentScopeKey(options: UseGeometryVectorSourcesOptions): string | null {
  const {
    active, mapRequest, tree, projectRevision, geometryModel, geometryRevision,
    topologyDraftState, topologyDraftIdentity, targetGeometryNode,
    targetGeometryCanonicalPath, targetGeometryRevision, expectedVisualizationPath,
  } = options;
  if (!active || !mapRequest || !tree || !projectRevision || !geometryModel ||
      !geometryRevision || !topologyDraftState || !topologyDraftIdentity ||
      !targetGeometryNode || !targetGeometryCanonicalPath || !targetGeometryRevision) return null;
  return stableJSON({
    projectPath: mapRequest.projectPath,
    projectXml: mapRequest.projectXml,
    projectXmlSHA: mapRequest.projectXmlSHA,
    projectRevision,
    projectSessionID: mapRequest.projectSessionID,
    coreLayerContext: mapRequest.coreLayerContext ?? null,
    map: mapRequest,
    treeVisualizationRevision: tree.visualizationRevision,
    treeRoots: tree.roots,
    hiddenLayerIDs: tree.hiddenLayerIDs,
    geometryModel,
    geometryRevision,
    topologyDraftState,
    topologyDraftIdentity,
    targetGeometryNode,
    targetGeometryCanonicalPath,
    targetGeometryRevision,
    expectedVisualizationPath: expectedVisualizationPath ?? null,
  });
}

export function useGeometryVectorSources(options: UseGeometryVectorSourcesOptions): UseGeometryVectorSourcesResult {
  const scopeKey = currentScopeKey(options);
  const optionsRef = useRef(options);
  optionsRef.current = options;
  const [inspectionEntry, setInspectionEntry] = useState<InspectionEntry | null>(null);
  const [selectionEntry, setSelectionEntry] = useState<SelectionEntry | null>(null);
  const [loadingScopeKey, setLoadingScopeKey] = useState<string | null>(null);
  const [errorEntry, setErrorEntry] = useState<ErrorEntry | null>(null);
  const [reloadGeneration, setReloadGeneration] = useState(0);
  const generationRef = useRef(0);
  const controllerRef = useRef<AbortController | null>(null);

  const reload = useCallback(() => setReloadGeneration((value) => value + 1), []);

  useEffect(() => {
    const generation = ++generationRef.current;
    const currentOptions = optionsRef.current;
    controllerRef.current?.abort();
    controllerRef.current = null;

    if (!currentOptions.active || !scopeKey) {
      setInspectionEntry(null);
      setSelectionEntry(null);
      setLoadingScopeKey(null);
      setErrorEntry(null);
      return () => { if (generationRef.current === generation) generationRef.current += 1; };
    }

    const controller = new AbortController();
    controllerRef.current = controller;
    const isCurrent = (): boolean => generationRef.current === generation &&
      controllerRef.current === controller && !controller.signal.aborted;
    setInspectionEntry(null);
    setLoadingScopeKey(scopeKey);
    setErrorEntry(null);
    setSelectionEntry((current) => current?.scopeKey === scopeKey ? current : null);

    const {
      backend, mapRequest, tree, projectRevision, geometryModel, geometryRevision,
      topologyDraftState, topologyDraftIdentity, targetGeometryNode,
      targetGeometryCanonicalPath, targetGeometryRevision, expectedVisualizationPath,
    } = currentOptions;

    void (async () => {
      try {
        if (!mapRequest || !tree || !projectRevision || !geometryModel || !geometryRevision ||
            !topologyDraftState || !topologyDraftIdentity || !targetGeometryNode ||
            !targetGeometryCanonicalPath || !targetGeometryRevision) return;
        if (!isSHA256(projectRevision) || !isSHA256(mapRequest.projectXmlSHA) ||
            !isSHA256(mapRequest.expectedVisualizationRevision) ||
            tree.visualizationRevision.toUpperCase() !== mapRequest.expectedVisualizationRevision.toUpperCase()) {
          throw new Error('当前保存地图、工程 XML 或 .vis 修订不完整，请刷新地图后重试。');
        }
        if (geometryModel.projectPath === null || !sameSourcePath(geometryModel.projectPath, mapRequest.projectPath) ||
            !sameSourcePath(geometryModel.assetPath, targetGeometryCanonicalPath) ||
            !sameSourcePath(geometryModel.assetPath, targetGeometryNode.assetID ?? targetGeometryNode.filename ?? '') ||
            !selectedTargetExists(tree.roots, targetGeometryNode)) {
          throw new Error('所选 Geometry 与当前保存地图的来源定位不匹配。');
        }
        if (!isSHA256(geometryRevision) || !isSHA256(targetGeometryRevision) ||
            geometryRevision.toUpperCase() !== targetGeometryRevision.toUpperCase() ||
            !/^[0-9a-f]{64}$/i.test(topologyDraftIdentity)) {
          throw new Error('当前 Geometry 修订或拓扑草稿身份无效。');
        }
        if (!tree.roots || !Array.isArray(tree.roots) || !Array.isArray(tree.hiddenLayerIDs)) {
          throw new Error('当前地图树快照不完整。');
        }
        if (!backend.supports('topology.vectorSource.inspect')) {
          throw new Error('当前 C# 服务尚未提供持久化矢量来源读取能力。');
        }

        const nodes = checkedVisibleLocators(tree.roots, tree.hiddenLayerIDs);
        if (nodes.length === 0) {
          if (!isCurrent()) return;
          setInspectionEntry(null);
          setErrorEntry({ scopeKey, message: '当前地图没有已勾选且可见的折线或多边形来源。' });
          return;
        }

        const request: TopologyVectorSourceInspectRequest = createTopologyVectorSourceInspectRequest({
          requestID: requestID(),
          map: mapRequest,
          sources: nodes.map((node) => ({ node, expectedFamily: null, expectedGdbSource: null })),
        });
        const raw = await backend.request<unknown>('topology.vectorSource.inspect', request, { signal: controller.signal });
        if (!isCurrent()) return;
        const inspection = parseTopologyVectorSourceInspection(raw, {
          request,
          projectRevision,
          ...(expectedVisualizationPath ? { expectedVisualizationPath } : {}),
        });
        if (!isCurrent()) return;
        setInspectionEntry({ scopeKey, inspection });
      } catch (caught) {
        if (isCurrent()) {
          const message = caught instanceof Error ? caught.message : String(caught);
          setInspectionEntry(null);
          setErrorEntry({ scopeKey, message: `读取地图矢量来源失败：${message}` });
        }
      } finally {
        if (isCurrent()) {
          setLoadingScopeKey((current) => current === scopeKey ? null : current);
          controllerRef.current = null;
        }
      }
    })();

    return () => {
      controller.abort();
      if (controllerRef.current === controller) controllerRef.current = null;
      if (generationRef.current === generation) generationRef.current += 1;
    };
  }, [options.active, options.backend, options.backendRevision, scopeKey, reloadGeneration]);

  const inspection = scopeKey && inspectionEntry?.scopeKey === scopeKey ? inspectionEntry.inspection : null;
  const sources = inspection?.sources ?? [];
  const connectableSources = useMemo(() => sources.filter((source) => source.checked && source.effectiveVisible &&
    hasConnectableVectorCapture(source) &&
    typeof source.canonicalSourcePath === 'string' && source.canonicalSourcePath.length > 0), [sources]);
  const components = useMemo<GeometryVectorComponentOption[]>(() => connectableSources.flatMap((source) =>
    source.components.map((component) => ({ key: vectorDeepConnectComponentSelectionKey(source, component), source, component }))),
  [connectableSources]);
  const selection = scopeKey && selectionEntry?.scopeKey === scopeKey ? selectionEntry : null;
  const selectedComponent = selection ? components.find((item) => item.key === selection.stableKey) ?? null : null;
  const selectedComponentKey = selectedComponent?.key ?? null;
  const extent = useMemo(() => extentOf(selectedComponent?.component ?? null), [selectedComponent]);
  const error = scopeKey && errorEntry?.scopeKey === scopeKey ? errorEntry.message : undefined;
  const loading = !!scopeKey && loadingScopeKey === scopeKey;

  const onSelectedComponentChange = useCallback((key: string | null) => {
    if (!scopeKey || !key) {
      setSelectionEntry(null);
      return;
    }
    const option = components.find((item) => item.key === key);
    if (!option) return;
    setSelectionEntry({ scopeKey, stableKey: key });
  }, [components, scopeKey]);

  const ownerKey = scopeKey && inspection ? scopeKey : null;
  const inspectionOwnerKey = inspection?.owner.ownerKey ?? null;

  return {
    inspection,
    sources,
    connectableSources,
    components,
    selectedComponentKey,
    selectedComponent,
    onSelectedComponentChange,
    extent,
    ownerKey,
    inspectionOwnerKey,
    loading,
    error,
    reload,
  };
}
