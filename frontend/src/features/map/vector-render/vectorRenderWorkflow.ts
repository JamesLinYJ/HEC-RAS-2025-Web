import { parseGlobalSymbologySnapshot } from '../global-symbology/globalSymbologyWorkflow';
import type { GlobalSymbologySnapshot } from '../global-symbology/globalSymbologyWorkflow';
import type { CatalogTreeNodeLocator } from '../mapTypes';
import type {
  MapVectorPlotStyle,
  VectorLayerSource,
  VectorLayerSymbologySnapshot,
  VectorPlotOptionDescriptor,
  VectorProjectCondition,
  VectorStyleProperty,
  VectorStyleValues,
  VectorSymbologyDefinition,
} from './vectorRenderTypes';

export const vectorLayerSymbologyMethods = {
  inspect: 'map.vector.symbology.inspect',
  save: 'map.vector.symbology.save',
  globalInspect: 'global.symbology.inspect',
} as const;

export type VectorStyleGeometry = 'point' | 'line' | 'polygon' | 'text' | 'unknown';

const styleProperties = new Set<VectorStyleProperty>([
  'penColor', 'penWidth', 'fillColor', 'pointSize', 'haloColor', 'haloThickness', 'textEmphasis',
]);

function record(value: unknown, label: string): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    throw new Error(`${label} 格式无效。`);
  }
  return value as Record<string, unknown>;
}

function text(value: unknown, label: string): string {
  if (typeof value !== 'string') throw new Error(`${label} 文本无效。`);
  return value;
}

function sha256(value: unknown, label: string): string {
  if (typeof value !== 'string' || !/^[\da-f]{64}$/i.test(value)) {
    throw new Error(`${label} SHA-256 无效。`);
  }
  return value;
}

function finite(value: unknown, label: string): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) throw new Error(`${label} 数值无效。`);
  return value;
}

function nullableText(value: unknown, label: string): string | null {
  if (value === null) return null;
  return text(value, label);
}

function parseStyleValues(value: unknown, label: string): VectorStyleValues {
  const item = record(value, label);
  const color = (candidate: unknown, property: string): string => {
    if (typeof candidate !== 'string' || !/^#[\da-f]{8}$/i.test(candidate)) {
      throw new Error(`${label} ${property} 必须是 #AARRGGBB 颜色。`);
    }
    return candidate.toUpperCase();
  };
  const pointSize = finite(item.pointSize, `${label} PointSize`);
  const textEmphasis = finite(item.textEmphasis, `${label} TextEmphasis`);
  if (!Number.isInteger(pointSize) || !Number.isInteger(textEmphasis)) {
    throw new Error(`${label} 点大小或文字强调值无效。`);
  }
  return {
    penColor: color(item.penColor, 'PenColor'),
    penWidth: finite(item.penWidth, `${label} PenWidth`),
    fillColor: color(item.fillColor, 'FillColor'),
    pointSize,
    haloColor: color(item.haloColor, 'HaloColor'),
    haloThickness: finite(item.haloThickness, `${label} HaloThickness`),
    textEmphasis,
  };
}

function parseStyles(value: unknown): MapVectorPlotStyle[] {
  if (!Array.isArray(value) || value.length === 0) throw new Error('服务端没有返回可用的矢量符号定义。');
  const seen = new Set<string>();
  return value.map((entry, index) => {
    const item = record(entry, `矢量符号 ${index + 1}`);
    const definitionID = text(item.definitionID, `矢量符号 ${index + 1} DefinitionID`);
    if (!definitionID.trim() || seen.has(definitionID)) throw new Error('服务端返回了空或重复的矢量符号定义。');
    seen.add(definitionID);
    if (item.isProjectOverride !== null && typeof item.isProjectOverride !== 'boolean') {
      throw new Error(`${definitionID} 的项目覆盖来源标记无效。`);
    }
    if (!Array.isArray(item.sourceOverrides)) throw new Error(`${definitionID} 的来源条件覆写列表无效。`);
    const sourceOverrides = item.sourceOverrides.map((overrideValue, overrideIndex) => {
      const override = record(overrideValue, `${definitionID} 来源覆写 ${overrideIndex + 1}`);
      if (!Number.isInteger(override.sourceFeatureIndex) || (override.sourceFeatureIndex as number) < 0) {
        throw new Error(`${definitionID} 的来源要素序号无效。`);
      }
      return {
        sourceFeatureIndex: override.sourceFeatureIndex as number,
        values: parseStyleValues(override.values, `${definitionID} 来源覆写 ${overrideIndex + 1}`),
      };
    });
    return {
      definitionID,
      base: parseStyleValues(item.base, `${definitionID} 基础样式`),
      sourceOverrides,
      isProjectOverride: item.isProjectOverride as boolean | null,
    };
  });
}

