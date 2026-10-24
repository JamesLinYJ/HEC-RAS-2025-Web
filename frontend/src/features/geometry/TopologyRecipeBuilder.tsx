import { useEffect, useMemo, useRef, useState } from 'react';
import type { Language } from '../../shared/workbench';
import { GeometryError } from './GeometryError';
import { gfield, gt } from './geometryText';
import { geometryDiagnosticMessage } from './geometryErrors';
import type { GeometryRow, GeometryTopologyRecipeSnapshot, GeometryTopologySnapshot } from './geometryTypes';

type RecipeStep = GeometryTopologyRecipeSnapshot['steps'][number];
type RecipeStepSchema = GeometryTopologyRecipeSnapshot['stepSchema'][number];

export interface TopologyRecipeBuilderProps {
  language: Language;
  rows: GeometryRow[];
  snapshot?: GeometryTopologySnapshot;
  canEdit: boolean;
  busy: boolean;
  supported: boolean;
  onLoadRecipe?: (controlPointFIDs: number[], recipeText?: string) => Promise<GeometryTopologyRecipeSnapshot>;
  onApplyRecipe?: (controlPointFIDs: number[], recipeText: string, forceSet: boolean,
    expected: { revision: string; draftState: string; draftIdentity: string }) => Promise<void>;
}

const palette = ['OptSkew', 'OptTopology', 'Orthogonalize', 'RemoveFaces', 'RemoveNodes', 'Smooth', 'SmoothAngles', 'Split', 'SwapEdges'];

function serializeSteps(steps: RecipeStep[]): string {
  return steps.map((step) => {
    const parameters = Object.entries(step.parameters).map(([name, value]) => `${name}=${value}`).join(', ');
    return `${step.name}(${parameters})`;
  }).join('\n');
}

function stepFromSchema(schema: RecipeStepSchema): RecipeStep {
  return { name: schema.name, parameters: Object.fromEntries(schema.parameters.map((parameter) => [parameter.name, parameter.defaultValue])) };
}

function localStepLabel(language: Language, name: string): string {
  if (language !== 'zh') return name;
  const names: Record<string, string> = {
    OptSkew: '优化偏斜', OptTopology: '优化拓扑', Orthogonalize: '正交化', RemoveFaces: '移除面',
    RemoveNodes: '移除节点', Smooth: '平滑', SmoothAngles: '平滑角度', Split: '分割', SwapEdges: '交换边', Default: '默认流程',
  };
  return names[name] ?? name;
}

function parameterLabel(language: Language, name: string): string {
  if (language !== 'zh') return name;
  const label = gfield(language, name, name);
  return label === '字段' ? name : label;
}

