import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { BackendTransportState, FeaturePageProps } from '../../shared/workbench';
import { ServerBackendError } from '../../platform/ServerBackend';
import type { ModelSnapshot } from '../editors/model';
import { ComputeButton, type ComputeRunState } from './ComputeButton';
import { ComputeSummaryControl } from './ComputeSummaryControl';
import { PlanComputeControl } from './PlanComputeControl';
import { RealTimeMonitor } from './RealTimeMonitor';
import { ViewComputeResultsControl } from './ViewComputeResultsControl';
import {
  parseComputeProgress,
  parsePlanComputeExecutionResult,
  parsePlanComputeInspection,
  type ComputeProgressSample,
  type PlanComputeExecutionResult,
  type PlanComputeInspection,
} from './computeTypes';
import './compute.css';

const copy = {
  zh: {
    eyebrow: 'HEC-RAS 2025 · 计划计算', title: '计划计算',
    description: '先加载当前计划的完整模型快照并由后端检查依赖，再使用检查身份启动原 C# 计算流程。',
    runtime: '服务状态', inspect: '计划检查', compute: '计划求解',
    serverReady: '服务端能力已连接', serverWaiting: '等待 ASP.NET Core 服务连接',
    loadingModel: '正在读取计划快照…', inspecting: '正在检查计划和依赖…',
    selectPlan: '请先在工程页选择一个已保存的计算计划。', missingProject: '当前计划缺少所属工程路径或工程 XML，无法安全检查。',
    unsupported: '服务端尚未发布此操作。', retry: '重新检查计划',
    serverOperationFailed: '服务端操作失败', connectionError: '无法连接 C# 服务，请确认服务已启动且当前地址可访问。',
    localOperationFailed: '无法完成计划检查或计算。', invalidInspection: '服务端返回的计划检查数据格式无效。', staleInspection: '计划在检查期间发生变化，请重新加载并检查。',
    invalidResult: '服务端返回的计划计算结果无法识别，未标记为成功。', resultIdentityMismatch: '服务端结果与已检查的计划版本不一致。', diagnostics: '服务端诊断信息',
    dirty: '存在未保存的编辑草稿。请先保存或放弃这些草稿，再检查和运行计算。',
    notExecutable: '当前计划检查未通过，不能启动计算。',
    inspection: '后端计划检查结果', canExecute: '允许计算', yes: '是', no: '否', requiresCuda: '需要 CUDA',
    reasons: '检查原因', messages: '检查消息', sideEffects: '可能产生的副作用',
    computeDescription: '计算在 ASP.NET Core C# 服务中执行；前端只传递已检查的模型快照和检查身份。',
    cancelRequested: '已请求服务端协作取消；继续等待服务端确认的终态。',
    cancelConfirmed: '服务端已确认取消。',
    cancelMissed: '计算在取消请求生效前已完成。',
    computeFailed: '服务端计算失败。',
    copyFailed: '无法访问剪贴板。', language: 'English', failure: '服务端计算失败',
    noInspection: '加载计划后会显示服务端检查结论。', noRuntime: '服务端尚未报告计算能力。',
    waiting: '就绪', unsupportedTitle: '当前计划由服务端运行原有 C# 求解流程，进度和结果均来自该计算操作。',
  },
  en: {
    eyebrow: 'HEC-RAS 2025 · Plan compute', title: 'Plan compute',
    description: 'Load the complete current plan snapshot, inspect its dependencies on the server, then execute with that inspection identity.',
    runtime: 'Service status', inspect: 'Plan inspection', compute: 'Plan solve',
    serverReady: 'Server capabilities connected', serverWaiting: 'Waiting for the ASP.NET Core service',
    loadingModel: 'Loading the plan snapshot…', inspecting: 'Inspecting plan and dependencies…',
    selectPlan: 'Select a saved compute plan in Project first.', missingProject: 'The selected plan has no owning project path or project XML, so it cannot be safely inspected.',
    unsupported: 'The server has not advertised this operation.', retry: 'Inspect plan again',
    serverOperationFailed: 'Server operation failed', connectionError: 'Cannot connect to the C# server. Confirm that it is running and reachable.',
    localOperationFailed: 'Plan inspection or compute could not be completed.', invalidInspection: 'The server returned an invalid plan inspection.', staleInspection: 'The plan changed during inspection. Reload and inspect it again.',
    invalidResult: 'The server returned an unrecognized plan-compute result; it was not marked successful.', resultIdentityMismatch: 'The server result does not match the inspected plan revision.', diagnostics: 'Server diagnostics',
    dirty: 'There are unsaved editor drafts. Save or discard them before inspecting or starting compute.',
    notExecutable: 'The server inspection did not pass; compute cannot start.',
    inspection: 'Backend plan inspection', canExecute: 'Can execute', yes: 'Yes', no: 'No', requiresCuda: 'Requires CUDA',
    reasons: 'Reasons', messages: 'Inspection messages', sideEffects: 'Potential side effects',
    computeDescription: 'Compute runs in the ASP.NET Core C# service. The UI sends only the inspected model snapshot and inspection identity.',
    cancelRequested: 'Cancellation was requested from the server; waiting for its confirmed terminal state.',
    cancelConfirmed: 'The server confirmed cancellation.',
    cancelMissed: 'Compute completed before the cancellation request took effect.',
    computeFailed: 'The server reported that compute failed.',
    copyFailed: 'Clipboard access failed.', language: '中文', failure: 'Server compute failed',
    noInspection: 'The server inspection appears here after the plan is loaded.', noRuntime: 'The server has not advertised compute capabilities.',
    waiting: 'Ready', unsupportedTitle: 'The selected plan runs through the retained C# solver on the server; progress and results come from that operation.',
  },
} satisfies Record<FeaturePageProps['language'], Record<string, string>>;

