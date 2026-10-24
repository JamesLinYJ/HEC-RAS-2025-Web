import type { Language } from '../../../shared/workbench';
import type { GeoprocessingToolDefinition } from '../GeoprocessingToolBase';
import { ToolNodeButton } from './ToolNodeButton';

export function ToolRichFlyoutControl({ title, language, tools, activeToolId, onSelect }: {
  title: string;
  language: Language;
  tools: GeoprocessingToolDefinition[];
  activeToolId: string;
  onSelect: (tool: GeoprocessingToolDefinition) => void;
}) {
  const zh = language === 'zh';
  return <div className="gp-control-rich-flyout" role="group" aria-label={title}>
    {tools.map(tool => <ToolNodeButton key={tool.id} tool={tool} language={language}
      selected={activeToolId === tool.id} onSelect={() => onSelect(tool)} />)}
    {!tools.length && <p className="tools-muted">{zh ? '没有匹配的工具。' : 'No matching tools.'}</p>}
  </div>;
}
