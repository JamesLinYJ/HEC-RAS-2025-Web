import { useEffect, useRef, useState } from 'react';
import { ServerBackend, ServerBackendError } from '../../platform/ServerBackend';
import { decodeResult, validateGeometry, type DecodedFlood, type FloodGeometry, type FloodResult } from './results';
import type { FloodScenario } from './scenario';

export function useFloodCompute(scenario: FloodScenario) {
  const [backend] = useState(() => new ServerBackend());
  const [connected, setConnected] = useState(false);
  const [geometry, setGeometry] = useState<FloodGeometry | null>(null);
  const [result, setResult] = useState<DecodedFlood | null>(null);
  const [busy, setBusy] = useState(false), [canceling, setCanceling] = useState(false);
  const [progress, setProgress] = useState(0), [message, setMessage] = useState('');
  const [recovery, setRecovery] = useState<string | null>(null);
  const [uncertain, setUncertain] = useState(false);
  const controller = useRef<AbortController | null>(null), alive = useRef(true), generation = useRef(0);
  const active = useRef(false);
  const connect = async () => {
    try { await backend.connect(); if (alive.current) { setConnected(backend.supports('flood.compute')); setMessage(''); } }
    catch { if (alive.current) { setConnected(false); setMessage('暂时无法连接，请重试'); } }
  };
  useEffect(() => { void connect(); return () => { alive.current = false; controller.current?.abort(); backend.dispose(); }; }, [backend]);
  useEffect(() => {
    if (!connected || result || busy) return;
    let current = true;
    const timeout = setTimeout(() => {
      void backend.request<{ geometry: FloodGeometry }>('flood.scene', scenario).then(value => {
        if (current && alive.current) setGeometry(validateGeometry(value.geometry));
      }).catch(() => { if (current && alive.current) setMessage('河谷加载失败，请重试'); });
    }, 300);
    return () => { current = false; clearTimeout(timeout); };
  }, [backend, connected, scenario, result, busy]);
  useEffect(() => {
    if (!busy && !uncertain) return;
    const guard = (event: BeforeUnloadEvent) => event.preventDefault();
    window.addEventListener('beforeunload', guard);
    return () => window.removeEventListener('beforeunload', guard);
  }, [busy, uncertain]);
  const run = async (resume = false) => {
    if (active.current || (!resume && uncertain)) return false;
    active.current = true;
    const ticket = ++generation.current;
    const abort = new AbortController(); controller.current = abort;
    setBusy(true); setCanceling(false); setProgress(0); setMessage('');
    try {
      const options = { signal: abort.signal, onProgress: (raw: unknown) => {
        const p = raw as { fraction?: unknown };
        if (alive.current && ticket === generation.current && typeof p?.fraction === 'number')
          setProgress(v => Math.max(v, Math.min(.99, Math.max(0, p.fraction as number))));
      } };
      const response = resume && recovery ? await backend.resumeOperation<FloodResult>(recovery, options) :
        await backend.request<FloodResult>('flood.compute', { ...scenario }, options);
      if (!alive.current || ticket !== generation.current) return false;
      if ((response as { state: string }).state === 'canceled') { setMessage('已取消'); setRecovery(null); setUncertain(false); return false; }
      const decoded = decodeResult(response);
      setResult(decoded); setGeometry(decoded.geometry); setProgress(1); setRecovery(null); setUncertain(false);
      return true;
    } catch (error) {
      if (alive.current && ticket === generation.current) {
        if (error instanceof ServerBackendError && error.code === 'OPERATION_STATUS_UNCONFIRMED') {
          setRecovery(error.operationId ?? null); setUncertain(true); setMessage('连接中断，正在保留本次任务');
        } else if (error instanceof Error && error.name === 'AbortError') { setMessage('已取消'); setRecovery(null); setUncertain(false); }
        else { setMessage('推演未完成，请重试'); setRecovery(null); setUncertain(false); }
      }
      return false;
    } finally { active.current = false; if (alive.current && ticket === generation.current) { setBusy(false); setCanceling(false); controller.current = null; } }
  };
  const cancel = () => { setCanceling(true); controller.current?.abort(); };
  const stale = Boolean(result && JSON.stringify(scenario) !== JSON.stringify(result.scenario));
  return { connected, geometry, result, busy, canceling, progress, message, recovery, uncertain, stale, run, cancel, connect };
}
