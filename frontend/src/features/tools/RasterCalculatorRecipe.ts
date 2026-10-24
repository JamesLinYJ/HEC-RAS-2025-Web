import type { Language, WorkbenchContext } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import type { MapDocumentTab } from '../map/mapTypes';

export interface RasterMapViewExtent {
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
  pixelWidth: number;
  pixelHeight: number;
}

export interface RasterCalculatorSourceFile {
  path: string;
  role: string;
  exists: boolean;
  revision: string | null;
}

export interface RasterCalculatorSourceChoice {
  id: string;
  kind: 'terrain' | 'resultMap';
  name: string;
  assetPath: string;
  revision: string;
  files: RasterCalculatorSourceFile[];
  isAvailable: boolean;
  unavailableReason: string | null;
  mapTypes: string[];
  profileCount: number;
  profileTimes: string[];
  noDataValue: number | null;
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
}

export interface RasterCalculatorMapCapture {
  owner: string;
  map: Record<string, unknown>;
  projectPath: string;
  projectRevision: string;
  projectXmlSHA: string;
  visualizationRevision: string;
  mapID: string;
  mapIndex: number;
  mapName: string;
  mapType: string;
  contextToken: string;
  defaultNoDataBehavior: string;
  noDataBehaviors: string[];
  reservedConstants: string[];
  sources: RasterCalculatorSourceChoice[];
}

export interface RasterCalculatorInputDraft {
  id: string;
  variableName: string;
  sourceID: string;
  sourceRevision: string;
  kind: RasterCalculatorSourceChoice['kind'] | '';
  mapType: string | null;
  forceHorizontal: boolean;
  isKeyFrame: boolean;
}

export interface RasterCalculatorConstantDraft {
  id: string;
  name: string;
  value: string;
}

export interface RasterCalculatorRecipeInput {
  variableName: string;
  sourceID: string;
  sourceRevision: string;
  kind: RasterCalculatorSourceChoice['kind'];
  mapType?: string;
  forceHorizontal: boolean;
  isKeyFrame: boolean;
}

export interface RasterCalculatorRecipePayload {
  expression: string;
  noDataBehavior: string;
  keyFrameVariableName: string | null;
  inputs: RasterCalculatorRecipeInput[];
  constants: { name: string; value: string }[];
}

export interface RasterCalculatorProfile {
  index: number;
  time: string;
}

export interface RasterCalculatorLayerPayload {
  id: string;
  mapID: string;
  name: string;
  width: number;
  height: number;
  /** System.Text.Json serializes the Host byte[] payload as base64. */
  rgba: string;
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
}

export interface RasterCalculatorEvaluateResult {
  projectPath: string;
  projectRevision: string;
  projectXmlSHA: string;
  visualizationRevision: string;
  mapID: string;
  mapIndex: number;
  mapName: string;
  mapType: string;
  contextToken: string;
  layerID: string;
  layerName: string;
  recipe: RasterCalculatorRecipePayload;
  isDynamic: boolean;
  profileCount: number;
  selectedProfileIndex: number;
  selectedProfileTime: string | null;
  profiles: RasterCalculatorProfile[];
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
  layer: RasterCalculatorLayerPayload;
  sources: RasterCalculatorSourceChoice[];
}

export interface RasterCalculatorDraftInput {
  expression: string;
  noDataBehavior: string;
  inputs: RasterCalculatorInputDraft[];
  constants: RasterCalculatorConstantDraft[];
}

export function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value);
}

export function isSha256(value: unknown): value is string {
  return typeof value === 'string' && /^[\da-f]{64}$/i.test(value);
}

function samePath(left: string, right: string): boolean {
  const normalize = (value: string): string => {
    const result = value.replaceAll('\\', '/').replace(/\/+$/, '');
    return /^[a-z]:\//i.test(result) ? result.toLowerCase() : result;
  };
  return normalize(left) === normalize(right);
}

function finite(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value);
}

function requireRecord(value: unknown, message: string): Record<string, unknown> {
  if (!isRecord(value)) throw new Error(message);
  return value;
}

