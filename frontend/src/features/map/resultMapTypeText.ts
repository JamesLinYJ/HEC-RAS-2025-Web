export type ResultMapTextLanguage = 'zh' | 'en';

interface MapHoverLabelInput {
  /** Structured source kind returned by the map identify endpoint. */
  kind?: string | null;
  /** Original serialized or enum map type; never changed by this formatter. */
  mapType?: string | null;
  /** Original sampled value, accepted for call-site completeness and never reformatted. */
  value?: string | null;
  /** Host-generated label shown to the user. */
  label?: string | null;
}

const mapTypeLabels = new Map<string, string>([
  ['depth', '水深'],
  ['watersurface', '水面高程'],
  ['watersurfaceelevation', '水面高程'],
  ['wse', '水面高程'],
  ['ws', '水面高程'],
  ['velocity', '流速'],
  ['vel', '流速'],
  ['v', '流速'],
  ['d*v', '水深×流速'],
  ['dv', '水深×流速'],
  ['depthtimesvelocity', '水深×流速'],
  ['depthvelocity', '水深×流速'],
  ['depth*velocity', '水深×流速'],
  ['d*v^2', '水深×流速²'],
  ['depthtimesvelocitysquared', '水深×流速²'],
  ['depth*velocity^2', '水深×流速²'],
]);

function normalizedMapType(value: string | null | undefined): string | null {
  if (typeof value !== 'string' || !value.trim()) return null;
  return value.toLowerCase().replace(/\s+/g, '');
}

/** Localizes only map type identifiers known from the Renamed C# serializers. */
export function resultMapTypeText(language: ResultMapTextLanguage, mapType: string | null | undefined): string {
  if (typeof mapType !== 'string' || language !== 'zh') return mapType ?? '';
  return mapTypeLabels.get(normalizedMapType(mapType) ?? '') ?? mapType;
}

/**
 * Localizes the final standard field token in Host hover labels while keeping
 * the project/result name, numeric text, unit, and source metadata untouched.
 */
export function mapHoverEntryText(language: ResultMapTextLanguage, input: MapHoverLabelInput): string {
  const label = input.label ?? '';
  if (language !== 'zh' || !label) return label;

  const finalField = /^(.*[—–-]\s*)([^:]+)(\s*:.*)$/s.exec(label);
  if (!finalField) return label;

  const field = finalField[2].trim();
  if (input.kind === 'terrain') {
    if (field !== 'Terrain') return label;
    return `${finalField[1]}地形高程${finalField[3]}`;
  }
  if (input.kind !== 'result') return label;

  const sourceText = resultMapTypeText(language, input.mapType);
  const fieldText = resultMapTypeText(language, field);
  if (sourceText === input.mapType || fieldText === field || sourceText !== fieldText) return label;
  const localized = fieldText;
  return `${finalField[1]}${localized}${finalField[3]}`;
}
