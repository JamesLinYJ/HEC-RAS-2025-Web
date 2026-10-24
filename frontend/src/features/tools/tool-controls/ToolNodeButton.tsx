import type { Language } from '../../../shared/workbench';
import { displayToolOption, type GeoprocessingToolDefinition } from '../GeoprocessingToolBase';

export function ToolNodeButton({ tool, language, selected, onSelect }: {
  tool: GeoprocessingToolDefinition;
  language: Language;
  selected: boolean;
  onSelect: () => void;
}) {
  return <button type="button" className={`gp-control-tool-button ${selected ? 'is-selected' : ''}`}
    aria-pressed={selected} onClick={onSelect}>
    <span className={`gp-control-tool-icon is-${tool.outputKind.toLowerCase()}`} aria-hidden="true">
      {tool.outputKind === 'Raster' ? '▧' : tool.outputKind === 'Polygon' ? '⬡' : tool.outputKind === 'Text' ? '≡' : '⌁'}
    </span>
    <span className="gp-control-tool-copy"><strong>{tool.title[language]}</strong>
      <small>{tool.description[language]}</small>
      <span>{displayToolOption(tool.inputKind, language)} <span aria-hidden="true">→</span> {displayToolOption(tool.outputKind, language)}</span></span>
    <span className="gp-control-tool-check" aria-hidden="true">{selected ? '✓' : ''}</span>
  </button>;
}
