import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { BackendGateway, Language, WorkbenchContext } from '../../shared/workbench';
import type { CatalogTreeNode, CatalogTreeNodeLocator, MapDocumentTab, MapExtent } from './mapTypes';
import { projectXmlSha } from './ProjectTreeListener';
import { mapError } from './mapText';
import type { MapAnalysisMapRequest } from './analysis/mapAnalysisTypes';
import { resultNodeIdentity, sharedKeyframeFromRender, verifySharedKeyframe } from './results-layer-settings/sharedKeyframe';
import type { ResultLayerSettingsController } from './results-layer-settings/useResultLayerSettings';

export interface MapResultFramesViewport {
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
  pixelWidth: number;
  pixelHeight: number;
}

export type MapResultFramesStatus = 'idle' | 'loading' | 'ready' | 'empty' | 'unavailable' | 'error';

export interface MapResultSourceChoice {
  resultPath: string;
  revision: string;
  resultName: string;
  planName: string | null;
  endTime: string | null;
  isComputing: boolean;
  computeState: string | null;
  computeFailureReason: string | null;
  diagnosticStatus: string | null;
  diagnosticReadError: string | null;
}

export interface MapResultSelectionState {
  selectionIndex: number;
  selectionMode: string;
  selectedResult: MapResultSourceChoice | null;
  choices: MapResultSourceChoice[];
}

export interface MapResultActiveNode {
  node: CatalogTreeNodeLocator;
  nodeKind: string;
  name: string;
  selections: MapResultSelectionState[];
  selectedMapType: string | null;
  profileIndex: number | null;
}

export interface MapResultSourceCatalog {
  projectPath: string;
  projectRevision: string;
  projectXmlSHA: string;
  visualizationRevision: string;
  mapID: string;
  mapIndex: number;
  mapName: string;
  mapType: string;
  contextToken: string;
  activeNodes: MapResultActiveNode[];
  /** Tree resolved against this exact slot-zero Result selection, never the default Latest tree. */
  treeRoots: CatalogTreeNode[];
  /** Host-resolved map-tree selection contract, distinct from chart slot overrides. */
  treeSelectionOverrides: Record<string, unknown>[];
}

export interface MapResultSelectionOverridePayload extends Record<string, unknown> {
  node: CatalogTreeNodeLocator;
  selectionIndex: number;
  mode: string;
  resultPath?: string;
  resultRevision?: string;
}

export interface MapResultRenderStep {
  id: number;
  label: string;
  time: string;
}

/** Host byte[] serializes as base64; bounds are the raster's physical map extent. */
export interface MapResultRenderLayer {
  id: string;
  sourceID: string;
  sourceMapID: string;
  resultPath: string;
  revision: string;
  resultName: string;
  mapType: string;
  terrainPath: string;
  terrainRevision: string;
  profileIndex: number;
  profileName: string;
  time: string;
  units: string;
  width: number;
  height: number;
  rgba: string;
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
  noDataValue: number;
  legendStops: { color: string; value: number }[];
  selectionIndices: number[] | null;
  velocityDisplay: string;
  mapDiagnostics: boolean;
  forceRecomputeMapPoints: boolean;
  diagnosticLabels: { text: string; x: number; y: number }[] | null;
  forceHorizontalRendering: boolean;
}

export interface MapResultRenderSourceStatus {
  resultPath: string;
  revision: string;
  resultName: string;
  selectionIndices: number[];
  terrainPath: string | null;
  terrainRevision: string | null;
  available: boolean;
  profileIndex: number | null;
  unavailableReason: string | null;
  validExtent: number[] | null;
}

export interface MapResultRenderSnapshot {
  contextToken: string;
  sourceID: string;
  selectedProfileIndex: number;
  selectedProfileTime: string | null;
  steps: MapResultRenderStep[];
  layers: MapResultRenderLayer[];
  sources: MapResultRenderSourceStatus[];
  unavailableReason: string | null;
  sharedKeyframeAnchor: unknown;
}

export interface MapResultFrameNodeOption {
  key: string;
  node: MapResultActiveNode;
}

export interface MapResultFramesController {
  status: MapResultFramesStatus;
  message: string | null;
  sources: MapResultSourceCatalog | null;
  /** Exact request retained with the currently checked Result source catalog. */
  mapRequest: MapAnalysisMapRequest | null;
  nodeOptions: MapResultFrameNodeOption[];
  activeNode: MapResultActiveNode | null;
  activeNodeKey: string | null;
  selectedSource: MapResultSourceChoice | null;
  selectionGroups: MapResultSelectionState[];
  steps: MapResultRenderStep[];
  selectedProfileIndex: number | null;
  selectedProfileTime: string | null;
  layers: MapResultRenderLayer[];
  /** All checked MultiResult groups, rendered independently against the same time key. */
  visibleLayers: MapResultRenderLayer[];
  keyframeOptions: MapResultFrameNodeOption[];
  keyframeNodeKey: string | null;
  groupRenderMessages: string[];
  activeResultProfileReady?: boolean;
  resultLayerSettings?: ResultLayerSettingsController;
  /** Full Result extent from the no-viewport render for the current source identity. */
  resultExtent: MapExtent | null;
  renderSnapshot: MapResultRenderSnapshot | null;
  isPlaying: boolean;
  canPlay: boolean;
  canChangeSource: boolean;
  canAddComparison: boolean;
  canRemoveComparison: boolean;
  selectNode: (key: string) => void;
  selectKeyframe: (key: string) => void;
  selectSource: (nodeKey: string, selectionIndex: number, choice: MapResultSourceChoice) => void;
  addComparison: () => void;
  removeComparison: () => void;
  selectProfile: (profileIndex: number) => void;
  play: () => void;
  pause: () => void;
  resync: () => void;
  refresh: () => void;
}

export interface UseMapResultFramesOptions {
  backend: BackendGateway;
  context: WorkbenchContext;
  document: MapDocumentTab | null;
  language: Language;
  /** null asks Host to render against the saved Result geometry's full extent. */
  viewport: MapResultFramesViewport | null;
  backendRevision?: number;
}

interface MapTreeRequestPayload {
  projectPath: string;
  projectXml: string | null;
  projectXmlSHA: string | null;
  mapID: string;
  mapIndex: number;
  mapName: string;
  mapType: string;
  expectedVisualizationRevision: string | null;
  projectSessionID: string;
}

interface LoadedMapResultContext {
  inputKey: string;
  map: MapTreeRequestPayload;
  catalog: MapResultSourceCatalog;
}

type PendingSourceChange =
  | { kind: 'select' | 'add'; nodeKey: string; selectionIndex: number; choice: MapResultSourceChoice; profileIndex: number | null }
  | { kind: 'remove'; nodeKey: string; selectionIndex: number; profileIndex: number | null };

interface RememberedSourceSelection {
  ownerKey: string;
  overrides: Record<string, unknown>[];
}

const MAX_RENDER_SIDE = 1536;
const MAX_MULTI_RESULT_SELECTION_SLOTS = 32;
const PLAY_INTERVAL_MS = 900;
const VIEWPORT_RENDER_DEBOUNCE_MS = 200;

function record(value: unknown): Record<string, unknown> | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null;
}

function requiredString(value: unknown, label: string): string {
  if (typeof value !== 'string' || !value.trim()) throw new Error(`The map result response is missing ${label}.`);
  return value;
}

function optionalString(value: unknown): string | null {
  return typeof value === 'string' ? value : null;
}

