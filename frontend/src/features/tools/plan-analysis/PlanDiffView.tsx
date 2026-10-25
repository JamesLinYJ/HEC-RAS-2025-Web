import { useEffect, useMemo, useRef, useState } from 'react';
import type { BackendGateway, FeaturePageProps } from '../../../shared/workbench';
import { errorDiagnostics, message } from '../../../shared/workbench';
import { resolveAnalysisDrafts } from '../../../shared/planAnalysisDrafts';
import type { PlanDiffOption } from './PlanDiffOption';
import {
  capturePlanDiffProject, parsePlanDiffOptions, parsePlanDiffResult, planDiffComparisonKey,
  planDiffSourceKey, PlanDiffRequestGate,
} from './planDiffWorkflow';
import type { PlanDiffProjectCapture, PlanDiffResult } from './planDiffWorkflow';
import './planDiff.css';

interface Catalog {
  sourceKey: string;
  backend: BackendGateway;
  capture: PlanDiffProjectCapture;
  options: PlanDiffOption[];
}

interface RequestState {
  key: string;
  status: 'loading' | 'ready' | 'error' | 'cancelled';
  error?: unknown;
}

interface ComparisonState extends RequestState { result?: PlanDiffResult; draftCount?: number }
const emptyOwners: ReadonlySet<string> = new Set();

function diagnosticsText(error: unknown): string {
  const diagnostics = errorDiagnostics(error);
  try { return typeof diagnostics === 'string' ? diagnostics : JSON.stringify(diagnostics, null, 2); }
  catch { return String(diagnostics); }
}

