import { useEffect, useMemo, useRef, useState } from 'react';
import type { CSSProperties } from 'react';
import type { BackendTransportState } from '../../../shared/workbench';
import { message } from '../../../shared/workbench';
import type { MapAnalysisPanelProps } from './mapAnalysisTypes';
import type { MeshElementKind, MeshTimeSeriesExecution, MeshTimeSeriesInspection } from './mapMeshTimeSeriesHelpers';
import {
  createMeshTimeSeriesChartData,
  isMeshHitForCapture,
  meshTimeSeriesCaptureIdentity,
  meshTimeSeriesOwner,
  parseMeshTimeSeriesExecution,
  parseMeshTimeSeriesInspection,
} from './mapMeshTimeSeriesHelpers';
import { TimeSeriesPayloadChart } from '../../results/controls/TimeSeriesPayloadChart';
import type { ChartSettings } from '../../results/controls/TimeSeriesPayloadChart';
import '../map.css';

type PanelPhase = 'loading' | 'ready' | 'noMesh' | 'unavailable' | 'failed';
type ExecutionPhase = 'running' | 'ready' | 'noMatchingMesh' | 'cancelled' | 'failed' | 'unconfirmed';

interface InspectionState {
  ownerIdentity: string;
  phase: PanelPhase;
  inspection?: MeshTimeSeriesInspection;
  error?: unknown;
}

interface ExecutionState {
  ownerIdentity: string;
  requestKey: string;
  phase: ExecutionPhase;
  result?: MeshTimeSeriesExecution;
  error?: unknown;
  progress?: OperationProgress;
  transport?: BackendTransportState;
  cancelRequested?: boolean;
}

interface OperationProgress {
  fraction?: number;
  taskName?: string;
  message?: string;
}

interface FormState {
  ownerIdentity: string;
  flowAreaIndex: string;
  elementKind: MeshElementKind;
  elementIndex: string;
  source: 'auto' | 'mesh-hit' | 'manual';
}

const DEFAULT_CHART_SETTINGS: ChartSettings = { grid: true, markers: false, step: false, yMin: '', yMax: '' };

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isAbortError(value: unknown): boolean {
  return isRecord(value) && value.name === 'AbortError';
}

function errorHasConfirmedTerminal(value: unknown): boolean {
  return isRecord(value) && value.terminalConfirmed === true;
}

function parseProgress(value: unknown): OperationProgress | undefined {
  if (!isRecord(value)) return undefined;
  const fraction = typeof value.fraction === 'number' && Number.isFinite(value.fraction)
    ? Math.max(0, Math.min(1, value.fraction)) : undefined;
  const taskName = typeof value.taskName === 'string' ? value.taskName : undefined;
  const messageValue = typeof value.message === 'string' ? value.message : undefined;
  if (fraction === undefined && taskName === undefined && messageValue === undefined) return undefined;
  return { fraction, taskName, message: messageValue };
}

function localized(language: 'zh' | 'en', zh: string, en: string): string {
  return language === 'zh' ? zh : en;
}

function initialForm(ownerIdentity: string): FormState {
  // Kept as a small constructor so owner changes discard every prior manual index.
  return { ownerIdentity, flowAreaIndex: '', elementKind: 'Cell', elementIndex: '', source: 'auto' };
}

function parseElementIndex(raw: string): number | null {
  if (!/^\d+$/.test(raw)) return null;
  const value = Number(raw);
  return Number.isSafeInteger(value) ? value : null;
}

function transportText(state: BackendTransportState, language: 'zh' | 'en'): string {
  if (language === 'en') {
    switch (state.state) {
      case 'reconnecting': return `Reconnecting to operation ${state.operationId} (attempt ${state.attempt})…`;
      case 'connected': return `Reconnected to operation ${state.operationId}.`;
      case 'cancelRequested': return 'Cancellation requested; waiting for the Host terminal state.';
      case 'cancelRetrying': return `Retrying cancellation request (attempt ${state.attempt}).`;
      case 'cancelSent': return state.accepted ? 'Cancellation request delivered; waiting for the Host terminal state.' : 'The Host has not accepted cancellation; waiting for the operation terminal state.';
      case 'cancelFailed': return `Cancellation delivery failed (${state.code}); waiting for the operation terminal state.`;
    }
  }
  switch (state.state) {
    case 'reconnecting': return `正在重连操作 ${state.operationId}（第 ${state.attempt} 次）…`;
    case 'connected': return `已重新连接操作 ${state.operationId}。`;
    case 'cancelRequested': return '已请求取消，正在等待 Host 确认终态。';
    case 'cancelRetrying': return `正在重试发送取消请求（第 ${state.attempt} 次）。`;
    case 'cancelSent': return state.accepted ? '取消请求已送达，正在等待 Host 确认终态。' : 'Host 尚未接受取消请求，仍在等待操作终态。';
    case 'cancelFailed': return `取消请求送达失败（${state.code}），仍在等待操作终态。`;
  }
}

