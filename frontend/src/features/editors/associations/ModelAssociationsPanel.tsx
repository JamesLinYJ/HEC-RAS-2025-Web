import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { AssetReference, FeaturePageProps, WorkbenchContext } from '../../../shared/workbench';
import { errorDiagnostics, message, text } from '../../../shared/workbench';
import { DetailedPlanHeader } from './DetailedPlanHeader';
import { GeometryAssociationsPicker } from './GeometryAssociationsPicker';
import { PlanAssociationsPicker } from './PlanAssociationsPicker';
import type { AssociationOption, AssociationState, AssociationTargetKind, ProjectAssociationInspection } from './AssociationTypes';
import { commitProjectAssociations, isServerPathEqual, sameAssociationState, StaleAssociationInspectionError, inspectProjectAssociations } from './AssociationWorkflow';
import './associations.css';

export interface ModelAssociationsPanelProps extends FeaturePageProps {
  activeAsset: AssetReference;
  modelRevision: string;
  modelDirty: boolean;
}

type Phase = 'inspect' | 'commit' | null;

function targetKind(asset: AssetReference): AssociationTargetKind | null {
  if (asset.kind.toLowerCase() === 'plan') return 'plan';
  if (asset.kind.toLowerCase() === 'geometry') return 'geometry';
  return null;
}

function sourceKey(context: WorkbenchContext, asset: AssetReference, modelRevision: string): string {
  const locator = (asset.locator && typeof asset.locator === 'object') ? asset.locator as Record<string, unknown> : {};
  const core = locator.coreLayerContext ?? locator.CoreLayerContext;
  const coreIdentity = core && typeof core === 'object'
    ? ((core as Record<string, unknown>).identityToken ?? (core as Record<string, unknown>).IdentityToken ?? null)
    : null;
  return JSON.stringify([
    context.projectSessionID, context.projectPath, context.projectRevision, context.projectXml,
    context.selectedAsset?.path ?? null, context.selectedAsset?.kind ?? null, context.selectedAsset?.revision ?? null,
    asset.path, asset.kind, asset.revision ?? null, modelRevision,
    locator.sourceState ?? locator.SourceState ?? null, coreIdentity,
  ]);
}

function rawDiagnostic(error: unknown): string {
  const details = errorDiagnostics(error);
  if (details instanceof Error) return `${details.name}: ${details.message}${details.stack ? `\n${details.stack}` : ''}`;
  try { return JSON.stringify(details, null, 2) ?? String(details); }
  catch { return String(details); }
}

function optionForPath(options: AssociationOption[], path: string): AssociationOption | null {
  return options.find((item) => isServerPathEqual(item.path, path)) ?? null;
}

function initialDraft(inspection: ProjectAssociationInspection): AssociationState {
  return {
    geometry: inspection.before.geometry ?? null,
    boundaryCondition: inspection.before.boundaryCondition ?? null,
    terrain: inspection.before.terrain ?? null,
    nValue: inspection.before.nValue ?? null,
  };
}

function sameSource(left: WorkbenchContext, right: WorkbenchContext, asset: AssetReference, modelRevision: string): boolean {
  return sourceKey(left, asset, modelRevision) === sourceKey(right, asset, modelRevision);
}