/** Read-only port of PlanDiffView. The Host reflects the same simple Plan properties as C#. */
export function PlanDiffView(props: FeaturePageProps) {
  const { context, backend, backendRevision, language, planAnalysisDrafts } = props;
  const zh = language === 'zh';
  const sourceKey = planDiffSourceKey(context, backendRevision);
  const dirtyOwners = props.dirtyOwners ?? emptyOwners;
  const dirtyKey = JSON.stringify([...dirtyOwners].sort());
  const [catalog, setCatalog] = useState<Catalog | null>(null);
  const [selection, setSelection] = useState({ sourceKey: '', leftID: '', rightID: '' });
  const [loadState, setLoadState] = useState<RequestState | null>(null);
  const [comparison, setComparison] = useState<ComparisonState | null>(null);
  const [differencesOnly, setDifferencesOnly] = useState(false);
  const [loadVersion, setLoadVersion] = useState(0);
  const [compareVersion, setCompareVersion] = useState(0);
  const catalogGate = useRef(new PlanDiffRequestGate());
  const comparisonGate = useRef(new PlanDiffRequestGate());
  const currentCatalog = catalog?.sourceKey === sourceKey && catalog.backend === backend ? catalog : null;
  const options = currentCatalog?.options ?? [];
  const left = selection.sourceKey === sourceKey ? options.find(option => option.id === selection.leftID) : undefined;
  const right = selection.sourceKey === sourceKey ? options.find(option => option.id === selection.rightID) : undefined;
  const requiredPlans = useMemo(() => [...new Map([left, right].flatMap(option => option
    ? [[option.planPath, { assetPath: option.planPath, revision: option.planRevision }] as const] : [])).values()], [left, right]);
  const draftResolution = useMemo(() => resolveAnalysisDrafts({ context, backend, backendRevision,
    registry: planAnalysisDrafts, hasUnsavedDrafts: props.hasUnsavedDrafts }, dirtyOwners, requiredPlans),
  [sourceKey, backend, backendRevision, planAnalysisDrafts, dirtyKey, requiredPlans, props.hasUnsavedDrafts]);
  const draftKey = JSON.stringify([planAnalysisDrafts?.revision ?? null, planAnalysisDrafts?.captureKey ?? null,
    draftResolution.captureKey, dirtyKey]);
  const comparisonKey = left && right ? planDiffComparisonKey(sourceKey, draftKey, left, right) : '';
  const canLoad = Boolean(context.projectPath && context.projectRevision && context.projectXml);
  const canCompare = Boolean(currentCatalog && left && right && draftResolution.ok
    && backend.supports('plan.analysis.diff'));
  const live = useRef({ sourceKey, backend, comparisonKey, canCompare });
  live.current = { sourceKey, backend, comparisonKey, canCompare };
  const currentLoad = loadState?.key === sourceKey ? loadState : null;
  const currentComparison = comparison?.key === comparisonKey && canCompare ? comparison : null;
  const result = currentComparison?.status === 'ready' ? currentComparison.result : undefined;
  const visibleRows = result?.rows.filter(row => !differencesOnly || row.differs) ?? [];
  const busy = currentLoad?.status === 'loading' || currentComparison?.status === 'loading';
  const failure = currentLoad?.status === 'error' ? currentLoad.error
    : currentComparison?.status === 'error' ? currentComparison.error : undefined;

  useEffect(() => {
    const gate = catalogGate.current;
    const request = gate.begin(sourceKey);
    const isCurrent = () => live.current.backend === backend && gate.accepts(request, live.current.sourceKey);
    comparisonGate.current.retire();
    setCatalog(null);
    setComparison(null);
    if (!canLoad) { setLoadState(null); return () => gate.retire(); }
    if (!backend.supports('plan.analysis.diffOptions')) {
      setLoadState({ key: sourceKey, status: 'error', error: new Error('当前 C# 服务未发布计划来源目录操作 plan.analysis.diffOptions。') });
      return () => gate.retire();
    }
    setLoadState({ key: sourceKey, status: 'loading' });
    void (async () => {
      try {
        const capture = await capturePlanDiffProject(context);
        if (!isCurrent()) return;
        const raw = await backend.request<unknown>('plan.analysis.diffOptions', { capture }, { signal: request.controller.signal });
        if (!isCurrent()) return;
        const loaded = parsePlanDiffOptions(raw, capture);
        setCatalog({ sourceKey, backend, capture, options: loaded.options });
        setSelection(previous => ({ sourceKey,
          leftID: previous.sourceKey === sourceKey && loaded.options.some(option => option.id === previous.leftID)
            ? previous.leftID : loaded.options[0]?.id ?? '',
          rightID: previous.sourceKey === sourceKey && loaded.options.some(option => option.id === previous.rightID)
            ? previous.rightID : (loaded.options[1] ?? loaded.options[0])?.id ?? '',
        }));
        setLoadState({ key: sourceKey, status: 'ready' });
      } catch (error) {
        if (isCurrent()) setLoadState({ key: sourceKey, status: 'error', error });
      }
    })();
    return () => gate.retire();
  }, [backend, sourceKey, loadVersion, canLoad]);

  useEffect(() => {
    const gate = comparisonGate.current;
    gate.retire();
    if (!canCompare || !currentCatalog || !left || !right) return;
    const request = gate.begin(comparisonKey);
    const isCurrent = () => live.current.backend === backend && live.current.canCompare
      && gate.accepts(request, live.current.comparisonKey);
    setComparison({ key: comparisonKey, status: 'loading' });
    const capture = currentCatalog.capture;
    void (async () => {
      try {
        // The resolver returns exact replayable Plan edits; copying pins them to this request.
        const planDrafts = structuredClone(draftResolution.planDrafts);
        if (!isCurrent()) return;
        const raw = await backend.request<unknown>('plan.analysis.diff', {
          capture, leftOptionID: left.id, rightOptionID: right.id, planDrafts,
        }, { signal: request.controller.signal });
        if (!isCurrent()) return;
        const returned = parsePlanDiffResult(raw, capture, left, right);
        setComparison({ key: comparisonKey, status: 'ready', result: returned, draftCount: planDrafts.length });
      } catch (error) {
        if (isCurrent()) setComparison({ key: comparisonKey, status: 'error', error });
      }
    })();
    return () => gate.retire();
  }, [backend, comparisonKey, canCompare, compareVersion, currentCatalog]);

  const select = (side: 'left' | 'right', id: string) => {
    comparisonGate.current.retire();
    setComparison(null);
    setSelection(current => ({ ...current, sourceKey, [side === 'left' ? 'leftID' : 'rightID']: id }));
  };
  const reload = () => {
    catalogGate.current.retire();
    comparisonGate.current.retire();
    setCatalog(null);
    setComparison(null);
    setLoadVersion(version => version + 1);
  };
  const cancel = () => {
    catalogGate.current.retire();
    comparisonGate.current.retire();
    if (currentLoad?.status === 'loading') setLoadState({ key: sourceKey, status: 'cancelled' });
    setComparison({ key: comparisonKey, status: 'cancelled' });
  };
  const sourcePicker = (side: 'left' | 'right', option: PlanDiffOption | undefined) => (
    <div className="plan-diff__source">
      <label htmlFor={`plan-diff-${side}`}>{side === 'left' ? (zh ? '左侧来源' : 'Left source') : (zh ? '右侧来源' : 'Right source')}</label>
      <select id={`plan-diff-${side}`} data-testid={`plan-diff-${side}`} value={option?.id ?? ''}
        disabled={!currentCatalog || !options.length} onChange={event => select(side, event.target.value)}>
        {!options.length && <option value="">{zh ? '暂无可比较来源' : 'No comparison sources'}</option>}
        {options.map(item => <option key={item.id} value={item.id}>{item.displayName}{item.isResult ? (zh ? ' · 关联 Plan' : ' · associated Plan') : ''}</option>)}
      </select>
      {option && <div className="plan-diff__source-detail">
        <span className="plan-diff__kind">{option.isResult ? (zh ? 'Result → 关联 Plan' : 'Result → associated Plan') : 'Plan'}</span>
        <code title={option.planPath}>{option.planPath}</code>
        <small title={option.planRevision}>{zh ? 'Plan 源修订' : 'Plan source revision'}: {option.planRevision.slice(0, 12)}</small>
        {option.isResult && option.resultPath && <details><summary>{zh ? '查看 Result 来源' : 'Result source'}</summary><code>{option.resultPath}</code></details>}
      </div>}
    </div>
  );

  return <section className="plan-diff" data-testid="plan-diff-view" aria-labelledby="plan-diff-title">
    <header className="plan-diff__heading">
      <div><span className="plan-diff__eyebrow">{zh ? '计划分析 · 只读' : 'PLAN ANALYSIS · READ ONLY'}</span>
        <h2 id="plan-diff-title">{zh ? '计划属性比较' : 'Compare Plan properties'}</h2>
        <p>{zh ? '逐项比较两侧 Plan 的原始属性值，并纳入已捕获的未保存 Plan 编辑。' : 'Compare original Plan property values, including captured unsaved Plan edits.'}</p>
      </div>
      <button type="button" data-testid="plan-diff-reload" onClick={reload} disabled={!canLoad || busy}>{zh ? '重新读取来源' : 'Reload sources'}</button>
    </header>
    <p className="plan-diff__semantics">{zh ? 'Result 项使用其关联 Plan。比较仅在内存中读取草稿；工程和 Plan 文件均不会保存。' : 'A Result choice uses its associated Plan. Comparison reads drafts in memory and saves neither the project nor Plan files.'}</p>
    <div className="plan-diff__sources">{sourcePicker('left', left)}{sourcePicker('right', right)}</div>
    <div className="plan-diff__toolbar">
      <label className="plan-diff__filter"><input type="checkbox" data-testid="plan-diff-only-differences" checked={differencesOnly}
        onChange={event => setDifferencesOnly(event.target.checked)} />{zh ? '仅显示差异' : 'Differences only'}</label>
      <span className="plan-diff__counts" data-testid="plan-diff-counts">{result
        ? (zh ? `${result.rows.length} 项属性 · ${result.rows.filter(row => row.differs).length} 项差异`
          : `${result.rows.length} properties · ${result.rows.filter(row => row.differs).length} differences`) : ''}</span>
      {busy ? <button type="button" data-testid="plan-diff-cancel" onClick={cancel}>{zh ? '取消读取' : 'Cancel read'}</button>
        : <button type="button" data-testid="plan-diff-compare" disabled={!canCompare}
          onClick={() => setCompareVersion(version => version + 1)}>{zh ? '重新比较' : 'Compare again'}</button>}
    </div>
    {!canLoad && <p className="plan-diff__empty" role="status">{zh ? '打开已保存的工程后，可选择 Plan 及其关联 Result 来源。' : 'Open a saved project to choose Plans and associated Results.'}</p>}
    {currentLoad?.status === 'loading' && <p className="plan-diff__status" role="status">{zh ? '正在读取计划来源…' : 'Reading Plan sources…'}</p>}
    {currentLoad?.status === 'ready' && !options.length && <p className="plan-diff__empty" role="status">{zh ? '当前工程没有可比较的 Plan 来源。' : 'This project has no Plan sources to compare.'}</p>}
    {currentCatalog && options.length > 0 && !draftResolution.ok && <div className="plan-diff__blocked" role="alert" data-testid="plan-diff-blocked">
      <strong>{zh ? '当前草稿尚不能安全比较' : 'The current draft cannot be compared safely'}</strong>
      {draftResolution.blockers.map((blocker, index) => <p key={index}>{blocker.message}</p>)}
    </div>}
    {currentCatalog && !backend.supports('plan.analysis.diff') && <p className="plan-diff__blocked" role="alert">{zh ? '当前 C# 服务未发布计划比较操作 plan.analysis.diff。' : 'The C# service has not advertised plan.analysis.diff.'}</p>}
    {currentComparison?.status === 'loading' && <p className="plan-diff__status" role="status" data-testid="plan-diff-status">{zh ? '正在比较当前来源与草稿…' : 'Comparing the current sources and drafts…'}</p>}
    {(currentLoad?.status === 'cancelled' || currentComparison?.status === 'cancelled') && <p className="plan-diff__status" role="status">{zh ? '读取已取消；可重新读取来源或重新比较。' : 'Read cancelled. Reload the sources or compare again.'}</p>}
    {failure !== undefined && <div className="plan-diff__error" role="alert" data-testid="plan-diff-error">
      <p>{message(failure, language)}</p><details><summary>{zh ? '原始诊断' : 'Original diagnostics'}</summary><pre>{diagnosticsText(failure)}</pre></details>
    </div>}
    {result && <>
      <p className="plan-diff__status" role="status" data-testid="plan-diff-status">{currentComparison?.draftCount
        ? (zh ? `已使用 ${currentComparison.draftCount} 份当前未保存 Plan 草稿进行比较。` : `Compared using ${currentComparison.draftCount} current unsaved Plan drafts.`)
        : (zh ? '已比较当前工程关联的已保存 Plan 属性。' : 'Compared saved Plan properties associated with the current project.')}</p>
      <div className="plan-diff__table-wrap" data-testid="plan-diff-result">
        <table aria-label={zh ? '计划属性比较结果' : 'Plan property comparison results'}>
          <thead><tr><th scope="col">{zh ? '属性键（KEY）' : 'KEY'}</th><th scope="col">{result.left.headerName}</th><th scope="col">{result.right.headerName}</th></tr></thead>
          <tbody>{visibleRows.map(row => <tr key={row.key} data-plan-diff-key={row.key} data-differs={row.differs ? 'true' : 'false'}>
            <th scope="row">{row.key}</th><td data-plan-diff-side="left" className={row.differs ? 'is-different' : undefined}>{row.leftValue}</td>
            <td data-plan-diff-side="right" className={row.differs ? 'is-different' : undefined}>{row.rightValue}</td>
          </tr>)}</tbody>
        </table>
        {!visibleRows.length && <p className="plan-diff__empty">{differencesOnly
          ? (zh ? '两侧原始属性值没有差异。' : 'The original values have no differences.')
          : (zh ? '服务未返回可比较的简单属性。' : 'The service returned no comparable simple properties.')}</p>}
      </div>
    </>}
  </section>;
}
