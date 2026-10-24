import { useCallback, useEffect, useRef, useState } from 'react';
import type { FeatureId, Language, WorkbenchContext } from '../shared/workbench';
import { assetKindLabel, message, text } from '../shared/workbench';
import { ServerBackend } from '../platform/ServerBackend';
import { RasContent } from './RasContent';
import { RasMenuBar } from './RasMenuBar';
import './RasMainWindow.css';
import type { PendingProjectCommand } from '../features/project';
import { ConfirmActionControlDialog } from '../controls/ConfirmActionControlDialog';
import { FramedContentDialog } from '../controls/FramedContentDialog';

const features: { id: FeatureId; zh: string; en: string }[] = [
  { id: 'project', zh: '工程', en: 'Project' },
  { id: 'map', zh: '地图', en: 'Map' },
  { id: 'editors', zh: '模型编辑', en: 'Model editors' },
  { id: 'geometry', zh: '几何与网格', en: 'Geometry and mesh' },
  { id: 'results', zh: '结果', en: 'Results' },
  { id: 'tools', zh: '工具', en: 'Tools' },
  { id: 'compute', zh: '计算', en: 'Compute' },
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

/** Web counterpart of RasMainWindow.cs; file/model mutations remain backend-owned. */
export function RasMainWindow() {
  const [language, setLanguage] = useState<Language>(initialLanguage);
  const [activeFeature, setActiveFeature] = useState<FeatureId>(initialFeature);
  const [context, setContext] = useState<WorkbenchContext>(blankContext);
  const contextRef = useRef(context);
  const [backend] = useState(() => new ServerBackend());
  const [backendRevision, setBackendRevision] = useState(0);
  const [dirtyOwners, setDirtyOwners] = useState<Set<string>>(() => new Set());
  const dirtyOwnersRef = useRef(dirtyOwners);
  const [pendingContext, setPendingContext] = useState<{ target: WorkbenchContext; source: string } | null>(null);
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
  useEffect(() => {
    const beforeUnload = (event: BeforeUnloadEvent) => {
      if (dirtyOwnersRef.current.size > 0) { event.preventDefault(); event.returnValue = ''; }
    };
    window.addEventListener('beforeunload', beforeUnload);
    return () => window.removeEventListener('beforeunload', beforeUnload);
  }, []);
  useEffect(() => backend.subscribeCapabilities?.(setBackendRevision), [backend]);
  useEffect(() => () => backend.dispose(), [backend]);

  const onNavigate = useCallback((feature: FeatureId) => {
    setActiveFeature(feature);
    window.history.replaceState(null, '', `#/${feature}`);
  }, []);
  const onProjectCommand = useCallback((kind: 'open' | 'new') => {
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
  const commitContext = useCallback((next: WorkbenchContext) => {
    contextRef.current = next;
    setContext(next);
    const kind = next.selectedAsset?.kind.toLowerCase();
    if (!kind || kind === 'project' || kind === 'projectfile') return;
    if (kind.includes('geometry')) onNavigate('geometry');
    else if (kind.includes('result')) onNavigate('results');
    else onNavigate('editors');
  }, [onNavigate]);
  const onContextChange = useCallback((next: WorkbenchContext) => {
    if (identity(next) !== identity(contextRef.current) && dirtyOwnersRef.current.size > 0) {
      setPendingContext({ target: next, source: identity(contextRef.current) });
      return;
    }
    commitContext(next);
  }, [commitContext]);
  const onContextCommit = useCallback((source: WorkbenchContext, committed: WorkbenchContext): boolean => {
    const current = contextRef.current;
    if (identity(source) !== identity(current) || source.projectXml !== current.projectXml || source.projectRevision !== current.projectRevision
      || source.selectedAsset?.kind !== current.selectedAsset?.kind
      || source.projectSessionID !== committed.projectSessionID || source.projectPath !== committed.projectPath
      || source.selectedAsset?.path !== committed.selectedAsset?.path
      || source.selectedAsset?.kind !== committed.selectedAsset?.kind) return false;
    commitContext(committed);
    return true;
  }, [commitContext]);
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
  const props = { language, context, backend, backendRevision, hasUnsavedDrafts: dirtyOwners.size > 0, dirtyOwners, onContextChange, onContextCommit, onNavigate, onDirtyStateChange, pendingProjectCommand, onProjectCommandHandled };
  const title = context.selectedAsset?.name ?? context.projectPath?.split(/[\\/]/).at(-1) ?? t('尚未打开工程', 'No project open');

  return <main className="ras-main-window">
    <header className="ras-titlebar"><strong>HEC-RAS 2025</strong><span className="ras-document-title">{title}{dirtyOwners.size > 0 ? ' •' : ''}</span><span className="ras-platform">{window.hecDesktop?.isElectron ? t('桌面版', 'Desktop') : t('网页版', 'Web')}</span><button onClick={() => setLanguage(language === 'zh' ? 'en' : 'zh')}>{language === 'zh' ? 'English' : '简体中文'}</button></header>
    <RasMenuBar language={language} onNavigate={onNavigate} onProjectCommand={onProjectCommand} onCloseProject={closeProject} hasProject={context.projectPath !== null} onToggleInspector={() => setShowInspector((value) => !value)} showInspector={showInspector} onHelp={() => setShowHelp(true)} />
    <nav className="ras-feature-nav" aria-label={t('工作区', 'Workspace')}>{features.map((feature) => <button key={feature.id} aria-current={activeFeature === feature.id ? 'page' : undefined} onClick={() => onNavigate(feature.id)}>{language === 'zh' ? feature.zh : feature.en}</button>)}<span className="ras-nav-spacer" /><button disabled={runtimeState === 'loading'} onClick={() => void connect()}>{runtimeState === 'ready' ? t('服务已连接 · 重试', 'Service connected · retry') : runtimeState === 'loading' ? t('正在连接…', 'Connecting…') : t('连接服务', 'Connect server')}</button></nav>
    <div className={`ras-main-content${showInspector ? '' : ' ras-inspector-hidden'}`}>
      <RasContent activeFeature={activeFeature} {...props} />
      {showInspector && <aside className="ras-inspector" aria-label={t('属性', 'Properties')}><h2>{t('属性', 'Properties')}</h2>{context.selectedAsset ? <><dl><dt>{t('名称', 'Name')}</dt><dd>{context.selectedAsset.name}</dd><dt>{t('类型', 'Type')}</dt><dd>{assetKindLabel(context.selectedAsset.kind, language)}</dd><dt>{t('文件', 'File')}</dt><dd>{context.selectedAsset.path}</dd></dl><button onClick={() => onNavigate(context.selectedAsset?.kind.toLowerCase().includes('geometry') ? 'geometry' : 'editors')}>{t('打开编辑器', 'Open editor')}</button></> : <p>{t('选择工程中的对象查看属性。', 'Select a project object to inspect its properties.')}</p>}{runtimeError && <details className="ras-runtime-error"><summary>{t('服务连接失败', 'Server connection failed')}</summary><p>{t('无法连接 C# 服务，请确认服务已启动且当前地址可访问。', 'Cannot connect to the C# server. Confirm that it is running and reachable.')}</p><pre>{runtimeError}</pre></details>}</aside>}
    </div>
    <footer className="ras-statusbar"><span>{dirtyOwners.size > 0 ? t('有未保存的草稿', 'Unsaved drafts') : t('就绪', 'Ready')}</span><span>{context.projectPath ?? t('无活动工程', 'No active project')}</span><span>{runtimeState === 'ready' ? t('C# 服务已连接', 'C# server connected') : runtimeState === 'loading' ? t('正在连接 C# 服务', 'Connecting to C# server') : t('C# 服务未连接', 'C# server disconnected')}</span></footer>
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
        if (!pendingContext || pendingContext.source !== identity(contextRef.current)) { setPendingContext(null); return; }
        dirtyOwnersRef.current = new Set(); setDirtyOwners(dirtyOwnersRef.current); commitContext(pendingContext.target); setPendingContext(null);
      }}
    />
    <FramedContentDialog open={showHelp} title="HEC-RAS 2025" onCancel={() => setShowHelp(false)}><p>{t('在工程页选择工程文件，在模型、几何、地图和结果页编辑与查看数据。界面在浏览器与桌面使用同一套 React 组件。', 'Select a project in Project, then use the model, geometry, map and result pages. Browser and desktop share the same React interface.')}</p><p>{t('需要计算能力的操作会根据当前可用功能启用，工程数据编辑与计算会通过关联服务处理。', 'Operations that need compute are enabled according to available capabilities. Project-data editing and computation are handled by the connected service.')}</p><div className="ras-dialog-actions"><button autoFocus onClick={() => setShowHelp(false)}>{t('关闭', 'Close')}</button></div></FramedContentDialog>
  </main>;
}