function requireArray(value: unknown, message: string): unknown[] {
  if (!Array.isArray(value)) throw new Error(message);
  return value;
}

function requireStringArray(value: unknown, message: string): string[] {
  const items = requireArray(value, message);
  const strings: string[] = [];
  for (const item of items) {
    if (typeof item !== 'string') throw new Error(message);
    strings.push(item);
  }
  return strings;
}

function requireString(value: unknown, message: string): string {
  if (typeof value !== 'string') throw new Error(message);
  return value;
}

function requireSha256(value: unknown, message: string): string {
  if (!isSha256(value)) throw new Error(message);
  return value;
}

function requireInteger(value: unknown, message: string): number {
  if (typeof value !== 'number' || !Number.isInteger(value)) throw new Error(message);
  return value;
}

function requireFinite(value: unknown, message: string): number {
  if (!finite(value)) throw new Error(message);
  return value;
}

function requireBoolean(value: unknown, message: string): boolean {
  if (typeof value !== 'boolean') throw new Error(message);
  return value;
}

function requireNullableString(value: unknown, message: string): string | null {
  if (value === null) return null;
  return requireString(value, message);
}

function requireNullableFinite(value: unknown, message: string): number | null {
  if (value === null) return null;
  return requireFinite(value, message);
}

function parseRasterCalculatorSources(value: unknown, scope: string): RasterCalculatorSourceChoice[] {
  const rawSources = requireArray(value, `${scope}来源清单格式无效。`);
  const sources = rawSources.map((raw, index): RasterCalculatorSourceChoice => {
    const itemLabel = `${scope}第 ${index + 1} 项`;
    const source = requireRecord(raw, `${itemLabel}格式无效。`);
    const id = requireSha256(source.id, `${itemLabel}来源 ID 无效。`);
    const kind = source.kind;
    if (kind !== 'terrain' && kind !== 'resultMap') throw new Error(`${itemLabel}来源类型无效。`);
    const name = requireString(source.name, `${itemLabel}名称无效。`);
    const assetPath = requireString(source.assetPath, `${itemLabel}路径无效。`);
    if (!assetPath) throw new Error(`${itemLabel}路径无效。`);
    const revision = requireSha256(source.revision, `${itemLabel}修订无效。`);
    const isAvailable = requireBoolean(source.isAvailable, `${itemLabel}可用状态无效。`);
    const unavailableReason = requireNullableString(source.unavailableReason, `${itemLabel}不可用原因无效。`);
    const profileCount = requireInteger(source.profileCount, `${itemLabel}Profile 数量无效。`);
    if (profileCount < 0) throw new Error(`${itemLabel}Profile 数量无效。`);
    const mapTypes = requireStringArray(source.mapTypes, `${itemLabel}地图类型无效。`);
    const profileTimes = requireStringArray(source.profileTimes, `${itemLabel}Profile 时刻无效。`);
    const minX = requireFinite(source.minX, `${itemLabel}范围无效。`);
    const minY = requireFinite(source.minY, `${itemLabel}范围无效。`);
    const maxX = requireFinite(source.maxX, `${itemLabel}范围无效。`);
    const maxY = requireFinite(source.maxY, `${itemLabel}范围无效。`);
    const noDataValue = requireNullableFinite(source.noDataValue, `${itemLabel}NoData 值无效。`);

    const rawFiles = requireArray(source.files, `${itemLabel}文件清单无效。`);
    const files = rawFiles.map((rawFile, fileIndex) => {
      const file = requireRecord(rawFile, `${itemLabel}文件 ${fileIndex + 1}格式无效。`);
      const path = requireString(file.path, `${itemLabel}文件 ${fileIndex + 1}路径无效。`);
      if (!path) throw new Error(`${itemLabel}文件 ${fileIndex + 1}路径无效。`);
      const role = requireString(file.role, `${itemLabel}文件 ${fileIndex + 1}角色无效。`);
      const exists = requireBoolean(file.exists, `${itemLabel}文件 ${fileIndex + 1}存在状态无效。`);
      const revisionValue = file.revision;
      const fileRevision = revisionValue === null
        ? null
        : requireSha256(revisionValue, `${itemLabel}文件 ${fileIndex + 1}修订无效。`);
      if (exists && fileRevision === null) throw new Error(`${itemLabel}文件 ${fileIndex + 1}缺少修订。`);
      return { path, role, exists, revision: fileRevision };
    });

    if (kind === 'resultMap' && isAvailable &&
        (profileCount < 1 || profileTimes.length !== profileCount || mapTypes.length === 0)) {
      throw new Error(`栅格计算结果源“${name}”的地图类型或 Profile 时刻与 Host 数量不一致。`);
    }
    return {
      id, kind, name, assetPath, revision, files, isAvailable, unavailableReason,
      mapTypes, profileCount, profileTimes, noDataValue, minX, minY, maxX, maxY,
    };
  });
  if (new Set(sources.map(source => source.id)).size !== sources.length) {
    throw new Error(`${scope}来源清单存在重复来源身份。`);
  }
  return sources;
}

