import type { Language } from '../../../shared/workbench';
import type { RasterSymbologyPalette } from './rasterSymbologyWorkflow';
import { ColorPaletteControl } from './ColorPaletteControl';

interface Props {
  language: Language;
  palettes: RasterSymbologyPalette[];
  selectedId: string | null;
  disabled: boolean;
  onSelect: (paletteID: string) => void;
}

export function ColorPalettePicker({ language, palettes, selectedId, disabled, onSelect }: Props) {
  const zh = language === 'zh';
  const selected = palettes.find(palette => palette.id === selectedId) ?? null;
  return (
    <div className="raster-symbology-picker">
      <label className="raster-symbology-field">
        <span>{zh ? '当前颜色带' : 'Selected color palette'}</span>
        <select value={selected?.id ?? ''} onChange={event => onSelect(event.target.value)} disabled={disabled}>
          <option value="">{zh ? '选择颜色带' : 'Choose a palette'}</option>
          {palettes.map(palette => <option key={palette.id} value={palette.id}>{palette.name}</option>)}
        </select>
      </label>
      {selected && <ColorPaletteControl palette={selected} className="raster-symbology-picker-preview" />}
    </div>
  );
}
