import { useEffect, useRef, useState } from 'react';
import type { BackendGateway, BackendRequestOptions } from '../../../shared/workbench';
import { errorDiagnostics, message } from '../../../shared/workbench';
import { assertDiscard, captureOwner, featureQueryMethods, featureQueryOwnerKey, isAbsoluteServerPath, ownerCurrent,
  parsePreview, parseQuery, parseSaveReceipt, parseSnapshot, queryInputError } from './featureQueryProtocol';
import type { FeatureQueryFormat, FeatureQueryOwner, FeatureQueryPreview, FeatureQueryResult,
  FeatureQuerySaveReceipt, FeatureQuerySnapshot } from './featureQueryProtocol';
import type { FeatureQueryDialogProps, FeatureQueryFormValue, FeatureQueryMapImportReport } from './featureQueryTypes';

interface Stage { owner: FeatureQueryOwner; result: FeatureQueryResult; queryKey: string;
  preview?: FeatureQueryPreview | null; mergedAlignments?: boolean }
interface SaveStatus { phase: 'saving' | 'saved' | 'failed' | 'unknown' | 'unverified'; operationID: string | null;
  message: string; receipt?: FeatureQuerySaveReceipt; rawReceipt?: unknown; diagnostic?: unknown; cancelRequested?: boolean }
interface SaveJob {
  owner: FeatureQueryOwner; stage: Stage; directory: string; format: FeatureQueryFormat; operationID: string | null;
  busy: boolean; status: SaveStatus; controller: AbortController | null; register: (owner: string, dirty: boolean) => void;
  dirtyOwner: string; notify: (() => void) | null;
}
interface ImportState { report: FeatureQueryMapImportReport | null; busy: boolean; owner: FeatureQueryOwner; notice: string }
interface Retained { job: SaveJob | null; saved: { owner: FeatureQueryOwner; receipt: FeatureQuerySaveReceipt; queryKey: string } | null;
  imported: ImportState | null; notify: (() => void) | null }
// Keep accepted writes and recovery closures if the page is temporarily unmounted.
const retainedByOwner = new Map<string, Retained>();
function retention(owner: string): Retained {
  let value = retainedByOwner.get(owner);
  if (!value) { value = { job: null, saved: null, imported: null, notify: null }; retainedByOwner.set(owner, value); }
  return value;
}
function retainedLocked(value: Retained): boolean {
  const job = value.job, imported = value.imported;
  return !!job && (job.busy || job.status.phase === 'unknown' || job.status.phase === 'unverified') ||
    !!imported && (imported.busy || imported.report?.status === 'unknown');
}
const initialForm = (): FeatureQueryFormValue => ({ nldLayerId: 'AlignmentLines', useBoundaryPolygon: false,
  polygonSourceId: '', rawQl: '', queryLabel: '' });
const queryFingerprint = (p: FeatureQueryDialogProps, form: FeatureQueryFormValue) =>
  JSON.stringify([p.kind, form, p.extent, p.coordinatesAreProject, p.projectionIssue ?? null]);
const isRecord = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);
const terminal = (value: unknown) => isRecord(value) && value.terminalConfirmed === true;
const cancelledResult = (value: unknown) => isRecord(value) && value.state === 'canceled' && value.cleanupComplete === true;
const operationId = (value: unknown): string | null => {
  if (!isRecord(value)) return null;
  if (typeof value.operationId === 'string' && /^[a-f\d]{32}$/i.test(value.operationId)) return value.operationId;
  const diagnostic = errorDiagnostics(value);
  return isRecord(diagnostic) && typeof diagnostic.operationId === 'string' && /^[a-f\d]{32}$/i.test(diagnostic.operationId)
    ? diagnostic.operationId : null;
};

