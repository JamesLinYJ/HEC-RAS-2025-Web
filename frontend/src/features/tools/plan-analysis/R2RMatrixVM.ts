import { useEffect, useRef, useState } from 'react';
import type { BackendGateway, FeaturePageProps } from '../../../shared/workbench';
import { errorDiagnostics } from '../../../shared/workbench';
import { resolveAnalysisDrafts } from '../../../shared/planAnalysisDrafts';
import {
  addR2RAxis, addR2RValues, captureR2RSource, initialR2RForm, isR2RInspectionCurrent,
  makeR2RPreviewRequest, parseR2REmitReceipt, parseR2ROptions, parseR2RPreview, parseR2RProgress,
  r2rConfirmedTerminal, r2rFormKey, r2rMethods, sameR2RPath,
  type R2REmitReceipt, type R2RForm, type R2RInspected, type R2ROptions, type R2RProgress, type R2RSource,
} from './r2rMatrixWorkflow';

export interface R2RRun {
  localID: string; operationID: string | null; phase: 'running' | 'unknown' | 'terminal';
  inspected: R2RInspected; form: R2RForm; progress: R2RProgress | null;
  fileProgress: Record<string, string>; receipt: R2REmitReceipt | null; rawReceipt?: unknown;
  cancelRequested: boolean; terminalMessage: string | null; error?: unknown; diagnostics?: unknown;
}
interface PendingRun {
  localID: string; operationID: string | null; owner: string; inspected: R2RInspected; form: R2RForm;
  controller: AbortController; busy: boolean; register: NonNullable<FeaturePageProps['onDirtyStateChange']>;
  backend: BackendGateway;
}

const emptyForm = (): R2RForm => ({ axes: [], enabledRuleNames: [], collapseIrrelevant: true,
  outputDirectory: '', overwrite: false, nameEmbeddedPlan: true, writeManifest: true });

