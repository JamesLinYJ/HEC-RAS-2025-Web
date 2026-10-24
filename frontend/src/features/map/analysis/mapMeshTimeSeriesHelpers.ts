import type { MapMeshElementHit, MapResultAnalysisCapture } from './mapAnalysisTypes';
import type { CatalogTreeNodeLocator } from '../mapTypes';
import type { ResultDataset, ResultSeries } from '../../results/data';

type Capture = MapResultAnalysisCapture;
type MapRequest = Capture['map'];
type ResultChoice = NonNullable<NonNullable<Capture['slotZero']>['selectedResult']>;

export interface MeshTimeSeriesFlowArea {
  flowAreaIndex: number;
  flowAreaID: string;
  name: string;
  meshHash: number;
  cellCount: number;
  faceCount: number;
  hasMesh: boolean;
}

export interface MeshTimeSeriesInspection {
  state: 'ready' | 'noMesh';
  message: string | null;
  projectSessionID: string;
  projectCanonicalPath: string;
  projectRevision: string;
  projectXmlSHA: string;
  visualizationRevision: string;
  coreLayerContextIdentity: string;
  mapID: string;
  mapIndex: number;
  mapName: string;
  mapType: string;
  contextToken: string;
  originPath: string;
  originRevision: string;
  matchingResultNames: string[];
  flowAreas: MeshTimeSeriesFlowArea[];
  skippedComputingResultCount: number;
}

export type MeshElementKind = 'Cell' | 'Face';

export interface MeshTimeSeriesRequestSelection {
  flowArea: MeshTimeSeriesFlowArea;
  elementKind: MeshElementKind;
  elementIndex: number;
  operationID: string;
}

export interface MeshTimeSeriesResultEntry {
  resultPath: string;
  resultRevision: string;
  resultName: string;
  owners: CatalogTreeNodeLocator[];
  variable: string;
  units: string;
  times: string[];
  values: Array<number | null>;
}

export interface MeshTimeSeriesExecution {
  operationID: string;
  state: 'ready' | 'noMatchingMesh' | 'cancelled';
  message: string | null;
  flowAreaIndex: number;
  flowAreaID: string;
  flowAreaName: string;
  meshHash: number;
  elementKind: MeshElementKind;
  elementIndex: number;
  variable: string;
  units: string;
  entries: MeshTimeSeriesResultEntry[];
  skippedComputingResultCount: number;
}

export interface MeshTimeSeriesChartData {
  dataset: ResultDataset;
  series: ResultSeries[];
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function record(value: unknown, label: string): Record<string, unknown> {
  if (!isRecord(value)) throw new Error(`${label} is not an object.`);
  return value;
}

function string(value: unknown, label: string): string {
  if (typeof value !== 'string') throw new Error(`${label} is not a string.`);
  return value;
}

function nonEmptyString(value: unknown, label: string): string {
  const result = string(value, label);
  if (!result.trim()) throw new Error(`${label} is empty.`);
  return result;
}

function nullableString(value: unknown, label: string): string | null {
  return value === null ? null : string(value, label);
}

function finiteNumber(value: unknown, label: string): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) throw new Error(`${label} is not a finite number.`);
  return value;
}

function integer(value: unknown, label: string): number {
  const result = finiteNumber(value, label);
  if (!Number.isInteger(result)) throw new Error(`${label} is not an integer.`);
  return result;
}

function nonNegativeInteger(value: unknown, label: string): number {
  const result = integer(value, label);
  if (result < 0) throw new Error(`${label} is negative.`);
  return result;
}

function boolean(value: unknown, label: string): boolean {
  if (typeof value !== 'boolean') throw new Error(`${label} is not a boolean.`);
  return value;
}

function stringArray(value: unknown, label: string): string[] {
  if (!Array.isArray(value)) throw new Error(`${label} is not an array.`);
  return value.map((item, index) => string(item, `${label}[${index}]`));
}

