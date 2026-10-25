import type { GeometryMeshSnapshot, GeometryModelRequest, GeometryTopologyCoordinate } from './geometryTypes';

export interface MeshElementMapCell {
  id: string;
  flowAreaID: string;
  flowAreaName: string;
  meshIndex: number;
  cellIndex: number;
  coordinates: GeometryTopologyCoordinate[];
}

export interface MeshElementMapPreview {
  cells: MeshElementMapCell[];
  totalCellCount: number;
  renderedCellCount: number;
  omittedCellCount: number;
  invalidCandidateCount: number;
  cellLimit: number;
  vertexLimit: number;
  extent: { minX: number; minY: number; maxX: number; maxY: number } | null;
}

export interface MeshElementCapture {
  operationID: string;
  projectSessionID: string;
  assetID: string;
  model: GeometryModelRequest;
  revision: string;
  draftState: string;
  topologyDraftState: string | null;
  meshDraftSHA256: string;
  topologyDraftSHA256: string;
  flowAreaID: string;
  flowAreaName: string;
  flowAreaIndex: number;
  elementKind: 'Cell' | 'Face';
  elementIndex: number;
}

export interface MeshElementFindResult {
  canonicalAssetPath: string;
  sourceRevision: string;
  flowAreaID: string;
  flowAreaName: string;
  elementKind: 'Cell' | 'Face';
  index: number;
  meshDraftSHA256: string;
  topologyDraftSHA256: string;
  x: number;
  y: number;
  viewport: { minX: number; minY: number; maxX: number; maxY: number };
  coordinates: GeometryTopologyCoordinate[];
}

export interface MeshElementPropertyChartResult {
  operationID: string;
  state: 'ready' | 'requiresPropertyTables' | 'cancelled';
  message: string | null;
  projectSessionID: string;
  assetID: string;
  canonicalAssetPath: string;
  sourceRevision: string;
  meshDraftSHA256: string;
  topologyDraftSHA256: string;
  flowAreaID: string;
  flowAreaName: string;
  flowAreaIndex: number;
  meshHash: number;
  elementKind: 'Cell' | 'Face';
  elementIndex: number;
  xQuantity: 'Volume' | 'Area';
  xUnits: string;
  yQuantity: 'Elevation';
  yUnits: string;
  points: Array<{ x: number; y: number }>;
}

type RecordValue = Record<string, unknown>;

function record(value: unknown, description: string): RecordValue {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) throw new Error(`${description}不是有效对象。`);
  return value as RecordValue;
}

function finiteNumber(value: unknown, description: string): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) throw new Error(`${description}不是有限数值。`);
  return value;
}

function integer(value: unknown, description: string, minimum = 0): number {
  if (!Number.isSafeInteger(value) || (value as number) < minimum) throw new Error(`${description}不是有效整数。`);
  return value as number;
}

function nonemptyString(value: unknown, description: string): string {
  if (typeof value !== 'string' || !value.trim()) throw new Error(`${description}缺失。`);
  return value;
}

function revision(value: unknown, description: string): string {
  const result = nonemptyString(value, description);
  if (!/^[0-9a-f]{64}$/i.test(result)) throw new Error(`${description}不是 SHA-256。`);
  return result;
}

function sameRevision(left: string, right: string): boolean {
  return left.toLowerCase() === right.toLowerCase();
}

function parseExtent(value: unknown, description: string): NonNullable<MeshElementMapPreview['extent']> {
  const raw = record(value, description);
  const extent = {
    minX: finiteNumber(raw.minX, `${description} minX`),
    minY: finiteNumber(raw.minY, `${description} minY`),
    maxX: finiteNumber(raw.maxX, `${description} maxX`),
    maxY: finiteNumber(raw.maxY, `${description} maxY`),
  };
  if (extent.minX > extent.maxX || extent.minY > extent.maxY) throw new Error(`${description}范围倒置。`);
  return extent;
}

