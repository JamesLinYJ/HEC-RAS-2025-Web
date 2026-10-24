import type { AssetReference, BackendGateway, WorkbenchContext } from '../../../shared/workbench';
import { exactCoreLayerContext } from '../../tools/coreImportWorkflow';
import { isProjectTreeSHA256, projectTreeXmlSHA } from '../../project/ProjectTreeModelActions';
import type {
  AssociationCommitReceipt,
  AssociationOption,
  AssociationOptionKind,
  AssociationProjectSnapshot,
  AssociationSourceDependency,
  AssociationState,
  AssociationTargetIdentity,
  AssociationTargetKind,
  ProjectAssociationInspection,
} from './AssociationTypes';

export interface ProjectAssociationInspectRequest {
  targetKind: AssociationTargetKind;
  projectPath: string;
  projectXml: string;
  projectXmlSHA: string;
  projectRevision: string;
  projectSessionID: string;
  targetAssetPath: string;
  targetAssetRevision: string;
  coreLayerContext?: Record<string, unknown>;
}

export interface AssociationSelection {
  geometryPath: string | null;
  geometrySHA256: string | null;
  boundaryConditionPath: string | null;
  boundaryConditionSHA256: string | null;
  terrainPath: string | null;
  terrainSHA256: string | null;
  clearTerrain: boolean;
  nValuePath: string | null;
  nValueSHA256: string | null;
  clearNValue: boolean;
}

export interface ProjectAssociationCommitRequest extends ProjectAssociationInspectRequest, AssociationSelection {
  expectedTargetSHA256: string;
  expectedAssociationSHA256: string;
  expectedOptionsSHA256: string;
  expectedSourceReferencesSHA256: string;
}

export interface AssociationOperationInput {
  backend: BackendGateway;
  context: WorkbenchContext;
  activeAsset: AssetReference;
  targetKind: AssociationTargetKind;
  modelRevision: string;
  signal?: AbortSignal;
}

export class StaleAssociationInspectionError extends Error {
  constructor(readonly freshInspection: ProjectAssociationInspection) {
    super('工程关联或候选源已变化，已重新载入当前选项。请核对后再次应用。');
    this.name = 'StaleAssociationInspectionError';
  }
}

export function isServerPathEqual(left: string, right: string): boolean {
  const normalize = (value: string): string => value.replace(/\\/g, '/').replace(/\/+$/, '');
  const a = normalize(left);
  const b = normalize(right);
  const windows = (value: string): boolean => /^[A-Za-z]:\//.test(value) || value.startsWith('//');
  return windows(a) && windows(b) ? a.toLowerCase() === b.toLowerCase() : a === b;
}

function requiredProjectSource(context: WorkbenchContext, activeAsset: AssetReference, modelRevision: string): ProjectAssociationInspectRequest {
  if (!context.projectPath || !context.projectXml || !context.projectRevision || !context.projectSessionID ||
      !isProjectTreeSHA256(context.projectRevision) || !activeAsset.path || !isProjectTreeSHA256(modelRevision)) {
    throw new Error('关联编辑需要已保存的工程、有效模型修订与活动工程会话。');
  }
  if (!context.selectedAsset || !isServerPathEqual(context.selectedAsset.path, activeAsset.path) ||
      context.selectedAsset.kind.toLowerCase() !== activeAsset.kind.toLowerCase()) {
    throw new Error('当前模型选择与工程会话不匹配，请重新选择模型。');
  }
  const locator = activeAsset.locator && typeof activeAsset.locator === 'object'
    ? activeAsset.locator as Record<string, unknown> : {};
  const carriedCoreContext = locator.coreLayerContext ?? locator.CoreLayerContext;
  if (carriedCoreContext != null && !exactCoreLayerContext(context, activeAsset)) {
    throw new Error('当前模型携带的工程来源校验已过期，请重新载入模型后再修改关联。');
  }
  if (activeAsset.kind.toLowerCase() !== 'plan' && activeAsset.kind.toLowerCase() !== 'geometry') {
    throw new Error('关联编辑仅支持工程计划和几何。');
  }
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(context.projectSessionID)) {
    throw new Error('当前工程会话标识无效，请重新打开工程。');
  }
  return {
    targetKind: activeAsset.kind.toLowerCase() === 'plan' ? 'plan' : 'geometry',
    projectPath: context.projectPath,
    projectXml: context.projectXml,
    projectXmlSHA: '',
    projectRevision: context.projectRevision,
    projectSessionID: context.projectSessionID,
    targetAssetPath: activeAsset.path,
    targetAssetRevision: modelRevision,
  };
}