function integerArray(value: unknown, label: string): number[] {
  if (!Array.isArray(value)) throw new Error(`${label} is not an array.`);
  return value.map((item, index) => integer(item, `${label}[${index}]`));
}

function stable(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(stable);
  if (!isRecord(value)) return value;
  return Object.fromEntries(Object.keys(value).sort().map((key) => [key, stable(value[key])]));
}

export function meshLocatorKey(value: CatalogTreeNodeLocator): string {
  return JSON.stringify(stable(value));
}

export function sameMeshLocator(left: CatalogTreeNodeLocator, right: CatalogTreeNodeLocator): boolean {
  return meshLocatorKey(left) === meshLocatorKey(right);
}

function sameRevision(left: string, right: string): boolean {
  return left.trim().toUpperCase() === right.trim().toUpperCase();
}

function sameMapID(left: string, right: string): boolean {
  return left.trim().replace(/^\{|\}$/g, '').toUpperCase() === right.trim().replace(/^\{|\}$/g, '').toUpperCase();
}

function canonicalPathKey(value: string): string {
  return value.trim().replaceAll('\\', '/').replace(/\/+$/, '');
}

function samePath(left: string, right: string): boolean {
  const leftKey = canonicalPathKey(left);
  const rightKey = canonicalPathKey(right);
  const windowsPath = /^[a-z]:\//i.test(leftKey) || leftKey.startsWith('//') ||
    /^[a-z]:\//i.test(rightKey) || rightKey.startsWith('//');
  return windowsPath ? leftKey.toUpperCase() === rightKey.toUpperCase() : leftKey === rightKey;
}

function coreLayerIdentity(map: MapRequest): string {
  const mapRecord = map as Record<string, unknown>;
  const context = mapRecord.coreLayerContext;
  if (!isRecord(context) || typeof context.identityToken !== 'string') return '';
  return context.identityToken;
}

function selectedResult(capture: Capture): ResultChoice | null {
  const slotZero = capture.slotZero;
  const selected = slotZero?.selectedResult;
  const activeSlot = capture.activeNode.selections.find((selection) => selection.selectionIndex === 0);
  if (slotZero?.selectionIndex !== 0 || !selected || !activeSlot?.selectedResult ||
      activeSlot.selectionMode !== slotZero.selectionMode ||
      activeSlot.selectedResult.resultPath !== selected.resultPath ||
      !sameRevision(activeSlot.selectedResult.revision, selected.revision) ||
      selected.isComputing ||
      !['Result', 'MultiResult'].includes(capture.activeNode.node.nodeType) ||
      capture.activeNode.nodeKind !== capture.activeNode.node.nodeType) return null;
  return selected;
}

/** A request identity for this Result slot, intentionally independent of the animation profile index. */
export function meshTimeSeriesCaptureIdentity(capture: Capture | null): string | null {
  if (!capture || !capture.ownerKey || !capture.contextToken) return null;
  const result = selectedResult(capture);
  const map = capture.map;
  if (!result || !map.projectPath || !map.projectXml || !map.projectXmlSHA || !map.projectSessionID ||
      !map.expectedVisualizationRevision || !map.mapID || !Number.isInteger(map.mapIndex) ||
      !sameRevision(map.expectedVisualizationRevision, capture.visualizationRevision) ||
      map.projectXmlSHA.toUpperCase() !== capture.projectXmlSHA.toUpperCase()) return null;
  return JSON.stringify([
    capture.ownerKey,
    capture.projectRevision,
    capture.projectXmlSHA.toUpperCase(),
    capture.visualizationRevision,
    map.projectSessionID.toUpperCase(),
    canonicalPathKey(map.projectPath),
    map.mapID.toUpperCase(),
    map.mapIndex,
    coreLayerIdentity(map),
    map.mapName,
    map.mapType,
    capture.contextToken,
    meshLocatorKey(capture.activeNode.node),
    result.resultPath,
    result.revision.toUpperCase(),
    capture.slotZero?.selectionMode,
  ]);
}

