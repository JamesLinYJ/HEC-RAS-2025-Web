import { useState } from 'react';
import type { Language, ThemeMode } from '../../shared/workbench';
import { text } from '../../shared/workbench';

interface ProjectExplorerSettingsPageProps {
  language: Language;
  themeMode: ThemeMode;
  onThemeModeChange?: (mode: ThemeMode) => void;
  recentProjectCount: number;
  onClearRecentProjects: () => void;
  busy: boolean;
  onClose: () => void;
}

/** Web settings that have real local state or existing Project Explorer callbacks. */
export function ProjectExplorerSettingsPage({ language, themeMode, onThemeModeChange, recentProjectCount, onClearRecentProjects, busy, onClose }: ProjectExplorerSettingsPageProps) {
  const [confirmClear, setConfirmClear] = useState(false);

  return <section className="project-settings-page" aria-labelledby="project-settings-title">
    <header className="project-settings-header">
      <div>
        <div className="project-kicker">{text(language, '工程管理 / 设置', 'PROJECT EXPLORER / SETTINGS')}</div>
        <h2 id="project-settings-title">{text(language, '工程设置', 'Project settings')}</h2>
      </div>
      <button type="button" className="project-button project-button-secondary" onClick={onClose}>{text(language, '返回', 'Back')}</button>
    </header>

    <section className="project-settings-section" aria-labelledby="project-theme-label">
      <label htmlFor="project-theme-mode" id="project-theme-label">{text(language, '应用主题', 'Application theme')}</label>
      <p>{text(language, '“跟随系统”会随当前浏览器或桌面系统的浅色/深色外观自动切换。', 'System follows the current browser or desktop operating system light/dark appearance.')}</p>
      <select id="project-theme-mode" value={themeMode} disabled={!onThemeModeChange} onChange={(event) => onThemeModeChange?.(event.currentTarget.value as ThemeMode)}>
        <option value="light">{text(language, '浅色', 'Light')}</option>
        <option value="dark">{text(language, '深色', 'Dark')}</option>
        <option value="system">{text(language, '跟随系统', 'System')}</option>
      </select>
    </section>

    <section className="project-settings-section" aria-labelledby="project-recent-settings-label">
      <div className="project-settings-section-heading">
        <div>
          <h3 id="project-recent-settings-label">{text(language, '最近工程', 'Recent projects')}</h3>
          <p>{text(language, '清除本机此应用保存的最近工程列表，不会修改工程文件。', 'Clear this app’s local recent-project list. Project files are not changed.')}</p>
        </div>
        <span className="project-settings-count">{recentProjectCount}</span>
      </div>
      {!confirmClear ? <button type="button" className="project-button project-button-secondary" disabled={busy || recentProjectCount === 0} onClick={() => setConfirmClear(true)}>{text(language, '清除最近工程', 'Clear recent projects')}</button> : <div className="project-settings-confirm" role="group" aria-label={text(language, '确认清除最近工程', 'Confirm clearing recent projects')}>
        <span>{text(language, `将清除 ${recentProjectCount} 条最近记录。`, `This will clear ${recentProjectCount} recent record(s).`)}</span>
        <button type="button" className="project-button project-button-secondary" onClick={() => setConfirmClear(false)}>{text(language, '取消', 'Cancel')}</button>
        <button type="button" className="project-button project-button-primary" disabled={busy} onClick={() => { onClearRecentProjects(); setConfirmClear(false); }}>{text(language, '确认清除', 'Clear')}</button>
      </div>}
    </section>

    <p className="project-settings-note">{text(language, '默认服务器工程目录和凭据尚无可用的 Web 设置操作。', 'Default server project-directory and credential settings are not available in this web interface.')}</p>
  </section>;
}