function parseSourceChoice(value: unknown): MapResultSourceChoice {
  const item = record(value);
  if (!item) throw new Error('The map result source response contains an invalid choice.');
  return {
    resultPath: requiredString(item.resultPath, 'a Result path'),
    revision: requiredString(item.revision, 'a Result revision'),
    resultName: requiredString(item.resultName, 'a Result name'),
    planName: optionalString(item.planName),
    endTime: optionalString(item.endTime),
    isComputing: item.isComputing === true,
    computeState: optionalString(item.computeState),
    computeFailureReason: optionalString(item.computeFailureReason),
    diagnosticStatus: optionalString(item.diagnosticStatus),
    diagnosticReadError: optionalString(item.diagnosticReadError),
  };
}

export function parseMapResultSourceCatalog(value: unknown): MapResultSourceCatalog {
  const item = record(value);
  if (!item || !Array.isArray(item.activeNodes) || !Array.isArray(item.treeRoots) ||
      !Array.isArray(item.treeSelectionOverrides)) throw new Error('The map result source response is incomplete.');
  const activeNodes = item.activeNodes.map((nodeValue): MapResultActiveNode => {
    const node = record(nodeValue);
    const locator = record(node?.node);
    if (!node || !locator || !Array.isArray(node.selections)) throw new Error('The map result source response contains an invalid map node.');
    const selections = node.selections.map((selectionValue): MapResultSelectionState => {
      const selection = record(selectionValue);
      if (!selection || !Array.isArray(selection.choices) || typeof selection.selectionIndex !== 'number') {
        throw new Error('The map result source response contains an invalid Result selection.');
      }
      return {
        selectionIndex: selection.selectionIndex,
        selectionMode: typeof selection.selectionMode === 'string' ? selection.selectionMode : 'Latest',
        selectedResult: selection.selectedResult === null || selection.selectedResult === undefined
          ? null
          : parseSourceChoice(selection.selectedResult),
        choices: selection.choices.map(parseSourceChoice),
      };
    });
    if (typeof node.nodeKind !== 'string' || typeof node.name !== 'string') {
      throw new Error('The map result source response contains an unnamed map node.');
    }
    return {
      node: locator as unknown as CatalogTreeNodeLocator,
      nodeKind: node.nodeKind,
      name: node.name,
      selections,
      selectedMapType: optionalString(node.selectedMapType),
      profileIndex: typeof node.profileIndex === 'number' && Number.isInteger(node.profileIndex) ? node.profileIndex : null,
    };
  });
  return {
    projectPath: requiredString(item.projectPath, 'the project path'),
    projectRevision: requiredString(item.projectRevision, 'the project revision'),
    projectXmlSHA: requiredString(item.projectXmlSHA, 'the project XML revision'),
    visualizationRevision: requiredString(item.visualizationRevision, 'the map visualization revision'),
    mapID: requiredString(item.mapID, 'the map ID'),
    mapIndex: typeof item.mapIndex === 'number' ? item.mapIndex : -1,
    mapName: requiredString(item.mapName, 'the map name'),
    mapType: requiredString(item.mapType, 'the map type'),
    contextToken: requiredString(item.contextToken, 'the map result context token'),
    activeNodes,
    treeRoots: item.treeRoots as CatalogTreeNode[],
    treeSelectionOverrides: item.treeSelectionOverrides as Record<string, unknown>[],
  };
}

function sourceNodeKey(locator: CatalogTreeNodeLocator): string {
  const stable = (value: unknown): unknown => {
    if (Array.isArray(value)) return value.map(stable);
    const item = record(value);
    if (!item) return value;
    return Object.fromEntries(Object.keys(item).sort().map((key) => [key, stable(item[key])]));
  };
  return JSON.stringify(stable(locator));
}

function sameMapContext(catalog: MapResultSourceCatalog, map: MapTreeRequestPayload, xmlSha: string): boolean {
  return catalog.projectPath === map.projectPath &&
    catalog.projectXmlSHA.toUpperCase() === xmlSha.toUpperCase() &&
    catalog.visualizationRevision === map.expectedVisualizationRevision &&
    catalog.mapID === map.mapID && catalog.mapIndex === map.mapIndex &&
    catalog.mapName === map.mapName && catalog.mapType === map.mapType;
}

function buildNodeOptions(catalog: MapResultSourceCatalog): MapResultFrameNodeOption[] {
  return catalog.activeNodes
    .filter((node) => Boolean(node.selectedMapType) && node.selections.some((selection) =>
      selection.selectedResult !== null || selection.choices.length > 0))
    .map((node) => ({ key: sourceNodeKey(node.node), node }));
}

function defaultNodeKey(options: MapResultFrameNodeOption[]): string | null {
  return options.find((option) => option.node.selections.some((selection) => selection.selectedResult !== null))?.key
    ?? options[0]?.key
    ?? null;
}

export function mapResultSelectionOverridesFromCatalog(
  catalog: MapResultSourceCatalog,
): MapResultSelectionOverridePayload[] {
  return catalog.activeNodes.flatMap((node) => node.selections.flatMap((selection) => {
    const selected = selection.selectedResult;
    if (!selected) return [];
    const mode = selection.selectionMode || 'Explicit';
    const override: MapResultSelectionOverridePayload = { node: node.node, selectionIndex: selection.selectionIndex, mode };
    if (mode === 'Explicit') {
      override.resultPath = selected.resultPath;
      override.resultRevision = selected.revision;
    }
    return [override];
  }));
}

function hasUnresolvedExplicitSelection(catalog: MapResultSourceCatalog): boolean {
  return catalog.activeNodes.some((node) => node.selections.some((selection) =>
    selection.selectionMode === 'Explicit' && selection.selectedResult === null));
}

function pendingComputeChoice(catalog: MapResultSourceCatalog): MapResultSourceChoice | null {
  return catalog.activeNodes.flatMap((node) => node.selections.flatMap((selection) =>
    selection.selectedResult ? [selection.selectedResult] : []))
    .find(isMapResultSourcePending) ?? null;
}

export function isMapResultSourcePending(choice: MapResultSourceChoice): boolean {
  if (choice.isComputing) return true;
  const state = choice.computeState?.trim().toLowerCase().replace(/[\s_-]/g, '') ?? '';
  return ['queued', 'starting', 'running', 'computing', 'writing', 'inprogress', 'cancelling'].includes(state);
}

function resultChoiceIdentity(choice: MapResultSourceChoice): string {
  return `${choice.resultPath}\u0000${choice.revision.toUpperCase()}`;
}

function hasContiguousSelectionSlots(selections: readonly MapResultSelectionState[]): boolean {
  return selections.every((selection, index) => selection.selectionIndex === index);
}

function comparisonChoiceForAdd(selections: readonly MapResultSelectionState[]): MapResultSourceChoice | null {
  const choices = selections.flatMap((selection) => selection.choices);
  const used = new Set(selections.flatMap((selection) =>
    selection.selectedResult ? [resultChoiceIdentity(selection.selectedResult)] : []));
  return choices.find((choice) => !used.has(resultChoiceIdentity(choice)) && !isMapResultSourcePending(choice)) ??
    choices.find((choice) => !isMapResultSourcePending(choice)) ?? null;
}

function finiteExtent(extent: MapExtent): boolean {
  return Number.isFinite(extent.minX) && Number.isFinite(extent.minY) &&
    Number.isFinite(extent.maxX) && Number.isFinite(extent.maxY) &&
    extent.minX < extent.maxX && extent.minY < extent.maxY;
}

function base64Sextet(code: number): number {
  if (code >= 65 && code <= 90) return code - 65;
  if (code >= 97 && code <= 122) return code - 97 + 26;
  if (code >= 48 && code <= 57) return code - 48 + 52;
  if (code === 43) return 62;
  if (code === 47) return 63;
  return -1;
}

