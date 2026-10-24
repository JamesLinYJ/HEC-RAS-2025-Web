import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { MutableRefObject } from 'react';
import type { BackendGateway, Language, WorkbenchContext } from '../../../shared/workbench';
import { errorDiagnostics, message } from '../../../shared/workbench';
import { VectorSymbologyOptionEditor } from './VectorSymbologyOptionEditor';
import { VectorSymbologyPreviewShape } from './VectorSymbologyPreviewShape';
import {
  isCompleteCondition,
  parseVectorSymbologyEvaluation,
  parseVectorSymbologyInspection,
  sameRevision,
  supportedProperties,
  validateSourceFeatureSelection,
  vectorGeometryLabel,
  vectorSymbologyMethods,
} from './vectorSymbologyWorkflow';
import type {
  VectorGeometryType,
  VectorSymbologyCondition,
  VectorSymbologyEvaluation,
  VectorSymbologyEvaluationExpectation,
  VectorSymbologyInspection,
  VectorSymbologyPropertyConditions,
  VectorSymbologyRuleDraft,
} from './vectorSymbologyWorkflow';
import './vectorSymbology.css';

export interface VectorSymbologyEditorProps {
  backend: BackendGateway;
  context: WorkbenchContext;
  language: Language;
  /** Exact GeoJSON path and SHA-256 from the validated persisted vector capture. */
  vectorPath: string;
  revision: string;
  /** Stable map/tree owner supplied by the parent capture. */
  ownerID: string;
  onDirtyStateChange?: (owner: string, dirty: boolean) => void;
}

type Phase = 'loading' | 'ready' | 'failed';
interface RequestToken { id: number; ownerKey: string; controller: AbortController }
interface RequestEnvelope<T> { id: number; ownerKey: string; value: T }
interface PropsSnapshot { ownerKey: string; vectorPath: string; revision: string }
interface InspectionReceipt { ownerKey: string; value: VectorSymbologyInspection }
interface EvaluationDraftReceipt { ownerKey: string; draftKey: string; value: VectorSymbologyEvaluation }