function parseRasterCalculatorRecipePayload(value: unknown): RasterCalculatorRecipePayload {
  const recipe = requireRecord(value, '栅格计算结果中的配方格式无效。');
  const expression = requireString(recipe.expression, '栅格计算结果缺少表达式。');
  const noDataBehavior = requireString(recipe.noDataBehavior, '栅格计算结果缺少 NoData 行为。');
  const keyFrameVariableName = requireNullableString(recipe.keyFrameVariableName, '栅格计算结果的时刻变量无效。');
  const inputs = requireArray(recipe.inputs, '栅格计算结果的输入清单无效。').map<RasterCalculatorRecipeInput>(raw => {
    const input = requireRecord(raw, '栅格计算结果的输入项无效。');
    const kind = input.kind;
    if (kind !== 'terrain' && kind !== 'resultMap') throw new Error('栅格计算结果的输入类型无效。');
    const mapType = requireNullableString(input.mapType, '栅格计算结果的地图类型无效。');
    return {
      variableName: requireString(input.variableName, '栅格计算结果的变量名无效。'),
      sourceID: requireSha256(input.sourceID, '栅格计算结果的来源 ID 无效。'),
      sourceRevision: requireSha256(input.sourceRevision, '栅格计算结果的来源修订无效。'),
      kind,
      ...(mapType === null ? {} : { mapType }),
      forceHorizontal: requireBoolean(input.forceHorizontal, '栅格计算结果的水平采样选项无效。'),
      isKeyFrame: requireBoolean(input.isKeyFrame, '栅格计算结果的时刻来源选项无效.'),
    };
  });
  const constants = requireArray(recipe.constants, '栅格计算结果的常量清单无效。').map(raw => {
    const constant = requireRecord(raw, '栅格计算结果的常量项无效。');
    return {
      name: requireString(constant.name, '栅格计算结果的常量名称无效。'),
      value: requireString(constant.value, '栅格计算结果的常量值无效。'),
    };
  });
  return { expression, noDataBehavior, keyFrameVariableName, inputs, constants };
}

export function rasterCalculatorOwner(context: WorkbenchContext): string {
  return JSON.stringify([
    context.projectSessionID, context.projectPath, context.projectRevision, context.projectXml,
    context.coreLayerContext?.identityToken ?? null,
  ]);
}