function isOption(value: unknown): value is AssociationOption {
  if (!value || typeof value !== 'object') return false;
  const option = value as Partial<AssociationOption>;
  return typeof option.name === 'string' && option.name.length > 0 &&
    typeof option.path === 'string' && option.path.length > 0 &&
    option.exists === true &&
    isProjectTreeSHA256(option.revision) && isProjectTreeSHA256(option.sha256);
}

function isOptionalOption(value: unknown): value is AssociationOption | null {
  return value === null || isOption(value);
}

function isDependency(value: unknown): value is AssociationSourceDependency {
  if (!value || typeof value !== 'object') return false;
  const dependency = value as Partial<AssociationSourceDependency>;
  if (typeof dependency.kind !== 'string' || typeof dependency.name !== 'string' ||
      typeof dependency.path !== 'string' || typeof dependency.canonicalPath !== 'string' ||
      typeof dependency.exists !== 'boolean') return false;
  return dependency.exists
    ? isProjectTreeSHA256(dependency.revision) && isProjectTreeSHA256(dependency.sha256)
    : (dependency.revision === null && dependency.sha256 === null);
}

function isAssociationState(value: unknown, targetKind: AssociationTargetKind): value is AssociationState {
  if (!value || typeof value !== 'object') return false;
  const state = value as AssociationState;
  if (targetKind === 'plan') return isOptionalOption(state.geometry) && isOptionalOption(state.boundaryCondition);
  return isOptionalOption(state.terrain) && isOptionalOption(state.nValue);
}

function isTarget(value: unknown): value is AssociationTargetIdentity {
  if (!value || typeof value !== 'object') return false;
  const target = value as Partial<AssociationTargetIdentity>;
  return (target.kind === 'plan' || target.kind === 'geometry') &&
    typeof target.name === 'string' && target.name.length > 0 && typeof target.path === 'string' &&
    isProjectTreeSHA256(target.revision) && isProjectTreeSHA256(target.sha256);
}

function compareSHA(left: string | null | undefined, right: string | null | undefined): boolean {
  return (left ?? '').toUpperCase() === (right ?? '').toUpperCase();
}

function sameOption(left: AssociationOption | null | undefined, right: AssociationOption | null | undefined): boolean {
  if (!left || !right) return left == null && right == null;
  return left.name === right.name && isServerPathEqual(left.path, right.path) &&
    left.exists === right.exists &&
    compareSHA(left.revision, right.revision) && compareSHA(left.sha256, right.sha256);
}

export function sameAssociationState(left: AssociationState, right: AssociationState, targetKind: AssociationTargetKind): boolean {
  if (targetKind === 'plan') return sameOption(left.geometry, right.geometry) && sameOption(left.boundaryCondition, right.boundaryCondition);
  return sameOption(left.terrain, right.terrain) && sameOption(left.nValue, right.nValue);
}

function sameDependencySet(left: AssociationSourceDependency[], right: AssociationSourceDependency[]): boolean {
  const key = (item: AssociationSourceDependency): string => JSON.stringify([
    item.kind, item.name, item.path, item.canonicalPath, item.revision?.toUpperCase() ?? null,
    item.sha256?.toUpperCase() ?? null, item.exists,
  ]);
  return left.map(key).sort().join('\n') === right.map(key).sort().join('\n');
}

