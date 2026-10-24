export type VectorGeometryType = 'point' | 'line' | 'polygon';
export type VectorSymbologyProperty = 'PenColor' | 'PenWidth' | 'FillColor' | 'PointSize';

export interface VectorSymbologyColumn {
  id: string;
  name: string;
  type: string;
  ordinal: number;
}

export interface VectorSymbologyBucket {
  geometryType: VectorGeometryType;
  renderFeatureCount: number;
  tableRowCount: number;
  ordinalBindingProven: boolean;
  sourceFeatureIndices: number[];
  supportedDefinitionIDs: string[];
  columns: VectorSymbologyColumn[];
  hasAttributeTable: boolean;
}

export interface VectorSymbologyInspection {
  revision: string;
  sourceFeatureCount: number;
  buckets: VectorSymbologyBucket[];
}

export interface VectorSymbologyCondition {
  column: string;
  operator: string;
  threshold: string;
  resultValue: string;
}

export interface VectorSymbologyConditionDraft extends VectorSymbologyCondition {
  id: string;
}

export interface VectorSymbologyPropertyConditions {
  definitionID: string;
  property: VectorSymbologyProperty;
  conditions: VectorSymbologyCondition[];
}

export interface VectorSymbologyRuleDraft {
  id: string;
  definitionID: string;
  property: VectorSymbologyProperty;
  conditions: VectorSymbologyConditionDraft[];
}

export interface VectorSymbologyOverride {
  sourceFeatureIndex: number;
  definitionID: string;
  property: VectorSymbologyProperty;
  hasMatch: boolean;
  colorValue: string | null;
  numberValue: number | null;
  integerValue: number | null;
}

export interface VectorSymbologyEvaluation {
  revision: string;
  overrides: VectorSymbologyOverride[];
}

export interface VectorSymbologyEvaluationExpectation {
  revision: string;
  sourceFeatureIndices: number[];
  properties: Pick<VectorSymbologyPropertyConditions, 'definitionID' | 'property'>[];
}

export const vectorSymbologyMethods = {
  inspect: 'vector.symbology.inspect',
  evaluate: 'vector.symbology.evaluate',
} as const;

export const conditionOperators = {
  numeric: ['==', '!=', '>', '>=', '<', '<='],
  string: ['equals', 'not equals', 'contains', 'starts with', 'ends with'],
} as const;

const propertiesByGeometry: Record<VectorGeometryType, VectorSymbologyProperty[]> = {
  point: ['PenColor', 'PenWidth', 'FillColor', 'PointSize'],
  line: ['PenColor', 'PenWidth'],
  polygon: ['PenColor', 'PenWidth', 'FillColor'],
};

export function supportedProperties(geometryType: VectorGeometryType): VectorSymbologyProperty[] {
  return [...propertiesByGeometry[geometryType]];
}

export function operatorsForColumn(columnType: string): readonly string[] {
  if (columnType === 'string') return conditionOperators.string;
  if (['int32', 'int64', 'float32', 'float64'].includes(columnType)) return conditionOperators.numeric;
  return [];
}

export function isSupportedColumnType(columnType: string): boolean {
  return operatorsForColumn(columnType).length > 0;
}

