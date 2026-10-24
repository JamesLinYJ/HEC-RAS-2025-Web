import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import type { RecentProjectRecord } from './ProjectTypes';

interface DetailedRecentProjectPreviewProps {
  language: Language;
  project: RecentProjectRecord;
  busy: boolean;
  onOpen: (project: RecentProjectRecord) => void;
}

export function DetailedRecentProjectPreview({ language, project, busy, onOpen }: DetailedRecentProjectPreviewProps) {
  const openedDate = new Date(project.openedAt);
  const openedAtIsValid = Number.isFinite(project.openedAt) && Number.isFinite(openedDate.getTime());
  const openedAt = openedAtIsValid
    ? openedDate.toLocaleString(language === 'zh' ? 'zh-CN' : 'en-US')
    : text(language, '时间不可用', 'Time unavailable');

  return (
    <article className="project-recent-preview">
      <div className="project-recent-preview-heading">
        <span className="project-file-mark" aria-hidden="true">RAS</span>
        <div>
          <div className="project-kicker">{text(language, '最近打开', 'MOST RECENT')}</div>
          <h3>{project.fileName}</h3>
          <time dateTime={openedAtIsValid ? openedDate.toISOString() : undefined}>{openedAt}</time>
        </div>
        <button type="button" className="project-button project-button-secondary" disabled={busy} onClick={() => onOpen(project)}>
          {text(language, '重新打开', 'Reopen')}
        </button>
      </div>
      <dl className="project-recent-preview-facts">
        <div><dt>{text(language, '服务器路径', 'Server path')}</dt><dd><code title={project.projectPath}>{project.projectPath}</code></dd></div>
        <div><dt>{text(language, '打开时的工程修订', 'Project revision at open')}</dt><dd><code title={project.revision}>{project.revision}</code></dd></div>
      </dl>
    </article>
  );
}
