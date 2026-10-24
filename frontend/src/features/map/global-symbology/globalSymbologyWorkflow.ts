export interface GlobalSymbologyValues {
  penColor: string;
  penWidth: number;
  fillColor: string;
  pointSize: number;
  haloColor: string;
  haloThickness: number;
  textEmphasis: number;
}

export type GlobalSymbologyProperty = keyof GlobalSymbologyValues;

export interface GlobalSymbologyDefinition {
  definitionID: string;
  name: string;
  layerName: string;
  vectorType: string;
  editableProperties: GlobalSymbologyProperty[];
  defaultValues: GlobalSymbologyValues;
  values: GlobalSymbologyValues;
  savedValues: GlobalSymbologyValues;
  isCustomized: boolean;
}

export interface GlobalSymbologyGroup {
  layerName: string;
  options: GlobalSymbologyDefinition[];
}

export interface GlobalSymbologySnapshot {
  revision: string;
  groups: GlobalSymbologyGroup[];
}

export interface GlobalSymbologyStyleInput {
  definitionID: string;
  values: GlobalSymbologyValues;
}

export const globalSymbologyMethods = {
  inspect: 'global.symbology.inspect',
  save: 'global.symbology.save',
} as const;

const properties = new Set<GlobalSymbologyProperty>([
  'penColor', 'penWidth', 'fillColor', 'pointSize', 'haloColor', 'haloThickness', 'textEmphasis',
]);

function recordValue(value: unknown, description: string): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    throw new Error(`${description} 格式无效。`);
  }
  return value as Record<string, unknown>;
}

function finiteNumber(value: unknown, description: string): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) throw new Error(`${description} 数值无效。`);
  return value;
}

function argbColor(value: unknown, description: string): string {
  if (typeof value !== 'string' || !/^#[0-9a-f]{8}$/i.test(value)) {
    throw new Error(`${description} 必须是 #AARRGGBB 颜色。`);
  }
  return value.toUpperCase();
}

function parseValues(value: unknown, description: string): GlobalSymbologyValues {
  const item = recordValue(value, description);
  return {
    penColor: argbColor(item.penColor, `${description} PenColor`),
    penWidth: finiteNumber(item.penWidth, `${description} PenWidth`),
    fillColor: argbColor(item.fillColor, `${description} FillColor`),
    pointSize: finiteNumber(item.pointSize, `${description} PointSize`),
    haloColor: argbColor(item.haloColor, `${description} HaloColor`),
    haloThickness: finiteNumber(item.haloThickness, `${description} HaloThickness`),
    textEmphasis: finiteNumber(item.textEmphasis, `${description} TextEmphasis`),
  };
}

function parseProperty(value: unknown): GlobalSymbologyProperty {
  if (typeof value !== 'string') throw new Error('Host 返回了无效的全局符号属性。');
  const property = (value.charAt(0).toLowerCase() + value.slice(1)) as GlobalSymbologyProperty;
  if (!properties.has(property)) throw new Error('Host 返回了未知的全局符号属性。');
  return property;
}

export function parseGlobalSymbologySnapshot(value: unknown): GlobalSymbologySnapshot {
  const response = recordValue(value, '全局矢量符号响应');
  if (typeof response.revision !== 'string' || !/^[0-9a-f]{64}$/i.test(response.revision) ||
      !Array.isArray(response.groups)) {
    throw new Error('Host 返回的全局矢量符号修订或分组无效。');
  }
  const seenIDs = new Set<string>();
  const groups = response.groups.map((entry): GlobalSymbologyGroup => {
    const group = recordValue(entry, '全局矢量符号分组');
    if (typeof group.layerName !== 'string' || !Array.isArray(group.options)) {
      throw new Error('Host 返回了不完整的全局矢量符号分组。');
    }
    const options = group.options.map((optionValue): GlobalSymbologyDefinition => {
      const option = recordValue(optionValue, '全局矢量符号定义');
      if (typeof option.definitionID !== 'string' || option.definitionID.trim() === '' ||
          typeof option.name !== 'string' || typeof option.vectorType !== 'string' ||
          typeof option.isCustomized !== 'boolean' || !Array.isArray(option.editableProperties)) {
        throw new Error('Host 返回了不完整的全局矢量符号定义。');
      }
      if (seenIDs.has(option.definitionID)) throw new Error('Host 返回了重复的全局矢量符号定义。');
      seenIDs.add(option.definitionID);
      const values = parseValues(option.values, `${option.definitionID} 当前值`);
      return {
        definitionID: option.definitionID,
        name: option.name,
        layerName: group.layerName as string,
        vectorType: option.vectorType,
        editableProperties: option.editableProperties.map(parseProperty),
        defaultValues: parseValues(option.defaultValues, `${option.definitionID} 默认值`),
        values,
        savedValues: { ...values },
        isCustomized: option.isCustomized,
      };
    });
    return { layerName: group.layerName as string, options };
  });
  return { revision: response.revision, groups };
}

export function toGlobalSymbologySaveStyles(groups: GlobalSymbologyGroup[]): GlobalSymbologyStyleInput[] {
  return groups.flatMap(group => group.options.map(option => ({
    definitionID: option.definitionID,
    values: { ...option.values },
  })));
}

export function sameGlobalSymbologyValues(left: GlobalSymbologyValues, right: GlobalSymbologyValues): boolean {
  return left.penColor === right.penColor && left.penWidth === right.penWidth &&
    left.fillColor === right.fillColor && left.pointSize === right.pointSize &&
    left.haloColor === right.haloColor && left.haloThickness === right.haloThickness &&
    left.textEmphasis === right.textEmphasis;
}

export function isGlobalSymbologyDirty(groups: GlobalSymbologyGroup[]): boolean {
  return groups.some(group => group.options.some(option =>
    !sameGlobalSymbologyValues(option.values, option.savedValues)));
}

export function resetGlobalSymbology(groups: GlobalSymbologyGroup[]): GlobalSymbologyGroup[] {
  return groups.map(group => ({
    ...group,
    options: group.options.map(option => ({
      ...option,
      values: { ...option.defaultValues },
    })),
  }));
}

export function updateGlobalSymbologyValue(
  groups: GlobalSymbologyGroup[], definitionID: string,
  change: Partial<GlobalSymbologyValues>,
): GlobalSymbologyGroup[] {
  return groups.map(group => ({
    ...group,
    options: group.options.map(option => option.definitionID === definitionID
      ? { ...option, values: { ...option.values, ...change } }
      : option),
  }));
}

export function globalSymbologyPropertyLabel(property: GlobalSymbologyProperty, language: 'zh' | 'en'): string {
  const labels: Record<GlobalSymbologyProperty, [string, string]> = {
    penColor: ['线条颜色', 'Pen color'],
    penWidth: ['线宽', 'Line width'],
    fillColor: ['填充颜色', 'Fill color'],
    pointSize: ['点大小', 'Point size'],
    haloColor: ['文字光晕颜色', 'Text halo color'],
    haloThickness: ['文字光晕宽度', 'Text halo width'],
    textEmphasis: ['文字强调值', 'Text emphasis'],
  };
  return labels[property][language === 'zh' ? 0 : 1];
}

export function propertyUsesColor(property: GlobalSymbologyProperty): boolean {
  return property === 'penColor' || property === 'fillColor' || property === 'haloColor';
}