export function meshTimeSeriesResultOrigin(capture: Capture): Record<string, unknown> | null {
  const result = selectedResult(capture);
  if (!result) return null;
  return {
    resultNode: capture.activeNode.node,
    selectionIndex: 0,
    selectedResultPath: result.resultPath,
    selectedResultRevision: result.revision,
  };
}
export function meshTimeSeriesOwner(capture: Capture): Record<string, unknown> | null {
  const resultOrigin = meshTimeSeriesResultOrigin(capture);
  if (!resultOrigin) return null;
  return {
    map: capture.map,
    projectSessionID: capture.map.projectSessionID,
    contextToken: capture.contextToken,
    selectionOverrides: capture.selectionOverrides,
    geometryOrigin: null,
    resultOrigin,
  };
}

/** A hit is only a prefill candidate when every captured Result/map owner still matches slot zero. */
export function isMeshHitForCapture(hit: MapMeshElementHit | null, capture: Capture | null): hit is MapMeshElementHit {
  const result = capture ? selectedResult(capture) : null;
  if (!hit || !capture || !result || hit.ownerKey !== capture.ownerKey ||
      hit.captureKey !== capture.captureKey ||
      hit.contextToken !== capture.contextToken || hit.selectionIndex !== 0 ||
      !sameMapID(hit.mapID, capture.map.mapID) || hit.mapIndex !== capture.map.mapIndex ||
      !sameRevision(hit.visualizationRevision, capture.visualizationRevision) ||
      !sameMeshLocator(hit.resultNode, capture.activeNode.node) ||
      !samePath(hit.resultPath, result.resultPath) || !sameRevision(hit.resultRevision, result.revision) ||
      !['Cell', 'Face'].includes(hit.elementKind) || !Number.isSafeInteger(hit.flowAreaIndex) ||
      hit.flowAreaIndex < 0 || !Number.isSafeInteger(hit.elementIndex) || hit.elementIndex < 0 ||
      !hit.geometryContextToken || !hit.geometryPath || !hit.geometryRevision ||
      !samePath(hit.geometryPath, result.resultPath) || !sameRevision(hit.geometryRevision, result.revision)) return false;
  return true;
}

function parseLocator(value: unknown, label: string): CatalogTreeNodeLocator {
  const item = record(value, label);
  if (typeof item.nodeType !== 'string' || !Array.isArray(item.nodePath) ||
      item.nodePath.some((part) => !Number.isInteger(part) || Number(part) < 0)) {
    throw new Error(`${label} is invalid.`);
  }
  return item as unknown as CatalogTreeNodeLocator;
}

function parseFlowArea(value: unknown, index: number): MeshTimeSeriesFlowArea {
  const item = record(value, `flowAreas[${index}]`);
  return {
    flowAreaIndex: nonNegativeInteger(item.flowAreaIndex, `flowAreas[${index}].flowAreaIndex`),
    flowAreaID: nonEmptyString(item.flowAreaID, `flowAreas[${index}].flowAreaID`),
    name: string(item.name, `flowAreas[${index}].name`),
    meshHash: integer(item.meshHash, `flowAreas[${index}].meshHash`),
    cellCount: nonNegativeInteger(item.cellCount, `flowAreas[${index}].cellCount`),
    faceCount: nonNegativeInteger(item.faceCount, `flowAreas[${index}].faceCount`),
    hasMesh: boolean(item.hasMesh, `flowAreas[${index}].hasMesh`),
  };
}

function requireCurrentCapture(capture: Capture): ResultChoice {
  if (!meshTimeSeriesCaptureIdentity(capture)) throw new Error('The captured Result slot zero is incomplete or stale.');
  const result = selectedResult(capture);
  if (!result) throw new Error('The captured Result slot zero is unavailable.');
  return result;
}

