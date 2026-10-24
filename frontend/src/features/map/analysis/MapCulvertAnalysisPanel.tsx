import { useEffect, useRef, useState, type CSSProperties } from 'react';
import { sameMapLocator, sameMapID } from '../associatedMapLayers';
import { mapError } from '../mapText';
import type { CatalogTreeNode, CatalogTreeNodeLocator } from '../mapTypes';
import type { MapAnalysisPanelProps, MapAnalysisResultChoice, MapResultAnalysisCapture } from './mapAnalysisTypes';

interface CulvertCoordinate { x: number; y: number }

interface CulvertBarrel {
  id: string;
  sourceOrdinal: number;
  name: string;
  coordinates: CulvertCoordinate[];
  barrelPropertyName: string;
  upstreamInvert: string;
  downstreamInvert: string;
  upstreamOpeningName: string;
  downstreamOpeningName: string;
}

interface CulvertProfileSnapshot {
  projectCanonicalPath: string;
  projectRevision: string;
  projectXmlSHA: string;
  visualizationRevision: string;
  mapID: string;
  mapIndex: number;
  mapName: string;
  mapType: string;
  contextToken: string;
  resultNode: CatalogTreeNodeLocator;
  selectionIndex: number;
  selectionMode: string;
  selectedResultPath: string;
  selectedResultRevision: string;
  selectedMapType: string | null;
  profileIndex: number;
  profileCount: number;
  profileNames: string[];
  geometryName: string | null;
  geometryPath: string;
  geometryRevision: string;
  geometryOwnerNode: CatalogTreeNodeLocator | null;
  geometryAvailable: boolean;
  outputAvailable: boolean;
  diagnostic: string | null;
  barrels: CulvertBarrel[];
  flow: number[];
  outsideDepth: number[];
  outletVelocity: number[];
}

interface CulvertTimeSeriesDataset {
  id: string;
  name: string;
  sourceOrdinal: number;
  colorRGBA: string;
  strokeWidth: number;
  values: number[];
}

interface CulvertTimeSeriesResult {
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
  resultNode: CatalogTreeNodeLocator;
  selectionIndex: number;
  selectedResultPath: string;
  selectedResultRevision: string;
  variable: 'Flow' | 'Velocity';
  units: string;
  times: string[];
  noDataValue: number;
  datasets: CulvertTimeSeriesDataset[];
}

interface ProfileRequestContext {
  capture: MapResultAnalysisCapture;
  selectedResult: MapAnalysisResultChoice;
  identity: string;
  request: Record<string, unknown>;
}

interface LoadState<T> {
  identity: string;
  status: 'loading' | 'ready' | 'error' | 'unavailable';
  value?: T;
  message?: string;
}

function record(value: unknown, label: string): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error(`${label} is not an object.`);
  return value as Record<string, unknown>;
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

function boolean(value: unknown, label: string): boolean {
  if (typeof value !== 'boolean') throw new Error(`${label} is not a boolean.`);
  return value;
}

function numberArray(value: unknown, label: string): number[] {
  if (!Array.isArray(value)) throw new Error(`${label} is not an array.`);
  return value.map((item, index) => finiteNumber(item, `${label}[${index}]`));
}

function stringArray(value: unknown, label: string): string[] {
  if (!Array.isArray(value)) throw new Error(`${label} is not an array.`);
  return value.map((item, index) => string(item, `${label}[${index}]`));
}

function parseLocator(value: unknown, label: string): CatalogTreeNodeLocator {
  const item = record(value, label);
  const rootName = nonEmptyString(item.rootName, `${label}.rootName`);
  const nodeType = nonEmptyString(item.nodeType, `${label}.nodeType`);
  const nodeName = nonEmptyString(item.nodeName, `${label}.nodeName`);
  if (item.nodePath !== undefined && item.nodePath !== null &&
      (!Array.isArray(item.nodePath) || item.nodePath.some(index => !Number.isInteger(index) || index < 0))) {
    throw new Error(`${label}.nodePath is invalid.`);
  }
  return { ...item, rootName, nodeType, nodeName } as CatalogTreeNodeLocator;
}

function parseBarrel(value: unknown, index: number): CulvertBarrel {
  const item = record(value, `barrels[${index}]`);
  if (!Array.isArray(item.coordinates)) throw new Error(`barrels[${index}].coordinates is not an array.`);
  return {
    id: nonEmptyString(item.id, `barrels[${index}].id`),
    sourceOrdinal: integer(item.sourceOrdinal, `barrels[${index}].sourceOrdinal`),
    name: string(item.name, `barrels[${index}].name`),
    coordinates: item.coordinates.map((value, coordinateIndex) => {
      const coordinate = record(value, `barrels[${index}].coordinates[${coordinateIndex}]`);
      return {
        x: finiteNumber(coordinate.x, `barrels[${index}].coordinates[${coordinateIndex}].x`),
        y: finiteNumber(coordinate.y, `barrels[${index}].coordinates[${coordinateIndex}].y`),
      };
    }),
    barrelPropertyName: string(item.barrelPropertyName, `barrels[${index}].barrelPropertyName`),
    upstreamInvert: string(item.upstreamInvert, `barrels[${index}].upstreamInvert`),
    downstreamInvert: string(item.downstreamInvert, `barrels[${index}].downstreamInvert`),
    upstreamOpeningName: string(item.upstreamOpeningName, `barrels[${index}].upstreamOpeningName`),
    downstreamOpeningName: string(item.downstreamOpeningName, `barrels[${index}].downstreamOpeningName`),
  };
}

