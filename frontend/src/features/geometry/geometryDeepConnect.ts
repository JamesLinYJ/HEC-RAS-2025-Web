import type {
  GeometryModelRequest,
  GeometryTopologyDeepConnectAnchor,
  GeometryTopologyDeepConnectSource,
  GeometryTopologyMapCommand,
  GeometryTopologyMapRequestContext,
  GeometryTopologySnapshot,
} from './geometryTypes';

export interface CreateTopologyDeepConnectCommandInput {
  model: GeometryModelRequest;
  topology: GeometryTopologySnapshot;
  mapRequestContext: GeometryTopologyMapRequestContext;
  mapCellSize: number;
  arcFID: number;
  startAnchor: GeometryTopologyDeepConnectAnchor;
  endAnchor: GeometryTopologyDeepConnectAnchor;
}

function isSHA256(value: unknown): value is string {
  return typeof value === 'string' && /^[0-9a-f]{64}$/i.test(value);
}

function isDraftIdentity(value: unknown): value is string {
  return typeof value === 'string' && /^[0-9a-f]{64}$/.test(value);
}

function isRequestID(value: unknown): value is string {
  // The Host accepts Guid D and N formats. Geometry workbench fallback IDs
  // use the compact 32-hex N format when randomUUID is unavailable.
  return typeof value === 'string' &&
    (/^[0-9a-f]{32}$/i.test(value) ||
      /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value));
}

function fail(reason: string): never {
  throw new Error(`无法连接当前几何弧段：${reason}`);
}

function captureArcPath(topology: GeometryTopologySnapshot, arcFID: number) {
  if (!Number.isSafeInteger(arcFID) || arcFID < 0) fail('弧段 FID 无效。');
  const matchingRows = topology.arcs.rows.filter((row) => row.fid === arcFID);
  if (matchingRows.length !== 1) fail('当前拓扑表中找不到唯一的源弧段 FID。');

  const matchingFeatures = topology.map?.features.filter((feature) =>
    feature.featureKind === 'arc' && feature.fid === arcFID) ?? [];
  if (matchingFeatures.length !== 1) fail('当前地图快照中找不到唯一的源弧段 FID。');
  const feature = matchingFeatures[0];
  if (!feature) fail('当前地图快照中找不到唯一的源弧段 FID。');
  const sourcePath = feature.paths[0];
  if (feature.geometryType !== 'line' || feature.paths.length !== 1 || !sourcePath || sourcePath.length < 2) {
    fail('源弧段没有唯一且完整的折线路径。');
  }

  const path = sourcePath.map((point) => ({ x: point.x, y: point.y }));
  if (path.some((point) => !Number.isFinite(point.x) || !Number.isFinite(point.y))) {
    fail('源弧段包含无效坐标。');
  }
  return path;
}

function anchorPoint(path: ReadonlyArray<{ x: number; y: number }>, anchor: GeometryTopologyDeepConnectAnchor,
  label: string): { x: number; y: number } {
  if (!Number.isInteger(anchor.segmentIndex) || anchor.segmentIndex < 0 ||
      anchor.segmentIndex >= path.length - 1 || !Number.isFinite(anchor.fraction) ||
      anchor.fraction < 0 || anchor.fraction > 1) {
    fail(`${label}锚点不再属于当前源弧段。`);
  }
  const first = path[anchor.segmentIndex];
  const second = path[anchor.segmentIndex + 1];
  if (!first || !second) fail(`${label}锚点不再属于当前源弧段。`);
  if (first.x === second.x && first.y === second.y) fail(`${label}锚点所在的源线段长度为零。`);
  const point = {
    x: first.x + (second.x - first.x) * anchor.fraction,
    y: first.y + (second.y - first.y) * anchor.fraction,
  };
  if (!Number.isFinite(point.x) || !Number.isFinite(point.y)) fail(`${label}锚点坐标无效。`);
  return point;
}

