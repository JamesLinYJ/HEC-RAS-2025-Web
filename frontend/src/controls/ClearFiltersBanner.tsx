import type { Language } from '../shared/workbench';

/** Text and ClearClicked action from ClearFiltersBanner.cs. */
export function ClearFiltersBanner({ language, active, description, onClear }: {
  language: Language;
  active: boolean;
  description: string;
  onClear: () => void;
}) {
  if (!active) return null;
  return <div className="project-clear-filters" role="status">
    <span>{description}</span>
    <button type="button" className="project-tree-tool-button" onClick={onClear}>
      {language === 'zh' ? '清除全部筛选' : 'Clear all filters'}
    </button>
  </div>;
}