/** Checks the exact byte count and scans the base64 alphabet without backtracking regexes. */
function isBase64ByteLength(value: string, byteLength: number): boolean {
  if (!Number.isSafeInteger(byteLength) || byteLength < 0) return false;
  const remainder = byteLength % 3;
  const expectedPadding = remainder === 1 ? 2 : remainder === 2 ? 1 : 0;
  const expectedLength = Math.ceil(byteLength / 3) * 4;
  if (value.length !== expectedLength || value.length % 4 !== 0) return false;

  const dataLength = value.length - expectedPadding;
  for (let index = 0; index < dataLength; index++) {
    if (base64Sextet(value.charCodeAt(index)) < 0) return false;
  }
  for (let index = dataLength; index < value.length; index++) {
    if (value.charCodeAt(index) !== 61) return false;
  }

  if (expectedPadding === 2) {
    const finalSextet = base64Sextet(value.charCodeAt(dataLength - 1));
    if (finalSextet < 0 || (finalSextet & 0x0f) !== 0) return false;
  } else if (expectedPadding === 1) {
    const finalSextet = base64Sextet(value.charCodeAt(dataLength - 1));
    if (finalSextet < 0 || (finalSextet & 0x03) !== 0) return false;
  }
  return true;
}

function parseRenderSnapshot(value: unknown): MapResultRenderSnapshot {
  const item = record(value);
  if (!item || !Array.isArray(item.steps) || !Array.isArray(item.layers) || !Array.isArray(item.sources)) {
    throw new Error('The map result render response is incomplete.');
  }
  const layers = item.layers.map((layerValue): MapResultRenderLayer => {
    const layer = record(layerValue);
    if (!layer) throw new Error('The map result render response contains an invalid raster layer.');
    const width = layer.width;
    const height = layer.height;
    const rgba = layer.rgba;
    if (typeof width !== 'number' || !Number.isInteger(width) || width < 1 || width > MAX_RENDER_SIDE ||
        typeof height !== 'number' || !Number.isInteger(height) || height < 1 || height > MAX_RENDER_SIDE ||
        typeof rgba !== 'string' || !isBase64ByteLength(rgba, width * height * 4)) {
      throw new Error('The map result renderer returned an invalid RGBA raster.');
    }
    const bounds = [layer.minX, layer.minY, layer.maxX, layer.maxY];
    if (!bounds.every((coordinate) => typeof coordinate === 'number' && Number.isFinite(coordinate)) ||
        (bounds[0] as number) >= (bounds[2] as number) || (bounds[1] as number) >= (bounds[3] as number)) {
      throw new Error('The map result renderer returned invalid physical raster bounds.');
    }
    return {
      id: requiredString(layer.id, 'a raster ID'),
      sourceID: requiredString(layer.sourceID, 'a source ID'),
      sourceMapID: requiredString(layer.sourceMapID, 'a source map ID'),
      resultPath: requiredString(layer.resultPath, 'a rendered Result path'),
      revision: requiredString(layer.revision, 'a rendered Result revision'),
      resultName: requiredString(layer.resultName, 'a rendered Result name'),
      mapType: requiredString(layer.mapType, 'a rendered map type'),
      terrainPath: requiredString(layer.terrainPath, 'a terrain path'),
      terrainRevision: requiredString(layer.terrainRevision, 'a terrain revision'),
      profileIndex: typeof layer.profileIndex === 'number' ? layer.profileIndex : -1,
      profileName: typeof layer.profileName === 'string' ? layer.profileName : '',
      time: typeof layer.time === 'string' ? layer.time : '',
      units: typeof layer.units === 'string' ? layer.units : '',
      width,
      height,
      rgba,
      minX: bounds[0] as number,
      minY: bounds[1] as number,
      maxX: bounds[2] as number,
      maxY: bounds[3] as number,
      noDataValue: typeof layer.noDataValue === 'number' ? layer.noDataValue : -9999,
      legendStops: Array.isArray(layer.legendStops) ? layer.legendStops as MapResultRenderLayer['legendStops'] : [],
      selectionIndices: Array.isArray(layer.selectionIndices) ? layer.selectionIndices as number[] : null,
      velocityDisplay: typeof layer.velocityDisplay === 'string' ? layer.velocityDisplay : 'Magnitude',
      mapDiagnostics: layer.mapDiagnostics === true,
      forceRecomputeMapPoints: layer.forceRecomputeMapPoints === true,
      diagnosticLabels: Array.isArray(layer.diagnosticLabels) ? layer.diagnosticLabels as MapResultRenderLayer['diagnosticLabels'] : null,
      forceHorizontalRendering: layer.forceHorizontalRendering === true,
    };
  });
  const steps = item.steps.map((stepValue): MapResultRenderStep => {
    const step = record(stepValue);
    if (!step || typeof step.id !== 'number' || !Number.isInteger(step.id) ||
        typeof step.label !== 'string' || typeof step.time !== 'string') {
      throw new Error('The map result render response contains an invalid time step.');
    }
    return { id: step.id, label: step.label, time: step.time };
  });
  return {
    contextToken: requiredString(item.contextToken, 'the render context token'),
    sourceID: requiredString(item.sourceID, 'the rendered source ID'),
    selectedProfileIndex: typeof item.selectedProfileIndex === 'number' ? item.selectedProfileIndex : -1,
    selectedProfileTime: optionalString(item.selectedProfileTime),
    steps,
    layers,
    sources: item.sources as MapResultRenderSourceStatus[],
    unavailableReason: optionalString(item.unavailableReason),
    sharedKeyframeAnchor: item.sharedKeyframeAnchor ?? null,
  };
}

function verifyRenderSnapshot(
  snapshot: MapResultRenderSnapshot,
  loaded: LoadedMapResultContext,
  activeNode: MapResultActiveNode,
): void {
  if (snapshot.contextToken !== loaded.catalog.contextToken) throw new Error('The map result sources changed while rendering.');
  const expected = activeNode.selections.flatMap((selection) => selection.selectedResult ? [selection.selectedResult] : []);
  const matches = (path: string, revision: string) => expected.some((choice) =>
    choice.resultPath === path && choice.revision.toUpperCase() === revision.toUpperCase());
  for (const layer of snapshot.layers) {
    if (layer.sourceID !== snapshot.sourceID || layer.sourceMapID !== loaded.map.mapID ||
        !matches(layer.resultPath, layer.revision)) {
      throw new Error('The map result raster no longer matches the selected map source revision.');
    }
  }
  for (const source of snapshot.sources) {
    if (!matches(source.resultPath, source.revision)) {
      throw new Error('The map result source revision changed while rendering.');
    }
  }
}

function unionLayerExtent(layers: MapResultRenderLayer[]): MapExtent | null {
  if (!layers.length) return null;
  const extent = layers.reduce<MapExtent>((union, layer) => ({
    minX: Math.min(union.minX, layer.minX),
    minY: Math.min(union.minY, layer.minY),
    maxX: Math.max(union.maxX, layer.maxX),
    maxY: Math.max(union.maxY, layer.maxY),
  }), { minX: layers[0].minX, minY: layers[0].minY, maxX: layers[0].maxX, maxY: layers[0].maxY });
  return finiteExtent(extent) ? extent : null;
}