function validateOwnerEcho(value: Record<string, unknown>, capture: Capture, label: string): void {
  const result = requireCurrentCapture(capture);
  const map = capture.map;
  const returnedOriginNode = value.originResultNode === null
    ? null
    : parseLocator(value.originResultNode, `${label}.originResultNode`);
  if (value.projectSessionID !== map.projectSessionID ||
      typeof value.projectCanonicalPath !== 'string' || !samePath(value.projectCanonicalPath, map.projectPath) ||
      typeof value.projectRevision !== 'string' || !sameRevision(value.projectRevision, capture.projectRevision) ||
      typeof value.projectXmlSHA !== 'string' || !sameRevision(value.projectXmlSHA, capture.projectXmlSHA) ||
      typeof value.visualizationRevision !== 'string' || !sameRevision(value.visualizationRevision, capture.visualizationRevision) ||
      value.coreLayerContextIdentity !== coreLayerIdentity(map) ||
      typeof value.mapID !== 'string' || !sameMapID(value.mapID, map.mapID) ||
      value.mapIndex !== map.mapIndex || value.mapName !== map.mapName || value.mapType !== map.mapType ||
      value.contextToken !== capture.contextToken || value.originKind !== 'Result' ||
      typeof value.originPath !== 'string' || !samePath(value.originPath, result.resultPath) ||
      typeof value.originRevision !== 'string' || !sameRevision(value.originRevision, result.revision) ||
      value.originMeshDraftSHA256 !== '' || value.originTopologyDraftSHA256 !== '' ||
      !returnedOriginNode || !sameMeshLocator(returnedOriginNode, capture.activeNode.node) ||
      value.originSelectionIndex !== 0) {
    throw new Error(`${label} no longer matches the captured Result, map, session, or source revisions.`);
  }
}

export function parseMeshTimeSeriesInspection(value: unknown, capture: Capture): MeshTimeSeriesInspection {
  const item = record(value, 'mesh time-series inspection');
  validateOwnerEcho(item, capture, 'mesh time-series inspection');
  if (item.state !== 'ready' && item.state !== 'noMesh') throw new Error('The mesh time-series inspection state is invalid.');
  if (!Array.isArray(item.flowAreas)) throw new Error('The mesh time-series inspection has no Flow Area list.');
  const flowAreas = item.flowAreas.map(parseFlowArea);
  if (new Set(flowAreas.map((area) => area.flowAreaIndex)).size !== flowAreas.length ||
      new Set(flowAreas.map((area) => area.flowAreaID)).size !== flowAreas.length) {
    throw new Error('The mesh time-series inspection contains duplicate Flow Area identities.');
  }
  const hasAnyMesh = flowAreas.some((area) => area.hasMesh);
  if ((item.state === 'ready') !== hasAnyMesh) throw new Error('The mesh time-series inspection state does not match its Flow Area meshes.');
  return {
    state: item.state,
    message: nullableString(item.message, 'message'),
    projectSessionID: nonEmptyString(item.projectSessionID, 'projectSessionID'),
    projectCanonicalPath: nonEmptyString(item.projectCanonicalPath, 'projectCanonicalPath'),
    projectRevision: nonEmptyString(item.projectRevision, 'projectRevision'),
    projectXmlSHA: nonEmptyString(item.projectXmlSHA, 'projectXmlSHA'),
    visualizationRevision: nonEmptyString(item.visualizationRevision, 'visualizationRevision'),
    coreLayerContextIdentity: string(item.coreLayerContextIdentity, 'coreLayerContextIdentity'),
    mapID: nonEmptyString(item.mapID, 'mapID'),
    mapIndex: integer(item.mapIndex, 'mapIndex'),
    mapName: string(item.mapName, 'mapName'),
    mapType: string(item.mapType, 'mapType'),
    contextToken: nonEmptyString(item.contextToken, 'contextToken'),
    originPath: nonEmptyString(item.originPath, 'originPath'),
    originRevision: nonEmptyString(item.originRevision, 'originRevision'),
    matchingResultNames: stringArray(item.matchingResultNames, 'matchingResultNames'),
    flowAreas,
    skippedComputingResultCount: nonNegativeInteger(item.skippedComputingResultCount, 'skippedComputingResultCount'),
  };
}

