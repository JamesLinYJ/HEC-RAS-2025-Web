import type { Language } from '../../../shared/workbench';
import type { ToolGroupDefinition } from '../GeoprocessingToolBase';

export function ToolGroupButton({ group, language, count, expanded, onToggle }: {
  group: ToolGroupDefinition;
  language: Language;
  count: number;
  expanded: boolean;
  onToggle: () => void;
}) {
  const zh = language === 'zh';
  return <button type="button" className={`gp-control-group-button ${expanded ? 'is-expanded' : ''}`}
    aria-expanded={expanded} onClick={onToggle}>
    <span className="gp-control-group-chevron" aria-hidden="true">{expanded ? '⌄' : '›'}</span>
    <strong>{group.title[language]}</strong>
    <small>{count} {zh ? '个工具' : count === 1 ? 'tool' : 'tools'}</small>
  </button>;
}
