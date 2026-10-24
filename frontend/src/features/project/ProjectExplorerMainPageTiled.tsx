import { useState } from 'react';
import type { BackendGateway, Language, ThemeMode, WorkbenchContext } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import type { RecentProjectRecord, SelectedProjectFile } from './ProjectTypes';
import { FirstLaunchScreen } from './FirstLaunchScreen';
import { RecentProjectsList } from './RecentProjectsList';
import { ExampleAndSyntheticDatasetsPage } from './ExampleAndSyntheticDatasetsPage';
import { ProjectExplorerSettingsPage } from './ProjectExplorerSettingsPage';

interface ProjectExplorerMainPageTiledProps {
  language: Language;
  themeMode?: ThemeMode;
  onThemeModeChange?: (mode: ThemeMode) => void;
  backend: BackendGateway;
  context: WorkbenchContext;
  backendRevision?: number;
  currentProject: SelectedProjectFile | null;
  externalProjectPath?: string | null;
  recents: RecentProjectRecord[];
  busy?: boolean;
  importBusy?: boolean;
  importBlockedReason?: string;
  onOpen: () => void;
  onNew: () => void;
  onOpenRecent: (recent: RecentProjectRecord) => void;
  onClearRecents: () => void;
  onImportBusyChange: (busy: boolean) => void;
  onOpenLibraryProject: (projectPath: string, source: WorkbenchContext) => Promise<boolean>;
  modelCreationDisabledReason?: string;
  onCreateModel?: (kind: 'newGeometry' | 'newPlan' | 'newBoundary') => void;
}

function formatSize(bytes: number, language: Language): string {
  if (bytes < 1024) return `${bytes} ${text(language, '字节', 'B')}`;
  const unit = bytes < 1024 * 1024 ? 'KB' : 'MB';
  const divisor = unit === 'KB' ? 1024 : 1024 * 1024;
  return `${(bytes / divisor).toFixed(bytes < divisor * 10 ? 1 : 0)} ${unit}`;
}

