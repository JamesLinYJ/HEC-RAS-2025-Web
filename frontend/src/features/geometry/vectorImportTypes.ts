import type { AttributeTableSourceFamilyCapture } from '../tools/attributes/attributeTableTypes';
import { isShapefileFamilyCapture, sameSourcePath } from '../tools/attributes/attributeTableTypes';
import type { GeometryTopologySnapshot } from './geometryTypes';

/** Exact capture required by geometry.vectorImport.inspect/apply. */
export interface GeometryVectorImportCapture {
  revision: string;
  draftState: string;
  draftIdentity: string;
}

/** Exact source selection accepted by GeometryVectorImportApplyRequest. */
export interface GeometryVectorImportSelectedSource {
  path: string;
  family: AttributeTableSourceFamilyCapture;
}

export interface GeometryVectorImportFeature {
  sourceOrdinal: number;
  geometryType: 'polyline' | 'polygon';
  points: Array<{ x: number; y: number }>;
}

export interface GeometryVectorImportSource extends GeometryVectorImportSelectedSource {
  name: string;
  shapeType: 'Polyline' | 'Polygon';
  shapeTypeCode: number;
  featureCount: number;
  features: GeometryVectorImportFeature[];
}

export interface GeometryVectorImportIssue {
  path: string;
  message: string;
}

/** Host inspection returns the saved revision and draft identity; draftState remains request-only. */
export interface GeometryVectorImportInspection {
  revision: string;
  draftIdentity: string;
  sources: GeometryVectorImportSource[];
  issues: GeometryVectorImportIssue[];
}

export type GeometryVectorImportMode = 'Merge' | 'NoMerge';

function record(value: unknown): Record<string, unknown> | undefined {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? value as Record<string, unknown> : undefined;
}

function validFeature(value: unknown): value is GeometryVectorImportFeature {
  const feature = record(value);
  if (!feature || typeof feature.sourceOrdinal !== 'number' || !Number.isInteger(feature.sourceOrdinal) || feature.sourceOrdinal < 0 ||
      (feature.geometryType !== 'polyline' && feature.geometryType !== 'polygon') || !Array.isArray(feature.points)) return false;
  return feature.points.every((raw) => {
    const point = record(raw);
    return point !== undefined && typeof point.x === 'number' && Number.isFinite(point.x) &&
      typeof point.y === 'number' && Number.isFinite(point.y);
  });
}

function validSource(value: unknown): value is GeometryVectorImportSource {
  const source = record(value);
  if (!source || typeof source.path !== 'string' || !source.path || typeof source.name !== 'string' ||
      (source.shapeType !== 'Polyline' && source.shapeType !== 'Polygon') ||
      typeof source.shapeTypeCode !== 'number' || !Number.isInteger(source.shapeTypeCode) ||
      typeof source.featureCount !== 'number' || !Number.isInteger(source.featureCount) ||
      source.featureCount < 0 || !Array.isArray(source.features) ||
      Number(source.featureCount) !== source.features.length || !source.features.every(validFeature)) return false;
  const ordinals = new Set(source.features.map(feature => feature.sourceOrdinal));
  const expectedGeometryType = source.shapeType === 'Polyline' ? 'polyline' : 'polygon';
  if (ordinals.size !== source.features.length || source.features.some(feature => feature.geometryType !== expectedGeometryType)) return false;
  const family = record(source.family);
  const files = Array.isArray(family?.files) ? family.files : [];
  const sourceFile = files.map(record).find((file) => file?.role === 'source');
  if (!family || typeof family.sourcePath !== 'string' || typeof family.revision !== 'string' ||
      typeof sourceFile?.revision !== 'string' || !isShapefileFamilyCapture(family, family.sourcePath, sourceFile.revision) ||
      !sameSourcePath(source.path, family.sourcePath)) return false;
  return true;
}

export function isGeometryVectorImportInspection(value: unknown,
  expected: GeometryVectorImportCapture, paths: readonly string[]): value is GeometryVectorImportInspection {
  const inspection = record(value);
  if (!inspection || inspection.revision !== expected.revision ||
      inspection.draftIdentity !== expected.draftIdentity || !Array.isArray(inspection.sources) ||
      !Array.isArray(inspection.issues) || !inspection.sources.every(validSource)) return false;
  if (!inspection.issues.every((raw) => {
    const issue = record(raw);
    return issue !== undefined && typeof issue.path === 'string' && typeof issue.message === 'string';
  })) return false;
  const outputs = [
    ...inspection.sources.map(source => source.path),
    ...inspection.issues.map(issue => String(record(issue)?.path)),
  ];
  const belongsToRequest = (path: string): boolean => paths.some((candidate) => sameSourcePath(candidate, path));
  if (outputs.length !== paths.length || outputs.some(path => !belongsToRequest(path))) return false;
  return paths.every(path => outputs.filter(resultPath => sameSourcePath(path, resultPath)).length === 1);
}

/** The returned topology snapshot must retain the captured saved source and identify a new draft. */
export function isGeometryVectorImportApplySnapshot(value: unknown,
  expected: GeometryVectorImportCapture): value is GeometryTopologySnapshot {
  const snapshot = record(value);
  const completion = record(snapshot?.completion);
  return snapshot !== undefined && snapshot.revision === expected.revision &&
    typeof snapshot.draftState === 'string' && snapshot.draftState.length > 0 && snapshot.draftState !== expected.draftState &&
    snapshot.isDirty === true && snapshot.canUndo === true &&
    completion !== undefined && completion.sourceRevision === expected.revision &&
    typeof completion.draftIdentity === 'string' && /^[\da-f]{64}$/i.test(completion.draftIdentity) &&
    completion.draftIdentity !== expected.draftIdentity;
}

export function sameGeometryVectorImportCapture(left: GeometryVectorImportCapture,
  right: GeometryVectorImportCapture): boolean {
  return left.revision === right.revision && left.draftState === right.draftState &&
    left.draftIdentity === right.draftIdentity;
}

export function isGeometryVectorImportSelectedSource(value: unknown): value is GeometryVectorImportSelectedSource {
  const selected = record(value);
  const family = record(selected?.family);
  const files = Array.isArray(family?.files) ? family.files : [];
  const sourceFile = files.map(record).find((file) => file?.role === 'source');
  return selected !== undefined && typeof selected.path === 'string' && selected.path.length > 0 &&
    family !== undefined && typeof family.sourcePath === 'string' && typeof sourceFile?.revision === 'string' &&
    isShapefileFamilyCapture(family, family.sourcePath, sourceFile.revision) &&
    sameSourcePath(selected.path, family.sourcePath);
}

export function geometryVectorImportCapture(snapshot: GeometryTopologySnapshot | undefined): GeometryVectorImportCapture | undefined {
  const completion = snapshot?.completion;
  if (!snapshot || !completion || completion.sourceRevision !== snapshot.revision ||
      !/^[\da-f]{64}$/i.test(completion.draftIdentity) || !snapshot.draftState) return undefined;
  return { revision: snapshot.revision, draftState: snapshot.draftState, draftIdentity: completion.draftIdentity };
}