function localizedResultReason(language: Language, reason: string | null): string | null {
  if (!reason) return null;
  if (language !== 'zh') return reason;
  const exact: Record<string, string> = {
    'This map node has no selected Result sources.': '此地图节点没有选定的结果源。',
    'The selected Result sources are no longer active in this map context.': '所选结果源已不属于当前地图上下文，请重新载入地图。',
    'This Result has no stored horizontal output profiles.': '此 Result 没有已保存的水平输出时刻。',
    'The selected Result map type is not supported by the current map renderer.': '当前地图渲染器不支持所选结果图类型。',
    'The selected Result has no current-project associated terrain.': '当前工程未关联此 Result 所需的地形。',
    'The MultiResult has no selected first Result to anchor its profile timeline.': 'MultiResult 尚未选定用于建立时间轴的首个 Result。',
    'The selected profile index is outside the Result timeline.': '所选时刻已超出 Result 时间序列范围。',
    'This Result has no profile within the 10 millisecond MultiResult timestamp tolerance.': '此 Result 没有与 MultiResult 时刻相差 10 毫秒以内的匹配时刻。',
    'The selected Result has no mapped cells in the current view.': '当前地图范围内没有可绘制的 Result 单元。',
  };
  return exact[reason] ?? `Result 帧不可用。服务端说明：${reason}`;
}

function localizedError(language: Language, error: unknown): string {
  const raw = error instanceof Error ? error.message : String(error);
  if (language === 'zh') {
    if (/map\.resultSources|map\.resultRender|map\.resultSelection/.test(raw) && /not advertised|unavailable/i.test(raw)) {
      return '当前 C# 服务暂不支持读取地图结果时间轴。';
    }
    if (/project (?:or saved map|changed)|source file changed|SourceFileChangedException/i.test(raw)) {
      return '工程、地图或结果源已发生变化，请重新载入地图后再试。';
    }
    if (/comput(?:e|ing).*(?:writer|running)|(?:writer|running).*comput/i.test(raw)) {
      return '结果仍由计算任务写入，完成后才能读取时间轴。';
    }
  }
  const localized = mapError(language, error);
  return language === 'zh' && localized === raw
    ? `读取 Result 帧失败。服务端诊断：${raw}`
    : localized;
}