function parsePlotOptions(value: unknown): VectorPlotOptionDescriptor[] {
  if (!Array.isArray(value)) throw new Error('服务端返回的绘图选项无效。');
  const seen = new Set<string>();
  return value.map((entry, index) => {
    const item = record(entry, `绘图选项 ${index + 1}`);
    const propertyName = text(item.propertyName, `绘图选项 ${index + 1} 属性名`);
    if (!propertyName.trim() || seen.has(propertyName)) throw new Error('服务端返回了空或重复的绘图选项。');
    seen.add(propertyName);
    if (typeof item.isEnabled !== 'boolean') throw new Error(`${propertyName} 的启用状态无效。`);
    if (!(item.enabledIf === null || typeof item.enabledIf === 'string')) throw new Error(`${propertyName} 的条件字段无效。`);
    if (!Array.isArray(item.enumValues)) throw new Error(`${propertyName} 的选项列表无效。`);
    const enumValues = item.enumValues.map((choiceValue, choiceIndex) => {
      const choice = record(choiceValue, `${propertyName} 选项 ${choiceIndex + 1}`);
      return { value: text(choice.value, `${propertyName} 选项值`), label: text(choice.label, `${propertyName} 选项名称`) };
    });
    const kind = text(item.kind, `${propertyName} 类型`);
    const currentValue = text(item.value, `${propertyName} 当前值`);
    if (kind === 'bool' && currentValue !== 'true' && currentValue !== 'false') {
      throw new Error(`${propertyName} 的布尔值无效。`);
    }
    if (kind === 'enum' && !enumValues.some(choice => choice.value === currentValue)) {
      throw new Error(`${propertyName} 的当前枚举值不在服务端选项中。`);
    }
    return {
      propertyName,
      name: text(item.name, `${propertyName} 名称`),
      label: text(item.label, `${propertyName} 标签`),
      description: text(item.description, `${propertyName} 说明`),
      kind,
      value: currentValue,
      enumValues,
      isEnabled: item.isEnabled,
      enabledIf: item.enabledIf as string | null,
    };
  });
}

function parseProjectConditions(value: unknown): VectorProjectCondition[] {
  if (!Array.isArray(value)) throw new Error('服务端返回的项目条件符号无效。');
  return value.map((entry, index) => {
    const item = record(entry, `项目条件 ${index + 1}`);
    if (!Array.isArray(item.conditions)) throw new Error('项目条件列表无效。');
    return {
      definitionID: text(item.definitionID, '项目条件 DefinitionID'),
      property: text(item.property, '项目条件属性'),
      conditions: item.conditions.map((conditionValue, conditionIndex) => {
        const condition = record(conditionValue, `项目条件 ${index + 1}.${conditionIndex + 1}`);
        return {
          column: text(condition.column, '条件字段'),
          operator: text(condition.operator, '条件运算符'),
          threshold: text(condition.threshold, '条件阈值'),
          resultValue: text(condition.resultValue, '条件结果'),
        };
      }),
    };
  });
}

function parseDefinitions(value: unknown): VectorSymbologyDefinition[] {
  if (!Array.isArray(value) || value.length === 0) throw new Error('服务端没有返回矢量符号定义元数据。');
  const seen = new Set<string>();
  return value.map((entry, index) => {
    const item = record(entry, `矢量符号定义 ${index + 1}`);
    const definitionID = text(item.definitionID, `矢量符号定义 ${index + 1} 标识`);
    if (!definitionID.trim() || seen.has(definitionID)) throw new Error('服务端返回了空或重复的矢量符号定义元数据。');
    seen.add(definitionID);
    if (!Array.isArray(item.editableProperties)) throw new Error(`${definitionID} 的可编辑属性列表无效。`);
    const editableProperties = item.editableProperties.map((value, propertyIndex) => {
      const sourceName = text(value, `${definitionID} 可编辑属性 ${propertyIndex + 1}`);
      const property = (sourceName.charAt(0).toLowerCase() + sourceName.slice(1)) as VectorStyleProperty;
      if (!styleProperties.has(property)) throw new Error(`${definitionID} 返回了未知的可编辑属性 ${sourceName}。`);
      return property;
    });
    if (new Set(editableProperties).size !== editableProperties.length) {
      throw new Error(`${definitionID} 返回了重复的可编辑属性。`);
    }
    return {
      definitionID,
      name: text(item.name, `${definitionID} 名称`),
      layerName: text(item.layerName, `${definitionID} 图层名称`),
      vectorType: text(item.vectorType, `${definitionID} 矢量类型`),
      editableProperties,
      defaultValues: parseStyleValues(item.defaultValues, `${definitionID} 默认值`),
      globalValues: parseStyleValues(item.globalValues, `${definitionID} 全局值`),
    };
  });
}