function parseSample(value: unknown, label: string): number | null {
  if (value === null) return null;
  if (typeof value === 'number') return Number.isFinite(value) ? value : null;
  if (typeof value === 'string' && /^(?:[+-]?infinity|nan)$/i.test(value.trim())) return null;
  throw new Error(`${label} is not a numeric Result value or an explicit NoData gap.`);
}

function expectedOwnerKeys(capture: Capture): Set<string> {
  return new Set(capture.selectionOverrides
    .filter((selection) => selection.selectionIndex === 0 &&
      (selection.node.nodeType === 'Result' || selection.node.nodeType === 'MultiResult'))
    .map((selection) => meshLocatorKey(selection.node)));
}

function expectedResultByOwner(capture: Capture): Map<string, { path: string; revision: string }> {
  const expected = new Map<string, { path: string; revision: string }>();
  for (const selection of capture.selectionOverrides) {
    if (selection.selectionIndex !== 0 || !['Result', 'MultiResult'].includes(selection.node.nodeType) ||
        selection.mode !== 'Explicit' || !selection.resultPath || !selection.resultRevision) continue;
    expected.set(meshLocatorKey(selection.node), { path: selection.resultPath, revision: selection.resultRevision });
  }
  const activeSlot = capture.activeNode.selections.find((selection) => selection.selectionIndex === 0);
  if (activeSlot?.selectedResult) {
    expected.set(meshLocatorKey(capture.activeNode.node), {
      path: activeSlot.selectedResult.resultPath,
      revision: activeSlot.selectedResult.revision,
    });
  }
  return expected;
}

function parseSeriesEntry(
  value: unknown,
  index: number,
  capture: Capture,
  flowArea: MeshTimeSeriesFlowArea,
  expectedVariable: string,
  expectedUnits: string,
): MeshTimeSeriesResultEntry {
  const item = record(value, `series[${index}]`);
  if (!Array.isArray(item.owners) || !Array.isArray(item.selectionIndices)) {
    throw new Error(`series[${index}] has no exact Result owner list.`);
  }
  const owners = item.owners.map((owner, ownerIndex) => parseLocator(owner, `series[${index}].owners[${ownerIndex}]`));
  const selectionIndices = integerArray(item.selectionIndices, `series[${index}].selectionIndices`);
  const knownOwners = expectedOwnerKeys(capture);
  const resultPath = nonEmptyString(item.resultPath, `series[${index}].resultPath`);
  const resultRevision = nonEmptyString(item.resultRevision, `series[${index}].resultRevision`);
  const expectedResults = expectedResultByOwner(capture);
  if (owners.length === 0 || owners.length !== selectionIndices.length ||
      selectionIndices.some((selectionIndex) => selectionIndex !== 0) ||
      owners.some((owner) => !knownOwners.has(meshLocatorKey(owner)))) {
    throw new Error(`series[${index}] does not belong to an active selected Result slot zero.`);
  }
  if (owners.some((owner) => {
    const expected = expectedResults.get(meshLocatorKey(owner));
    return expected && (!samePath(resultPath, expected.path) || !sameRevision(resultRevision, expected.revision));
  })) {
    throw new Error(`series[${index}] no longer matches its explicitly selected Result source revision.`);
  }
  const times = stringArray(item.times, `series[${index}].times`);
  if (!Array.isArray(item.values) || item.values.length !== times.length) {
    throw new Error(`series[${index}] time axis and values have different lengths.`);
  }
  const values = item.values.map((sample, sampleIndex) => parseSample(sample, `series[${index}].values[${sampleIndex}]`));
  const flowAreaIndex = integer(item.flowAreaIndex, `series[${index}].flowAreaIndex`);
  const meshHash = integer(item.meshHash, `series[${index}].meshHash`);
  if (flowAreaIndex !== flowArea.flowAreaIndex || item.flowAreaName !== flowArea.name || meshHash !== flowArea.meshHash ||
      item.variable !== expectedVariable) {
    throw new Error(`series[${index}] belongs to a different Flow Area mesh or output variable.`);
  }
  return {
    resultPath,
    resultRevision,
    resultName: string(item.resultName, `series[${index}].resultName`),
    owners,
    variable: expectedVariable,
    units: expectedUnits,
    times,
    values,
  };
}