export function useMapResultFrames({ backend, context, document, language, viewport, backendRevision }: UseMapResultFramesOptions): MapResultFramesController {
  const projectPath = context.projectPath;
  const projectXml = context.projectXml;
  const projectRevision = context.projectRevision;
  const projectSessionID = context.projectSessionID;
  const coreLayerContext = context.coreLayerContext ?? null;
  const coreLayerContextIdentity = typeof coreLayerContext?.identityToken === 'string'
    ? coreLayerContext.identityToken : null;
  // A .vis save invalidates rendered frames, but does not change the owner of
  // this session's explicit Result comparisons. The server revalidates every
  // locator, source path and SHA against the new visualization revision.
  const selectionOwnerKey = JSON.stringify([
    projectSessionID, projectPath, projectRevision, projectXml,
      coreLayerContextIdentity, document?.id, document?.mapID, document?.mapIndex, document?.name,
    document?.mapType, backendRevision,
  ]);
  const mapInputKey = JSON.stringify([
    projectSessionID, projectPath, projectRevision, projectXml,
      coreLayerContextIdentity, document?.id, document?.mapID, document?.mapIndex, document?.name,
    document?.mapType, document?.visualizationRevision, backendRevision,
  ]);
  const viewportKey = viewport ? JSON.stringify(viewport) : 'full-result-extent';
  const [loaded, setLoaded] = useState<LoadedMapResultContext | null>(null);
  const [status, setStatus] = useState<MapResultFramesStatus>('idle');
  const [message, setMessage] = useState<string | null>(null);
  const [activeNodeKey, setActiveNodeKey] = useState<string | null>(null);
  const [keyframeNodeKey, setKeyframeNodeKey] = useState<string | null>(null);
  const [pendingSourceChange, setPendingSourceChange] = useState<PendingSourceChange | null>(null);
  const [selectedProfileIndex, setSelectedProfileIndex] = useState<number | null>(null);
  const [steps, setSteps] = useState<MapResultRenderStep[]>([]);
  const [layers, setLayers] = useState<MapResultRenderLayer[]>([]);
  const [visibleLayers, setVisibleLayers] = useState<MapResultRenderLayer[]>([]);
  const [groupRenderMessages, setGroupRenderMessages] = useState<string[]>([]);
  const [resultExtent, setResultExtent] = useState<MapExtent | null>(null);
  const [renderSnapshot, setRenderSnapshot] = useState<MapResultRenderSnapshot | null>(null);
  const [isPlaying, setIsPlaying] = useState(false);
  const [refreshSequence, setRefreshSequence] = useState(0);
  const [renderRefreshSequence, setRenderRefreshSequence] = useState(0);
  const [fullExtentAcquired, setFullExtentAcquired] = useState<{ sourceIdentity: string } | null>(null);
  const rememberedSourceSelectionRef = useRef<RememberedSourceSelection | null>(null);
  const rememberedKeyframeRef = useRef<{ ownerKey: string; nodeKey: string; source: string; profileIndex: number } | null>(null);
  const sourceGenerationRef = useRef(0);
  const renderGenerationRef = useRef(0);
  const currentRenderSourceIdentityRef = useRef<string | null>(null);
  const previousRenderSourceIdentityRef = useRef<string | null>(null);
  const lastRenderFrameKeyRef = useRef<string | null>(null);

  useEffect(() => {
    const generation = ++sourceGenerationRef.current;
    const abortController = new AbortController();
    if (rememberedSourceSelectionRef.current?.ownerKey !== selectionOwnerKey) {
      rememberedSourceSelectionRef.current = null;
    }
    if (rememberedKeyframeRef.current?.ownerKey !== selectionOwnerKey) rememberedKeyframeRef.current = null;
    const rememberedSelectionOverrides = rememberedSourceSelectionRef.current?.overrides ?? null;
    setLoaded(null);
    setActiveNodeKey(null);
    setKeyframeNodeKey(null);
    setPendingSourceChange(null);
    setSelectedProfileIndex(null);
    setSteps([]);
    setLayers([]);
    setVisibleLayers([]);
    setGroupRenderMessages([]);
    setResultExtent(null);
    setRenderSnapshot(null);
    setMessage(null);
    setIsPlaying(false);

    if (!projectPath || !projectXml || !projectRevision || !document) {
      setStatus('idle');
      return () => abortController.abort();
    }
    if (!document.visualizationRevision) {
      setStatus('unavailable');
      setMessage(language === 'zh' ? '当前地图尚无可验证的 .vis 修订。' : 'This map has no verified .vis revision.');
      return () => abortController.abort();
    }
    if (!backend.supports('map.resultSources') || !backend.supports('map.resultRender')) {
      setStatus('unavailable');
      setMessage(language === 'zh' ? '当前 C# 服务尚未发布地图结果读取与渲染操作。' : 'The C# service has not advertised map result source and render operations.');
      return () => abortController.abort();
    }
    setStatus('loading');
    void (async () => {
      try {
        const xmlSha = await projectXmlSha(projectXml);
        if (abortController.signal.aborted || generation !== sourceGenerationRef.current) return;
        if (!xmlSha) throw new Error('The saved project XML snapshot is required to render map results.');
        const map: MapTreeRequestPayload = {
          projectPath,
          projectXml,
          projectXmlSHA: xmlSha,
          mapID: document.mapID,
          mapIndex: document.mapIndex,
          mapName: document.name,
          mapType: document.mapType,
          expectedVisualizationRevision: document.visualizationRevision,
          projectSessionID,
        };
        const raw = await backend.request<unknown>('map.resultSources', {
          map,
          ...(rememberedSelectionOverrides ? { selectionOverrides: rememberedSelectionOverrides } : {}),
        }, { signal: abortController.signal });
        if (abortController.signal.aborted || generation !== sourceGenerationRef.current) return;
        const catalog = parseMapResultSourceCatalog(raw);
        if (!sameMapContext(catalog, map, xmlSha) || catalog.projectRevision !== projectRevision) {
          throw new Error('The project or saved map changed while its Result sources were being read.');
        }
        const context: LoadedMapResultContext = { inputKey: mapInputKey, map, catalog };
        setLoaded(context);
        const nodeKey = defaultNodeKey(buildNodeOptions(catalog));
        setActiveNodeKey(nodeKey);
        const groupOptions = catalog.activeNodes.filter(node => node.nodeKind === 'MultiResult' && node.node.nodeType === 'MultiResult');
        const usable = (node: MapResultActiveNode) => {
          const source = node.selections.find(selection => selection.selectionIndex === 0)?.selectedResult;
          return !!source && !isMapResultSourcePending(source);
        };
        const remembered = rememberedKeyframeRef.current;
        const rememberedNode = groupOptions.find(node => {
          const source = node.selections.find(selection => selection.selectionIndex === 0)?.selectedResult;
          return sourceNodeKey(node.node) === remembered?.nodeKey && !!source &&
            JSON.stringify([source.resultPath, source.revision]) === remembered.source && usable(node);
        });
        const keyframe = rememberedNode ?? groupOptions.find(usable) ?? null;
        setKeyframeNodeKey(keyframe ? sourceNodeKey(keyframe.node) : null);
        const node = keyframe ?? buildNodeOptions(catalog).find((option) => option.key === nodeKey)?.node;
        setSelectedProfileIndex(rememberedNode ? remembered!.profileIndex : keyframe ? 0 : node?.profileIndex ?? 0);
        setStatus('loading');
      } catch (error) {
        if (abortController.signal.aborted || generation !== sourceGenerationRef.current) return;
        setStatus('error');
        setMessage(localizedError(language, error));
      }
    })();
    return () => abortController.abort();
  }, [backend, backendRevision, coreLayerContextIdentity, document, language, mapInputKey, selectionOwnerKey, projectPath, projectRevision, projectSessionID, projectXml, refreshSequence]);

  const activeNodeOptions = useMemo<MapResultFrameNodeOption[]>(() => loaded ? buildNodeOptions(loaded.catalog) : [], [loaded]);
  const keyframeOptions = useMemo<MapResultFrameNodeOption[]>(() => loaded
    ? loaded.catalog.activeNodes.filter(node => node.nodeKind === 'MultiResult' && node.node.nodeType === 'MultiResult')
      .map(node => ({ key: sourceNodeKey(node.node), node })) : [], [loaded]);
  const keyframeNode = keyframeOptions.find(option => option.key === keyframeNodeKey)?.node ?? null;
  const selectedNodeKey = activeNodeKey && activeNodeOptions.some((item) => item.key === activeNodeKey)
    ? activeNodeKey
    : defaultNodeKey(activeNodeOptions);
  const activeNode = activeNodeOptions.find((option) => option.key === selectedNodeKey)?.node ?? null;
  const timelineNode = keyframeNode ?? activeNode;
  useEffect(() => {
    const source = keyframeNode?.selections.find(selection => selection.selectionIndex === 0)?.selectedResult;
    if (loaded?.inputKey === mapInputKey && keyframeNode && source && selectedProfileIndex !== null) rememberedKeyframeRef.current = {
      ownerKey: selectionOwnerKey, nodeKey: sourceNodeKey(keyframeNode.node),
      source: JSON.stringify([source.resultPath, source.revision]), profileIndex: selectedProfileIndex,
    };
  }, [keyframeNode, loaded, mapInputKey, selectedProfileIndex, selectionOwnerKey]);
  const selectedSource = activeNode?.selections.find((selection) => selection.selectedResult)?.selectedResult ?? null;
  const renderSourceIdentity = useMemo(() => {
    if (!loaded || !timelineNode) return null;
    return JSON.stringify({
      projectPath: loaded.catalog.projectPath,
      projectRevision: loaded.catalog.projectRevision,
      projectXmlSHA: loaded.catalog.projectXmlSHA,
      visualizationRevision: loaded.catalog.visualizationRevision,
      mapID: loaded.catalog.mapID,
      mapIndex: loaded.catalog.mapIndex,
      mapName: loaded.catalog.mapName,
      mapType: loaded.catalog.mapType,
      node: sourceNodeKey(timelineNode.node),
      selectedNode: activeNode ? sourceNodeKey(activeNode.node) : null,
      sources: loaded.catalog.activeNodes.map(node => [sourceNodeKey(node.node), node.selectedMapType,
        node.selections.map(selection => [selection.selectionIndex, selection.selectedResult?.resultPath ?? null,
          selection.selectedResult?.revision ?? null])]),
    });
  }, [activeNode, loaded, timelineNode]);
  currentRenderSourceIdentityRef.current = renderSourceIdentity;
  const hasFullExtentForSource = Boolean(renderSourceIdentity &&
    fullExtentAcquired?.sourceIdentity === renderSourceIdentity);
  const requestedViewportKey = hasFullExtentForSource && viewportKey !== 'full-result-extent'
    ? viewportKey
    : 'full-result-extent';
  const requestedViewport = useMemo(() => requestedViewportKey === 'full-result-extent'
    ? null
    : JSON.parse(requestedViewportKey) as MapResultFramesViewport, [requestedViewportKey]);
  const renderFrameKey = renderSourceIdentity
    ? JSON.stringify([renderSourceIdentity, selectedProfileIndex ?? timelineNode?.profileIndex ?? 0])
    : null;

  useEffect(() => {
    if (previousRenderSourceIdentityRef.current === renderSourceIdentity) return;
    previousRenderSourceIdentityRef.current = renderSourceIdentity;
    lastRenderFrameKeyRef.current = null;
    setFullExtentAcquired(null);
    setResultExtent(null);
    setLayers([]);
    setVisibleLayers([]);
    setGroupRenderMessages([]);
    setRenderSnapshot(null);
    setSteps([]);
  }, [renderSourceIdentity]);

  useEffect(() => {
    if (!loaded || loaded.inputKey !== mapInputKey) return;
    if (pendingSourceChange) {
      const generation = ++sourceGenerationRef.current;
      const abortController = new AbortController();
      if (!backend.supports('map.resultSelection')) {
        setPendingSourceChange(null);
        setMessage(language === 'zh' ? '当前 C# 服务尚未发布 Result 数据源切换操作。' : 'The C# service has not advertised Result source selection.');
        setStatus('ready');
        return () => abortController.abort();
      }
      setStatus('loading');
      setMessage(null);
      setLayers([]);
      setVisibleLayers([]);
      setGroupRenderMessages([]);
      setRenderSnapshot(null);
      setIsPlaying(false);
      void (async () => {
        try {
          const selectionOverrides = mapResultSelectionOverridesFromCatalog(loaded.catalog)
            .filter((override) => {
              const node = override.node as CatalogTreeNodeLocator;
              return !(sourceNodeKey(node) === pendingSourceChange.nodeKey &&
                override.selectionIndex === pendingSourceChange.selectionIndex);
            });
          const targetNode = loaded.catalog.activeNodes.find((node) => sourceNodeKey(node.node) === pendingSourceChange.nodeKey);
          if (!targetNode) throw new Error('The selected Result node is no longer active in this map.');
          if (pendingSourceChange.kind !== 'select' && targetNode.node.nodeType !== 'MultiResult') {
            throw new Error(language === 'zh' ? '只有 MultiResult 地图节点支持结果对比。' : 'Result comparisons are available only for a MultiResult map node.');
          }
          if (pendingSourceChange.kind === 'add') {
            const selections = targetNode.selections;
            const choiceStillAvailable = selections.flatMap((selection) => selection.choices)
              .some((choice) => resultChoiceIdentity(choice) === resultChoiceIdentity(pendingSourceChange.choice) &&
                !isMapResultSourcePending(choice));
            if (!hasContiguousSelectionSlots(selections) || selections.length === 0 ||
                selections.length >= MAX_MULTI_RESULT_SELECTION_SLOTS ||
                pendingSourceChange.selectionIndex !== selections.length || !choiceStillAvailable) {
              throw new Error(language === 'zh' ? '对比结果槽或来源已发生变化，请重新载入后再试。' : 'The MultiResult comparison slot or source changed before it could be added.');
            }
          }
          if (pendingSourceChange.kind === 'remove' &&
              (!hasContiguousSelectionSlots(targetNode.selections) || targetNode.selections.length <= 1 ||
               pendingSourceChange.selectionIndex !== targetNode.selections.length - 1)) {
            throw new Error(language === 'zh' ? '只能移除末尾的对比结果，且至少保留一个来源。' : 'Only the last MultiResult comparison slot can be removed.');
          }
          if (pendingSourceChange.kind !== 'remove') {
            selectionOverrides.push({
              node: targetNode.node,
              selectionIndex: pendingSourceChange.selectionIndex,
              mode: 'Explicit',
              resultPath: pendingSourceChange.choice.resultPath,
              resultRevision: pendingSourceChange.choice.revision,
            });
          }
          const raw = await backend.request<unknown>('map.resultSelection', {
            map: loaded.map,
            expectedContextToken: loaded.catalog.contextToken,
            currentSelectionOverrides: mapResultSelectionOverridesFromCatalog(loaded.catalog),
            selectionOverrides,
          }, { signal: abortController.signal });
          if (abortController.signal.aborted || generation !== sourceGenerationRef.current || loaded.inputKey !== mapInputKey) return;
          const catalog = parseMapResultSourceCatalog(raw);
          const xmlSha = loaded.catalog.projectXmlSHA;
          if (!sameMapContext(catalog, loaded.map, xmlSha) || catalog.projectRevision !== loaded.catalog.projectRevision) {
            throw new Error('The project or saved map changed while its Result source was being changed.');
          }
          rememberedSourceSelectionRef.current = {
            ownerKey: selectionOwnerKey,
            overrides: mapResultSelectionOverridesFromCatalog(catalog),
          };
          setLoaded({ ...loaded, catalog });
          setActiveNodeKey(pendingSourceChange.nodeKey);
          const selectedNode = catalog.activeNodes.find((node) => sourceNodeKey(node.node) === pendingSourceChange.nodeKey);
          if (!keyframeNodeKey) setSelectedProfileIndex(pendingSourceChange.kind === 'select'
            ? selectedNode?.profileIndex ?? 0 : pendingSourceChange.profileIndex ?? selectedNode?.profileIndex ?? 0);
          else if (pendingSourceChange.kind === 'select' && pendingSourceChange.nodeKey === keyframeNodeKey &&
              pendingSourceChange.selectionIndex === 0) setSelectedProfileIndex(0);
          setSteps([]);
          setPendingSourceChange(null);
          setStatus('loading');
        } catch (error) {
          if (abortController.signal.aborted || generation !== sourceGenerationRef.current) return;
          setPendingSourceChange(null);
          setStatus('error');
          setMessage(localizedError(language, error));
        }
      })();
      return () => abortController.abort();
    }
    const pendingCompute = pendingComputeChoice(loaded.catalog);
    if (pendingCompute) {
      setSteps([]);
      setLayers([]);
      setVisibleLayers([]);
      setRenderSnapshot(null);
      setSelectedProfileIndex(null);
      setStatus('unavailable');
      setMessage(language === 'zh'
        ? `结果“${pendingCompute.resultName}”仍由计算任务写入，完成后点“刷新”重新读取。`
        : `Result “${pendingCompute.resultName}” is still being written. Refresh after compute completes.`);
      setIsPlaying(false);
      return;
    }
    if (hasUnresolvedExplicitSelection(loaded.catalog)) {
      setSteps([]);
      setLayers([]);
      setVisibleLayers([]);
      setRenderSnapshot(null);
      setSelectedProfileIndex(null);
      setStatus('unavailable');
      setMessage(language === 'zh'
        ? '地图中有明确指定但暂不可读取的 Result。等待计算写入完成或修复源文件后，点“刷新”重读时间轴。'
        : 'A Result explicitly selected by this map is not readable yet. Wait for compute to finish or repair the source, then refresh the timeline.');
      setIsPlaying(false);
      return;
    }
    if (keyframeOptions.length > 0 && !keyframeNode) {
      setSteps([]);
      setLayers([]);
      setVisibleLayers([]);
      setRenderSnapshot(null);
      setStatus('unavailable');
      setMessage(language === 'zh' ? '活动结果组没有可用的首个结果源，无法建立共享时间轴。' : 'No active result group has an available first source for the shared timeline.');
      setIsPlaying(false);
      return;
    }
    if (!timelineNode) {
      setSteps([]);
      setLayers([]);
      setVisibleLayers([]);
      setRenderSnapshot(null);
      setSelectedProfileIndex(null);
      setStatus('empty');
      setMessage(language === 'zh' ? '当前地图没有已选定且可显示的 Result 源。' : 'This map has no selected Result source to display.');
      return;
    }
    const selectedChoices = timelineNode.selections.flatMap((selection) => selection.selectedResult ? [selection.selectedResult] : []);
    if (selectedChoices.length === 0) {
      const explicitlyUnresolved = timelineNode.selections.some((selection) => selection.selectionMode === 'Explicit');
      setSteps([]);
      setLayers([]);
      setVisibleLayers([]);
      setRenderSnapshot(null);
      setSelectedProfileIndex(null);
      setStatus(explicitlyUnresolved ? 'unavailable' : 'empty');
      setMessage(explicitlyUnresolved
        ? (language === 'zh'
          ? '当前明确指定的 Result 尚不可读取。若计算仍在写入，请等待完成；也可以选择其他已保存的 Result。'
          : 'The explicitly selected Result is not currently readable. Wait for compute to finish or choose another saved Result.')
        : (language === 'zh' ? '当前地图节点没有已选定的 Result 数据源。' : 'This map node has no selected Result source.'));
      setIsPlaying(false);
      return;
    }
    const inProgress = selectedChoices.find(isMapResultSourcePending);
    if (inProgress) {
      setSteps([]);
      setLayers([]);
      setVisibleLayers([]);
      setRenderSnapshot(null);
      setSelectedProfileIndex(null);
      setStatus('unavailable');
      setMessage(language === 'zh'
        ? `结果“${inProgress.resultName}”仍由计算任务写入，完成后才能读取。`
        : `Result “${inProgress.resultName}” is still being written by a compute task.`);
      setIsPlaying(false);
      return;
    }
    if (!backend.supports('map.resultRender')) {
      setStatus('unavailable');
      setMessage(language === 'zh' ? '当前 C# 服务尚未发布 Result 帧渲染操作。' : 'The C# service has not advertised Result frame rendering.');
      return;
    }

    const generation = ++renderGenerationRef.current;
    const abortController = new AbortController();
    const profileIndex = selectedProfileIndex ?? timelineNode.profileIndex ?? 0;
    const requestSourceIdentity = renderSourceIdentity;
    const requestFrameKey = renderFrameKey;
    const keepCurrentFrame = requestFrameKey !== null && lastRenderFrameKeyRef.current === requestFrameKey;
    lastRenderFrameKeyRef.current = requestFrameKey;
    setStatus('loading');
    setMessage(null);
    if (!keepCurrentFrame) {
      setLayers([]);
      setVisibleLayers([]);
      setGroupRenderMessages([]);
      setRenderSnapshot(null);
    }
    let debounceTimer: number | null = null;
    const render = async () => {
      try {
        const renderRequest: Record<string, unknown> = {
          map: loaded.map,
          selectionOverrides: mapResultSelectionOverridesFromCatalog(loaded.catalog),
          contextToken: loaded.catalog.contextToken,
          node: timelineNode.node,
          profileIndex,
          viewport: requestedViewport,
        };
        const raw = await backend.request<unknown>('map.resultRender', renderRequest, { signal: abortController.signal });
        if (abortController.signal.aborted || generation !== renderGenerationRef.current ||
            loaded.inputKey !== mapInputKey || currentRenderSourceIdentityRef.current !== requestSourceIdentity) return;
        const anchorSnapshot = parseRenderSnapshot(raw);
        verifyRenderSnapshot(anchorSnapshot, loaded, timelineNode);
        if (keyframeNode && anchorSnapshot.steps.length && !anchorSnapshot.steps.some(step => step.id === profileIndex)) {
          setSelectedProfileIndex(anchorSnapshot.steps[0].id);
          return;
        }
        let snapshots: { node: MapResultActiveNode; snapshot: MapResultRenderSnapshot }[];
        if (keyframeNode) {
          const anchor = sharedKeyframeFromRender(keyframeNode, anchorSnapshot, profileIndex);
          snapshots = await Promise.all(keyframeOptions.map(async ({ node }) => {
            const response = await backend.request<unknown>('map.resultRender', {
              ...renderRequest, node: node.node, sharedKeyframeAnchor: anchor,
            }, { signal: abortController.signal });
            const snapshot = parseRenderSnapshot(response);
            verifyRenderSnapshot(snapshot, loaded, node);
            verifySharedKeyframe(anchor, snapshot.sharedKeyframeAnchor);
            if (snapshot.selectedProfileIndex !== anchor.profileIndex || snapshot.selectedProfileTime !== anchor.time ||
                JSON.stringify(snapshot.steps) !== JSON.stringify(anchorSnapshot.steps)) {
              throw new Error('结果组渲染回执中的共享时间轴不一致。');
            }
            return { node, snapshot };
          }));
          // Standalone Result nodes retain their own saved time state; they are not members of the shared group.
          if (activeNode && activeNode.node.nodeType === 'Result') {
            const independentRaw = await backend.request<unknown>('map.resultRender', {
              ...renderRequest, node: activeNode.node, profileIndex: activeNode.profileIndex ?? 0,
            }, { signal: abortController.signal });
            const independent = parseRenderSnapshot(independentRaw);
            verifyRenderSnapshot(independent, loaded, activeNode);
            snapshots.push({ node: activeNode, snapshot: independent });
          }
        } else {
          snapshots = [{ node: timelineNode, snapshot: anchorSnapshot }];
        }
        if (abortController.signal.aborted || generation !== renderGenerationRef.current ||
            loaded.inputKey !== mapInputKey || currentRenderSourceIdentityRef.current !== requestSourceIdentity) return;
        const snapshot = snapshots.find(item => activeNode && resultNodeIdentity(item.node.node) === resultNodeIdentity(activeNode.node))?.snapshot
          ?? anchorSnapshot;
        const allLayers = snapshots.flatMap(item => item.snapshot.layers);
        const groupMessages = snapshots.flatMap(({ node, snapshot: current }) => {
          const reasons = [current.unavailableReason, ...current.sources.filter(source => !source.available).map(source => source.unavailableReason)]
            .filter((reason): reason is string => Boolean(reason));
          return [...new Set(reasons)].map(reason => `${node.name}: ${localizedResultReason(language, reason)}`);
        });
        if (requestedViewport === null) {
          const extent = unionLayerExtent(allLayers);
          setResultExtent(extent);
          setFullExtentAcquired({ sourceIdentity: requestSourceIdentity! });
        }
        setSteps(anchorSnapshot.steps);
        setLayers(snapshot.layers);
        setVisibleLayers(allLayers);
        setGroupRenderMessages(groupMessages);
        setRenderSnapshot(snapshot);
        const reason = localizedResultReason(language, snapshot.unavailableReason);
        if (allLayers.length === 0) {
          setStatus('empty');
          setMessage(reason ?? (language === 'zh' ? '所选时刻没有可绘制的 Result 帧。' : 'No drawable Result frame is available for this time step.'));
        } else {
          setStatus('ready');
          setMessage(reason);
        }
      } catch (error) {
        if (abortController.signal.aborted || generation !== renderGenerationRef.current ||
            currentRenderSourceIdentityRef.current !== requestSourceIdentity) return;
        setLayers([]);
        setVisibleLayers([]);
        setGroupRenderMessages([]);
        setRenderSnapshot(null);
        setStatus('error');
        setMessage(localizedError(language, error));
      }
    };
    if (requestedViewport === null || !keepCurrentFrame) {
      void render();
    } else {
      debounceTimer = window.setTimeout(() => {
        debounceTimer = null;
        void render();
      }, VIEWPORT_RENDER_DEBOUNCE_MS);
    }
    return () => {
      if (debounceTimer !== null) window.clearTimeout(debounceTimer);
      abortController.abort();
      if (renderGenerationRef.current === generation) renderGenerationRef.current++;
    };
  }, [activeNode, backend, keyframeNode, keyframeNodeKey, keyframeOptions, language, loaded, mapInputKey, selectionOwnerKey, pendingSourceChange, renderFrameKey, renderSourceIdentity, renderRefreshSequence, requestedViewport, requestedViewportKey, selectedProfileIndex, timelineNode]);

  const selectNode = useCallback((key: string) => {
    if (!activeNodeOptions.some((option) => option.key === key)) return;
    setIsPlaying(false);
    const node = activeNodeOptions.find((option) => option.key === key)?.node;
    if (!keyframeNode) setSelectedProfileIndex(node?.profileIndex ?? 0);
    setActiveNodeKey(key);
  }, [activeNodeOptions, keyframeNode]);

  const selectKeyframe = useCallback((key: string) => {
    const node = keyframeOptions.find(option => option.key === key)?.node;
    const source = node?.selections.find(selection => selection.selectionIndex === 0)?.selectedResult;
    if (!node || !source || isMapResultSourcePending(source) || pendingSourceChange || status === 'loading') return;
    rememberedKeyframeRef.current = { ownerKey: selectionOwnerKey, nodeKey: key,
      source: JSON.stringify([source.resultPath, source.revision]), profileIndex: 0 };
    setIsPlaying(false);
    setSelectedProfileIndex(0);
    setKeyframeNodeKey(key);
  }, [keyframeOptions, pendingSourceChange, selectionOwnerKey, status]);

  const selectSource = useCallback((nodeKey: string, selectionIndex: number, choice: MapResultSourceChoice) => {
    if (!loaded || pendingSourceChange || status === 'loading' || pendingComputeChoice(loaded.catalog) || hasUnresolvedExplicitSelection(loaded.catalog) ||
        isMapResultSourcePending(choice) || !backend.supports('map.resultSelection')) return;
    const target = loaded.catalog.activeNodes.find((node) => sourceNodeKey(node.node) === nodeKey);
    const selection = target?.selections.find((item) => item.selectionIndex === selectionIndex);
    if (!selection || (!selection.choices.some((item) => item.resultPath === choice.resultPath && item.revision === choice.revision) &&
        !(selection.selectedResult?.resultPath === choice.resultPath && selection.selectedResult.revision === choice.revision))) return;
    setIsPlaying(false);
    setActiveNodeKey(nodeKey);
    setPendingSourceChange({ kind: 'select', nodeKey, selectionIndex, choice, profileIndex: null });
  }, [backend, loaded, pendingSourceChange, status]);

  const selectionCanChange = Boolean(loaded && !pendingSourceChange && status !== 'loading' &&
    !pendingComputeChoice(loaded.catalog) && !hasUnresolvedExplicitSelection(loaded.catalog) &&
    backend.supports('map.resultSelection'));
  const activeSelectionsAreContiguous = activeNode !== null && hasContiguousSelectionSlots(activeNode.selections);
  const availableComparisonChoice = activeNode ? comparisonChoiceForAdd(activeNode.selections) : null;
  const canAddComparison = Boolean(selectionCanChange && activeNode?.node.nodeType === 'MultiResult' &&
    activeNode.selections.length > 0 && activeNode.selections.length < MAX_MULTI_RESULT_SELECTION_SLOTS &&
    activeSelectionsAreContiguous && availableComparisonChoice);
  const canRemoveComparison = Boolean(selectionCanChange && activeNode?.node.nodeType === 'MultiResult' &&
    activeNode.selections.length > 1 && activeSelectionsAreContiguous);

  const addComparison = useCallback(() => {
    if (!selectionCanChange || !activeNode || activeNode.node.nodeType !== 'MultiResult' ||
        activeNode.selections.length === 0 || activeNode.selections.length >= MAX_MULTI_RESULT_SELECTION_SLOTS ||
        !hasContiguousSelectionSlots(activeNode.selections)) return;
    const choice = comparisonChoiceForAdd(activeNode.selections);
    if (!choice) return;
    setIsPlaying(false);
    setActiveNodeKey(sourceNodeKey(activeNode.node));
    setPendingSourceChange({
      kind: 'add',
      nodeKey: sourceNodeKey(activeNode.node),
      selectionIndex: activeNode.selections.length,
      choice,
      profileIndex: selectedProfileIndex,
    });
  }, [activeNode, selectedProfileIndex, selectionCanChange]);

  const removeComparison = useCallback(() => {
    if (!selectionCanChange || !activeNode || activeNode.node.nodeType !== 'MultiResult' ||
        activeNode.selections.length <= 1 || !hasContiguousSelectionSlots(activeNode.selections)) return;
    setIsPlaying(false);
    setActiveNodeKey(sourceNodeKey(activeNode.node));
    setPendingSourceChange({
      kind: 'remove',
      nodeKey: sourceNodeKey(activeNode.node),
      selectionIndex: activeNode.selections.length - 1,
      profileIndex: selectedProfileIndex,
    });
  }, [activeNode, selectedProfileIndex, selectionCanChange]);

  const selectProfile = useCallback((profileIndex: number) => {
    if (!steps.some((step) => step.id === profileIndex)) return;
    setIsPlaying(false);
    setSelectedProfileIndex(profileIndex);
  }, [steps]);

  const pause = useCallback(() => setIsPlaying(false), []);
  const resync = useCallback(() => {
    if (!keyframeNode || pendingSourceChange || status === 'loading') return;
    setIsPlaying(false);
    setRenderRefreshSequence(sequence => sequence + 1);
  }, [keyframeNode, pendingSourceChange, status]);
  const refresh = useCallback(() => {
    setIsPlaying(false);
    setRefreshSequence((sequence) => sequence + 1);
  }, []);
  const play = useCallback(() => {
    if (steps.length > 1 && activeNode && status === 'ready') setIsPlaying(true);
  }, [activeNode, status, steps.length]);

  useEffect(() => {
    if (!isPlaying) return;
    if (steps.length < 2 || !activeNode || status === 'error' || status === 'unavailable' || status === 'empty') {
      setIsPlaying(false);
      return;
    }
    if (status !== 'ready') return;
    const timer = window.setTimeout(() => {
      const index = steps.findIndex((step) => step.id === selectedProfileIndex);
      const next = steps[(index < 0 ? 0 : index + 1) % steps.length];
      setSelectedProfileIndex(next.id);
    }, PLAY_INTERVAL_MS);
    return () => window.clearTimeout(timer);
  }, [activeNode, isPlaying, selectedProfileIndex, status, steps]);

  const nodeOptions = activeNodeOptions;
  const selectionGroups = activeNode?.selections ?? [];
  const selectedProfileTime = steps.find((step) => step.id === selectedProfileIndex)?.time
    ?? renderSnapshot?.selectedProfileTime
    ?? null;
  // React can render a newly selected node/time once before the effect has
  // cleared the previous frame. Never expose that old receipt as ready for the
  // new input, including to the profile projection and its user-facing status.
  const currentFramePending = status === 'ready' && renderSourceIdentity !== null &&
    (previousRenderSourceIdentityRef.current !== renderSourceIdentity || lastRenderFrameKeyRef.current !== renderFrameKey);
  const visibleStatus: MapResultFramesStatus = currentFramePending ? 'loading' : status;
  const mapRequest = useMemo<MapAnalysisMapRequest | null>(() =>
    loaded && loaded.inputKey === mapInputKey && loaded.map.projectPath === projectPath &&
      loaded.map.projectXml === projectXml && typeof loaded.map.projectXml === 'string' &&
      typeof loaded.map.projectXmlSHA === 'string' && loaded.map.projectSessionID === projectSessionID &&
      typeof loaded.map.expectedVisualizationRevision === 'string' && loaded.map.expectedVisualizationRevision.length > 0 &&
      Boolean(loaded.catalog.contextToken) &&
      loaded.catalog.visualizationRevision === loaded.map.expectedVisualizationRevision &&
      loaded.catalog.projectRevision === projectRevision
      ? { ...loaded.map, projectXml: loaded.map.projectXml, projectXmlSHA: loaded.map.projectXmlSHA,
        expectedVisualizationRevision: loaded.map.expectedVisualizationRevision, coreLayerContext }
      : null,
    [coreLayerContext, loaded, mapInputKey, projectPath, projectRevision, projectSessionID, projectXml]);

  return {
    status: visibleStatus,
    message,
    sources: loaded?.catalog ?? null,
    mapRequest,
    nodeOptions,
    activeNode,
    activeNodeKey: selectedNodeKey,
    selectedSource,
    selectionGroups,
    steps,
    selectedProfileIndex,
    selectedProfileTime,
    layers,
    visibleLayers,
    keyframeOptions,
    keyframeNodeKey,
    groupRenderMessages,
    resultExtent,
    renderSnapshot,
    isPlaying,
    canPlay: steps.length > 1 && Boolean(activeNode) && visibleStatus === 'ready',
    canChangeSource: selectionCanChange && visibleStatus !== 'loading',
    canAddComparison: canAddComparison && visibleStatus !== 'loading',
    canRemoveComparison: canRemoveComparison && visibleStatus !== 'loading',
    selectNode,
    selectKeyframe,
    selectSource,
    addComparison,
    removeComparison,
    selectProfile,
    play,
    pause,
    resync,
    refresh,
  };
}
