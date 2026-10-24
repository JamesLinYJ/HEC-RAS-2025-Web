import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import type { ProjectTreeCategory, ProjectTreeModeName } from './ProjectTreeModeVM';

export interface RasCatalogTreeModeProps {
  language: Language;
  mode: ProjectTreeModeName;
  category: ProjectTreeCategory;
  query: string;
  onModeChange: (mode: ProjectTreeModeName) => void;
  onCategoryChange: (category: ProjectTreeCategory) => void;
  onQueryChange: (query: string) => void;
  onExpandAll: (expand: boolean) => void;
}

/** Catalog mode selector and its existing tree filters and expand controls. */
export function RasCatalogTreeMode({
  language,
  mode,
  category,
  query,
  onModeChange,
  onCategoryChange,
  onQueryChange,
  onExpandAll,
}: RasCatalogTreeModeProps) {
  return (
    <>
      <header className="project-tree-toolbar">
        <div className="project-tree-mode-switch" role="tablist" aria-label={text(language, '工程树模式', 'Project tree mode')}>
          <button type="button" role="tab" aria-selected={mode === 'project'} className={mode === 'project' ? 'is-active' : ''} onClick={() => onModeChange('project')}>{text(language, '工程树', 'Project tree')}</button>
          <button type="button" role="tab" aria-selected={mode === 'catalog'} className={mode === 'catalog' ? 'is-active' : ''} onClick={() => onModeChange('catalog')}>{text(language, '目录', 'Catalog')}</button>
        </div>
        <div className="project-tree-search"><span>⌕</span><input aria-label={text(language, '筛选工程对象', 'Filter project items')} value={query} onChange={(event) => onQueryChange(event.currentTarget.value)} placeholder={text(language, '筛选…', 'Filter…')} /><button type="button" aria-label={text(language, '清除筛选', 'Clear filter')} onClick={() => onQueryChange('')} disabled={!query}>×</button></div>
        <button type="button" className="project-tree-tool-button" onClick={() => onExpandAll(true)}>{text(language, '全部展开', 'Expand all')}</button>
        <button type="button" className="project-tree-tool-button" onClick={() => onExpandAll(false)}>{text(language, '全部折叠', 'Collapse all')}</button>
      </header>

      <nav className="project-tree-categories" aria-label={text(language, '工程分组', 'Project categories')}>
        {([
          ['all', text(language, '全部', 'All')],
          ['geometries', text(language, '几何', 'Geometries')],
          ['terrains', text(language, '地形', 'Terrains')],
          ['boundaries', text(language, '边界', 'Boundaries')],
          ['plans', text(language, '计划', 'Plans')],
          ['results', text(language, '结果', 'Results')],
        ] as const).map(([key, label]) => <button type="button" key={key} className={category === key ? 'is-active' : ''} onClick={() => onCategoryChange(key)}>{label}</button>)}
      </nav>
    </>
  );
}