export function useFeatureQuery(props: FeatureQueryDialogProps) {
  const propsRef = useRef(props); propsRef.current = props;
  const mounted = useRef(false);
  const [form, setForm] = useState(initialForm);
  const formRef = useRef(form); formRef.current = form;
  const [snapshot, setSnapshot] = useState<FeatureQuerySnapshot | null>(null);
  const snapshotRef = useRef<FeatureQuerySnapshot | null>(null); snapshotRef.current = snapshot;
  const ownerRef = useRef<FeatureQueryOwner | null>(null);
  const [result, setResult] = useState<FeatureQueryResult | null>(null);
  const [preview, setPreview] = useState<FeatureQueryPreview | null>(null);
  const stageRef = useRef<Stage | null>(null);
  const resultKeyRef = useRef<string | null>(null);
  const [reading, setReading] = useState<'snapshot' | 'query' | 'preview' | null>(null);
  const [notice, setNotice] = useState('');
  const [problemState, setProblemState] = useState<{ message: string; diagnostic?: unknown }>({ message: '' });
  const setProblem = (message: string, diagnostic?: unknown) => setProblemState({ message, diagnostic });
  const [cleanupErrors, setCleanupErrors] = useState<Stage[]>([]);
  const cleanupRef = useRef<Stage[]>([]);
  const [format, setFormat] = useState<FeatureQueryFormat>('GeoJson');
  const [directory, setDirectory] = useState('');
  const [, rerender] = useState(0);
  const currentRetention = retention(props.dirtyOwner);
  const retentionRef = useRef(currentRetention); retentionRef.current = currentRetention;
  const readRef = useRef<{ generation: number; controller: AbortController | null; operationID: string | null;
    phase: 'snapshot' | 'query' | 'preview' | null }>(
    { generation: 0, controller: null, operationID: null, phase: null });
  const job = currentRetention.job;
  const saveStatus = job?.status ?? null;
  const importState = currentRetention.imported;
  const locked = retainedLocked(currentRetention);
  const ownerKey = featureQueryOwnerKey(props.context, props.document);
  const extentKey = JSON.stringify([props.extent, props.coordinatesAreProject, props.projectionIssue ?? null]);
  const hasDraft = JSON.stringify(form) !== JSON.stringify(initialForm()) || !!directory || format !== 'GeoJson' ||
    !!result || !!currentRetention.saved || !!currentRetention.imported;
  const draftRegistration = useRef<{ callback: FeatureQueryDialogProps['onDirtyStateChange']; owner: string; dirty: boolean }>(
    { callback: props.onDirtyStateChange, owner: props.dirtyOwner, dirty: false });
  const force = () => { if (mounted.current) rerender(v => v + 1); };
  const validOwner = (owner: FeatureQueryOwner, includeRevision = true) => ownerCurrent(owner, propsRef.current.context,
    propsRef.current.document, propsRef.current.backend, propsRef.current.backendRevision ?? 0, includeRevision);
  const currentRead = (generation: number, owner: FeatureQueryOwner) => mounted.current && readRef.current.generation === generation &&
    !readRef.current.controller?.signal.aborted && validOwner(owner);
  const publish = (retained: Retained) => retained.notify?.();
  function release(jobToRelease: SaveJob) { jobToRelease.register(jobToRelease.dirtyOwner, false); }
  function setJobStatus(retained: Retained, active: SaveJob, status: SaveStatus) { active.status = status; publish(retained); }

  async function discard(stage: Stage) {
    try {
      if (!stage.owner.backend.supports(featureQueryMethods.discard)) throw new Error('原服务未提供查询暂存清理。');
      const raw = await stage.owner.backend.request(featureQueryMethods.discard,
        { resultId: stage.result.resultId, expectedMap: stage.result.map });
      assertDiscard(raw, stage.result.resultId);
      cleanupRef.current = cleanupRef.current.filter(item => item.result.resultId !== stage.result.resultId || item.owner.backend !== stage.owner.backend);
    } catch {
      if (!cleanupRef.current.some(item => item.result.resultId === stage.result.resultId && item.owner.backend === stage.owner.backend))
        cleanupRef.current = [...cleanupRef.current, stage];
    }
    if (mounted.current) setCleanupErrors([...cleanupRef.current]);
  }
  function clearStage() {
    const previous = stageRef.current;
    stageRef.current = null; resultKeyRef.current = null; setResult(null); setPreview(null);
    const active = currentRetention.job;
    if (previous && !(active?.stage === previous && (active.busy || ['unknown', 'unverified'].includes(active.status.phase)))) void discard(previous);
  }
  function invalidateRead() {
    readRef.current.generation++; readRef.current.controller?.abort(); readRef.current.controller = null;
    readRef.current.operationID = null; readRef.current.phase = null; if (mounted.current) setReading(null);
  }
  function change(patch: Partial<FeatureQueryFormValue>) {
    if (retainedLocked(retentionRef.current)) return;
    const next = { ...formRef.current, ...patch };
    if (JSON.stringify(next) === JSON.stringify(formRef.current)) return;
    if (readRef.current.phase === 'query' || readRef.current.phase === 'preview') invalidateRead();
    clearStage(); setProblem(''); setNotice(''); formRef.current = next; setForm(next);
  }

  useEffect(() => {
    const registered = draftRegistration.current;
    if (registered.callback !== props.onDirtyStateChange || registered.owner !== props.dirtyOwner) {
      if (registered.dirty) registered.callback?.(registered.owner, false);
      registered.callback = props.onDirtyStateChange; registered.owner = props.dirtyOwner; registered.dirty = false;
    }
    if (registered.dirty !== hasDraft) { registered.dirty = hasDraft; registered.callback?.(registered.owner, hasDraft); }
  }, [props.onDirtyStateChange, props.dirtyOwner, hasDraft]);

  useEffect(() => {
    mounted.current = true;
    return () => { mounted.current = false; invalidateRead();
      const registered = draftRegistration.current;
      if (registered.dirty) { registered.callback?.(registered.owner, false); registered.dirty = false; }
      const old = stageRef.current; stageRef.current = null;
      const active = currentRetention.job;
      if (old && !(active?.stage === old && (active.busy || ['unknown', 'unverified'].includes(active.status.phase)))) void discard(old);
    };
  }, []);
  useEffect(() => {
    currentRetention.notify = force;
    if (currentRetention.job) currentRetention.job.notify = () => publish(currentRetention);
    force();
    return () => { if (currentRetention.notify === force) currentRetention.notify = null; };
  }, [currentRetention]);
  useEffect(() => {
    invalidateRead(); clearStage(); ownerRef.current = null; snapshotRef.current = null; setSnapshot(null); setProblem('');
    const generation = ++readRef.current.generation, controller = new AbortController();
    readRef.current.controller = controller; readRef.current.phase = 'snapshot'; setReading('snapshot');
    const p = propsRef.current;
    const stillSnapshot = () => mounted.current && generation === readRef.current.generation && !controller.signal.aborted &&
      propsRef.current.backend === p.backend && (propsRef.current.backendRevision ?? 0) === (p.backendRevision ?? 0) &&
      featureQueryOwnerKey(propsRef.current.context, propsRef.current.document) === ownerKey;
    void (async () => {
      try {
        const owner = await captureOwner(p.context, p.document, p.backend, p.backendRevision ?? 0);
        if (!currentRead(generation, owner)) return;
        if (!owner.backend.supports(featureQueryMethods.snapshot)) throw new Error('当前服务未提供在线数据查询。');
        const raw = await owner.backend.request(featureQueryMethods.snapshot, { map: owner.map }, { signal: controller.signal });
        const data = parseSnapshot(raw, owner);
        if (!currentRead(generation, owner)) return;
        ownerRef.current = owner; snapshotRef.current = data; setSnapshot(data);
        if (data.selectedPolygonId && !formRef.current.polygonSourceId) {
          const next = { ...formRef.current, polygonSourceId: data.selectedPolygonId }; formRef.current = next; setForm(next);
        }
      } catch (caught) { if (stillSnapshot()) setProblem(message(caught), errorDiagnostics(caught)); }
      finally { if (mounted.current && generation === readRef.current.generation) { readRef.current.controller = null; readRef.current.phase = null; setReading(null); } }
    })();
    return () => { if (readRef.current.generation === generation) invalidateRead(); };
  }, [ownerKey, props.backend, props.backendRevision]);
  // Moving the map invalidates a query result, but leaves any saved receipt independent.
  const previousExtent = useRef(extentKey);
  useEffect(() => {
    if (previousExtent.current === extentKey) return;
    previousExtent.current = extentKey;
    if (readRef.current.phase === 'query' || readRef.current.phase === 'preview') invalidateRead();
    clearStage(); setProblem(''); setNotice('地图范围已变化，请重新查询。');
  }, [extentKey]);

  // Recover a query result hidden by a cancellation race solely to clean its owned stage.
  async function recoverCancelledQuery(backend: BackendGateway, id: string | null, owner: FeatureQueryOwner, source: FeatureQuerySnapshot, queryKey: string) {
    if (!id || !backend.resumeOperation) return;
    try {
      const raw = await backend.resumeOperation(id);
      if (cancelledResult(raw)) return;
      const found = parseQuery(raw, owner, source, props.kind);
      if (found.hasFeatures) await discard({ owner, result: found, queryKey });
    } catch { /* Host also expires abandoned stages; retry is possible only for an identified stage. */ }
  }
  async function query() {
    const owner = ownerRef.current, source = snapshotRef.current, p = propsRef.current;
    if (retainedLocked(retentionRef.current) || readRef.current.phase || !owner || !source || !validOwner(owner)) return;
    const inputError = queryInputError(p.kind, formRef.current, source.polygonSources, p.extent, p.coordinatesAreProject, p.projectionIssue);
    if (inputError) { setProblem(inputError); return; }
    if (![featureQueryMethods.query, featureQueryMethods.preview, featureQueryMethods.discard].every(method => owner.backend.supports(method))) {
      setProblem('当前 C# 服务没有提供完整的查询、预览和清理操作。'); return;
    }
    invalidateRead(); clearStage(); setProblem(''); setNotice('正在查询在线数据…');
    const generation = ++readRef.current.generation, controller = new AbortController();
    readRef.current.controller = controller; readRef.current.phase = 'query'; setReading('query');
    const capturedForm = structuredClone(formRef.current), capturedExtent = p.coordinatesAreProject ? structuredClone(p.extent) : null;
    const capturedQueryKey = queryFingerprint(p, capturedForm);
    const stillQuery = () => currentRead(generation, owner) && queryFingerprint(propsRef.current, formRef.current) === capturedQueryKey;
    let queryID: string | null = null;
    try {
      const raw = await owner.backend.request(featureQueryMethods.query, { map: owner.map, kind: p.kind, extent: capturedExtent,
        nldLayerId: capturedForm.nldLayerId || null, useBoundaryPolygon: capturedForm.useBoundaryPolygon,
        polygonSourceId: capturedForm.polygonSourceId || null, rawQl: capturedForm.rawQl || null,
        queryLabel: capturedForm.queryLabel || null }, { signal: controller.signal, onTransportState: state => {
          if (!queryID) queryID = state.operationId;
          if (generation === readRef.current.generation) readRef.current.operationID = queryID;
        } });
      if (cancelledResult(raw)) { if (stillQuery()) setNotice('查询已取消。'); return; }
      const found = parseQuery(raw, owner, source, p.kind);
      const stage: Stage = { owner, result: found, queryKey: capturedQueryKey,
        mergedAlignments: p.kind === 'NLD' && capturedForm.nldLayerId === 'SystemRoutes' };
      if (!stillQuery()) { if (found.hasFeatures) void discard(stage); return; }
      resultKeyRef.current = capturedQueryKey;
      setResult(found);
      if (!found.hasFeatures) { setNotice('查询完成，当前范围没有返回要素。'); return; }
      stageRef.current = stage; readRef.current.phase = 'preview'; setReading('preview'); setNotice('查询完成，正在读取真实要素预览…');
      const data = await owner.backend.request(featureQueryMethods.preview,
        { map: owner.map, resultId: found.resultId, expectedMap: found.map }, { signal: controller.signal });
      const parsed = parsePreview(data, found);
      if (!stillQuery()) { if (stageRef.current === stage) stageRef.current = null; void discard(stage); return; }
      stage.preview = parsed; setPreview(parsed); setNotice('查询完成，预览为工程坐标。');
    } catch (caught) {
      if (controller.signal.aborted) void recoverCancelledQuery(owner.backend, queryID ?? operationId(caught), owner, source, capturedQueryKey);
      if (stillQuery()) { setProblem(message(caught), errorDiagnostics(caught)); setNotice('查询或预览未完成。'); }
    } finally { if (mounted.current && generation === readRef.current.generation) { readRef.current.controller = null; readRef.current.phase = null; setReading(null); } }
  }
  function cancelRead() { invalidateRead(); clearStage(); setNotice('已请求取消查询；若服务已完成，将清理原查询暂存。'); }

  async function trackSave(retained: Retained, active: SaveJob, resume: boolean) {
    if (active.busy) return;
    active.busy = true; active.controller = new AbortController();
    setJobStatus(retained, active, { phase: 'saving', operationID: active.operationID,
      message: resume ? '正在核对原导出任务…' : '正在导出文件…' });
    let returned = false, rawReceipt: unknown;
    const options: BackendRequestOptions = { signal: active.controller.signal, onTransportState: state => {
      if (active.operationID && active.operationID !== state.operationId) return;
      active.operationID = state.operationId;
      active.status = { ...active.status, operationID: active.operationID }; publish(retained);
    } };
    try {
      const raw = resume ? await active.owner.backend.resumeOperation!(active.operationID!, options)
        : await active.owner.backend.request(featureQueryMethods.save, { map: active.owner.map, resultId: active.stage.result.resultId,
          formatId: active.format, outputDirectory: active.directory }, options);
      if (cancelledResult(raw)) {
        active.status = { phase: 'failed', operationID: active.operationID, message: '服务器已确认取消导出，查询暂存仍可使用。' };
        release(active); publish(retained); return;
      }
      returned = true; rawReceipt = raw;
      const receipt = parseSaveReceipt(raw, active.stage.result, active.format, active.directory, active.stage.preview, active.stage.mergedAlignments);
      retained.saved = { owner: active.owner, receipt, queryKey: active.stage.queryKey }; retained.imported = null;
      if (stageRef.current === active.stage) stageRef.current = null;
      active.status = { phase: 'saved', operationID: active.operationID, receipt,
        message: '文件已导出并核对回执。请检查文件列表，再选择是否加入地图。' };
      release(active); publish(retained);
    } catch (caught) {
      const id = operationId(caught); if (!active.operationID && id) active.operationID = id;
      const confirmed = terminal(caught);
      active.status = { phase: returned ? 'unverified' : confirmed ? 'failed' : 'unknown', operationID: active.operationID,
        diagnostic: errorDiagnostics(caught), rawReceipt, message: returned ? '服务已返回结果，但导出回执未通过身份核对；已保留写保护和原任务。'
          : confirmed ? '服务已确认导出终止，未收到成功回执；请核对服务端文件。'
          : '导出结果尚未确认，已保留写保护和原任务，不会重复提交。' };
      if (confirmed && !returned) { release(active); if (!mounted.current || !validOwner(active.owner)) void discard(active.stage); }
      publish(retained);
    } finally { active.busy = false; active.controller = null; publish(retained); }
  }
  async function save() {
    const activeStage = stageRef.current, p = propsRef.current;
    if (retainedLocked(retentionRef.current) || readRef.current.phase || !activeStage || !validOwner(activeStage.owner) ||
        queryFingerprint(p, formRef.current) !== activeStage.queryKey) return;
    if (!p.onDirtyStateChange) { setProblem('当前工作区缺少写入保护，无法导出。'); return; }
    if (!isAbsoluteServerPath(directory)) { setProblem('请输入服务端上的绝对目录路径。'); return; }
    if (!activeStage.owner.backend.supports(featureQueryMethods.save)) { setProblem('当前服务未提供在线数据导出。'); return; }
    const retained = retentionRef.current;
    const active: SaveJob = { owner: activeStage.owner, stage: activeStage, directory, format, operationID: null, busy: false,
      status: { phase: 'saving', operationID: null, message: '正在准备导出…' }, controller: null,
      register: p.onDirtyStateChange, dirtyOwner: `map.write:feature-query-export:${p.dirtyOwner}`, notify: null };
    retained.job = active; active.register(active.dirtyOwner, true); setProblem(''); publish(retained);
    await trackSave(retained, active, false);
  }
  function cancelSave() {
    const active = retentionRef.current.job;
    if (!active?.busy || !active.controller) return;
    active.status = { ...active.status, cancelRequested: true, message: '已请求取消，正在等待服务确认导出终态。' };
    publish(retentionRef.current); active.controller.abort('用户请求取消导出');
  }
  async function resumeSave() {
    const retained = retentionRef.current, active = retained.job;
    if (!active || active.busy || !active.operationID || !active.owner.backend.resumeOperation ||
        !['unknown', 'unverified'].includes(active.status.phase)) return;
    await trackSave(retained, active, true);
  }
  async function importSources(resume = false) {
    const retained = retentionRef.current, saved = retained.saved, existing = retained.imported;
    if (!saved || !saved.receipt.sources.length || existing?.busy || currentRetention.job?.busy ||
        (!resume && (!validOwner(saved.owner, false) || queryFingerprint(propsRef.current, formRef.current) !== saved.queryKey))) return;
    if (!resume && existing && !(existing.report?.status === 'failed' && existing.report.imported.length === 0)) return;
    if (resume && (existing?.report?.status !== 'unknown' || !existing.report.resume)) return;
    const state: ImportState = resume && existing ? existing : { report: null, busy: false, owner: saved.owner, notice: '' };
    state.busy = true; state.notice = resume ? '正在核对原地图加入任务…' : '正在将已导出文件加入原地图…';
    retained.imported = state; publish(retained);
    try {
      state.report = resume ? await state.report!.resume!() : await propsRef.current.onImportSources(
        structuredClone(saved.owner.context), structuredClone(saved.owner.document), structuredClone(saved.receipt.sources),
        () => validOwner(saved.owner, false) && queryFingerprint(propsRef.current, formRef.current) === saved.queryKey);
      state.notice = state.report.status === 'complete' ? '已确认导出文件加入地图。'
        : state.report.status === 'unknown' ? '地图加入结果尚未确认，请继续核对原任务。'
        : state.report.status === 'partial' ? '部分文件已加入，后续文件未重复提交。' : '未能确认文件加入地图，请查看详细信息。';
    } catch (caught) {
      // The callback may have dispatched a mutation before throwing. Never offer a second POST.
      propsRef.current.onDirtyStateChange?.(`map.write:feature-query-import:${propsRef.current.dirtyOwner}`, true);
      state.report = { status: 'unknown', imported: [], failures: [], message: message(caught) };
      state.notice = '地图加入结果尚未确认，已保留原回执；请核对原地图。';
    } finally { state.busy = false; publish(retained); }
  }
  const queryOwnerCurrent = !!ownerRef.current && validOwner(ownerRef.current);
  const queryResultCurrent = queryOwnerCurrent && resultKeyRef.current === queryFingerprint(props, form);
  const currentSnapshot = queryOwnerCurrent ? snapshot : null;
  const inputError = currentSnapshot ? queryInputError(props.kind, form, currentSnapshot.polygonSources, props.extent, props.coordinatesAreProject, props.projectionIssue) : null;
  const canQuery = !locked && !reading && !!currentSnapshot && !inputError;
  const canSave = !locked && !reading && !!stageRef.current && validOwner(stageRef.current.owner) &&
    stageRef.current.queryKey === queryFingerprint(props, form) && isAbsoluteServerPath(directory);
  const canClose = !locked && !reading;
  return { form, change, snapshot: currentSnapshot, result: queryResultCurrent ? result : null, preview: queryResultCurrent ? preview : null,
    reading, notice, problem: problemState.message, problemDiagnostic: problemState.diagnostic, inputError, format, setFormat, directory, setDirectory,
    saveStatus, saved: currentRetention.saved, importState, locked, canClose, canQuery, canSave, query, cancelRead, save, cancelSave,
    canResumeSave: !!job && !job.busy && !!job.operationID && !!job.owner.backend.resumeOperation && ['unknown', 'unverified'].includes(job.status.phase),
    resumeSave, importSources: () => importSources(false), resumeImport: () => importSources(true),
    retryCleanup: () => { for (const stage of [...cleanupRef.current]) void discard(stage); }, cleanupErrors,
    savedCurrent: !!currentRetention.saved && validOwner(currentRetention.saved.owner, false) &&
      queryFingerprint(props, form) === currentRetention.saved.queryKey,
    canImport: !!currentRetention.saved && currentRetention.saved.receipt.sources.length > 0 && !locked &&
      (!currentRetention.imported || currentRetention.imported.report?.status === 'failed' && currentRetention.imported.report.imported.length === 0) &&
      validOwner(currentRetention.saved.owner, false) && queryFingerprint(props, form) === currentRetention.saved.queryKey,
  };
}
