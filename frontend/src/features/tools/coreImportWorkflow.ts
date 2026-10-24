import type { AssetReference, WorkbenchContext } from '../../shared/workbench';
import type { ModelSnapshot } from '../editors/model';

export interface CoreLayerImportRequest {
  projectPath: string;
  projectSessionID: string;
  projectRevision: string;
  projectXml: string;
  projectXmlRevision: string;
  sourcePath: string;
  expectedSourceCanonicalPath: string;
  sourceRevision: string;
  action?: 'copyFile' | 'addReference';
  coreLayerContext?: Record<string, unknown>;
}

export interface WorkspaceFileInspection {
  sourcePath: string;
  canonicalPath: string;
  revision: string;
  length: number;
}

export interface CoreLayerImportInspection {
  projectPath: string;
  projectSessionID: string;
  projectRevision: string;
  projectXmlRevision: string;
  sourcePath: string;
  canonicalSourcePath: string;
  sourceRevision: string;
  kind: string;
  name: string;
  description: string;
  fileSize: number;
  availableActions: string[];
  referenceIsSessionOnly: boolean;
}

export interface CoreLayerImportReceipt {
  action: string;
  projectPath: string;
  projectSessionID: string;
  projectRevision: string;
  projectXmlRevision: string;
  sourcePath: string;
  canonicalSourcePath: string;
  sourceRevision: string;
  kind: string;
  name: string;
  reference?: Record<string, unknown> | null;
  importedAssetPath?: string | null;
  importedAssetRevision?: string | null;
  referenceIsSessionOnly: boolean;
}

export interface TerrainInputImportSource {
  id: string;
  path: string;
  name: string;
  revision: string;
  isValid: boolean;
  error?: string | null;
  width?: number | null;
  height?: number | null;
  bandCount?: number | null;
  cellSize?: number | null;
  projection?: string | null;
  minimum?: number | null;
  maximum?: number | null;
  defaultScaleFactor?: number | null;
  overviewCount?: number | null;
  overviews?: string[] | null;
}

export interface TerrainInputImportInspection {
  targetProjection: string;
  sources: TerrainInputImportSource[];
  roundingOptions: string[];
  unitConversionOptions: string[];
  stitchTilesDefault: boolean;
}

export interface TerrainInputImportSelection {
  path: string;
  sourceRevision: string;
  rounding: string;
  unitConversion: string;
  customScaleFactor?: number | null;
  sourceId?: string | null;
  actualScaleFactor?: number | null;
}

export interface TerrainInputAppendRequest {
  model: Record<string, unknown>;
  files: TerrainInputImportSelection[];
  stitchTiles: boolean;
  draftToken: string;
  batchToken: string;
}

export interface TerrainInputOperationResult {
  operationID: string;
  order: number;
  sourceIDs: string[];
  sourcePaths: string[];
  sourceRevisions: string[];
  status: string;
  outputInputID?: string | null;
  outputName?: string | null;
  diagnostic?: string | null;
  progress?: number | null;
}

export interface TerrainInputAppendResult {
  snapshot: ModelSnapshot;
  draftToken: string;
  batchToken: string;
  mode: string;
  batchStatus: string;
  operations: TerrainInputOperationResult[];
}

export interface ClassificationImportFile {
  file: string;
  revision: string;
  kind: 'Raster' | 'Polygon';
  column?: string | null;
  name?: string | null;
}

export interface ClassificationImportItem {
  selection: ClassificationImportFile;
  columns: string[];
  featureCount: number;
  previewFeatureCount: number;
  geoJson?: string | null;
  raster?: { width: number; height: number; rgba: string; minX: number; minY: number; maxX: number; maxY: number; minimum?: number | null; maximum?: number | null } | null;
  rows?: number;
  columnsCount?: number;
  bands?: number;
}

export interface ClassificationImportInspection {
  items: ClassificationImportItem[];
  projection: string;
}

export interface ClassificationImportRequest {
  model: Record<string, unknown>;
  files: ClassificationImportFile[];
}

export function sourceOwnerKey(context: WorkbenchContext): string {
  const asset = context.selectedAsset;
  const locator = asset?.locator && typeof asset.locator === 'object' ? asset.locator as Record<string, unknown> : undefined;
  const core = locator?.coreLayerContext ?? locator?.CoreLayerContext;
  const coreValue = core && typeof core === 'object' && !Array.isArray(core) ? core as Record<string, unknown> : undefined;
  const coreIdentity = coreValue?.identityToken ?? coreValue?.IdentityToken ?? null;
  return JSON.stringify([context.projectSessionID, context.projectPath, context.projectRevision,
    context.projectXml, asset?.path ?? null, asset?.kind ?? null, asset?.revision ?? null,
    modelSourceState(asset ?? null), coreIdentity]);
}

export function modelSourceState(asset: AssetReference | null): string | undefined {
  const locator = asset?.locator;
  if (!locator || typeof locator !== 'object') return undefined;
  const value = (locator as Record<string, unknown>).sourceState ?? (locator as Record<string, unknown>).SourceState;
  return typeof value === 'string' ? value : undefined;
}

