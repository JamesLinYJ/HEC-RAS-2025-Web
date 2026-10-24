import { useCallback, useEffect, useRef, useState } from 'react';
import type { BackendGateway, FeaturePageProps, WorkbenchContext } from '../../shared/workbench';
import { errorDiagnostics, message } from '../../shared/workbench';
import { exactCoreLayerContext, hasUncapturedSessionReferences } from '../tools/coreImportWorkflow';
import { catalogAssetKind, walkCatalogTree } from './CatalogTreeSnapshot';
import type { CatalogTreeSnapshot } from './CatalogTreeSnapshot';
import { isProjectTreeSHA256, projectTreeXmlSHA } from './ProjectTreeModelActions';

export type ModelCreationKind = 'newGeometry' | 'newPlan' | 'newBoundary';
export type ModelCreationFields = Record<string, string>;

export interface ModelCreationChoice {
  id: string;
  title: string;
}

export interface CreationWindow {
  start: string;
  end: string;
}

export interface BoundaryCreationDefaults {
  name: string;
  start: string;
  end: string;
}

export interface ModelCreationInspection {
  revision: string;
  canExecute: boolean;
  validationMessage?: string | null;
  warning?: string | null;
  choices: Record<string, ModelCreationChoice[]>;
  boundaryTimeWindows: Record<string, CreationWindow>;
  boundaryDefaults?: BoundaryCreationDefaults | null;
  projectSessionID?: string | null;
  projectPath: string;
  projectRevision?: string | null;
  projectXmlSHA?: string | null;
}

export interface CreatedModel {
  assetPath: string;
  section: string;
  group?: string | null;
  element?: string | null;
  primaryKey?: string | null;
  name: string;
  associations: Record<string, string>;
}

export interface ModelCreationProjectSnapshot {
  success: boolean;
  projectPath: string;
  projectXml: string;
  revision: string;
  projectXmlSHA: string;
  projectSessionID: string;
  coreLayerContext?: Record<string, unknown> | null;
}

export interface ModelCreationCommitReceipt {
  created: CreatedModel;
  project: ModelCreationProjectSnapshot;
  assetRevision: string;
  previousProjectRevision: string;
}

interface ModelCreationRequest {
  kind: ModelCreationKind;
  projectPath: string;
  projectXml: string;
  fields: ModelCreationFields;
  revision?: string;
  coreLayerContext?: Record<string, unknown>;
  projectSessionID: string;
  expectedProjectRevision: string;
  projectXmlSHA: string;
}

interface Props {
  backend: BackendGateway;
  context: WorkbenchContext;
  language: FeaturePageProps['language'];
  kind: ModelCreationKind;
  fields: ModelCreationFields;
  open: boolean;
  hasUnsavedDrafts?: boolean;
  onContextCommit?: FeaturePageProps['onContextCommit'];
  onCreated?: (source: WorkbenchContext, committed: WorkbenchContext, created: CreatedModel) => void;
}

export interface ModelCreationWorkflow {
  inspection: ModelCreationInspection | null;
  busy: 'inspect' | 'create' | null;
  error?: string;
  details?: string;
  notice?: string;
  inspectMatchesFields: boolean;
  canCreate: boolean;
  inspect: () => Promise<void>;
  create: () => Promise<void>;
  invalidate: () => void;
}

function sameServerPath(left: string, right: string): boolean {
  const normalize = (value: string): string => value.replace(/\\/g, '/').replace(/\/+$/, '');
  const a = normalize(left);
  const b = normalize(right);
  const windows = (value: string): boolean => /^[A-Za-z]:\//.test(value) || value.startsWith('//');
  return windows(a) && windows(b) ? a.toLowerCase() === b.toLowerCase() : a === b;
}

function sameProjectSource(left: WorkbenchContext, right: WorkbenchContext): boolean {
  return left.projectSessionID === right.projectSessionID && left.projectPath === right.projectPath &&
    left.projectXml === right.projectXml && left.projectRevision === right.projectRevision &&
    left.selectedAsset?.path === right.selectedAsset?.path && left.selectedAsset?.kind === right.selectedAsset?.kind &&
    left.selectedAsset?.revision === right.selectedAsset?.revision;
}

function fieldsFingerprint(fields: ModelCreationFields): string {
  return JSON.stringify(Object.entries(fields).sort(([left], [right]) => left.localeCompare(right)));
}

