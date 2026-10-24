import type { WorkbenchContext } from '../../../shared/workbench';
import { projectXmlSha } from '../../map/ProjectTreeListener';
import type {
  SessionHdfComposedContextReceipt,
  SessionHdfInspection,
  SessionHdfKind,
  SessionHdfMapCatalog,
  SessionHdfMapDocument,
  SessionHdfMapTreeReceipt,
  SessionHdfReferenceCapture,
  WorkspaceFileCapture,
} from './sessionHdfImportTypes';

export const sessionHdfMethods = {
  fileInspect: 'workspace.file.inspect',
  inspect: 'workspace.import.sessionHdf.inspect',
  compose: 'workspace.references.compose',
  mapList: 'map.document.list',
  mapPerform: 'map.tree.perform',
} as const;

type Dict = Record<string, unknown>;

function object(value: unknown, label: string): Dict {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    throw new Error(`${label}格式无效。`);
  }
  return value as Dict;
}

function string(value: unknown, label: string): string {
  if (typeof value !== 'string' || !value.trim()) throw new Error(`${label}缺失。`);
  return value;
}

function nullableString(value: unknown, label: string): string | null {
  if (value === null) return null;
  return string(value, label);
}

function sha(value: unknown, label: string): string {
  if (typeof value !== 'string' || !/^[\da-f]{64}$/i.test(value)) throw new Error(`${label} SHA-256 无效。`);
  return value;
}

function absolutePath(value: unknown, label: string): string {
  const candidate = string(value, label);
  const absolute = candidate.startsWith('/') || candidate.startsWith('\\\\') || /^[a-z]:[\\/]/i.test(candidate);
  if (!absolute) throw new Error(`${label}必须是服务端可见的绝对路径。`);
  return candidate;
}

function comparablePath(value: string): string {
  const normalized = value.replace(/\\/g, '/');
  const windowsPath = /^[a-z]:\//i.test(normalized) || normalized.startsWith('//');
  const prefix = normalized.startsWith('//') ? '//' : normalized.startsWith('/') ? '/' : normalized.slice(0, 2).toLowerCase() + '/';
  const remainder = normalized.startsWith('//') ? normalized.slice(2) : normalized.startsWith('/') ? normalized.slice(1) : normalized.slice(3);
  const segments: string[] = [];
  for (const segment of remainder.split('/')) {
    if (!segment || segment === '.') continue;
    if (segment === '..') { if (segments.length) segments.pop(); continue; }
    segments.push(segment);
  }
  const result = `${prefix}${segments.join('/')}`.replace(/\/$/, '');
  return windowsPath ? result.toLowerCase() : result;
}

function revisionMatches(left: string, right: string): boolean {
  return left.toUpperCase() === right.toUpperCase();
}

function sameSession(left: string, right: string): boolean {
  return left.toLowerCase() === right.toLowerCase();
}

function contextIdentity(context: Dict | null | undefined): string | null {
  if (!context) return null;
  const value = context.identityToken ?? context.IdentityToken;
  return typeof value === 'string' && value.trim() ? value : null;
}

export function parseWorkspaceFileCapture(value: unknown, requestedPath: string): WorkspaceFileCapture {
  const item = object(value, '来源文件捕获');
  const sourcePath = absolutePath(item.sourcePath, '来源文件路径');
  const canonicalPath = absolutePath(item.canonicalPath, '来源规范路径');
  const revision = sha(item.revision, '来源文件');
  const length = item.length;
  if (comparablePath(sourcePath) !== comparablePath(absolutePath(requestedPath, '输入来源路径')) ||
      typeof length !== 'number' || !Number.isSafeInteger(length) || length < 0) {
    throw new Error('服务端文件捕获与输入路径或长度不匹配。');
  }
  return { sourcePath, canonicalPath, revision, length };
}

function parseReference(value: unknown): SessionHdfReferenceCapture {
  const item = object(value, 'Session HDF 引用捕获');
  const kind = string(item.kind, 'Session HDF 类型');
  if (kind !== 'plan' && kind !== 'result' && kind !== 'terrain') throw new Error('服务端返回了不支持的 Session HDF 类型。');
  if (item.sessionOnly !== true) throw new Error('来源捕获不是仅限当前会话的引用。');
  return {
    referenceID: string(item.referenceID, '引用标识'),
    projectSessionID: string(item.projectSessionID, '引用所属会话'),
    projectPath: absolutePath(item.projectPath, '引用所属工程路径'),
    kind,
    sourcePath: absolutePath(item.sourcePath, '引用来源路径'),
    canonicalSourcePath: absolutePath(item.canonicalSourcePath, '引用规范来源路径'),
    sourceRevision: sha(item.sourceRevision, '引用来源'),
    name: string(item.name, '引用名称'),
    resultPlanName: nullableString(item.resultPlanName ?? null, '嵌入计划名称'),
    sessionOnly: true,
  };
}