/** Parse only the bounded source-coordinate preview carried by geometry.mesh.load. */
export function parseMeshElementMapPreview(snapshot: GeometryMeshSnapshot | undefined): MeshElementMapPreview | null {
  if (!snapshot) return null;
  const rawMap = (snapshot as GeometryMeshSnapshot & { map?: unknown }).map;
  if (rawMap === null || rawMap === undefined) return null;
  const map = record(rawMap, 'Host 网格预览');
  if (!Array.isArray(map.cells)) throw new Error('Host 网格预览缺少单元数组。');
  const totalCellCount = integer(map.totalCellCount, 'Host 总单元数');
  const renderedCellCount = integer(map.renderedCellCount, 'Host 已绘制单元数');
  const omittedCellCount = integer(map.omittedCellCount, 'Host 未绘制单元数');
  const invalidCandidateCount = integer(map.invalidCandidateCount, 'Host 无效候选数');
  const cellLimit = integer(map.cellLimit, 'Host 单元预览上限', 1);
  const vertexLimit = integer(map.vertexLimit, 'Host 顶点预览上限', 1);
  if (cellLimit > 12_000 || vertexLimit > 1_000_000 || map.cells.length > cellLimit ||
      renderedCellCount !== map.cells.length || totalCellCount !== renderedCellCount + omittedCellCount ||
      invalidCandidateCount > omittedCellCount) {
    throw new Error('Host 网格预览计数或有界上限不一致，已停止绘制。');
  }

  const flowAreas = new Map(snapshot.flowAreas.map((area) => [area.id, area]));
  const seen = new Set<string>();
  let vertices = 0;
  const cells = map.cells.map((candidate, position): MeshElementMapCell => {
    const raw = record(candidate, `Host 网格单元 ${position + 1}`);
    const cell: MeshElementMapCell = {
      id: nonemptyString(raw.id, `Host 网格单元 ${position + 1} 标识`),
      flowAreaID: nonemptyString(raw.flowAreaID, `Host 网格单元 ${position + 1} 流动区域`),
      flowAreaName: nonemptyString(raw.flowAreaName, `Host 网格单元 ${position + 1} 流动区域名称`),
      meshIndex: integer(raw.meshIndex, `Host 网格单元 ${position + 1} 网格索引`),
      cellIndex: integer(raw.cellIndex, `Host 网格单元 ${position + 1} 源单元索引`),
      coordinates: [],
    };
    const area = flowAreas.get(cell.flowAreaID);
    if (!area || area.name !== cell.flowAreaName || area.rowIndex !== cell.meshIndex || cell.cellIndex >= area.cellCount) {
      throw new Error(`Host 网格单元 ${cell.id} 与当前 Flow Area 源行不匹配。`);
    }
    const key = `${cell.flowAreaID}\u0000${cell.cellIndex}`;
    if (seen.has(key)) throw new Error(`Host 网格预览包含重复源单元 ${cell.id}。`);
    seen.add(key);
    if (!Array.isArray(raw.coordinates) || raw.coordinates.length < 3) throw new Error(`Host 网格单元 ${cell.id} 缺少多边形坐标。`);
    cell.coordinates = raw.coordinates.map((coordinate, index) => {
      const point = record(coordinate, `Host 网格单元 ${cell.id} 坐标 ${index + 1}`);
      return { x: finiteNumber(point.x, '网格 X 坐标'), y: finiteNumber(point.y, '网格 Y 坐标') };
    });
    vertices += cell.coordinates.length;
    if (vertices > vertexLimit) throw new Error('Host 网格预览超过其顶点预算，已停止绘制。');
    return cell;
  });

  let extent: MeshElementMapPreview['extent'] = null;
  if (cells.length) {
    let minX = Number.POSITIVE_INFINITY;
    let minY = Number.POSITIVE_INFINITY;
    let maxX = Number.NEGATIVE_INFINITY;
    let maxY = Number.NEGATIVE_INFINITY;
    for (const cell of cells) {
      for (const point of cell.coordinates) {
        minX = Math.min(minX, point.x);
        minY = Math.min(minY, point.y);
        maxX = Math.max(maxX, point.x);
        maxY = Math.max(maxY, point.y);
      }
    }
    extent = { minX, minY, maxX, maxY };
  }
  return { cells, totalCellCount, renderedCellCount, omittedCellCount, invalidCandidateCount, cellLimit, vertexLimit, extent };
}