function errorDetail(error: unknown): string {
  const details = errorDiagnostics(error);
  if (details instanceof Error) return `${details.name}: ${details.message}${details.stack ? `\n${details.stack}` : ''}`;
  try { return JSON.stringify(details, null, 2) ?? String(details); }
  catch { return String(details); }
}

function requiredName(kind: ModelCreationKind, fields: ModelCreationFields): string {
  if (kind === 'newGeometry') return fields['NewGeometryControl/GeometryName'] ?? '';
  if (kind === 'newPlan') return fields['NewPlanControl/PlanName'] ?? '';
  return fields['NewBoundaryConditionControl/BoundaryConditionName'] ?? '';
}

function expectedSection(kind: ModelCreationKind): string {
  if (kind === 'newGeometry') return 'Geometries';
  if (kind === 'newPlan') return 'Plans';
  return 'Boundary Conditions';
}

function expectedSourceClass(kind: ModelCreationKind): string {
  if (kind === 'newGeometry') return 'ObservableGeometry';
  if (kind === 'newPlan') return 'ObservablePlan';
  return 'ObservableBoundaryCondition';
}

function expectedCatalogAssetKind(kind: ModelCreationKind): string {
  if (kind === 'newGeometry') return 'Geometry';
  if (kind === 'newPlan') return 'Plan';
  return 'Boundary Conditions';
}

function verifyCreatedModelReadback(snapshot: CatalogTreeSnapshot, kind: ModelCreationKind, created: CreatedModel): void {
  if (!snapshot || !Array.isArray(snapshot.roots)) throw new Error('project.tree.load did not return a project tree snapshot.');
  const section = snapshot.roots.find((node) => node.name === expectedSection(kind));
  const matches = walkCatalogTree(section?.children ?? []).filter((node) =>
    node.name === created.name && node.sourceClass === expectedSourceClass(kind) &&
    node.assetID && sameServerPath(node.assetID, created.assetPath) && catalogAssetKind(node) === expectedCatalogAssetKind(kind));
  if (matches.length !== 1) {
    throw new Error(`project.tree.load did not read back exactly one ${expectedSourceClass(kind)} named “${created.name}” at ${created.assetPath}.`);
  }
}

function isAbsoluteServerPath(path: string): boolean {
  return path.startsWith('/') || /^\\\\[^\\]+\\[^\\]+/.test(path) || /^[A-Za-z]:[\\/]/.test(path);
}

function choiceTitle(inspection: ModelCreationInspection, field: string, id: string): string | undefined {
  return inspection.choices[field]?.find((choice) => sameServerPath(choice.id, id))?.title;
}

function validateCreatedModel(kind: ModelCreationKind, fields: ModelCreationFields,
  inspection: ModelCreationInspection, created: CreatedModel): void {
  const name = requiredName(kind, fields);
  if (created.name !== name || created.section !== expectedSection(kind) || !isAbsoluteServerPath(created.assetPath) ||
      !created.assetPath.toLowerCase().endsWith(`${name}.h5`.toLowerCase()) || !created.associations || typeof created.associations !== 'object') {
    throw new Error('模型创建回执中的名称、目录段、文件路径或关联字段与检查请求不匹配。');
  }
  if (kind === 'newGeometry') {
    const terrainPath = fields['NewGeometryControl/SelectedTerrain'] ?? '';
    const nvaluePath = fields['NewGeometryControl/SelectedNValue'] ?? '';
    const expectedTerrain = terrainPath ? choiceTitle(inspection, 'NewGeometryControl/SelectedTerrain', terrainPath) : '';
    const expectedNValue = nvaluePath ? choiceTitle(inspection, 'NewGeometryControl/SelectedNValue', nvaluePath) : '';
    if (created.associations.Terrain !== expectedTerrain || created.associations.N_Values !== expectedNValue) {
      throw new Error('模型创建回执中的 Terrain 或 N_Values 关联与检查选项不匹配。');
    }
  }
  if (kind === 'newPlan') {
    const geometryPath = fields['NewPlanControl/SelectedGeometry'] ?? '';
    const boundaryPath = fields['NewPlanControl/SelectedBoundaryCondition'] ?? '';
    const expectedGeometry = choiceTitle(inspection, 'NewPlanControl/SelectedGeometry', geometryPath);
    const expectedBoundary = choiceTitle(inspection, 'NewPlanControl/SelectedBoundaryCondition', boundaryPath);
    if (!expectedGeometry || !expectedBoundary || created.associations.Geometry !== expectedGeometry ||
        created.associations.BoundaryCondition !== expectedBoundary) {
      throw new Error('方案创建回执中的 Geometry 或 BoundaryCondition 关联与检查选项不匹配。');
    }
  }
}

