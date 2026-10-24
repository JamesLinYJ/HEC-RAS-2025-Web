import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import type { RecentProjectRecord } from './ProjectTypes';
import { DetailedRecentProjectPreview } from './DetailedRecentProjectPreview';
import { RecentProjectStatistic } from './RecentProjectStatistic';

interface RecentProjectsListProps {
  language: Language;
  projects: RecentProjectRecord[];
  busy: boolean;
  onProjectClick: (project: RecentProjectRecord) => void;
  onClear: () => void;
}

function openedAtLabel(value: number, language: Language): string {
  const date = new Date(value);
  return Number.isFinite(date.getTime())
    ? date.toLocaleString(language === 'zh' ? 'zh-CN' : 'en-US')
    : text(language, '时间不可用', 'Time unavailable');
}

/** Recent-project view backed only by the validated localStorage records owned by ProjectExplorerControl. */
export function RecentProjectsList({ language, projects, busy, onProjectClick, onClear }: RecentProjectsListProps) {
  return (
    <section className="project-recent-section">
      <div className="project-section-heading">
        <div>
          <div className="project-kicker">{text(language, '最近', 'RECENT')}</div>
          <h2>{text(language, '最近工程', 'Recent projects')}</h2>
        </div>
        <div className="project-recent-heading-actions">
          <RecentProjectStatistic language={language} value={projects.length} />
          {projects.length > 0 && <button type="button" className="project-text-button" onClick={onClear}>{text(language, '清除列表', 'Clear list')}</button>}
        </div>
      </div>

      {projects.length === 0 ? (
        <div className="project-recent-empty"><span>◇</span><strong>{text(language, '暂无最近工程', 'No recent projects')}</strong><p>{text(language, '成功打开服务器工程后，它会出现在这里。', 'Projects appear here after they have been opened successfully from the server.')}</p></div>
      ) : (
        <>
          <DetailedRecentProjectPreview language={language} project={projects[0]} busy={busy} onOpen={onProjectClick} />
          <ul className="project-recent-list">
            {projects.map((project) => (
              <li key={project.id}>
                <button type="button" className="project-recent-item" disabled={busy} onClick={() => onProjectClick(project)}>
                  <span className="project-recent-file-icon">RAS</span>
                  <span className="project-recent-info"><strong>{project.fileName}</strong><small title={project.projectPath}>{project.projectPath} · {openedAtLabel(project.openedAt, language)}</small></span>
                  <span className="project-recent-reselect">{text(language, '重新打开', 'Reopen')} ↗</span>
                </button>
              </li>
            ))}
          </ul>
        </>
      )}
      <p className="project-recents-note">{text(language, '最近列表只保存本机浏览器中已成功打开过的服务器工程路径；工程目录统计未由当前 Host 契约提供。', 'Recents contain server project paths successfully opened by this browser. The current Host contract does not provide project-directory statistics.')}</p>
    </section>
  );
}