/** The tool tab stays mounted. Write ownership belongs here, not to a preview table or selection. */
export function useR2RMatrixVM(props: FeaturePageProps) {
  const propsRef = useRef(props); propsRef.current = props;
  const pending = useRef<PendingRun | null>(null);
  const read = useRef<{ generation: number; controller: AbortController | null }>({ generation: 0, controller: null });
  const defaultPlan = (): string => propsRef.current.context.selectedAsset?.kind === 'Plan' ? propsRef.current.context.selectedAsset.path
    : propsRef.current.planAnalysisDrafts?.entries.find(entry => entry.dirty)?.assetPath ?? '';
  const [planPath, setPlanPath] = useState(defaultPlan);
  const [options, setOptions] = useState<R2ROptions | null>(null);
  const [source, setSource] = useState<R2RSource | null>(null);
  const [form, setForm] = useState<R2RForm>(emptyForm);
  const [propertyToAdd, setPropertyToAdd] = useState('');
  const [inspection, setInspection] = useState<R2RInspected | null>(null);
  const [reading, setReading] = useState<'options' | 'preview' | null>(null);
  const [error, setError] = useState<unknown>();
  const [run, setRun] = useState<R2RRun | null>(null);
  const [isTracking, setIsTracking] = useState(false);
  const [formDirty, setFormDirty] = useState(false);
  const formOwner = useRef({ owner: `tools.r2r-form:${props.context.projectSessionID}:${crypto.randomUUID()}`,
    dirty: false, register: props.onDirtyStateChange });
  const markForm = (dirty: boolean): void => {
    if (formOwner.current.dirty !== dirty) formOwner.current.register?.(formOwner.current.owner, dirty);
    formOwner.current.dirty = dirty; setFormDirty(dirty);
  };
  const planRevision = options?.plans.find(plan => sameR2RPath(plan.planPath, planPath))?.revision;

  const resolve = (path: string, revision?: string) => {
    const current = propsRef.current;
    return resolveAnalysisDrafts({ context: current.context, backend: current.backend,
      backendRevision: current.backendRevision, registry: current.planAnalysisDrafts,
      hasUnsavedDrafts: current.hasUnsavedDrafts,
      excludedDirtyOwners: new Set([formOwner.current.owner, ...(pending.current ? [pending.current.owner] : [])]),
    }, current.dirtyOwners ?? new Set(), path ? [{ assetPath: path, revision }] : []);
  };
  const resolution = resolve(planPath, planRevision);
  const live = useRef({ planPath, form, captureKey: resolution.captureKey });
  live.current = { planPath, form, captureKey: resolution.captureKey };
  const contextIdentity = JSON.stringify([props.context.projectSessionID, props.context.projectPath]);
  const previousContext = useRef(contextIdentity);
  const discardRevision = props.planAnalysisDrafts?.discardRevision ?? 0;
  const previousDiscard = useRef(discardRevision);

  useEffect(() => {
    // Selection changes do not retire a dispatched write. Its captured receipt remains visible.
    if (previousContext.current === contextIdentity && previousDiscard.current === discardRevision) return;
    previousDiscard.current = discardRevision;
    previousContext.current = contextIdentity;
    markForm(false);
    formOwner.current = { owner: `tools.r2r-form:${props.context.projectSessionID}:${crypto.randomUUID()}`,
      dirty: false, register: propsRef.current.onDirtyStateChange };
    read.current.generation++; read.current.controller?.abort();
    setReading(null); setOptions(null); setSource(null); setInspection(null); setForm(emptyForm());
    setPlanPath(defaultPlan());
    setPropertyToAdd(''); setError(undefined);
  }, [contextIdentity, discardRevision, props.context.selectedAsset]);

  useEffect(() => {
    if (inspection && !isR2RInspectionCurrent(inspection, props.context.projectSessionID,
      resolution.captureKey, planPath, form)) setInspection(null);
  }, [resolution.captureKey, props.context.projectSessionID, planPath, form, inspection]);

  useEffect(() => () => {
    read.current.generation++; read.current.controller?.abort();
    formOwner.current.register?.(formOwner.current.owner, false);
    // Do not drop the root write owner on unmount. The promise still releases it only at a terminal state.
    pending.current?.controller.abort('R2R view closed; request cancellation and keep tracking the terminal state.');
  }, []);

  const invalidate = (): void => {
    read.current.generation++; read.current.controller?.abort(); read.current.controller = null;
    setReading(null); setInspection(null); setError(undefined);
  };
  const changeForm = (update: (current: R2RForm) => R2RForm): void => {
    if (pending.current) return;
    invalidate(); markForm(true); setForm(update);
  };
  const choosePlan = (path: string): void => {
    if (pending.current) return;
    invalidate(); markForm(true); setPlanPath(path); setSource(null); setForm(emptyForm()); setPropertyToAdd('');
  };
  const requireResolution = (path: string, revision?: string) => {
    const resolved = resolve(path, revision);
    if (!resolved.ok) throw new Error(resolved.blockers.map(blocker => blocker.message).join('；') || '当前草稿尚不能完整捕获。');
    return resolved;
  };

  const loadOptions = async (): Promise<void> => {
    if (pending.current) return;
    invalidate(); const generation = ++read.current.generation, controller = new AbortController();
    read.current.controller = controller; setReading('options');
    const requestedPath = planPath || defaultPlan();
    const start = requireSafeResolution(requestedPath);
    if (!start) { setReading(null); return; }
    const isCurrent = (path: string, revision: string | undefined, key: string): boolean =>
      generation === read.current.generation && !controller.signal.aborted && resolve(path, revision).captureKey === key;
    try {
      let captured = await captureR2RSource(propsRef.current.context, start.captureKey, start.planDrafts);
      if (!isCurrent(requestedPath, undefined, start.captureKey)) return;
      const initialDraft = captured.drafts.find(draft => sameR2RPath(draft.assetPath, requestedPath)) ?? null;
      let loaded = parseR2ROptions(await propsRef.current.backend.request(r2rMethods.options,
        { capture: captured.capture, selectedPlanPath: requestedPath || null, planDraft: initialDraft }, { signal: controller.signal }), captured, requestedPath || null);
      if (!isCurrent(requestedPath, undefined, start.captureKey)) return;
      const selected = loaded.plans.find(plan => loaded.selectedPlanPath && sameR2RPath(plan.planPath, loaded.selectedPlanPath));
      if (selected) {
        const exact = requireResolution(selected.planPath, selected.revision);
        captured = await captureR2RSource(propsRef.current.context, exact.captureKey, exact.planDrafts);
        if (!isCurrent(selected.planPath, selected.revision, exact.captureKey)) return;
        const draft = captured.drafts.find(item => sameR2RPath(item.assetPath, selected.planPath)) ?? null;
        // The first call can discover the default Plan. Its values are never adopted over a live Plan draft.
        if (JSON.stringify(draft) !== JSON.stringify(initialDraft)) {
          loaded = parseR2ROptions(await propsRef.current.backend.request(r2rMethods.options,
            { capture: captured.capture, selectedPlanPath: selected.planPath, planDraft: draft }, { signal: controller.signal }), captured, selected.planPath);
          if (!isCurrent(selected.planPath, selected.revision, exact.captureKey)) return;
        }
      }
      setOptions(loaded); setSource(captured); setPlanPath(loaded.selectedPlanPath ?? '');
      setForm(current => {
        // Explicit reload refreshes source defaults. Preserve the user's server destination and emit choices.
        const fresh = initialR2RForm(loaded);
        return { ...fresh, outputDirectory: current.outputDirectory || fresh.outputDirectory,
          overwrite: current.overwrite, nameEmbeddedPlan: current.nameEmbeddedPlan, writeManifest: current.writeManifest };
      });
      setPropertyToAdd(loaded.properties[0]?.propertyName ?? '');
    } catch (caught) { if (generation === read.current.generation && !controller.signal.aborted) setError(caught); }
    finally { if (generation === read.current.generation) { setReading(null); read.current.controller = null; } }
  };
  function requireSafeResolution(path: string) {
    try { return requireResolution(path); }
    catch (caught) { setError(caught); return null; }
  }

  const preview = async (): Promise<void> => {
    if (pending.current || !options || !source) return;
    invalidate(); const controller = new AbortController(), generation = ++read.current.generation;
    read.current.controller = controller; setReading('preview');
    try {
      const current = requireResolution(planPath, planRevision);
      if (current.captureKey !== source.captureKey) throw new Error('来源或 Plan 草稿已变化，请重新读取参数后预览。');
      const captured = await captureR2RSource(propsRef.current.context, current.captureKey, current.planDrafts);
      const capturedForm = structuredClone(form), formKey = r2rFormKey(capturedForm);
      const request = makeR2RPreviewRequest(captured, options, planPath, capturedForm);
      const result = parseR2RPreview(await propsRef.current.backend.request(r2rMethods.preview, request, { signal: controller.signal }), captured, request);
      if (generation !== read.current.generation || controller.signal.aborted ||
          !isR2RInspectionCurrent({ source: captured, request, preview: result, formKey }, propsRef.current.context.projectSessionID,
            resolve(planPath, planRevision).captureKey, live.current.planPath, live.current.form)) return;
      setInspection({ source: captured, request, preview: result, formKey });
    } catch (caught) { if (generation === read.current.generation && !controller.signal.aborted) setError(caught); }
    finally { if (generation === read.current.generation) { setReading(null); read.current.controller = null; } }
  };

  const patchRun = (job: PendingRun, patch: Partial<R2RRun>): void => {
    setRun(current => current?.localID === job.localID ? { ...current, ...patch } : current);
  };
  const release = (job: PendingRun): void => {
    job.register(job.owner, false);
    if (pending.current === job) pending.current = null;
  };
  const track = async (job: PendingRun, resume: boolean): Promise<void> => {
    if (job.busy) return;
    job.busy = true; job.controller = new AbortController(); setIsTracking(true);
    patchRun(job, { phase: 'running', error: undefined, diagnostics: undefined, cancelRequested: false });
    const requestOptions = { signal: job.controller.signal,
      onProgress: (raw: unknown) => {
        const progress = parseR2RProgress(raw);
        if (!progress) return;
        setRun(current => current?.localID === job.localID ? { ...current, progress,
          fileProgress: progress.filename && progress.status ? { ...current.fileProgress, [progress.filename]: progress.status } : current.fileProgress } : current);
      },
      onTransportState: (state: { operationId: string; state: string }) => {
        if (job.operationID && job.operationID !== state.operationId) return;
        if (!job.operationID) {
          job.operationID = state.operationId;
          const oldOwner = job.owner; job.owner = `tools.r2r-export:${state.operationId}`;
          job.register(job.owner, true); job.register(oldOwner, false);
        }
        patchRun(job, { operationID: job.operationID });
      },
    };
    try {
      const backend = job.backend;
      const raw = resume
        ? await backend.resumeOperation!(job.operationID!, requestOptions)
        : await backend.request(r2rMethods.emit, { preview: job.inspected.request, inspectionID: job.inspected.preview.inspectionID,
          overwrite: job.form.overwrite, nameEmbeddedPlan: job.form.nameEmbeddedPlan, writeManifest: job.form.writeManifest }, requestOptions);
      patchRun(job, { rawReceipt: raw });
      const receipt = parseR2REmitReceipt(raw, job.inspected);
      patchRun(job, { phase: 'terminal', receipt, cancelRequested: false, terminalMessage: null });
      release(job);
    } catch (caught) {
      const diagnostics = errorDiagnostics(caught);
      if (!job.operationID && diagnostics && typeof diagnostics === 'object' && 'operationId' in diagnostics && typeof diagnostics.operationId === 'string') {
        job.operationID = diagnostics.operationId;
        const oldOwner = job.owner; job.owner = `tools.r2r-export:${job.operationID}`;
        job.register(job.owner, true); job.register(oldOwner, false);
      }
      if (r2rConfirmedTerminal(caught)) {
        const cancelled = caught instanceof Error && caught.name === 'AbortError';
        patchRun(job, { phase: 'terminal', operationID: job.operationID, error: cancelled ? undefined : caught, diagnostics,
          cancelRequested: false, terminalMessage: cancelled
            ? 'Host 已确认任务取消，但没有逐文件回执。已生成的文件不会回滚，请核对原输出目录。'
            : 'Host 已确认任务终止。未收到完整逐文件回执；部分文件可能已经生成，请核对原输出目录。' });
        release(job);
      } else {
        patchRun(job, { phase: 'unknown', operationID: job.operationID, error: caught, diagnostics,
          terminalMessage: '任务终态或回执尚未确认。保留原来源和写保护，禁止重复导出；可继续核对同一任务。' });
      }
    } finally { job.busy = false; setIsTracking(false); }
  };

  const emit = async (): Promise<void> => {
    if (pending.current) return;
    try {
      const current = requireResolution(planPath, planRevision);
      if (!propsRef.current.onDirtyStateChange) throw new Error('当前工作区没有任务写保护，暂不能导出。');
      if (!isR2RInspectionCurrent(inspection, propsRef.current.context.projectSessionID, current.captureKey, planPath, form) || !inspection?.preview.writeCount) {
        setInspection(null); throw new Error('当前输入或来源尚未通过预览，请重新预览后导出。');
      }
      const localID = crypto.randomUUID();
      const job: PendingRun = { localID, operationID: null, owner: `tools.r2r-export:pending-${localID}`,
        inspected: structuredClone(inspection), form: structuredClone(form), controller: new AbortController(), busy: false,
        register: propsRef.current.onDirtyStateChange, backend: propsRef.current.backend };
      pending.current = job; job.register(job.owner, true);
      setRun({ localID, operationID: null, phase: 'running', inspected: job.inspected, form: job.form,
        progress: null, fileProgress: {}, receipt: null, cancelRequested: false, terminalMessage: null });
      setInspection(null); setError(undefined);
      await track(job, false);
    } catch (caught) { setError(caught); }
  };
  const resume = async (): Promise<void> => {
    const job = pending.current;
    if (!job || job.busy || !job.operationID || !job.backend.resumeOperation) return;
    await track(job, true);
  };
  const cancel = (): void => {
    const job = pending.current;
    if (!job?.busy) return;
    patchRun(job, { cancelRequested: true });
    job.controller.abort('User requested R2R matrix cancellation.');
  };

  const locked = !!pending.current;
  const currentInspection = isR2RInspectionCurrent(inspection, props.context.projectSessionID,
    resolution.captureKey, planPath, form) ? inspection : null;
  const optionsCurrent = !!source && source.captureKey === resolution.captureKey && source.sessionID === props.context.projectSessionID;
  const availableProperties = options?.properties.filter(property => !form.axes.some(axis => axis.propertyName === property.propertyName)) ?? [];
  const addAxis = (): void => {
    const property = availableProperties.find(item => item.propertyName === propertyToAdd) ?? availableProperties[0];
    if (!property) return;
    try { const next = addR2RAxis(form, property); changeForm(() => next); setPropertyToAdd(availableProperties.find(item => item !== property)?.propertyName ?? ''); }
    catch (caught) { setError(caught); }
  };
  const addValues = (name: string): void => {
    const property = options?.properties.find(item => item.propertyName === name), axis = form.axes.find(item => item.propertyName === name);
    if (!property || !axis) return;
    try { const next = addR2RValues(axis, property); changeForm(current => ({ ...current, axes: current.axes.map(item => item === axis ? next : item) })); }
    catch (caught) { setError(caught); }
  };
  return { planPath, options, source, form, formDirty, inspection: currentInspection, reading, error, run, isTracking, locked,
    resolution, optionsCurrent, availableProperties, propertyToAdd, loadOptions, choosePlan, changeForm, addAxis, addValues,
    preview, emit, resume, cancel,
    setPropertyToAdd: (name: string) => { if (!locked) { invalidate(); markForm(true); setPropertyToAdd(name); } },
    discardForm: () => { if (!locked) { invalidate(); markForm(false); setForm(options && optionsCurrent ? initialR2RForm(options) : emptyForm()); } },
    canEmit: !locked && !reading && resolution.ok && !!props.onDirtyStateChange && props.backend.supports(r2rMethods.emit) &&
      !!inspection?.preview.writeCount && isR2RInspectionCurrent(inspection, props.context.projectSessionID, resolution.captureKey, planPath, form),
    canResume: locked && !isTracking && !!pending.current?.operationID && !!pending.current.backend.resumeOperation,
  };
}