function sameLocator(left: CatalogTreeNodeLocator, right: CatalogTreeNodeLocator): boolean {
  const keys: Array<keyof CatalogTreeNodeLocator> = [
    'rootName', 'nodeType', 'nodeName', 'assetID', 'mapType', 'filename', 'filenameRelative',
    'parentGeometryAssetID', 'parentGeometryNodeName', 'associationAssetID', 'associationKind',
    'parentPlanAssetID', 'parentPlanNodeName', 'parentResultNodeType', 'parentResultAssetID',
    'parentResultNodeName', 'parentResultRevision',
  ];
  if (keys.some(key => (left[key] ?? null) !== (right[key] ?? null))) return false;
  const arrays: Array<keyof CatalogTreeNodeLocator> = [
    'nodePath', 'parentGeometryNodePath', 'parentPlanNodePath', 'parentResultNodePath',
  ];
  return arrays.every(key => {
    const a = left[key] ?? [];
    const b = right[key] ?? [];
    return Array.isArray(a) && Array.isArray(b) && a.length === b.length && a.every((part, index) => part === b[index]);
  });
}

function parseSnapshot(value: unknown, source?: VectorLayerSource,
  allowVisualizationRevisionChange = false): VectorLayerSymbologySnapshot {
  const response = record(value, '矢量图层符号响应');
  const mapID = text(response.mapID, 'MapID');
  const layerID = text(response.layerID, '图层标识');
  const node = record(response.node, '目录节点定位');
  const projectPath = text(response.projectPath, '工程路径');
  const projectXmlSHA = nullableText(response.projectXmlSHA, '工程 XML SHA-256');
  const projectRevision = sha256(response.projectRevision, '工程修订');
  const projectSessionID = text(response.projectSessionID, '工程会话标识');
  const sourcePath = text(response.sourcePath, '矢量源路径');
  const sourceRevision = sha256(response.sourceRevision, '矢量源修订');
  const visualizationRevision = response.visualizationRevision === null
    ? null : sha256(response.visualizationRevision, '地图可视化修订');
  const styleSHA256 = sha256(response.styleSHA256, '矢量符号修订');
  const globalSymbologyRevision = sha256(response.globalSymbologyRevision, '全局矢量符号修订');
  if (typeof response.hasUnsupportedProjectConditions !== 'boolean' ||
      typeof response.hasUnknownProjectSymbologyContent !== 'boolean') {
    throw new Error('服务端返回的只读符号内容状态无效。');
  }
  const expectedPath = source?.expectedFamily?.sourcePath ?? null;
  if (source && (mapID.toUpperCase() !== source.map.mapID.toUpperCase() || layerID !== source.layerID ||
      !sameLocator(source.node, node as unknown as CatalogTreeNodeLocator) ||
      projectPath !== source.map.projectPath || projectXmlSHA !== source.map.projectXmlSHA ||
      projectRevision.toUpperCase() !== source.expectedProjectRevision.toUpperCase() ||
      projectSessionID.toUpperCase() !== source.map.projectSessionID.toUpperCase() ||
      sourceRevision.toUpperCase() !== source.sourceRevision.toUpperCase() ||
      (!allowVisualizationRevisionChange &&
        (source.map.expectedVisualizationRevision ?? null)?.toUpperCase() !== visualizationRevision?.toUpperCase()) ||
      (expectedPath !== null && sourcePath !== expectedPath))) {
    throw new Error('服务端返回的工程、地图、来源文件或可视化修订与当前选中图层不一致。');
  }
  return {
    mapID,
    layerID,
    node: node as unknown as CatalogTreeNodeLocator,
    projectPath,
    projectXmlSHA,
    projectRevision,
    projectSessionID,
    sourcePath,
    sourceRevision,
    visualizationRevision,
    styleSHA256,
    globalSymbologyRevision,
    definitions: parseDefinitions(response.definitions),
    styles: parseStyles(response.styles),
    plotOptions: parsePlotOptions(response.plotOptions),
    projectConditions: parseProjectConditions(response.projectConditions),
    hasUnsupportedProjectConditions: response.hasUnsupportedProjectConditions,
    hasUnknownProjectSymbologyContent: response.hasUnknownProjectSymbologyContent,
  };
}

