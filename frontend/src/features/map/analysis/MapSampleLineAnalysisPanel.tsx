import { useEffect, useRef, useState } from 'react';
import type { MapAnalysisPanelProps, MapSampleLinePart, MapSampleLineSource } from './mapAnalysisTypes';
import type { MapResultAnalysisCapture } from './mapAnalysisTypes';
import type { CatalogTreeNodeLocator, XY } from '../mapTypes';
import type { ResultDataset, ResultSeries } from '../../results/data';
import { ProfileSampleLinePlot } from '../../results/controls/ProfileSampleLinePlot';
import '../../results/resultInsights.css';
import './mapSampleLineAnalysis.css';
import {
  comparableMapSourcePath,
  computeMapSampleLineCoordinateSHA,
  isSha256,
  MAP_SAMPLE_LINE_MAX_POINTS,
  mapSampleLineSeriesToPlot,
  sameXYCoordinates,
  type MapSampleLineComputedSeries,
  type MapSampleLineOutput,
  type MapSampleLineResponse,
  type MapSampleLineSideEffect,
} from './mapSampleLineAnalysis';

type SampleLineMode = 'Flow' | 'CumulativeVolume';

interface PanelView {
  identity: string;
  partIndex: number | null;
  mode: SampleLineMode;
  computing: boolean;
  result: MapSampleLineResponse | null;
  error: string | null;
}

interface RequestState {
  controller: AbortController;
  generation: number;
  identity: string;
}

const EMPTY_PARTS: MapSampleLinePart[] = [];