interface PlanModelSaveRequest {
  assetPath: string;
  kind: string;
  projectPath: string;
  revision: string;
  fields: Record<string, string>;
  images: ModelSnapshot['images'];
  projectXml: string;
  sourceState: string | null;
  rows: Record<string, never[]>;
  samples: Record<string, never[]>;
  planTimeWindowEdits: never[];
  geometryTopologyState: null;
  geometryMeshState: null;
  geometryCulvertState: null;
  tableSelections: Record<string, string[]>;
  changedCells: Record<string, never[]>;
  coreLayerContext?: Record<string, unknown>;
}

type LoadState = 'idle' | 'loading' | 'inspecting' | 'ready' | 'failed' | 'unavailable';

/** Server-owned Plan.ComputeAsync workflow; this page never creates a WASM client. */
export function ComputeProofPage({ language, context, backend, backendRevision = 0, hasUnsavedDrafts = false, onContextChange, onNavigate }: FeaturePageProps) {
  const t = copy[language];
  const plan = context.selectedAsset;
  const isPlan = Boolean(plan && plan.kind.toLowerCase().includes('plan') && !plan.kind.toLowerCase().includes('result'));
  const contextIdentity = useMemo(() => JSON.stringify([
    context.projectSessionID, context.projectPath, context.projectXml, context.projectRevision, plan?.path, plan?.kind, plan?.revision, plan?.locator ?? null,
  ]), [context.projectSessionID, context.projectPath, context.projectXml, context.projectRevision, plan?.path, plan?.kind, plan?.revision, plan?.locator]);

  const [loadState, setLoadState] = useState<LoadState>('idle');
  const [loadError, setLoadError] = useState<string>();
  const [loadErrorDetail, setLoadErrorDetail] = useState<string>();
  const [inspection, setInspection] = useState<PlanComputeInspection>();
  const [modelSnapshot, setModelSnapshot] = useState<PlanModelSaveRequest>();
  const [runState, setRunState] = useState<ComputeRunState>('idle');
  const [progressSamples, setProgressSamples] = useState<ComputeProgressSample[]>([]);
  const [latestRawProgress, setLatestRawProgress] = useState<string>();
  const [rawResult, setRawResult] = useState<string>();
  const [parsedResult, setParsedResult] = useState<PlanComputeExecutionResult>();
  const [rawError, setRawError] = useState<string>();
  const [rawErrorDetail, setRawErrorDetail] = useState<string>();
  const [cancelNote, setCancelNote] = useState<string>();
  const [transportState, setTransportState] = useState<BackendTransportState>();
  const [runStartedAt, setRunStartedAt] = useState<number>();
  const [runEndedAt, setRunEndedAt] = useState<number>();
  const [copied, setCopied] = useState(false);
  const [copyError, setCopyError] = useState<string>();
  const inspectionGeneration = useRef(0);
  const runGeneration = useRef(0);
  const previousContextIdentity = useRef(contextIdentity);
  const runController = useRef<AbortController | undefined>(undefined);
  const inspectionController = useRef<AbortController | undefined>(undefined);
  const [now, setNow] = useState(() => Date.now());
  const [inspectionRefresh, setInspectionRefresh] = useState(0);
  const busy = runState === 'running' || runState === 'cancelling';
  const missingProject = !context.projectPath || !context.projectXml;
  const supportsLoad = backend.supports('model.load');
  const supportsInspect = backend.supports('plan.compute.inspect');
  const supportsExecute = backend.supports('plan.compute.execute');

  useEffect(() => {
    const contextChanged = previousContextIdentity.current !== contextIdentity;
    previousContextIdentity.current = contextIdentity;
    const generation = ++inspectionGeneration.current;
    const controller = new AbortController();
    inspectionController.current?.abort();
    inspectionController.current = controller;
    setInspection(undefined);
    setModelSnapshot(undefined);
    setLoadError(undefined);
    setLoadErrorDetail(undefined);
    if (contextChanged) {
      runGeneration.current++;
      runController.current?.abort();
      runController.current = undefined;
      setRawError(undefined);
      setRawResult(undefined);
      setParsedResult(undefined);
      setProgressSamples([]);
      setLatestRawProgress(undefined);
      setRunState('idle');
      setCancelNote(undefined);
      setTransportState(undefined);
      setRunStartedAt(undefined);
      setRunEndedAt(undefined);
      setRawErrorDetail(undefined);
    }

    if (!isPlan || !plan) {
      setLoadState('idle');
      return () => controller.abort();
    }
    if (missingProject) {
      setLoadState('failed');
      setLoadError(t.missingProject);
      return () => controller.abort();
    }
    if (hasUnsavedDrafts) {
      setLoadState('failed');
      setLoadError(t.dirty);
      return () => controller.abort();
    }
    if (!supportsLoad || !supportsInspect) {
      setLoadState('unavailable');
      setLoadError(t.unsupported);
      return () => controller.abort();
    }

    setLoadState('loading');
    void (async () => {
      try {
        const locator = asRecord(plan.locator);
        const core = coreLayerContext(locator, context.projectSessionID, context.projectPath!);
        const loadRequest = {
          assetPath: plan.path,
          kind: plan.kind,
          projectPath: context.projectPath,
          projectXml: context.projectXml,
          sourceState: sourceStateFor(locator) ?? null,
          ...(core ? { coreLayerContext: core } : {}),
        };
        const loaded = await backend.request<ModelSnapshot>('model.load', loadRequest, { signal: controller.signal });
        assertModelSnapshot(loaded);
        if (generation !== inspectionGeneration.current) return;
        const completeModel = buildModelSaveRequest(context.projectPath!, context.projectXml!, plan, loaded, locator, context.projectSessionID);
        setModelSnapshot(completeModel);
        setLoadState('inspecting');
        const inspectedValue = await backend.request<unknown>('plan.compute.inspect', { model: completeModel }, { signal: controller.signal });
        const inspected = parsePlanComputeInspection(inspectedValue);
        if (!inspected) throw new Error(t.invalidInspection);
        if (generation !== inspectionGeneration.current) return;
        if (inspected.revision !== completeModel.revision) throw new Error(t.staleInspection);
        setInspection(inspected);
        setLoadState('ready');
      } catch (error) {
        if (generation !== inspectionGeneration.current || isAbort(error)) return;
        setLoadState('failed');
        const display = errorDisplay(error, language, t);
        setLoadError(display.message);
        setLoadErrorDetail(display.detail);
      }
    })();

    return () => {
      controller.abort();
      if (inspectionController.current === controller) inspectionController.current = undefined;
    };
  }, [backend, backendRevision, contextIdentity, context.projectPath, context.projectSessionID, context.projectXml, hasUnsavedDrafts, inspectionRefresh, isPlan, language, missingProject, plan, supportsInspect, supportsLoad, t.dirty, t.invalidInspection, t.missingProject, t.staleInspection, t.unsupported]);

  useEffect(() => {
    if (!busy || !runStartedAt || runEndedAt) return;
    const timer = window.setInterval(() => setNow(Date.now()), 250);
    return () => window.clearInterval(timer);
  }, [busy, runStartedAt, runEndedAt]);

  useEffect(() => () => {
    inspectionController.current?.abort();
    runController.current?.abort();
  }, []);

  const inspectAgain = useCallback(() => {
    // Re-running the request effect also reloads the source snapshot before inspection.
    setLoadState('idle');
    setInspection(undefined);
    setInspectionRefresh((value) => value + 1);
  }, []);

  const runCompute = async (): Promise<void> => {
    if (!plan || !modelSnapshot || !inspection?.canExecute || hasUnsavedDrafts || busy || !supportsExecute) return;
    // Creating even a cancelled Result changes the next available output path,
    // which is part of the server inspection identity. Consume it once.
    setInspection(undefined);
    const generation = ++runGeneration.current;
    const controller = new AbortController();
    runController.current?.abort();
    runController.current = controller;
    setProgressSamples([]);
    setLatestRawProgress(undefined);
    setRawResult(undefined);
    setParsedResult(undefined);
    setRawError(undefined);
    setRawErrorDetail(undefined);
    setCancelNote(undefined);
    setTransportState(undefined);
    setCopyError(undefined);
    setCopied(false);
    const startedAt = Date.now();
    setRunStartedAt(startedAt);
    setRunEndedAt(undefined);
    setRunState('running');
    let sequence = 0;
    let terminalConfirmed = false;

    try {
      const value = await backend.request<unknown>('plan.compute.execute', {
        model: modelSnapshot,
        inspectionId: inspection.inspectionId,
      }, {
        signal: controller.signal,
        onTransportState: (state) => {
          if (generation === runGeneration.current) setTransportState(state);
        },
        onProgress: (progress) => {
          if (generation !== runGeneration.current) return;
          const raw = safeJson(progress);
          setLatestRawProgress(raw);
          const parsed = parseComputeProgress(raw);
          if (!parsed) return;
          const sample: ComputeProgressSample = { ...parsed, sequence: sequence++, timestamp: Date.now() };
          setProgressSamples((current) => [...current, sample].slice(-10_000));
        },
      });
      if (generation !== runGeneration.current) return;
      terminalConfirmed = true;
      const raw = safeJson(value);
      setRawResult(raw);
      const result = parsePlanComputeExecutionResult(value);
      setParsedResult(result);
      const ended = Date.now();
      setRunEndedAt(ended);
      if (!result) {
        setRunState('unverified');
        setRawError(t.invalidResult);
        return;
      }
      if (result.inspectionId !== inspection.inspectionId || result.revision !== modelSnapshot.revision) {
        setRunState('unverified');
        setRawError(t.resultIdentityMismatch);
        return;
      }
      const terminal = result.state === 'cancelled' ? 'cancelled' : result.state;
      setRunState(terminal);
      if (terminal === 'cancelled') setCancelNote(t.cancelConfirmed);
      else if (controller.signal.aborted && terminal === 'completed') setCancelNote(t.cancelMissed);
      else setCancelNote(undefined);
      if (terminal === 'failed') {
        setRawError(t.computeFailed);
        const details = [result.errorMessage, result.summary?.failureReason, result.summary?.computeLog].filter((value): value is string => typeof value === 'string' && value.length > 0);
        setRawErrorDetail(details.length > 0 ? details.join('\n\n') : undefined);
      }
    } catch (error) {
      if (generation !== runGeneration.current) return;
      const ended = Date.now();
      setRunEndedAt(ended);
      if (isAbort(error) && controller.signal.aborted) {
        terminalConfirmed = true;
        setRunState('cancelled');
        setCancelNote(t.cancelConfirmed);
      } else {
        terminalConfirmed = error instanceof ServerBackendError && error.terminalConfirmed === true;
        setRunState(terminalConfirmed ? 'failed' : 'unconfirmed');
        const display = errorDisplay(error, language, t);
        setRawError(display.message);
        setRawErrorDetail(display.detail);
      }
    } finally {
      if (generation === runGeneration.current && runController.current === controller) {
        runController.current = undefined;
        if (terminalConfirmed) inspectAgain();
      }
    }
  };

  const cancelCompute = (): void => {
    if (!runController.current || runController.current.signal.aborted) return;
    setRunState('cancelling');
    setCancelNote(t.cancelRequested);
    runController.current.abort();
  };

  const copyResult = async (): Promise<void> => {
    if (!rawResult) return;
    setCopyError(undefined);
    try {
      await navigator.clipboard.writeText(rawResult);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1400);
    } catch {
      setCopyError(t.copyFailed);
    }
  };

  const canRun = Boolean(inspection?.canExecute && modelSnapshot && !hasUnsavedDrafts && supportsExecute && !busy);
  const inspectionReasons = inspection?.reasons ?? [];
  const elapsed = runStartedAt === undefined ? undefined : (runEndedAt ?? (busy ? now : runStartedAt)) - runStartedAt;
  const currentStatus = loadState === 'loading' ? t.loadingModel
    : loadState === 'inspecting' ? t.inspecting
      : loadState === 'failed' || loadState === 'unavailable' ? loadError ?? t.unsupported
        : inspection ? t.serverReady : t.noInspection;

  return (
    <div className="compute-tool-page plan-compute-page">
      <section className="hero">
        <div className="hero-copy">
          <div className="eyebrow"><span className="eyebrow-line" />{t.eyebrow}</div>
          <h1>{t.title}</h1>
          <p>{t.description}</p>
        </div>
        <div className="hero-emblem" aria-hidden="true">
          <div className="emblem-ring"><div className="emblem-core"><span className="emblem-wave wave-a" /><span className="emblem-wave wave-b" /><span className="emblem-wave wave-c" /></div></div>
          <span className="orbit orbit-one" /><span className="orbit orbit-two" />
          <span className="emblem-label">C# / ASP.NET</span>
        </div>
      </section>

      <div className="compute-fixture-boundary" role="note">
        <span aria-hidden="true">ⓘ</span><span><strong>{t.unsupportedTitle}</strong></span>
      </div>
      {hasUnsavedDrafts && <div className="notice notice-warning" role="alert"><span>!</span>{t.dirty}</div>}

      {!isPlan ? <div className="compute-fixture-boundary" role="status">{t.selectPlan}</div>
        : missingProject ? <div className="notice notice-warning" role="alert">{t.missingProject}</div>
          : null}

      <section className="compute-grid">
        <article className="panel runtime-panel">
          <div className="panel-heading">
            <div className="panel-icon runtime-icon"><span className="chip-grid">▦</span></div>
            <div><div className="panel-kicker">01 / {t.runtime}</div><h2>{t.runtime}</h2></div>
          </div>
          <div className="runtime-state">
            <span className={`runtime-indicator ${loadState === 'loading' || loadState === 'inspecting' ? 'is-loading' : loadError ? 'is-error' : inspection ? 'is-ready' : ''}`} />
            <span>{currentStatus}</span>
          </div>
          {plan ? <pre className="code-block runtime-code">{JSON.stringify({ assetPath: plan.path, projectPath: context.projectPath, revision: modelSnapshot?.revision ?? plan.revision ?? null }, null, 2)}</pre>
            : <div className="empty-code">{t.noRuntime}</div>}
          {loadError && <pre className="code-block error-block">{loadError}</pre>}
          {loadErrorDetail && <details className="compute-error-details"><summary>{t.diagnostics}</summary><pre className="code-block error-block">{loadErrorDetail}</pre></details>}
          {isPlan && !missingProject && <button className="text-button" onClick={inspectAgain} disabled={!supportsLoad || busy || loadState === 'loading' || loadState === 'inspecting'}>{t.retry}</button>}
          <div className="panel-footnote"><span className="footnote-mark">i</span>{t.computeDescription}</div>
        </article>

        <article className="panel compute-panel">
          <div className="panel-heading">
            <div className="panel-icon compute-icon"><span className="play-triangle">▶</span></div>
            <div><div className="panel-kicker">02 / {t.compute}</div><h2>{t.compute}</h2></div>
          </div>
          <p className="compute-panel-description">{t.computeDescription}</p>
          <ComputeButton language={language} state={runState} canRun={canRun} onRun={() => void runCompute()} onCancel={cancelCompute} />
          {cancelNote && <div className={`notice ${runState === 'failed' ? 'notice-warning' : ''}`}><span>{runState === 'failed' ? '!' : '↻'}</span>{cancelNote}</div>}
          {busy && transportState && transportState.state !== 'connected' && transportState.state !== 'cancelRequested' && <div className="notice notice-warning compute-transport-status" role="status" data-transport-state={transportState.state}>
            <span>↻</span>{transportState.state === 'reconnecting'
              ? language === 'zh' ? '与服务的连接暂时中断，正在恢复原任务的状态；计算可能仍在进行。' : 'The connection was interrupted. Reconnecting to the original job; compute may still be running.'
              : transportState.state === 'cancelRetrying'
                ? language === 'zh' ? '取消请求尚未送达，正在重试；仍在等待服务端确认计算终态。' : 'Cancellation has not reached the server. Retrying while awaiting the confirmed terminal state.'
                : transportState.state === 'cancelFailed'
                  ? language === 'zh' ? '取消请求未被接受，仍在跟踪原任务；请检查服务连接和访问权限。' : 'Cancellation was not accepted. Tracking the original job; check the connection and access permissions.'
                  : language === 'zh' ? '服务端已回复取消请求，正在等待计算终态。' : 'The server replied to the cancellation request. Awaiting the compute terminal state.'}
          </div>}
          {!supportsExecute && <div className="compute-field-error" role="status">{t.unsupported}</div>}
          {inspection && <section className="plan-inspection" aria-label={t.inspection}>
            <div className="section-row"><h3>{t.inspection}</h3><span>{inspection.canExecute ? t.yes : t.no}</span></div>
            <dl className="plan-inspection-grid">
              <dt>{t.canExecute}</dt><dd>{inspection.canExecute ? t.yes : t.no}</dd>
              <dt>{t.requiresCuda}</dt><dd>{inspection.requiresCUDA ? t.yes : t.no}</dd>
              <dt>{language === 'zh' ? '检查身份' : 'Inspection ID'}</dt><dd>{inspection.inspectionId}</dd>
              <dt>{language === 'zh' ? '模型版本' : 'Model revision'}</dt><dd>{inspection.revision}</dd>
            </dl>
            {!inspection.canExecute && <p role="alert" className="compute-invalid-hint">{t.notExecutable}</p>}
            {inspectionReasons.length > 0 && <><div className="small-label">{t.reasons}</div><ul className="plan-inspection-list">{inspectionReasons.map((reason, index) => <li key={`${index}:${reason}`}>{reason}</li>)}</ul></>}
            {inspection.messages.length > 0 && <details><summary>{t.messages}</summary><pre className="code-block">{safeJson(inspection.messages)}</pre></details>}
            {inspection.sideEffects.length > 0 && <details><summary>{t.sideEffects}</summary><pre className="code-block">{safeJson(inspection.sideEffects)}</pre></details>}
          </section>}
          {!inspection && <div className="progress-empty">{t.noInspection}</div>}
          <PlanComputeControl language={language} state={runState} samples={progressSamples} latestRawProgress={latestRawProgress} startedAt={runStartedAt} endedAt={runEndedAt} />
          <ComputeSummaryControl language={language} state={runState} result={parsedResult} rawResult={rawResult} rawError={rawError} rawErrorDetail={rawErrorDetail} elapsedMilliseconds={elapsed} copied={copied} onCopy={() => void copyResult()} />
          <ViewComputeResultsControl language={language} context={context} backend={backend} result={parsedResult} hasUnsavedDrafts={hasUnsavedDrafts} onContextChange={onContextChange} onNavigate={onNavigate} />
          {copyError && <div className="compute-field-error" role="status">{copyError}</div>}
        </article>

        <RealTimeMonitor language={language} samples={progressSamples} />
      </section>
    </div>
  );
}

