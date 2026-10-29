import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import type { ContextChangeOptions, FeatureId, Language, MapVisualizationAcknowledgement, MapVisualizationTarget, ThemeMode, WorkbenchContext } from '../shared/workbench';
import { assetKindLabel, message, text } from '../shared/workbench';
import { ServerBackend } from '../platform/ServerBackend';
import { sessionBackend } from '../platform/SessionBackend';
import { mergeSessionReferenceContext, sessionReferenceIdentity, sessionReferenceSourceIdentity, validateSessionReferenceContext, withSessionReferenceContext } from '../shared/sessionReferences';
import { RasContent } from './RasContent';
import { RasMenuBar } from './RasMenuBar';
import './RasMainWindow.css';
import type { PendingProjectCommand } from '../features/project';
import { ConfirmActionControlDialog } from '../controls/ConfirmActionControlDialog';
import { FramedContentDialog } from '../controls/FramedContentDialog';
import { captureMapVisualizationLease, matchesMapVisualizationReceipt, type MapVisualizationLease } from '../shared/mapVisualizationMutation';
import { captureProjectContextLease, matchesProjectContextCommit, type ProjectContextLease } from '../shared/projectContextMutation';
import { captureModelSaveLease, matchesModelSaveCommit, trackModelSaveOperation, type ModelSaveLease } from '../shared/modelSaveMutation';
import { useRasterCalculatorSessions } from '../shared/useRasterCalculatorSessions';
import { clearPlanAnalysisDraftRegistry, emptyPlanAnalysisDraftRegistry, hasProtectedSourceWrite, matchesPlanSaveRecoveryAcknowledgement, publishPlanAnalysisDraft, type PlanAnalysisDraftPublication, type PlanSaveRecoveryAcknowledgement } from '../shared/planAnalysisDrafts';

