import type { Language } from '../../../shared/workbench';
import type { ResultDataset, ResultSeries } from '../../results/data';
import type { XY } from '../mapTypes';

const COORDINATE_HASH_DOMAIN = 'HECRAS-MAP-LINE-v1\0';
export const MAP_SAMPLE_LINE_MAX_POINTS = 10_000;
export const MAP_SAMPLE_LINE_NO_DATA = -9999;

/**
 * Match MapResultChartService.HashCoordinates byte-for-byte: UTF-8 domain
 * prefix followed by each original XY pair as big-endian IEEE-754 doubles.
 */
export async function computeMapSampleLineCoordinateSHA(coordinates: readonly XY[]): Promise<string> {
  if (coordinates.length < 2 || coordinates.length > MAP_SAMPLE_LINE_MAX_POINTS
    || coordinates.some((point) => point.length !== 2 || !Number.isFinite(point[0]) || !Number.isFinite(point[1]))) {
    throw new Error('Sample-line coordinates must contain 2 to 10,000 finite XY points.');
  }
  if (!globalThis.crypto?.subtle) {
    throw new Error('Web Crypto SHA-256 is unavailable in this browser context.');
  }

  const prefix = new TextEncoder().encode(COORDINATE_HASH_DOMAIN);
  const bytes = new Uint8Array(prefix.length + coordinates.length * 16);
  bytes.set(prefix);
  const view = new DataView(bytes.buffer);
  let offset = prefix.length;
  for (const [x, y] of coordinates) {
    view.setFloat64(offset, x, false);
    offset += 8;
    view.setFloat64(offset, y, false);
    offset += 8;
  }
  const digest = new Uint8Array(await globalThis.crypto.subtle.digest('SHA-256', bytes));
  return Array.from(digest, (value) => value.toString(16).padStart(2, '0')).join('').toUpperCase();
}

export interface MapSampleLinePoint {
  profileIndex: number;
  time: string;
  value: number | null;
}

export interface MapSampleLineComputedSeries {
  resultPath: string;
  revision: string;
  resultName: string;
  mode: 'Flow' | 'CumulativeVolume';
  quantity: string;
  units: string;
  available: boolean;
  unavailableReason: string | null;
  points: MapSampleLinePoint[];
}

export interface MapSampleLineSelectionReference {
  node: {
    rootName: string;
    nodeType: string;
    nodeName: string;
    nodePath?: number[] | null;
    assetID?: string | null;
    mapType?: string | null;
    filename?: string | null;
    filenameRelative?: string | null;
    parentGeometryAssetID?: string | null;
    parentGeometryNodePath?: number[] | null;
    parentGeometryNodeName?: string | null;
    associationAssetID?: string | null;
    associationKind?: string | null;
    parentPlanAssetID?: string | null;
    parentPlanNodePath?: number[] | null;
    parentPlanNodeName?: string | null;
    parentResultNodeType?: string | null;
    parentResultAssetID?: string | null;
    parentResultNodePath?: number[] | null;
    parentResultNodeName?: string | null;
    parentResultRevision?: string | null;
  };
  selectionIndex: number;
}

export interface MapSampleLineOutput {
  selections: MapSampleLineSelectionReference[];
  selectedMapType: string | null;
  profileIndex: number | null;
  series: MapSampleLineComputedSeries;
}

export interface MapSampleLineSideEffect {
  code: string;
  path: string | null;
}

export interface MapSampleLineResponse {
  projectPath: string;
  projectRevision: string;
  projectXmlSHA: string;
  visualizationRevision: string;
  mapID: string;
  mapIndex: number;
  mapName: string;
  mapType: string;
  contextToken: string;
  featureContextToken: string;
  feature: {
    vectorPath: string;
    vectorRevision: string;
    featureID: string;
    coordinateSHA: string;
  };
  mode: 'Flow' | 'CumulativeVolume';
  sampleLine: Array<{ x: number; y: number }>;
  series: MapSampleLineOutput[];
  sideEffects: MapSampleLineSideEffect[];
}

export function comparableMapSourcePath(value: string): string {
  const normalized = value.trim().replaceAll('\\', '/').replace(/\/+$/, '') || '/';
  return /^[a-z]:\//i.test(normalized) ? normalized.toLowerCase() : normalized;
}

export function isSha256(value: unknown): value is string {
  return typeof value === 'string' && /^[a-f\d]{64}$/i.test(value);
}

export function sameXYCoordinates(left: readonly XY[], right: readonly { x: number; y: number }[]): boolean {
  return left.length === right.length && left.every(([x, y], index) =>
    Object.is(x, right[index]?.x) && Object.is(y, right[index]?.y));
}

export function mapSampleLineSeriesToPlot(
  result: MapSampleLineComputedSeries,
  language: Language,
  disambiguator: string,
): { dataset: ResultDataset; series: ResultSeries; noDataCount: number } {
  const values = result.points.map((point) => point.value === MAP_SAMPLE_LINE_NO_DATA ? null : point.value);
  const series: ResultSeries = {
    id: `map-sample-line:${comparableMapSourcePath(result.resultPath)}:${result.revision.toUpperCase()}:${disambiguator}`,
    name: `${result.resultName} · ${result.quantity}`,
    units: result.units,
    values,
  };
  return {
    dataset: {
      name: result.resultName,
      times: result.points.map((point) => point.time),
      series: [series],
      source: 'backend-result',
      sourceLabel: language === 'zh'
        ? (result.quantity === 'Flow' ? '地图采样线结果中的流量' : '地图采样线结果中的累积水量')
        : (result.quantity === 'Flow' ? 'Map sample-line flow from Result output' : 'Map sample-line accumulated volume from Result output'),
    },
    series,
    noDataCount: result.points.filter((point) => point.value === null || point.value === MAP_SAMPLE_LINE_NO_DATA).length,
  };
}
