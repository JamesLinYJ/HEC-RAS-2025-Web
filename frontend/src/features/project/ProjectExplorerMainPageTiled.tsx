import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import type { RecentProjectRecord, SelectedProjectFile } from './ProjectTypes';

interface ProjectExplorerMainPageTiledProps {
  language: Language;
  currentProject: SelectedProjectFile | null;
  externalProjectPath?: string | null;
  recents: RecentProjectRecord[];
  busy?: boolean;
  onOpen: () => void;
  onNew: () => void;
  onOpenRecent: (recent: RecentProjectRecord) => void;
  onClearRecents: () => void;
}

function formatSize(bytes: number, language: Language): string {
  if (bytes < 1024) return `${bytes} ${text(language, '字节', 'B')}`;
  const unit = bytes < 1024 * 1024 ? 'KB' : 'MB';
  const divisor = unit === 'KB' ? 1024 : 1024 * 1024;
  return `${(bytes / divisor).toFixed(bytes < divisor * 10 ? 1 : 0)} ${unit}`;
}

export function ProjectExplorerMainPageTiled({ language, currentProject, externalProjectPath, recents, busy = false, onOpen, onNew, onOpenRecent, onClearRecents }: ProjectExplorerMainPageTiledProps) {
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
            <button type="button" className="project-button project-button-secondary" disabled={busy} onClick={onOpen}>{text(language, '切换工程', 'Switch project')}</button>
            <button type="button" className="project-button project-button-primary" disabled={busy} onClick={onNew}>{text(language, '新建工程', 'New project')}</button>
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
      <header className="project-home-hero">
        <div className="project-home-copy">
          <div className="project-kicker">{text(language, 'HEC-RAS 2025 · 工程管理器', 'HEC-RAS 2025 · PROJECT EXPLORER')}</div>
          <h1>{text(language, '工程管理器', 'Project Explorer')}</h1>
          <p>{text(language, '打开已有工程或创建新工程。最近工程仅显示你实际打开过的文件。', 'Open an existing project or start a new one. Recent projects include only files you have opened.')}</p>
        </div>
        <div className="project-home-emblem" aria-hidden="true"><span>RAS</span><i /><i /><i /></div>
      </header>
      <div className="project-home-actions">
        <button type="button" className="project-home-action" disabled={busy} onClick={onOpen}>
          <span className="project-action-symbol">↗</span><strong>{text(language, '打开工程', 'Open project')}</strong><small>{text(language, '输入服务器路径，或上传 .ras / 完整目录', 'Enter a server path or upload a .ras / full directory')}</small>
        </button>
        <button type="button" className="project-home-action" disabled={busy} onClick={onNew}>
          <span className="project-action-symbol">＋</span><strong>{text(language, '新建工程', 'New project')}</strong><small>{text(language, '设置标题、单位制和投影', 'Set title, units, and projection')}</small>
        </button>
      </div>

      <section className="project-recent-section">
        <div className="project-section-heading">
          <div><div className="project-kicker">{text(language, '最近', 'Recent')}</div><h2>{text(language, '最近工程', 'Recent projects')}</h2></div>
          {recents.length > 0 && <button type="button" className="project-text-button" onClick={onClearRecents}>{text(language, '清除列表', 'Clear list')}</button>}
        </div>
        {recents.length === 0 ? (
          <div className="project-recent-empty"><span>◇</span><strong>{text(language, '暂无最近工程', 'No recent projects')}</strong><p>{text(language, '成功打开服务器工程后，它会出现在这里。', 'Projects appear here after they have been opened successfully from the server.')}</p></div>
        ) : (
          <ul className="project-recent-list">
            {recents.map((recent) => (
              <li key={recent.id}>
                <button type="button" className="project-recent-item" disabled={busy} onClick={() => onOpenRecent(recent)}>
                  <span className="project-recent-file-icon">RAS</span>
                  <span className="project-recent-info"><strong>{recent.fileName}</strong><small title={recent.projectPath}>{recent.projectPath} · {new Date(recent.openedAt).toLocaleString(language === 'zh' ? 'zh-CN' : 'en-US')}</small></span>
                  <span className="project-recent-reselect">{text(language, '重新打开', 'Reopen')} ↗</span>
                </button>
              </li>
            ))}
          </ul>
        )}
        <p className="project-recents-note">{text(language, '最近列表只保存实际成功打开过的服务器工程路径。', 'Recents contain only server project paths that were actually opened successfully.')}</p>
      </section>
    </section>
  );
}
