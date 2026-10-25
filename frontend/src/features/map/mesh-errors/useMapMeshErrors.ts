import { useEffect, useRef, useState } from 'react';
import { errorDiagnostics, text } from '../../../shared/workbench';
import type { BackendGateway, FeaturePageProps, Language, MapVisualizationTarget } from '../../../shared/workbench';
import { sameMapRevision } from '../associatedMapLayers';
import { mapError } from '../mapText';
import { captureMapMeshErrors, loadCapturedMeshErrors, meshErrorWriteReceipt, normalizeMeshThreshold,
  parseMapMeshErrorSnapshot, readbackMapMeshErrors } from './mapMeshErrorsWorkflow';
import type { MapMeshErrorCandidate, MapMeshErrorCapture, MapMeshErrorReadback, MapMeshErrorSnapshot } from './mapMeshErrorsWorkflow';

export interface MapMeshErrorGuard { locked: boolean; transaction: boolean }
type Phase = 'idle' | 'loading' | 'ready' | 'cancelled' | 'error' | 'writing' | 'readback' | 'saved' |
  'unknown' | 'receiptNotAdopted' | 'readbackFailed' | 'reconciled';
interface State {
  phase: Phase;
  capture: MapMeshErrorCapture | null;
  snapshot: MapMeshErrorSnapshot | null;
  enabled: boolean;
  threshold: string;
  message: string;
  diagnostic: string | null;
}
interface Transaction {
  capture: MapMeshErrorCapture;
  before: MapMeshErrorSnapshot;
  enabled: boolean;
  threshold: number;
  target: MapVisualizationTarget;
  leaseID: string;
  saved: MapMeshErrorSnapshot | null;
  adopted: boolean;
  terminalConfirmed: boolean;
  controller: AbortController;
  busy: boolean;
}
const initial: State = { phase: 'idle', capture: null, snapshot: null, enabled: false, threshold: '8', message: '', diagnostic: null };
const dirty = (state: State) => !!state.snapshot &&
  (state.enabled !== state.snapshot.checkMeshErrors || state.threshold !== String(state.snapshot.maxFacesPerNode));

interface Options extends Pick<FeaturePageProps, 'onBeginMapVisualizationMutation' | 'onCommitMapVisualization' |
  'onEndMapVisualizationMutation' | 'onDirtyStateChange' | 'dirtyOwners' | 'hasUnsavedDrafts'> {
  backend: BackendGateway;
  backendRevision?: number;
  language: Language;
  scope: string;
  candidate: MapMeshErrorCandidate | null;
  selectionKey: string | null;
  visible: boolean;
  otherBusy: boolean;
  onGuardChange: (guard: MapMeshErrorGuard) => void;
  onInstall: (readback: MapMeshErrorReadback, previous: MapMeshErrorCapture) => boolean;
}