function sampleText(value: number | null | undefined): string {
  return value === null || value === undefined || !Number.isFinite(value) ? '—' : String(value);
}

/** Reads a Result-owned Flow Area mesh series for an exact zero-based Cell or Face index. */
export function MapMeshTimeSeriesPanel({ backend, language, capture, meshHit }: MapAnalysisPanelProps) {
  const ownerIdentity = meshTimeSeriesCaptureIdentity(capture);
  const hitIsCurrent = isMeshHitForCapture(meshHit, capture);
  const hitKey = hitIsCurrent && meshHit
    ? JSON.stringify([meshHit.flowAreaIndex, meshHit.elementKind, meshHit.elementIndex, meshHit.geometryPath, meshHit.geometryRevision])
    : '';

  const captureRef = useRef(capture);
  const meshHitRef = useRef(meshHit);
  const meshHitIsCurrentRef = useRef(hitIsCurrent);
  const ownerIdentityRef = useRef(ownerIdentity);
  captureRef.current = capture;
  meshHitRef.current = meshHit;
  meshHitIsCurrentRef.current = hitIsCurrent;
  ownerIdentityRef.current = ownerIdentity;

  const [capabilityRevision, setCapabilityRevision] = useState(0);
  const [inspectAttempt, setInspectAttempt] = useState(0);
  const [inspectionState, setInspectionState] = useState<InspectionState | null>(null);
  const [formState, setFormState] = useState<FormState | null>(null);
  const [executionState, setExecutionState] = useState<ExecutionState | null>(null);
  const [selectedTimeIndex, setSelectedTimeIndex] = useState(0);
  const [chartRange, setChartRange] = useState<[number, number]>([0, 1]);
  const [chartSettings, setChartSettings] = useState<ChartSettings>(DEFAULT_CHART_SETTINGS);
  const inspectionController = useRef<AbortController | null>(null);
  const executionController = useRef<AbortController | null>(null);
  const executionTransportID = useRef<string | null>(null);
  const executionGeneration = useRef(0);

  useEffect(() => backend.subscribeCapabilities?.(() => setCapabilityRevision((value) => value + 1)), [backend]);

  useEffect(() => {
    setFormState((current) => {
      if (!ownerIdentity) return null;
      if (current?.ownerIdentity !== ownerIdentity) {
        const next = initialForm(ownerIdentity);
        const hit = meshHitRef.current;
        if (meshHitIsCurrentRef.current && hit) {
          return { ...next, flowAreaIndex: String(hit.flowAreaIndex), elementKind: hit.elementKind,
            elementIndex: String(hit.elementIndex), source: 'mesh-hit' };
        }
        return next;
      }
      const hit = meshHitRef.current;
      if (current.source === 'auto' && meshHitIsCurrentRef.current && hit) {
        return { ...current, flowAreaIndex: String(hit.flowAreaIndex), elementKind: hit.elementKind,
          elementIndex: String(hit.elementIndex), source: 'mesh-hit' };
      }
      return current;
    });
  }, [ownerIdentity, hitKey]);

  useEffect(() => {
    return () => {
      inspectionController.current?.abort('The map Result owner changed or panel closed.');
      executionController.current?.abort('The map Result owner changed or panel closed.');
      executionGeneration.current += 1;
    };
  }, [ownerIdentity]);

  useEffect(() => {
    inspectionController.current?.abort('A newer mesh inspection started.');
    inspectionController.current = null;
    setInspectionState(null);
    setExecutionState(null);
    if (!ownerIdentity) return;
    if (!backend.supports('map.result.meshTimeSeries.inspect')) {
      setInspectionState({ ownerIdentity, phase: 'unavailable' });
      return;
    }
    const currentCapture = captureRef.current;
    const currentOwner = currentCapture ? meshTimeSeriesOwner(currentCapture) : null;
    if (!currentCapture || !currentOwner || meshTimeSeriesCaptureIdentity(currentCapture) !== ownerIdentity) return;
    const controller = new AbortController();
    inspectionController.current = controller;
    setInspectionState({ ownerIdentity, phase: 'loading' });
    void backend.request<unknown>('map.result.meshTimeSeries.inspect', { owner: currentOwner }, { signal: controller.signal })
      .then((raw) => parseMeshTimeSeriesInspection(raw, currentCapture))
      .then((inspection) => {
        if (controller.signal.aborted || ownerIdentityRef.current !== ownerIdentity) return;
        setInspectionState({ ownerIdentity, phase: inspection.state, inspection });
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted || ownerIdentityRef.current !== ownerIdentity || isAbortError(error)) return;
        setInspectionState({ ownerIdentity, phase: 'failed', error });
      });
    return () => {
      controller.abort('The map Result owner changed during mesh inspection.');
      if (inspectionController.current === controller) inspectionController.current = null;
    };
  }, [backend, ownerIdentity, inspectAttempt, capabilityRevision]);

  useEffect(() => {
    setSelectedTimeIndex(0);
    setChartRange([0, 1]);
    setChartSettings(DEFAULT_CHART_SETTINGS);
  }, [executionState?.requestKey, executionState?.result?.operationID]);

  const currentInspectionState = inspectionState?.ownerIdentity === ownerIdentity ? inspectionState : null;
  const inspection = currentInspectionState?.inspection ?? null;
  const form = formState?.ownerIdentity === ownerIdentity
    ? formState
    : ownerIdentity ? initialForm(ownerIdentity) : null;
  const flowAreaIndex = form?.flowAreaIndex === '' ? null : Number(form?.flowAreaIndex);
  const selectedFlowArea = inspection && flowAreaIndex !== null && Number.isInteger(flowAreaIndex)
    ? inspection.flowAreas.find((area) => area.flowAreaIndex === flowAreaIndex) ?? null
    : null;
  const elementIndex = parseElementIndex(form?.elementIndex ?? '');
  const elementCount = selectedFlowArea
    ? form?.elementKind === 'Cell' ? selectedFlowArea.cellCount : selectedFlowArea.faceCount
    : 0;
  const elementIndexIsValid = Boolean(selectedFlowArea?.hasMesh && elementIndex !== null && elementIndex < elementCount);
  const requestKey = ownerIdentity && selectedFlowArea && form && elementIndexIsValid && elementIndex !== null
    ? JSON.stringify([ownerIdentity, selectedFlowArea.flowAreaIndex, selectedFlowArea.flowAreaID, selectedFlowArea.meshHash, form.elementKind, elementIndex])
    : null;
  const currentExecution = executionState?.ownerIdentity === ownerIdentity && executionState.requestKey === requestKey
    ? executionState
    : null;
  const executionBusy = currentExecution?.phase === 'running';
  const hasExecute = backend.supports('map.result.meshTimeSeries.execute');
  const canExecute = Boolean(ownerIdentity && inspection?.state === 'ready' && selectedFlowArea?.hasMesh &&
    requestKey && elementIndex !== null && hasExecute && !executionBusy);
  const chartPresentation = useMemo(() => {
    if (currentExecution?.phase !== 'ready' || !currentExecution.result) return null;
    try {
      return { data: createMeshTimeSeriesChartData(currentExecution.result, language), error: null as unknown };
    } catch (error) {
      return { data: null, error };
    }
  }, [currentExecution?.phase, currentExecution?.result, language]);
  const chartData = chartPresentation?.data ?? null;
  const chartError = chartPresentation?.error ?? null;
  const dataset = chartData?.dataset ?? null;
  const visibleSeries = chartData?.series ?? [];
  const boundedTimeIndex = dataset && dataset.times.length > 0
    ? Math.max(0, Math.min(dataset.times.length - 1, selectedTimeIndex))
    : 0;

  const currentFormUpdate = (update: (current: FormState) => FormState): void => {
    if (!ownerIdentity || !form) return;
    setFormState((current) => {
      const base = current?.ownerIdentity === ownerIdentity ? current : form;
      return update(base);
    });
  };

  const applyMeshHit = (): void => {
    if (!hitIsCurrent || !meshHit || !ownerIdentity) return;
    setFormState({ ownerIdentity, flowAreaIndex: String(meshHit.flowAreaIndex), elementKind: meshHit.elementKind,
      elementIndex: String(meshHit.elementIndex), source: 'mesh-hit' });
  };

  const execute = async (): Promise<void> => {
    if (!canExecute || !requestKey || !ownerIdentity || !capture || !inspection || !selectedFlowArea || !form || elementIndex === null) return;
    const requestOwner = meshTimeSeriesOwner(capture);
    if (!requestOwner || meshTimeSeriesCaptureIdentity(capture) !== ownerIdentity) return;
    const operationID = crypto.randomUUID();
    const selection = { flowArea: selectedFlowArea, elementKind: form.elementKind, elementIndex, operationID };
    const controller = new AbortController();
    executionController.current?.abort('A newer mesh element request started.');
    executionController.current = controller;
    executionTransportID.current = null;
    const generation = ++executionGeneration.current;
    const current = (): boolean => generation === executionGeneration.current &&
      ownerIdentityRef.current === ownerIdentity && !controller.signal.aborted;
    let terminalResultReceived = false;
    setExecutionState({ ownerIdentity, requestKey, phase: 'running', cancelRequested: false });
    try {
      const raw = await backend.request<unknown>('map.result.meshTimeSeries.execute', {
        operationID,
        owner: requestOwner,
        flowAreaIndex: selectedFlowArea.flowAreaIndex,
        flowAreaID: selectedFlowArea.flowAreaID,
        expectedFlowAreaName: selectedFlowArea.name,
        elementKind: form.elementKind,
        elementIndex,
      }, {
        signal: controller.signal,
        onProgress: (progress) => {
          if (!current()) return;
          const parsed = parseProgress(progress);
          if (parsed) setExecutionState((state) => state?.ownerIdentity === ownerIdentity && state.requestKey === requestKey && state.phase === 'running'
            ? { ...state, progress: parsed } : state);
        },
        onTransportState: (state) => {
          if (generation !== executionGeneration.current || ownerIdentityRef.current !== ownerIdentity) return;
          // Keep the accepted HTTP task identity through reconnects and cancellation.
          executionTransportID.current = state.operationId;
          setExecutionState((previous) => previous?.ownerIdentity === ownerIdentity && previous.requestKey === requestKey && previous.phase === 'running'
            ? { ...previous, transport: state, cancelRequested: previous.cancelRequested || controller.signal.aborted || state.state === 'cancelRequested' || state.state === 'cancelRetrying' || state.state === 'cancelSent' }
            : previous);
        },
      });
      terminalResultReceived = true;
      if (ownerIdentityRef.current !== ownerIdentity || generation !== executionGeneration.current) return;
      const result = parseMeshTimeSeriesExecution(raw, capture, inspection, selection);
      if (ownerIdentityRef.current !== ownerIdentity || generation !== executionGeneration.current) return;
      setExecutionState({ ownerIdentity, requestKey, phase: result.state, result, cancelRequested: controller.signal.aborted });
    } catch (error) {
      if (generation !== executionGeneration.current || ownerIdentityRef.current !== ownerIdentity) return;
      if (isAbortError(error) && executionTransportID.current) {
        setExecutionState({ ownerIdentity, requestKey, phase: 'cancelled' });
      } else if (terminalResultReceived || errorHasConfirmedTerminal(error)) {
        setExecutionState({ ownerIdentity, requestKey, phase: 'failed', error });
      } else {
        setExecutionState({ ownerIdentity, requestKey, phase: 'unconfirmed', error });
      }
    } finally {
      if (generation === executionGeneration.current && executionController.current === controller) executionController.current = null;
    }
  };

  const cancel = (): void => {
    const controller = executionController.current;
    if (!controller || controller.signal.aborted || !executionBusy) return;
    setExecutionState((current) => current?.ownerIdentity === ownerIdentity && current.requestKey === requestKey && current.phase === 'running'
      ? { ...current, cancelRequested: true }
      : current);
    controller.abort('User requested mesh time-series cancellation.');
  };

  const changeFlowArea = (raw: string): void => currentFormUpdate((current) => ({ ...current, flowAreaIndex: raw, elementIndex: '', source: 'manual' }));
  const changeElementKind = (kind: MeshElementKind): void => currentFormUpdate((current) => ({ ...current, elementKind: kind, elementIndex: '', source: 'manual' }));
  const changeElementIndex = (raw: string): void => currentFormUpdate((current) => ({ ...current, elementIndex: raw, source: 'manual' }));

  const selectedSampleTime = dataset?.times[boundedTimeIndex] ?? null;
  const selectedSampleValues = visibleSeries.map((series) => ({ series, value: series.values[boundedTimeIndex] ?? null }));

  return <section className="map-inspector-section" aria-label={localized(language, '网格要素时间序列', 'Mesh element time series')}
    data-testid="map-mesh-timeseries-panel" style={{ padding: '10px 12px 14px' }}>
    <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', gap: 10 }}>
      <h3 style={{ margin: '0 0 8px', fontSize: 13 }}>{localized(language, '网格要素时间序列', 'Mesh element time series')}</h3>
      {capture && <small style={{ color: '#6e8290' }}>{capture.activeNode.name} · {capture.slotZero?.selectedResult?.resultName ?? ''}</small>}
    </div>

    {!ownerIdentity && <p className="map-page-notice" role="status">{localized(language,
      '请先选择已载入的 Result 或 MultiResult，确保槽位 0 已选中已完成的 Result。',
      'Select a loaded Result or MultiResult with a completed Result in slot zero.')}</p>}
    {ownerIdentity && currentInspectionState?.phase === 'loading' && <p className="map-page-notice" role="status">{localized(language, '正在检查当前 Result 的 Flow Area 网格…', 'Inspecting Flow Area meshes in the current Result…')}</p>}
    {ownerIdentity && currentInspectionState?.phase === 'unavailable' && <p className="map-page-notice" role="status">{localized(language,
      '当前服务未发布网格要素时间序列检查操作。', 'The active service has not advertised mesh time-series inspection.')}</p>}
    {ownerIdentity && currentInspectionState?.phase === 'failed' && <div className="map-page-notice map-error-notice" role="alert">
      <span>!</span><div>{localized(language, '无法检查当前 Result 的网格来源。', 'Could not inspect the current Result mesh source.')}
        {currentInspectionState.error !== undefined && <details><summary>{localized(language, '查看诊断', 'View diagnostic')}</summary><pre style={{ whiteSpace: 'pre-wrap' }}>{message(currentInspectionState.error, language)}</pre></details>}
      </div><button type="button" onClick={() => setInspectAttempt((value) => value + 1)}>{localized(language, '重试', 'Retry')}</button>
    </div>}
    {ownerIdentity && currentInspectionState?.phase === 'noMesh' && <p className="map-page-notice" role="status">{localized(language,
      '当前 Result 的 Flow Area 图层没有可读取的网格。', 'The current Result has no readable Flow Area mesh.')}</p>}

    {inspection && form && <>
      <dl className="map-feature-properties" style={{ display: 'grid', gridTemplateColumns: 'minmax(100px, .7fr) minmax(0, 1.7fr)', gap: '4px 12px', margin: 0 }}>
        <dt>{localized(language, 'Result 来源', 'Result source')}</dt><dd style={definitionValueStyle}><code>{capture?.slotZero?.selectedResult?.resultPath ?? ''}</code></dd>
        <dt>{localized(language, 'Result 修订', 'Result revision')}</dt><dd style={definitionValueStyle}><code>{capture?.slotZero?.selectedResult?.revision ?? ''}</code></dd>
        <dt>{localized(language, '匹配网格的 Result', 'Results with matching mesh')}</dt><dd style={definitionValueStyle}>{inspection.matchingResultNames.length ? inspection.matchingResultNames.join('、') : '—'}</dd>
        {inspection.skippedComputingResultCount > 0 && <><dt>{localized(language, '仍在计算而跳过', 'Skipped while computing')}</dt><dd style={definitionValueStyle}>{inspection.skippedComputingResultCount}</dd></>}
      </dl>

      <div className="map-property-feature" style={{ margin: '8px 0 0', padding: 9 }}>
        <strong>{localized(language, '来源 Flow Area 与网格身份', 'Source Flow Areas and mesh identity')}</strong>
        {inspection.flowAreas.length > 0 ? <div style={{ overflowX: 'auto', marginTop: 6 }}>
          <table style={{ borderCollapse: 'collapse', width: '100%', fontSize: 12 }}>
            <thead><tr>{[
              localized(language, '序号', 'Index'), localized(language, '名称', 'Name'), 'FlowAreaID', 'MeshHash',
              localized(language, '单元数', 'Cells'), localized(language, '面数', 'Faces'),
            ].map((heading) => <th key={heading} style={tableHeaderStyle}>{heading}</th>)}</tr></thead>
            <tbody>{inspection.flowAreas.map((area) => <tr key={area.flowAreaID}>
              <td style={tableCellStyle}>{area.flowAreaIndex}</td><td style={tableCellStyle}>{area.name || '—'}</td>
              <td style={tableCellStyle}><code>{area.flowAreaID}</code></td><td style={tableCellStyle}><code>{area.meshHash}</code></td>
              <td style={tableCellStyle}>{area.cellCount}</td><td style={tableCellStyle}>{area.faceCount}</td>
            </tr>)}</tbody>
          </table>
        </div> : <p className="map-page-notice">{localized(language, 'Result 几何中没有 Flow Area。', 'The Result geometry contains no Flow Areas.')}</p>}
      </div>

      <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'end', gap: 9, marginTop: 10 }}>
        <label className="tools-field" style={{ minWidth: 190, flex: '1 1 230px' }}>
          <span>{localized(language, 'Flow Area', 'Flow Area')}</span>
          <select data-testid="mesh-timeseries-flowarea" aria-label={localized(language, 'Flow Area', 'Flow Area')}
            value={form.flowAreaIndex} onChange={(event) => changeFlowArea(event.target.value)} disabled={executionBusy || inspection.flowAreas.length === 0}>
            <option value="">{localized(language, '选择 Flow Area', 'Choose a Flow Area')}</option>
            {inspection.flowAreas.map((area) => <option key={area.flowAreaID} value={area.flowAreaIndex} disabled={!area.hasMesh}>
              {area.name || localized(language, `Flow Area ${area.flowAreaIndex + 1}`, `Flow Area ${area.flowAreaIndex + 1}`)} · {area.flowAreaID} · {localized(language, `单元 ${area.cellCount} / 面 ${area.faceCount}`, `Cells ${area.cellCount} / Faces ${area.faceCount}`)}
            </option>)}
          </select>
        </label>
        <label className="tools-field" style={{ minWidth: 125 }}>
          <span>{localized(language, '网格要素类型', 'Mesh element type')}</span>
          <select data-testid="mesh-timeseries-element-kind" aria-label={localized(language, '网格要素类型', 'Mesh element type')}
            value={form.elementKind} onChange={(event) => changeElementKind(event.target.value as MeshElementKind)} disabled={executionBusy}>
            <option value="Cell">Cell（{localized(language, '单元', 'cell')}）</option>
            <option value="Face">Face（{localized(language, '面', 'face')}）</option>
          </select>
        </label>
        <label className="tools-field" style={{ minWidth: 150 }}>
          <span>{form.elementKind === 'Cell' ? localized(language, '单元编号（从 0 开始）', 'Cell index (zero-based)') : localized(language, '面编号（从 0 开始）', 'Face index (zero-based)')}</span>
          <input data-testid="mesh-timeseries-element-index" aria-label={form.elementKind === 'Cell'
            ? localized(language, '单元编号（从 0 开始）', 'Cell index (zero-based)')
            : localized(language, '面编号（从 0 开始）', 'Face index (zero-based)')}
            type="text" inputMode="numeric" pattern="[0-9]*" value={form.elementIndex}
            onChange={(event) => changeElementIndex(event.target.value)} disabled={executionBusy} />
          <small>{selectedFlowArea ? localized(language, `有效范围：0–${Math.max(0, elementCount - 1)}（共 ${elementCount} 个）`, `Valid range: 0–${Math.max(0, elementCount - 1)} (${elementCount} total)`)
            : localized(language, '先选择一个有网格的 Flow Area。', 'Choose a Flow Area with a mesh first.')}</small>
        </label>
        {hitIsCurrent && meshHit && <div style={{ display: 'flex', alignItems: 'center', gap: 7, minHeight: 34 }}>
          <span className="map-page-notice" role="status" style={{ margin: 0, padding: '6px 8px' }}>
            {localized(language, `当前地图命中：${meshHit.elementKind} ${meshHit.elementIndex} · Flow Area ${meshHit.flowAreaIndex}`, `Current map hit: ${meshHit.elementKind} ${meshHit.elementIndex} · Flow Area ${meshHit.flowAreaIndex}`)}
          </span>
          <button type="button" className="tools-button tools-button-secondary" onClick={applyMeshHit} disabled={executionBusy}>
            {localized(language, '使用此命中', 'Use this hit')}
          </button>
        </div>}
        <div style={{ display: 'flex', gap: 8 }}>
          <button type="button" className="tools-button tools-button-primary" data-testid="mesh-timeseries-load"
            onClick={() => void execute()} disabled={!canExecute}>
            {executionBusy ? localized(language, '正在读取…', 'Reading…') : localized(language, '读取时间序列', 'Load time series')}
          </button>
          {executionBusy && <button type="button" className="tools-button tools-button-secondary" data-testid="mesh-timeseries-cancel"
            onClick={cancel} disabled={currentExecution?.cancelRequested}>
            {currentExecution?.cancelRequested ? localized(language, '正在等待取消…', 'Waiting for cancellation…') : localized(language, '请求取消', 'Request cancellation')}
          </button>}
        </div>
      </div>

      {inspection.state === 'ready' && inspection.matchingResultNames.length === 0 && <p className="map-page-notice" role="status">
        {localized(language, '当前已选 Result 中没有与此来源网格匹配的已完成结果；仍可读取一次以确认 Host 终态。',
          'No completed selected Result currently matches this source mesh. You can still run once to confirm the Host terminal state.')}
      </p>}
      {selectedFlowArea && !selectedFlowArea.hasMesh && <p className="map-page-notice" role="status">{localized(language, '所选 Flow Area 没有可读取的网格。', 'The selected Flow Area has no readable mesh.')}</p>}
      {form.elementIndex !== '' && !elementIndexIsValid && <p className="map-page-notice map-error-notice" role="alert">
        {localized(language, '请输入当前网格范围内的非负整数编号；编辑内容按原始文本保留。', 'Enter a non-negative integer within the inspected mesh range. The input remains raw text while editing.')}
      </p>}
      {executionBusy && <div className="map-page-notice" role="status" aria-live="polite">
        <strong>{localized(language, '正在读取已选 Result 的要素时间序列。', 'Reading the selected Result element time series.')}</strong>
        {currentExecution?.progress?.taskName && <div>{currentExecution.progress.taskName}</div>}
        {currentExecution?.progress?.message && <div>{currentExecution.progress.message}</div>}
        {currentExecution?.progress?.fraction !== undefined && <progress max={1} value={currentExecution.progress.fraction} />}
        {currentExecution?.transport && <div>{transportText(currentExecution.transport, language)}</div>}
      </div>}
      {currentExecution?.phase === 'unconfirmed' && <p className="map-page-notice map-error-notice" role="alert">
        {localized(language, '操作终态尚未确认。当前不会展示此请求的结果；请先核对服务端操作状态，再重试。',
          'The operation terminal state is unconfirmed. No result from this request is displayed; check the Host operation state before retrying.')}
        {currentExecution.error !== undefined && <details><summary>{localized(language, '查看诊断', 'View diagnostic')}</summary><pre style={{ whiteSpace: 'pre-wrap' }}>{message(currentExecution.error, language)}</pre></details>}
      </p>}
      {currentExecution?.phase === 'failed' && <p className="map-page-notice map-error-notice" role="alert">
        {localized(language, '网格要素时间序列读取失败。', 'Mesh element time-series read failed.')}
        {currentExecution.error !== undefined && <details><summary>{localized(language, '查看诊断', 'View diagnostic')}</summary><pre style={{ whiteSpace: 'pre-wrap' }}>{message(currentExecution.error, language)}</pre></details>}
      </p>}
      {currentExecution?.phase === 'cancelled' && <p className="map-page-notice" role="status">{localized(language, 'Host 已确认取消本次读取。', 'The Host confirmed cancellation of this read.')}</p>}
      {currentExecution?.phase === 'noMatchingMesh' && <p className="map-page-notice" role="status">
        {localized(language, '当前没有已选 Result 使用所选 Flow Area 的完全匹配网格。', 'No selected Result uses the exact mesh in this Flow Area.')}
        {currentExecution.result?.skippedComputingResultCount ? ` ${localized(language, '仍在计算而跳过', 'Skipped while computing')}: ${currentExecution.result.skippedComputingResultCount}.` : ''}
      </p>}
      {currentExecution?.phase === 'ready' && currentExecution.result && chartData && dataset && <div className="map-property-feature" style={{ margin: '10px 0 0', padding: 9 }} data-testid="mesh-timeseries-chart">
        <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'baseline', justifyContent: 'space-between', gap: 8 }}>
          <strong>{localized(language, '实际 Result 时间序列', 'Result time series')}</strong>
          <span>{currentExecution.result.variable} · {currentExecution.result.units} · {currentExecution.result.elementKind} {currentExecution.result.elementIndex}</span>
        </div>
        {currentExecution.result.skippedComputingResultCount > 0 && <p className="map-page-notice" role="status">
          {localized(language, `另有 ${currentExecution.result.skippedComputingResultCount} 个正在计算的结果已跳过。`, `${currentExecution.result.skippedComputingResultCount} Result(s) still computing were skipped.`)}
        </p>}
        {dataset.times.length === 0 ? <p className="map-page-notice" role="status">{localized(language, 'Host 没有返回时间样本。', 'The Host returned no time samples.')}</p> : <>
          <TimeSeriesPayloadChart dataset={dataset} series={visibleSeries} selectedIndex={boundedTimeIndex}
            onSelectIndex={(index) => setSelectedTimeIndex(Math.max(0, Math.min(dataset.times.length - 1, index)))}
            range={chartRange} onRangeChange={setChartRange} settings={chartSettings} onSettingsChange={setChartSettings} language={language} />
          {dataset.times.length > 1 && <label className="tools-field" style={{ display: 'grid', gridTemplateColumns: 'auto minmax(120px, 1fr) auto', alignItems: 'center', gap: 8 }}>
            <span>{localized(language, '查看时间样本', 'Inspect time sample')}</span>
            <input data-testid="mesh-timeseries-time-sample" aria-label={localized(language, '查看时间样本', 'Inspect time sample')}
              type="range" min={0} max={dataset.times.length - 1} step={1} value={boundedTimeIndex}
              onChange={(event) => setSelectedTimeIndex(Number(event.target.value))} />
            <span>{boundedTimeIndex + 1} / {dataset.times.length}</span>
          </label>}
          <div className="map-page-notice" role="status" style={{ marginTop: 8 }}>
            <div>{localized(language, 'Host 原始时间字符串', 'Raw Host timestamp')}: <code data-testid="mesh-timeseries-raw-time">{selectedSampleTime}</code></div>
            <div style={{ display: 'grid', gridTemplateColumns: 'minmax(150px, 1fr) minmax(100px, .6fr)', gap: '3px 12px', marginTop: 5 }}>
              {selectedSampleValues.map(({ series, value }) => <div key={series.id} style={{ display: 'contents' }}>
                <span>{series.name}</span><code>{sampleText(value)}{series.units ? ` ${series.units}` : ''}</code>
              </div>)}
            </div>
          </div>
          <p className="map-page-notice" role="note" style={{ marginTop: 7 }}>
            {localized(language, '空值或非有限样本按 NoData 断开曲线；不猜测有限数值哨兵。图表刻度会本地化显示，以上原始时间字符串未作时区换算。',
              'Null or non-finite samples break the curve as NoData; finite sentinel values are not guessed. Chart ticks are localized, while the raw timestamps above are shown without timezone conversion.')}
          </p>
        </>}
        <details style={{ marginTop: 8 }}>
          <summary>{localized(language, '查看实际结果来源与修订', 'View Result sources and revisions')}</summary>
          <ul style={{ margin: '6px 0', paddingInlineStart: 22 }}>
            {currentExecution.result.entries.map((entry) => <li key={`${entry.resultPath}:${entry.resultRevision}`}>
              <strong>{entry.resultName || '—'}</strong> · <code>{entry.resultPath}</code> · <code>{entry.resultRevision}</code>
            </li>)}
          </ul>
        </details>
      </div>}
      {currentExecution?.phase === 'ready' && currentExecution.cancelRequested && <p className="map-page-notice" role="status">
        {localized(language, '已发出取消请求，但 Host 在取消生效前完成了读取；以下显示的是 Host 返回的完整结果。',
          'Cancellation was requested, but the Host completed the read before it took effect. The complete Host result is shown below.')}
      </p>}
      {currentExecution?.phase === 'ready' && chartError !== null && <p className="map-page-notice map-error-notice" role="alert">
        {localized(language, 'Host 已返回数据，但时间轴顺序无法安全对齐，未绘制该图表。',
          'The Host returned data, but the time axes could not be aligned safely, so the chart was not drawn.')}
        <details><summary>{localized(language, '查看诊断', 'View diagnostic')}</summary><pre style={{ whiteSpace: 'pre-wrap' }}>{message(chartError, language)}</pre></details>
      </p>}
      {!hasExecute && inspection.state === 'ready' && <p className="map-page-notice" role="status">{localized(language,
        '当前服务未发布网格要素时间序列读取操作。', 'The active service has not advertised mesh time-series execution.')}</p>}
    </>}
  </section>;
}

const definitionValueStyle: CSSProperties = { minWidth: 0, margin: 0, overflowWrap: 'anywhere' };
const tableHeaderStyle: CSSProperties = { padding: '5px 7px', borderBottom: '1px solid #dce4e9', textAlign: 'left', whiteSpace: 'nowrap' };
const tableCellStyle: CSSProperties = { padding: '5px 7px', borderBottom: '1px solid #e8edf0', whiteSpace: 'nowrap' };