function parseProfile(value: unknown): CulvertProfileSnapshot {
  const item = record(value, 'culvert profile response');
  if (!Array.isArray(item.barrels)) throw new Error('The culvert profile response has no barrel list.');
  const geometryOwnerNode = item.geometryOwnerNode === null ? null : parseLocator(item.geometryOwnerNode, 'geometryOwnerNode');
  const profile: CulvertProfileSnapshot = {
    projectCanonicalPath: nonEmptyString(item.projectCanonicalPath, 'projectCanonicalPath'),
    projectRevision: nonEmptyString(item.projectRevision, 'projectRevision'),
    projectXmlSHA: nonEmptyString(item.projectXmlSHA, 'projectXmlSHA'),
    visualizationRevision: nonEmptyString(item.visualizationRevision, 'visualizationRevision'),
    mapID: nonEmptyString(item.mapID, 'mapID'),
    mapIndex: integer(item.mapIndex, 'mapIndex'),
    mapName: string(item.mapName, 'mapName'),
    mapType: string(item.mapType, 'mapType'),
    contextToken: nonEmptyString(item.contextToken, 'contextToken'),
    resultNode: parseLocator(item.resultNode, 'resultNode'),
    selectionIndex: integer(item.selectionIndex, 'selectionIndex'),
    selectionMode: nonEmptyString(item.selectionMode, 'selectionMode'),
    selectedResultPath: nonEmptyString(item.selectedResultPath, 'selectedResultPath'),
    selectedResultRevision: nonEmptyString(item.selectedResultRevision, 'selectedResultRevision'),
    selectedMapType: nullableString(item.selectedMapType, 'selectedMapType'),
    profileIndex: integer(item.profileIndex, 'profileIndex'),
    profileCount: integer(item.profileCount, 'profileCount'),
    profileNames: stringArray(item.profileNames, 'profileNames'),
    geometryName: nullableString(item.geometryName, 'geometryName'),
    geometryPath: nonEmptyString(item.geometryPath, 'geometryPath'),
    geometryRevision: nonEmptyString(item.geometryRevision, 'geometryRevision'),
    geometryOwnerNode,
    geometryAvailable: boolean(item.geometryAvailable, 'geometryAvailable'),
    outputAvailable: boolean(item.outputAvailable, 'outputAvailable'),
    diagnostic: nullableString(item.diagnostic, 'diagnostic'),
    barrels: item.barrels.map(parseBarrel),
    flow: numberArray(item.flow, 'flow'),
    outsideDepth: numberArray(item.outsideDepth, 'outsideDepth'),
    outletVelocity: numberArray(item.outletVelocity, 'outletVelocity'),
  };
  if (profile.profileCount < 0 || profile.profileIndex < 0 || profile.profileNames.length !== profile.profileCount) {
    throw new Error('The culvert profile metadata does not match the requested frame.');
  }
  if (profile.profileCount > 0 && profile.profileIndex >= profile.profileCount) {
    throw new Error('The returned culvert profile index is outside its stored range.');
  }
  const barrelIDs = new Set<string>();
  profile.barrels.forEach((barrel, index) => {
    if (barrel.sourceOrdinal !== index || barrel.id !== `barrel:source:${barrel.sourceOrdinal}` || barrelIDs.has(barrel.id)) {
      throw new Error('The returned culvert barrels do not preserve source-ordinal identities.');
    }
    if (barrel.coordinates.length < 2) throw new Error('A returned culvert barrel has no usable source coordinates.');
    barrelIDs.add(barrel.id);
  });
  if (profile.geometryAvailable && !profile.geometryOwnerNode) throw new Error('The culvert geometry has no exact owner node.');
  if (profile.geometryOwnerNode && profile.geometryOwnerNode.nodeType !== 'Geometry') {
    throw new Error('The returned culvert geometry owner is not a Geometry node.');
  }
  if (profile.geometryAvailable && profile.outputAvailable &&
      (profile.flow.length !== profile.barrels.length || profile.outsideDepth.length !== profile.barrels.length * 2 ||
       profile.outletVelocity.length !== profile.barrels.length)) {
    throw new Error('The stored culvert profile arrays do not match the source barrel geometry.');
  }
  return profile;
}

function parseTimeSeries(value: unknown): CulvertTimeSeriesResult {
  const item = record(value, 'culvert time-series response');
  if (!Array.isArray(item.datasets)) throw new Error('The culvert time-series response has no datasets.');
  const datasets = item.datasets.map((value, index): CulvertTimeSeriesDataset => {
    const dataset = record(value, `datasets[${index}]`);
    return {
      id: nonEmptyString(dataset.id, `datasets[${index}].id`),
      name: string(dataset.name, `datasets[${index}].name`),
      sourceOrdinal: integer(dataset.sourceOrdinal, `datasets[${index}].sourceOrdinal`),
      colorRGBA: nonEmptyString(dataset.colorRGBA, `datasets[${index}].colorRGBA`),
      strokeWidth: finiteNumber(dataset.strokeWidth, `datasets[${index}].strokeWidth`),
      values: numberArray(dataset.values, `datasets[${index}].values`),
    };
  });
  const variable = string(item.variable, 'variable');
  if (variable !== 'Flow' && variable !== 'Velocity') throw new Error('The culvert time-series variable is unsupported.');
  return {
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
    resultNode: parseLocator(item.resultNode, 'resultNode'),
    selectionIndex: integer(item.selectionIndex, 'selectionIndex'),
    selectedResultPath: nonEmptyString(item.selectedResultPath, 'selectedResultPath'),
    selectedResultRevision: nonEmptyString(item.selectedResultRevision, 'selectedResultRevision'),
    variable,
    units: string(item.units, 'units'),
    times: stringArray(item.times, 'times'),
    noDataValue: finiteNumber(item.noDataValue, 'noDataValue'),
    datasets,
  };
}

function canonicalPath(value: string): string {
  return value.replace(/\\/g, '/').replace(/\/+$/, '');
}

function sameRevision(left: string, right: string): boolean {
  return left.toUpperCase() === right.toUpperCase();
}

function sameResultChoice(
  left: MapAnalysisResultChoice | null,
  right: MapAnalysisResultChoice | null,
): boolean {
  return left !== null && right !== null && left.resultPath === right.resultPath && sameRevision(left.revision, right.revision);
}

function findCapturedNode(nodes: readonly CatalogTreeNode[], locator: CatalogTreeNodeLocator): CatalogTreeNode | null {
  for (const node of nodes) {
    if (node.locator && sameMapLocator(node.locator, locator)) return node;
    const child = node.children?.length ? findCapturedNode(node.children, locator) : null;
    if (child) return child;
  }
  return null;
}