/** Strictly validates a symbology inspection; when source is supplied it also binds the response to it. */
export function parseVectorSymbologySnapshot(value: unknown,
  source?: VectorLayerSource): VectorLayerSymbologySnapshot {
  return parseSnapshot(value, source);
}

export function parseVectorLayerSymbologySnapshot(value: unknown,
  source: VectorLayerSource, allowVisualizationRevisionChange = false): VectorLayerSymbologySnapshot {
  return parseSnapshot(value, source, allowVisualizationRevisionChange);
}

export function parseGlobalVectorSymbology(value: unknown): GlobalSymbologySnapshot {
  return parseGlobalSymbologySnapshot(value);
}

export function sameStyleValues(left: VectorStyleValues, right: VectorStyleValues): boolean {
  return left.penColor === right.penColor && left.penWidth === right.penWidth &&
    left.fillColor === right.fillColor && left.pointSize === right.pointSize &&
    left.haloColor === right.haloColor && left.haloThickness === right.haloThickness &&
    left.textEmphasis === right.textEmphasis;
}

export function sameStyleDraft(left: MapVectorPlotStyle[], right: MapVectorPlotStyle[]): boolean {
  return left.length === right.length && left.every((style, index) => {
    const original = right[index];
    return original !== undefined && style.definitionID === original.definitionID &&
      style.isProjectOverride === original.isProjectOverride && sameStyleValues(style.base, original.base);
  });
}

export function updateStyleValue(styles: MapVectorPlotStyle[], definitionID: string,
  property: VectorStyleProperty, value: string | number): MapVectorPlotStyle[] {
  return styles.map(style => style.definitionID === definitionID ? {
    ...style,
    base: { ...style.base, [property]: value },
    isProjectOverride: true,
  } : style);
}

export function resetStyleToGlobal(styles: MapVectorPlotStyle[], definitionID: string,
  globalValues: VectorStyleValues, hasProjectConditions = false): MapVectorPlotStyle[] {
  return styles.map(style => style.definitionID === definitionID ? {
    ...style,
    base: { ...globalValues },
    // Conditions still belong to this project even when their base values match global defaults.
    isProjectOverride: hasProjectConditions || style.sourceOverrides.length > 0,
  } : style);
}

export function vectorStyleGeometry(definitionID: string): VectorStyleGeometry {
  switch (definitionID) {
    case 'Point_Map_Layer_Symbology':
    case 'Polyline_Vertex_Symbology':
    case 'Polygon_Map_Layer_Vertex_Symbology':
    case 'Gdb_Point_Symbology':
    case 'Gdb_Line_Vertex_Symbology':
      return 'point';
    case 'Polyline_Line_Symbology':
    case 'Gdb_Line_Symbology':
      return 'line';
    case 'Polygon_Map_Layer_Shape_Symbology':
    case 'Gdb_Polygon_Symbology':
      return 'polygon';
    default:
      return definitionID.toLowerCase().includes('text') ? 'text' : 'unknown';
  }
}

export function editableVectorStyleProperties(geometry: VectorStyleGeometry): VectorStyleProperty[] {
  switch (geometry) {
    case 'point': return ['penColor', 'penWidth', 'fillColor', 'pointSize'];
    case 'line': return ['penColor', 'penWidth'];
    case 'polygon': return ['penColor', 'penWidth', 'fillColor'];
    case 'text': return ['fillColor', 'penWidth', 'haloColor', 'haloThickness', 'textEmphasis'];
    default: return [];
  }
}

export function isValidStyleDraft(styles: MapVectorPlotStyle[]): boolean {
  return styles.length > 0 && styles.every(style => {
    const values = style.base;
    return /^#[\da-f]{8}$/i.test(values.penColor) &&
      /^#[\da-f]{8}$/i.test(values.fillColor) && /^#[\da-f]{8}$/i.test(values.haloColor) &&
      Number.isFinite(values.penWidth) && values.penWidth >= 0 && values.penWidth <= 10_000 &&
      Number.isInteger(values.pointSize) && values.pointSize >= 0 && values.pointSize <= 10_000 &&
      Number.isFinite(values.haloThickness) && values.haloThickness >= 0 && values.haloThickness <= 10_000 &&
      Number.isInteger(values.textEmphasis);
  });
}