function validateInspection(value: ProjectAssociationInspection, request: ProjectAssociationInspectRequest, modelRevision: string): void {
  if (!value || typeof value.projectSessionID !== 'string' || value.targetKind !== request.targetKind || !isServerPathEqual(value.projectPath, request.projectPath) ||
      !compareSHA(value.projectRevision, request.projectRevision) || !isProjectTreeSHA256(value.projectXmlSHA) ||
      !compareSHA(value.projectXmlSHA, request.projectXmlSHA) ||
      value.projectSessionID.toLowerCase() !== request.projectSessionID.toLowerCase() ||
      !isTarget(value.target) || value.target.kind !== request.targetKind ||
      !isServerPathEqual(value.target.path, request.targetAssetPath) ||
      !compareSHA(value.target.revision, modelRevision) || !compareSHA(value.target.sha256, modelRevision) ||
      !isAssociationState(value.before, request.targetKind) ||
      !isProjectTreeSHA256(value.associationSHA256) || !isProjectTreeSHA256(value.optionsSHA256) ||
      !isProjectTreeSHA256(value.sourceReferencesSHA256) || !Array.isArray(value.dependencies) ||
      !value.dependencies.every(isDependency) || !value.options ||
      !(['geometry', 'boundaryCondition', 'terrain', 'nValue'] as AssociationOptionKind[])
        .every((kind) => Array.isArray(value.options[kind]) && value.options[kind].every(isOption))) {
    throw new Error('工程服务返回的关联检查结果与当前工程、模型修订或候选源不匹配。');
  }
}

export async function inspectProjectAssociations(input: AssociationOperationInput): Promise<{
  inspection: ProjectAssociationInspection;
  request: ProjectAssociationInspectRequest;
}> {
  if (!input.backend.supports('project.association.inspect')) throw new Error('当前服务未提供工程关联检查。');
  const assetKind = input.activeAsset.kind.toLowerCase();
  if ((assetKind === 'plan' ? 'plan' : assetKind === 'geometry' ? 'geometry' : null) !== input.targetKind) {
    throw new Error('关联目标类型与当前模型不匹配。');
  }
  const request = requiredProjectSource(input.context, input.activeAsset, input.modelRevision);
  request.projectXmlSHA = await projectTreeXmlSHA(request.projectXml);
  const coreLayerContext = exactCoreLayerContext(input.context, input.activeAsset);
  if (coreLayerContext) request.coreLayerContext = coreLayerContext;
  if (input.signal?.aborted) throw new DOMException('The request was aborted.', 'AbortError');
  const inspection = await input.backend.request<ProjectAssociationInspection>('project.association.inspect', request, { signal: input.signal });
  validateInspection(inspection, request, input.modelRevision);
  return { inspection, request };
}

function selectedOption(options: AssociationOption[], path: string, label: string): AssociationOption {
  const option = options.find((candidate) => isServerPathEqual(candidate.path, path));
  if (!option) throw new Error(`所选${label}已不在当前工程选项中，请重新选择。`);
  return option;
}

function selectionFromDraft(
  targetKind: AssociationTargetKind,
  inspection: ProjectAssociationInspection,
  draft: AssociationState,
): AssociationSelection {
  if (targetKind === 'plan') {
    if (!draft.geometry?.path || !draft.boundaryCondition?.path) throw new Error('计划必须关联几何和边界条件。');
    const geometry = selectedOption(inspection.options.geometry, draft.geometry.path, '几何');
    const boundaryCondition = selectedOption(inspection.options.boundaryCondition, draft.boundaryCondition.path, '边界条件');
    return {
      geometryPath: geometry.path, geometrySHA256: geometry.sha256,
      boundaryConditionPath: boundaryCondition.path, boundaryConditionSHA256: boundaryCondition.sha256,
      terrainPath: null, terrainSHA256: null, clearTerrain: false,
      nValuePath: null, nValueSHA256: null, clearNValue: false,
    };
  }
  const terrain = draft.terrain?.path ? selectedOption(inspection.options.terrain, draft.terrain.path, '地形') : null;
  const nValue = draft.nValue?.path ? selectedOption(inspection.options.nValue, draft.nValue.path, '糙率图层') : null;
  return {
    geometryPath: null, geometrySHA256: null,
    boundaryConditionPath: null, boundaryConditionSHA256: null,
    terrainPath: terrain?.path ?? null, terrainSHA256: terrain?.sha256 ?? null, clearTerrain: terrain === null,
    nValuePath: nValue?.path ?? null, nValueSHA256: nValue?.sha256 ?? null, clearNValue: nValue === null,
  };
}

