import type { CSSProperties } from 'react';

const paths = {
  water: 'M2 8c2.5-3 4.5 3 7 0s4.5 3 7 0 4.5 3 6 0M2 14c2.5-3 4.5 3 7 0s4.5 3 7 0 4.5 3 6 0M2 20c2.5-3 4.5 3 7 0s4.5 3 7 0 4.5 3 6 0',
  rain: 'M6 13a4 4 0 0 1 0-8 6 6 0 0 1 11.4 1A3.5 3.5 0 0 1 18 13M7 16l-1 3m6-3-1 3m6-3-1 3',
  layers: 'm12 3 10 6-10 6L2 9l10-6ZM2 13l10 6 10-6M2 17l10 6 10-6',
  play: 'm8 5 11 7-11 7V5Z',
  pause: 'M8 5v14M16 5v14',
  reset: 'M3 10a9 9 0 1 1 2 8M3 4v6h6',
  arrow: 'M5 12h14m-5-5 5 5-5 5',
  back: 'M19 12H5m5-5-5 5 5 5',
  chevron: 'm8 5 7 7-7 7',
  chevronDown: 'm6 9 6 6 6-6',
  terrain: 'm2 19 7-13 4 7 3-5 6 11H2Zm4-7 3 1 2-2',
  dam: 'M3 20 6 4h12l3 16M4 15h16M8 4l-1 11m9-11 1 11M10 20v-5h4v5',
  clock: 'M12 7v5l3 2M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z',
  check: 'm5 12 4 4L19 6',
  cube: 'm12 2 9 5v10l-9 5-9-5V7l9-5Zm-9 5 9 5 9-5m-9 5v10',
  settings: 'M4 6h16M4 12h16M4 18h16M8 3v6m8 0v6M10 15v6',
  expand: 'M8 3H3v5m13-5h5v5M3 16v5h5m13-5v5h-5',
  eye: 'M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12Zm13 0a3 3 0 1 1-6 0 3 3 0 0 1 6 0Z',
  info: 'M12 11v6m0-10v.2M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z',
  download: 'M12 3v12m-5-5 5 5 5-5M4 16v5h16v-5',
  close: 'm6 6 12 12M6 18 18 6',
  plus: 'M12 5v14M5 12h14',
  minus: 'M5 12h14',
} as const;

export function FloodIcon({ name, size = 20, style }: { name: keyof typeof paths; size?: number; style?: CSSProperties }) {
  return <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" style={style}><path d={paths[name]} /></svg>;
}