export function parseSessionHdfInspection(value: unknown, input: {
  context: WorkbenchContext;
  projectXmlRevision: string;
  source: WorkspaceFileCapture;
}): SessionHdfInspection {
  const item = object(value, 'Session HDF 检查结果');
  const reference = parseReference(item.reference);
  const projectPath = absolutePath(item.projectPath, '检查结果工程路径');
  const projectSessionID = string(item.projectSessionID, '检查结果工程会话');
  const projectRevision = sha(item.projectRevision, '检查结果工程');
  const projectXmlRevision = sha(item.projectXmlRevision, '检查结果工程 XML');
  const sourcePath = absolutePath(item.sourcePath, '检查结果来源路径');
  const canonicalSourcePath = absolutePath(item.canonicalSourcePath, '检查结果规范来源路径');
  const sourceRevision = sha(item.sourceRevision, '检查结果来源');
  const kind = string(item.kind, 'Session HDF 类型');
  if (kind !== 'plan' && kind !== 'result' && kind !== 'terrain') throw new Error('服务端返回了不支持的 Session HDF 类型。');
  const name = string(item.name, 'Session HDF 名称');
  const resultPlanName = nullableString(item.resultPlanName ?? null, '嵌入计划名称');
  const description = typeof item.description === 'string' ? item.description : '';
  const fileSize = item.fileSize;
  if (item.sessionOnly !== true || typeof fileSize !== 'number' || !Number.isSafeInteger(fileSize) || fileSize < 0 ||
      !sameSession(projectSessionID, input.context.projectSessionID) ||
      !input.context.projectRevision || !revisionMatches(projectRevision, input.context.projectRevision) ||
      !revisionMatches(projectXmlRevision, input.projectXmlRevision) ||
      !revisionMatches(sourceRevision, input.source.revision) ||
      canonicalSourcePath !== input.source.canonicalPath || fileSize !== input.source.length ||
      !sameSession(reference.projectSessionID, input.context.projectSessionID) ||
      reference.projectPath !== projectPath || reference.kind !== kind || reference.name !== name ||
      reference.sourcePath !== sourcePath || reference.canonicalSourcePath !== canonicalSourcePath ||
      !revisionMatches(reference.sourceRevision, sourceRevision) || reference.resultPlanName !== resultPlanName) {
    throw new Error('Session HDF 检查结果与当前工程会话或来源文件捕获不匹配。');
  }
  return { projectPath, projectSessionID, projectRevision, projectXmlRevision, sourcePath,
    canonicalSourcePath, sourceRevision, kind, name, resultPlanName, description, fileSize,
    sessionOnly: true, reference };
}

export function parseSessionHdfMapCatalog(value: unknown, context: WorkbenchContext): SessionHdfMapCatalog {
  const item = object(value, '工程地图目录');
  const projectRevision = sha(item.projectRevision, '地图目录工程');
  const visualizationRevision = item.visualizationRevision === null
    ? null : sha(item.visualizationRevision, '地图可视化');
  const activeMapIndex = item.activeMapIndex === null ? null : item.activeMapIndex;
  if (!context.projectRevision || !revisionMatches(projectRevision, context.projectRevision) ||
      !(activeMapIndex === null || (typeof activeMapIndex === 'number' && Number.isInteger(activeMapIndex) && activeMapIndex >= 0)) ||
      !Array.isArray(item.maps)) {
    throw new Error('地图目录与当前工程修订不匹配或内容不完整。');
  }
  const maps: SessionHdfMapDocument[] = item.maps.map((entry, index) => {
    const map = object(entry, `地图 ${index + 1}`);
    const mapIndex = map.mapIndex;
    const mapName = string(map.mapName, `地图 ${index + 1} 名称`);
    const mapType = string(map.mapType, `地图 ${index + 1} 类型`);
    if (typeof mapIndex !== 'number' || !Number.isInteger(mapIndex) || mapIndex < 0) {
      throw new Error(`地图 ${index + 1} 序号无效。`);
    }
    return { mapIndex, mapName, mapType };
  });
  if (maps.length > 0 && visualizationRevision === null) throw new Error('地图目录缺少当前 .vis 修订。');
  if (activeMapIndex !== null && !maps.some(map => map.mapIndex === activeMapIndex)) {
    throw new Error('地图目录活动地图不在返回列表中。');
  }
  if (new Set(maps.map(map => map.mapIndex)).size !== maps.length) throw new Error('地图目录包含重复序号。');
  return { projectRevision, visualizationRevision, maps, activeMapIndex };
}