export function parseMeshTimeSeriesExecution(
  value: unknown,
  capture: Capture,
  inspection: MeshTimeSeriesInspection,
  selection: MeshTimeSeriesRequestSelection,
): MeshTimeSeriesExecution {
  const item = record(value, 'mesh time-series execution');
  validateOwnerEcho(item, capture, 'mesh time-series execution');
  if (item.operationID !== selection.operationID) throw new Error('The mesh time-series response belongs to a different operation.');
  if (item.state !== 'ready' && item.state !== 'noMatchingMesh' && item.state !== 'cancelled') {
    throw new Error('The mesh time-series execution state is invalid.');
  }
  const flowAreaIndex = nonNegativeInteger(item.flowAreaIndex, 'flowAreaIndex');
  const meshHash = integer(item.meshHash, 'meshHash');
  // The Host returns OutputVariable.Name, including its source mesh element.
  const expectedVariable = selection.elementKind === 'Cell' ? 'Cell Depth' : 'Face Velocity';
  const flowArea = inspection.flowAreas.find((area) => area.flowAreaIndex === selection.flowArea.flowAreaIndex);
  if (!flowArea || !flowArea.hasMesh || flowAreaIndex !== flowArea.flowAreaIndex ||
      item.flowAreaID !== flowArea.flowAreaID || item.flowAreaName !== flowArea.name || meshHash !== flowArea.meshHash ||
      item.elementKind !== selection.elementKind || item.elementIndex !== selection.elementIndex ||
      item.variable !== expectedVariable) {
    throw new Error('The mesh time-series receipt no longer matches the inspected Flow Area and element.');
  }
  const expectedCount = selection.elementKind === 'Cell' ? flowArea.cellCount : flowArea.faceCount;
  if (selection.elementIndex >= expectedCount) throw new Error('The requested mesh element is outside the inspected source mesh.');
  const units = nonEmptyString(item.units, 'units');
  if (!Array.isArray(item.series)) throw new Error('The mesh time-series result has no series list.');
  const entries = item.series.map((entry, index) => parseSeriesEntry(entry, index, capture, flowArea, expectedVariable, units));
  if ((item.state === 'ready' && entries.length === 0) || (item.state !== 'ready' && entries.length !== 0)) {
    throw new Error('The mesh time-series state does not match the returned series.');
  }
  return {
    operationID: nonEmptyString(item.operationID, 'operationID'),
    state: item.state,
    message: nullableString(item.message, 'message'),
    flowAreaIndex,
    flowAreaID: flowArea.flowAreaID,
    flowAreaName: flowArea.name,
    meshHash,
    elementKind: selection.elementKind,
    elementIndex: selection.elementIndex,
    variable: expectedVariable,
    units,
    entries,
    skippedComputingResultCount: nonNegativeInteger(item.skippedComputingResultCount, 'skippedComputingResultCount'),
  };
}

interface OrderedTimeSlot {
  key: string;
  time: string;
  order: number;
  indegree: number;
  next: Set<string>;
}

function pushMinHeap(heap: OrderedTimeSlot[], value: OrderedTimeSlot): void {
  let index = heap.length;
  heap.push(value);
  while (index > 0) {
    const parent = Math.floor((index - 1) / 2);
    if ((heap[parent]?.order ?? Number.POSITIVE_INFINITY) <= value.order) break;
    heap[index] = heap[parent]!;
    index = parent;
  }
  heap[index] = value;
}