export function exactCoreLayerContext(context: WorkbenchContext, asset: AssetReference | null): Record<string, unknown> | undefined {
  const locator = asset?.locator;
  if (!locator || typeof locator !== 'object') return undefined;
  const value = locator as Record<string, unknown>;
  const candidate = value.coreLayerContext ?? value.CoreLayerContext;
  if (!candidate || typeof candidate !== 'object' || Array.isArray(candidate)) return undefined;
  const core = candidate as Record<string, unknown>;
  const projectPath = core.projectPath ?? core.ProjectPath;
  const projectSessionID = core.projectSessionID ?? core.ProjectSessionID;
  const identityToken = core.identityToken ?? core.IdentityToken;
  const projectRevision = core.projectRevision ?? core.ProjectRevision;
  const references = core.references ?? core.References;
  const sessionHdfReferences = core.sessionHdfReferences ?? core.SessionHdfReferences;
  if (projectPath !== context.projectPath || projectSessionID !== context.projectSessionID ||
      typeof identityToken !== 'string' || identityToken.trim().length === 0 ||
      !Array.isArray(references) ||
      (sessionHdfReferences !== undefined && !Array.isArray(sessionHdfReferences)) ||
      (projectRevision !== undefined && projectRevision !== null && typeof projectRevision !== 'string') ||
      (typeof projectRevision === 'string' && context.projectRevision !== projectRevision)) return undefined;
  return core;
}

/** A missing CoreLayerContext is valid for ordinary project-owned models. It is
 * required only when the selected locator already carries session-only captures. */
export function hasUncapturedSessionReferences(context: WorkbenchContext, asset: AssetReference | null): boolean {
  const locator = asset?.locator;
  if (!locator || typeof locator !== 'object') return false;
  const value = locator as Record<string, unknown>;
  const candidate = value.coreLayerContext ?? value.CoreLayerContext;
  if (!candidate || typeof candidate !== 'object' || Array.isArray(candidate)) return false;
  const core = candidate as Record<string, unknown>;
  const refs = core.references ?? core.References;
  const hdfRefs = core.sessionHdfReferences ?? core.SessionHdfReferences;
  if (!Array.isArray(refs) || (hdfRefs !== undefined && !Array.isArray(hdfRefs))) return true;
  const carriesSessionReferences = (Array.isArray(refs) && refs.length > 0) ||
    (Array.isArray(hdfRefs) && hdfRefs.length > 0);
  return carriesSessionReferences && exactCoreLayerContext(context, asset) === undefined;
}

export function modelLoadRequest(context: WorkbenchContext, asset: AssetReference): Record<string, unknown> {
  const core = exactCoreLayerContext(context, asset);
  return {
    assetPath: asset.path,
    kind: asset.kind,
    projectPath: context.projectPath,
    projectXml: context.projectXml,
    ...(modelSourceState(asset) ? { sourceState: modelSourceState(asset) } : {}),
    ...(core ? { coreLayerContext: core } : {}),
  };
}

export function modelSaveRequest(context: WorkbenchContext, asset: AssetReference,
  snapshot: ModelSnapshot): Record<string, unknown> {
  const core = exactCoreLayerContext(context, asset);
  return {
    assetPath: asset.path,
    kind: asset.kind,
    projectPath: context.projectPath,
    projectXml: context.projectXml,
    revision: snapshot.revision,
    fields: {},
    images: {},
    sourceState: snapshot.sourceState ?? modelSourceState(asset) ?? null,
    rows: {},
    samples: {},
    planTimeWindowEdits: [],
    tableSelections: {},
    changedCells: {},
    ...(core ? { coreLayerContext: core } : {}),
  };
}

export function updatedModelContext(context: WorkbenchContext, asset: AssetReference, snapshot: ModelSnapshot): WorkbenchContext {
  const locator = { ...(asset.locator as Record<string, unknown> | undefined) };
  delete locator.SourceState;
  if (snapshot.sourceState == null) delete locator.sourceState;
  else locator.sourceState = snapshot.sourceState;
  return { ...context, selectedAsset: { ...asset, revision: snapshot.revision, locator } };
}

export function requestIdentity(): string {
  return globalThis.crypto?.randomUUID?.().replaceAll('-', '') ??
    `${Date.now().toString(16)}${Math.random().toString(16).slice(2).padEnd(24, '0')}`.slice(0, 32);
}

export function isSha256(value: unknown): value is string {
  return typeof value === 'string' && /^[\da-f]{64}$/i.test(value.trim());
}

export function assertModelSnapshot(value: unknown): asserts value is ModelSnapshot {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('The backend did not return a model snapshot.');
  const snapshot = value as Partial<ModelSnapshot>;
  if (typeof snapshot.revision !== 'string' || !snapshot.revision.trim() ||
      !snapshot.fields || typeof snapshot.fields !== 'object' || Array.isArray(snapshot.fields) ||
      !snapshot.images || typeof snapshot.images !== 'object' || Array.isArray(snapshot.images) ||
      typeof snapshot.canEdit !== 'boolean') throw new Error('The backend returned an incomplete model snapshot.');
}