function buildModelSaveRequest(projectPath: string, projectXml: string, asset: NonNullable<FeaturePageProps['context']['selectedAsset']>, snapshot: ModelSnapshot, locator: Record<string, unknown>, sessionID: string): PlanModelSaveRequest {
  const model: PlanModelSaveRequest = {
    assetPath: asset.path,
    kind: asset.kind,
    projectPath,
    revision: snapshot.revision,
    // This page computes the acknowledged saved model and blocks unsaved drafts.
    // ModelSaveRequest fields/images are a change set, not a snapshot echo:
    // replaying all loaded fields would attempt writes to read-only properties
    // and bypass the ordered PlanTimeWindowEdits contract.
    fields: {},
    images: {},
    projectXml,
    sourceState: snapshot.sourceState ?? sourceStateFor(locator) ?? null,
    rows: {},
    samples: {},
    planTimeWindowEdits: [],
    geometryTopologyState: null,
    geometryMeshState: null,
    geometryCulvertState: null,
    tableSelections: {},
    changedCells: {},
  };
  const core = coreLayerContext(locator, sessionID, projectPath);
  if (core) model.coreLayerContext = core;
  return model;
}

function assertModelSnapshot(value: unknown): asserts value is ModelSnapshot {
  if (!value || typeof value !== 'object') throw new Error('The backend did not return a plan snapshot.');
  const candidate = value as Partial<ModelSnapshot>;
  if (typeof candidate.revision !== 'string' || !isStringRecord(candidate.fields) || !isImageRecord(candidate.images)) {
    throw new Error('The backend returned an incomplete plan snapshot.');
  }
}