function requestContext(capture: MapResultAnalysisCapture | null): ProfileRequestContext | null {
  if (!capture || !capture.captureKey || !capture.ownerKey || !capture.contextToken) return null;
  const selectedResult = capture.slotZero?.selectedResult;
  const selectedSlot = capture.activeNode.selections.find(selection => selection.selectionIndex === 0);
  const nodeType = capture.activeNode.node.nodeType;
  const frame = capture.renderSnapshot;
  if (!selectedResult || !selectedSlot?.selectedResult || selectedSlot.selectionMode !== capture.slotZero?.selectionMode ||
      !sameResultChoice(selectedResult, selectedSlot.selectedResult) ||
      capture.slotZero?.selectionIndex !== 0 || capture.activeNode.nodeKind !== nodeType ||
      (nodeType !== 'Result' && nodeType !== 'MultiResult') || selectedResult.isComputing || selectedSlot.selectedResult.isComputing ||
      capture.profileIndex === null || !Number.isInteger(capture.profileIndex) || capture.profileIndex < 0 ||
      capture.activeNode.profileIndex !== capture.profileIndex || !findCapturedNode(capture.treeRoots, capture.activeNode.node) ||
      !frame || frame.contextToken !== capture.contextToken || frame.selectedProfileIndex !== capture.profileIndex ||
      (capture.profileTime !== null && frame.selectedProfileTime !== null && capture.profileTime !== frame.selectedProfileTime) || !frame.sourceID) {
    return null;
  }
  const map = capture.map;
  if (!map.projectPath || !map.projectXml || !map.projectXmlSHA || !map.projectSessionID ||
      !capture.projectRevision || !capture.projectXmlSHA || !capture.visualizationRevision ||
      !map.expectedVisualizationRevision || !map.mapID || !Number.isInteger(map.mapIndex) ||
      !sameRevision(map.projectXmlSHA, capture.projectXmlSHA) ||
      !sameRevision(map.expectedVisualizationRevision, capture.visualizationRevision)) return null;
  const coreLayerContextIdentity = expectedCoreLayerIdentity(capture);
  if (coreLayerContextIdentity === null) return null;
  const identity = JSON.stringify({
    ownerKey: capture.ownerKey,
    captureKey: capture.captureKey,
    projectPath: map.projectPath,
    projectRevision: capture.projectRevision,
    projectXmlSHA: capture.projectXmlSHA,
    projectSessionID: map.projectSessionID,
    visualizationRevision: capture.visualizationRevision,
    coreLayerContextIdentity,
    mapID: map.mapID,
    mapIndex: map.mapIndex,
    mapName: map.mapName,
    mapType: map.mapType,
    contextToken: capture.contextToken,
    resultNode: capture.activeNode.node,
    selectionMode: capture.slotZero.selectionMode,
    selectedResultPath: selectedResult.resultPath,
    selectedResultRevision: selectedResult.revision,
    profileIndex: capture.profileIndex,
    profileTime: capture.profileTime,
    renderSnapshot: frame,
  });
  return {
    capture,
    selectedResult,
    identity,
    request: {
      map,
      contextToken: capture.contextToken,
      selectionOverrides: capture.selectionOverrides,
      resultNode: capture.activeNode.node,
      selectionIndex: 0,
      profileIndex: capture.profileIndex,
    },
  };
}

function expectedCoreLayerIdentity(capture: MapResultAnalysisCapture): string | null {
  const map = capture.map as Record<string, unknown>;
  const context = map.coreLayerContext;
  if (context === undefined || context === null) return '';
  if (typeof context !== 'object' || Array.isArray(context)) return null;
  const identity = (context as Record<string, unknown>).identityToken;
  return typeof identity === 'string' && identity.trim() ? identity : null;
}

function validateProfile(profile: CulvertProfileSnapshot, context: ProfileRequestContext): void {
  const { capture, selectedResult } = context;
  const map = capture.map;
  const selectedNode = findCapturedNode(capture.treeRoots, capture.activeNode.node);
  const geometryRows = selectedNode?.children?.filter(node => node.locator?.nodeType === 'Geometry') ?? [];
  const expectedGeometry = geometryRows.length === 1 ? geometryRows[0].locator ?? null : null;
  if (canonicalPath(profile.projectCanonicalPath) !== canonicalPath(map.projectPath) ||
      !sameRevision(profile.projectRevision, capture.projectRevision) ||
      !sameRevision(profile.projectXmlSHA, capture.projectXmlSHA) ||
      !sameRevision(profile.visualizationRevision, capture.visualizationRevision) ||
      !sameMapID(profile.mapID, map.mapID) || profile.mapIndex !== map.mapIndex ||
      profile.mapName !== map.mapName || profile.mapType !== map.mapType || profile.contextToken !== capture.contextToken ||
      !sameMapLocator(profile.resultNode, capture.activeNode.node) || profile.selectionIndex !== 0 ||
      profile.selectionMode !== capture.slotZero?.selectionMode ||
      canonicalPath(profile.selectedResultPath) !== canonicalPath(selectedResult.resultPath) ||
      !sameRevision(profile.selectedResultRevision, selectedResult.revision) ||
      profile.profileIndex !== capture.profileIndex ||
      profile.selectedMapType !== capture.activeNode.selectedMapType ||
      !expectedGeometry || !profile.geometryOwnerNode || !sameMapLocator(profile.geometryOwnerNode, expectedGeometry) ||
      profile.geometryName !== expectedGeometry.nodeName ||
      canonicalPath(profile.geometryPath) !== canonicalPath(selectedResult.resultPath) ||
      !sameRevision(profile.geometryRevision, selectedResult.revision)) {
    throw new Error('The culvert profile response no longer matches the captured map, Result owner, revision, or frame.');
  }
}