/** Lives in MapPage, outside the conditional node inspector and catalog refresh lifecycle. */
export function useMapMeshErrors(options: Options) {
  const [state, setState] = useState<State>(initial);
  const stateRef = useRef(state); stateRef.current = state;
  const optionsRef = useRef(options); optionsRef.current = options;
  const transaction = useRef<Transaction | null>(null);
  const reading = useRef<AbortController | null>(null);
  const generation = useRef(0);
  const [refresh, setRefresh] = useState(0);
  const loadedRefresh = useRef(0);
  const draftOwner = 'map.mesh-errors:' + options.scope;
  const ownerRef = useRef(draftOwner); ownerRef.current = draftOwner;
  const mounted = useRef(true);

  const publish = (next: State, pending = transaction.current !== null) => {
    stateRef.current = next;
    setState(next);
    const locked = dirty(next) || pending;
    optionsRef.current.onGuardChange({ locked, transaction: pending });
    optionsRef.current.onDirtyStateChange?.(ownerRef.current, locked);
  };
  const isCurrent = (capture: MapMeshErrorCapture) =>
    optionsRef.current.candidate?.key === capture.key && mounted.current;
  const installed = (readback: MapMeshErrorReadback, message: string, phase: Phase) => {
    publish({ phase, capture: readback.capture, snapshot: readback.snapshot,
      enabled: readback.snapshot.checkMeshErrors, threshold: String(readback.snapshot.maxFacesPerNode),
      message, diagnostic: readback.snapshot.diagnostic });
  };

  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
      generation.current++;
      reading.current?.abort();
      // A submitted write is not declared cancelled or released by unmounting.
      // Root's lease prevents switching project sessions while it is unresolved.
      if (!transaction.current) optionsRef.current.onDirtyStateChange?.(ownerRef.current, false);
    };
  }, []);

  useEffect(() => {
    const token = ++generation.current;
    reading.current?.abort();
    if (transaction.current || dirty(stateRef.current)) return;
    const candidate = options.candidate;
    if (!candidate) {
      // A catalog refresh temporarily has no active map. Keep the last capture
      // until the new catalog arrives, but it cannot be edited or used to zoom.
      const capturedSelection = stateRef.current.capture
        ? JSON.stringify([stateRef.current.capture.document.id, stateRef.current.capture.flow.id]) : null;
      if (options.selectionKey !== null && options.selectionKey !== capturedSelection) publish(initial);
      return;
    }
    if (stateRef.current.capture?.key === candidate.key && loadedRefresh.current === refresh) return;
    loadedRefresh.current = refresh;
    const controller = new AbortController();
    reading.current = controller;
    publish({ ...initial, phase: 'loading', message: text(options.language, '正在读取保存的网格检查设置…', 'Reading saved mesh checks…') });
    void (async () => {
      try {
        const capture = await captureMapMeshErrors(options.backend, candidate, controller.signal);
        const snapshot = await loadCapturedMeshErrors(options.backend, capture, controller.signal);
        if (controller.signal.aborted || token !== generation.current || !isCurrent(capture)) return;
        publish({ phase: 'ready', capture, snapshot, enabled: snapshot.checkMeshErrors,
          threshold: String(snapshot.maxFacesPerNode), message: '', diagnostic: snapshot.diagnostic });
      } catch (error) {
        if (controller.signal.aborted || token !== generation.current) return;
        publish({ ...initial, phase: 'error', message: text(options.language, '网格检查读取失败。', 'Mesh checks could not be read.'),
          diagnostic: mapError(options.language, error) });
      }
    })();
    return () => controller.abort();
  }, [options.backend, options.backendRevision, options.candidate?.key, options.selectionKey, refresh]);

  const finishTransaction = (pending: Transaction, readback: MapMeshErrorReadback, message: string, phase: Phase) => {
    if (!mounted.current || transaction.current !== pending ||
        !optionsRef.current.onInstall(readback, pending.capture)) throw new Error('当前地图会话已变化，读回结果未安装。');
    // Install the new selection before allowing the catalog effect to resume.
    transaction.current = null;
    installed(readback, message, phase);
    optionsRef.current.onEndMapVisualizationMutation?.(pending.leaseID);
  };
  const adopt = (pending: Transaction) => {
    if (pending.adopted) return true;
    if (!pending.saved) return false;
    pending.adopted = optionsRef.current.onCommitMapVisualization?.(pending.capture.context, pending.target,
      meshErrorWriteReceipt(pending.saved), pending.leaseID) ?? false;
    return pending.adopted;
  };
  const completeReadback = async (pending: Transaction) => {
    const readback = await readbackMapMeshErrors(optionsRef.current.backend, pending.capture,
      pending.controller.signal, pending.saved ?? undefined);
    if (!mounted.current || transaction.current !== pending) return;
    if (pending.controller.signal.aborted) throw new DOMException('已取消核对；写入保护仍然有效。', 'AbortError');
    if (!pending.saved || !adopt(pending)) {
      const unchanged = pending.terminalConfirmed && !pending.saved &&
        sameMapRevision(readback.snapshot.visualizationRevision, pending.before.visualizationRevision) &&
        readback.snapshot.checkMeshErrors === pending.before.checkMeshErrors &&
        readback.snapshot.maxFacesPerNode === pending.before.maxFacesPerNode;
      if (unchanged) {
        finishTransaction(pending, readback, text(optionsRef.current.language,
          '已核对：本次操作已结束，保存设置未改变。', 'Reconciled: the operation ended without changing the saved settings.'), 'reconciled');
        return;
      }
      publish({ ...stateRef.current, phase: pending.saved ? 'receiptNotAdopted' : 'unknown',
        message: text(optionsRef.current.language,
          pending.saved ? '已读取保存设置，但工作区尚未接纳写入回执；请继续核对。'
            : '已读取当前设置；先前写入的最终结果仍未确认，保存保持锁定。',
          pending.saved ? 'Saved settings were read, but the workspace has not adopted the receipt. Reconcile again.'
            : 'Current settings were read, but the earlier write remains unconfirmed. Saving is locked.'),
        diagnostic: readback.snapshot.diagnostic });
      return;
    }
    if (readback.snapshot.status === 'writerProtected' || readback.snapshot.status === 'savedReadbackUnavailable') {
      publish({ ...stateRef.current, phase: 'readbackFailed', message: text(optionsRef.current.language,
        '设置已写入，网格诊断尚未完成读回；请稍后重新核对。', 'Settings were written; the mesh scan is not yet verified. Reconcile again later.'),
        diagnostic: readback.snapshot.diagnostic });
      return;
    }
    finishTransaction(pending, readback, text(optionsRef.current.language, '设置已保存并重新读取。', 'Settings saved and reread.'), 'saved');
  };

  const save = async () => {
    const current = stateRef.current, opts = optionsRef.current;
    const threshold = Number(current.threshold);
    if (transaction.current || !current.capture || !current.snapshot || !dirty(current) ||
        !current.threshold.trim() || !Number.isFinite(threshold) || !isCurrent(current.capture) || opts.otherBusy ||
        !opts.onBeginMapVisualizationMutation || !opts.onCommitMapVisualization || !opts.onEndMapVisualizationMutation ||
        (opts.dirtyOwners ? [...opts.dirtyOwners].some(owner => owner !== ownerRef.current) : opts.hasUnsavedDrafts && !dirty(current))) return;
    const capture = current.capture;
    const target: MapVisualizationTarget = { mapID: capture.map.mapID, mapIndex: capture.map.mapIndex,
      name: capture.map.mapName, mapType: capture.map.mapType, visualizationRevision: capture.map.expectedVisualizationRevision };
    const leaseID = opts.onBeginMapVisualizationMutation(capture.context, target, ownerRef.current);
    if (!leaseID) {
      publish({ ...current, message: text(opts.language, '其他草稿或写入尚未结束，暂时无法保存。', 'Another draft or write is active; saving is unavailable.') });
      return;
    }
    const pending: Transaction = { capture, before: current.snapshot, enabled: current.enabled, threshold, target,
      leaseID, saved: null, adopted: false, terminalConfirmed: false, controller: new AbortController(), busy: true };
    transaction.current = pending;
    reading.current?.abort();
    generation.current++;
    publish({ ...current, phase: 'writing', message: text(opts.language, '正在保存网格检查设置…', 'Saving mesh checks…'), diagnostic: null });
    try {
      const raw = await opts.backend.request<unknown>('map.tree.meshErrors', {
        ...capture.request, action: 'set', checkMeshErrors: pending.enabled, maxFacesPerNodeEntry: threshold,
      }, { signal: pending.controller.signal });
      // A valid terminal receipt is retained even if cancellation raced completion.
      pending.terminalConfirmed = true;
      pending.saved = parseMapMeshErrorSnapshot(raw, capture, 'set', { enabled: pending.enabled, threshold });
      if (!adopt(pending)) {
        publish({ ...stateRef.current, phase: 'receiptNotAdopted', message: text(opts.language,
          '服务已返回保存回执，但工作区未接纳；请重新读取并核对。', 'The service returned a receipt that the workspace did not adopt. Reread and reconcile.') });
        return;
      }
      pending.controller = new AbortController();
      publish({ ...stateRef.current, phase: 'readback', message: text(opts.language,
        '正在核对新修订和网格诊断…', 'Verifying the new revision and mesh scan…') });
      await completeReadback(pending);
    } catch (error) {
      if (!mounted.current || transaction.current !== pending) return;
      const diagnostics = errorDiagnostics(error);
      pending.terminalConfirmed ||= !!diagnostics && typeof diagnostics === 'object' &&
        'terminalConfirmed' in diagnostics && diagnostics.terminalConfirmed === true;
      publish({ ...stateRef.current, phase: pending.saved ? 'readbackFailed' : 'unknown',
        message: text(opts.language, pending.saved ? '设置已写入，但完整读回未完成；请重新读取并核对。'
          : '本次写入结果尚未核对；请重新读取，核对前不能再次保存。',
        pending.saved ? 'Settings were written, but the full readback is incomplete. Reread and reconcile.'
          : 'The write outcome has not been reconciled. Reread before saving again.'), diagnostic: mapError(opts.language, error) });
    } finally { pending.busy = false; }
  };

  const reconcile = async () => {
    const pending = transaction.current;
    if (!pending || pending.busy) return;
    pending.busy = true;
    pending.controller = new AbortController();
    publish({ ...stateRef.current, phase: 'readback', message: text(optionsRef.current.language,
      '正在重新读取并核对…', 'Rereading and reconciling…'), diagnostic: null });
    try { await completeReadback(pending); }
    catch (error) {
      if (transaction.current !== pending || !mounted.current) return;
      publish({ ...stateRef.current, phase: pending.saved ? 'readbackFailed' : 'unknown',
        message: text(optionsRef.current.language, '尚未完成核对，保存保持锁定。', 'Reconciliation is incomplete; saving remains locked.'),
        diagnostic: mapError(optionsRef.current.language, error) });
    } finally { pending.busy = false; }
  };
  const cancel = () => {
    const pending = transaction.current;
    if (pending) {
      pending.controller.abort();
      publish({ ...stateRef.current, message: text(optionsRef.current.language,
        '已请求取消；写入是否完成仍需核对。', 'Cancellation requested; the write outcome still requires reconciliation.') });
    } else {
      reading.current?.abort();
      generation.current++;
      publish({ ...stateRef.current, phase: 'cancelled', message: text(optionsRef.current.language, '已取消读取。', 'Read cancelled.') });
    }
  };
  const change = (patch: Partial<Pick<State, 'enabled' | 'threshold'>>) => {
    if (transaction.current || !stateRef.current.capture || !isCurrent(stateRef.current.capture)) return;
    publish({ ...stateRef.current, ...patch, message: '', diagnostic: null });
  };
  const revert = () => {
    const current = stateRef.current;
    if (transaction.current || !current.snapshot) return;
    publish({ ...current, enabled: current.snapshot.checkMeshErrors, threshold: String(current.snapshot.maxFacesPerNode),
      message: text(optionsRef.current.language, '已放弃未保存的设置。', 'Unsaved settings discarded.') });
  };
  const pending = transaction.current;
  const isDirty = dirty(state);
  const current = !!state.capture && options.candidate?.key === state.capture.key;
  const thresholdValid = !!state.threshold.trim() && Number.isFinite(Number(state.threshold));
  const unsupported = !['map.tree.meshErrors', 'workspace.file.inspect', 'map.resultSources', 'map.document.list', 'map.tree.load']
    .every(method => options.backend.supports(method));
  return { state, visible: options.visible || isDirty || !!pending, dirty: isDirty, locked: isDirty || !!pending,
    current, busy: ['loading', 'writing', 'readback'].includes(state.phase), pending: !!pending,
    canSave: current && isDirty && thresholdValid && !unsupported && !pending && !options.otherBusy &&
      !!options.onBeginMapVisualizationMutation && !!options.onCommitMapVisualization && !!options.onEndMapVisualizationMutation &&
      !(options.dirtyOwners && [...options.dirtyOwners].some(owner => owner !== draftOwner)),
    normalizedThreshold: thresholdValid ? normalizeMeshThreshold(Number(state.threshold)) : null,
    setEnabled: (enabled: boolean) => change({ enabled }), setThreshold: (threshold: string) => change({ threshold }),
    save, reconcile, cancel, revert, refresh: () => { if (!pending && !isDirty) setRefresh(value => value + 1); },
  };
}

export type MapMeshErrorsController = ReturnType<typeof useMapMeshErrors>;