function expectedAfter(targetKind: AssociationTargetKind, selection: AssociationSelection, inspection: ProjectAssociationInspection): AssociationState {
  if (targetKind === 'plan') return {
    geometry: selectedOption(inspection.options.geometry, selection.geometryPath!, '几何'),
    boundaryCondition: selectedOption(inspection.options.boundaryCondition, selection.boundaryConditionPath!, '边界条件'),
  };
  return {
    terrain: selection.terrainPath ? selectedOption(inspection.options.terrain, selection.terrainPath, '地形') : null,
    nValue: selection.nValuePath ? selectedOption(inspection.options.nValue, selection.nValuePath, '糙率图层') : null,
  };
}

function validCommittedCoreLayerContext(
  value: Record<string, unknown> | null | undefined,
  project: AssociationProjectSnapshot,
): boolean {
  if (value == null) return true;
  const projectPath = value.projectPath ?? value.ProjectPath;
  const projectRevision = value.projectRevision ?? value.ProjectRevision;
  const projectSessionID = value.projectSessionID ?? value.ProjectSessionID;
  return typeof projectPath === 'string' && isServerPathEqual(projectPath, project.projectPath) &&
    typeof projectRevision === 'string' && compareSHA(projectRevision, project.revision) &&
    typeof projectSessionID === 'string' && projectSessionID.toLowerCase() === project.projectSessionID.toLowerCase();
}

function sameInspectionBasis(left: ProjectAssociationInspection, right: ProjectAssociationInspection): boolean {
  return left.targetKind === right.targetKind && isServerPathEqual(left.projectPath, right.projectPath) &&
    left.projectSessionID.toLowerCase() === right.projectSessionID.toLowerCase() &&
    compareSHA(left.projectRevision, right.projectRevision) && compareSHA(left.projectXmlSHA, right.projectXmlSHA) &&
    left.target.name === right.target.name && isServerPathEqual(left.target.path, right.target.path) &&
    compareSHA(left.target.revision, right.target.revision) && compareSHA(left.target.sha256, right.target.sha256) &&
    compareSHA(left.associationSHA256, right.associationSHA256) &&
    compareSHA(left.optionsSHA256, right.optionsSHA256) &&
    compareSHA(left.sourceReferencesSHA256, right.sourceReferencesSHA256) &&
    sameAssociationState(left.before, right.before, left.targetKind) &&
    sameDependencySet(left.dependencies, right.dependencies);
}

