import { useCallback, useEffect, useRef, useState } from 'react';
import type { BackendGateway, Language, WorkbenchContext } from '../../../shared/workbench';
import type { ResultDataset, ResultSeries } from '../data';
import type { ResultInspectorSnapshot } from './resultDetailsTypes';
import { ProfileSampleLinePlot } from './ProfileSampleLinePlot';

interface DynamicProfilePlotProps {
  backend: BackendGateway;
  backendRevision?: number;
  context: WorkbenchContext;
  snapshot: ResultInspectorSnapshot;
  language: Language;
}

interface ResultProfileSourceChoice {
  resultPath: string;
  revision: string;
  resultName: string;
  embeddedPlanName: string;
  profileCount: number;
  firstFlowAreaName: string | null;
  firstFlowAreaCellCount: number;
  firstFlowAreaFaceCount: number;
}

interface ResultProfileSourcesResult {
  projectPath: string;
  resultPath: string;
  revision: string;
  embeddedPlanName: string;
  firstFlowAreaName: string | null;
  firstFlowAreaCellCount: number;
  firstFlowAreaFaceCount: number;
  sources: ResultProfileSourceChoice[];
}

interface ResultProfileSampleLineSeries {
  resultPath: string;
  revision: string;
  resultName: string;
  mode: 'Flow' | 'CumulativeVolume';
  quantity: string;
  units: string;
  available: boolean;
  unavailableReason: string | null;
  points: Array<{ profileIndex: number; time: string; value: number | null }>;
}

interface ResultProfileSampleLineResult {
  projectPath: string;
  resultPath: string;
  revision: string;
  mode: 'Flow' | 'CumulativeVolume';
  sampleLine: Array<{ x: number; y: number }>;
  series: ResultProfileSampleLineSeries[];
}

interface CoordinateDraft { id: number; x: string; y: string }

interface ViewState {
  owner: string;
  sources: ResultProfileSourceChoice[];
  selectedKeys: string[];
  points: CoordinateDraft[];
  mode: 'Flow' | 'CumulativeVolume';
  sourcesLoading: boolean;
  computing: boolean;
  message: string | null;
  error: string | null;
  sourceError: string | null;
  result: ResultProfileSampleLineResult | null;
}

interface RequestState { controller: AbortController; generation: number }

function isSha(value: unknown): value is string {
  return typeof value === 'string' && /^[a-f\d]{64}$/i.test(value);
}

function comparablePath(value: string): string {
  const normalized = value.trim().replaceAll('\\', '/').replace(/\/+$/, '') || '/';
  return /^[a-z]:\//i.test(normalized) ? normalized.toLowerCase() : normalized;
}

function sourceKey(source: Pick<ResultProfileSourceChoice, 'resultPath' | 'revision'>): string {
  return JSON.stringify([comparablePath(source.resultPath), source.revision.toUpperCase()]);
}

function emptyView(owner: string): ViewState {
  return { owner, sources: [], selectedKeys: [], points: [{ id: 0, x: '', y: '' }, { id: 1, x: '', y: '' }], mode: 'Flow', sourcesLoading: false, computing: false, message: null, error: null, sourceError: null, result: null };
}

function sameCoordinates(left: Array<{ x: number; y: number }>, right: Array<{ x: number; y: number }>): boolean {
  return left.length === right.length && left.every((point, index) => point.x === right[index]?.x && point.y === right[index]?.y);
}

function validatePoints(value: unknown): value is ResultProfileSampleLineSeries['points'] {
  return Array.isArray(value) && value.every((point, index) => point && Number.isInteger(point.profileIndex)
    && point.profileIndex === index && typeof point.time === 'string'
    && (point.value === null || (typeof point.value === 'number' && Number.isFinite(point.value))));
}

function resultDataset(series: ResultProfileSampleLineSeries, language: Language): { dataset: ResultDataset; values: ResultSeries } {
  const seriesId = `sample-line:${comparablePath(series.resultPath)}:${series.revision.toUpperCase()}`;
  const values: ResultSeries = {
    id: seriesId,
    name: `${series.resultName} · ${series.quantity}`,
    units: series.units,
    values: series.points.map((point) => point.value),
  };
  return {
    dataset: {
      name: series.resultName,
      times: series.points.map((point) => point.time),
      series: [values],
      source: 'backend-result',
      sourceLabel: language === 'zh'
        ? (series.quantity === 'Flow' ? '结果文件中的断面流量' : '结果文件中的断面累积水量')
        : (series.quantity === 'Flow' ? 'Sample-line flow from Result output' : 'Sample-line accumulated volume from Result output'),
    },
    values,
  };
}

