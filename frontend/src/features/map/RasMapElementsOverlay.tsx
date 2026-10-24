import type { ReactNode } from 'react';
import type { Language } from '../../shared/workbench';
import type { AppliedRasterLegend } from './symbology/RasterSymbologyEditor';
import { RasMapLegend } from './RasMapLegend';
import './mapOverlay.css';

export interface RasMapElementsOverlayProps {
  legend: AppliedRasterLegend | null;
  language: Language;
  /** Controlled by the map display settings. */
  visible?: boolean;
  children?: ReactNode;
}

/** Map-side overlay host for controls such as the applied raster legend. */
export function RasMapElementsOverlay({ legend, language, visible = true, children }: RasMapElementsOverlayProps) {
  if (!visible) return null;
  return (
    <div className="ras-map-elements-overlay" aria-label={language === 'zh' ? '地图叠加控件' : 'Map overlays'}>
      {children}
      <RasMapLegend legend={legend} language={language} />
    </div>
  );
}