export function parseComposedSessionHdfContext(value: unknown, input: {
  context: WorkbenchContext;
  sourceCoreLayerContext: Record<string, unknown> | null;
  reference: SessionHdfReferenceCapture;
}): SessionHdfComposedContextReceipt {
  const item = object(value, '会话引用组合回执');
  const projectPath = absolutePath(item.projectPath, '回执工程路径');
  const projectSessionID = string(item.projectSessionID, '回执工程会话');
  const projectRevision = sha(item.projectRevision, '回执工程修订');
  const previousContextIdentityToken = nullableString(item.previousContextIdentityToken ?? null, '原会话上下文标识');
  const nextContext = object(item.nextContext, '新会话上下文');
  const nextProjectPath = absolutePath(nextContext.projectPath ?? nextContext.ProjectPath, '新上下文工程路径');
  const nextProjectSessionID = string(nextContext.projectSessionID ?? nextContext.ProjectSessionID, '新上下文工程会话');
  const nextProjectRevision = nextContext.projectRevision ?? nextContext.ProjectRevision;
  const nextIdentity = contextIdentity(nextContext);
  const nextReferences = nextContext.sessionHdfReferences ?? nextContext.SessionHdfReferences;
  const sourceIdentity = contextIdentity(input.sourceCoreLayerContext);
  const carriesReference = Array.isArray(nextReferences) && nextReferences.some(entry => {
    if (typeof entry !== 'object' || entry === null || Array.isArray(entry)) return false;
    const candidate = entry as Dict;
    return candidate.referenceID === input.reference.referenceID &&
      candidate.sourceRevision && typeof candidate.sourceRevision === 'string' &&
      revisionMatches(candidate.sourceRevision, input.reference.sourceRevision) &&
      candidate.canonicalSourcePath === input.reference.canonicalSourcePath;
  });
  if (!input.context.projectPath || !input.context.projectRevision ||
      !sameSession(projectSessionID, input.context.projectSessionID) ||
      !revisionMatches(projectRevision, input.context.projectRevision) ||
      projectPath !== input.reference.projectPath || nextProjectPath !== projectPath ||
      !sameSession(nextProjectSessionID, projectSessionID) ||
      typeof nextProjectRevision !== 'string' || !revisionMatches(nextProjectRevision, projectRevision) ||
      !nextIdentity || !carriesReference ||
      (sourceIdentity === null ? previousContextIdentityToken !== null
        : previousContextIdentityToken === null || !revisionMatches(sourceIdentity, previousContextIdentityToken))) {
    throw new Error('会话引用组合回执与当前工程、原上下文或新增来源捕获不匹配。');
  }
  return { projectPath, projectSessionID, projectRevision, previousContextIdentityToken, nextContext };
}

export function parseSessionHdfMapTreeReceipt(value: unknown,
  expectedVisualizationRevision: string, target: {
    reference: SessionHdfReferenceCapture;
    nextContext: Record<string, unknown>;
    mapType: string;
  }): SessionHdfMapTreeReceipt {
  const item = object(value, '地图树写入回执');
  const previousVisualizationRevision = sha(item.previousVisualizationRevision, '原地图可视化');
  const visualizationRevision = sha(item.visualizationRevision, '新地图可视化');
  if (!revisionMatches(previousVisualizationRevision, expectedVisualizationRevision) || !Array.isArray(item.roots) ||
      !Array.isArray(item.expandedNodeIDs) || !item.expandedNodeIDs.every(entry => typeof entry === 'string') ||
      !Array.isArray(item.hiddenLayerIDs) || !item.hiddenLayerIDs.every(entry => typeof entry === 'string') ||
      !(item.selectedNodeID === null || typeof item.selectedNodeID === 'string')) {
    throw new Error('地图树写入回执与检查时的 .vis 修订或树内容不匹配。');
  }
  const nodes = catalogNodes(item.roots);
  verifySessionHdfNode(nodes, target);
  return { roots: item.roots, previousVisualizationRevision, visualizationRevision,
    expandedNodeIDs: item.expandedNodeIDs as string[], selectedNodeID: item.selectedNodeID as string | null,
    hiddenLayerIDs: item.hiddenLayerIDs as string[] };
}