function validateTimeSeries(
  result: CulvertTimeSeriesResult,
  context: ProfileRequestContext,
  profile: CulvertProfileSnapshot,
  variable: 'Flow' | 'Velocity',
  requestedIDs: string[],
): void {
  const { capture, selectedResult } = context;
  const map = capture.map;
  if (canonicalPath(result.projectCanonicalPath) !== canonicalPath(map.projectPath) ||
      !sameRevision(result.projectRevision, capture.projectRevision) ||
      !sameRevision(result.projectRevision, profile.projectRevision) ||
      !sameRevision(result.projectXmlSHA, capture.projectXmlSHA) ||
      !sameRevision(result.visualizationRevision, capture.visualizationRevision) ||
      result.coreLayerContextIdentity !== expectedCoreLayerIdentity(capture) ||
      !sameMapID(result.mapID, map.mapID) || result.mapIndex !== map.mapIndex ||
      result.mapName !== map.mapName || result.mapType !== map.mapType || result.contextToken !== capture.contextToken ||
      !sameMapLocator(result.resultNode, capture.activeNode.node) || result.selectionIndex !== 0 ||
      canonicalPath(result.selectedResultPath) !== canonicalPath(selectedResult.resultPath) ||
      !sameRevision(result.selectedResultRevision, selectedResult.revision) || result.variable !== variable ||
      result.times.length === 0 || result.datasets.length !== requestedIDs.length) {
    throw new Error('The culvert time-series response no longer matches the captured Result source and selection.');
  }
  const barrelByID = new Map(profile.barrels.map(barrel => [barrel.id, barrel]));
  result.datasets.forEach((dataset, index) => {
    const requestedID = requestedIDs[index];
    const barrel = barrelByID.get(requestedID);
    if (!barrel || dataset.id !== requestedID || dataset.sourceOrdinal !== barrel.sourceOrdinal ||
        dataset.name !== barrel.name || dataset.values.length !== result.times.length || dataset.strokeWidth <= 0) {
      throw new Error('The culvert time-series datasets do not match the selected source barrels.');
    }
  });
}

function raw(value: number | string | null): string {
  return value === null ? '—' : String(value);
}

function safeSeriesColor(value: string): string {
  const normalized = value.replace(/^#/, '');
  if (/^[\da-f]{8}$/i.test(normalized)) return `#${normalized.slice(2)}`;
  if (/^[\da-f]{6}$/i.test(normalized)) return `#${normalized}`;
  return '#3488a5';
}

function buildSeriesSegments(values: number[], noDataValue: number): number[][] {
  const segments: number[][] = [];
  let current: number[] = [];
  values.forEach((value, index) => {
    if (value === noDataValue) {
      if (current.length) segments.push(current);
      current = [];
    } else {
      current.push(index);
    }
  });
  if (current.length) segments.push(current);
  return segments;
}

function CulvertSeriesChart({ result, language }: { result: CulvertTimeSeriesResult; language: 'zh' | 'en' }) {
  const width = 920;
  const height = 260;
  const margin = { top: 16, right: 20, bottom: 38, left: 74 };
  const plotWidth = width - margin.left - margin.right;
  const plotHeight = height - margin.top - margin.bottom;
  const validValues = result.datasets.flatMap(dataset => dataset.values.filter(value => value !== result.noDataValue));
  if (!validValues.length) {
    return <p className="map-page-notice" role="status">{language === 'zh' ? '所选时序全部为 NoData，没有可绘制的数值。' : 'All selected time-series values are NoData; there are no numeric points to plot.'}</p>;
  }
  const { minimum: observedMin, maximum: observedMax } = validValues.reduce((range, value) => ({
    minimum: Math.min(range.minimum, value),
    maximum: Math.max(range.maximum, value),
  }), { minimum: Number.POSITIVE_INFINITY, maximum: Number.NEGATIVE_INFINITY });
  const padding = observedMin === observedMax ? Math.max(Math.abs(observedMin) * 0.05, 1) : (observedMax - observedMin) * 0.06;
  const minimum = observedMin - padding;
  const maximum = observedMax + padding;
  const xAt = (index: number) => margin.left + (result.times.length <= 1 ? 0 : index / (result.times.length - 1)) * plotWidth;
  const yAt = (value: number) => margin.top + (maximum - value) / (maximum - minimum) * plotHeight;
  const grids = Array.from({ length: 5 }, (_, index) => {
    const fraction = index / 4;
    const y = margin.top + fraction * plotHeight;
    const value = maximum - fraction * (maximum - minimum);
    return { y, value };
  });
  return (
    <figure style={{ margin: 0 }}>
      <svg role="img" aria-label={language === 'zh' ? `涵洞${result.variable === 'Flow' ? '流量' : '出口流速'}时序图，单位 ${result.units}` : `Culvert ${result.variable} time series, units ${result.units}`} viewBox={`0 0 ${width} ${height}`} style={{ display: 'block', width: '100%', minHeight: 220, overflow: 'visible' }}>
        {grids.map((grid, index) => (
          <g key={index}>
            <line x1={margin.left} x2={width - margin.right} y1={grid.y} y2={grid.y} stroke="#dbe4ea" strokeDasharray="3 4" />
            <text x={margin.left - 8} y={grid.y + 4} textAnchor="end" fill="#526a7a" fontSize="10">{grid.value.toString()}</text>
          </g>
        ))}
        <line x1={margin.left} x2={width - margin.right} y1={height - margin.bottom} y2={height - margin.bottom} stroke="#78909f" />
        {result.datasets.map(dataset => {
          const color = safeSeriesColor(dataset.colorRGBA);
          const segments = buildSeriesSegments(dataset.values, result.noDataValue);
          return <g key={dataset.id}>
            {segments.map((segment, segmentIndex) => {
              const points = segment.map(index => `${xAt(index)},${yAt(dataset.values[index])}`);
              if (points.length === 1) {
                const [cx, cy] = points[0].split(',').map(Number);
                return <circle key={segmentIndex} cx={cx} cy={cy} r="3.2" fill={color} />;
              }
              return <polyline key={segmentIndex} points={points.join(' ')} fill="none" stroke={color} strokeWidth={Math.min(Math.max(dataset.strokeWidth, 1), 6)} strokeLinejoin="round" strokeLinecap="round" />;
            })}
          </g>;
        })}
        {result.times.length > 0 && <>
          <text x={margin.left} y={height - 10} textAnchor="start" fill="#526a7a" fontSize="9">{result.times[0]}</text>
          {result.times.length > 1 && <text x={width - margin.right} y={height - 10} textAnchor="end" fill="#526a7a" fontSize="9">{result.times[result.times.length - 1]}</text>}
        </>}
      </svg>
      <figcaption style={{ color: '#647987', fontSize: 11 }}>
        {language === 'zh' ? `缺测值 ${result.noDataValue} 会形成折线断点；横轴时间与单位按服务返回原样显示。` : `NoData sentinel ${result.noDataValue} creates line gaps; times and units are shown as returned.`}
      </figcaption>
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: '8px 16px', marginTop: 8 }}>
        {result.datasets.map(dataset => <span key={dataset.id} style={{ display: 'inline-flex', alignItems: 'center', gap: 6, fontSize: 11 }}>
          <i aria-hidden="true" style={{ width: 14, height: 3, background: safeSeriesColor(dataset.colorRGBA) }} />
          <span>{dataset.name || dataset.id}</span>
        </span>)}
      </div>
    </figure>
  );
}