export function parseVectorSymbologyInspection(value: unknown): VectorSymbologyInspection {
  if (!isRecord(value) || !isRevision(value.revision) || !isNonNegativeInteger(value.sourceFeatureCount)
    || !Array.isArray(value.buckets)) {
    throw new Error('C# Host 返回的矢量符号检查结构无效。');
  }
  const revision = value.revision;
  const sourceFeatureCount = value.sourceFeatureCount;
  const rawBuckets = value.buckets;
  const seenGeometry = new Set<string>();
  const allIndices = new Set<number>();
  const buckets = rawBuckets.map((item): VectorSymbologyBucket => {
    if (!isRecord(item) || !isGeometryType(item.geometryType)
      || !isNonNegativeInteger(item.renderFeatureCount) || !isNonNegativeInteger(item.tableRowCount)
      || typeof item.ordinalBindingProven !== 'boolean' || !Array.isArray(item.sourceFeatureIndices)
      || !Array.isArray(item.supportedDefinitionIDs) || !Array.isArray(item.columns)
      || typeof item.hasAttributeTable !== 'boolean') {
      throw new Error('C# Host 返回的矢量几何分组结构无效。');
    }
    if (seenGeometry.has(item.geometryType)) throw new Error('C# Host 重复返回了矢量几何分组。');
    seenGeometry.add(item.geometryType);

    const sourceFeatureIndices = item.sourceFeatureIndices.map(index => {
      if (!isNonNegativeInteger(index) || index >= sourceFeatureCount || allIndices.has(index)) {
        throw new Error('C# Host 返回的原始 GeoJSON 要素索引无效或重复。');
      }
      allIndices.add(index);
      return index;
    });
    const supportedDefinitionIDs = uniqueStrings(item.supportedDefinitionIDs, '矢量符号定义 ID');
    const columnOrdinals = new Set<number>();
    const columns = item.columns.map((column): VectorSymbologyColumn => {
      if (!isRecord(column) || typeof column.id !== 'string' || typeof column.name !== 'string'
        || typeof column.type !== 'string' || !isNonNegativeInteger(column.ordinal)
        || columnOrdinals.has(column.ordinal)) {
        throw new Error('C# Host 返回的矢量属性列结构无效。');
      }
      columnOrdinals.add(column.ordinal);
      return { id: column.id, name: column.name, type: column.type, ordinal: column.ordinal };
    });
    if (item.ordinalBindingProven && (item.renderFeatureCount !== item.tableRowCount
      || item.renderFeatureCount !== sourceFeatureIndices.length)) {
      throw new Error('C# Host 的原始要素序号绑定证明与行数不一致。');
    }
    return {
      geometryType: item.geometryType,
      renderFeatureCount: item.renderFeatureCount,
      tableRowCount: item.tableRowCount,
      ordinalBindingProven: item.ordinalBindingProven,
      sourceFeatureIndices,
      supportedDefinitionIDs,
      columns,
      hasAttributeTable: item.hasAttributeTable,
    };
  });
  return { revision, sourceFeatureCount, buckets };
}

export function parseVectorSymbologyEvaluation(
  value: unknown,
  expectation: VectorSymbologyEvaluationExpectation,
): VectorSymbologyEvaluation {
  if (!isRecord(value) || !isRevision(value.revision) || !Array.isArray(value.overrides)
    || !sameRevision(value.revision, expectation.revision)) {
    throw new Error('C# Host 返回的矢量条件求值结果与当前源修订不匹配。');
  }
  const expectedKeys = new Set<string>();
  for (const sourceFeatureIndex of expectation.sourceFeatureIndices) {
    for (const property of expectation.properties) {
      expectedKeys.add(overrideKey(sourceFeatureIndex, property.definitionID, property.property));
    }
  }
  if (value.overrides.length !== expectedKeys.size) {
    throw new Error('C# Host 返回的矢量条件结果数量与本次请求不匹配。');
  }
  const seen = new Set<string>();
  const overrides = value.overrides.map((item): VectorSymbologyOverride => {
    if (!isRecord(item) || !isNonNegativeInteger(item.sourceFeatureIndex)
      || typeof item.definitionID !== 'string' || !isVectorProperty(item.property)
      || typeof item.hasMatch !== 'boolean'
      || !(item.colorValue === null || typeof item.colorValue === 'string')
      || !(item.numberValue === null || (typeof item.numberValue === 'number' && Number.isFinite(item.numberValue)))
      || !(item.integerValue === null || isSafeInteger(item.integerValue))) {
      throw new Error('C# Host 返回的矢量条件覆写结构无效。');
    }
    const key = overrideKey(item.sourceFeatureIndex, item.definitionID, item.property);
    if (!expectedKeys.has(key) || seen.has(key)) throw new Error('C# Host 返回了未请求或重复的矢量条件覆写。');
    seen.add(key);
    const values = [item.colorValue, item.numberValue, item.integerValue];
    if (!item.hasMatch && values.some(candidate => candidate !== null)) {
      throw new Error('C# Host 的未匹配条件结果包含覆写值。');
    }
    if (item.hasMatch && !hasExpectedOverrideValue(item.property, item.colorValue, item.numberValue, item.integerValue)) {
      throw new Error('C# Host 的匹配条件结果缺少对应属性值。');
    }
    return {
      sourceFeatureIndex: item.sourceFeatureIndex,
      definitionID: item.definitionID,
      property: item.property,
      hasMatch: item.hasMatch,
      colorValue: item.colorValue,
      numberValue: item.numberValue,
      integerValue: item.integerValue,
    };
  });
  return { revision: value.revision, overrides };
}

export function parseSourceFeatureIndices(input: string): number[] {
  const trimmed = input.trim();
  if (!trimmed) return [];
  const tokens = trimmed.split(/[\s,;]+/).filter(Boolean);
  if (tokens.length > 500_000) throw new Error('单次最多选择 500000 个原始 GeoJSON 要素序号。');
  const indices: number[] = [];
  const seen = new Set<number>();
  for (const token of tokens) {
    if (!/^\d+$/.test(token)) throw new Error(`要素序号“${token}”不是非负整数；请直接输入 Host 返回的原始序号。`);
    const index = Number(token);
    if (!isNonNegativeInteger(index)) throw new Error(`要素序号“${token}”超出安全整数范围。`);
    if (seen.has(index)) throw new Error(`原始要素序号 ${index} 重复。`);
    seen.add(index);
    indices.push(index);
  }
  return indices;
}

