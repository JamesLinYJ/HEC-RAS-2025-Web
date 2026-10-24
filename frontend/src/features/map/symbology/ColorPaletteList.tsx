import type { Language } from '../../../shared/workbench';
import type { RasterSymbologyPalette } from './rasterSymbologyWorkflow';
import { colorInputValue } from './rasterSymbologyWorkflow';

interface Props {
  language: Language;
  palettes: RasterSymbologyPalette[];
  selectedId: string | null;
  disabled: boolean;
  deletingId: string | null;
  onSelect: (paletteID: string) => void;
  onDelete: (palette: RasterSymbologyPalette) => void;
  onCancelDelete: () => void;
}

export function ColorPaletteList({ language, palettes, selectedId, disabled, deletingId, onSelect, onDelete, onCancelDelete }: Props) {
  const zh = language === 'zh';
  const defaults = palettes.filter(palette => !palette.isCustom);
  const custom = palettes.filter(palette => palette.isCustom);
  const renderPalette = (palette: RasterSymbologyPalette) => (
    <div className={`raster-symbology-palette ${selectedId === palette.id ? 'is-selected' : ''}`} key={palette.id}>
      <button type="button" className="raster-symbology-palette-pick" onClick={() => onSelect(palette.id)} disabled={disabled}>
        <span className="raster-symbology-palette-ramp" aria-hidden="true">
          {palette.colors.map((color, index) => <i key={`${palette.id}-${index}`} style={{ backgroundColor: colorInputValue(color) }} />)}
        </span>
        <span className="raster-symbology-palette-name">{palette.name}</span>
      </button>
      {palette.isCustom && <div className="raster-symbology-delete-actions">
        <button type="button" className="raster-symbology-icon-button is-danger" onClick={() => onDelete(palette)} disabled={disabled}>
          {deletingId === palette.id ? (zh ? '确认删除' : 'Confirm delete') : (zh ? '删除' : 'Delete')}
        </button>
        {deletingId === palette.id && <button type="button" className="raster-symbology-icon-button" onClick={onCancelDelete} disabled={disabled}>{zh ? '取消' : 'Cancel'}</button>}
      </div>}
    </div>
  );

  return (
    <div className="raster-symbology-palette-list">
      <section>
        <h4>{zh ? '默认颜色带' : 'Default palettes'}</h4>
        {defaults.length > 0 ? <div className="raster-symbology-palette-grid">{defaults.map(renderPalette)}</div>
          : <p className="raster-symbology-muted">{zh ? 'Host 未返回默认颜色带。' : 'The Host returned no default palettes.'}</p>}
      </section>
      <section>
        <h4>{zh ? '自定义颜色带' : 'Custom palettes'}</h4>
        {custom.length > 0 ? <div className="raster-symbology-palette-grid">{custom.map(renderPalette)}</div>
          : <p className="raster-symbology-muted">{zh ? '尚无已保存的自定义颜色带。' : 'No saved custom palettes.'}</p>}
      </section>
    </div>
  );
}