function popMinHeap(heap: OrderedTimeSlot[]): OrderedTimeSlot | undefined {
  if (heap.length === 0) return undefined;
  const first = heap[0];
  const last = heap.pop();
  if (heap.length > 0 && last) {
    let index = 0;
    while (true) {
      const left = index * 2 + 1;
      const right = left + 1;
      if (left >= heap.length) break;
      const child = right < heap.length && (heap[right]?.order ?? Number.POSITIVE_INFINITY) < (heap[left]?.order ?? Number.POSITIVE_INFINITY) ? right : left;
      if ((heap[child]?.order ?? Number.POSITIVE_INFINITY) >= last.order) break;
      heap[index] = heap[child]!;
      index = child;
    }
    heap[index] = last;
  }
  return first;
}

/** Aligns only equal raw timestamps (and their occurrence numbers); it never parses, shifts, or interpolates time. */
export function createMeshTimeSeriesChartData(
  execution: MeshTimeSeriesExecution,
  language: 'zh' | 'en',
): MeshTimeSeriesChartData {
  const slotByID = new Map<string, OrderedTimeSlot>();
  const valuesByEntry = execution.entries.map((entry) => {
    const occurrence = new Map<string, number>();
    const values = new Map<string, number | null>();
    let priorKey: string | null = null;
    entry.times.forEach((time, index) => {
      const ordinal = occurrence.get(time) ?? 0;
      occurrence.set(time, ordinal + 1);
      const key = JSON.stringify([time, ordinal]);
      if (!slotByID.has(key)) slotByID.set(key, { key, time, order: slotByID.size, indegree: 0, next: new Set<string>() });
      values.set(key, entry.values[index] ?? null);
      if (priorKey !== null && priorKey !== key) {
        const prior = slotByID.get(priorKey)!;
        if (!prior.next.has(key)) {
          prior.next.add(key);
          slotByID.get(key)!.indegree += 1;
        }
      }
      priorKey = key;
    });
    return { entry, values };
  });

  const heap: OrderedTimeSlot[] = [];
  for (const slot of slotByID.values()) {
    if (slot.indegree === 0) pushMinHeap(heap, slot);
  }
  const ordered: string[] = [];
  while (heap.length > 0) {
    const slot = popMinHeap(heap)!;
    ordered.push(slot.key);
    for (const nextKey of slot.next) {
      const next = slotByID.get(nextKey)!;
      next.indegree -= 1;
      if (next.indegree === 0) pushMinHeap(heap, next);
    }
  }
  if (ordered.length !== slotByID.size) {
    throw new Error('The returned Result time axes have conflicting sample order; no samples were reordered or interpolated.');
  }
  const times = ordered.map((key) => slotByID.get(key)!.time);
  const series: ResultSeries[] = valuesByEntry.map(({ entry, values }) => ({
    id: JSON.stringify([entry.resultPath, entry.resultRevision, entry.owners.map(meshLocatorKey)]),
    name: entry.resultName,
    units: entry.units,
    elementIndex: execution.elementIndex,
    values: ordered.map((key) => values.has(key) ? values.get(key)! : null),
  }));
  const elementLabel = execution.elementKind === 'Cell'
    ? language === 'zh' ? '单元' : 'Cell'
    : language === 'zh' ? '面' : 'Face';
  const label = language === 'zh' ? '地图 Result' : 'Map Result';
  return {
    dataset: {
      name: `${execution.flowAreaName || (language === 'zh' ? `Flow Area ${execution.flowAreaIndex + 1}` : `Flow Area ${execution.flowAreaIndex + 1}`)} · ${elementLabel} ${execution.elementIndex}`,
      times,
      series,
      source: 'backend-result',
      sourceLabel: label,
    },
    series,
  };
}