export function validateSourceFeatureSelection(input: string, bucket: VectorSymbologyBucket): number[] {
  if (!bucket.ordinalBindingProven) throw new Error('Host 未证明属性表行与原始 GeoJSON 要素序号一一对应，当前分组不能求值。');
  if (!bucket.hasAttributeTable) throw new Error('当前几何分组没有可用的源属性表。');
  const indices = parseSourceFeatureIndices(input);
  if (indices.length === 0) throw new Error('请输入一个或多个原始 GeoJSON 要素序号。');
  const valid = new Set(bucket.sourceFeatureIndices);
  const unknown = indices.filter(index => !valid.has(index));
  if (unknown.length > 0) throw new Error(`Host 当前 ${bucket.geometryType} 分组不包含这些原始序号：${unknown.slice(0, 12).join(', ')}`);
  return indices;
}

export function isCompleteCondition(condition: VectorSymbologyCondition): boolean {
  return condition.column.length > 0 && condition.operator.length > 0
    && condition.threshold.length > 0 && condition.resultValue.length > 0;
}

export function conditionColumnChoices(columns: VectorSymbologyColumn[]): VectorSymbologyColumn[] {
  const nameCounts = new Map<string, number>();
  for (const column of columns) nameCounts.set(column.name, (nameCounts.get(column.name) ?? 0) + 1);
  return columns.filter(column => nameCounts.get(column.name) === 1 && isSupportedColumnType(column.type));
}

export function vectorGeometryLabel(geometryType: VectorGeometryType, language: 'zh' | 'en'): string {
  if (language === 'en') return geometryType;
  return geometryType === 'point' ? '点' : geometryType === 'line' ? '线' : '面';
}

export function vectorPropertyLabel(property: VectorSymbologyProperty, language: 'zh' | 'en'): string {
  if (language === 'en') return property;
  switch (property) {
    case 'PenColor': return '边线颜色';
    case 'PenWidth': return '边线宽度';
    case 'FillColor': return '填充颜色';
    case 'PointSize': return '点大小';
  }
}

export function colorValueToCss(value: string | null): string | null {
  if (!value) return null;
  const hex = value.trim().replace(/^#/, '');
  if (/^[\da-f]{8}$/i.test(hex)) {
    const alpha = Number.parseInt(hex.slice(0, 2), 16) / 255;
    return `rgba(${Number.parseInt(hex.slice(2, 4), 16)}, ${Number.parseInt(hex.slice(4, 6), 16)}, ${Number.parseInt(hex.slice(6, 8), 16)}, ${alpha.toFixed(3)})`;
  }
  if (/^[\da-f]{6}$/i.test(hex)) return `#${hex}`;
  return null;
}

export function sameRevision(left: string, right: string): boolean {
  return left.toLocaleUpperCase('en-US') === right.toLocaleUpperCase('en-US');
}

function hasExpectedOverrideValue(
  property: VectorSymbologyProperty,
  colorValue: string | null,
  numberValue: number | null,
  integerValue: number | null,
): boolean {
  switch (property) {
    case 'PenColor':
    case 'FillColor': return colorValue !== null && numberValue === null && integerValue === null;
    case 'PenWidth': return colorValue === null && numberValue !== null && integerValue === null;
    case 'PointSize': return colorValue === null && numberValue === null && integerValue !== null;
  }
  return false;
}

function uniqueStrings(value: unknown[], label: string): string[] {
  if (!value.every(item => typeof item === 'string' && item.length > 0)) throw new Error(`C# Host 返回的${label}无效。`);
  const strings = value as string[];
  if (new Set(strings).size !== strings.length) throw new Error(`C# Host 返回的${label}重复。`);
  return strings;
}

function overrideKey(sourceFeatureIndex: number, definitionID: string, property: string): string {
  return JSON.stringify([sourceFeatureIndex, definitionID, property]);
}

function isGeometryType(value: unknown): value is VectorGeometryType {
  return value === 'point' || value === 'line' || value === 'polygon';
}

function isVectorProperty(value: unknown): value is VectorSymbologyProperty {
  return value === 'PenColor' || value === 'PenWidth' || value === 'FillColor' || value === 'PointSize';
}

function isRevision(value: unknown): value is string {
  return typeof value === 'string' && /^[\da-f]{64}$/i.test(value);
}

function isNonNegativeInteger(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
}

function isSafeInteger(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