const features: { id: FeatureId; zh: string; en: string }[] = [
  { id: 'project', zh: '工程', en: 'Project' },
  { id: 'map', zh: '地图', en: 'Map' },
  { id: 'editors', zh: '模型编辑', en: 'Model editors' },
  { id: 'geometry', zh: '几何与网格', en: 'Geometry and mesh' },
  { id: 'results', zh: '结果', en: 'Results' },
  { id: 'tools', zh: '工具', en: 'Tools' },
  { id: 'compute', zh: '计算', en: 'Compute' },
  { id: 'dam-break', zh: '洪水推演', en: 'Flood Studio' },
];
const blankContext = (): WorkbenchContext => ({ projectSessionID: crypto.randomUUID(), projectPath: null, projectXml: null, projectRevision: null, selectedAsset: null });
const identity = (value: WorkbenchContext) => JSON.stringify([value.projectSessionID, value.projectPath, value.selectedAsset?.path ?? null, value.selectedAsset?.revision ?? null]);
const initialFeature = (): FeatureId => {
  const route = window.location.hash.replace(/^#\/?/, '');
  return features.some((feature) => feature.id === route) ? route as FeatureId : 'project';
};
const initialLanguage = (): Language => {
  try { return localStorage.getItem('hecras.language') === 'en' ? 'en' : 'zh'; }
  catch { return 'zh'; }
};
const themePreferenceKey = 'hecras.theme.preference.v1';
const initialThemeMode = (): ThemeMode => {
  try {
    const raw = localStorage.getItem(themePreferenceKey);
    if (!raw) return 'system';
    const value = JSON.parse(raw) as { version?: unknown; mode?: unknown };
    return value.version === 1 && (value.mode === 'light' || value.mode === 'dark' || value.mode === 'system') ? value.mode : 'system';
  } catch {
    return 'system';
  }
};

/** Web counterpart of RasMainWindow.cs; file/model mutations remain backend-owned. */
export function RasMainWindow() {
  const [language, setLanguage] = useState<Language>(initialLanguage);
  const [themeMode, setThemeMode] = useState<ThemeMode>(initialThemeMode);
  const [activeFeature, setActiveFeature] = useState<FeatureId>(initialFeature);
  const [context, setContext] = useState<WorkbenchContext>(blankContext);
  const contextRef = useRef(context);
  const [transport] = useState(() => new ServerBackend());
  const backend = useMemo(() => sessionBackend(transport, context),
    [transport, context.projectSessionID, context.projectPath, context.coreLayerContext]);
  const referenceWriteLease = useRef<{ id: string; source: string } | null>(null);
  const [referenceWriting, setReferenceWriting] = useState(false);
  const mapWriteLease = useRef<{ capture: MapVisualizationLease; receipt: string | null } | null>(null);
  const [mapWriting, setMapWriting] = useState(false);
  const [mapRevision, setMapRevision] = useState(0);
  const projectWriteLease = useRef<ProjectContextLease | null>(null);
  const modelSaveLease = useRef<ModelSaveLease | null>(null);
  const [projectWriting, setProjectWriting] = useState(false);
  const [backendRevision, setBackendRevision] = useState(0);
  const [dirtyOwners, setDirtyOwners] = useState<Set<string>>(() => new Set());
  const dirtyOwnersRef = useRef(dirtyOwners);
  const [planDraftRegistry, setPlanDraftRegistry] = useState(emptyPlanAnalysisDraftRegistry);
  const planDraftRegistryRef = useRef(planDraftRegistry);
  const acceptedPlanRecoveryAttempts = useRef(new Set<string>());
  const planAnalysisDrafts = useMemo(() => {
    const sessionBusy = referenceWriting || mapWriting || projectWriting;
    return { ...planDraftRegistry, sessionBusy, captureKey: `${planDraftRegistry.captureKey}:busy:${sessionBusy}` };
  }, [planDraftRegistry, referenceWriting, mapWriting, projectWriting]);
  const onPlanAnalysisDraftChange = useCallback((editorID: string, publication: PlanAnalysisDraftPublication | null): void => {
    const current = contextRef.current;
    if (publication && (publication.source.projectSessionID !== current.projectSessionID
      || publication.source.projectPath !== current.projectPath)) return;
    const next = publishPlanAnalysisDraft(planDraftRegistryRef.current, editorID, publication);
    planDraftRegistryRef.current = next;
    setPlanDraftRegistry(next);
  }, []);
  useLayoutEffect(() => {
    let next = planDraftRegistryRef.current;
    for (const entry of next.entries) {
      if (entry.source.projectSessionID !== context.projectSessionID || entry.source.projectPath !== context.projectPath)
        next = publishPlanAnalysisDraft(next, entry.editorID, null);
    }
    planDraftRegistryRef.current = next;
    setPlanDraftRegistry(next);
  }, [context.projectSessionID, context.projectPath]);
  const [pendingContext, setPendingContext] = useState<{ target: WorkbenchContext; source: string; options?: ContextChangeOptions } | null>(null);
  const [showInspector, setShowInspector] = useState(false);
  const [showHelp, setShowHelp] = useState(false);
  const [pendingProjectCommand, setPendingProjectCommand] = useState<PendingProjectCommand | null>(null);
  const [runtimeState, setRuntimeState] = useState<'idle' | 'loading' | 'ready' | 'failed'>('idle');
  const [runtimeError, setRuntimeError] = useState<string | null>(null);
  const t = (zh: string, en: string) => text(language, zh, en);

  useEffect(() => { contextRef.current = context; }, [context]);
  useEffect(() => {
    try { localStorage.setItem('hecras.language', language); } catch { /* Optional preference storage. */ }
    document.documentElement.lang = language === 'zh' ? 'zh-CN' : 'en';
  }, [language]);
  useLayoutEffect(() => {
    try { localStorage.setItem(themePreferenceKey, JSON.stringify({ version: 1, mode: themeMode })); }
    catch { /* Preferences are optional when browser storage is unavailable. */ }
    const media = window.matchMedia('(prefers-color-scheme: dark)');
    const applyTheme = (): void => {
      document.documentElement.dataset.theme = themeMode === 'system' ? (media.matches ? 'dark' : 'light') : themeMode;
    };
    applyTheme();
    media.addEventListener('change', applyTheme);
    return () => media.removeEventListener('change', applyTheme);
  }, [themeMode]);
  useEffect(() => {
    const beforeUnload = (event: BeforeUnloadEvent) => {
      if (dirtyOwnersRef.current.size > 0 || referenceWriteLease.current || mapWriteLease.current || projectWriteLease.current) { event.preventDefault(); event.returnValue = ''; }
    };
    window.addEventListener('beforeunload', beforeUnload);
    return () => window.removeEventListener('beforeunload', beforeUnload);
  }, []);
  useEffect(() => backend.subscribeCapabilities?.(setBackendRevision), [backend]);
  useEffect(() => () => transport.dispose(), [transport]);

  const onNavigate = useCallback((feature: FeatureId) => {
    if (referenceWriteLease.current || mapWriteLease.current || projectWriteLease.current || hasProtectedSourceWrite(dirtyOwnersRef.current, planDraftRegistryRef.current)) return;
    setActiveFeature(feature);
    window.history.replaceState(null, '', `#/${feature}`);
  }, []);
  const calculatorSessions = useRasterCalculatorSessions(context, onNavigate,
    referenceWriting || mapWriting || projectWriting || hasProtectedSourceWrite(dirtyOwners, planAnalysisDrafts));
  const onProjectCommand = useCallback((kind: 'open' | 'new') => {
    if (referenceWriteLease.current || mapWriteLease.current || projectWriteLease.current || hasProtectedSourceWrite(dirtyOwnersRef.current, planDraftRegistryRef.current)) return;
    setPendingProjectCommand({ id: crypto.randomUUID(), kind });
    onNavigate('project');
  }, [onNavigate]);
  const onProjectCommandHandled = useCallback((id: string) => {
    setPendingProjectCommand((current) => current?.id === id ? null : current);
  }, []);
  const onDirtyStateChange = useCallback((owner: string, dirty: boolean) => {
    const current = dirtyOwnersRef.current;
    if (current.has(owner) === dirty) return;
    const next = new Set(current);
    if (dirty) next.add(owner); else next.delete(owner);
    dirtyOwnersRef.current = next;
    setDirtyOwners(next);
  }, []);
  const commitContext = useCallback((next: WorkbenchContext, options?: ContextChangeOptions) => {
    contextRef.current = next;
    setContext(next);
    if (options?.navigation === 'preserve') return;
    if (options?.navigation && options.navigation !== 'auto') {
      onNavigate(options.navigation);
      return;
    }
    const kind = next.selectedAsset?.kind.toLowerCase();
    if (!kind || kind === 'project' || kind === 'projectfile') return;
    if (kind.includes('geometry')) onNavigate('geometry');
    else if (kind.includes('result')) onNavigate('results');
    else onNavigate('editors');
  }, [onNavigate]);
  const onContextChange = useCallback((incoming: WorkbenchContext, options?: ContextChangeOptions) => {
    if (referenceWriteLease.current || mapWriteLease.current || projectWriteLease.current || hasProtectedSourceWrite(dirtyOwnersRef.current, planDraftRegistryRef.current)) return;
    let next: WorkbenchContext;
    try { next = mergeSessionReferenceContext(incoming, contextRef.current); }
    catch (error) { setRuntimeError(message(error)); return; }
    if (identity(next) !== identity(contextRef.current) && dirtyOwnersRef.current.size > 0) {
      setPendingContext({ target: next, source: identity(contextRef.current), options });
      return;
    }
    commitContext(next, options);
  }, [commitContext]);
  const onContextCommit = useCallback((source: WorkbenchContext, committed: WorkbenchContext, recovery?: PlanSaveRecoveryAcknowledgement): boolean => {
    const current = contextRef.current;
    const recoveryKey = recovery ? JSON.stringify(recovery) : null;
    if (recovery && (acceptedPlanRecoveryAttempts.current.has(recoveryKey!)
      || !planDraftRegistryRef.current.entries.some(entry => matchesPlanSaveRecoveryAcknowledgement(entry, source, recovery)))) return false;
    if (referenceWriteLease.current || mapWriteLease.current || projectWriteLease.current || hasProtectedSourceWrite(dirtyOwnersRef.current, planDraftRegistryRef.current, source, recovery)) return false;
    if (sessionReferenceIdentity(source) !== sessionReferenceIdentity(current) || identity(source) !== identity(current) || source.projectXml !== current.projectXml || source.projectRevision !== current.projectRevision
      || source.selectedAsset?.kind !== current.selectedAsset?.kind
      || source.projectSessionID !== committed.projectSessionID || source.projectPath !== committed.projectPath
      || source.selectedAsset?.path !== committed.selectedAsset?.path
      || source.selectedAsset?.kind !== committed.selectedAsset?.kind) return false;
    try {
      const next = mergeSessionReferenceContext(committed, current, true);
      if (recoveryKey) acceptedPlanRecoveryAttempts.current.add(recoveryKey);
      commitContext(next);
    }
    catch (error) { setRuntimeError(message(error)); return false; }
    return true;
  }, [commitContext]);
  const onBeginSessionReferenceMutation = useCallback((source: WorkbenchContext): string | null => {
    if (referenceWriteLease.current || mapWriteLease.current || projectWriteLease.current || hasProtectedSourceWrite(dirtyOwnersRef.current, planDraftRegistryRef.current) || dirtyOwnersRef.current.size || !source.projectPath ||
        sessionReferenceSourceIdentity(source) !== sessionReferenceSourceIdentity(contextRef.current)) return null;
    const id = crypto.randomUUID();
    referenceWriteLease.current = { id, source: sessionReferenceSourceIdentity(source) };
    setReferenceWriting(true);
    return id;
  }, []);
  const onCommitSessionReferences = useCallback((source: WorkbenchContext, core: Record<string, unknown>, leaseID: string): boolean => {
    const lease = referenceWriteLease.current;
    if (!lease || lease.id !== leaseID || lease.source !== sessionReferenceSourceIdentity(source) ||
        lease.source !== sessionReferenceSourceIdentity(contextRef.current) || dirtyOwnersRef.current.size) return false;
    try {
      validateSessionReferenceContext(core, source);
      const next = withSessionReferenceContext(contextRef.current, core);
      // This acknowledged map transaction retains selection and active workspace.
      contextRef.current = next;
      setContext(next);
      return true;
    } catch (error) { setRuntimeError(message(error)); return false; }
  }, []);
  const onEndSessionReferenceMutation = useCallback((leaseID: string): void => {
    if (referenceWriteLease.current?.id !== leaseID) return;
    referenceWriteLease.current = null;
    setReferenceWriting(false);
  }, []);
  const onBeginMapVisualizationMutation = useCallback((source: WorkbenchContext, target: MapVisualizationTarget, draftOwner: string): string | null => {
    if (referenceWriteLease.current || mapWriteLease.current || projectWriteLease.current || hasProtectedSourceWrite(dirtyOwnersRef.current, planDraftRegistryRef.current)) return null;
    const capture = captureMapVisualizationLease(contextRef.current, source, target, draftOwner, dirtyOwnersRef.current);
    if (!capture) return null;
    mapWriteLease.current = { capture, receipt: null };
    setMapWriting(true);
    return capture.id;
  }, []);
  const onCommitMapVisualization = useCallback((source: WorkbenchContext, target: MapVisualizationTarget,
    receipt: MapVisualizationAcknowledgement, leaseID: string): boolean => {
    const lease = mapWriteLease.current;
    if (!lease || lease.capture.id !== leaseID ||
        !matchesMapVisualizationReceipt(lease.capture, contextRef.current, source, target, receipt)) return false;
    const receiptIdentity = JSON.stringify(receipt);
    if (lease.receipt !== null) return lease.receipt === receiptIdentity;
    lease.receipt = receiptIdentity;
    // The acknowledged write leaves model drafts and the current selection intact.
    setMapRevision(current => current + 1);
    return true;
  }, []);
  const onEndMapVisualizationMutation = useCallback((leaseID: string): void => {
    const lease = mapWriteLease.current;
    if (lease?.capture.id !== leaseID) return;
    // A lost acknowledgement can still follow a committed .vis write. Read
    // Invalidate the map page so it reads the actual saved state again.
    if (lease.receipt === null) setMapRevision(current => current + 1);
    mapWriteLease.current = null;
    setMapWriting(false);
  }, []);
  const onBeginProjectContextMutation = useCallback((source: WorkbenchContext, draftOwner: string): string | null => {
    if (referenceWriteLease.current || mapWriteLease.current || projectWriteLease.current || hasProtectedSourceWrite(dirtyOwnersRef.current, planDraftRegistryRef.current)) return null;
    const lease = captureProjectContextLease(contextRef.current, source, draftOwner, dirtyOwnersRef.current);
    if (!lease) return null;
    projectWriteLease.current = lease;
    setProjectWriting(true);
    return lease.id;
  }, []);
  const onCommitProjectContextMutation = useCallback((source: WorkbenchContext, committed: WorkbenchContext, leaseID: string): boolean => {
    const lease = projectWriteLease.current;
    if (!lease || lease.id !== leaseID || !matchesProjectContextCommit(lease, contextRef.current, source, committed)) return false;
    try {
      commitContext(mergeSessionReferenceContext(committed, contextRef.current, true), { navigation: 'preserve' });
      return true;
    } catch (error) { setRuntimeError(message(error)); return false; }
  }, [commitContext]);
  const onEndProjectContextMutation = useCallback((leaseID: string): void => {
    if (projectWriteLease.current?.id !== leaseID) return;
    projectWriteLease.current = null;
    setProjectWriting(false);
  }, []);
  const onBeginModelSave = useCallback((source: WorkbenchContext, draftOwner: string, writeOwner: string, sourceRevision: string): string | null => {
    if (modelSaveLease.current || referenceWriteLease.current || mapWriteLease.current || projectWriteLease.current
      || hasProtectedSourceWrite(dirtyOwnersRef.current, planDraftRegistryRef.current)) return null;
    const lease = captureModelSaveLease(contextRef.current, source, draftOwner, writeOwner, dirtyOwnersRef.current, sourceRevision);
    if (!lease) return null;
    modelSaveLease.current = lease;
    onDirtyStateChange(writeOwner, true);
    return lease.id;
  }, [onDirtyStateChange]);
  const onTrackModelSave = useCallback((leaseID: string, operationID: string): boolean => {
    const lease = modelSaveLease.current;
    if (!lease || lease.id !== leaseID) return false;
    const tracked = trackModelSaveOperation(lease, operationID);
    if (!tracked) return false;
    modelSaveLease.current = tracked;
    return true;
  }, []);
  const onCommitModelSave = useCallback((source: WorkbenchContext, committed: WorkbenchContext,
    leaseID: string, operationID: string): boolean => {
    const lease = modelSaveLease.current;
    if (!lease || lease.id !== leaseID || referenceWriteLease.current || mapWriteLease.current || projectWriteLease.current
      || !dirtyOwnersRef.current.has(lease.writeOwner)
      || !matchesModelSaveCommit(lease, contextRef.current, source, committed, operationID)) return false;
    const otherOwners = new Set(dirtyOwnersRef.current); otherOwners.delete(lease.writeOwner);
    if (hasProtectedSourceWrite(otherOwners, planDraftRegistryRef.current)) return false;
    try {
      const next = mergeSessionReferenceContext(committed, contextRef.current, true);
      // Adopt and consume the exact acknowledgement in one synchronous callback.
      commitContext(next, { navigation: 'preserve' });
      modelSaveLease.current = null;
      onDirtyStateChange(lease.writeOwner, false);
      return true;
    } catch (error) { setRuntimeError(message(error)); return false; }
  }, [commitContext, onDirtyStateChange]);
  const onEndModelSave = useCallback((leaseID: string): void => {
    const lease = modelSaveLease.current;
    if (!lease || lease.id !== leaseID) return;
    modelSaveLease.current = null;
    onDirtyStateChange(lease.writeOwner, false);
  }, [onDirtyStateChange]);
  const closeProject = useCallback(() => onContextChange(blankContext()), [onContextChange]);
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'o') {
        event.preventDefault(); onProjectCommand('open');
      }
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [onProjectCommand]);
  const connect = useCallback(async () => {
    setRuntimeState('loading'); setRuntimeError(null);
    try { await backend.request('runtime.info', {}); setRuntimeState('ready'); }
    catch (error) { setRuntimeState('failed'); setRuntimeError(message(error)); }
  }, [backend]);
  useEffect(() => { void connect(); }, [connect]);
  const props = { language, themeMode, onThemeModeChange: setThemeMode, context, backend, backendRevision, calculatorSessions, planAnalysisDrafts, onPlanAnalysisDraftChange, hasUnsavedDrafts: dirtyOwners.size > 0 || referenceWriting || mapWriting || projectWriting, dirtyOwners, onContextChange, onContextCommit, onBeginSessionReferenceMutation, onCommitSessionReferences, onEndSessionReferenceMutation, onBeginMapVisualizationMutation, onCommitMapVisualization, onEndMapVisualizationMutation, onBeginProjectContextMutation, onCommitProjectContextMutation, onEndProjectContextMutation, onBeginModelSave, onTrackModelSave, onCommitModelSave, onEndModelSave, mapRevision, onNavigate, onDirtyStateChange, pendingProjectCommand, onProjectCommandHandled };
  const title = context.selectedAsset?.name ?? context.projectPath?.split(/[\\/]/).at(-1) ?? t('尚未打开工程', 'No project open');

  return <main className={`ras-main-window${activeFeature === 'dam-break' ? ' ras-flood-workspace' : ''}`}>
    <header className="ras-titlebar"><strong>HEC-RAS 2025</strong><span className="ras-document-title">{title}{dirtyOwners.size > 0 ? ' •' : ''}</span><span className="ras-platform">{window.hecDesktop?.isElectron ? t('桌面版', 'Desktop') : t('网页版', 'Web')}</span><button onClick={() => setLanguage(language === 'zh' ? 'en' : 'zh')}>{language === 'zh' ? 'English' : '简体中文'}</button></header>
    <RasMenuBar language={language} onNavigate={onNavigate} onProjectCommand={onProjectCommand} onCloseProject={closeProject} hasProject={context.projectPath !== null} onToggleInspector={() => setShowInspector((value) => !value)} showInspector={showInspector} onHelp={() => setShowHelp(true)} />
    <nav className="ras-feature-nav" aria-label={t('工作区', 'Workspace')}>{features.map((feature) => <button key={feature.id} aria-current={activeFeature === feature.id ? 'page' : undefined} onClick={() => onNavigate(feature.id)}>{language === 'zh' ? feature.zh : feature.en}</button>)}<span className="ras-nav-spacer" /><button disabled={runtimeState === 'loading'} onClick={() => void connect()}>{runtimeState === 'ready' ? t('服务已连接 · 重试', 'Service connected · retry') : runtimeState === 'loading' ? t('正在连接…', 'Connecting…') : t('连接服务', 'Connect server')}</button></nav>
    <div className={`ras-main-content${showInspector ? '' : ' ras-inspector-hidden'}`}>
      <RasContent activeFeature={activeFeature} {...props} />
      {showInspector && <aside className="ras-inspector" aria-label={t('属性', 'Properties')}><h2>{t('属性', 'Properties')}</h2>{context.selectedAsset ? <><dl><dt>{t('名称', 'Name')}</dt><dd>{context.selectedAsset.name}</dd><dt>{t('类型', 'Type')}</dt><dd>{assetKindLabel(context.selectedAsset.kind, language)}</dd><dt>{t('文件', 'File')}</dt><dd>{context.selectedAsset.path}</dd></dl><button onClick={() => onNavigate(context.selectedAsset?.kind.toLowerCase().includes('geometry') ? 'geometry' : 'editors')}>{t('打开编辑器', 'Open editor')}</button></> : <p>{t('选择工程中的对象查看属性。', 'Select a project object to inspect its properties.')}</p>}{runtimeError && <details className="ras-runtime-error"><summary>{t('服务连接失败', 'Server connection failed')}</summary><p>{t('无法连接 C# 服务，请确认服务已启动且当前地址可访问。', 'Cannot connect to the C# server. Confirm that it is running and reachable.')}</p><pre>{runtimeError}</pre></details>}</aside>}
    </div>
    <footer className="ras-statusbar"><span role="status">{projectWriting ? t('正在更新工程连接…', 'Updating a project connection…') : mapWriting ? t('正在加入地图图层…', 'Adding a map layer…') : hasProtectedSourceWrite(dirtyOwners, planAnalysisDrafts) ? t('正在等待操作确认完成…', 'Awaiting operation completion…') : referenceWriting ? t('正在提交外部来源…', 'Committing external source…') : dirtyOwners.size > 0 ? t('有未保存的草稿', 'Unsaved drafts') : t('就绪', 'Ready')}</span><span>{context.projectPath ?? t('无活动工程', 'No active project')}</span><span>{runtimeState === 'ready' ? t('C# 服务已连接', 'C# server connected') : runtimeState === 'loading' ? t('正在连接 C# 服务', 'Connecting to C# server') : t('C# 服务未连接', 'C# server disconnected')}</span></footer>
    <ConfirmActionControlDialog
      open={pendingContext !== null}
      title={t('保留未保存的修改？', 'Keep unsaved changes?')}
      message={t('当前编辑器有未保存的草稿。你可以返回编辑器保存，或者放弃草稿后切换对象。', 'The current editors contain unsaved drafts. Return to save them, or discard the drafts before switching objects.')}
      severity="warning"
      cancelLabel={t('返回编辑', 'Keep editing')}
      continueLabel={t('放弃草稿并继续', 'Discard and continue')}
      continueIsDangerous
      autoFocusCancel
      onCancel={() => setPendingContext(null)}
      onContinue={() => {
        if (referenceWriteLease.current || mapWriteLease.current || projectWriteLease.current || hasProtectedSourceWrite(dirtyOwnersRef.current, planDraftRegistryRef.current)) return;
        if (!pendingContext || pendingContext.source !== identity(contextRef.current)) { setPendingContext(null); return; }
        const clearedRegistry = clearPlanAnalysisDraftRegistry(planDraftRegistryRef.current, true);
        planDraftRegistryRef.current = clearedRegistry; setPlanDraftRegistry(clearedRegistry);
        dirtyOwnersRef.current = new Set(); setDirtyOwners(dirtyOwnersRef.current); commitContext(pendingContext.target, pendingContext.options); setPendingContext(null);
      }}
    />
    <FramedContentDialog open={showHelp} title="HEC-RAS 2025" onCancel={() => setShowHelp(false)}><p>{t('在工程页选择工程文件，在模型、几何、地图和结果页编辑与查看数据。界面在浏览器与桌面使用同一套 React 组件。', 'Select a project in Project, then use the model, geometry, map and result pages. Browser and desktop share the same React interface.')}</p><p>{t('需要计算能力的操作会根据当前可用功能启用，工程数据编辑与计算会通过关联服务处理。', 'Operations that need compute are enabled according to available capabilities. Project-data editing and computation are handled by the connected service.')}</p><div className="ras-dialog-actions"><button autoFocus onClick={() => setShowHelp(false)}>{t('关闭', 'Close')}</button></div></FramedContentDialog>
  </main>;
}
