import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';

interface FirstLaunchScreenProps {
  language: Language;
  busy: boolean;
  canBrowseExamples: boolean;
  onOpen: () => void;
  onNew: () => void;
  onBrowseExamples: () => void;
}

/** Start screen actions corresponding to the retained first-launch open/new choices. */
export function FirstLaunchScreen({ language, busy, canBrowseExamples, onOpen, onNew, onBrowseExamples }: FirstLaunchScreenProps) {
  return (
    <>
      <header className="project-home-hero">
        <div className="project-home-copy">
          <div className="project-kicker">{text(language, 'HEC-RAS 2025 · 工程管理器', 'HEC-RAS 2025 · PROJECT EXPLORER')}</div>
          <h1>{text(language, '工程管理器', 'Project Explorer')}</h1>
          <p>{text(language, '打开已有工程、创建新工程，或浏览 Host 返回的示例工程。最近工程只显示实际成功打开过的文件。', 'Open an existing project, create a new one, or browse examples returned by the Host. Recent projects include only files that were actually opened.')}</p>
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
        {canBrowseExamples && <button type="button" className="project-home-action" disabled={busy} onClick={onBrowseExamples}>
          <span className="project-action-symbol">◇</span><strong>{text(language, '示例工程', 'Example projects')}</strong><small>{text(language, '浏览 Host 工程库中的实际条目', 'Browse actual entries in the Host project library')}</small>
        </button>}
      </div>
    </>
  );
}