export function VectorSymbologyEditor({ backend, context, language, vectorPath, revision, ownerID, onDirtyStateChange }: VectorSymbologyEditorProps) {
  const zh = language === 'zh';
  const contextKey = JSON.stringify([context.projectSessionID, context.projectPath, context.projectRevision]);
  const ownerKey = JSON.stringify([ownerID, vectorPath, revision, contextKey]);
  const dirtyOwner = `vector.symbology.preview:${ownerID || 'unowned'}`;
  const propsRef = useRef<PropsSnapshot>({ ownerKey, vectorPath, revision });
  propsRef.current = { ownerKey, vectorPath, revision };
  const languageRef = useRef(language);
  languageRef.current = language;

  const [phase, setPhase] = useState<Phase>('loading');
  const [busy, setBusy] = useState(false);
  const [requestLabel, setRequestLabel] = useState('');
  const [error, setError] = useState<unknown | null>(null);
  const [inspectionReceipt, setInspectionReceipt] = useState<InspectionReceipt | null>(null);
  const [geometryType, setGeometryType] = useState<VectorGeometryType>('point');
  const [sourceFeatureIndicesText, setSourceFeatureIndicesText] = useState('');
  const [rules, setRules] = useState<VectorSymbologyRuleDraft[]>([]);
  const [evaluationReceipt, setEvaluationReceipt] = useState<EvaluationDraftReceipt | null>(null);
  const [status, setStatus] = useState('');
  const [inspectionNonce, setInspectionNonce] = useState(0);
  const requestGenerationRef = useRef(0);
  const activeRequestRef = useRef<RequestToken | null>(null);
  const mountedRef = useRef(false);
  const nextDraftIDRef = useRef(0);

  const draftKey = JSON.stringify([geometryType, sourceFeatureIndicesText, rules]);
  const draftKeyRef = useRef(draftKey);
  draftKeyRef.current = draftKey;
  const inspection = inspectionReceipt?.ownerKey === ownerKey ? inspectionReceipt.value : null;
  const evaluation = evaluationReceipt?.ownerKey === ownerKey && evaluationReceipt.draftKey === draftKey
    ? evaluationReceipt.value : null;
  const bucket = inspection?.buckets.find(item => item.geometryType === geometryType) ?? null;

  const isInputReady = Boolean(ownerID.trim() && vectorPath.trim() && isSha256(revision)
    && context.projectSessionID.trim() && context.projectPath?.trim());

  const requestIsCurrent = useCallback((request: RequestToken): boolean => mountedRef.current
    && activeRequestRef.current === request
    && request.id === requestGenerationRef.current
    && propsRef.current.ownerKey === request.ownerKey, []);

  const envelopeIsCurrent = useCallback((envelope: RequestEnvelope<unknown>): boolean => mountedRef.current
    && envelope.id === requestGenerationRef.current
    && propsRef.current.ownerKey === envelope.ownerKey, []);

  const performRequest = useCallback(async <T,>(method: string, parameters: unknown,
    parse: (value: unknown) => T, label: string): Promise<RequestEnvelope<T> | null> => {
    const requestOwnerKey = propsRef.current.ownerKey;
    if (!requestOwnerKey) return null;
    if (!backend.supports(method)) {
      setError(new Error(`C# Host 尚未发布 ${method}。`));
      return null;
    }
    activeRequestRef.current?.controller.abort();
    const request: RequestToken = {
      id: ++requestGenerationRef.current,
      ownerKey: requestOwnerKey,
      controller: new AbortController(),
    };
    activeRequestRef.current = request;
    setBusy(true);
    setRequestLabel(label);
    setError(null);
    setStatus('');
    try {
      const result = await backend.request(method, parameters, { signal: request.controller.signal });
      if (!requestIsCurrent(request)) return null;
      const value = parse(result);
      if (!requestIsCurrent(request)) return null;
      return { id: request.id, ownerKey: request.ownerKey, value };
    } catch (requestError) {
      if (requestIsCurrent(request)) setError(requestError);
      return null;
    } finally {
      if (activeRequestRef.current === request) {
        activeRequestRef.current = null;
        setBusy(false);
        setRequestLabel('');
      }
    }
  }, [backend, requestIsCurrent]);

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
      requestGenerationRef.current++;
      activeRequestRef.current?.controller.abort();
      activeRequestRef.current = null;
    };
  }, []);

  useEffect(() => {
    requestGenerationRef.current++;
    activeRequestRef.current?.controller.abort();
    activeRequestRef.current = null;
    setBusy(false);
    setRequestLabel('');
    setInspectionReceipt(null);
    setEvaluationReceipt(null);
    setRules([]);
    setSourceFeatureIndicesText('');
    setStatus('');
    setError(null);
    if (!isInputReady) {
      setPhase('failed');
      setError(new Error('缺少已验证的 GeoJSON 路径、SHA-256 修订、工程会话或地图归属。'));
      return;
    }
    setPhase('loading');
    let cancelled = false;
    void (async () => {
      const result = await performRequest(vectorSymbologyMethods.inspect,
        { vectorPath, revision }, parseVectorSymbologyInspection,
        languageRef.current === 'zh' ? '读取 GeoJSON 属性表与序号绑定' : 'Inspecting GeoJSON table and ordinal binding');
      if (cancelled || !result || !envelopeIsCurrent(result)) {
        if (!cancelled && propsRef.current.ownerKey === ownerKey && !activeRequestRef.current) setPhase('failed');
        return;
      }
      if (!sameRevision(result.value.revision, revision)) {
        setPhase('failed');
        setError(new Error('C# Host 检查结果的源修订与父级验证的 GeoJSON 修订不一致。'));
        return;
      }
      const initialBucket = result.value.buckets.find(item => item.renderFeatureCount > 0)
        ?? result.value.buckets[0];
      setInspectionReceipt({ ownerKey, value: result.value });
      if (initialBucket) setGeometryType(initialBucket.geometryType);
      setPhase('ready');
      setStatus(languageRef.current === 'zh' ? `已读取 ${result.value.sourceFeatureCount} 个原始 GeoJSON 要素。` : `Read ${result.value.sourceFeatureCount} source GeoJSON features.`);
    })();
    return () => { cancelled = true; };
  }, [ownerKey, vectorPath, revision, isInputReady, performRequest, envelopeIsCurrent, inspectionNonce]);

  const selectedIndices = useMemo(() => {
    if (!bucket) return { values: [] as number[], error: '' };
    try { return { values: validateSourceFeatureSelection(sourceFeatureIndicesText, bucket), error: '' }; }
    catch (selectionError) { return { values: [] as number[], error: selectionError instanceof Error ? selectionError.message : String(selectionError) }; }
  }, [sourceFeatureIndicesText, bucket]);

  const completeRules = useMemo(() => rules.filter(rule => rule.conditions.some(isCompleteCondition)), [rules]);
  const expectedOverrideCount = selectedIndices.values.length * completeRules.length;
  const evaluationBlockedReason = !bucket ? (zh ? '未选择可用几何分组。' : 'No geometry bucket is selected.')
    : !bucket.ordinalBindingProven ? (zh ? 'Host 未证明行序号绑定，无法安全求值。' : 'The Host has not proven ordinal binding.')
      : !bucket.hasAttributeTable ? (zh ? '当前分组没有属性表。' : 'The selected group has no attribute table.')
        : selectedIndices.error || (completeRules.length === 0 ? (zh ? '至少需要一个属性组包含完整条件。' : 'At least one property group needs a complete condition.')
          : expectedOverrideCount > 5_000_000 ? (zh ? '本次输出超过 Host 的 5000000 条覆写限制。' : 'This selection exceeds the Host limit of 5000000 overrides.') : '');
  const canEvaluate = phase === 'ready' && !busy && Boolean(inspection) && !evaluationBlockedReason;
  const evaluationDraftKey = draftKey;

  useEffect(() => { setEvaluationReceipt(null); }, [draftKey]);

  const onGeometryChange = (nextType: VectorGeometryType) => {
    setGeometryType(nextType);
    setSourceFeatureIndicesText('');
    setRules([]);
  };

  const onRuleAdd = (definitionID: string, property: VectorSymbologyRuleDraft['property']) => {
    if (!bucket || !bucket.ordinalBindingProven || !bucket.hasAttributeTable
      || !bucket.supportedDefinitionIDs.includes(definitionID)
      || !supportedProperties(bucket.geometryType).includes(property)
      || rules.some(rule => rule.definitionID === definitionID && rule.property === property)
      || rules.length >= 64) return;
    setRules(current => [...current, {
      id: nextLocalID('property', nextDraftIDRef), definitionID, property, conditions: [],
    }]);
  };

  const onRuleRemove = (ruleID: string) => setRules(current => current.filter(rule => rule.id !== ruleID));
  const onConditionAdd = (ruleID: string) => setRules(current => current.map(rule => rule.id === ruleID && rule.conditions.length < 256
    ? { ...rule, conditions: [...rule.conditions, { id: nextLocalID('condition', nextDraftIDRef), column: '', operator: '==', threshold: '', resultValue: '' }] }
    : rule));
  const onConditionChange = (ruleID: string, conditionID: string, change: Partial<VectorSymbologyCondition>) => setRules(current => current.map(rule => rule.id !== ruleID ? rule : ({
    ...rule,
    conditions: rule.conditions.map(condition => condition.id === conditionID ? { ...condition, ...change } : condition),
  })));
  const onConditionRemove = (ruleID: string, conditionID: string) => setRules(current => current.map(rule => rule.id !== ruleID ? rule : ({
    ...rule,
    conditions: rule.conditions.filter(condition => condition.id !== conditionID),
  })));

  const onEvaluate = async () => {
    if (!bucket || evaluationBlockedReason) return;
    let sourceFeatureIndices: number[];
    try { sourceFeatureIndices = validateSourceFeatureSelection(sourceFeatureIndicesText, bucket); }
    catch (selectionError) { setError(selectionError); return; }
    const properties: VectorSymbologyPropertyConditions[] = completeRules.map(rule => ({
      definitionID: rule.definitionID,
      property: rule.property,
      conditions: rule.conditions.map(condition => ({
        column: condition.column,
        operator: condition.operator,
        threshold: condition.threshold,
        resultValue: condition.resultValue,
      })),
    }));
    if (properties.some(rule => !bucket.supportedDefinitionIDs.includes(rule.definitionID)
      || !supportedProperties(bucket.geometryType).includes(rule.property))) {
      setError(new Error('当前条件草稿包含所选 Host 几何分组不支持的定义或属性。'));
      return;
    }
    const expectation: VectorSymbologyEvaluationExpectation = { revision, sourceFeatureIndices, properties };
    const requestDraftKey = evaluationDraftKey;
    const response = await performRequest(vectorSymbologyMethods.evaluate, {
      vectorPath,
      revision,
      geometryType: bucket.geometryType,
      sourceFeatureIndices,
      properties,
    }, value => parseVectorSymbologyEvaluation(value, expectation),
    zh ? '由 C# Host 求值条件规则' : 'Evaluating conditional rules in the C# Host');
    if (!response || !envelopeIsCurrent(response) || draftKeyRef.current !== requestDraftKey) return;
    if (!sameRevision(response.value.revision, revision)) {
      setError(new Error('C# Host 求值回执的源修订与当前 GeoJSON 修订不一致。'));
      return;
    }
    setEvaluationReceipt({ ownerKey: response.ownerKey, draftKey: requestDraftKey, value: response.value });
    const matched = response.value.overrides.filter(item => item.hasMatch).length;
    setStatus(zh ? `Host 已完成 ${response.value.overrides.length} 项属性求值，其中 ${matched} 项匹配条件。` : `The Host evaluated ${response.value.overrides.length} properties; ${matched} matched a condition.`);
  };

  const dirty = rules.length > 0;
  useEffect(() => {
    onDirtyStateChange?.(dirtyOwner, dirty);
    return () => onDirtyStateChange?.(dirtyOwner, false);
  }, [dirty, dirtyOwner, onDirtyStateChange]);

  const diagnosticText = error ? formatDiagnostics(errorDiagnostics(error)) : '';
  const firstSelectedIndex = selectedIndices.values[0];
  const previewDefinitionIDs = [...new Set(completeRules.map(rule => rule.definitionID))];
  const previewRows = evaluation?.overrides.slice(0, 200) ?? [];

  return (
    <section className="vector-symbology-editor" aria-label={zh ? '矢量条件符号预览' : 'Vector conditional symbology preview'}>
      <header className="vector-symbology-header">
        <div>
          <p className="vector-symbology-eyebrow">{zh ? 'GeoJSON 矢量' : 'GeoJSON vector'}</p>
          <h3>{zh ? '条件符号' : 'Conditional symbology'}</h3>
          <code className="vector-symbology-source-path">{vectorPath}</code>
          <span className="vector-symbology-source-revision">SHA-256: {revision}</span>
        </div>
        <div className="vector-symbology-status" role="status" aria-live="polite">
          {phase === 'loading' ? (requestLabel || (zh ? '正在读取…' : 'Loading…'))
            : phase === 'ready' ? (busy ? requestLabel : (status || (zh ? '已就绪' : 'Ready')))
              : (zh ? '不可用' : 'Unavailable')}
        </div>
      </header>

      <aside className="vector-symbology-limitation" role="note">
        {zh ? '此 Host 仅提供 GeoJSON 条件规则检查与 C# 求值，不写入矢量文件或地图 .map 样式。以下规则是本页临时预览草稿，不会显示为已保存。' : 'This Host provides GeoJSON conditional-rule inspection and C# evaluation only. It does not write vector files or .map styles. Rules below are temporary page drafts, not saved map styling.'}
      </aside>

      {error !== null && <div className="vector-symbology-error" role="alert">
        <p>{message(error, language)}</p>
        <details><summary>{zh ? 'C# Host 诊断' : 'C# Host diagnostics'}</summary><pre>{diagnosticText}</pre></details>
      </div>}

      {phase === 'loading' && <div className="vector-symbology-loading" role="status">{requestLabel || (zh ? '读取矢量属性表与原始要素序号…' : 'Reading vector table and source ordinals…')}</div>}
      {phase === 'ready' && inspection && <>
        <section className="vector-symbology-inspection">
          <div className="vector-symbology-section-title">
            <h4>{zh ? 'Host 检查结果' : 'Host inspection'}</h4>
            <span>{inspection.sourceFeatureCount} {zh ? '个原始要素' : 'source features'}</span>
          </div>
          <p className="vector-symbology-muted">
            {zh ? `源修订已核对：${inspection.revision}` : `Verified source revision: ${inspection.revision}`}
          </p>
          <div className="vector-symbology-bucket-summary">
            {inspection.buckets.map(item => <span key={item.geometryType} className={item.ordinalBindingProven && item.hasAttributeTable ? 'is-proven' : 'is-unproven'}>
              {vectorGeometryLabel(item.geometryType, language)} · {item.sourceFeatureIndices.length} {zh ? '序号' : 'indices'} · {item.columns.length} {zh ? '列' : 'columns'} · {item.ordinalBindingProven ? (zh ? '已绑定' : 'bound') : (zh ? '未证明' : 'unproven')}
            </span>)}
          </div>
          <button type="button" className="vector-symbology-secondary-button" disabled={busy || dirty}
            onClick={() => { setPhase('loading'); setEvaluationReceipt(null); setError(null); setInspectionNonce(value => value + 1); }}>
            {dirty ? (zh ? '先清除本地规则后才能重新检查' : 'Clear local rules before reinspection') : (zh ? '重新检查源文件' : 'Reinspect source')}
          </button>
        </section>

        <section className="vector-symbology-feature-selection">
          <div className="vector-symbology-section-title"><h4>{zh ? '原始 GeoJSON 要素序号' : 'Original GeoJSON feature indices'}</h4></div>
          <label className="vector-symbology-field">
            <span>{zh ? '输入要送给 C# Host 的实际 SourceFeatureIndices' : 'Exact SourceFeatureIndices sent to the C# Host'}</span>
            <textarea rows={3} value={sourceFeatureIndicesText} disabled={busy}
              onChange={event => setSourceFeatureIndicesText(event.currentTarget.value)}
              placeholder={zh ? '例如：0, 4, 12（仅接受 Host 检查返回的原始序号）' : 'For example: 0, 4, 12 (must be returned by Host inspection)'} />
          </label>
          {bucket && <p className={selectedIndices.error ? 'vector-symbology-warning' : 'vector-symbology-muted'}>
            {selectedIndices.error || (zh ? `当前分组返回 ${bucket.sourceFeatureIndices.length} 个原始序号。求值仅接收此列表中的精确序号。` : `${bucket.sourceFeatureIndices.length} original indices are available; evaluation accepts only exact indices from this list.`)}
          </p>}
          {bucket && <details className="vector-symbology-source-indices">
            <summary>{zh ? '查看 Host 返回的序号样例' : 'View Host-returned index sample'}</summary>
            <code>{bucket.sourceFeatureIndices.slice(0, 64).join(', ') || '—'}</code>
            {bucket.sourceFeatureIndices.length > 64 && <span>{zh ? `另有 ${bucket.sourceFeatureIndices.length - 64} 个序号` : `${bucket.sourceFeatureIndices.length - 64} more indices`}</span>}
          </details>}
        </section>

        <VectorSymbologyOptionEditor buckets={inspection.buckets} geometryType={geometryType} rules={rules} language={language}
          disabled={busy} onGeometryChange={onGeometryChange} onRuleAdd={onRuleAdd} onRuleRemove={onRuleRemove}
          onConditionAdd={onConditionAdd} onConditionChange={onConditionChange} onConditionRemove={onConditionRemove} />

        <section className="vector-symbology-actions">
          <div className="vector-symbology-muted">
            {completeRules.length > 0 ? (zh ? `将求值 ${selectedIndices.values.length} 个源要素 × ${completeRules.length} 个完整属性组。` : `Will evaluate ${selectedIndices.values.length} source features × ${completeRules.length} complete property groups.`)
              : (zh ? '添加至少一条完整的属性条件后可求值。' : 'Add at least one complete property condition to evaluate.')}
          </div>
          <button type="button" className="vector-symbology-primary-button" disabled={!canEvaluate} onClick={() => void onEvaluate()}>
            {busy && requestLabel ? requestLabel : (zh ? '由 C# Host 求值预览' : 'Evaluate preview in C# Host')}
          </button>
          <button type="button" className="vector-symbology-secondary-button" disabled={busy || rules.length === 0}
            onClick={() => { setRules([]); setEvaluationReceipt(null); setStatus(zh ? '本地条件草稿已清除。' : 'Local condition drafts cleared.'); }}>
            {zh ? '清除本地规则' : 'Clear local rules'}
          </button>
        </section>
        {evaluationBlockedReason && <p className="vector-symbology-warning">{evaluationBlockedReason}</p>}

        {evaluation && <section className="vector-symbology-evaluation">
          <div className="vector-symbology-section-title">
            <h4>{zh ? 'Host 求值回执' : 'Host evaluation receipt'}</h4>
            <span>{evaluation.overrides.filter(item => item.hasMatch).length} / {evaluation.overrides.length} {zh ? '匹配' : 'matched'}</span>
          </div>
          <p className="vector-symbology-muted">{zh ? `回执源修订：${evaluation.revision}` : `Receipt source revision: ${evaluation.revision}`}</p>
          {firstSelectedIndex !== undefined && previewDefinitionIDs.map(definitionID => <VectorSymbologyPreviewShape key={definitionID}
            geometryType={geometryType} sourceFeatureIndex={firstSelectedIndex} definitionID={definitionID}
            overrides={evaluation.overrides.filter(item => item.sourceFeatureIndex === firstSelectedIndex && item.definitionID === definitionID)}
            language={language} />)}
          <div className="vector-symbology-results-table-wrap">
            <table className="vector-symbology-results-table">
              <thead><tr>
                <th>{zh ? 'SourceFeatureIndex' : 'SourceFeatureIndex'}</th>
                <th>DefinitionID</th><th>Property</th><th>{zh ? '匹配' : 'HasMatch'}</th>
                <th>ColorValue</th><th>NumberValue</th><th>IntegerValue</th>
              </tr></thead>
              <tbody>{previewRows.map((item, index) => <tr key={`${item.sourceFeatureIndex}-${item.definitionID}-${item.property}-${index}`}>
                <td><code>{item.sourceFeatureIndex}</code></td><td><code>{item.definitionID}</code></td><td><code>{item.property}</code></td>
                <td>{item.hasMatch ? (zh ? '是' : 'true') : (zh ? '否' : 'false')}</td>
                <td><code>{item.colorValue ?? '—'}</code></td><td><code>{item.numberValue === null ? '—' : String(item.numberValue)}</code></td>
                <td><code>{item.integerValue === null ? '—' : String(item.integerValue)}</code></td>
              </tr>)}</tbody>
            </table>
            {evaluation.overrides.length > previewRows.length && <p className="vector-symbology-muted">
              {zh ? `表格显示前 ${previewRows.length} 条，共 ${evaluation.overrides.length} 条；回执数据保留完整。` : `Showing ${previewRows.length} of ${evaluation.overrides.length} rows; the full receipt is retained.`}
            </p>}
          </div>
        </section>}
      </>}
    </section>
  );
}

function nextLocalID(kind: string, counter: MutableRefObject<number>): string {
  counter.current++;
  return `${kind}-${counter.current}`;
}

function isSha256(value: string): boolean { return /^[\da-f]{64}$/i.test(value); }

function formatDiagnostics(value: unknown): string {
  try { return JSON.stringify(value, null, 2) ?? String(value); }
  catch { return String(value); }
}