function field(item: Dict, camelName: string, pascalName: string): unknown {
  return item[camelName] ?? item[pascalName];
}

function optionalString(item: Dict, camelName: string, pascalName: string): string | null {
  const value = field(item, camelName, pascalName);
  return typeof value === 'string' && value.trim() ? value : null;
}

function emptyField(item: Dict, camelName: string, pascalName: string): boolean {
  const value = field(item, camelName, pascalName);
  return value === null || value === undefined;
}

function catalogNodes(roots: unknown[]): Dict[] {
  const result: Dict[] = [];
  const visit = (value: unknown, label: string): void => {
    const node = object(value, label);
    string(field(node, 'id', 'Id'), `${label} ID`);
    string(field(node, 'sourceClass', 'SourceClass'), `${label}来源类型`);
    string(field(node, 'name', 'Name'), `${label}名称`);
    result.push(node);
    const children = field(node, 'children', 'Children');
    if (children === undefined || children === null) return;
    if (!Array.isArray(children)) throw new Error(`${label}子节点格式无效。`);
    children.forEach((child, index) => visit(child, `${label}子节点 ${index + 1}`));
  };
  roots.forEach((root, index) => visit(root, `地图根节点 ${index + 1}`));
  return result;
}

function nodeLocator(node: Dict): Dict | null {
  const value = field(node, 'locator', 'Locator');
  if (value === null || value === undefined) return null;
  return object(value, '地图树节点定位信息');
}

function sourceNodeMatches(node: Dict, locator: Dict, input: {
  sourceClass: string;
  nodeType: string;
  rootName: string;
  name: string;
  path: string;
}): boolean {
  const nodeAsset = optionalString(node, 'assetID', 'AssetID');
  const locatorAsset = optionalString(locator, 'assetID', 'AssetID');
  return field(node, 'sourceClass', 'SourceClass') === input.sourceClass &&
    field(node, 'name', 'Name') === input.name &&
    field(locator, 'nodeType', 'NodeType') === input.nodeType &&
    field(locator, 'nodeName', 'NodeName') === input.name &&
    field(locator, 'rootName', 'RootName') === input.rootName &&
    nodeAsset !== null && locatorAsset !== null &&
    comparablePath(nodeAsset) === comparablePath(input.path) &&
    comparablePath(locatorAsset) === comparablePath(input.path);
}

function contextReferences(context: Record<string, unknown>): Dict[] {
  const raw = field(context, 'sessionHdfReferences', 'SessionHdfReferences');
  if (!Array.isArray(raw)) throw new Error('新会话上下文缺少 Session HDF 引用列表。');
  return raw.map((entry, index) => object(entry, `新会话引用 ${index + 1}`));
}

function exactReferenceInContext(references: Dict[], reference: SessionHdfReferenceCapture): boolean {
  const matches = references.filter(candidate =>
    typeof field(candidate, 'referenceID', 'ReferenceID') === 'string' &&
    sameSession(field(candidate, 'referenceID', 'ReferenceID') as string, reference.referenceID) &&
    field(candidate, 'kind', 'Kind') === reference.kind &&
    field(candidate, 'sessionOnly', 'SessionOnly') === true &&
    typeof field(candidate, 'projectSessionID', 'ProjectSessionID') === 'string' &&
    sameSession(field(candidate, 'projectSessionID', 'ProjectSessionID') as string, reference.projectSessionID) &&
    typeof field(candidate, 'projectPath', 'ProjectPath') === 'string' &&
    comparablePath(field(candidate, 'projectPath', 'ProjectPath') as string) === comparablePath(reference.projectPath) &&
    typeof field(candidate, 'canonicalSourcePath', 'CanonicalSourcePath') === 'string' &&
    comparablePath(field(candidate, 'canonicalSourcePath', 'CanonicalSourcePath') as string) === comparablePath(reference.canonicalSourcePath) &&
    typeof field(candidate, 'sourceRevision', 'SourceRevision') === 'string' &&
    revisionMatches(field(candidate, 'sourceRevision', 'SourceRevision') as string, reference.sourceRevision) &&
    (field(candidate, 'resultPlanName', 'ResultPlanName') ?? null) === reference.resultPlanName);
  return matches.length === 1;
}