export function TopologyRecipeBuilder({ language, rows, snapshot, canEdit, busy, supported, onLoadRecipe, onApplyRecipe }: TopologyRecipeBuilderProps) {
  const selectedRows = rows.filter((row) => row.fid != null && row.fid >= 0);
  const selectedFIDs = selectedRows.map((row) => row.fid!);
  const selectionKey = selectedFIDs.join(',');
  const hasOnlySourceSelection = rows.length > 0 && selectedRows.length === rows.length && selectedFIDs.length > 0;
  const [recipe, setRecipe] = useState<GeometryTopologyRecipeSnapshot>();
  const [rawText, setRawText] = useState('');
  const [steps, setSteps] = useState<RecipeStep[]>([]);
  const [mode, setMode] = useState<'builder' | 'text'>('builder');
  const [loading, setLoading] = useState(false);
  const [applying, setApplying] = useState(false);
  const [error, setError] = useState('');
  const [validatedText, setValidatedText] = useState<string>();
  const [recipeDraftState, setRecipeDraftState] = useState<string>();
  const requestSequence = useRef(0);
  const loadRef = useRef(onLoadRecipe);
  loadRef.current = onLoadRecipe;
  const applyRef = useRef(onApplyRecipe);
  applyRef.current = onApplyRecipe;

  const schemaByName = useMemo(() => new Map((recipe?.stepSchema ?? []).map((item) => [item.name, item])), [recipe?.stepSchema]);
  const recipeMatchesSelection = !!recipe && recipe.controlPointFIDs.length === selectedFIDs.length
    && recipe.controlPointFIDs.every((fid, index) => fid === selectedFIDs[index]);
  const addableSteps = useMemo(() => palette.flatMap((name) => {
    const schema = schemaByName.get(name);
    return schema ? [{ name, schema }] : [];
  }), [schemaByName]);

  useEffect(() => {
    const sequence = ++requestSequence.current;
    let cancelled = false;
    if (!supported || !hasOnlySourceSelection || !loadRef.current) {
      setRecipe(undefined); setRecipeDraftState(undefined); setRawText(''); setSteps([]); setValidatedText(undefined); setError(''); setLoading(false);
      return () => { cancelled = true; };
    }
    setLoading(true); setError(''); setValidatedText(undefined);
    const capturedDraftState = snapshot?.draftState;
    void loadRef.current(selectedFIDs).then((result) => {
      if (cancelled || sequence !== requestSequence.current) return;
      setRecipe(result); setRecipeDraftState(capturedDraftState); setRawText(result.rawText); setSteps(result.steps); setValidatedText(result.rawText);
    }).catch((reason: unknown) => {
      if (!cancelled && sequence === requestSequence.current) setError(geometryDiagnosticMessage(reason));
    }).finally(() => {
      if (!cancelled && sequence === requestSequence.current) setLoading(false);
    });
    return () => { cancelled = true; };
  }, [hasOnlySourceSelection, selectionKey, snapshot?.draftState, snapshot?.revision, supported]);

  const inspect = async (text: string, adoptSteps: boolean): Promise<void> => {
    if (!loadRef.current || !hasOnlySourceSelection || !supported) return;
    const sequence = ++requestSequence.current;
    const capturedDraftState = snapshot?.draftState;
    setLoading(true); setError(''); setValidatedText(undefined);
    try {
      const result = await loadRef.current(selectedFIDs, text);
      if (sequence !== requestSequence.current) return;
      setRecipe(result); setRecipeDraftState(capturedDraftState); setValidatedText(text);
      if (adoptSteps && result.isValid) setSteps(result.steps);
    } catch (reason) {
      if (sequence === requestSequence.current) setError(geometryDiagnosticMessage(reason));
    } finally {
      if (sequence === requestSequence.current) setLoading(false);
    }
  };

  const updateSteps = (next: RecipeStep[]): void => {
    setSteps(next); setRawText(serializeSteps(next)); setValidatedText(undefined); setError('');
  };
  const apply = async (forceSet: boolean): Promise<void> => {
    if (!applyRef.current || !hasOnlySourceSelection || !recipe?.isValid || !recipeMatchesSelection || validatedText !== rawText || !canEdit || busy || applying
      || !snapshot || recipe.revision !== snapshot.revision || recipeDraftState !== snapshot.draftState
      || (snapshot.completion?.draftIdentity && recipe.draftIdentity !== snapshot.completion.draftIdentity)) return;
    setApplying(true); setError('');
    try { await applyRef.current(selectedFIDs, rawText, forceSet, { revision: recipe.revision, draftState: recipeDraftState!, draftIdentity: recipe.draftIdentity }); }
    catch (reason) { setError(geometryDiagnosticMessage(reason)); }
    finally { setApplying(false); }
  };

  const canApply = !!recipe?.isValid && recipeMatchesSelection && recipe.rawText === rawText && validatedText === rawText && hasOnlySourceSelection
    && !!snapshot && recipe.revision === snapshot.revision && recipeDraftState === snapshot.draftState
    && (!snapshot.completion?.draftIdentity || recipe.draftIdentity === snapshot.completion.draftIdentity);
  const parseIssue = recipe?.rawText === rawText && !recipe.isValid ? recipe.parseIssue : undefined;
  const patchTypes = recipe?.selectedPatchTypes?.length ? [...new Set(recipe.selectedPatchTypes)].join(' · ') : '';

  return (
    <section className="geometry-panel" aria-label={language === 'zh' ? '拓扑 Recipe 构建器' : 'Topology recipe builder'}>
      <header className="geometry-panel-heading"><div>
        <h3>{language === 'zh' ? '拓扑 Recipe 构建器' : 'Topology recipe builder'}</h3>
        <p>{`${selectedFIDs.length} ${language === 'zh' ? '个源控制点' : 'source control points'}${patchTypes ? ` · ${patchTypes}` : ''}`}</p>
      </div>
        <div className="geometry-toolbar-group">
          <button className="geometry-secondary" type="button" aria-pressed={mode === 'builder'} onClick={() => setMode('builder')}>{language === 'zh' ? '步骤构建' : 'Build steps'}</button>
          <button className="geometry-secondary" type="button" aria-pressed={mode === 'text'} onClick={() => setMode('text')}>{language === 'zh' ? 'Recipe 文本' : 'Recipe text'}</button>
        </div>
      </header>
      {!supported ? <div className="geometry-empty"><strong>{gt(language, 'unavailable')}</strong></div> : !hasOnlySourceSelection ? (
        <div className="geometry-empty"><strong>{language === 'zh' ? '选择一个或多个源控制点后编辑 Recipe。' : 'Select one or more source control points to edit a recipe.'}</strong></div>
      ) : (
        <>
          <div className="geometry-panel-body geometry-toolbar-group">
            <button className="geometry-secondary" disabled={loading || busy} onClick={() => void inspect(rawText, true)}>{language === 'zh' ? '检查并解析' : 'Inspect and parse'}</button>
            <button className="geometry-secondary" disabled={loading || busy} onClick={() => { setRawText('Default()'); setSteps([]); setValidatedText(undefined); setError(''); void inspect('Default()', true); }}>{language === 'zh' ? '恢复 Default()' : 'Set Default()'}</button>
            <span className="geometry-inline-help">{loading ? (language === 'zh' ? '正在读取 Recipe 契约…' : 'Loading recipe contract…') : recipe?.hasMismatch ? (language === 'zh' ? '所选控制点的 Recipe 不一致。' : 'Selected control points have mixed recipes.') : ''}</span>
          </div>
          {mode === 'builder' ? (
            <div className="geometry-recipe-builder">
              <div className="geometry-toolbar-group geometry-recipe-add">
                <label className="geometry-form-field">{language === 'zh' ? '添加步骤' : 'Add step'}
                  <select value="" disabled={loading || !addableSteps.length} onChange={(event) => {
                    const entry = addableSteps.find((candidate) => candidate.name === event.target.value);
                    if (entry) updateSteps([...steps, stepFromSchema(entry.schema)]);
                  }}>
                    <option value="">{language === 'zh' ? '选择步骤…' : 'Choose a step…'}</option>
                    {addableSteps.map(({ name }) => <option value={name} key={name}>{localStepLabel(language, name)}</option>)}
                  </select>
                </label>
              </div>
              {!steps.length ? <div className="geometry-empty"><strong>{language === 'zh' ? '当前 Recipe 没有可编辑步骤；可添加步骤或使用 Default()。' : 'There are no editable steps. Add a step or use Default().'}</strong></div> : (
                <ol className="geometry-recipe-steps">
                  {steps.map((step, index) => {
                    const schema = schemaByName.get(step.name);
                    return <li className="geometry-recipe-step" key={`${index}-${step.name}`}>
                      <header><strong>{index + 1}. {localStepLabel(language, step.name)}</strong><div className="geometry-toolbar-group">
                        <button className="geometry-secondary" type="button" disabled={index === 0 || loading} onClick={() => { const next = [...steps]; [next[index - 1], next[index]] = [next[index], next[index - 1]]; updateSteps(next); }}>{language === 'zh' ? '上移' : 'Move up'}</button>
                        <button className="geometry-secondary" type="button" disabled={index === steps.length - 1 || loading} onClick={() => { const next = [...steps]; [next[index + 1], next[index]] = [next[index], next[index + 1]]; updateSteps(next); }}>{language === 'zh' ? '下移' : 'Move down'}</button>
                        <button className="geometry-secondary" type="button" disabled={loading} onClick={() => updateSteps(steps.filter((_, itemIndex) => itemIndex !== index))}>{language === 'zh' ? '移除' : 'Remove'}</button>
                      </div></header>
                      {!schema ? <div className="geometry-inline-help">{language === 'zh' ? `当前契约没有步骤“${step.name}”的参数定义。` : `The current contract has no parameter schema for “${step.name}”.`}</div> : schema.parameters.length > 0 ? (
                        <div className="geometry-recipe-parameters">{schema.parameters.map((parameter) => {
                          const value = step.parameters[parameter.name] ?? parameter.defaultValue;
                          const bool = parameter.sourceType.toLowerCase() === 'bool' || parameter.sourceType.toLowerCase() === 'boolean';
                          return <label className="geometry-form-field" key={parameter.name}>{parameterLabel(language, parameter.name)}
                            {bool ? <select value={value.toLowerCase() === 'true' ? 'true' : 'false'} onChange={(event) => {
                              const next = [...steps]; next[index] = { ...step, parameters: { ...step.parameters, [parameter.name]: event.target.value } }; updateSteps(next);
                            }}><option value="true">{gt(language, 'yes')}</option><option value="false">{gt(language, 'no')}</option></select> :
                              <input type="number" step="any" value={value} onChange={(event) => {
                                const next = [...steps]; next[index] = { ...step, parameters: { ...step.parameters, [parameter.name]: event.target.value } }; updateSteps(next);
                              }} />}
                          </label>;
                        })}</div>
                      ) : <div className="geometry-inline-help">{language === 'zh' ? '此步骤没有参数。' : 'This step has no parameters.'}</div>}
                    </li>;
                  })}
                </ol>
              )}
            </div>
          ) : (
            <label className="geometry-detail-field geometry-recipe-raw">{language === 'zh' ? 'Recipe 原文' : 'Raw recipe'}
              <textarea value={rawText} onChange={(event) => { setRawText(event.target.value); setValidatedText(undefined); setError(''); }} spellCheck={false} />
            </label>
          )}
          {parseIssue && <div className="geometry-validation-item geometry-recipe-parse-error"><strong>{language === 'zh' ? '解析诊断' : 'Parse diagnostic'}</strong><pre>{parseIssue}</pre></div>}
          {error && <GeometryError language={language} error={error} style={{ margin: 12 }} />}
          <div className="geometry-recipe-apply">
            <details><summary>{language === 'zh' ? '查看待应用的 Recipe 文本' : 'Preview recipe text to apply'}</summary><pre>{rawText || '—'}</pre></details>
            {recipe?.isValid && validatedText === rawText ? <div className="geometry-ok">{language === 'zh' ? '当前文本通过服务端 Recipe 解析。' : 'The current text passed the server recipe parser.'}</div> : null}
            {recipe?.hasMismatch ? <button className="geometry-primary" disabled={!canApply || !canEdit || busy || applying} onClick={() => void apply(true)}>{applying ? gt(language, 'operationRunning') : (language === 'zh' ? '强制应用到所选控制点' : 'Force apply to selected control points')}</button> : <button className="geometry-primary" disabled={!canApply || !canEdit || busy || applying} onClick={() => void apply(false)}>{applying ? gt(language, 'operationRunning') : (language === 'zh' ? '应用 Recipe 草稿' : 'Apply recipe draft')}</button>}
          </div>
        </>
      )}
      <div className="geometry-inline-help">{language === 'zh' ? 'Recipe 参数、有效性和解析诊断由 topology.recipe 服务读取；应用写入现有拓扑草稿并可撤销。' : 'Parameters, validity, and parse diagnostics come from topology.recipe. Apply writes to the existing undoable topology draft.'}</div>
    </section>
  );
}

export default TopologyRecipeBuilder;
