import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { FeaturePageProps } from '../../../shared/workbench';
import { message } from '../../../shared/workbench';
import { updateStringArray, updateStringMatrix } from './WilcockFields';
import { WilcockBTMControl } from './WilcockBTMControl';
import { WilcockSTATEControl } from './WilcockSTATEControl';
import { WilcockSTABControl } from './WilcockSTABControl';
import { WilcockResults } from './WilcockResults';
import type { WilcockDraft, WilcockWorkspaceSnapshot, WireObject } from './wilcockTypes';
import './hydraulicsUi.css';

type Section = 'btm' | 'stab' | 'state';
type EvaluateAction = 'calculate' | 'addCase' | 'removeCase' | 'importCaseFromBTM';
type BusyAction = 'loading' | 'calculating' | null;

interface ActiveRequest {
  owner: symbol;
  generation: number;
  controller: AbortController;
}

function isObject(value: unknown): value is WireObject {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function parseSnapshot(value: unknown): WilcockWorkspaceSnapshot {
  if (!isObject(value) || !isObject(value.draft) ||
      !Array.isArray(value.cases) || !Array.isArray(value.tables) ||
      !Array.isArray(value.charts) || !Array.isArray(value.inputIssues) || typeof value.btmMode !== 'string') {
    throw new Error('Wilcock 服务返回的工作区快照缺少 draft、cases、tables、charts 或输入诊断。');
  }
  return value as unknown as WilcockWorkspaceSnapshot;
}

function mergeSection(previous: unknown, returned: unknown): unknown {
  if (isObject(previous) && isObject(returned)) return { ...previous, ...returned };
  return returned;
}

/** Keep extension fields from both request and response while adopting C#-returned values. */
function mergeDraft(previous: WilcockDraft, returned: WilcockDraft): WilcockDraft {
  const merged: WilcockDraft = { ...previous, ...returned };
  for (const key of ['sbtm', 'isbtm', 'stab', 'state'] as const) {
    if (Object.prototype.hasOwnProperty.call(returned, key)) {
      (merged as WireObject)[key] = mergeSection(previous[key], returned[key]);
    }
  }
  return merged;
}

function displayAction(action: EvaluateAction): string {
  if (action === 'addCase') return '新增工况';
  if (action === 'removeCase') return '删除工况';
  if (action === 'importCaseFromBTM') return '从 BTM 导入工况';
  return '计算';
}

export function WilcockMainControl({ language, backend }: FeaturePageProps) {
  const zh = language === 'zh';
  const languageRef = useRef(language);
  languageRef.current = language;
  const [draft, setDraft] = useState<WilcockDraft>();
  const [snapshot, setSnapshot] = useState<WilcockWorkspaceSnapshot | null>(null);
  const [activeSection, setActiveSection] = useState<Section>('btm');
  const [busyAction, setBusyAction] = useState<BusyAction>(null);
  const [error, setError] = useState<string>();
  const [notice, setNotice] = useState<string>();
  const [cancelled, setCancelled] = useState(false);
  const [confirmImportDraftKey, setConfirmImportDraftKey] = useState<string | null>(null);
  const generation = useRef(0);
  const currentOwner = useRef<symbol | undefined>(undefined);
  const controller = useRef<AbortController | null>(null);
  const lastEvaluatedDraftKey = useRef<string | null>(null);
  const lastAttemptedDraftKey = useRef<string | null>(null);
  const draftRef = useRef<WilcockDraft | undefined>(undefined);
  draftRef.current = draft;
  const draftKey = useMemo(() => draft ? JSON.stringify(draft) : '', [draft]);
  const supportsLoad = backend.supports('wilcock.load');
  const supportsEvaluate = backend.supports('wilcock.evaluate');

  const beginRequest = useCallback((): ActiveRequest => {
    controller.current?.abort();
    const requestController = new AbortController();
    const owner = Symbol('wilcock-request-owner');
    const requestGeneration = ++generation.current;
    currentOwner.current = owner;
    controller.current = requestController;
    return { owner, generation: requestGeneration, controller: requestController };
  }, []);

  const isCurrent = useCallback((request: ActiveRequest): boolean =>
    currentOwner.current === request.owner && generation.current === request.generation &&
    controller.current === request.controller && !request.controller.signal.aborted, []);

  const invalidateRequest = useCallback(() => {
    generation.current += 1;
    currentOwner.current = undefined;
    controller.current?.abort();
    controller.current = null;
    setBusyAction(null);
  }, []);

  const acceptSnapshot = useCallback((returned: WilcockWorkspaceSnapshot, previous: WilcockDraft) => {
    const merged = mergeDraft(previous, returned.draft);
    lastEvaluatedDraftKey.current = JSON.stringify(merged);
    lastAttemptedDraftKey.current = JSON.stringify(merged);
    draftRef.current = merged;
    setDraft(merged);
    setSnapshot({ ...returned, draft: merged });
    setConfirmImportDraftKey(returned.importConfirmationRequired ? JSON.stringify(merged) : null);
    setCancelled(false);
  }, []);

  const loadDefaults = useCallback(async () => {
    invalidateRequest();
    setError(undefined);
    setNotice(undefined);
    setCancelled(false);
    if (!backend.supports('wilcock.load')) {
      setBusyAction(null);
      setError(languageRef.current === 'zh' ? '当前 C# 服务未注册 wilcock.load。' : 'The C# service has not registered wilcock.load.');
      return;
    }
    const request = beginRequest();
    setBusyAction('loading');
    try {
      const numberCulture = languageRef.current === 'zh' ? 'zh-CN' : 'en-US';
      const raw = await backend.request<unknown>('wilcock.load', { numberCulture }, { signal: request.controller.signal });
      if (!isCurrent(request)) return;
      const returned = parseSnapshot(raw);
      acceptSnapshot(returned, {});
    } catch (requestError) {
      if (isCurrent(request)) setError(message(requestError, languageRef.current));
    } finally {
      if (isCurrent(request)) {
        setBusyAction(null);
        controller.current = null;
        currentOwner.current = undefined;
      }
    }
  }, [acceptSnapshot, backend, beginRequest, invalidateRequest, isCurrent]);

  const evaluate = useCallback(async (action: EvaluateAction, caseID?: string, confirmImport = false) => {
    const currentDraft = draftRef.current;
    if (!currentDraft) return;
    if (!backend.supports('wilcock.evaluate')) {
      setError(languageRef.current === 'zh' ? '当前 C# 服务未注册 wilcock.evaluate。' : 'The C# service has not registered wilcock.evaluate.');
      return;
    }
    const request = beginRequest();
    lastAttemptedDraftKey.current = JSON.stringify(currentDraft);
    setBusyAction('calculating');
    setError(undefined);
    setNotice(undefined);
    setCancelled(false);
    try {
      const raw = await backend.request<unknown>('wilcock.evaluate', {
        draft: currentDraft,
        action,
        ...(caseID ? { caseID } : {}),
        confirmImport,
      }, { signal: request.controller.signal });
      if (!isCurrent(request)) return;
      const returned = parseSnapshot(raw);
      acceptSnapshot(returned, currentDraft);
      if (action !== 'calculate' && action !== 'importCaseFromBTM') {
        setNotice(`${displayAction(action)}：已接收 C# 工作区回执。`);
      }
      if (action === 'importCaseFromBTM') {
        if (returned.importConfirmationRequired && !confirmImport) {
          setNotice(returned.btmImportPolicy === 'replaceExistingCases'
            ? 'C# 服务要求确认替换现有 STAB 工况后才能导入。'
            : 'C# 服务要求确认追加 STAB 工况后才能导入。');
        } else if (returned.replacedExistingCases) {
          setNotice('C# 回执确认导入工况时替换了原 STAB 工况。');
        } else if (confirmImport && returned.canImportCase) {
          setNotice('C# 回执确认已将 BTM 工况追加到 STAB。');
        } else if (!returned.canImportCase) {
          setNotice(returned.btmImportReasonKey
            ? `C# 当前不能导入工况：${returned.btmImportReasonKey}`
            : 'C# 当前判定没有可导入的 BTM 工况。');
        }
      }
    } catch (requestError) {
      if (isCurrent(request)) setError(message(requestError, languageRef.current));
    } finally {
      if (isCurrent(request)) {
        setBusyAction(null);
        controller.current = null;
        currentOwner.current = undefined;
      }
    }
  }, [acceptSnapshot, backend, beginRequest, isCurrent]);

  useEffect(() => {
    void loadDefaults();
    return () => {
      generation.current += 1;
      currentOwner.current = undefined;
      controller.current?.abort();
      controller.current = null;
    };
  }, [loadDefaults]);

  useEffect(() => {
    if (!draft || busyAction !== null) return;
    if (draftKey === lastAttemptedDraftKey.current) return;
    const timer = window.setTimeout(() => void evaluate('calculate'), 280);
    return () => window.clearTimeout(timer);
  }, [busyAction, draft, draftKey, evaluate]);

  const updateDraft = (next: WilcockDraft): void => {
    invalidateRequest();
    draftRef.current = next;
    setDraft(next);
    setConfirmImportDraftKey(null);
    setNotice(undefined);
    setCancelled(false);
  };

  const updateSectionScalar = (section: 'sbtm' | 'isbtm' | 'stab' | 'state', key: string, value: string | boolean): void => {
    if (!draft) return;
    const current = isObject(draft[section]) ? draft[section] as WireObject : {};
    updateDraft({ ...draft, [section]: { ...current, [key]: value } });
  };

  const updateSectionArray = (section: 'sbtm' | 'isbtm' | 'stab' | 'state', key: string, index: number, value: string): void => {
    if (!draft) return;
    const current = isObject(draft[section]) ? draft[section] as WireObject : {};
    updateDraft({ ...draft, [section]: { ...current, [key]: updateStringArray(current[key], index, value) } });
  };

  const updateStabMatrix = (row: number, column: number, value: string): void => {
    if (!draft) return;
    const stab = isObject(draft.stab) ? draft.stab as WireObject : {};
    updateDraft({ ...draft, stab: { ...stab, transportGrainSizes: updateStringMatrix(stab.transportGrainSizes, row, column, value) } });
  };

  const cancel = (): void => {
    if (!busyAction) return;
    invalidateRequest();
    setCancelled(true);
    setNotice('已请求取消当前 C# 请求；迟到的结果不会覆盖当前输入。');
  };

  const availableTabs: Array<{ id: Section; label: string }> = [
    { id: 'btm', label: '底床输沙 · BTM' },
    { id: 'stab', label: '输沙与断面 · STAB' },
    { id: 'state', label: '输沙状态 · STATE' },
  ];
  const stale = Boolean(draft && draftKey !== lastEvaluatedDraftKey.current);
  const selectedPlotIndex = draft?.selectedPlotIndex ?? snapshot?.selectedPlotIndex ?? 0;
  const stabCharts = snapshot?.charts.filter(chart => chart.section === 'stab') ?? [];
  const selectedStabChartID = stabCharts[selectedPlotIndex]?.id;
  const importNeedsConfirmation = Boolean(snapshot?.importConfirmationRequired && confirmImportDraftKey === draftKey);
  const importLabel = snapshot?.btmImportPolicy === 'replaceExistingCases'
    ? '确认替换现有 STAB 工况并导入'
    : '确认追加 STAB 工况';

  return <main className="hydraulics-workspace">
    <header className="hydraulics-header">
      <div><span className="tools-kicker">{zh ? '水力设计' : 'HYDRAULIC DESIGN'}</span>
        <h2>{zh ? 'Wilcock 输沙计算' : 'Wilcock sediment transport'}</h2>
        <p>Wilcock 粒径输沙与河道稳定性分析。</p>
      </div>
      <div className="hydraulics-header-actions">
        <span className={supportsEvaluate ? 'hydraulics-runtime is-connected' : 'hydraulics-runtime'}>{supportsEvaluate ? '计算服务可用' : '计算服务不可用'}</span>
        <button type="button" className="hydraulics-button" disabled={busyAction !== null || !supportsLoad} onClick={() => void loadDefaults()}>加载源默认值</button>
        <button type="button" className="hydraulics-button hydraulics-button-primary" disabled={busyAction !== null || !draft || !supportsEvaluate} onClick={() => void evaluate('calculate')}>重新计算</button>
        {busyAction && <button type="button" className="hydraulics-button hydraulics-button-danger" onClick={cancel}>取消</button>}
      </div>
    </header>
    {!supportsLoad && <p className="hydraulics-warning" role="status">当前 C# 服务未注册 <code>wilcock.load</code>；控件不构造默认输入或模拟结果。</p>}
    {!supportsEvaluate && <p className="hydraulics-warning" role="status">当前 C# 服务未注册 <code>wilcock.evaluate</code>；输入仍可查看和编辑，但不会计算。</p>}
    {busyAction && <p className="hydraulics-status" role="status">{busyAction === 'loading' ? '正在加载参数…' : '正在计算…'}</p>}
    {cancelled && <p className="hydraulics-warning" role="status">已取消请求；服务端若已完成，旧回执会因请求代次失效而被丢弃。</p>}
    {error && <p className="hydraulics-error" role="alert">{error}</p>}
    {notice && <p className="hydraulics-status" role="status">{notice}</p>}
    {draft && snapshot && <>
      <nav className="hydraulics-tabs" role="tablist" aria-label="Wilcock 方法区段">
        {availableTabs.map(tab => <button key={tab.id} type="button" role="tab" aria-selected={activeSection === tab.id}
          className={activeSection === tab.id ? 'hydraulics-tab is-active' : 'hydraulics-tab'} onClick={() => setActiveSection(tab.id)}>{tab.label}</button>)}
      </nav>
      {stale && <p className="hydraulics-warning" role="status">输入已修改；下方保留上一次结果，新计算完成后更新。</p>}
      <section className="hydraulics-layout">
        <div className="hydraulics-input-column">
          {activeSection === 'btm' && <WilcockBTMControl draft={draft} mode={typeof draft.btmMode === 'string' ? draft.btmMode : snapshot.btmMode} onDraftChange={updateDraft} />}
          {activeSection === 'stab' && <>
            <WilcockSTABControl
              draft={draft.stab ?? {}}
              cases={snapshot.cases}
              selectedCaseID={typeof draft.selectedCaseID === 'string' ? draft.selectedCaseID : snapshot.selectedCaseID}
              canAddCase={snapshot.canAddCase && supportsEvaluate && !busyAction}
              canRemoveCase={snapshot.canRemoveCase && supportsEvaluate && !busyAction}
              importAvailable={snapshot.canImportCase && supportsEvaluate && !busyAction}
              onScalarChange={(key, value) => updateSectionScalar('stab', key, value)}
              onBooleanChange={(key, value) => updateSectionScalar('stab', key, value)}
              onArrayChange={(key, index, value) => updateSectionArray('stab', key, index, value)}
              onMatrixChange={updateStabMatrix}
              onCaseSelect={caseID => updateDraft({ ...draft, selectedCaseID: caseID })}
              onCaseAction={(action, caseID) => void evaluate(action, caseID)}
              onImportCase={() => void evaluate('importCaseFromBTM', undefined, false)}
            />
            {importNeedsConfirmation && <div className="hydraulics-confirmation" role="group" aria-label="确认导入 BTM 工况">
              <p>{snapshot.btmImportPolicy === 'replaceExistingCases'
                ? 'C# 判定粒径网格不一致；确认后将按后端回执替换现有 STAB 工况。'
                : 'C# 判定可以追加工况；确认后将按后端回执追加到 STAB。'}</p>
              <button type="button" className="hydraulics-button hydraulics-button-primary" disabled={busyAction !== null} onClick={() => void evaluate('importCaseFromBTM', undefined, true)}>{importLabel}</button>
              <button type="button" className="hydraulics-link-button" onClick={() => setConfirmImportDraftKey(null)}>取消确认</button>
            </div>}
            <div className="hydraulics-chart-picker" aria-label="STAB 图表">
              {stabCharts.map((chart, index) => <button key={chart.id} type="button" aria-pressed={selectedPlotIndex === index}
                className={selectedPlotIndex === index ? 'hydraulics-chart-tab is-active' : 'hydraulics-chart-tab'}
                onClick={() => updateDraft({ ...draft, selectedPlotIndex: index })}>{index + 1}. {chart.titleKey}</button>)}
            </div>
          </>}
          {activeSection === 'state' && <WilcockSTATEControl draft={draft.state ?? {}}
            onArrayChange={(key, index, value) => updateSectionArray('state', key, index, value)} />}
          {!supportsEvaluate && <p className="hydraulics-warning">参数仅保留在此工作区；没有可用的 C# 计算结果。</p>}
        </div>
        <aside className="hydraulics-output-column">
          <WilcockResults snapshot={snapshot} section={activeSection} language={language} stale={stale}
            chartID={activeSection === 'stab' ? selectedStabChartID : undefined} />
        </aside>
      </section>
      <details className="hydraulics-raw-draft"><summary>完整原始输入 draft（含服务扩展字段）</summary><pre>{JSON.stringify(draft, null, 2)}</pre></details>
    </>}
    {!snapshot && !busyAction && <div className="hydraulics-empty">尚未加载 Wilcock 工作区。</div>}
  </main>;
}