export function buildRasterCalculatorMapRequest(
  context: WorkbenchContext,
  document: MapDocumentTab,
  projectXmlSHA: string,
): Record<string, unknown> {
  if (!context.projectPath || !context.projectRevision || !isSha256(context.projectRevision) ||
      !context.projectXml?.trim() || !context.projectSessionID) {
    throw new Error('栅格计算器需要当前已保存工程、XML 快照、工程修订和工程会话。');
  }
  if (!isSha256(projectXmlSHA) || !isSha256(document.visualizationRevision)) {
    throw new Error('所选保存地图缺少有效的 XML 或 .vis 修订。');
  }
  let coreLayerContext: Record<string, unknown> | undefined;
  if (context.coreLayerContext) {
    const core = context.coreLayerContext;
    if (core.projectSessionID !== context.projectSessionID || core.projectPath !== context.projectPath ||
        typeof core.projectRevision !== 'string' || core.projectRevision.toUpperCase() !== context.projectRevision.toUpperCase() ||
        typeof core.identityToken !== 'string' || !core.identityToken.trim() || !Array.isArray(core.references) ||
        (core.sessionHdfReferences !== undefined && !Array.isArray(core.sessionHdfReferences))) {
      throw new Error('当前外部图层上下文与工程、会话或工程修订不匹配；请刷新工程来源后重试。');
    }
    coreLayerContext = core;
  }
  return {
    projectPath: context.projectPath,
    projectXml: context.projectXml,
    projectXmlSHA,
    mapID: document.mapID,
    mapIndex: document.mapIndex,
    mapName: document.name,
    mapType: document.mapType,
    expectedVisualizationRevision: document.visualizationRevision,
    projectSessionID: context.projectSessionID,
    ...(coreLayerContext ? { coreLayerContext } : {}),
  };
}

export function parseRasterCalculatorInspection(
  value: unknown,
  expected: { context: WorkbenchContext; map: MapDocumentTab; projectXmlSHA: string },
): RasterCalculatorMapCapture {
  const response = requireRecord(value, '栅格计算器源目录回执格式不完整。');
  const projectPath = requireString(response.projectPath, '栅格计算器源回执缺少工程路径。');
  const projectRevision = requireSha256(response.projectRevision, '栅格计算器源回执的工程修订无效。');
  const projectXmlSHA = requireSha256(response.projectXmlSHA, '栅格计算器源回执的工程 XML SHA 无效。');
  const visualizationRevision = requireSha256(response.visualizationRevision, '栅格计算器源回执的地图修订无效。');
  const mapID = requireString(response.mapID, '栅格计算器源回执缺少地图 ID。');
  const mapIndex = requireInteger(response.mapIndex, '栅格计算器源回执的地图索引无效。');
  const mapName = requireString(response.mapName, '栅格计算器源回执缺少地图名称。');
  const mapType = requireString(response.mapType, '栅格计算器源回执缺少地图类型。');
  const contextToken = requireSha256(response.contextToken, '栅格计算器源回执的上下文令牌无效。');
  const defaultNoDataBehavior = requireString(response.defaultNoDataBehavior, '栅格计算器源回执缺少默认 NoData 行为。');
  if (!samePath(projectPath, expected.context.projectPath ?? '') ||
      projectRevision.toUpperCase() !== expected.context.projectRevision?.toUpperCase() ||
      projectXmlSHA.toUpperCase() !== expected.projectXmlSHA.toUpperCase() ||
      visualizationRevision.toUpperCase() !== expected.map.visualizationRevision?.toUpperCase() ||
      mapID !== expected.map.mapID || mapIndex !== expected.map.mapIndex ||
      mapName !== expected.map.name || mapType !== expected.map.mapType) {
    throw new Error('栅格计算器源回执与当前工程、地图或 .vis 修订不匹配。');
  }
  const noDataBehaviors = requireStringArray(response.noDataBehaviors, '栅格计算器返回了无效的 NoData 选项。');
  const reservedConstants = requireStringArray(response.reservedConstants, '栅格计算器返回了无效的保留常量清单。');
  if (!noDataBehaviors.includes(defaultNoDataBehavior)) throw new Error('栅格计算器默认 NoData 行为不在 Host 选项中。');
  const sources = parseRasterCalculatorSources(response.sources, '栅格计算器源目录');
  return {
    owner: rasterCalculatorOwner(expected.context),
    map: buildRasterCalculatorMapRequest(expected.context, expected.map, expected.projectXmlSHA),
    projectPath, projectRevision, projectXmlSHA, visualizationRevision,
    mapID, mapIndex, mapName, mapType, contextToken,
    defaultNoDataBehavior, noDataBehaviors, reservedConstants, sources,
  };
}