function initialView(identity: string): PanelView {
  return { identity, partIndex: null, mode: 'Flow', computing: false, result: null, error: null };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function localized(language: 'zh' | 'en', zh: string, en: string): string {
  return language === 'zh' ? zh : en;
}

function numberIdentity(value: number): string {
  return Object.is(value, -0) ? '-0' : String(value);
}

function partsIdentity(parts: readonly MapSampleLinePart[] | undefined): string {
  if (!parts) return '';
  return parts.map((part) => {
    const points = Array.isArray(part.coordinates) ? part.coordinates.map((point) => {
      if (!Array.isArray(point) || point.length !== 2) return 'invalid';
      return `${numberIdentity(point[0] as number)},${numberIdentity(point[1] as number)}`;
    }).join(';') : 'invalid';
    return `${part.partIndex}:${points}`;
  }).join('|');
}

function panelIdentity(capture: MapResultAnalysisCapture | null, source: MapSampleLineSource | null): string {
  return JSON.stringify([
    capture?.ownerKey ?? null,
    capture?.captureKey ?? null,
    capture?.contextToken ?? null,
    capture?.map.projectPath ?? null,
    capture?.map.projectSessionID ?? null,
    capture?.map.projectXmlSHA ?? null,
    capture?.map.expectedVisualizationRevision ?? null,
    capture?.map.mapID ?? null,
    capture?.map.mapIndex ?? null,
    capture?.map.mapName ?? null,
    capture?.map.mapType ?? null,
    source?.ownerKey ?? null,
    source?.layerID ?? null,
    source?.nodeID ?? null,
    source?.mapType ?? null,
    source?.featureID ?? null,
    source?.vectorPath ?? null,
    source?.vectorRevision ?? null,
    partsIdentity(source?.parts),
  ]);
}

function validParts(source: MapSampleLineSource | null): MapSampleLinePart[] {
  if (!source || !Array.isArray(source.parts)) return EMPTY_PARTS;
  const seen = new Set<number>();
  return source.parts.filter((part) => {
    if (!part || !Number.isInteger(part.partIndex) || part.partIndex < 0 || seen.has(part.partIndex)
      || !Array.isArray(part.coordinates) || part.coordinates.length < 2
      || part.coordinates.length > MAP_SAMPLE_LINE_MAX_POINTS
      || part.coordinates.some((point) => !Array.isArray(point) || point.length !== 2
        || !Number.isFinite(point[0]) || !Number.isFinite(point[1]))) return false;
    seen.add(part.partIndex);
    return true;
  });
}

function locatorKey(locator: CatalogTreeNodeLocator): string {
  return JSON.stringify([
    locator.rootName, locator.nodeType, locator.nodeName, locator.nodePath ?? null,
    locator.assetID ?? null, locator.mapType ?? null, locator.filename ?? null,
    locator.filenameRelative ?? null, locator.parentGeometryAssetID ?? null,
    locator.parentGeometryNodePath ?? null, locator.parentGeometryNodeName ?? null,
    locator.associationAssetID ?? null, locator.associationKind ?? null,
    locator.parentPlanAssetID ?? null, locator.parentPlanNodePath ?? null,
    locator.parentPlanNodeName ?? null, locator.parentResultNodeType ?? null,
    locator.parentResultAssetID ?? null, locator.parentResultNodePath ?? null,
    locator.parentResultNodeName ?? null, locator.parentResultRevision ?? null,
  ]);
}

function isLocator(value: unknown): value is CatalogTreeNodeLocator {
  if (!isRecord(value) || typeof value.rootName !== 'string' || typeof value.nodeType !== 'string'
    || typeof value.nodeName !== 'string') return false;
  return value.nodePath === undefined || value.nodePath === null
    || (Array.isArray(value.nodePath) && value.nodePath.every((item) => Number.isInteger(item)));
}

function validatePointList(value: unknown): value is MapSampleLineComputedSeries['points'] {
  return Array.isArray(value) && value.every((point, index) => isRecord(point)
    && Number.isInteger(point.profileIndex) && point.profileIndex === index
    && typeof point.time === 'string'
    && (point.value === null || (typeof point.value === 'number' && Number.isFinite(point.value))));
}

function activeSelectionsCovered(capture: MapResultAnalysisCapture, outputs: readonly MapSampleLineOutput[]): boolean {
  const nodeKey = locatorKey(capture.activeNode.node);
  const expected = capture.activeNode.selections.filter((item) => item.selectedResult !== null);
  if (expected.length === 0) return false;

  const activeOutputs: MapSampleLineOutput[] = [];
  for (const output of outputs) {
    for (const source of output.selections.filter((item) => locatorKey(item.node) === nodeKey)) {
      const selection = expected.find((item) => item.selectionIndex === source.selectionIndex);
      const selected = selection?.selectedResult;
      if (!selected || selected.isComputing
        || comparableMapSourcePath(selected.resultPath) !== comparableMapSourcePath(output.series.resultPath)
        || selected.revision.toUpperCase() !== output.series.revision.toUpperCase()
        || (capture.activeNode.selectedMapType !== null && output.selectedMapType !== capture.activeNode.selectedMapType)) return false;
      activeOutputs.push(output);
    }
  }

  for (const selection of expected) {
    const selected = selection.selectedResult;
    if (!selected || selected.isComputing) return false;
    const matches = outputs.filter((output) => output.series.resultPath.length > 0
      && comparableMapSourcePath(selected.resultPath) === comparableMapSourcePath(output.series.resultPath)
      && selected.revision.toUpperCase() === output.series.revision.toUpperCase()
      && output.selections.some((source) => locatorKey(source.node) === nodeKey
        && source.selectionIndex === selection.selectionIndex));
    if (matches.length !== 1) return false;
    const output = matches[0];
    if (capture.activeNode.selectedMapType !== null
      && output.selectedMapType !== capture.activeNode.selectedMapType) return false;
    if (!activeOutputs.includes(output)) activeOutputs.push(output);
  }

  // The Host returns the map's selected-profile metadata for each selected
  // slot. All active slots on this one map node must describe the same profile.
  const profileIndex = activeOutputs[0]?.profileIndex;
  return activeOutputs.every((output) => output.profileIndex === profileIndex);
}

function assertResponse(
  value: unknown,
  capture: MapResultAnalysisCapture,
  source: MapSampleLineSource,
  part: MapSampleLinePart,
  coordinateSHA: string,
  mode: SampleLineMode,
  language: 'zh' | 'en',
): MapSampleLineResponse {
  const invalid = (): never => { throw new Error(localized(language,
    '采样线响应的工程、地图、来源、坐标或时步与当前快照不一致。',
    'The sample-line response does not match the captured project, map, source, coordinates, or time steps.')); };
  if (!isRecord(value)) return invalid();
  const response = value;
  const feature = response.feature;
  if (!isRecord(feature)) return invalid();
  const map = capture.map;
  if (typeof response.projectPath !== 'string' || comparableMapSourcePath(response.projectPath) !== comparableMapSourcePath(map.projectPath)
    || !isSha256(response.projectRevision) || response.projectRevision.toUpperCase() !== capture.projectRevision.toUpperCase()
    || response.projectXmlSHA !== map.projectXmlSHA
    || response.visualizationRevision !== map.expectedVisualizationRevision
    || response.mapID !== map.mapID || response.mapIndex !== map.mapIndex
    || response.mapName !== map.mapName || response.mapType !== map.mapType
    || response.contextToken !== capture.contextToken || !isSha256(response.featureContextToken)
    || response.mode !== mode || !Array.isArray(response.sampleLine)
    || !sameXYCoordinates(part.coordinates, response.sampleLine as Array<{ x: number; y: number }>)
    || !Array.isArray(response.series) || response.series.length === 0 || !Array.isArray(response.sideEffects)) return invalid();

  if (typeof feature.vectorPath !== 'string'
    || comparableMapSourcePath(feature.vectorPath) !== comparableMapSourcePath(source.vectorPath)
    || typeof feature.vectorRevision !== 'string' || feature.vectorRevision.toUpperCase() !== source.vectorRevision.toUpperCase()
    || feature.featureID !== source.featureID || feature.coordinateSHA !== coordinateSHA) return invalid();

  const outputs: MapSampleLineOutput[] = [];
  const outputKeys = new Set<string>();
  for (const rawOutput of response.series) {
    if (!isRecord(rawOutput) || !Array.isArray(rawOutput.selections) || rawOutput.selections.length === 0
      || !(rawOutput.selectedMapType === null || typeof rawOutput.selectedMapType === 'string')
      || !(rawOutput.profileIndex === null || (Number.isInteger(rawOutput.profileIndex) && Number(rawOutput.profileIndex) >= 0))
      || !isRecord(rawOutput.series)) return invalid();
    const computed = rawOutput.series;
    const quantity = mode === 'Flow' ? 'Flow' : 'Volume';
    if (typeof computed.resultPath !== 'string' || computed.resultPath.length === 0 || !isSha256(computed.revision)
      || typeof computed.resultName !== 'string' || computed.mode !== mode || computed.quantity !== quantity
      || typeof computed.units !== 'string' || typeof computed.available !== 'boolean'
      || !(computed.unavailableReason === null || typeof computed.unavailableReason === 'string')
      || !validatePointList(computed.points) || (!computed.available && computed.points.length !== 0)) return invalid();
    const selections: MapSampleLineOutput['selections'] = [];
    for (const rawSelection of rawOutput.selections) {
      if (!isRecord(rawSelection) || !isLocator(rawSelection.node)
        || !Number.isInteger(rawSelection.selectionIndex) || Number(rawSelection.selectionIndex) < 0) return invalid();
      const selection = rawSelection as unknown as MapSampleLineOutput['selections'][number];
      const override = capture.selectionOverrides.find((item) => locatorKey(item.node) === locatorKey(selection.node)
        && item.selectionIndex === selection.selectionIndex);
      if (override?.mode === 'Explicit' && (override.resultPath || override.resultRevision)) {
        if (!override.resultPath || !isSha256(override.resultRevision)
          || comparableMapSourcePath(override.resultPath) !== comparableMapSourcePath(computed.resultPath)
          || override.resultRevision.toUpperCase() !== computed.revision.toUpperCase()) return invalid();
      }
      selections.push(selection);
    }
    const output = {
      selections,
      selectedMapType: rawOutput.selectedMapType as string | null,
      profileIndex: rawOutput.profileIndex as number | null,
      series: computed as unknown as MapSampleLineComputedSeries,
    };
    const key = JSON.stringify([
      comparableMapSourcePath(output.series.resultPath), output.series.revision.toUpperCase(),
      output.selectedMapType, output.selections.map((item) => [locatorKey(item.node), item.selectionIndex]),
    ]);
    if (outputKeys.has(key)) return invalid();
    outputKeys.add(key);
    outputs.push(output);
  }

  if (!activeSelectionsCovered(capture, outputs)) return invalid();

  const sideEffects: MapSampleLineSideEffect[] = [];
  for (const rawEffect of response.sideEffects) {
    if (!isRecord(rawEffect) || typeof rawEffect.code !== 'string' || rawEffect.code.length === 0
      || !(rawEffect.path === null || typeof rawEffect.path === 'string')) return invalid();
    sideEffects.push(rawEffect as unknown as MapSampleLineSideEffect);
  }

  return {
    projectPath: response.projectPath,
    projectRevision: response.projectRevision as string,
    projectXmlSHA: response.projectXmlSHA as string,
    visualizationRevision: response.visualizationRevision as string,
    mapID: response.mapID as string,
    mapIndex: response.mapIndex as number,
    mapName: response.mapName as string,
    mapType: response.mapType as string,
    contextToken: response.contextToken as string,
    featureContextToken: response.featureContextToken as string,
    feature: feature as unknown as MapSampleLineResponse['feature'],
    mode,
    sampleLine: response.sampleLine as MapSampleLineResponse['sampleLine'],
    series: outputs,
    sideEffects,
  };
}

function featureIdentityError(
  capture: MapResultAnalysisCapture | null,
  source: MapSampleLineSource | null,
  language: 'zh' | 'en',
): string | null {
  if (!capture) return localized(language, '等待当前地图与 Result 选择快照。', 'Waiting for the active map and Result selection snapshot.');
  if (!source || source.ownerKey !== capture.ownerKey) return localized(language,
    '等待当前地图所有者对应的已核验折线要素。', 'Waiting for a checked polyline feature owned by the active map context.');
  if (source.mapType !== 'PolylinesVector' || !source.featureID.trim() || source.featureID.length > 1024
    || /[\u0000-\u001F\u007F-\u009F]/.test(source.featureID)
    || !source.layerID.trim() || !source.nodeID.trim()
    || !source.vectorPath.trim() || !isSha256(source.vectorRevision)) return localized(language,
    '当前要素缺少已保存折线来源、FID 或主文件 SHA。', 'The feature is missing its saved polyline source, FID, or primary-file SHA.');
  if (!capture.map.projectPath || !capture.map.projectXml || !isSha256(capture.projectRevision)
    || capture.projectXmlSHA.toUpperCase() !== capture.map.projectXmlSHA.toUpperCase()
    || capture.visualizationRevision.toUpperCase() !== capture.map.expectedVisualizationRevision.toUpperCase()
    || !isSha256(capture.map.projectXmlSHA)
    || !isSha256(capture.map.expectedVisualizationRevision) || !capture.map.mapID
    || !Number.isInteger(capture.map.mapIndex) || capture.map.mapIndex < 0 || !capture.map.projectSessionID
    || !isSha256(capture.contextToken)) return localized(language,
    '当前地图快照缺少已保存工程、session、地图或有效 revision。', 'The active map capture is missing its saved project, session, map, or valid revision.');
  if (!Array.isArray(capture.activeNode.selections) || !capture.activeNode.selections.some((selection) =>
    selection.selectedResult !== null)) return localized(language,
    '当前 Result 节点没有带有效路径和 SHA 的来源选择。', 'The active Result node has no source selection with a valid path and SHA.');
  if (capture.activeNode.selections.some((selection) => selection.selectedResult !== null
    && (!selection.selectedResult.resultPath || !isSha256(selection.selectedResult.revision)))) return localized(language,
    '一个或多个 Result 比较槽缺少来源路径或有效 SHA。', 'One or more Result comparison slots are missing a source path or valid SHA.');
  if (capture.activeNode.selections.some((selection) => selection.selectedResult?.isComputing)) return localized(language,
    '当前 Result 比较槽仍在计算，完成后再运行采样线。', 'A selected Result comparison slot is still computing. Run sample-line analysis after it finishes.');
  if (!Array.isArray(source.parts) || !source.parts.length) return localized(language, '选中折线没有可选的几何 part。', 'The selected polyline has no selectable geometry part.');
  return null;
}

function sideEffectDescription(effect: MapSampleLineSideEffect, language: 'zh' | 'en'): string {
  const description = effect.code === 'ResultPostProcessCacheMayBeCreatedOrUpdated'
    ? localized(language, '读取可能创建或更新 Result 后处理缓存', 'Reading may create or update a Result post-process cache')
    : effect.code;
  return effect.path ? `${description} · ${effect.path}` : description;
}

function outputDisambiguator(output: MapSampleLineOutput): string {
  return JSON.stringify(output.selections.map((item) => [locatorKey(item.node), item.selectionIndex]));
}

function resultPlot(output: MapSampleLineOutput, language: 'zh' | 'en'):
  { dataset: ResultDataset; series: ResultSeries; noDataCount: number } {
  return mapSampleLineSeriesToPlot(output.series, language, outputDisambiguator(output));
}

export function MapSampleLineAnalysisPanel({ backend, language, capture, sampleLineSource }: MapAnalysisPanelProps) {
  const identity = panelIdentity(capture, sampleLineSource);
  const identityRef = useRef(identity);
  identityRef.current = identity;
  const backendRef = useRef(backend);
  backendRef.current = backend;
  const requestRef = useRef<RequestState | null>(null);
  const generationRef = useRef(0);
  const [view, setView] = useState<PanelView>(() => initialView(identity));
  const active = view.identity === identity ? view : initialView(identity);
  const parts = validParts(sampleLineSource);
  const selectedPart = active.partIndex === null ? null : parts.find((part) => part.partIndex === active.partIndex) ?? null;
  const identityError = featureIdentityError(capture, sampleLineSource, language);
  const canCompute = !identityError && selectedPart !== null && backend.supports('map.resultSampleLine')
    && !active.computing && selectedPart.coordinates.length >= 2
    && selectedPart.coordinates.length <= MAP_SAMPLE_LINE_MAX_POINTS;

  useEffect(() => {
    const pending = requestRef.current;
    if (pending) {
      pending.controller.abort();
      requestRef.current = null;
    }
    generationRef.current += 1;
    setView(initialView(identity));
    return () => {
      const current = requestRef.current;
      if (current?.identity === identity) {
        current.controller.abort();
        requestRef.current = null;
      }
      generationRef.current += 1;
    };
  }, [identity, backend]);

  const invalidateRequest = (): void => {
    requestRef.current?.controller.abort();
    requestRef.current = null;
    generationRef.current += 1;
  };

  const updateView = (transform: (current: PanelView) => PanelView): void => {
    setView((current) => current.identity === identity ? transform(current) : current);
  };

  const compute = async (): Promise<void> => {
    if (!capture || !sampleLineSource || !selectedPart || !canCompute || identityError) return;
    const source = sampleLineSource;
    const part = selectedPart;
    const mode = active.mode;
    const controller = new AbortController();
    const generation = ++generationRef.current;
    const pending = { controller, generation, identity };
    requestRef.current?.controller.abort();
    requestRef.current = pending;
    const isCurrent = (): boolean => identityRef.current === identity && backendRef.current === backend && !controller.signal.aborted
      && generationRef.current === generation && requestRef.current === pending;
    updateView((current) => ({ ...current, computing: true, error: null, result: null }));

    try {
      // The standard JSON transport serializes -0 as 0. Hash the exact wire
      // coordinates, retaining every point and its original order.
      const wireCoordinates: XY[] = part.coordinates.map(([x, y]) => [x === 0 ? 0 : x, y === 0 ? 0 : y] as const);
      const wirePart = { ...part, coordinates: wireCoordinates };
      const coordinateSHA = await computeMapSampleLineCoordinateSHA(wireCoordinates);
      if (!isCurrent()) return;
      const request = {
        map: capture.map,
        contextToken: capture.contextToken,
        selectionOverrides: capture.selectionOverrides,
        feature: {
          vectorPath: source.vectorPath,
          vectorRevision: source.vectorRevision,
          featureID: source.featureID,
          coordinateSHA,
        },
        sampleLine: wireCoordinates.map(([x, y]) => ({ x, y })),
        mode,
      };
      const rawResponse = await backend.request<unknown>('map.resultSampleLine', request, { signal: controller.signal });
      if (!isCurrent()) return;
      const result = assertResponse(rawResponse, capture, source, wirePart, coordinateSHA, mode, language);
      if (!isCurrent()) return;
      updateView((current) => ({ ...current, computing: false, result, error: null }));
    } catch (error) {
      if (isCurrent()) updateView((current) => ({
        ...current,
        computing: false,
        result: null,
        error: error instanceof Error ? error.message : String(error),
      }));
    } finally {
      if (requestRef.current === pending) requestRef.current = null;
      if (isCurrent()) updateView((current) => ({ ...current, computing: false }));
    }
  };

  const result = active.result;
  const modeLabel = (mode: SampleLineMode): string => mode === 'Flow'
    ? localized(language, '流量', 'Flow')
    : localized(language, '累积水量', 'Cumulative volume');

  return <section className="results-insight-panel results-dynamic-profile map-sample-line-analysis" data-testid="map-result-sample-line-analysis">
    <div className="results-insight-heading">
      <div>
        <span className="results-section-index">XY</span>
        <div>
          <h2>{localized(language, '地图折线采样', 'Map polyline sampling')}</h2>
          <p>{localized(language,
            '使用当前地图中已核验并保存的折线几何，按单个折线分段计算结果原始时刻的流量或累积水量。',
            'Compute flow or cumulative volume at native Result times from one checked, persisted polyline part in the active map.')}</p>
        </div>
      </div>
      <span className="results-readonly-chip">{localized(language, '服务器计算', 'Server calculation')}</span>
    </div>

    {identityError && <p className="results-insight-state" role="status">{identityError}</p>}
    {sampleLineSource && !identityError && <>
      <div className="map-sample-line-source">
        <strong>{sampleLineSource.featureName || localized(language, '未命名折线', 'Unnamed polyline')}</strong>
        <span>{localized(language, '要素 ID', 'Feature ID')}: <code>{sampleLineSource.featureID}</code></span>
        <span title={sampleLineSource.vectorPath}>{localized(language, '来源', 'Source')}: <code>{sampleLineSource.vectorPath}</code></span>
        <span>{localized(language, '主文件 SHA', 'Primary-file SHA')}: <code>{sampleLineSource.vectorRevision.slice(0, 16)}…</code></span>
        <details><summary>{localized(language, '数据校验详情', 'Data validation details')}</summary><small>{localized(language,
          'Host 校验来源路径、主文件 SHA 与所选几何坐标哈希；当前契约不核验 FID 是否仍存在于源文件。',
          'The Host verifies source path, primary-file SHA, and selected-coordinate hash; the current contract does not verify that the FID still exists in the source file.')}</small></details>
      </div>
      <p className="map-sample-line-source-note">{localized(language,
        '计算时可能生成或更新结果缓存，完成后会显示缓存文件信息。',
        'Calculation does not save the project or .vis definition. Any Result post-process cache creation or update reported by the Host is listed in the response effects.')}</p>

      {parts.length === 0
        ? <p className="results-insight-state" role="status">{localized(language, '当前要素没有可用于采样的有效线段 part。', 'The selected feature has no valid line part for sampling.')}</p>
        : <div className="map-sample-line-controls">
          <label>
            <span>{localized(language, '几何 part', 'Geometry part')}</span>
            <select
              aria-label={localized(language, '选择折线 part', 'Choose polyline part')}
              data-testid="map-sample-line-part-select"
              value={active.partIndex === null ? '' : String(active.partIndex)}
              disabled={active.computing}
              onChange={(event) => {
                invalidateRequest();
                const partIndex = event.target.value === '' ? null : Number(event.target.value);
                updateView((current) => ({ ...current, partIndex, computing: false, result: null, error: null }));
              }}
            >
              <option value="">{localized(language, '请选择一个 part', 'Select one part')}</option>
              {parts.map((part) => <option key={part.partIndex} value={part.partIndex}>
                {localized(language, `Part ${part.partIndex + 1} · ${part.coordinates.length} 个点`, `Part ${part.partIndex + 1} · ${part.coordinates.length} points`)}
              </option>)}
            </select>
          </label>
          <label>
            <span>{localized(language, '计算量', 'Quantity')}</span>
            <select
              aria-label={localized(language, '采样线计算量', 'Sample-line quantity')}
              data-testid="map-sample-line-mode-select"
              value={active.mode}
              disabled={active.computing}
              onChange={(event) => {
                invalidateRequest();
                const mode: SampleLineMode = event.target.value === 'CumulativeVolume' ? 'CumulativeVolume' : 'Flow';
                updateView((current) => ({ ...current, mode, computing: false, result: null, error: null }));
              }}
            >
              <option value="Flow">{localized(language, '流量', 'Flow')}</option>
              <option value="CumulativeVolume">{localized(language, '累积水量', 'Cumulative volume')}</option>
            </select>
          </label>
          <div className="map-sample-line-actions">
            <span>{selectedPart ? localized(language, `已选 Part ${selectedPart.partIndex + 1}，${selectedPart.coordinates.length} 个原始顶点`, `Part ${selectedPart.partIndex + 1} selected, ${selectedPart.coordinates.length} native vertices`) : localized(language, '选择 part 后可计算。', 'Choose a part to calculate.')}</span>
            <button
              type="button"
              className="results-primary-button"
              data-testid="map-sample-line-compute"
              disabled={!canCompute}
              onClick={() => void compute()}
            >
              {active.computing ? localized(language, '正在计算…', 'Computing…') : localized(language, '计算采样线时间序列', 'Compute sample-line series')}
            </button>
          </div>
        </div>}
    </>}

    {!backend.supports('map.resultSampleLine') && <p className="results-insight-state" role="status">
      {localized(language, '当前 C# Host 未发布 map.resultSampleLine；采样计算尚未接通。', 'The C# Host does not advertise map.resultSampleLine; sampling is unavailable.')}
    </p>}
    {active.computing && <p className="results-insight-state" role="status">{localized(language, '正在准备采样请求并等待 Host 返回…', 'Preparing the sample-line request and waiting for the Host…')}</p>}
    {active.error && <p className="results-inline-error" role="alert">{active.error}</p>}

    {result && <div className="results-profile-results" data-testid="map-sample-line-results">
      <h3>{localized(language, `${modeLabel(result.mode)} · Result 原始时刻`, `${modeLabel(result.mode)} · native Result times`)}</h3>
      {result.series.map((output) => {
        const series = output.series;
        const key = `${comparableMapSourcePath(series.resultPath)}:${series.revision.toUpperCase()}:${outputDisambiguator(output)}`;
        if (!series.available) return <article key={key} className="results-profile-unavailable" data-testid="map-sample-line-unavailable">
          <strong>{series.resultName} · {series.quantity}</strong>
          <span>{series.unavailableReason || localized(language, '此 Result 未提供可计算序列。', 'This Result did not provide a computable series.')}</span>
        </article>;
        const plot = resultPlot(output, language);
        const selectionLabel = output.selections.map((selection) =>
          `${selection.node.nodeName} · ${localized(language, '槽位', 'slot')} ${selection.selectionIndex + 1}`).join('、');
        return <article className="results-profile-source-result map-sample-line-result" key={key} data-testid="map-sample-line-series">
          <header>
            <strong>{series.resultName} · {series.quantity}</strong>
            <span>{series.units || localized(language, '未提供单位', 'Unit unavailable')}</span>
            <code title={series.resultPath}>{series.revision.slice(0, 12)}…</code>
          </header>
          <p className="map-sample-line-series-meta">
            {selectionLabel}{output.selectedMapType ? ` · ${localized(language, '地图量', 'map type')} ${output.selectedMapType}` : ''}
            {output.profileIndex !== null ? ` · ${localized(language, '时刻索引', 'profile index')} ${output.profileIndex}` : ''}
          </p>
          <p className="map-sample-line-series-meta" title={series.resultPath}>{series.resultPath}</p>
          {series.points.length === 0
            ? <p className="results-insight-state">{localized(language, '来源可用，但没有原始输出时刻。', 'The source is available, but it returned no native output time steps.')}</p>
            : <>
              <p className="map-sample-line-series-meta" data-testid="map-sample-line-nodata-count">
                {localized(language, `原始时刻 ${series.points.length} 个 · NoData ${plot.noDataCount} 个`, `${series.points.length} native times · ${plot.noDataCount} NoData samples`)}
              </p>
              <ProfileSampleLinePlot dataset={plot.dataset} series={[plot.series]} language={language} />
            </>}
        </article>;
      })}
      <div className="map-sample-line-side-effects" data-testid="map-sample-line-side-effects">
        <strong>{localized(language, 'Host 返回的读取副作用', 'Read side effects returned by Host')}</strong>
        {result.sideEffects.length === 0
          ? <span>{localized(language, '本次响应未报告缓存副作用。', 'This response reported no cache side effects.')}</span>
          : <ul>{result.sideEffects.map((effect, index) => <li key={`${effect.code}:${effect.path ?? ''}:${index}`}>{sideEffectDescription(effect, language)}</li>)}</ul>}
      </div>
    </div>}
  </section>;
}