export async function sha256Text(value: string): Promise<string> {
  if (!globalThis.crypto?.subtle) throw new Error('当前环境无法计算 SHA-256，无法安全读取网格元素。');
  const digest = await globalThis.crypto.subtle.digest('SHA-256', new TextEncoder().encode(value));
  return Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, '0')).join('').toUpperCase();
}

export function parseMeshElementFindResult(value: unknown, capture: MeshElementCapture): MeshElementFindResult {
  const raw = record(value, 'Host 网格元素定位回执');
  const result: MeshElementFindResult = {
    canonicalAssetPath: nonemptyString(raw.canonicalAssetPath, 'Host Geometry 路径'),
    sourceRevision: revision(raw.sourceRevision, 'Host Geometry 修订'),
    flowAreaID: nonemptyString(raw.flowAreaID, 'Host Flow Area 标识'),
    flowAreaName: nonemptyString(raw.flowAreaName, 'Host Flow Area 名称'),
    elementKind: raw.elementKind === 'Cell' || raw.elementKind === 'Face' ? raw.elementKind : (() => { throw new Error('Host 网格元素类型与请求不匹配。'); })(),
    index: integer(raw.index, 'Host 网格元素索引'),
    meshDraftSHA256: revision(raw.meshDraftSHA256, 'Host mesh draft SHA-256'),
    topologyDraftSHA256: revision(raw.topologyDraftSHA256, 'Host topology draft SHA-256'),
    x: finiteNumber(raw.x, 'Host 网格元素中心 X'),
    y: finiteNumber(raw.y, 'Host 网格元素中心 Y'),
    viewport: parseExtent(raw.viewport, 'Host 网格元素视口'),
    coordinates: [],
  };
  if (result.canonicalAssetPath !== capture.model.assetPath || !sameRevision(result.sourceRevision, capture.revision) ||
      result.flowAreaID !== capture.flowAreaID || result.flowAreaName !== capture.flowAreaName ||
      result.elementKind !== capture.elementKind || result.index !== capture.elementIndex ||
      !sameRevision(result.meshDraftSHA256, capture.meshDraftSHA256) ||
      !sameRevision(result.topologyDraftSHA256, capture.topologyDraftSHA256) ||
      result.viewport.minX >= result.viewport.maxX || result.viewport.minY >= result.viewport.maxY ||
      result.x < result.viewport.minX || result.x > result.viewport.maxX ||
      result.y < result.viewport.minY || result.y > result.viewport.maxY) {
    throw new Error('Host 网格元素定位回执不属于当前工程、Geometry 源、草稿或所选元素。');
  }
  if (!Array.isArray(raw.coordinates)) throw new Error('Host 网格元素定位回执缺少源坐标。');
  const minimumCoordinates = capture.elementKind === 'Cell' ? 3 : 2;
  if (raw.coordinates.length < minimumCoordinates) throw new Error('Host 网格元素源坐标不足，不能显示定位结果。');
  result.coordinates = raw.coordinates.map((coordinate, index) => {
    const point = record(coordinate, `Host 网格元素坐标 ${index + 1}`);
    return { x: finiteNumber(point.x, '网格元素 X 坐标'), y: finiteNumber(point.y, '网格元素 Y 坐标') };
  });
  return result;
}