export function defaultRasterCalculatorVariableName(inputs: readonly RasterCalculatorInputDraft[]): string {
  const used = new Set(inputs.map(input => input.variableName.trim().toLocaleLowerCase('en-US')));
  for (const name of 'abcdefghijklmnopqrstuvwxyz') if (!used.has(name)) return name;
  for (let index = 1; index <= 100; index += 1) if (!used.has(`x${index}`)) return `x${index}`;
  return 'var';
}

export function normalizeRasterCalculatorKeyFrame(inputs: RasterCalculatorInputDraft[], preferredID?: string): RasterCalculatorInputDraft[] {
  const dynamicIndexes = inputs.map((input, index) => input.kind === 'resultMap' ? index : -1).filter(index => index >= 0);
  const selected = (preferredID ? dynamicIndexes.find(index => inputs[index].id === preferredID) : undefined) ??
    dynamicIndexes.find(index => inputs[index].isKeyFrame) ?? dynamicIndexes[0];
  return inputs.map((input, index) => ({
    ...input,
    isKeyFrame: input.kind === 'resultMap' && index === selected,
  }));
}

export function calculatorSourceExtent(inputs: readonly RasterCalculatorInputDraft[], sources: readonly RasterCalculatorSourceChoice[]): Omit<RasterMapViewExtent, 'pixelWidth' | 'pixelHeight'> | null {
  const selected = inputs.map(input => sources.find(source => source.id === input.sourceID && source.revision === input.sourceRevision));
  if (!selected.length || selected.some(source => !source || !source.isAvailable ||
      ![source.minX, source.minY, source.maxX, source.maxY].every(Number.isFinite) ||
      source.minX >= source.maxX || source.minY >= source.maxY)) return null;
  const extents = selected as RasterCalculatorSourceChoice[];
  const extent = {
    minX: Math.max(...extents.map(source => source.minX)),
    minY: Math.max(...extents.map(source => source.minY)),
    maxX: Math.min(...extents.map(source => source.maxX)),
    maxY: Math.min(...extents.map(source => source.maxY)),
  };
  return Number.isFinite(extent.minX) && Number.isFinite(extent.minY) && Number.isFinite(extent.maxX) &&
    Number.isFinite(extent.maxY) && extent.minX < extent.maxX && extent.minY < extent.maxY ? extent : null;
}

