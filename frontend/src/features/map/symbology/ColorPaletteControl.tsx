import type { RasterSymbologyPalette } from './rasterSymbologyWorkflow';
import { isHexColor } from './rasterSymbologyWorkflow';

type PalettePreview = Pick<RasterSymbologyPalette, 'name' | 'colors'>;

interface Props {
  palette: PalettePreview;
  orientation?: 'horizontal' | 'vertical';
  className?: string;
}

/** Renders the Host palette's actual colors as a continuous, evenly-spaced ramp. */
export function ColorPaletteControl({ palette, orientation = 'horizontal', className }: Props) {
  const colors = palette.colors.filter(isHexColor).map(color => `#${color.trim().replace(/^#/, '')}`);
  const rampColors = colors.length === 1 ? [colors[0], colors[0]] : colors;
  const direction = orientation === 'vertical' ? 'to top' : 'to right';
  const backgroundImage = rampColors.length > 1
    ? `linear-gradient(${direction}, ${rampColors.map((color, index) =>
      `${color} ${index / (rampColors.length - 1) * 100}%`).join(', ')})`
    : undefined;

  return (
    <span
      className={['raster-symbology-palette-control', className].filter(Boolean).join(' ')}
      role="img"
      aria-label={palette.name}
      title={palette.name}
      style={backgroundImage ? { backgroundImage } : undefined}
    />
  );
}
