import { useEffect, useRef } from 'react';
import type { FeatureId, Language } from '../shared/workbench';
import { text } from '../shared/workbench';

interface RasMenuBarProps {
  language: Language;
  onNavigate: (feature: FeatureId) => void;
  onProjectCommand: (kind: 'open' | 'new') => void;
  onCloseProject: () => void;
  hasProject: boolean;
  onToggleInspector: () => void;
  showInspector: boolean;
  onHelp: () => void;
}

/** Web counterpart of RasMenuBar.cs. Menu actions route through the current workspace. */
export function RasMenuBar({ language, onNavigate, onProjectCommand, onCloseProject, hasProject, onToggleInspector, showInspector, onHelp }: RasMenuBarProps) {
  const bar = useRef<HTMLDivElement>(null);
  const t = (zh: string, en: string) => text(language, zh, en);
  const closeMenus = () => bar.current?.querySelectorAll('details[open]').forEach((item) => item.removeAttribute('open'));
  useEffect(() => {
    const onOutside = (event: PointerEvent) => { if (!bar.current?.contains(event.target as Node)) closeMenus(); };
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape') closeMenus(); };
    document.addEventListener('pointerdown', onOutside);
    document.addEventListener('keydown', onKey);
    return () => { document.removeEventListener('pointerdown', onOutside); document.removeEventListener('keydown', onKey); };
  }, []);
  const navigate = (feature: FeatureId) => { closeMenus(); onNavigate(feature); };
  const projectCommand = (kind: 'open' | 'new') => {
    closeMenus();
    onProjectCommand(kind);
  };
  return <div className="ras-menubar" ref={bar} aria-label={t('主菜单', 'Main menu')}>
    <details><summary>{t('文件', 'File')}</summary><div className="ras-menu-popover">
      <button onClick={() => projectCommand('new')}>{t('新建工程…', 'New project…')}</button>
      <button onClick={() => projectCommand('open')}>{t('打开工程…', 'Open project…')}<kbd>⌘/Ctrl O</kbd></button>
      <hr /><button disabled={!hasProject} onClick={() => { closeMenus(); onCloseProject(); }}>{t('关闭工程', 'Close project')}</button>
    </div></details>
    <details><summary>{t('编辑', 'Edit')}</summary><div className="ras-menu-popover">
      <button onClick={() => navigate('editors')}>{t('模型属性', 'Model properties')}</button>
      <button onClick={() => navigate('geometry')}>{t('几何、网格与涵洞', 'Geometry, mesh and culverts')}</button>
    </div></details>
    <details><summary>{t('视图', 'View')}</summary><div className="ras-menu-popover">
      <button onClick={() => navigate('map')}>{t('地图', 'Map')}</button>
      <button onClick={() => navigate('results')}>{t('结果与时间序列', 'Results and time series')}</button>
      <hr /><button aria-pressed={showInspector} onClick={() => { closeMenus(); onToggleInspector(); }}>{showInspector ? '✓ ' : ''}{t('属性面板', 'Properties panel')}</button>
    </div></details>
    <details><summary>{t('工具', 'Tools')}</summary><div className="ras-menu-popover">
      <button onClick={() => navigate('tools')}>{t('地理处理与导入导出', 'Geoprocessing and import/export')}</button>
      <button onClick={() => navigate('compute')}>{t('计算', 'Compute')}</button>
    </div></details>
    <button className="ras-menu-help" onClick={onHelp}>{t('帮助', 'Help')}</button>
  </div>;
}