export function validateRasterCalculatorRecipe(
  draft: RasterCalculatorDraftInput,
  capture: RasterCalculatorMapCapture,
  language: Language = 'zh',
): { recipe?: RasterCalculatorRecipePayload; error?: string } {
  const say = (zh: string, en: string): string => text(language, zh, en);
  const expression = draft.expression.trim();
  if (!expression) return { error: say('请输入栅格表达式。', 'Enter a raster expression.') };
  if (!capture.noDataBehaviors.includes(draft.noDataBehavior)) return { error: say('所选 NoData 行为不在当前 Host 选项中。请刷新源目录。', 'The NoData behavior is absent from the current Host options. Refresh the source catalog.') };
  if (draft.inputs.length === 0) return { error: say('请至少添加一个地形或结果栅格输入。', 'Add at least one Terrain or Result raster input.') };
  const variableNames = draft.inputs.map(input => input.variableName.trim());
  if (variableNames.some(name => !name)) return { error: say('所有输入都需要变量名。', 'Every input needs a variable name.') };
  if (variableNames.some(name => name !== name.trim())) return { error: say('输入变量名不能包含前后空格。', 'Input variable names cannot have leading or trailing spaces.') };
  const normalizedVariables = variableNames.map(name => name.toLocaleLowerCase('en-US'));
  if (new Set(normalizedVariables).size !== normalizedVariables.length) return { error: say('输入变量名不能重复。', 'Input variable names must be unique.') };
  const reserved = new Set(capture.reservedConstants.map(name => name.toLocaleLowerCase('en-US')));
  if (normalizedVariables.some(name => reserved.has(name))) return { error: say(`输入变量名不能使用保留字：${capture.reservedConstants.join(', ')}。`, `Input variable names cannot use reserved words: ${capture.reservedConstants.join(', ')}.`) };

  const keyFrames = draft.inputs.filter(input => input.kind === 'resultMap' && input.isKeyFrame);
  const resultInputs = draft.inputs.filter(input => input.kind === 'resultMap');
  if (keyFrames.length > 1) return { error: say('只能选择一个结果输入驱动计算时刻。', 'Only one Result input can drive calculator timesteps.') };
  if (resultInputs.length > 0 && keyFrames.length !== 1) return { error: say('请选择一个结果输入作为计算时刻来源。', 'Choose one Result input to drive calculator timesteps.') };
  const inputs: RasterCalculatorRecipeInput[] = [];
  const generatedNoDataNames = new Set(draft.inputs.flatMap(input => {
    const source = capture.sources.find(candidate => candidate.id === input.sourceID);
    return source?.kind === 'terrain' && source.noDataValue !== null && source.noDataValue !== -9999
      ? [`${input.variableName.trim()}_ndv`.toLocaleLowerCase('en-US')] : [];
  }));
  if (generatedNoDataNames.size !== [...draft.inputs].filter(input => {
    const source = capture.sources.find(candidate => candidate.id === input.sourceID);
    return source?.kind === 'terrain' && source.noDataValue !== null && source.noDataValue !== -9999;
  }).length) return { error: say('多个地形输入生成了重复的 NoData 常量名。请更改变量名。', 'Multiple Terrain inputs generate the same NoData constant name. Change the variable names.') };
  if (normalizedVariables.some(name => generatedNoDataNames.has(name))) return { error: say('输入变量名与自动生成的 NoData 常量重名。', 'An input variable collides with an automatically generated NoData constant.') };
  for (const [index, input] of draft.inputs.entries()) {
    const variableName = variableNames[index];
    const source = capture.sources.find(candidate => candidate.id === input.sourceID);
    if (!source || !source.isAvailable || source.revision !== input.sourceRevision || source.kind !== input.kind) {
      return { error: say(`输入“${variableName}”的来源不可用或已变化。请重新检查来源并重新绑定。`, `The source for input “${variableName}” is unavailable or changed. Refresh and rebind it.`) };
    }
    if (source.files.some(file => !file.exists || !file.revision)) {
      return { error: say(`输入“${variableName}”依赖的文件缺失或未捕获修订。`, `A file required by input “${variableName}” is missing or has no captured revision.`) };
    }
    if (source.kind === 'terrain') {
      if (input.mapType !== null || input.forceHorizontal || input.isKeyFrame) return { error: say(`地形输入“${variableName}”不能使用结果地图选项。`, `Terrain input “${variableName}” cannot use Result map options.`) };
      inputs.push({ variableName, sourceID: source.id, sourceRevision: source.revision, kind: source.kind,
        forceHorizontal: false, isKeyFrame: false });
    } else {
      if (!input.mapType || !source.mapTypes.includes(input.mapType)) return { error: say(`请选择输入“${variableName}”真实提供的结果地图类型。`, `Choose a Result map type actually offered for input “${variableName}”.`) };
      inputs.push({ variableName, sourceID: source.id, sourceRevision: source.revision, kind: source.kind,
        mapType: input.mapType, forceHorizontal: Boolean(input.forceHorizontal), isKeyFrame: Boolean(input.isKeyFrame) });
    }
  }

  const constantNames = draft.constants.map(constant => constant.name.trim());
  if (constantNames.some(name => !name)) return { error: say('所有常量都需要名称。', 'Every constant needs a name.') };
  if (constantNames.some(name => name !== name.trim())) return { error: say('常量名称不能包含前后空格。', 'Constant names cannot have leading or trailing spaces.') };
  const normalizedConstants = constantNames.map(name => name.toLocaleLowerCase('en-US'));
  if (new Set(normalizedConstants).size !== normalizedConstants.length) return { error: say('常量名称不能重复。', 'Constant names must be unique.') };
  const floatMax = 3.4028234663852886e38;
  const constants: { name: string; value: string }[] = [];
  for (const [index, name] of constantNames.entries()) {
    const normalizedName = name.toLocaleLowerCase('en-US');
    if (normalizedVariables.includes(normalizedName)) return { error: say(`常量“${name}”与输入变量重名。`, `Constant “${name}” duplicates an input variable.`) };
    if (reserved.has(normalizedName) || normalizedName.endsWith('_ndv')) return { error: say(`常量名“${name}”属于保留名称。`, `Constant name “${name}” is reserved.`) };
    const valueText = draft.constants[index].value.trim();
    const value = Number(valueText);
    if (!/^[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?$/.test(valueText) ||
        !Number.isFinite(value) || Math.abs(value) > floatMax) return { error: say(`常量“${name}”必须是有限的 Float32 数值。`, `Constant “${name}” must be a finite Float32 value.`) };
    constants.push({ name, value: valueText });
  }

  return {
    recipe: {
      expression, noDataBehavior: draft.noDataBehavior,
      keyFrameVariableName: keyFrames[0]?.variableName.trim() ?? null,
      inputs, constants,
    },
  };
}

export function parseRasterCalculatorEvaluation(
  value: unknown,
  expected: {
    capture: RasterCalculatorMapCapture;
    layerID: string;
    layerName: string;
    profileIndex: number;
  },
): RasterCalculatorEvaluateResult {
  const result = requireRecord(value, '栅格计算结果回执格式不完整。');
  const layer = requireRecord(result.layer, '栅格计算结果缺少图层帧。');
  const projectPath = requireString(result.projectPath, '栅格计算结果缺少工程路径。');
  const projectRevision = requireSha256(result.projectRevision, '栅格计算结果的工程修订无效。');
  const projectXmlSHA = requireSha256(result.projectXmlSHA, '栅格计算结果的工程 XML SHA 无效。');
  const visualizationRevision = requireSha256(result.visualizationRevision, '栅格计算结果的地图修订无效。');
  const mapID = requireString(result.mapID, '栅格计算结果缺少地图 ID。');
  const mapIndex = requireInteger(result.mapIndex, '栅格计算结果的地图索引无效。');
  const mapName = requireString(result.mapName, '栅格计算结果缺少地图名称。');
  const mapType = requireString(result.mapType, '栅格计算结果缺少地图类型。');
  const contextToken = requireSha256(result.contextToken, '栅格计算结果的上下文令牌无效。');
  const layerID = requireString(result.layerID, '栅格计算结果缺少图层 ID。');
  const layerName = requireString(result.layerName, '栅格计算结果缺少图层名称。');
  const isDynamic = requireBoolean(result.isDynamic, '栅格计算结果的动态状态无效。');
  const profileCount = requireInteger(result.profileCount, '栅格计算结果的 Profile 数量无效。');
  const selectedProfileIndex = requireInteger(result.selectedProfileIndex, '栅格计算结果的选定 Profile 无效。');
  const selectedProfileTime = requireNullableString(result.selectedProfileTime, '栅格计算结果的选定时刻无效。');
  const minX = requireFinite(result.minX, '栅格计算结果范围无效。');
  const minY = requireFinite(result.minY, '栅格计算结果范围无效。');
  const maxX = requireFinite(result.maxX, '栅格计算结果范围无效。');
  const maxY = requireFinite(result.maxY, '栅格计算结果范围无效。');
  const layerIDValue = requireString(layer.id, '栅格预览帧缺少图层 ID。');
  const layerMapID = requireString(layer.mapID, '栅格预览帧缺少地图 ID。');
  const layerNameValue = requireString(layer.name, '栅格预览帧缺少图层名称。');
  const width = requireInteger(layer.width, '栅格预览帧宽度无效。');
  const height = requireInteger(layer.height, '栅格预览帧高度无效。');
  const rgba = requireString(layer.rgba, '栅格预览帧缺少 RGBA 数据。');
  const layerMinX = requireFinite(layer.minX, '栅格预览帧范围无效。');
  const layerMinY = requireFinite(layer.minY, '栅格预览帧范围无效。');
  const layerMaxX = requireFinite(layer.maxX, '栅格预览帧范围无效。');
  const layerMaxY = requireFinite(layer.maxY, '栅格预览帧范围无效。');
  if (projectPath !== expected.capture.projectPath ||
      projectRevision.toUpperCase() !== expected.capture.projectRevision.toUpperCase() ||
      projectXmlSHA.toUpperCase() !== expected.capture.projectXmlSHA.toUpperCase() ||
      visualizationRevision.toUpperCase() !== expected.capture.visualizationRevision.toUpperCase() ||
      mapID !== expected.capture.mapID || mapIndex !== expected.capture.mapIndex ||
      mapName !== expected.capture.mapName || mapType !== expected.capture.mapType ||
      contextToken !== expected.capture.contextToken || layerID !== expected.layerID ||
      layerName !== expected.layerName || selectedProfileIndex !== expected.profileIndex ||
      profileCount < 1 || width < 1 || height < 1 || width * height > 2_097_152 ||
      minX >= maxX || minY >= maxY || layerIDValue !== expected.layerID ||
      layerMapID !== expected.capture.mapID || layerNameValue !== expected.layerName ||
      layerMinX >= layerMaxX || layerMinY >= layerMaxY) {
    throw new Error('栅格计算结果与当前地图、来源修订、剖面或 RGBA 预览数据不匹配。');
  }
  let rgbaByteLength: number;
  try { rgbaByteLength = atob(rgba).length; }
  catch { throw new Error('Host 栅格预览没有返回有效的 base64 RGBA 数据。'); }
  if (rgbaByteLength !== width * height * 4) {
    throw new Error('Host 栅格预览像素字节数与图像尺寸不一致。');
  }
  const profiles = requireArray(result.profiles, '栅格计算结果的 Profile 清单无效。').map((raw, index) => {
    const profile = requireRecord(raw, '栅格计算结果的 Profile 项无效。');
    const returnedIndex = requireInteger(profile.index, '栅格计算结果的 Profile 索引无效。');
    const time = requireString(profile.time, '栅格计算结果的 Profile 时刻无效。');
    if (returnedIndex !== index) {
      throw new Error('栅格计算返回的结果时刻列表不连续。');
    }
    return { index: returnedIndex, time };
  });
  if (profiles.length !== profileCount || expected.profileIndex < 0 || expected.profileIndex >= profiles.length) {
    throw new Error('栅格计算返回的选定时刻超出结果时刻列表。');
  }
  const sources = parseRasterCalculatorSources(result.sources, '栅格计算结果');
  if (sources.length !== expected.capture.sources.length) {
    throw new Error('计算期间 Host 来源清单发生变化；请刷新后重新计算。');
  }
  for (const captured of expected.capture.sources) {
    const current = sources.find(source => source.id === captured.id);
    if (!current || current.kind !== captured.kind || !samePath(current.assetPath, captured.assetPath) ||
        current.revision.toUpperCase() !== captured.revision.toUpperCase() || current.isAvailable !== captured.isAvailable) {
      throw new Error('计算期间来源清单或来源修订发生变化；请刷新后重新计算。');
    }
  }
  return {
    projectPath, projectRevision, projectXmlSHA, visualizationRevision,
    mapID, mapIndex, mapName, mapType, contextToken, layerID, layerName,
    recipe: parseRasterCalculatorRecipePayload(result.recipe),
    isDynamic, profileCount, selectedProfileIndex, selectedProfileTime, profiles,
    minX, minY, maxX, maxY,
    layer: {
      id: layerIDValue, mapID: layerMapID, name: layerNameValue,
      width, height, rgba,
      minX: layerMinX, minY: layerMinY, maxX: layerMaxX, maxY: layerMaxY,
    },
    sources,
  };
}