function isImageRecord(value: unknown): value is ModelSnapshot['images'] {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  return Object.values(value).every((images) => Array.isArray(images) && images.every((image) => image && typeof image.id === 'string' && typeof image.name === 'string' && typeof image.data === 'string'));
}

function isStringRecord(value: unknown): value is Record<string, string> {
  return Boolean(value && typeof value === 'object' && !Array.isArray(value) && Object.values(value).every((item) => typeof item === 'string'));
}

function sourceStateFor(locator: Record<string, unknown>): string | undefined {
  const sourceState = locator.sourceState ?? locator.SourceState;
  return typeof sourceState === 'string' ? sourceState : undefined;
}

function coreLayerContext(locator: Record<string, unknown>, sessionID?: string, projectPath?: string): Record<string, unknown> | undefined {
  const candidate = locator.coreLayerContext ?? locator.CoreLayerContext;
  if (!candidate || typeof candidate !== 'object' || Array.isArray(candidate)) return undefined;
  const value = candidate as Record<string, unknown>;
  const session = value.projectSessionID ?? value.ProjectSessionID;
  const project = value.projectPath ?? value.ProjectPath;
  const identityToken = value.identityToken ?? value.IdentityToken;
  if ((sessionID !== undefined && session !== sessionID) || (projectPath !== undefined && project !== projectPath)
    || typeof identityToken !== 'string' || identityToken.length === 0) return undefined;
  return value;
}

function asRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : {};
}

function safeJson(value: unknown): string {
  try { return JSON.stringify(value, null, 2) ?? String(value); }
  catch { return String(value); }
}

function errorDisplay(error: unknown, language: FeaturePageProps['language'], labels: Record<string, string>): { message: string; detail?: string } {
  if (error instanceof ServerBackendError) {
    const raw = safeJson({ code: error.code, message: error.diagnosticMessage, details: error.details });
    return { message: error.localizedMessage(language), detail: raw };
  }
  const raw = error instanceof Error ? error.message : String(error);
  if (language === 'zh') {
    const connectionFailure = error instanceof TypeError || /failed to fetch|networkerror|network request failed/i.test(raw);
    return { message: connectionFailure ? labels.connectionError : labels.localOperationFailed, detail: raw };
  }
  return { message: raw, detail: error instanceof Error ? error.stack : undefined };
}

function isAbort(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError';
}