function ProfileTable({ profile, language }: { profile: CulvertProfileSnapshot; language: 'zh' | 'en' }) {
  const canPairData = profile.geometryAvailable && profile.outputAvailable && profile.flow.length === profile.barrels.length;
  return (
    <div style={{ overflowX: 'auto', maxHeight: 380 }}>
      {profile.barrels.length > 0 ? <table style={{ borderCollapse: 'collapse', width: '100%', fontSize: 11 }}>
        <thead><tr>
          {(language === 'zh'
            ? ['源序号', '涵洞名称', '属性名', '上游底高程', '下游底高程', '上游开口', '下游开口', '剖面流量', '上游外部水深', '下游外部水深', '出口流速', '源坐标']
            : ['Source ordinal', 'Barrel name', 'Property name', 'Upstream invert', 'Downstream invert', 'Upstream opening', 'Downstream opening', 'Profile flow', 'Upstream outside depth', 'Downstream outside depth', 'Outlet velocity', 'Source coordinates']).map(title => <th key={title} style={{ padding: '7px 8px', borderBottom: '1px solid #d9e2e9', textAlign: 'left', whiteSpace: 'nowrap' }}>{title}</th>)}
        </tr></thead>
        <tbody>{profile.barrels.map((barrel, index) => <tr key={barrel.id} data-barrel-id={barrel.id}>
          <td style={cellStyle}><code>{barrel.sourceOrdinal}</code></td>
          <td style={cellStyle}>{barrel.name || '—'}<br /><code>{barrel.id}</code></td>
          <td style={cellStyle}>{barrel.barrelPropertyName || '—'}</td>
          <td style={cellStyle}>{barrel.upstreamInvert}</td>
          <td style={cellStyle}>{barrel.downstreamInvert}</td>
          <td style={cellStyle}>{barrel.upstreamOpeningName || '—'}</td>
          <td style={cellStyle}>{barrel.downstreamOpeningName || '—'}</td>
          <td style={cellStyle}>{canPairData ? raw(profile.flow[index]) : '—'}</td>
          <td style={cellStyle}>{canPairData ? raw(profile.outsideDepth[index * 2]) : '—'}</td>
          <td style={cellStyle}>{canPairData ? raw(profile.outsideDepth[index * 2 + 1]) : '—'}</td>
          <td style={cellStyle}>{canPairData ? raw(profile.outletVelocity[index]) : '—'}</td>
          <td style={cellStyle}>
            <details>
              <summary>{language === 'zh' ? `${barrel.coordinates.length} 个坐标点` : `${barrel.coordinates.length} coordinate points`}</summary>
              <ol style={{ margin: '4px 0', paddingInlineStart: 22 }}>
                {barrel.coordinates.map((coordinate, coordinateIndex) => <li key={coordinateIndex}><code>{coordinate.x}, {coordinate.y}</code></li>)}
              </ol>
            </details>
          </td>
        </tr>)}</tbody>
      </table> : <p className="map-page-notice">{language === 'zh' ? '当前 Result 几何中没有可读取的涵洞 barrel。' : 'The current Result geometry contains no readable culvert barrels.'}</p>}
    </div>
  );
}

const cellStyle: CSSProperties = { padding: '6px 8px', borderBottom: '1px solid #e8edf0', verticalAlign: 'top', whiteSpace: 'nowrap' };