export function ModelAssociationsPanel({ activeAsset, modelRevision, modelDirty, ...props }: ModelAssociationsPanelProps) {
  const { language, backend, backendRevision, context, hasUnsavedDrafts = false, onContextCommit } = props;
  const kind = targetKind(activeAsset);
  const key = sourceKey(context, activeAsset, modelRevision);
  const input = useMemo(() => ({ backend, context, activeAsset, targetKind: kind ?? 'plan', modelRevision }),
    [backend, context, activeAsset, kind, modelRevision]);
  const [inspection, setInspection] = useState<ProjectAssociationInspection | null>(null);
  const [draft, setDraft] = useState<AssociationState | null>(null);
  const [phase, setPhase] = useState<Phase>(null);
  const [error, setError] = useState<string>();
  const [details, setDetails] = useState<string>();
  const [notice, setNotice] = useState<string>();
  const generation = useRef(0);
  const abort = useRef<AbortController | undefined>(undefined);
  const draftRef = useRef<AssociationState | null>(draft);
  draftRef.current = draft;
  const contextRef = useRef(context);
  contextRef.current = context;
  const sourceKeyRef = useRef(key);
  sourceKeyRef.current = key;

  const supportsInspect = backend.supports('project.association.inspect');
  const supportsCommit = backend.supports('project.association.commit');
  const blocked = Boolean(modelDirty || hasUnsavedDrafts);
  const sourceReady = Boolean(kind && context.projectPath && context.projectXml && context.projectRevision && context.projectSessionID &&
    context.selectedAsset && isServerPathEqual(context.selectedAsset.path, activeAsset.path) &&
    context.selectedAsset.kind.toLowerCase() === activeAsset.kind.toLowerCase());

  const loadInspection = useCallback(async (resetDraft: boolean): Promise<void> => {
    if (!kind || !sourceReady || !supportsInspect) return;
    const requestGeneration = ++generation.current;
    abort.current?.abort();
    const controller = new AbortController();
    abort.current = controller;
    setPhase('inspect'); setError(undefined); setDetails(undefined); setNotice(undefined);
    try {
      const result = await inspectProjectAssociations({ ...input, signal: controller.signal });
      if (controller.signal.aborted || generation.current !== requestGeneration || sourceKeyRef.current !== key) return;
      setInspection(result.inspection);
      if (resetDraft || !draftRef.current) setDraft(initialDraft(result.inspection));
      if (!result.inspection.canCommit && result.inspection.validationMessage) setNotice(result.inspection.validationMessage);
    } catch (requestError) {
      if (controller.signal.aborted || generation.current !== requestGeneration) return;
      setError(message(requestError, language));
      setDetails(rawDiagnostic(requestError));
    } finally {
      if (!controller.signal.aborted && generation.current === requestGeneration) setPhase(null);
    }
  }, [input, key, kind, language, sourceReady, supportsInspect]);

  useEffect(() => {
    abort.current?.abort();
    abort.current = undefined;
    generation.current += 1;
    setInspection(null); setDraft(null); setPhase(null); setError(undefined); setDetails(undefined); setNotice(undefined);
    if (!kind || !sourceReady || !supportsInspect) return;
    void loadInspection(true);
    return () => {
      abort.current?.abort();
      generation.current += 1;
    };
  }, [backendRevision, key, kind, loadInspection, sourceReady, supportsInspect]);

  const setAssociation = (field: keyof AssociationState, path: string, options: AssociationOption[]): void => {
    setDraft((current) => current ? { ...current, [field]: path ? optionForPath(options, path) : null } : current);
    setNotice(undefined); setError(undefined); setDetails(undefined);
  };

  const currentMatchesDraft = Boolean(inspection && draft && sameAssociationState(draft, inspection.before, kind ?? 'plan'));
  const requiredChoicesReady = kind === 'plan'
    ? Boolean(draft?.geometry?.path && draft.boundaryCondition?.path && inspection?.options.geometry.length && inspection.options.boundaryCondition.length)
    : Boolean(draft && inspection);
  const canApply = Boolean(kind && inspection?.canCommit && supportsCommit && onContextCommit &&
    !blocked && !phase && requiredChoicesReady && !currentMatchesDraft && sourceReady);

  const apply = async (): Promise<void> => {
    if (!canApply || !inspection || !draft || !kind) return;
    const source = contextRef.current;
    const sourceOwner = sourceKeyRef.current;
    const requestGeneration = ++generation.current;
    abort.current?.abort();
    const controller = new AbortController();
    abort.current = controller;
    setPhase('commit'); setError(undefined); setDetails(undefined); setNotice(undefined);
    try {
      const result = await commitProjectAssociations({ ...input, context: source, signal: controller.signal }, draft, inspection);
      if (controller.signal.aborted || generation.current !== requestGeneration || sourceKeyRef.current !== sourceOwner ||
          !sameSource(contextRef.current, source, activeAsset, modelRevision)) return;
      const accepted = onContextCommit?.(source, result.committed) ?? false;
      if (!accepted) {
        setNotice(text(language,
          '关联已由工程服务提交，但当前工程会话未接受此回执。当前上下文未更新；请重新载入工程目录核对。',
          'The project service committed the associations, but the active project session rejected the receipt. The current context was not updated; reload the project catalog to verify it.'));
        return;
      }
      setNotice(text(language, '关联已提交到工程。正在使用更新后的工程修订重新载入模型。', 'Associations were committed. The model will reload with the updated project revision.'));
      setInspection(null); setDraft(null);
    } catch (requestError) {
      if (controller.signal.aborted || generation.current !== requestGeneration) return;
      if (requestError instanceof StaleAssociationInspectionError) {
        setInspection(requestError.freshInspection);
        setDraft(initialDraft(requestError.freshInspection));
        setError(text(language, requestError.message, 'The project associations or source options changed. They were reloaded; review the current selections and apply again.'));
        setDetails(rawDiagnostic(requestError));
      } else {
        setError(message(requestError, language));
        setDetails(rawDiagnostic(requestError));
      }
    } finally {
      if (!controller.signal.aborted && generation.current === requestGeneration) setPhase(null);
    }
  };

  if (!kind) return null;

  return (
    <section className="model-associations-panel" data-testid="model-associations-panel" aria-label={text(language, '模型关联', 'Model associations')}>
      <DetailedPlanHeader language={language} kind={kind} asset={activeAsset} modelRevision={modelRevision} projectRevision={context.projectRevision} associations={inspection?.before} />
      <div className="model-associations-toolbar">
        <div><strong>{text(language, '工程模型关联', 'Project model associations')}</strong><p>{text(language, '选择只影响待提交表单；只有通过工程服务校验并接受完整回执后才更新当前工程。', 'Selections stay in this form until the project service validates them and the active session accepts the full receipt.')}</p></div>
        <button type="button" className="editor-button editor-button-secondary" data-testid="association-refresh" disabled={phase !== null || !supportsInspect || !sourceReady} onClick={() => void loadInspection(true)}>{text(language, '重新检查', 'Refresh inspection')}</button>
      </div>
      {!supportsInspect && <div className="model-associations-warning" role="status">{text(language, '当前服务未提供关联检查能力。', 'The service does not provide association inspection.')}</div>}
      {!supportsCommit && <div className="model-associations-warning" role="status">{text(language, '当前服务未提供关联提交能力；可以查看已读取的信息。', 'The service does not provide association commit; current association data can be inspected.')}</div>}
      {!sourceReady && <div className="model-associations-warning" role="status">{text(language, '请先打开并载入当前工程中的计划或几何。', 'Open a project-owned plan or geometry and wait for it to load.')}</div>}
      {blocked && <div className="model-associations-warning" role="status">{text(language, '存在未保存的模型草稿；保存或处理草稿后才能修改工程关联。', 'Unsaved model drafts exist. Save or resolve them before changing project associations.')}</div>}
      {!onContextCommit && <div className="model-associations-warning" role="status">{text(language, '当前工程会话未提供回执接收处理。', 'The active project session cannot accept committed receipts.')}</div>}
      {phase === 'inspect' && <p className="model-associations-status" role="status"><span className="model-associations-spinner" />{text(language, '正在读取当前关联和工程候选文件…', 'Reading current associations and project source files…')}</p>}
      {inspection && draft && kind === 'plan' && <PlanAssociationsPicker
        language={language}
        options={{ geometry: inspection.options.geometry, boundaryCondition: inspection.options.boundaryCondition }}
        geometryPath={draft.geometry?.path ?? ''}
        boundaryConditionPath={draft.boundaryCondition?.path ?? ''}
        disabled={phase !== null || blocked || !inspection.canCommit}
        onGeometryChange={(path) => setAssociation('geometry', path, inspection.options.geometry)}
        onBoundaryConditionChange={(path) => setAssociation('boundaryCondition', path, inspection.options.boundaryCondition)}
      />}
      {inspection && draft && kind === 'geometry' && <GeometryAssociationsPicker
        language={language}
        options={{ terrain: inspection.options.terrain, nValue: inspection.options.nValue }}
        terrainPath={draft.terrain?.path ?? ''}
        nValuePath={draft.nValue?.path ?? ''}
        disabled={phase !== null || blocked || !inspection.canCommit}
        onTerrainChange={(path) => setAssociation('terrain', path, inspection.options.terrain)}
        onNValueChange={(path) => setAssociation('nValue', path, inspection.options.nValue)}
      />}
      {inspection && <details className="model-association-capture">
        <summary>{text(language, '查看来源校验信息', 'Show source verification')}</summary>
        <div><span>{text(language, '目标模型 SHA-256', 'Target model SHA-256')}</span><code>{inspection.target.sha256}</code></div>
        <div><span>{text(language, '关联修订 SHA-256', 'Association SHA-256')}</span><code>{inspection.associationSHA256}</code></div>
        <div><span>{text(language, '候选项 SHA-256', 'Options SHA-256')}</span><code>{inspection.optionsSHA256}</code></div>
        <div><span>{text(language, '源引用 SHA-256', 'Source references SHA-256')}</span><code>{inspection.sourceReferencesSHA256}</code></div>
        <ul>{inspection.dependencies.map((item) => <li key={`${item.kind}:${item.path}`}><span>{item.name} · {item.kind}</span><code title={item.path}>{item.exists ? item.sha256 : text(language, '文件不存在', 'Missing file')} · {item.path}</code></li>)}</ul>
      </details>}
      {inspection?.validationMessage && !inspection.canCommit && <div className="model-associations-warning" role="alert">{inspection.validationMessage}</div>}
      {notice && <div className="model-associations-notice" role="status">{notice}</div>}
      {error && <div className="model-associations-error" role="alert"><span>!</span><div>{error}{details && <details><summary>{text(language, '原始诊断', 'Raw diagnostics')}</summary><pre>{details}</pre></details>}</div></div>}
      <footer className="model-associations-footer">
        <small>{inspection ? text(language, `${inspection.dependencies.length} 个已捕获源文件，工程会话 ${inspection.projectSessionID.slice(0, 8)}`, `${inspection.dependencies.length} captured source files · session ${inspection.projectSessionID.slice(0, 8)}`) : text(language, '等待工程服务检查。', 'Waiting for project service inspection.')}</small>
        <button type="button" className="editor-button editor-button-primary" data-testid="association-apply" disabled={!canApply} onClick={() => void apply()}>
          {phase === 'commit' ? text(language, '正在校验并提交…', 'Verifying and committing…') : text(language, '应用关联', 'Apply associations')}
        </button>
      </footer>
    </section>
  );
}
