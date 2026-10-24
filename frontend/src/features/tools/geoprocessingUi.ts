export type ProjectionOption = 'Project' | 'WGS84' | 'WebMercator' | 'Custom';

export interface ProjectionDefinitionRequest {
  type: string;
  value: string;
}

export interface CoordinateRowDraft {
  id: string;
  xText: string;
  yText: string;
}

const knownProjectProjectionTypes = new Set(['EPSG', 'Proj', 'WKT', 'ESRI', 'Local']);
let coordinateRowCounter = 0;

export function createCoordinateRows(count: number): CoordinateRowDraft[] {
  return Array.from({ length: count }, () => {
    coordinateRowCounter += 1;
    const id = globalThis.crypto?.randomUUID?.() ?? `coordinate-row-${Date.now()}-${coordinateRowCounter}`;
    return { id, xText: '', yText: '' };
  });
}

/** Read the same Project/Projection Type attribute and text value written by ProjectionDefinition.Serialize. */
export function readProjectProjection(xml: string | null): ProjectionDefinitionRequest | null {
  if (!xml || typeof DOMParser === 'undefined') return null;
  const document = new DOMParser().parseFromString(xml, 'application/xml');
  if (document.querySelector('parsererror')) return null;
  const project = document.getElementsByTagName('Project').item(0);
  const projection = project?.getElementsByTagName('Projection').item(0);
  const type = projection?.getAttribute('Type')?.trim() ?? '';
  const value = projection?.textContent?.trim() ?? '';
  if (!knownProjectProjectionTypes.has(type) || !value) return null;
  return { type, value };
}

export function projectionOptionLabel(option: ProjectionOption, language: 'zh' | 'en'): string {
  if (language !== 'zh') return option;
  return {
    Project: '工程投影',
    WGS84: 'WGS 84',
    WebMercator: 'Web Mercator',
    Custom: '自定义',
  }[option];
}

export function formatCoordinate(value: number | null | undefined, language: 'zh' | 'en'): string {
  if (typeof value !== 'number' || !Number.isFinite(value)) return '—';
  return new Intl.NumberFormat(language === 'zh' ? 'zh-CN' : 'en-US', {
    maximumSignificantDigits: 15,
  }).format(value);
}