export function DynamicProfilePlot({ backend, backendRevision, context, snapshot, language }: DynamicProfilePlotProps) {
  const zh = language === 'zh';
  const selectedResultPath = snapshot.resultPath;
  const selectedRevision = snapshot.revision;
  const owner = JSON.stringify([context.projectSessionID, context.projectPath, context.projectRevision, context.projectXml, selectedResultPath, selectedRevision, backendRevision ?? null]);
  const ownerRef = useRef(owner);
  ownerRef.current = owner;
  const sourcesRequestRef = useRef<RequestState | null>(null);
  const computeRequestRef = useRef<RequestState | null>(null);
  const sourcesGenerationRef = useRef(0);
  const computeGenerationRef = useRef(0);
  const pointIDRef = useRef(2);
  const [view, setView] = useState<ViewState>(() => emptyView(owner));
  const active = view.owner === owner ? view : emptyView(owner);

  const loadSources = useCallback(async (): Promise<void> => {
    sourcesRequestRef.current?.controller.abort();
    computeRequestRef.current?.controller.abort();
    computeRequestRef.current = null;
    const controller = new AbortController();
    const generation = ++sourcesGenerationRef.current;
    const pending = { controller, generation };
    sourcesRequestRef.current = pending;
    const isCurrent = (): boolean => ownerRef.current === owner && !controller.signal.aborted
      && sourcesGenerationRef.current === generation && sourcesRequestRef.current === pending;
    setView({ ...emptyView(owner), sourcesLoading: true });
    if (!backend.supports('result.profileSources')) {
      setView({ ...emptyView(owner), sourceError: zh ? '当前 C# Host 未发布 result.profileSources，无法验证可用结果来源。' : 'The C# Host does not advertise result.profileSources, so eligible Result sources cannot be verified.' });
      sourcesRequestRef.current = null;
      return;
    }
    if (!context.projectPath || !context.projectXml || !isSha(selectedRevision)) {
      setView({ ...emptyView(owner), sourceError: zh ? '需要已保存工程、所选 Result 路径和有效 SHA 才能读取采样线来源。' : 'A saved project and a selected Result with a valid SHA are required to read sample-line sources.' });
      sourcesRequestRef.current = null;
      return;
    }
    try {
      const response = await backend.request<ResultProfileSourcesResult>('result.profileSources', {
        projectPath: context.projectPath,
        projectXml: context.projectXml,
        resultPath: selectedResultPath,
        revision: selectedRevision,
      }, { signal: controller.signal });
      if (!isCurrent()) return;
      if (comparablePath(response.projectPath) !== comparablePath(context.projectPath)
        || comparablePath(response.resultPath) !== comparablePath(selectedResultPath)
        || !isSha(response.revision) || response.revision.toUpperCase() !== selectedRevision.toUpperCase()
        || !Array.isArray(response.sources)) {
        throw new Error(zh ? '采样线来源返回的工程、Result 路径或 SHA 与当前选择不一致。' : 'The sample-line source response does not match the active project and Result identity.');
      }
      const seen = new Set<string>();
      for (const source of response.sources) {
        if (typeof source.resultPath !== 'string' || !source.resultPath || !isSha(source.revision)
          || typeof source.resultName !== 'string' || !Number.isInteger(source.profileCount)) {
          throw new Error(zh ? '采样线来源列表包含无效的路径、SHA 或元数据。' : 'The sample-line source list contains an invalid path, SHA, or metadata.');
        }
        const key = sourceKey(source);
        if (seen.has(key)) throw new Error(zh ? '采样线来源列表包含重复结果。' : 'The sample-line source list contains a duplicate Result.');
        seen.add(key);
      }
      const selectedKey = sourceKey({ resultPath: response.resultPath, revision: response.revision });
      if (!seen.has(selectedKey)) throw new Error(zh ? '后端来源目录未包含当前所选 Result。' : 'The backend source catalog does not include the selected Result.');
      setView({ ...emptyView(owner), sources: response.sources, selectedKeys: [selectedKey] });
    } catch (error) {
      if (isCurrent()) setView({ ...emptyView(owner), sourceError: error instanceof Error ? error.message : String(error) });
    } finally {
      if (sourcesRequestRef.current === pending) sourcesRequestRef.current = null;
    }
  }, [backend, context.projectPath, context.projectXml, owner, selectedResultPath, selectedRevision, zh]);

  useEffect(() => {
    pointIDRef.current = 2;
    setView(emptyView(owner));
    void loadSources();
    return () => {
      const sources = sourcesRequestRef.current;
      if (sources) { sources.controller.abort(); sourcesRequestRef.current = null; }
      const compute = computeRequestRef.current;
      if (compute) { compute.controller.abort(); computeRequestRef.current = null; }
      sourcesGenerationRef.current += 1;
      computeGenerationRef.current += 1;
    };
  }, [loadSources, owner]);

  const update = (transform: (current: ViewState) => ViewState): void => {
    setView((current) => current.owner === owner ? transform(current) : current);
  };

  const toggleSource = (source: ResultProfileSourceChoice, checked: boolean): void => {
    const key = sourceKey(source);
    const selectedKey = sourceKey({ resultPath: selectedResultPath, revision: selectedRevision });
    if (key === selectedKey) return;
    update((current) => {
      const selectedKeys = checked
        ? current.selectedKeys.length < 32 ? [...current.selectedKeys, key] : current.selectedKeys
        : current.selectedKeys.filter((item) => item !== key);
      return { ...current, selectedKeys, error: null, result: null };
    });
  };

  const compute = async (): Promise<void> => {
    if (!context.projectPath || !context.projectXml || !isSha(selectedRevision)
      || !backend.supports('result.profileSampleLine') || active.computing) return;
    const line = active.points.map((point) => ({ x: Number(point.x.trim()), y: Number(point.y.trim()) }));
    if (line.length < 2 || line.some((point, index) => !active.points[index]?.x.trim() || !active.points[index]?.y.trim() || !Number.isFinite(point.x) || !Number.isFinite(point.y))) {
      update((current) => ({ ...current, error: zh ? '输入至少两个有效的有限坐标点。' : 'Enter at least two valid finite coordinate points.', result: null }));
      return;
    }
    if (!line.some((point) => point.x !== line[0]?.x || point.y !== line[0]?.y)) {
      update((current) => ({ ...current, error: zh ? '采样线必须包含不同的坐标点。' : 'The sample line must contain distinct coordinates.', result: null }));
      return;
    }
    const selectedSources = active.sources.filter((source) => active.selectedKeys.includes(sourceKey(source)));
    const selectedKey = sourceKey({ resultPath: selectedResultPath, revision: selectedRevision });
    if (!selectedSources.some((source) => sourceKey(source) === selectedKey) || selectedSources.length > 32) {
      update((current) => ({ ...current, error: zh ? '来源选择必须包含当前 Result，且最多 32 个结果。' : 'Include the selected Result and choose no more than 32 sources.', result: null }));
      return;
    }
    if (!selectedSources.some((source) => sourceKey(source) === selectedKey && comparablePath(source.resultPath) === comparablePath(selectedResultPath) && source.revision.toUpperCase() === selectedRevision.toUpperCase())) {
      update((current) => ({ ...current, error: zh ? '所选 Result 的 SHA 与来源目录不一致。' : 'The selected Result SHA does not match the source catalog.', result: null }));
      return;
    }
    computeRequestRef.current?.controller.abort();
    const controller = new AbortController();
    const generation = ++computeGenerationRef.current;
    const pending = { controller, generation };
    computeRequestRef.current = pending;
    const requestOwner = owner;
    const isCurrent = (): boolean => ownerRef.current === requestOwner && !controller.signal.aborted
      && computeGenerationRef.current === generation && computeRequestRef.current === pending;
    update((current) => ({ ...current, computing: true, error: null, message: zh ? 'C# 正在计算显式采样线时间序列…' : 'C# is computing the explicit sample-line time series…', result: null }));
    try {
      const response = await backend.request<ResultProfileSampleLineResult>('result.profileSampleLine', {
        projectPath: context.projectPath,
        projectXml: context.projectXml,
        resultPath: selectedResultPath,
        revision: selectedRevision,
        sources: selectedSources.map(({ resultPath, revision }) => ({ resultPath, revision })),
        sampleLine: line,
        mode: active.mode,
      }, {
        signal: controller.signal,
        onProgress: (progress) => {
          if (!isCurrent() || !progress || typeof progress !== 'object') return;
          const value = progress as Record<string, unknown>;
          const detail = typeof value.message === 'string' ? value.message : typeof value.taskName === 'string' ? value.taskName : null;
          if (detail) update((current) => ({ ...current, message: detail }));
        },
      });
      if (!isCurrent()) return;
      if (comparablePath(response.projectPath) !== comparablePath(context.projectPath)
        || comparablePath(response.resultPath) !== comparablePath(selectedResultPath)
        || !isSha(response.revision) || response.revision.toUpperCase() !== selectedRevision.toUpperCase()
        || response.mode !== active.mode || !Array.isArray(response.sampleLine) || !sameCoordinates(response.sampleLine, line)
        || !Array.isArray(response.series) || response.series.length !== selectedSources.length) {
        throw new Error(zh ? '采样线响应的工程、Result、SHA、模式或坐标与当前请求不一致。' : 'The sample-line response identity or coordinates do not match the request.');
      }
      const expectedByKey = new Map<string, ResultProfileSourceChoice>(selectedSources.map((source) => [sourceKey(source), source] as const));
      const responseKeys = new Set<string>();
      for (const series of response.series) {
        const key = sourceKey(series);
        const expected = expectedByKey.get(key);
        if (!expected || responseKeys.has(key) || comparablePath(series.resultPath) !== comparablePath(expected.resultPath)
          || series.revision.toUpperCase() !== expected.revision.toUpperCase() || series.mode !== active.mode
          || typeof series.resultName !== 'string' || typeof series.quantity !== 'string' || typeof series.units !== 'string'
          || series.quantity !== (active.mode === 'Flow' ? 'Flow' : 'Volume')
          || typeof series.available !== 'boolean'
          || !(series.unavailableReason === null || typeof series.unavailableReason === 'string')
          || !validatePoints(series.points) || (!series.available && series.points.length !== 0)) {
          throw new Error(zh ? '采样线序列包含不匹配的来源身份、模式或时步数据。' : 'A sample-line series has a mismatched source identity, mode, or time-step payload.');
        }
        responseKeys.add(key);
      }
      update((current) => ({ ...current, result: response, message: null, error: null }));
    } catch (error) {
      if (isCurrent()) update((current) => ({ ...current, error: error instanceof Error ? error.message : String(error), message: null, result: null }));
    } finally {
      if (computeRequestRef.current === pending) {
        computeRequestRef.current = null;
        update((current) => ({ ...current, computing: false, message: null }));
      }
    }
  };

  const selectedResultKey = sourceKey({ resultPath: selectedResultPath, revision: selectedRevision });
  const validInput = active.points.length >= 2 && active.points.every((point) => point.x.trim() !== '' && point.y.trim() !== '' && Number.isFinite(Number(point.x)) && Number.isFinite(Number(point.y)));
  const selectedCount = active.sources.filter((source) => active.selectedKeys.includes(sourceKey(source))).length;

  return <section className="results-insight-panel results-dynamic-profile" data-testid="result-profile-sample-line">
    <div className="results-insight-heading">
      <div><span className="results-section-index">04</span><div><h2>{zh ? '动态采样线结果' : 'Dynamic sample-line result'}</h2><p>{zh ? '输入明确的工程坐标线，由 C# 对所选 Result 计算流量或累积体积随原始输出时刻变化的时间序列。此图不是网格元素空间剖面。' : 'Enter an explicit project-coordinate line. C# computes flow or cumulative volume over native output times for the selected Results. This is a time series, not a spatial mesh profile.'}</p></div></div>
      <span className="results-readonly-chip">{zh ? '计算读取' : 'Computed read'}</span>
    </div>
    {active.sourceError && <p className="results-insight-state" role="status">{active.sourceError}</p>}
    {active.sourcesLoading && <p className="results-insight-state" role="status">{zh ? '正在读取当前方案可用的 Result 来源…' : 'Reading eligible Result sources for the active Plan…'}</p>}
    {active.sources.length > 0 && <>
      <div className="results-profile-input-grid">
        <fieldset className="results-profile-sources">
          <legend>{zh ? 'Result 来源' : 'Result sources'}</legend>
          <p>{zh ? '当前 Result 必选；其他选项来自相同方案名称匹配目录。最多选择 32 个。' : 'The active Result is required. Other choices are from the same Plan name-matched catalog. Select up to 32.'}</p>
          {active.sources.map((source) => {
            const key = sourceKey(source);
            const checked = active.selectedKeys.includes(key);
            const required = key === selectedResultKey;
            const atLimit = !checked && selectedCount >= 32;
            return <label key={key} className="results-profile-source-option">
              <input type="checkbox" checked={checked} disabled={required || atLimit || active.computing} onChange={(event) => toggleSource(source, event.target.checked)} />
              <span><strong>{source.resultName}</strong><small>{source.embeddedPlanName || (zh ? '未关联方案' : 'No embedded Plan')} · {source.revision.slice(0, 12)}…</small></span>
              {required && <em>{zh ? '当前' : 'active'}</em>}
            </label>;
          })}
        </fieldset>
        <div className="results-profile-line-editor">
          <label className="results-field"><span>{zh ? '计算量' : 'Quantity'}</span><select value={active.mode} disabled={active.computing} onChange={(event) => update((current) => ({ ...current, mode: event.target.value as ViewState['mode'], result: null, error: null }))}>
            <option value="Flow">{zh ? '流量' : 'Flow'}</option>
            <option value="CumulativeVolume">{zh ? '累积体积' : 'Cumulative volume'}</option>
          </select></label>
          <p className="results-profile-coordinate-note">{zh ? '坐标为原样输入的工程 XY；至少需要两个有效点。此控件不读取地图点击，也不推测 CRS。' : 'Coordinates are the project XY values you enter. At least two valid points are required. This control does not read map clicks or infer a CRS.'}</p>
          <div className="results-profile-points">
            {active.points.map((point, index) => <div key={point.id} className="results-profile-point-row">
              <span>{zh ? `点 ${index + 1}` : `Point ${index + 1}`}</span>
              <label><span>X</span><input inputMode="decimal" value={point.x} aria-label={`X ${index + 1}`} disabled={active.computing} onChange={(event) => update((current) => ({ ...current, points: current.points.map((item) => item.id === point.id ? { ...item, x: event.target.value } : item), result: null, error: null }))} /></label>
              <label><span>Y</span><input inputMode="decimal" value={point.y} aria-label={`Y ${index + 1}`} disabled={active.computing} onChange={(event) => update((current) => ({ ...current, points: current.points.map((item) => item.id === point.id ? { ...item, y: event.target.value } : item), result: null, error: null }))} /></label>
              {active.points.length > 2 && <button type="button" disabled={active.computing} aria-label={zh ? '删除点' : 'Remove point'} onClick={() => update((current) => ({ ...current, points: current.points.filter((item) => item.id !== point.id), result: null }))}>×</button>}
            </div>)}
          </div>
          <div className="results-profile-actions">
            <button type="button" className="results-secondary-button" disabled={active.computing || active.points.length >= 256} onClick={() => {
              const id = pointIDRef.current++;
              update((current) => ({ ...current, points: [...current.points, { id, x: '', y: '' }], result: null }));
            }}>{zh ? '添加坐标点' : 'Add coordinate'}</button>
            <button type="button" className="results-primary-button" disabled={!validInput || active.computing || active.sourcesLoading || !backend.supports('result.profileSampleLine') || selectedCount === 0 || selectedCount > 32} onClick={() => void compute()}>
              {active.computing ? (zh ? '正在计算…' : 'Computing…') : (zh ? '计算采样线时间序列' : 'Compute sample-line series')}
            </button>
          </div>
        </div>
      </div>
      {!backend.supports('result.profileSampleLine') && <p className="results-insight-state">{zh ? '当前 C# Host 未发布 result.profileSampleLine。' : 'The C# Host does not advertise result.profileSampleLine.'}</p>}
      {active.message && <p className="results-insight-state" role="status">{active.message}</p>}
      {active.error && <p className="results-inline-error" role="alert">{active.error}</p>}
      {active.result && <div className="results-profile-results">
        <h3>{zh ? '采样线时间序列' : 'Sample-line time series'}</h3>
        {active.result.series.map((series) => {
          if (!series.available) return <article key={sourceKey(series)} className="results-profile-unavailable" data-testid="result-profile-sample-line-unavailable">
            <strong>{series.resultName} · {series.quantity}</strong><span>{series.unavailableReason || (zh ? '此来源未提供可计算序列。' : 'This source did not provide a computable series.')}</span>
          </article>;
          const payload = resultDataset(series, language);
          return <article className="results-profile-source-result" key={sourceKey(series)}>
            <header><strong>{series.resultName} · {series.quantity}</strong><span>{series.units || (zh ? '未提供单位' : 'Unit unavailable')}</span><code>{series.revision.slice(0, 12)}…</code></header>
            {series.points.length === 0 ? <p className="results-insight-state">{zh ? '后端返回了可用来源，但没有输出时步。' : 'The source is available, but no output time steps were returned.'}</p> : <ProfileSampleLinePlot dataset={payload.dataset} series={[payload.values]} language={language} />}
          </article>;
        })}
      </div>}
    </>}
  </section>;
}
