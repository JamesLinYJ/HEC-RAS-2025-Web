import { useEffect, useRef, useState } from 'react';
import type { FeaturePageProps } from '../../../shared/workbench';
import { errorDiagnostics } from '../../../shared/workbench';
import { assertChannelProjectReadback, assertChannelSourceID, captureChannelOperation, channelCaptureCurrent,
  channelComputeError, channelComputeInputError, channelContextKey, channelDraftFromState, channelDraftKey,
  channelStateFromDraft, channelTerminalConfirmed, mergeChannelComputed, parseChannelCompute, parseChannelOpen,
  parseChannelSave, resumeChannelSave, stableChannelMethods, StableChannelError } from './stableChannelWorkflow';
import type { ChannelCapture, ChannelDraft, ChannelPendingSave, ChannelSave } from './stableChannelWorkflow';

interface SaveJob extends ChannelPendingSave { owner: string; busy: boolean; register: NonNullable<FeaturePageProps['onDirtyStateChange']> }
interface SaveStatus { phase: 'saving' | 'unknown' | 'unverified' | 'saved' | 'failed'; operationID: string | null; designPath: string | null; message: string; receipt?: ChannelSave; diagnostics?: unknown }

/** React glue: source C# form roles remain in their own same-name components. */
export function useStableChannel(props: FeaturePageProps) {
  const propsRef = useRef(props); propsRef.current = props;
  const [capture, setCapture] = useState<ChannelCapture | null>(null);
  const [draft, setDraft] = useState<ChannelDraft | null>(null);
  const [baseline, setBaseline] = useState<ChannelDraft | null>(null);
  const [reading, setReading] = useState<'open' | 'compute' | null>(null);
  const [error, setError] = useState<unknown>();
  const [notice, setNotice] = useState('');
  const [saveStatus, setSaveStatus] = useState<SaveStatus | null>(null);
  const [editRevision, setEditRevision] = useState(0);
  const owner = useRef(`tools.stable-channel-draft:${crypto.randomUUID()}`);
  const registered = useRef<{ callback: FeaturePageProps['onDirtyStateChange']; dirty: boolean }>({ callback: props.onDirtyStateChange, dirty: false });
  const read = useRef<{ generation: number; controller: AbortController | null }>({ generation: 0, controller: null });
  const pending = useRef<SaveJob | null>(null);
  const live = useRef({ capture, draft, baseline }); live.current = { capture, draft, baseline };
  const mounted = useRef(true);
  const previousDiscard = useRef(props.planAnalysisDrafts?.discardRevision ?? 0);
  const currentKey = channelContextKey(props.context);
  const current = channelCaptureCurrent(capture, props.context, props.backend, props.backendRevision);
  const dirty = !!draft && (!baseline || channelDraftKey(draft) !== channelDraftKey(baseline));

  function markDirty(value: boolean) {
    registered.current.dirty = value;
    registered.current.callback?.(owner.current, value);
  }
  function invalidateRead() {
    read.current.generation++; read.current.controller?.abort(); read.current.controller = null;
    setReading(null);
  }
  function setRaw(next: ChannelDraft, base = live.current.baseline) {
    live.current.draft = next; setDraft(next);
    markDirty(!base || channelDraftKey(next) !== channelDraftKey(base));
  }
  function reset() {
    invalidateRead(); markDirty(false); live.current = { capture: null, draft: null, baseline: null };
    setCapture(null); setDraft(null); setBaseline(null); setNotice(''); setError(undefined); setEditRevision(0);
  }
  useEffect(() => {
    const token = props.planAnalysisDrafts?.discardRevision ?? 0;
    if (token !== previousDiscard.current) {
      previousDiscard.current = token;
      if (!pending.current) reset();
    } else if (live.current.capture && !channelCaptureCurrent(live.current.capture, propsRef.current.context,
      propsRef.current.backend, propsRef.current.backendRevision)) {
      invalidateRead();
      // A context replacement without an explicit discard never destroys raw input.
      if (!registered.current.dirty && !pending.current) reset();
    }
  }, [currentKey, props.backend, props.backendRevision, props.planAnalysisDrafts?.discardRevision]);
  useEffect(() => {
    if (registered.current.callback === props.onDirtyStateChange) return;
    registered.current.callback?.(owner.current, false);
    registered.current.callback = props.onDirtyStateChange;
    registered.current.callback?.(owner.current, registered.current.dirty);
  }, [props.onDirtyStateChange]);
  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false; read.current.generation++; read.current.controller?.abort();
      // The root protects navigation and keeps this page mounted. A write's owner lives until its receipt/terminal state.
      registered.current.callback?.(owner.current, false);
    };
  }, []);

  function otherDrafts(): boolean {
    const p = propsRef.current;
    return p.dirtyOwners ? [...p.dirtyOwners].some(key => key !== owner.current && key !== pending.current?.owner)
      : !!p.hasUnsavedDrafts;
  }
  function validSource(source: ChannelCapture): boolean {
    const p = propsRef.current;
    return channelCaptureCurrent(source, p.context, p.backend, p.backendRevision);
  }
  async function projectReadback(source: Pick<ChannelCapture, 'context' | 'backend'>, signal?: AbortSignal) {
    if (!source.context.projectPath) {
      if (source.context.projectXml) throw new StableChannelError('当前工程尚未保存，稳定河道无法读取此工程。请先保存工程。');
      return;
    }
    const raw = await source.backend.request('project.open', { projectPath: source.context.projectPath }, { signal });
    assertChannelProjectReadback(raw, source.context);
  }
  async function open() {
    if (pending.current || registered.current.dirty) return;
    invalidateRead(); setError(undefined); setNotice('');
    const generation = ++read.current.generation, controller = new AbortController();
    read.current.controller = controller; setReading('open');
    const p = propsRef.current, context = structuredClone(p.context), backend = p.backend, backendRevision = p.backendRevision ?? 0;
    const key = channelContextKey(context), requestID = crypto.randomUUID(), memoryID = crypto.randomUUID();
    const stillCurrent = () => generation === read.current.generation && !controller.signal.aborted &&
      key === channelContextKey(propsRef.current.context) && backend === propsRef.current.backend && backendRevision === (propsRef.current.backendRevision ?? 0);
    try {
      await projectReadback({ context, backend }, controller.signal);
      if (!stillCurrent()) return;
      const raw = await backend.request(stableChannelMethods.open, { requestID, projectPath: context.projectPath,
        inMemorySourceID: memoryID, coreLayerContext: context.coreLayerContext }, { signal: controller.signal });
      const opened = parseChannelOpen(raw, requestID, context, memoryID);
      await assertChannelSourceID(opened.source);
      if (!stillCurrent()) return;
      const source = { context, contextKey: key, backend, backendRevision, source: opened.source };
      const initial = channelDraftFromState(opened.state);
      live.current = { capture: source, draft: initial, baseline: initial };
      setCapture(source); setBaseline(initial); setDraft(initial); markDirty(false); setSaveStatus(null);
      setNotice(opened.source.inMemory ? '已读取临时设计，请补全参数后计算。临时设计不能保存到文件。' : '已读取工程中的首个稳定河道设计。');
    } catch (caught) { if (stillCurrent()) setError(caught); }
    finally { if (generation === read.current.generation) { setReading(null); read.current.controller = null; } }
  }
  function change(update: (current: ChannelDraft) => ChannelDraft) {
    if (pending.current || !live.current.draft || !live.current.capture || !validSource(live.current.capture)) return;
    invalidateRead(); setError(undefined); setNotice('');
    setRaw(update(live.current.draft)); setEditRevision(value => value + 1);
  }
  async function compute(automatic = false) {
    const source = live.current.capture, currentDraft = live.current.draft;
    if (pending.current || !source || !currentDraft || !validSource(source)) return;
    const inputError = channelComputeInputError(currentDraft);
    if (inputError) { if (!automatic) setError(new StableChannelError(inputError)); return; }
    invalidateRead(); setError(undefined); setNotice('');
    const generation = ++read.current.generation, controller = new AbortController(), requestID = crypto.randomUUID();
    read.current.controller = controller; setReading('compute');
    const submitted = structuredClone(currentDraft), key = channelDraftKey(submitted);
    const stillCurrent = () => generation === read.current.generation && !controller.signal.aborted && validSource(source) &&
      !!live.current.draft && channelDraftKey(live.current.draft) === key;
    try {
      // The capture binds saved XML, Core access, session and both file revisions; Host rechecks files before and after solving.
      const raw = await source.backend.request(stableChannelMethods.compute, { requestID, source: source.source,
        state: channelStateFromDraft(submitted), solveFor: submitted.solveFor, coreLayerContext: source.context.coreLayerContext }, { signal: controller.signal });
      const result = parseChannelCompute(raw, requestID, source.source, submitted.solveFor);
      if (!stillCurrent()) return;
      if (!result.computed) throw new StableChannelError(channelComputeError(result.errorCode), result);
      setRaw(mergeChannelComputed(submitted, result.state));
      setNotice('计算完成，两个求解目标已更新。');
    } catch (caught) { if (stillCurrent()) setError(caught); }
    finally { if (generation === read.current.generation) { setReading(null); read.current.controller = null; } }
  }
  // Original TractiveForceVM recomputes when an input or the exact-two selection changes.
  useEffect(() => {
    if (!editRevision || !current || pending.current || !draft || channelComputeInputError(draft)) return;
    const timer = setTimeout(() => void compute(true), 450);
    return () => clearTimeout(timer);
    // A compute updates solved values without scheduling itself again.
  }, [editRevision, current]);

  function release(job: SaveJob) { job.register(job.owner, false); if (pending.current === job) pending.current = null; }
  async function trackSave(job: SaveJob, resume: boolean) {
    if (job.busy) return;
    job.busy = true;
    setSaveStatus({ phase: 'saving', operationID: job.operationID, designPath: job.request.source.designPath, message: resume ? '正在核对原保存任务…' : '正在保存设计…' });
    const options = { onTransportState: (state: { operationId: string }) => {
      captureChannelOperation(job, state.operationId);
      if (mounted.current) setSaveStatus(value => value ? { ...value, operationID: job.operationID } : value);
    } };
    let rawReceipt: unknown;
    let returnedReceipt = false;
    try {
      const raw = resume ? await resumeChannelSave(job, options) : await job.capture.backend.request(stableChannelMethods.save, job.request, options);
      rawReceipt = raw; returnedReceipt = true;
      const receipt = parseChannelSave(raw, job);
      const canAdopt = validSource(job.capture) && !!live.current.draft && channelDraftKey(live.current.draft) === channelDraftKey(job.draft);
      if (canAdopt && mounted.current) {
        const savedCapture = { ...job.capture, source: { ...job.capture.source, designRevision: receipt.designRevision } };
        // Numeric receipt was checked above. Keep the user's exact raw spellings in the acknowledged baseline.
        const savedDraft = structuredClone(job.draft);
        live.current = { capture: savedCapture, draft: savedDraft, baseline: savedDraft };
        setCapture(savedCapture); setBaseline(savedDraft); setDraft(savedDraft); markDirty(false);
      }
      release(job);
      if (mounted.current) setSaveStatus({ phase: 'saved', operationID: job.operationID, designPath: receipt.designPath, receipt,
        message: canAdopt ? '设计文件已保存，修订已按匹配回执更新。' : '原来源的设计文件已保存；当前来源已变化，未覆盖当前表单。' });
    } catch (caught) {
      const diagnostics = errorDiagnostics(caught);
      if (diagnostics && typeof diagnostics === 'object' && 'operationId' in diagnostics && typeof diagnostics.operationId === 'string') {
        try { captureChannelOperation(job, diagnostics.operationId); } catch { /* Keep the original operation identity. */ }
      }
      const terminal = channelTerminalConfirmed(caught);
      if (terminal) release(job);
      if (mounted.current) setSaveStatus({ phase: returnedReceipt ? 'unverified' : terminal ? 'failed' : 'unknown', operationID: job.operationID,
        designPath: job.request.source.designPath, diagnostics: returnedReceipt ? { rawReceipt, validation: diagnostics } : diagnostics,
        message: returnedReceipt ? '服务器已返回结果，但回执身份或数值未通过核对。原始回执、输入和写保护已保留，尚未采用该修订。'
          : terminal ? '服务器已确认保存任务终止，未收到成功回执。当前输入仍保留，请核对原设计文件。'
          : '保存结果尚未确认。已保留原输入和写保护，请继续核对原任务；不会重复提交保存。' });
    } finally { job.busy = false; }
  }
  async function save() {
    const source = live.current.capture, currentDraft = live.current.draft;
    if (pending.current || !source || !currentDraft || !validSource(source) || source.source.inMemory) return;
    try {
      if (!propsRef.current.onDirtyStateChange) throw new StableChannelError('当前工作区缺少保存保护，请重新打开此页面。');
      if (otherDrafts()) throw new StableChannelError('其他页面有未保存修改或进行中的任务，请先处理后保存稳定河道。');
      const state = channelStateFromDraft(currentDraft);
      invalidateRead(); setError(undefined); setNotice('');
      const requestID = crypto.randomUUID();
      const job: SaveJob = { capture: source, request: { requestID, source: structuredClone(source.source), state,
        coreLayerContext: structuredClone(source.context.coreLayerContext) }, draft: structuredClone(currentDraft), operationID: null,
        owner: `tools.stable-channel-write:${requestID}`, busy: true, register: propsRef.current.onDirtyStateChange };
      // Reserve before the asynchronous readback, so another editor cannot start a competing write.
      pending.current = job; job.register(job.owner, true);
      setSaveStatus({ phase: 'saving', operationID: null, designPath: source.source.designPath, message: '正在核对工程来源…' });
      try {
        await projectReadback(source);
        if (!validSource(source) || !live.current.draft || channelDraftKey(live.current.draft) !== channelDraftKey(job.draft))
          throw new StableChannelError('来源或输入已变化，本次未提交保存。');
      } catch (caught) { release(job); setSaveStatus(null); throw caught; }
      job.busy = false; await trackSave(job, false);
    } catch (caught) { setError(caught); }
  }
  function discard() {
    if (pending.current) return;
    if (!live.current.baseline || !live.current.capture || !validSource(live.current.capture)) { reset(); return; }
    invalidateRead(); setRaw(structuredClone(live.current.baseline)); setError(undefined); setNotice('已放弃本页修改。'); setEditRevision(0);
  }
  return { capture, draft, dirty, current, reading, error, notice, saveStatus, locked: !!pending.current,
    open, change, compute: () => compute(false), save, discard,
    inputError: draft ? channelComputeInputError(draft) : null,
    canSave: !!capture && current && !capture.source.inMemory && !pending.current && !reading && dirty && !otherDrafts() && !!props.onDirtyStateChange && props.backend.supports(stableChannelMethods.save),
    canResume: !!pending.current && !pending.current.busy && !!pending.current.operationID && !!pending.current.capture.backend.resumeOperation,
    resume: async () => { if (pending.current) await trackSave(pending.current, true); },
  };
}