function verifySessionHdfNode(nodes: Dict[], target: {
  reference: SessionHdfReferenceCapture;
  nextContext: Record<string, unknown>;
  mapType: string;
}): void {
  const { reference, nextContext, mapType } = target;
  if (!supportedMapTypes(reference.kind).includes(mapType)) {
    throw new Error('目标地图类型不支持此 Session HDF 来源。');
  }
  const nextSession = optionalString(nextContext, 'projectSessionID', 'ProjectSessionID');
  const nextProject = optionalString(nextContext, 'projectPath', 'ProjectPath');
  const identity = contextIdentity(nextContext);
  const references = contextReferences(nextContext);
  if (!nextSession || !nextProject || !identity || !sameSession(nextSession, reference.projectSessionID) ||
      comparablePath(nextProject) !== comparablePath(reference.projectPath) ||
      !exactReferenceInContext(references, reference)) {
    throw new Error('地图树目标与已组合的工程会话引用不匹配。');
  }

  const directMatches = (predicate: (node: Dict, locator: Dict) => boolean): Dict[] => nodes.filter(node => {
    const locator = nodeLocator(node);
    return locator !== null && predicate(node, locator);
  });

  if (reference.kind === 'plan') {
    const rootName = mapType === 'Plan' ? 'Focused Plans' : mapType === 'Geometry' ? 'Layers' : 'Plans';
    const matches = directMatches((node, locator) =>
      sourceNodeMatches(node, locator, {
        sourceClass: 'ObservablePlan', nodeType: 'Plan', rootName,
        name: reference.name, path: reference.canonicalSourcePath,
      }) && emptyField(locator, 'parentPlanAssetID', 'ParentPlanAssetID') &&
      emptyField(locator, 'parentResultAssetID', 'ParentResultAssetID') &&
      emptyField(locator, 'associationKind', 'AssociationKind'));
    if (matches.length !== 1) throw new Error('地图树中没有唯一匹配此计划文件的计划节点。');
    return;
  }

  if (reference.kind === 'terrain') {
    const terrainRoot = mapType === 'Project' ? 'Terrains' : 'Layers';
    const matches = directMatches((node, locator) =>
      sourceNodeMatches(node, locator, {
        sourceClass: 'ObservableTerrain', nodeType: 'Terrain', rootName: terrainRoot,
        name: reference.name, path: reference.canonicalSourcePath,
      }) && emptyField(locator, 'parentGeometryAssetID', 'ParentGeometryAssetID') &&
      emptyField(locator, 'parentPlanAssetID', 'ParentPlanAssetID') &&
      emptyField(locator, 'parentResultAssetID', 'ParentResultAssetID') &&
      emptyField(locator, 'associationKind', 'AssociationKind'));
    if (matches.length !== 1) throw new Error('地图树中没有唯一匹配此地形文件的地形节点。');
    return;
  }

  if (reference.kind !== 'result') throw new Error('地图树回执包含不支持的 Session HDF 类型。');
  const resultRoot = mapType === 'Project' ? 'Results' : 'Layers';
  if (mapType === 'Project' || !reference.resultPlanName) {
    const matches = directMatches((node, locator) =>
      sourceNodeMatches(node, locator, {
        sourceClass: 'ObservableResult', nodeType: 'Result', rootName: resultRoot,
        name: reference.name, path: reference.canonicalSourcePath,
      }));
    if (matches.length !== 1) throw new Error('地图树中没有唯一匹配此结果文件的结果节点。');
    return;
  }

  const sameNameGroups = directMatches((node, locator) =>
    field(node, 'sourceClass', 'SourceClass') === 'MultiResultNodeVM' &&
    field(node, 'name', 'Name') === reference.resultPlanName &&
    field(locator, 'nodeType', 'NodeType') === 'MultiResult' &&
    field(locator, 'nodeName', 'NodeName') === reference.resultPlanName &&
    field(locator, 'rootName', 'RootName') === resultRoot);
  if (sameNameGroups.length !== 1) throw new Error('地图树中没有唯一匹配此嵌入计划的计算结果分组。');
  const locator = nodeLocator(sameNameGroups[0]);
  const ownerValue = locator && field(locator, 'sessionHdfResultOwner', 'SessionHdfResultOwner');
  if (!locator || ownerValue === null || ownerValue === undefined) {
    throw new Error('地图树结果分组缺少会话来源归属信息。');
  }
  const owner = object(ownerValue, '结果分组会话归属');
  const ownerReferenceID = optionalString(owner, 'referenceID', 'ReferenceID');
  const ownerProjectSessionID = optionalString(owner, 'projectSessionID', 'ProjectSessionID');
  const ownerProjectPath = optionalString(owner, 'projectPath', 'ProjectPath');
  const ownerResultPath = optionalString(owner, 'resultPath', 'ResultPath');
  const ownerResultRevision = optionalString(owner, 'resultRevision', 'ResultRevision');
  const ownerPlanName = optionalString(owner, 'embeddedPlanName', 'EmbeddedPlanName');
  const ownerContextToken = optionalString(owner, 'coreLayerContextIdentityToken', 'CoreLayerContextIdentityToken');
  const anchorMatches = references.filter(candidate =>
    typeof field(candidate, 'referenceID', 'ReferenceID') === 'string' && ownerReferenceID !== null &&
    sameSession(field(candidate, 'referenceID', 'ReferenceID') as string, ownerReferenceID) &&
    field(candidate, 'kind', 'Kind') === 'result' &&
    field(candidate, 'sessionOnly', 'SessionOnly') === true &&
    field(candidate, 'resultPlanName', 'ResultPlanName') === reference.resultPlanName &&
    typeof field(candidate, 'projectSessionID', 'ProjectSessionID') === 'string' &&
    sameSession(field(candidate, 'projectSessionID', 'ProjectSessionID') as string, reference.projectSessionID) &&
    typeof field(candidate, 'projectPath', 'ProjectPath') === 'string' &&
    comparablePath(field(candidate, 'projectPath', 'ProjectPath') as string) === comparablePath(reference.projectPath) &&
    typeof field(candidate, 'canonicalSourcePath', 'CanonicalSourcePath') === 'string' &&
    ownerResultPath !== null &&
    comparablePath(field(candidate, 'canonicalSourcePath', 'CanonicalSourcePath') as string) === comparablePath(ownerResultPath) &&
    typeof field(candidate, 'sourceRevision', 'SourceRevision') === 'string' &&
    ownerResultRevision !== null &&
    revisionMatches(field(candidate, 'sourceRevision', 'SourceRevision') as string, ownerResultRevision));
  if (!ownerReferenceID || !ownerProjectSessionID || !ownerProjectPath || !ownerResultPath ||
      !ownerResultRevision || !ownerPlanName || !ownerContextToken ||
      !sameSession(ownerProjectSessionID, reference.projectSessionID) ||
      comparablePath(ownerProjectPath) !== comparablePath(reference.projectPath) ||
      ownerPlanName !== reference.resultPlanName || !sameSession(ownerContextToken, identity) ||
      anchorMatches.length !== 1) {
    throw new Error('地图树结果分组的会话归属与当前工程上下文不匹配。');
  }
}

export function supportedMapTypes(kind: SessionHdfKind): string[] {
  switch (kind) {
    case 'plan': return ['Project', 'Geometry', 'Plan'];
    case 'result': return ['Project', 'Buildable', 'Geometry'];
    case 'terrain': return ['Project', 'Geometry', 'Plan'];
  }
}

export async function currentProjectXmlRevision(context: WorkbenchContext): Promise<string> {
  if (!context.projectPath || !context.projectRevision || !context.projectXml?.trim() || !context.projectSessionID) {
    throw new Error('请先打开并保存 HEC-RAS 工程。');
  }
  const revision = await projectXmlSha(context.projectXml);
  if (!revision) throw new Error('当前工程 XML 快照无法校验。');
  return revision;
}

export function isSha256(value: unknown): value is string {
  return typeof value === 'string' && /^[\da-f]{64}$/i.test(value);
}

export function sessionHdfKindLabel(kind: SessionHdfKind, language: 'zh' | 'en'): string {
  if (language === 'zh') return kind === 'plan' ? '计划' : kind === 'result' ? '计算结果' : '地形';
  return kind === 'plan' ? 'Plan' : kind === 'result' ? 'Result' : 'Terrain';
}
