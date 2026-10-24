import type { ReactNode } from 'react';
import type { FeatureId, Language } from '../../shared/workbench';

export interface WorkspaceLayoutProps {
  language: Language;
  activeFeature: FeatureId;
  onLanguageChange: (language: Language) => void;
  onNavigate?: (feature: FeatureId) => void;
  navigation?: ReactNode;
  menuBar?: ReactNode;
  toolbar?: ReactNode;
  projectTree?: ReactNode;
  mapCanvas?: ReactNode;
  inspector?: ReactNode;
  projectTitle?: ReactNode;
  statusBar?: ReactNode;
  children?: ReactNode;
}

const labels = {
  zh: {
    project: '工程树',
    map: '地图', inspector: '属性检查器', noProject: '尚未打开工程',
    projectHint: '打开工程后，工程对象与结果将显示在此处。',
    mapHint: '打开工程或地图后，地图内容将在此显示。',
    inspectorHint: '选择工程对象后查看其属性。',
    projectState: '当前工程', ready: '就绪', workspace: '工作区', compute: '计算验证',
    language: 'English', appName: 'HEC-RAS 2025',
  },
  en: {
    project: 'Project tree',
    map: 'Map', inspector: 'Properties', noProject: 'No project open',
    projectHint: 'Project objects and results appear here after opening a project.',
    mapHint: 'Map content appears here after opening a project or map.',
    inspectorHint: 'Select a project object to inspect its properties.',
    projectState: 'Project', ready: 'Ready', workspace: 'Workspace', compute: 'Compute proof',
    language: '简体中文', appName: 'HEC-RAS 2025',
  },
} satisfies Record<Language, Record<string, string>>;

function EmptyPanel({ title, message, kind, language }: { title: string; message: string; kind: 'project' | 'map' | 'inspector'; language: Language }) {
  return (
    <section className={`workspace-panel workspace-${kind}`}>
      <div className="workspace-panel-title">{title}</div>
      <div className="workspace-empty">
        {kind === 'map' ? <div className="empty-map-symbol" aria-hidden="true"><span /><span /><span /></div> : <div className={`empty-symbol empty-symbol-${kind}`} aria-hidden="true">{kind === 'project' ? '◇' : '◈'}</div>}
        {kind === 'project' && <strong>{language === 'zh' ? '尚未打开工程' : 'No project open'}</strong>}
        <p>{message}</p>
      </div>
    </section>
  );
}

export function WorkspaceLayout({
  language,
  activeFeature,
  onLanguageChange,
  onNavigate,
  navigation,
  menuBar,
  toolbar,
  projectTree,
  mapCanvas,
  inspector,
  projectTitle,
  statusBar,
  children,
}: WorkspaceLayoutProps) {
  const t = labels[language];
  const onCompute = activeFeature === 'compute';

  return (
    <main className="workspace-shell">
      <header className="workspace-topbar">
        <div className="workspace-brand"><span className="workspace-brand-mark" aria-hidden="true"><i /><i /><i /></span><strong>{t.appName}</strong></div>
        <nav className="workspace-nav" aria-label={language === 'zh' ? '主导航' : 'Main navigation'}>
          {navigation ?? <>
            <button className={!onCompute ? 'nav-selected' : ''} onClick={() => onNavigate?.('project')}>{t.workspace}</button>
            <button className={onCompute ? 'nav-selected' : ''} onClick={() => onNavigate?.('compute')}>{t.compute}</button>
          </>}
        </nav>
        {projectTitle && <div className="workspace-project-title">{projectTitle}</div>}
        <div className="workspace-top-actions">
          <span className="workspace-platform"><span />{window.hecDesktop?.isElectron ? (language === 'zh' ? '桌面版' : 'Desktop') : (language === 'zh' ? '网页版' : 'Web')}</span>
          <button className="workspace-language" onClick={() => onLanguageChange(language === 'zh' ? 'en' : 'zh')}>{t.language}</button>
        </div>
      </header>

      {!onCompute ? (
        <>
          <div className="workspace-toolbar-row">
            <div className="workspace-menu-strip">{menuBar}</div>
            <div className="workspace-toolbar-content">{toolbar}</div>
          </div>
          <div className="workspace-content">
            {projectTree ?? <EmptyPanel kind="project" title={t.project} message={t.projectHint} language={language} />}
            {mapCanvas ?? <EmptyPanel kind="map" title={t.map} message={t.mapHint} language={language} />}
            {inspector ?? <EmptyPanel kind="inspector" title={t.inspector} message={t.inspectorHint} language={language} />}
          </div>
          <footer className="workspace-statusbar">
            <span><i className="status-ready-dot" />{statusBar ?? t.ready}</span>
            {projectTitle && <span>{t.projectState}: {projectTitle}</span>}
          </footer>
        </>
      ) : <section className="compute-route">{children}</section>}
    </main>
  );
}
