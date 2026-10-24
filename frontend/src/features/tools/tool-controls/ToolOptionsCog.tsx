import type { Language } from '../../../shared/workbench';

export function ToolOptionsCog({ language, expanded, onToggle }: {
  language: Language;
  expanded: boolean;
  onToggle: () => void;
}) {
  const zh = language === 'zh';
  return <button type="button" className={`gp-control-options-cog ${expanded ? 'is-expanded' : ''}`}
    aria-expanded={expanded} aria-controls="geoprocessing-tool-options" onClick={onToggle}>
    <span aria-hidden="true">⚙</span><span>{zh ? '参数选项' : 'Tool options'}</span>
  </button>;
}
