import { useEffect, useMemo, useRef, useState } from 'react';
import type { Language } from '../../shared/workbench';
import { GeometryError } from './GeometryError';
import { IngredientCard, type RecipeIngredient } from './IngredientCard';
import { RecipeStepCard, type RecipeStepDraft } from './RecipeStepCard';
import { gt } from './geometryText';
import { geometryDiagnosticMessage } from './geometryErrors';
import type { GeometryRow, GeometryTopologyRecipeSnapshot, GeometryTopologySnapshot } from './geometryTypes';

export interface RecipeBuilderProps {
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

type RecipeStep = GeometryTopologyRecipeSnapshot['steps'][number];
type RecipeStepSchema = GeometryTopologyRecipeSnapshot['stepSchema'][number];

// These are the source RecipeBuilder's ingredient cards. Other steps remain inspectable/editable
// when present in a recipe, but the source UI did not offer them in its add palette.
const sourceIngredientNames = ['OptSkew', 'OptTopology', 'Orthogonalize', 'RemoveFaces', 'RemoveNodes', 'Smooth', 'SmoothAngles', 'Split', 'SwapEdges'];
const sourceParameterOrder: Record<string, string[]> = {
  SwapEdges: [], Smooth: ['count', 'scaled', 'sliding'], ForceDisplacement: ['count'],
  Orthogonalize: ['count', 'scaler', 'sliding', 'method', 'smoothmethod', 'alpha'], SmoothAngles: ['count'],
  Distort: ['jitter', 'count', 'nudgeconvex'], RemoveFaces: ['threshold'], RemoveNodes: ['threshold'],
  OptTopology: ['threshold', 'deltacost', 'merge', 'collapse', 'perimeter', 'skew', 'cellarea', 'facesmooth', 'facelength'],
  OptSkew: ['count'], Split: ['threshold'], Default: [],
};

function sourceParameters(stepName: string, parameters: Record<string, string>): Record<string, string> {
  const visible = sourceParameterOrder[stepName];
  if (!visible) return { ...parameters };
  const byNormalizedName = new Map(Object.entries(parameters).map(([name, value]) => [name.toLocaleLowerCase(), [name, value] as const]));
  const entries = visible.flatMap((name): Array<readonly [string, string]> => {
    const entry = byNormalizedName.get(name);
    return entry ? [entry] : [];
  });
  return Object.fromEntries(entries);
}

function sourceStepSchema(schema: RecipeStepSchema | undefined): RecipeStepSchema | undefined {
  if (!schema) return undefined;
  const visible = sourceParameterOrder[schema.name];
  if (!visible) return schema;
  const byNormalizedName = new Map(schema.parameters.map((parameter) => [parameter.name.toLocaleLowerCase(), parameter]));
  return { ...schema, parameters: visible.flatMap((name) => {
    const parameter = byNormalizedName.get(name);
    return parameter ? [parameter] : [];
  }) };
}

export function serializeSteps(steps: RecipeStepDraft[]): string {
  return steps.map(({ name, parameters }) => {
    const orderedNames = sourceParameterOrder[name];
    const orderedParameters = orderedNames
      ? [
        ...orderedNames.flatMap((parameterName) => {
          const actualName = Object.keys(parameters).find((key) => key.toLocaleLowerCase() === parameterName);
          if (!actualName) return [];
          const serializedName = name === 'Orthogonalize' && parameterName === 'smoothmethod' ? 'smoothMethod' : actualName;
          return [[serializedName, parameters[actualName]] as const];
        }),
        // The original form exposes only a subset of the Host's parameters.
        // Retain every existing hidden value when editing or reordering a step.
        ...Object.entries(parameters).filter(([key]) => !orderedNames.includes(key.toLocaleLowerCase())),
      ]
      : Object.entries(parameters);
    const serializedParameters = orderedParameters.map(([parameterName, value]) => `${parameterName}=${value}`).join(', ');
    return `${name}(${serializedParameters})`;
  }).join('\n');
}

function stepFromSchema(schema: RecipeStepSchema, id: string): RecipeStepDraft {
  return { id, name: schema.name, parameters: sourceParameters(schema.name, Object.fromEntries(schema.parameters.map((parameter) => [parameter.name, parameter.defaultValue]))) };
}

export function stepFromSource(step: RecipeStep, id: string): RecipeStepDraft {
  return { id, name: step.name, parameters: { ...step.parameters } };
}

function draftsFromSource(steps: RecipeStep[], sequence: { current: number }): RecipeStepDraft[] {
  return steps.map((step) => stepFromSource(step, `recipe-step-${++sequence.current}`));
}

function stepLabel(language: Language, name: string): string {
  if (language !== 'zh') return name;
  const names: Record<string, string> = {
    OptSkew: '优化偏斜', OptTopology: '优化拓扑', Orthogonalize: '正交化', RemoveFaces: '移除面',
    RemoveNodes: '移除节点', Smooth: '平滑', SmoothAngles: '平滑角度', Split: '分割', SwapEdges: '交换边',
    Distort: '扰动网格', ForceDisplacement: '强制位移', Default: '默认流程',
  };
  return names[name] ?? name;
}

function selectedFIDsOf(rows: GeometryRow[]): number[] {
  return rows.flatMap((row) => row.fid != null && Number.isInteger(row.fid) && row.fid >= 0 ? [row.fid] : []);
}

/** C# RecipeBuilder stage, backed by topology.recipe inspection and topology.edit/applyRecipe. */
export function RecipeBuilder({ language, rows, snapshot, canEdit, busy, supported, onLoadRecipe, onApplyRecipe }: RecipeBuilderProps) {
  const snapshotRevision = snapshot?.revision;
  const snapshotDraftState = snapshot?.draftState;
  const snapshotDraftIdentity = snapshot?.completion?.draftIdentity;
  const selectedFIDs = selectedFIDsOf(rows);
  const selectionKey = selectedFIDs.join(',');
  const hasOnlySourceSelection = rows.length > 0 && selectedFIDs.length === rows.length
    && new Set(selectedFIDs).size === selectedFIDs.length;
  const [recipe, setRecipe] = useState<GeometryTopologyRecipeSnapshot>();
  const [rawText, setRawText] = useState('');
  const [steps, setSteps] = useState<RecipeStepDraft[]>([]);
  const [selectedStepID, setSelectedStepID] = useState<string>();
  const [mode, setMode] = useState<'builder' | 'text'>('builder');
  const [loading, setLoading] = useState(false);
  const [applying, setApplying] = useState(false);
  const [error, setError] = useState('');
  const [validatedText, setValidatedText] = useState<string>();
  const [lastValidText, setLastValidText] = useState<string>();
  const [recipeDraftState, setRecipeDraftState] = useState<string>();
  const requestSequence = useRef(0);
  const nextStepID = useRef(0);
  const loadRef = useRef(onLoadRecipe);
  loadRef.current = onLoadRecipe;
  const applyRef = useRef(onApplyRecipe);
  applyRef.current = onApplyRecipe;

  const schemaByName = useMemo(() => new Map((recipe?.stepSchema ?? []).map((item) => [item.name, item])), [recipe?.stepSchema]);
  const schemaForStep = (name: string): RecipeStepSchema | undefined => sourceStepSchema(schemaByName.get(name));
  const recipeMatchesSelection = !!recipe && recipe.controlPointFIDs.length === selectedFIDs.length
    && recipe.controlPointFIDs.every((fid, index) => fid === selectedFIDs[index]);
  const addableIngredients: RecipeIngredient[] = sourceIngredientNames.flatMap((name) => {
    const schema = schemaForStep(name);
    return schema ? [schema] : [];
  });

  useEffect(() => {
    const sequence = ++requestSequence.current;
    let cancelled = false;
    if (!supported || !hasOnlySourceSelection || !loadRef.current) {
      setRecipe(undefined); setRecipeDraftState(undefined); setRawText(''); setSteps([]); setSelectedStepID(undefined);
      setValidatedText(undefined); setLastValidText(undefined); setError(''); setLoading(false);
      return () => { cancelled = true; };
    }
    setLoading(true); setError(''); setValidatedText(undefined);
    const capturedDraftState = snapshotDraftState;
    void loadRef.current(selectedFIDs).then((result) => {
      if (cancelled || sequence !== requestSequence.current) return;
      const nextSteps = draftsFromSource(result.steps, nextStepID);
      setRecipe(result); setRecipeDraftState(capturedDraftState); setRawText(result.rawText); setSteps(nextSteps);
      setSelectedStepID(nextSteps[0]?.id); setValidatedText(result.rawText);
      setLastValidText(result.isValid ? result.rawText : undefined);
    }).catch((reason: unknown) => {
      if (!cancelled && sequence === requestSequence.current) setError(geometryDiagnosticMessage(reason));
    }).finally(() => {
      if (!cancelled && sequence === requestSequence.current) setLoading(false);
    });
    return () => { cancelled = true; };
  }, [hasOnlySourceSelection, selectionKey, snapshotDraftIdentity, snapshotDraftState, snapshotRevision, supported]);

  const inspect = async (text: string, adoptSteps: boolean): Promise<void> => {
    if (!loadRef.current || !hasOnlySourceSelection || !supported) return;
    const sequence = ++requestSequence.current;
    const capturedDraftState = snapshotDraftState;
    setLoading(true); setError(''); setValidatedText(undefined);
    try {
      const result = await loadRef.current(selectedFIDs, text);
      if (sequence !== requestSequence.current) return;
      setRecipe(result); setRecipeDraftState(capturedDraftState); setValidatedText(text);
      if (result.isValid) setLastValidText(text);
      if (adoptSteps && result.isValid) {
        const nextSteps = draftsFromSource(result.steps, nextStepID);
        setSteps(nextSteps); setSelectedStepID(nextSteps[0]?.id);
      }
    } catch (reason) {
      if (sequence === requestSequence.current) setError(geometryDiagnosticMessage(reason));
    } finally {
      if (sequence === requestSequence.current) setLoading(false);
    }
  };

  const updateSteps = (next: RecipeStepDraft[]): void => {
    setSteps(next); setRawText(serializeSteps(next)); setValidatedText(undefined); setError('');
  };
  const addIngredient = (ingredient: RecipeIngredient): void => {
    const step = stepFromSchema(ingredient, `recipe-step-${++nextStepID.current}`);
    setSelectedStepID(step.id); updateSteps([...steps, step]);
  };
  const changeStepParameters = (stepID: string, parameters: Record<string, string>): void => {
    updateSteps(steps.map((step) => step.id === stepID ? { ...step, parameters } : step));
  };
  const moveStep = (stepID: string, direction: -1 | 1): void => {
    const index = steps.findIndex((step) => step.id === stepID);
    const target = index + direction;
    if (index < 0 || target < 0 || target >= steps.length) return;
    const next = [...steps]; [next[index], next[target]] = [next[target], next[index]]; updateSteps(next);
  };
  const removeStep = (stepID: string): void => {
    const index = steps.findIndex((step) => step.id === stepID);
    const next = steps.filter((step) => step.id !== stepID);
    if (selectedStepID === stepID) setSelectedStepID(next[Math.min(index, next.length - 1)]?.id);
    updateSteps(next);
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
  const recipeIdentityMatches = !!recipe && !!snapshot && recipe.revision === snapshot.revision && recipeDraftState === snapshot.draftState
    && (!snapshot.completion?.draftIdentity || recipe.draftIdentity === snapshot.completion.draftIdentity);

  return <section className="geometry-panel" aria-label={language === 'zh' ? '拓扑 Recipe 构建器' : 'Topology recipe builder'}>
    <header className="geometry-panel-heading"><div>
      <h3>{language === 'zh' ? '拓扑 Recipe 构建器' : 'Topology recipe builder'}</h3>
      <p>{`${selectedFIDs.length} ${language === 'zh' ? '个源控制点' : 'source control points'}${patchTypes ? ` · ${patchTypes}` : ''}`}</p>
    </div><div className="geometry-toolbar-group">
      <button className="geometry-secondary" type="button" aria-pressed={mode === 'builder'} onClick={() => setMode('builder')}>{language === 'zh' ? '步骤构建' : 'Build steps'}</button>
      <button className="geometry-secondary" type="button" aria-pressed={mode === 'text'} onClick={() => setMode('text')}>{language === 'zh' ? 'Recipe 文本' : 'Recipe text'}</button>
    </div></header>
    {!supported ? <div className="geometry-empty"><strong>{gt(language, 'unavailable')}</strong></div> : !hasOnlySourceSelection ? (
      <div className="geometry-empty"><strong>{language === 'zh' ? '选择一个或多个具有源 FID 的控制点后编辑 Recipe。' : 'Select one or more control points with source FIDs to edit a recipe.'}</strong></div>
    ) : <>
      <div className="geometry-panel-body geometry-toolbar-group geometry-recipe-source">
        <span>{language === 'zh' ? `控制点 FID：${selectedFIDs.join('、')}` : `Control-point FIDs: ${selectedFIDs.join(', ')}`}</span>
        <span>{language === 'zh' ? `源修订：${recipe?.revision ?? snapshot?.revision ?? '—'}` : `Source revision: ${recipe?.revision ?? snapshot?.revision ?? '—'}`}</span>
        <span className={recipeIdentityMatches ? 'geometry-ok' : 'geometry-inline-help'}>{recipeIdentityMatches ? (language === 'zh' ? '已绑定当前拓扑草稿' : 'Bound to the current topology draft') : (language === 'zh' ? '检查结果与当前草稿不一致' : 'Inspection no longer matches the current draft')}</span>
        {recipe ? <details><summary>{language === 'zh' ? '查看草稿身份' : 'View draft identity'}</summary><code>{recipe.draftIdentity}</code></details> : null}
      </div>
      <div className="geometry-panel-body geometry-toolbar-group">
        <button className="geometry-secondary" disabled={loading || busy} onClick={() => void inspect(rawText, true)}>{language === 'zh' ? '检查并解析' : 'Inspect and parse'}</button>
        <button className="geometry-secondary" disabled={loading || busy} onClick={() => { setRawText('Default()'); setSteps([]); setSelectedStepID(undefined); setValidatedText(undefined); setError(''); void inspect('Default()', true); }}>{language === 'zh' ? '恢复 Default()' : 'Set Default()'}</button>
        {recipe?.rawText === rawText && !recipe.isValid && lastValidText !== undefined ? <button className="geometry-secondary" disabled={loading || busy}
          onClick={() => { setRawText(lastValidText); void inspect(lastValidText, true); }}>{language === 'zh' ? '恢复上次有效文本' : 'Restore last valid text'}</button> : null}
        <span className="geometry-inline-help">{loading ? (language === 'zh' ? '正在读取 Recipe 契约…' : 'Loading recipe contract…') : recipe?.hasMismatch ? (language === 'zh' ? '所选控制点的 Recipe 不一致。' : 'Selected control points have mixed recipes.') : ''}</span>
      </div>
      {mode === 'builder' ? <div className="geometry-recipe-layout">
        <section className="geometry-recipe-catalog" aria-label={language === 'zh' ? '可添加的 Recipe 步骤' : 'Available Recipe steps'}>
          <header><strong>{language === 'zh' ? '可用步骤' : 'Available steps'}</strong><span>{addableIngredients.length}</span></header>
          {addableIngredients.length ? addableIngredients.map((ingredient) => <IngredientCard key={ingredient.name} language={language} ingredient={ingredient}
            label={stepLabel(language, ingredient.name)} disabled={loading || busy || applying} onAdd={addIngredient} />) :
            <div className="geometry-empty"><strong>{loading ? (language === 'zh' ? '正在读取步骤定义…' : 'Loading step definitions…') : (language === 'zh' ? 'Host 未提供可添加的源步骤定义。' : 'The Host did not provide source step definitions.')}</strong></div>}
        </section>
        <section className="geometry-recipe-stage" aria-label={language === 'zh' ? 'Recipe 步骤顺序' : 'Recipe step order'}>
          <header><strong>{language === 'zh' ? '当前步骤' : 'Current steps'}</strong><span>{steps.length}</span></header>
          {!steps.length ? <div className="geometry-empty"><strong>{language === 'zh' ? '当前 Recipe 没有可编辑步骤；可从来源列表添加或使用 Default()。' : 'The recipe has no steps; add one from the source list or use Default().'}</strong></div> : (
            <ol className="geometry-recipe-steps">
              {steps.map((step, index) => <RecipeStepCard key={step.id} language={language} step={step} schema={schemaForStep(step.name)} label={stepLabel(language, step.name)} index={index} stepCount={steps.length}
                selected={step.id === selectedStepID} disabled={loading || busy || applying} onSelect={() => setSelectedStepID(step.id)}
                onParametersChange={(parameters) => changeStepParameters(step.id, parameters)} onMove={(direction) => moveStep(step.id, direction)} onRemove={() => removeStep(step.id)} />)}
            </ol>
          )}
        </section>
      </div> : <label className="geometry-detail-field geometry-recipe-raw">{language === 'zh' ? 'Recipe 原文' : 'Raw recipe'}
        <textarea aria-label={language === 'zh' ? 'Recipe 原文' : 'Raw recipe'} value={rawText} onChange={(event) => { setRawText(event.target.value); setValidatedText(undefined); setError(''); }} spellCheck={false} />
      </label>}
      {parseIssue ? <div className="geometry-validation-item geometry-recipe-parse-error"><strong>{language === 'zh' ? '解析诊断' : 'Parse diagnostic'}</strong><pre>{parseIssue}</pre></div> : null}
      {error ? <GeometryError language={language} error={error} style={{ margin: 12 }} /> : null}
      <div className="geometry-recipe-apply">
        <details><summary>{language === 'zh' ? '查看待应用的 Recipe 文本' : 'Preview recipe text to apply'}</summary><pre>{rawText || '—'}</pre></details>
        {recipe?.isValid && validatedText === rawText ? <div className="geometry-ok">{language === 'zh' ? '当前文本通过服务端 Recipe 解析。' : 'The current text passed the server recipe parser.'}</div> : null}
        {recipe?.hasMismatch ? <button className="geometry-primary" disabled={!canApply || !canEdit || busy || applying} onClick={() => void apply(true)}>{applying ? gt(language, 'operationRunning') : (language === 'zh' ? '强制应用到所选控制点' : 'Force apply to selected control points')}</button> :
          <button className="geometry-primary" disabled={!canApply || !canEdit || busy || applying} onClick={() => void apply(false)}>{applying ? gt(language, 'operationRunning') : (language === 'zh' ? '应用 Recipe 草稿' : 'Apply recipe draft')}</button>}
      </div>
    </>}
    <div className="geometry-inline-help">{language === 'zh' ? '步骤定义、参数类型、默认值、有效性和诊断来自 topology.recipe；应用写入选中控制点的 topology.edit 草稿，可通过几何工作区撤销并由父级保存。' : 'Step schemas, parameter types, defaults, validity, and diagnostics come from topology.recipe. Apply writes to the selected control-point topology.edit draft for parent save and workspace undo.'}</div>
  </section>;
}

export default RecipeBuilder;