function validateReceipt(
  receipt: AssociationCommitReceipt,
  context: WorkbenchContext,
  request: ProjectAssociationInspectRequest,
  inspection: ProjectAssociationInspection,
  selection: AssociationSelection,
): void {
  const project: AssociationProjectSnapshot | null | undefined = receipt?.project;
  if (!receipt || receipt.success !== true || receipt.targetKind !== inspection.targetKind ||
      !project || project.success !== true || typeof project.projectSessionID !== 'string' || !isServerPathEqual(project.projectPath, context.projectPath ?? '') ||
      project.projectSessionID.toLowerCase() !== context.projectSessionID.toLowerCase() ||
      !compareSHA(receipt.previousProjectRevision, context.projectRevision) ||
      !isProjectTreeSHA256(receipt.previousProjectRevision) || !isProjectTreeSHA256(project.revision) ||
      !isProjectTreeSHA256(project.projectXmlSHA) || typeof project.projectXml !== 'string' ||
      !compareSHA(project.revision, project.projectXmlSHA) ||
      (request.coreLayerContext != null && project.coreLayerContext == null) ||
      !validCommittedCoreLayerContext(project.coreLayerContext, project) ||
      !isTarget(receipt.target) || receipt.target.kind !== inspection.targetKind ||
      receipt.target.name !== inspection.target.name || !isServerPathEqual(receipt.target.path, inspection.target.path) ||
      !compareSHA(receipt.target.revision, inspection.target.revision) || !compareSHA(receipt.target.sha256, inspection.target.sha256) ||
      !isAssociationState(receipt.before, inspection.targetKind) || !sameAssociationState(receipt.before, inspection.before, inspection.targetKind) ||
      !isAssociationState(receipt.after, inspection.targetKind) || !sameAssociationState(receipt.after, expectedAfter(inspection.targetKind, selection, inspection), inspection.targetKind) ||
      !isProjectTreeSHA256(receipt.previousSourceReferencesSHA256) ||
      !compareSHA(receipt.previousSourceReferencesSHA256, inspection.sourceReferencesSHA256) ||
      !isProjectTreeSHA256(receipt.sourceReferencesSHA256) || !receipt.sourceCapture ||
      !isProjectTreeSHA256(receipt.sourceCapture.sourceReferencesSHA256) ||
      !compareSHA(receipt.sourceCapture.sourceReferencesSHA256, inspection.sourceReferencesSHA256) ||
      !compareSHA(receipt.sourceReferencesSHA256, receipt.sourceCapture.sourceReferencesSHA256) ||
      !Array.isArray(receipt.sourceCapture.dependencies) ||
      !receipt.sourceCapture.dependencies.every(isDependency) ||
      !sameDependencySet(receipt.sourceCapture.dependencies, inspection.dependencies)) {
    throw new Error('工程服务未返回与本次工程、模型、关联和源文件捕获相匹配的完整提交回执。');
  }
}

function committedContext(context: WorkbenchContext, asset: AssetReference, project: AssociationProjectSnapshot): WorkbenchContext {
  const current = context.selectedAsset;
  if (!current || !isServerPathEqual(current.path, asset.path) || current.kind.toLowerCase() !== asset.kind.toLowerCase()) {
    throw new Error('提交期间活动模型已变化，无法将回执应用到其他模型。');
  }
  const locator = { ...((current.locator && typeof current.locator === 'object') ? current.locator as Record<string, unknown> : {}) };
  if (project.coreLayerContext) {
    if ('CoreLayerContext' in locator) locator.CoreLayerContext = project.coreLayerContext;
    locator.coreLayerContext = project.coreLayerContext;
  }
  return {
    ...context,
    projectXml: project.projectXml,
    projectRevision: project.revision,
    selectedAsset: { ...current, locator },
  };
}

export async function commitProjectAssociations(input: AssociationOperationInput, draft: AssociationState, initial: ProjectAssociationInspection): Promise<{
  receipt: AssociationCommitReceipt;
  committed: WorkbenchContext;
}> {
  if (!input.backend.supports('project.association.commit')) throw new Error('当前服务未提供工程关联提交。');
  const latest = await inspectProjectAssociations(input);
  if (!latest.inspection.canCommit) throw new Error(latest.inspection.validationMessage || '工程服务当前不允许修改此关联。');
  if (!sameInspectionBasis(initial, latest.inspection)) throw new StaleAssociationInspectionError(latest.inspection);
  const selection = selectionFromDraft(input.targetKind, latest.inspection, draft);
  const request: ProjectAssociationCommitRequest = {
    ...latest.request,
    ...selection,
    expectedTargetSHA256: latest.inspection.target.sha256,
    expectedAssociationSHA256: latest.inspection.associationSHA256,
    expectedOptionsSHA256: latest.inspection.optionsSHA256,
    expectedSourceReferencesSHA256: latest.inspection.sourceReferencesSHA256,
  };
  if (input.signal?.aborted) throw new DOMException('The request was aborted.', 'AbortError');
  const receipt = await input.backend.request<AssociationCommitReceipt>('project.association.commit', request, { signal: input.signal });
  validateReceipt(receipt, input.context, latest.request, latest.inspection, selection);
  const projectXmlSHA = await projectTreeXmlSHA(receipt.project.projectXml);
  if (!compareSHA(projectXmlSHA, receipt.project.projectXmlSHA)) throw new Error('提交回执中的工程 XML 与 SHA-256 不匹配。');
  return { receipt, committed: committedContext(input.context, input.activeAsset, receipt.project) };
}