export function ProjectExplorerMainPageTiled({
  language,
  themeMode = 'system',
  onThemeModeChange,
  backend,
  context,
  backendRevision,
  currentProject,
  externalProjectPath,
  recents,
  busy = false,
  importBusy = false,
  importBlockedReason,
  onOpen,
  onNew,
  onOpenRecent,
  onClearRecents,
  onImportBusyChange,
  onOpenLibraryProject,
  modelCreationDisabledReason,
  onCreateModel,
}: ProjectExplorerMainPageTiledProps) {
  const [showProjectLibrary, setShowProjectLibrary] = useState(false);
  const [showSettings, setShowSettings] = useState(false);
  if (showSettings) {
    return <ProjectExplorerSettingsPage
      language={language}
      themeMode={themeMode}
      onThemeModeChange={onThemeModeChange}
      recentProjectCount={recents.length}
      onClearRecentProjects={onClearRecents}
      busy={busy || importBusy}
      onClose={() => setShowSettings(false)}
    />;
  }
  if (showProjectLibrary) {
    return <ExampleAndSyntheticDatasetsPage
      language={language}
      backend={backend}
      context={context}
      backendRevision={backendRevision}
      busy={busy || importBusy}
      importBlockedReason={importBlockedReason}
      onBack={() => setShowProjectLibrary(false)}
      onBusyChange={onImportBusyChange}
      onOpenProjectPath={async (projectPath, source) => {
        const opened = await onOpenLibraryProject(projectPath, source);
        if (opened) setShowProjectLibrary(false);
        return opened;
      }}
    />;
  }

  if (currentProject || externalProjectPath) {
    const fileName = currentProject?.fileName ?? externalProjectPath?.split(/[\\/]/).pop() ?? '';
    return (
      <section className="project-open-summary">
        <div className="project-summary-heading">
          <span className="project-file-mark" aria-hidden="true">RAS</span>
          <div className="project-summary-title">
            <div className="project-kicker">{text(language, '当前工程', 'CURRENT PROJECT')}</div>
            <h1>{fileName}</h1>
            <p>{currentProject ? text(language, '由 C# 工程模型从服务器路径实际打开', 'Opened by C# Project from the server path') : text(language, '工程路径来自当前工作台会话', 'Project path from the current workbench session')}</p>
          </div>
          <div className="project-summary-actions">
            <button type="button" className="project-button project-button-secondary" onClick={() => setShowSettings(true)}>{text(language, '设置', 'Settings')}</button>
            {backend.supports('projectLibrary.catalog') && <button type="button" className="project-button project-button-secondary" disabled={busy || importBusy} onClick={() => setShowProjectLibrary(true)}>{text(language, '示例工程', 'Examples')}</button>}
            {onCreateModel && <div className="project-summary-model-actions" aria-label={text(language, '创建工程模型', 'Create project models')}>
              <button type="button" className="project-button project-button-secondary" disabled={busy || importBusy || Boolean(modelCreationDisabledReason)} title={modelCreationDisabledReason} onClick={() => onCreateModel('newGeometry')}>{text(language, '新建几何', 'New geometry')}</button>
              <button type="button" className="project-button project-button-secondary" disabled={busy || importBusy || Boolean(modelCreationDisabledReason)} title={modelCreationDisabledReason} onClick={() => onCreateModel('newPlan')}>{text(language, '新建方案', 'New plan')}</button>
              <button type="button" className="project-button project-button-secondary" disabled={busy || importBusy || Boolean(modelCreationDisabledReason)} title={modelCreationDisabledReason} onClick={() => onCreateModel('newBoundary')}>{text(language, '新建边界条件', 'New boundary condition')}</button>
            </div>}
            <button type="button" className="project-button project-button-secondary" disabled={busy || importBusy} onClick={onOpen}>{text(language, '切换工程', 'Switch project')}</button>
            <button type="button" className="project-button project-button-primary" disabled={busy || importBusy} onClick={onNew}>{text(language, '新建工程', 'New project')}</button>
          </div>
        </div>
        {currentProject ? <>
          <div className="project-file-facts">
            <div><span>{text(language, '文件大小', 'File size')}</span><strong>{formatSize(currentProject.byteLength, language)}</strong></div>
            <div><span>{text(language, '修改时间', 'Last modified')}</span><strong>{currentProject.lastModified ? new Date(currentProject.lastModified).toLocaleString(language === 'zh' ? 'zh-CN' : 'en-US') : text(language, '服务器未返回', 'Not returned by server')}</strong></div>
            <div><span>{text(language, 'XML 语法', 'XML syntax')}</span><strong className={currentProject.isWellFormedXml ? 'project-value-good' : 'project-value-warning'}>{currentProject.isWellFormedXml ? text(language, '格式良好', 'Well formed') : text(language, '格式错误', 'Malformed')}</strong></div>
            <div><span>{text(language, '源修订 SHA-256', 'Source revision SHA-256')}</span><strong className="project-mono">{currentProject.digest ? `${currentProject.digest.slice(0, 12)}…` : text(language, '缺失', 'Missing')}</strong></div>
          </div>
          <div className="project-parse-state">
            <span className="project-parse-icon">{currentProject.isWellFormedXml ? 'i' : '!'}</span>
            <p>{currentProject.isWellFormedXml
              ? text(language, 'C# 工程模型已从服务器路径成功读取 XML。工程对象与目录树仍由后端服务解析，没有从 XML 标签自行推断。', 'C# Project successfully read the project XML from the server path. Project objects and the catalog tree are still parsed by the C# services; they are not inferred from XML labels.')
              : text(language, '后端返回的工程 XML 语法无效。当前内容保留用于诊断；请检查服务器上的文件。', 'The project XML returned by C# Project is malformed. It is retained for diagnostics; inspect the server-side file.')}</p>
          </div>
        </> : <div className="project-detail-path"><span>{text(language, '工程路径', 'Project path')}</span><code>{externalProjectPath}</code></div>}
      </section>
    );
  }

  return (
    <section className="project-explorer-home">
      <div className="project-explorer-home-settings"><button type="button" className="project-button project-button-secondary" onClick={() => setShowSettings(true)}>{text(language, '设置', 'Settings')}</button></div>
      <FirstLaunchScreen
        language={language}
        busy={busy || importBusy}
        canBrowseExamples={backend.supports('projectLibrary.catalog')}
        onOpen={onOpen}
        onNew={onNew}
        onBrowseExamples={() => setShowProjectLibrary(true)}
      />
      <RecentProjectsList language={language} projects={recents} busy={busy || importBusy} onProjectClick={onOpenRecent} onClear={onClearRecents} />
    </section>
  );
}