/** Reads exact Result slot-zero culvert geometry and stored Flow/Velocity output. */
export function MapCulvertAnalysisPanel({ backend, language, capture }: MapAnalysisPanelProps) {
  const zh = language === 'zh';
  const context = requestContext(capture);
  const profileIdentity = context?.identity ?? null;
  const profileIdentityRef = useRef(profileIdentity);
  profileIdentityRef.current = profileIdentity;
  const captureRef = useRef(capture);
  captureRef.current = capture;
  const languageRef = useRef(language);
  languageRef.current = language;
  const [profileState, setProfileState] = useState<LoadState<CulvertProfileSnapshot> | null>(null);
  const [timeState, setTimeState] = useState<LoadState<CulvertTimeSeriesResult> | null>(null);
  const [selectedBarrelsState, setSelectedBarrelsState] = useState<{ identity: string; ids: string[] } | null>(null);
  const [variable, setVariable] = useState<'Flow' | 'Velocity'>('Flow');
  const [profileAttempt, setProfileAttempt] = useState(0);
  const [capabilityRevision, setCapabilityRevision] = useState(0);
  const profileController = useRef<AbortController | null>(null);
  const timeController = useRef<AbortController | null>(null);

  useEffect(() => backend.subscribeCapabilities?.(() => setCapabilityRevision(value => value + 1)), [backend]);

  useEffect(() => {
    profileController.current?.abort();
    profileController.current = null;
    if (!profileIdentity) return;
    const requestContextValue = requestContext(captureRef.current);
    if (!requestContextValue || requestContextValue.identity !== profileIdentity) return;
    if (!backend.supports('map.result.culvertProfile')) {
      setProfileState({ identity: profileIdentity, status: 'unavailable' });
      return;
    }
    const controller = new AbortController();
    profileController.current = controller;
    setProfileState({ identity: profileIdentity, status: 'loading' });
    void backend.request<unknown>('map.result.culvertProfile', requestContextValue.request, { signal: controller.signal })
      .then(parseProfile)
      .then(profile => {
        validateProfile(profile, requestContextValue);
        if (controller.signal.aborted || profileIdentityRef.current !== profileIdentity) return;
        setProfileState({ identity: profileIdentity, status: 'ready', value: profile });
        setSelectedBarrelsState(current => current?.identity === profileIdentity
          ? { identity: profileIdentity, ids: current.ids.filter(id => profile.barrels.some(barrel => barrel.id === id)) }
          : { identity: profileIdentity, ids: profile.barrels.map(barrel => barrel.id) });
      })
      .catch(cause => {
        if (controller.signal.aborted || profileIdentityRef.current !== profileIdentity) return;
        setProfileState({ identity: profileIdentity, status: 'error', message: mapError(languageRef.current, cause) });
      });
    return () => {
      controller.abort();
      if (profileController.current === controller) profileController.current = null;
    };
  }, [backend, capabilityRevision, profileIdentity, profileAttempt]);

  const profile = profileState?.identity === profileIdentity && profileState.status === 'ready'
    ? profileState.value ?? null
    : null;
  const selectedIDs = selectedBarrelsState?.identity === profileIdentity ? selectedBarrelsState.ids : [];
  const selectedSet = new Set(selectedIDs);
  const selectedBarrelIDs = profile?.barrels.filter(barrel => selectedSet.has(barrel.id)).map(barrel => barrel.id) ?? [];
  const timeSeriesIdentity = profile && profileIdentity
    ? JSON.stringify([profileIdentity, variable, selectedBarrelIDs])
    : null;
  const timeSeriesIdentityRef = useRef(timeSeriesIdentity);
  timeSeriesIdentityRef.current = timeSeriesIdentity;

  useEffect(() => {
    timeController.current?.abort();
    timeController.current = null;
    return () => {
      timeController.current?.abort();
      timeController.current = null;
    };
  }, [timeSeriesIdentity]);

  const loadTimeSeries = async (): Promise<void> => {
    const currentCapture = requestContext(captureRef.current);
    const currentProfileState = profileState;
    const currentProfile = currentProfileState?.identity === profileIdentity && currentProfileState.status === 'ready'
      ? currentProfileState.value
      : null;
    if (!currentCapture || currentCapture.identity !== profileIdentity || !currentProfile || !timeSeriesIdentity ||
        selectedBarrelIDs.length === 0 || !backend.supports('map.result.culvertTimeSeries')) return;
    const requestedIDs = [...selectedBarrelIDs];
    const requestIdentity = timeSeriesIdentity;
    timeController.current?.abort();
    const controller = new AbortController();
    timeController.current = controller;
    setTimeState({ identity: requestIdentity, status: 'loading' });
    try {
      const request = {
        map: currentCapture.capture.map,
        resultOwner: {
          resultNode: currentCapture.capture.activeNode.node,
          contextToken: currentCapture.capture.contextToken,
          selectionOverrides: currentCapture.capture.selectionOverrides,
          selectionIndex: 0,
          selectedResultPath: currentCapture.selectedResult.resultPath,
          selectedResultRevision: currentCapture.selectedResult.revision,
        },
        barrelIDs: requestedIDs,
        variable,
      };
      const result = parseTimeSeries(await backend.request<unknown>('map.result.culvertTimeSeries', request, { signal: controller.signal }));
      validateTimeSeries(result, currentCapture, currentProfile, variable, requestedIDs);
      if (controller.signal.aborted || profileIdentityRef.current !== profileIdentity || timeSeriesIdentityRef.current !== requestIdentity) return;
      setTimeState({ identity: requestIdentity, status: 'ready', value: result });
    } catch (cause) {
      if (controller.signal.aborted || profileIdentityRef.current !== profileIdentity || timeSeriesIdentityRef.current !== requestIdentity) return;
      setTimeState({ identity: requestIdentity, status: 'error', message: mapError(languageRef.current, cause) });
    }
  };

  const timeSeries = timeState?.identity === timeSeriesIdentity && timeState.status === 'ready' ? timeState.value ?? null : null;
  const timeLoading = timeState?.identity === timeSeriesIdentity && timeState.status === 'loading';
  const timeError = timeState?.identity === timeSeriesIdentity && timeState.status === 'error' ? timeState.message : null;

  const toggleBarrel = (id: string, checked: boolean): void => {
    if (!profile || !profile.barrels.some(barrel => barrel.id === id)) return;
    setSelectedBarrelsState(current => {
      const ids = current?.identity === profileIdentity ? current.ids : [];
      return { identity: profileIdentity ?? '', ids: checked ? [...new Set([...ids, id])] : ids.filter(candidate => candidate !== id) };
    });
  };

  const selectAllBarrels = (): void => {
    if (profile && profileIdentity) setSelectedBarrelsState({ identity: profileIdentity, ids: profile.barrels.map(barrel => barrel.id) });
  };

  const clearBarrels = (): void => {
    if (profile && profileIdentity) setSelectedBarrelsState({ identity: profileIdentity, ids: [] });
  };

  const requestIsReady = Boolean(context && profileIdentity && profile && profile.geometryAvailable && profile.barrels.length > 0);
  const supportsTimeSeries = backend.supports('map.result.culvertTimeSeries');

  return (
    <section className="map-inspector-section" aria-label={zh ? '涵洞结果分析' : 'Culvert Result analysis'} data-testid="map-culvert-analysis-panel" style={{ padding: '10px 12px 14px' }}>
      <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', gap: 10 }}>
        <h3 style={{ margin: '0 0 8px', fontSize: 13 }}>{zh ? '涵洞结果分析' : 'Culvert Result analysis'}</h3>
        {context && <small style={{ color: '#6e8290' }}>{context.capture.activeNode.name} · {context.capture.profileTime ?? ''}</small>}
      </div>
      {!context && <p className="map-page-notice" role="status">{zh
        ? '请选择已加载的 Result 或 MultiResult，并等待当前时间帧完成读取后查看涵洞数据。'
        : 'Select a loaded Result or MultiResult and wait for its current frame before viewing culvert data.'}</p>}
      {context && profileState?.identity === profileIdentity && profileState.status === 'unavailable' && <p className="map-page-notice" role="status">{zh ? '当前工作环境暂不支持读取涵洞剖面。' : 'Culvert profile reading is unavailable in the current workspace.'}</p>}
      {context && profileState?.identity === profileIdentity && profileState.status === 'loading' && <p className="map-page-notice" role="status">{zh ? '正在读取当前 Result 的涵洞几何与剖面…' : 'Reading culvert geometry and profile from the current Result…'}</p>}
      {context && profileState?.identity === profileIdentity && profileState.status === 'error' && <div className="map-page-notice map-error-notice" role="alert">
        <span>!</span><div>{zh ? '涵洞剖面读取失败。' : 'Could not read the culvert profile.'}<details><summary>{zh ? '查看诊断' : 'View diagnostic'}</summary><pre style={{ whiteSpace: 'pre-wrap' }}>{profileState.message}</pre></details></div>
        <button type="button" data-testid="map-culvert-profile-retry" onClick={() => setProfileAttempt(value => value + 1)}>{zh ? '重试' : 'Retry'}</button>
      </div>}
      {context && !backend.supports('map.result.culvertProfile') && profileState?.identity !== profileIdentity && <p className="map-page-notice" role="status">{zh ? '当前工作环境暂不支持读取涵洞剖面。' : 'Culvert profile reading is unavailable in the current workspace.'}</p>}
      {profile && <>
        <dl className="map-feature-properties" style={{ display: 'grid', gridTemplateColumns: 'minmax(100px, .7fr) minmax(0, 1.7fr)', gap: '4px 12px', margin: 0 }}>
          <dt>{zh ? '当前剖面' : 'Current profile'}</dt><dd style={definitionValueStyle}>{profile.profileNames[profile.profileIndex] ?? '—'} <small>({profile.profileIndex + 1}/{profile.profileCount || 0})</small></dd>
          <dt>{zh ? '几何名称' : 'Geometry name'}</dt><dd style={definitionValueStyle}>{profile.geometryName ?? '—'}</dd>
          <dt>{zh ? '几何来源' : 'Geometry source'}</dt><dd style={definitionValueStyle}><code>{profile.geometryPath}</code></dd>
          <dt>{zh ? '几何修订' : 'Geometry revision'}</dt><dd style={definitionValueStyle}><code>{profile.geometryRevision}</code></dd>
          {profile.geometryOwnerNode && <><dt>{zh ? '几何所属节点' : 'Geometry owner node'}</dt><dd style={definitionValueStyle}><code>{profile.geometryOwnerNode.rootName} / {profile.geometryOwnerNode.nodeType} / {profile.geometryOwnerNode.nodeName}</code></dd></>}
          <dt>{zh ? '嵌入几何' : 'Embedded geometry'}</dt><dd style={definitionValueStyle}>{profile.geometryAvailable ? (zh ? '可用' : 'Available') : (zh ? '不可用' : 'Unavailable')}</dd>
          <dt>{zh ? '剖面输出' : 'Profile output'}</dt><dd style={definitionValueStyle}>{profile.outputAvailable ? (zh ? '可用' : 'Available') : (zh ? '不可用' : 'Unavailable')}</dd>
        </dl>
        {profile.diagnostic && <p className="map-page-notice" role="status"><span>i</span><span>{profile.diagnostic}</span></p>}
        <details className="results-detail-extra" style={{ marginTop: 8 }}>
          <summary>{zh ? '当前 Result 来源身份' : 'Current Result source identity'}</summary>
          <dl>
            <div><dt>{zh ? 'Result 路径' : 'Result path'}</dt><dd><code>{profile.selectedResultPath}</code></dd></div>
            <div><dt>{zh ? 'Result 修订' : 'Result revision'}</dt><dd><code>{profile.selectedResultRevision}</code></dd></div>
            <div><dt>{zh ? '地图上下文令牌' : 'Map context token'}</dt><dd><code>{profile.contextToken}</code></dd></div>
            <div><dt>{zh ? '工程修订' : 'Project revision'}</dt><dd><code>{profile.projectRevision}</code></dd></div>
          </dl>
        </details>
        <div className="map-property-feature" style={{ margin: '8px 0 0', padding: 9 }}>
          <strong>{zh ? '当前剖面中的涵洞管节与源坐标属性' : 'Culvert barrels and source coordinates at this profile'}</strong>
          <ProfileTable profile={profile} language={language} />
          {profile.outputAvailable && (!profile.geometryAvailable || profile.flow.length !== profile.barrels.length) && <details style={{ marginTop: 8 }}>
            <summary>{zh ? '查看未关联到几何要素的原始输出数组' : 'View raw output arrays not paired with geometry features'}</summary>
            <pre style={{ maxHeight: 240, overflow: 'auto', whiteSpace: 'pre-wrap' }}>{JSON.stringify({ flow: profile.flow, outsideDepth: profile.outsideDepth, outletVelocity: profile.outletVelocity }, null, 2)}</pre>
          </details>}
        </div>
        <section style={{ marginTop: 12, paddingTop: 10, borderTop: '1px solid #dce4e9' }} aria-label={zh ? '涵洞时间序列' : 'Culvert time series'}>
          <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'end', gap: 9 }}>
            <label style={{ display: 'grid', gap: 4, fontSize: 11 }}>
              <span>{zh ? '时序变量' : 'Time-series variable'}</span>
              <select data-testid="map-culvert-time-series-variable" aria-label={zh ? '涵洞时序变量' : 'Culvert time-series variable'} value={variable} onChange={event => setVariable(event.currentTarget.value === 'Velocity' ? 'Velocity' : 'Flow')}>
                <option value="Flow">{zh ? '流量' : 'Flow'}</option>
                <option value="Velocity">{zh ? '出口流速' : 'Outlet velocity'}</option>
              </select>
            </label>
            <button type="button" className="map-import-button" onClick={selectAllBarrels} disabled={profile.barrels.length === 0}>{zh ? '全选' : 'Select all'}</button>
            <button type="button" className="map-import-button" onClick={clearBarrels} disabled={selectedBarrelIDs.length === 0}>{zh ? '清空' : 'Clear'}</button>
            <button type="button" className="map-create-document" data-testid="map-culvert-load-time-series" onClick={() => void loadTimeSeries()} disabled={!requestIsReady || selectedBarrelIDs.length === 0 || !supportsTimeSeries || timeLoading}>
              {timeLoading ? (zh ? '正在读取…' : 'Reading…') : (zh ? '读取完整时序' : 'Read full time series')}
            </button>
            <span style={{ color: '#687e8d', fontSize: 10 }}>{zh ? `已选 ${selectedBarrelIDs.length}/${profile.barrels.length} 个管节` : `${selectedBarrelIDs.length}/${profile.barrels.length} barrels selected`}</span>
          </div>
          {!supportsTimeSeries && <p className="map-page-notice" role="status">{zh ? '当前工作环境暂不支持读取涵洞时序。' : 'Culvert time-series reading is unavailable in the current workspace.'}</p>}
          {!profile.geometryAvailable && <p className="map-page-notice" role="status">{zh ? '缺少当前 Result 的涵洞几何，不能按源 barrel 身份请求时序。' : 'The current Result has no culvert geometry, so source-barrel time series cannot be requested.'}</p>}
          {profile.geometryAvailable && !profile.outputAvailable && <p className="map-page-notice" role="status">{zh ? '当前时间帧没有可读取的涵洞剖面输出。' : 'No readable culvert profile output is available at this frame.'}</p>}
          {profile.barrels.length > 0 && <fieldset disabled={!requestIsReady} style={{ display: 'flex', flexWrap: 'wrap', gap: '5px 12px', margin: '8px 0', padding: '7px 9px', border: '1px solid #dce4e9', borderRadius: 5 }}>
            <legend style={{ padding: '0 5px', fontSize: 10 }}>{zh ? '参与时序的源管节' : 'Source barrels for the time series'}</legend>
            {profile.barrels.map(barrel => <label key={barrel.id} style={{ display: 'inline-flex', alignItems: 'center', gap: 5, fontSize: 11 }}>
              <input type="checkbox" data-testid={`map-culvert-barrel-${barrel.sourceOrdinal}`} checked={selectedSet.has(barrel.id)} onChange={event => toggleBarrel(barrel.id, event.currentTarget.checked)} />
              <span>{barrel.name || (zh ? `管节 ${barrel.sourceOrdinal + 1}` : `barrel ${barrel.sourceOrdinal + 1}`)}</span>
              <code>{barrel.id}</code>
            </label>)}
          </fieldset>}
          {timeError && <div className="map-page-notice map-error-notice" role="alert"><span>!</span><span>{zh ? '涵洞时序读取失败。' : 'Could not read the culvert time series.'}<details><summary>{zh ? '查看诊断' : 'View diagnostic'}</summary><pre style={{ whiteSpace: 'pre-wrap' }}>{timeError}</pre></details></span></div>}
          {timeState?.identity === timeSeriesIdentity && timeState.status === 'unavailable' && <p className="map-page-notice" role="status">{zh ? '当前工作环境暂不支持读取涵洞时序。' : 'Culvert time-series reading is unavailable in the current workspace.'}</p>}
          {timeSeries && <>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: '4px 16px', margin: '8px 0', fontSize: 11 }}>
              <span>{zh ? '变量' : 'Variable'}：<strong>{timeSeries.variable}</strong></span>
              <span>{zh ? '单位' : 'Units'}：<strong>{timeSeries.units || '—'}</strong></span>
              <span>{zh ? '时间点' : 'Times'}：<strong>{timeSeries.times.length}</strong></span>
              <span>{zh ? 'NoData' : 'NoData'}：<code>{timeSeries.noDataValue}</code></span>
            </div>
            <div data-testid="map-culvert-timeseries-graph"><CulvertSeriesChart result={timeSeries} language={language} /></div>
            <div style={{ overflowX: 'auto', maxHeight: 330, marginTop: 8 }}>
              <table data-testid="map-culvert-timeseries-table" style={{ borderCollapse: 'collapse', width: '100%', fontSize: 10 }}>
                <thead><tr><th style={cellStyle}>{zh ? '时间' : 'Time'}</th>{timeSeries.datasets.map(dataset => <th key={dataset.id} style={cellStyle}>{dataset.name || dataset.id}<br /><small>{timeSeries.units}</small></th>)}</tr></thead>
                <tbody>{timeSeries.times.map((time, timeIndex) => <tr key={`${timeIndex}:${time}`}>
                  <td style={cellStyle}><code>{time}</code></td>
                  {timeSeries.datasets.map(dataset => {
                    const value = dataset.values[timeIndex];
                    const noData = value === timeSeries.noDataValue;
                    return <td key={dataset.id} style={{ ...cellStyle, textAlign: 'right', color: noData ? '#7f8c94' : undefined }} title={noData ? `${zh ? 'NoData 源标记：' : 'Source NoData sentinel: '}${timeSeries.noDataValue}` : undefined}>
                      {noData ? (zh ? `NoData (${timeSeries.noDataValue})` : `NoData (${timeSeries.noDataValue})`) : value.toString()}
                    </td>;
                  })}
                </tr>)}</tbody>
              </table>
            </div>
          </>}
          {timeState?.identity === timeSeriesIdentity && timeState.status === 'loading' && <p className="map-page-notice" role="status">{zh ? '正在读取 Result 中存储的全部涵洞时序…' : 'Reading all stored culvert time-series values from the Result…'}</p>}
          {!timeSeries && !timeLoading && !timeError && <p className="map-page-notice" role="status">{zh ? '选择一个或多个源管节，再读取 Flow 或 Velocity 时序。' : 'Select one or more source barrels, then read Flow or Velocity.'}</p>}
        </section>
      </>}
    </section>
  );
}

const definitionValueStyle: CSSProperties = { margin: 0, minWidth: 0, overflowWrap: 'anywhere', color: '#405d70' };
