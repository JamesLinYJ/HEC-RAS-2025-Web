import { useEffect, useRef, useState } from 'react';
import type { BackendGateway } from '../../../shared/workbench';
import type { CatalogTreeNode, CatalogTreeNodeLocator } from '../mapTypes';
import type {
  MapResultAnalysisCapture,
  MapResultMeshSourceCapture,
} from './mapAnalysisTypes';

const EMPTY_SHA256 = 'E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855';

export interface MapResultMeshHoverCaptureState {
  capture: MapResultMeshSourceCapture | null;
  loading: boolean;
  error: string | null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function stable(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(stable);
  if (!isRecord(value)) return value;
  return Object.fromEntries(Object.keys(value).sort().map(key => [key, stable(value[key])]));
}

function locatorKey(value: CatalogTreeNodeLocator): string {
  return JSON.stringify(stable(value));
}

function sameLocator(left: CatalogTreeNodeLocator, right: CatalogTreeNodeLocator): boolean {
  return locatorKey(left) === locatorKey(right);
}

function findNode(
  nodes: CatalogTreeNode[],
  locator: CatalogTreeNodeLocator,
): CatalogTreeNode | null {
  for (const node of nodes) {
    if (node.locator && sameLocator(node.locator, locator)) return node;
    const child = node.children?.length ? findNode(node.children, locator) : null;
    if (child) return child;
  }
  return null;
}

function checkedTreeNode(nodes: CatalogTreeNode[], locator: CatalogTreeNodeLocator): CatalogTreeNode | null {
  for (const node of nodes) {
    if (node.locator && sameLocator(node.locator, locator)) {
      return node.checked === 'checked' ? node : null;
    }
    if (node.checked === 'unchecked') continue;
    const child = node.children?.length ? checkedTreeNode(node.children, locator) : null;
    if (child) return child;
  }
  return null;
}

function requiredString(value: unknown, label: string): string {
  if (typeof value !== 'string' || !value.trim()) throw new Error(`${label}缺失。`);
  return value;
}

function requireLocator(value: unknown, label: string): CatalogTreeNodeLocator {
  if (!isRecord(value) || typeof value.nodeType !== 'string' || !Array.isArray(value.nodePath)) {
    throw new Error(`${label}无效。`);
  }
  return value as unknown as CatalogTreeNodeLocator;
}

function sameRevision(left: string, right: string): boolean {
  return left.toUpperCase() === right.toUpperCase();
}

function parseGeometryResponse(
  value: unknown,
  request: MapResultAnalysisCapture,
  resultPath: string,
  resultRevision: string,
  geometryNode: CatalogTreeNodeLocator,
): MapResultMeshSourceCapture {
  if (!isRecord(value) || !isRecord(value.geometry)) {
    throw new Error('Host 没有返回 Result 几何及 Flow Area 图层身份。');
  }
  const geometry = value.geometry;
  const flowAreasState = geometry.flowAreasState;
  if (!isRecord(flowAreasState)) {
    throw new Error('Host 没有返回 Result 几何及 Flow Area 图层身份。');
  }
  const returnedResultNode = requireLocator(value.resultNode, 'Result 节点');
  const returnedGeometryNode = requireLocator(geometry.geometryNode, 'Geometry 子节点');
  const flowAreaNode = requireLocator(flowAreasState.locator, 'Flow Area 图层节点');
  const geometryPath = requiredString(geometry.geometryCanonicalPath, 'Geometry 来源路径');
  const geometryRevision = requiredString(geometry.geometryRevision, 'Geometry 来源修订');
  const geometryContextToken = requiredString(geometry.contextToken, 'Geometry 页面令牌');
  const contextToken = requiredString(value.contextToken, '地图 Result 令牌');
  const selectedResultPath = requiredString(value.selectedResultPath, '当前 Result 路径');
  const selectedResultRevision = requiredString(value.selectedResultRevision, '当前 Result 修订');

  if (value.selectionIndex !== 0 || !sameLocator(returnedResultNode, request.activeNode.node) ||
      !sameLocator(returnedGeometryNode, geometryNode) || flowAreaNode.nodeType !== 'FlowAreaLayer' ||
      contextToken !== request.contextToken || selectedResultPath !== resultPath ||
      !sameRevision(selectedResultRevision, resultRevision) ||
      !sameRevision(geometryRevision, resultRevision) ||
      geometryPath !== resultPath || flowAreasState.effectiveVisible !== true ||
      value.projectCanonicalPath !== request.map.projectPath ||
      value.projectRevision !== request.projectRevision ||
      !sameRevision(String(value.projectXmlSHA ?? ''), request.projectXmlSHA) ||
      value.visualizationRevision !== request.visualizationRevision ||
      value.mapID !== request.map.mapID || value.mapIndex !== request.map.mapIndex ||
      value.mapName !== request.map.mapName || value.mapType !== request.map.mapType ||
      geometry.projectCanonicalPath !== request.map.projectPath ||
      geometry.projectRevision !== request.projectRevision ||
      !sameRevision(String(geometry.projectXmlSHA ?? ''), request.projectXmlSHA) ||
      geometry.visualizationRevision !== request.visualizationRevision ||
      geometry.mapID !== request.map.mapID || geometry.mapIndex !== request.map.mapIndex ||
      geometry.mapName !== request.map.mapName || geometry.mapType !== request.map.mapType) {
    throw new Error('Result 或 Flow Area 图层身份已变化，已丢弃该 Mesh 捕获。');
  }

  const resultOwner = {
    resultNode: request.activeNode.node,
    contextToken: request.contextToken,
    selectionOverrides: request.selectionOverrides,
    selectionIndex: 0 as const,
    selectedResultPath: resultPath,
    selectedResultRevision: resultRevision,
  };
  const sourceIdentity = JSON.stringify([geometryPath, geometryRevision.toUpperCase()]);
  const mesh = {
    node: flowAreaNode,
    geometryNode,
    sourceIdentity,
    geometryContextToken,
    sourcePath: geometryPath,
    sourceRevision: geometryRevision,
    planNodes: null,
    resultOwner,
    model: null,
    ownerMode: 'resultSource' as const,
    meshDraftState: '' as const,
    meshDraftSHA256: EMPTY_SHA256,
    topologyDraftState: null,
    topologyDraftSHA256: EMPTY_SHA256,
    flowAreaVisibilityOverride: false as const,
  };
  const captureKey = JSON.stringify([
    request.captureKey,
    locatorKey(geometryNode),
    locatorKey(flowAreaNode),
    geometryPath,
    geometryRevision.toUpperCase(),
    geometryContextToken,
  ]);
  return {
    ownerKey: request.ownerKey,
    captureKey,
    resultOwner,
    geometryNode,
    flowAreaNode,
    geometryContextToken,
    geometryPath,
    geometryRevision,
    hoverLayer: { node: flowAreaNode, kind: 'mesh', mesh },
  };
}

/** Loads exact Result-owned Geometry metadata before exposing a mesh hover capture. */
export function useMapResultMeshHoverCapture(
  backend: BackendGateway,
  request: MapResultAnalysisCapture | null,
): MapResultMeshHoverCaptureState {
  const [state, setState] = useState<MapResultMeshHoverCaptureState>({ capture: null, loading: false, error: null });
  const generationRef = useRef(0);
  const requestRef = useRef(request);
  requestRef.current = request;
  const requestKey = request ? JSON.stringify([
    request.ownerKey,
    request.contextToken,
    locatorKey(request.activeNode.node),
    request.slotZero?.selectedResult?.resultPath ?? null,
    request.slotZero?.selectedResult?.revision.toUpperCase() ?? null,
    request.selectionOverrides,
    request.treeRoots.flatMap(root => {
      const result = findNode([root], request.activeNode.node);
      return result?.children?.filter(child => child.locator?.nodeType === 'Geometry')
        .map(child => [child.locator, child.checked]) ?? [];
    }),
  ]) : null;

  useEffect(() => {
    const generation = ++generationRef.current;
    const controller = new AbortController();
    setState({ capture: null, loading: false, error: null });
    const exactRequest = requestRef.current;

    if (!exactRequest || !backend.supports('map.result.geometry.load')) {
      return () => {
        controller.abort();
        if (generationRef.current === generation) generationRef.current++;
      };
    }
    const slotZero = exactRequest.slotZero;
    const selectedResult = slotZero?.selectionIndex === 0 ? slotZero.selectedResult : null;
    if (!selectedResult || selectedResult.isComputing ||
        !['Result', 'MultiResult'].includes(exactRequest.activeNode.node.nodeType)) {
      return () => {
        controller.abort();
        if (generationRef.current === generation) generationRef.current++;
      };
    }
      const resultRow = findNode(exactRequest.treeRoots, exactRequest.activeNode.node);
    const geometryRows = resultRow?.children?.filter(child => child.locator?.nodeType === 'Geometry') ?? [];
    if (geometryRows.length !== 1 || !geometryRows[0].locator ||
        !checkedTreeNode(exactRequest.treeRoots, exactRequest.activeNode.node)) {
      return () => {
        controller.abort();
        if (generationRef.current === generation) generationRef.current++;
      };
    }
    const geometryNode = geometryRows[0].locator;
    const resultOwner = {
      resultNode: exactRequest.activeNode.node,
      contextToken: exactRequest.contextToken,
      selectionOverrides: exactRequest.selectionOverrides,
      selectionIndex: 0 as const,
      selectedResultPath: selectedResult.resultPath,
      selectedResultRevision: selectedResult.revision,
    };

    setState({ capture: null, loading: true, error: null });
    void (async () => {
      try {
        const response = await backend.request<unknown>('map.result.geometry.load', {
          map: exactRequest.map,
          resultOwner,
          geometryNode,
          pageSize: 1,
          skipTopologyPage: true,
          skipFlowAreasPage: true,
          skipCulvertsPage: true,
        }, { signal: controller.signal });
        if (controller.signal.aborted || generationRef.current !== generation) return;
        const capture = parseGeometryResponse(response, exactRequest, selectedResult.resultPath,
          selectedResult.revision, geometryNode);
        if (controller.signal.aborted || generationRef.current !== generation) return;
        setState({ capture, loading: false, error: null });
      } catch (error) {
        if (controller.signal.aborted || generationRef.current !== generation) return;
        setState({ capture: null, loading: false, error: error instanceof Error ? error.message : String(error) });
      }
    })();

    return () => {
      controller.abort();
      if (generationRef.current === generation) generationRef.current++;
    };
  }, [backend, requestKey]);

  return state;
}