export function parseMeshElementPropertyChart(value: unknown, capture: MeshElementCapture): MeshElementPropertyChartResult {
  const raw = record(value, 'Host 网格属性曲线回执');
  const state = raw.state;
  if (state !== 'ready' && state !== 'requiresPropertyTables' && state !== 'cancelled') throw new Error('Host 网格属性曲线状态无效。');
  const result: MeshElementPropertyChartResult = {
    operationID: nonemptyString(raw.operationID, 'Host 曲线操作标识'),
    state,
    message: raw.message === null || raw.message === undefined ? null : nonemptyString(raw.message, 'Host 曲线说明'),
    projectSessionID: nonemptyString(raw.projectSessionID, 'Host 工程会话标识'),
    assetID: nonemptyString(raw.assetID, 'Host Geometry 资产标识'),
    canonicalAssetPath: nonemptyString(raw.canonicalAssetPath, 'Host Geometry 路径'),
    sourceRevision: revision(raw.sourceRevision, 'Host Geometry 修订'),
    meshDraftSHA256: revision(raw.meshDraftSHA256, 'Host mesh draft SHA-256'),
    topologyDraftSHA256: revision(raw.topologyDraftSHA256, 'Host topology draft SHA-256'),
    flowAreaID: nonemptyString(raw.flowAreaID, 'Host Flow Area 标识'),
    flowAreaName: nonemptyString(raw.flowAreaName, 'Host Flow Area 名称'),
    flowAreaIndex: integer(raw.flowAreaIndex, 'Host Flow Area 索引'),
    meshHash: integer(raw.meshHash, 'Host 网格摘要码', -2_147_483_648),
    elementKind: raw.elementKind === 'Cell' || raw.elementKind === 'Face' ? raw.elementKind : (() => { throw new Error('Host 曲线元素类型与请求不匹配。'); })(),
    elementIndex: integer(raw.elementIndex, 'Host 曲线元素索引'),
    xQuantity: raw.xQuantity === 'Volume' || raw.xQuantity === 'Area' ? raw.xQuantity : (() => { throw new Error('Host 曲线横轴物理量无效。'); })(),
    xUnits: typeof raw.xUnits === 'string' ? raw.xUnits : (() => { throw new Error('Host 曲线横轴单位无效。'); })(),
    yQuantity: raw.yQuantity === 'Elevation' ? raw.yQuantity : (() => { throw new Error('Host 曲线纵轴物理量无效。'); })(),
    yUnits: typeof raw.yUnits === 'string' ? raw.yUnits : (() => { throw new Error('Host 曲线纵轴单位无效。'); })(),
    points: [],
  };
  const expectedQuantity = capture.elementKind === 'Cell' ? 'Volume' : 'Area';
  const expectedUnitPairs = capture.elementKind === 'Cell'
    ? [['ft³', 'ft'], ['m³', 'm'], ['', '']]
    : [['ft²', 'ft'], ['m²', 'm'], ['', '']];
  if (result.operationID !== capture.operationID || result.projectSessionID !== capture.projectSessionID ||
      result.assetID !== capture.assetID || result.canonicalAssetPath !== capture.model.assetPath ||
      !sameRevision(result.sourceRevision, capture.revision) ||
      !sameRevision(result.meshDraftSHA256, capture.meshDraftSHA256) ||
      !sameRevision(result.topologyDraftSHA256, capture.topologyDraftSHA256) ||
      result.flowAreaID !== capture.flowAreaID || result.flowAreaName !== capture.flowAreaName ||
      result.flowAreaIndex !== capture.flowAreaIndex || result.elementKind !== capture.elementKind ||
      result.elementIndex !== capture.elementIndex || result.xQuantity !== expectedQuantity ||
      result.yQuantity !== 'Elevation' || result.meshHash < -2_147_483_648 || result.meshHash > 2_147_483_647 ||
      !expectedUnitPairs.some(([xUnit, yUnit]) => result.xUnits === xUnit && result.yUnits === yUnit)) {
    throw new Error('Host 网格曲线回执不属于当前工程、Geometry 源、草稿、Flow Area、元素类型、索引或属性单位。');
  }
  if (!Array.isArray(raw.points)) throw new Error('Host 网格曲线缺少点数组。');
  result.points = raw.points.map((candidate, index) => {
    const point = record(candidate, `Host 曲线点 ${index + 1}`);
    return { x: finiteNumber(point.x, `Host 曲线点 ${index + 1} 横坐标`), y: finiteNumber(point.y, `Host 曲线点 ${index + 1} 纵坐标`) };
  });
  if (state === 'ready' && result.points.length === 0) throw new Error('Host 标记曲线就绪，但没有返回实际属性表数据。');
  if (state !== 'ready' && result.points.length !== 0) throw new Error('Host 非就绪曲线状态却返回了数据点，已拒绝显示。');
  return result;
}