function exactEndpointNodeFID(topology: GeometryTopologySnapshot, path: ReadonlyArray<{ x: number; y: number }>,
  anchor: GeometryTopologyDeepConnectAnchor, point: { x: number; y: number }): number | null {
  const isFirstEndpoint = anchor.segmentIndex === 0 && anchor.fraction === 0;
  const isLastEndpoint = anchor.segmentIndex === path.length - 2 && anchor.fraction === 1;
  if (!isFirstEndpoint && !isLastEndpoint) return null;

  const rows = topology.nodes.rows.filter((row) => row.x === point.x && row.y === point.y &&
    Number.isSafeInteger(row.fid) && (row.fid ?? -1) >= 0);
  const row = rows[0];
  if (rows.length !== 1 || !row || typeof row.fid !== 'number' || !Number.isSafeInteger(row.fid)) {
    fail('起点位于弧段端点，但当前拓扑中没有唯一对应的源节点。');
  }
  const fid = row.fid;
  const features = topology.map?.features.filter((feature) => feature.featureKind === 'node' && feature.fid === fid) ?? [];
  if (features.length !== 1) fail('起点对应的源节点在当前地图中不唯一。');
  const feature = features[0];
  const nodePath = feature?.paths.length === 1 ? feature.paths[0] : undefined;
  const nodePoint = nodePath?.[0];
  if (!feature || feature.geometryType !== 'point' || !nodePath || nodePath.length !== 1 || !nodePoint ||
      nodePoint.x !== point.x || nodePoint.y !== point.y) {
    fail('起点对应的源节点坐标与弧段端点不一致。');
  }
  return fid;
}

/**
 * Build the Host's complete, source-backed DeepConnect map command from one
 * exact current Geometry arc capture. The server re-reads the captured arc and
 * verifies every point, revision, draft owner and map owner before journaling.
 */
export function createTopologyDeepConnectCommand({
  model,
  topology,
  mapRequestContext,
  mapCellSize,
  arcFID,
  startAnchor,
  endAnchor,
}: CreateTopologyDeepConnectCommandInput): GeometryTopologyMapCommand {
  if (!model || typeof model.assetPath !== 'string' || !model.assetPath.trim() ||
      typeof model.kind !== 'string' || !model.kind.trim()) {
    fail('几何模型身份缺失。');
  }
  const completion = topology?.completion;
  if (!topology?.canEdit || !topology.draftState || !isSHA256(topology.revision) || !completion ||
      completion.sourceRevision !== topology.revision || !isDraftIdentity(completion.draftIdentity)) {
    fail('当前几何修订或草稿身份无效。');
  }
  const draftIdentity = completion.draftIdentity;
  if (!mapRequestContext || !isRequestID(mapRequestContext.requestID) ||
      typeof mapRequestContext.mapID !== 'string' || !mapRequestContext.mapID.trim() ||
      mapRequestContext.mapID.length > 1024 ||
      typeof mapRequestContext.contextToken !== 'string' || !mapRequestContext.contextToken.trim() ||
      mapRequestContext.contextToken.length > 4096 ||
      mapRequestContext.geometryRevision !== topology.revision ||
      mapRequestContext.expectedDraftIdentity !== draftIdentity) {
    fail('地图上下文与当前几何修订或草稿身份不一致。');
  }
  if (!Number.isFinite(mapCellSize) || mapCellSize <= 0) fail('当前地图比例无效。');
  if (!startAnchor || !endAnchor) fail('需要从同一源弧段拾取两个锚点。');

  const path = captureArcPath(topology, arcFID);
  const startPoint = anchorPoint(path, startAnchor, '起点');
  const endPoint = anchorPoint(path, endAnchor, '终点');
  if (startPoint.x === endPoint.x && startPoint.y === endPoint.y) {
    fail('起点与终点重合，请重新拾取两个不同位置。');
  }

  const sourceLocator = `arc:${arcFID}`;
  const startNodeFID = exactEndpointNodeFID(topology, path, startAnchor, startPoint);
  const deepConnectSource: GeometryTopologyDeepConnectSource = {
    sourceKind: 'topology',
    sourceLocator,
    sourceRevision: topology.revision,
    featureFID: arcFID,
    pathIndex: 0,
    componentIdentity: `arc:${arcFID}:path:0`,
    points: path,
    startSegmentIndex: startAnchor.segmentIndex,
    startSegmentFraction: startAnchor.fraction,
    endSegmentIndex: endAnchor.segmentIndex,
    endSegmentFraction: endAnchor.fraction,
    topologySource: {
      model,
      revision: topology.revision,
      draftState: topology.draftState,
      draftIdentity,
      mapRequestContext,
      featureKind: 'arc',
      featureID: sourceLocator,
    },
  };

  const command: GeometryTopologyMapCommand = {
    action: 'deepConnect',
    featureKind: 'arc',
    featureIDs: [],
    coordinates: null,
    mapCellSize,
    deepConnectSource,
  };
  if (startNodeFID !== null) return { ...command, startNodeFID };
  return {
    ...command,
    startArcTarget: {
      arcFID,
      sourceSegmentIndex: startAnchor.segmentIndex,
      point: startPoint,
    },
    splitMode: 'Anywhere',
  };
}
