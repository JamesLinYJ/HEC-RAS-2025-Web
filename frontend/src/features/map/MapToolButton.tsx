import type { MapToolID } from './MapToolsVM';

/** Tool selection remains owned by the map. */
export function MapToolButton({ tool, label, title, glyph, selected, disabled = false, onSelect }: {
  tool: MapToolID; label: string; title: string; glyph: string; selected: boolean;
  disabled?: boolean; onSelect: (tool: MapToolID) => void;
}) {
  return <button type="button" className={'map-tool-button' + (selected ? ' is-active' : '')}
    title={title} aria-label={title} aria-pressed={selected} disabled={disabled} onClick={() => onSelect(tool)}>
    <span aria-hidden="true">{glyph}</span><small>{label}</small>
  </button>;
}