export function hasSourceFeatureOverrides(styles: MapVectorPlotStyle[]): boolean {
  return styles.some(style => style.sourceOverrides.length > 0);
}

export function plotOptionBaseline(options: VectorPlotOptionDescriptor[]): Record<string, string> {
  return Object.fromEntries(options.map(option => [option.propertyName, option.value]));
}

function isUnsupportedOption(description: string): boolean {
  const lower = description.toLowerCase();
  return lower.includes('does not implement') || description.includes('未实现');
}

export function enabledPlotOptionProperties(options: VectorPlotOptionDescriptor[],
  values: Record<string, string>): Set<string> {
  const byName = new Map(options.map(option => [option.propertyName, option]));
  return new Set(options.filter(option => {
    if (isUnsupportedOption(option.description)) return false;
    if (option.enabledIf) {
      const controller = byName.get(option.enabledIf);
      return controller?.kind === 'bool' && values[controller.propertyName] === 'true';
    }
    return option.isEnabled;
  }).map(option => option.propertyName));
}

export function changedEnabledPlotOptions(options: VectorPlotOptionDescriptor[],
  baseline: Record<string, string>, draft: Record<string, string>): Record<string, string> {
  const enabled = enabledPlotOptionProperties(options, draft);
  return Object.fromEntries(options
    .filter(option => enabled.has(option.propertyName) && draft[option.propertyName] !== baseline[option.propertyName])
    .map(option => [option.propertyName, draft[option.propertyName]]));
}

export function vectorLayerSymbologyRequest(source: VectorLayerSource) {
  return {
    map: source.map,
    node: source.node,
    layerID: source.layerID,
    expectedProjectRevision: source.expectedProjectRevision,
    expectedFamily: source.expectedFamily,
    sourceRevision: source.sourceRevision,
  };
}

export function serializeStylesForSave(styles: MapVectorPlotStyle[]): MapVectorPlotStyle[] {
  return styles.map(style => ({ ...style,
    base: { ...style.base },
    sourceOverrides: style.sourceOverrides.map(override => ({ ...override, values: { ...override.values } })),
  }));
}

export function rebaseSourceOverrides(styles: MapVectorPlotStyle[], conditions: VectorProjectCondition[]): MapVectorPlotStyle[] {
  const propertiesByDefinition = new Map<string, Set<VectorStyleProperty>>();
  for (const conditionGroup of conditions) {
    const property = (conditionGroup.property.charAt(0).toLowerCase() + conditionGroup.property.slice(1)) as VectorStyleProperty;
    if (!styleProperties.has(property)) throw new Error(`无法识别条件式样式属性：${conditionGroup.property}`);
    if (conditionGroup.conditions.length === 0 || conditionGroup.conditions.some(condition =>
      !condition.column.trim() || !condition.operator.trim() || !condition.resultValue.trim())) {
      throw new Error(`样式 ${conditionGroup.definitionID} 的条件结果不完整。`);
    }
    const properties = propertiesByDefinition.get(conditionGroup.definitionID) ?? new Set<VectorStyleProperty>();
    properties.add(property);
    propertiesByDefinition.set(conditionGroup.definitionID, properties);
  }
  const styleIDs = new Set(styles.map(style => style.definitionID));
  for (const definitionID of propertiesByDefinition.keys()) {
    if (!styleIDs.has(definitionID)) throw new Error(`条件样式 ${definitionID} 没有对应的渲染定义。`);
  }
  return styles.map(style => {
    const conditioned = propertiesByDefinition.get(style.definitionID) ?? new Set<VectorStyleProperty>();
    if (style.sourceOverrides.length > 0 && conditioned.size === 0) {
      throw new Error(`样式 ${style.definitionID} 含有未绑定到已检查条件的来源覆写。`);
    }
    return {
      ...style,
      sourceOverrides: style.sourceOverrides.map(override => {
        const values = { ...override.values };
        for (const property of styleProperties) {
          if (!conditioned.has(property)) Object.assign(values, { [property]: style.base[property] });
        }
        return { ...override, values };
      }),
    };
  });
}

export function formatConditionValue(condition: VectorProjectCondition['conditions'][number]): string {
  return `${condition.column} ${condition.operator} ${condition.threshold} → ${condition.resultValue}`;
}
