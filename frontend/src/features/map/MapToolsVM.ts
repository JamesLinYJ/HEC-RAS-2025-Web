/** Map tool identities exposed by MapToolsVM.cs and the React map controls. */
export type MapToolID = 'select' | 'pan' | 'drawPoint' | 'drawLine' | 'drawPolygon' | 'measureLength' | 'measureArea';

export interface MapToolDescriptor {
  id: MapToolID;
  label: string;
  title: string;
  glyph: string;
}

export const MAP_TOOL_DESCRIPTORS: readonly MapToolDescriptor[] = [
  { id: 'select', label: 'Select', title: 'Select a feature', glyph: '↖' },
  { id: 'pan', label: 'Pan', title: 'Pan the map', glyph: '✥' },
  { id: 'drawPoint', label: 'Point', title: 'Draw a point draft', glyph: '●' },
  { id: 'drawLine', label: 'Line', title: 'Draw a line draft', glyph: '╱' },
  { id: 'drawPolygon', label: 'Area', title: 'Draw an area draft', glyph: '⬠' },
  { id: 'measureLength', label: 'Length', title: 'Measure length in source coordinate units', glyph: '↔' },
  { id: 'measureArea', label: 'Area', title: 'Measure area in source coordinate units', glyph: '▱' },
];