function updateSelectedAssetAfterProjectCommit(source: WorkbenchContext, project: ModelCreationProjectSnapshot): WorkbenchContext['selectedAsset'] {
  const selected = source.selectedAsset;
  if (!selected) return null;
  if (selected.kind === 'ProjectFile' && sameServerPath(selected.path, project.projectPath)) {
    return { ...selected, revision: project.revision };
  }
  if (!project.coreLayerContext || !selected.locator || typeof selected.locator !== 'object') return selected;
  const locator = selected.locator as Record<string, unknown>;
  const nextLocator = { ...locator };
  if ('coreLayerContext' in locator || !('CoreLayerContext' in locator)) nextLocator.coreLayerContext = project.coreLayerContext;
  if ('CoreLayerContext' in locator) nextLocator.CoreLayerContext = project.coreLayerContext;
  return { ...selected, locator: nextLocator };
}

export function useProjectModelCreation({
  backend,
  context,
  language,
  kind,
  fields,
  open,
  hasUnsavedDrafts = false,
  onContextCommit,
  onCreated,
}: Props): ModelCreationWorkflow {
  const [inspection, setInspection] = useState<ModelCreationInspection | null>(null);
  const [inspectedFields, setInspectedFields] = useState<string>();
  const [busy, setBusy] = useState<'inspect' | 'create' | null>(null);
  const [error, setError] = useState<string>();
  const [details, setDetails] = useState<string>();
  const [notice, setNotice] = useState<string>();
  const requestGeneration = useRef(0);
  const abort = useRef<AbortController | undefined>(undefined);
  const contextRef = useRef(context);
  const fieldsRef = useRef(fields);
  contextRef.current = context;
  fieldsRef.current = fields;
  const zh = language === 'zh';
  const currentFieldsFingerprint = fieldsFingerprint(fields);
  const inspectMatchesFields = Boolean(inspection && inspectedFields === currentFieldsFingerprint);
  const canCreate = Boolean(inspection?.canExecute && inspectMatchesFields && backend.supports('modelCreation.commit') &&
    backend.supports('project.tree.load') &&
    onContextCommit && !hasUnsavedDrafts && busy === null);

  const invalidate = useCallback((): void => {
    // Changing a form field invalidates execution, not the inspected project
    // choices/time windows. Keep those available while the user finishes editing.
    setInspection(current => current ? { ...current, canExecute: false, validationMessage: null, warning: null } : null);
    setInspectedFields(undefined);
    setError(undefined);
    setDetails(undefined);
    setNotice(undefined);
  }, []);

  const currentSource = useCallback((): WorkbenchContext => contextRef.current, []);

  const inspect = useCallback(async (): Promise<void> => {
    const source = currentSource();
    const sourceXml = source.projectXml;
    const projectPath = source.projectPath;
    const projectRevision = source.projectRevision;
    const projectSessionID = source.projectSessionID;
    const fieldsAtRequest = { ...fieldsRef.current };
    const fingerprint = fieldsFingerprint(fieldsAtRequest);
    if (!backend.supports('modelCreation.inspect')) {
      setError(zh ? '当前 C# 服务未发布 modelCreation.inspect。' : 'The C# service has not advertised modelCreation.inspect.');
      return;
    }
    if (!projectPath || !sourceXml || !isProjectTreeSHA256(projectRevision) || !isAbsoluteServerPath(projectPath)) {
      setError(zh ? '请先打开具有服务器路径、工程 XML 和 SHA-256 修订的已保存工程。' : 'Open a saved project with a server path, project XML, and SHA-256 revision first.');
      return;
    }
    if (hasUncapturedSessionReferences(source, source.selectedAsset)) {
      setError(zh ? '当前资产携带未捕获的会话专属模型引用，无法安全创建关联。' : 'The selected asset has uncaptured session-only model references; related model creation is unsafe.');
      return;
    }
    const generation = ++requestGeneration.current;
    abort.current?.abort();
    const controller = new AbortController();
    abort.current = controller;
    setBusy('inspect');
    setError(undefined);
    setDetails(undefined);
    setNotice(undefined);
    setInspection(null);
    setInspectedFields(undefined);
    try {
      const projectXmlSHA = await projectTreeXmlSHA(sourceXml);
      if (generation !== requestGeneration.current || controller.signal.aborted || !sameProjectSource(contextRef.current, source)) return;
      const coreLayerContext = exactCoreLayerContext(source, source.selectedAsset);
      const request: ModelCreationRequest = {
        kind,
        projectPath,
        projectXml: sourceXml,
        fields: fieldsAtRequest,
        projectSessionID,
        expectedProjectRevision: projectRevision,
        projectXmlSHA,
        ...(coreLayerContext ? { coreLayerContext } : {}),
      };
      const result = await backend.request<ModelCreationInspection>('modelCreation.inspect', request, { signal: controller.signal });
      if (generation !== requestGeneration.current || controller.signal.aborted || !sameProjectSource(contextRef.current, source)) return;
      if (!result || !isProjectTreeSHA256(result.revision) || typeof result.canExecute !== 'boolean' ||
          !sameServerPath(result.projectPath, projectPath) || result.projectSessionID?.toLowerCase() !== projectSessionID.toLowerCase() ||
          (result.projectRevision && result.projectRevision.toUpperCase() !== projectRevision.toUpperCase()) ||
          (result.projectXmlSHA && result.projectXmlSHA.toUpperCase() !== projectXmlSHA.toUpperCase())) {
        throw new Error(zh ? 'modelCreation.inspect 的工程路径、会话、修订或回执与当前工程不匹配。' : 'modelCreation.inspect returned a project path, session, revision, or receipt that does not match the current project.');
      }
      setInspection(result);
      setInspectedFields(fingerprint);
      if (result.validationMessage) setNotice(result.validationMessage);
    } catch (requestError) {
      if (generation !== requestGeneration.current || controller.signal.aborted) return;
      setError(`${message(requestError, language)}${zh ? ' 展开查看原始诊断。' : ' Expand to view raw diagnostics.'}`);
      setDetails(errorDetail(requestError));
    } finally {
      if (generation === requestGeneration.current && !controller.signal.aborted) setBusy(null);
    }
  }, [backend, currentSource, kind, language, zh]);

  const create = useCallback(async (): Promise<void> => {
    if (!canCreate || !inspection || inspectedFields !== fieldsFingerprint(fieldsRef.current)) return;
    const source = currentSource();
    const sourceXml = source.projectXml;
    const projectPath = source.projectPath;
    const projectRevision = source.projectRevision;
    const projectSessionID = source.projectSessionID;
    const fieldsAtRequest = { ...fieldsRef.current };
    const fingerprint = fieldsFingerprint(fieldsAtRequest);
    if (!sourceXml || !projectPath || !isProjectTreeSHA256(projectRevision)) return;
    const generation = ++requestGeneration.current;
    abort.current?.abort();
    const controller = new AbortController();
    abort.current = controller;
    setBusy('create');
    setError(undefined);
    setDetails(undefined);
    setNotice(undefined);
    try {
      const projectXmlSHA = await projectTreeXmlSHA(sourceXml);
      if (generation !== requestGeneration.current || controller.signal.aborted || !sameProjectSource(contextRef.current, source)) return;
      const coreLayerContext = exactCoreLayerContext(source, source.selectedAsset);
      const request: ModelCreationRequest = {
        kind,
        projectPath,
        projectXml: sourceXml,
        fields: fieldsAtRequest,
        revision: inspection.revision,
        projectSessionID,
        expectedProjectRevision: projectRevision,
        projectXmlSHA,
        ...(coreLayerContext ? { coreLayerContext } : {}),
      };
      const receipt = await backend.request<ModelCreationCommitReceipt>('modelCreation.commit', request, { signal: controller.signal });
      if (generation !== requestGeneration.current || controller.signal.aborted || !sameProjectSource(contextRef.current, source)) return;
      if (!receipt || !receipt.created || !receipt.project || receipt.project.success !== true ||
          typeof receipt.project.projectPath !== 'string' || typeof receipt.project.projectSessionID !== 'string' ||
          typeof receipt.project.projectXml !== 'string' || typeof receipt.project.projectXmlSHA !== 'string' ||
          typeof receipt.project.revision !== 'string' || typeof receipt.assetRevision !== 'string' ||
          typeof receipt.previousProjectRevision !== 'string' ||
          !sameServerPath(receipt.project.projectPath, projectPath) ||
          receipt.project.projectSessionID.toLowerCase() !== projectSessionID.toLowerCase() ||
          !isProjectTreeSHA256(receipt.project.revision) || !isProjectTreeSHA256(receipt.project.projectXmlSHA) ||
          !isProjectTreeSHA256(receipt.assetRevision) || !isProjectTreeSHA256(receipt.previousProjectRevision) ||
          receipt.previousProjectRevision.toUpperCase() !== projectRevision.toUpperCase()) {
        throw new Error(zh ? '创建回执缺少匹配的工程路径、会话、旧修订、新 XML/SHA 或资产 SHA-256。' : 'The creation receipt is missing a matching project path, session, prior revision, new XML/SHA, or asset SHA-256.');
      }
      if (await projectTreeXmlSHA(receipt.project.projectXml) !== receipt.project.projectXmlSHA.toUpperCase()) {
        throw new Error(zh ? '创建回执中的工程 XML SHA-256 不匹配。' : 'The project XML SHA-256 in the creation receipt does not match.');
      }
      validateCreatedModel(kind, fieldsAtRequest, inspection, receipt.created);
      if (!backend.supports('project.tree.load')) {
        throw new Error(zh ? '当前 C# 服务未发布 project.tree.load，无法读回核验新模型。' : 'The C# service has not advertised project.tree.load, so the created model cannot be verified by read-back.');
      }
      const readback = await backend.request<CatalogTreeSnapshot>('project.tree.load', {
        projectPath: receipt.project.projectPath,
        projectXml: receipt.project.projectXml,
        projectSessionID: receipt.project.projectSessionID,
        mode: 'Project',
        ...(receipt.project.coreLayerContext ? { coreLayerContext: receipt.project.coreLayerContext } : {}),
      }, { signal: controller.signal });
      if (generation !== requestGeneration.current || controller.signal.aborted || !sameProjectSource(contextRef.current, source)) return;
      verifyCreatedModelReadback(readback, kind, receipt.created);
      if (generation !== requestGeneration.current || controller.signal.aborted || !sameProjectSource(contextRef.current, source) ||
          fieldsFingerprint(fieldsRef.current) !== fingerprint) return;
      const committed: WorkbenchContext = {
        ...source,
        projectXml: receipt.project.projectXml,
        projectRevision: receipt.project.revision,
        selectedAsset: updateSelectedAssetAfterProjectCommit(source, receipt.project),
      };
      const accepted = onContextCommit?.(source, committed) ?? false;
      if (!accepted) {
        setNotice(zh
          ? `模型文件“${receipt.created.name}”已写入，但父级工程会话拒绝了旧请求回执；未更新当前上下文。请刷新工程目录核对。`
          : `Model file “${receipt.created.name}” was written, but the parent session rejected the stale receipt. The current context was not updated; refresh the project catalog to verify it.`);
        return;
      }
      setNotice(zh ? `已创建“${receipt.created.name}”，并提交工程回执。` : `Created “${receipt.created.name}” and committed the project receipt.`);
      onCreated?.(source, committed, receipt.created);
    } catch (requestError) {
      if (generation !== requestGeneration.current || controller.signal.aborted) return;
      setError(`${message(requestError, language)}${zh ? ' 展开查看原始诊断；若文件已写入，请刷新目录核对。' : ' Expand raw diagnostics; if the file was written, refresh the catalog to verify it.'}`);
      setDetails(errorDetail(requestError));
    } finally {
      if (generation === requestGeneration.current && !controller.signal.aborted) setBusy(null);
    }
  }, [backend, canCreate, currentSource, inspection, inspectedFields, kind, language, onContextCommit, onCreated, zh]);

  useEffect(() => {
    if (open) {
      void inspect();
      return;
    }
    requestGeneration.current += 1;
    abort.current?.abort();
    abort.current = undefined;
    setInspection(null);
    setInspectedFields(undefined);
    setBusy(null);
  }, [context.projectPath, context.projectRevision, context.projectSessionID, context.projectXml, inspect, open]);

  return { inspection, busy, error, details, notice, inspectMatchesFields, canCreate, inspect, create, invalidate };
}
