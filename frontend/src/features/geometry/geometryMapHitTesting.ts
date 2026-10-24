import type {
  GeometryTopologyArcPointsSnapshot,
  GeometryTopologyCoordinate,
  GeometryTopologyMap,
  GeometryTopologyMapFeature,
} from './geometryTypes';

export interface ArcPathCapture {
  feature: GeometryTopologyMapFeature;
  path: GeometryTopologyCoordinate[];
}

export interface ProjectedSegmentHit {
  feature: GeometryTopologyMapFeature;
  arcFID: number;
  sourceSegmentIndex: number;
  point: GeometryTopologyCoordinate;
  distance: number;
}

export interface ArcFeatureHit {
  feature: GeometryTopologyMapFeature;
  arcFID: number;
  distance: number;
}

export interface InternalArcPointHit {
  arcFID: number;
  pointIndex: number;
  point: GeometryTopologyCoordinate;
  distance: number;
}

export type NodeHitExtentClassification =
  | { kind: 'none' }
  | { kind: 'unique'; nodeFID: number; point: GeometryTopologyCoordinate }
  | { kind: 'ambiguous'; candidateCount: number; nodeFIDs: number[] };

const TIE_EPSILON = 1e-9;

export function isFiniteCoordinate(value: unknown): value is GeometryTopologyCoordinate {
  if (!value || typeof value !== 'object') return false;
  const point = value as Partial<GeometryTopologyCoordinate>;
  return Number.isFinite(point.x) && Number.isFinite(point.y);
}

/** Resolve only a unique, source-backed line feature with the requested stable FID. */
export function resolveArcPath(map: GeometryTopologyMap | null, arcFID: number): ArcPathCapture | null {
  if (!map || !Number.isSafeInteger(arcFID) || arcFID < 0) return null;
  const matches = map.features.filter((feature) => feature.featureKind === 'arc' && feature.fid === arcFID);
  if (matches.length !== 1) return null;
  const feature = matches[0];
  const path = feature.paths.length === 1 ? feature.paths[0] : null;
  if (!path || path.length < 2 || !path.every(isFiniteCoordinate)) return null;
  return { feature, path };
}

export function resolveUniqueSelectedArcPath(
  map: GeometryTopologyMap | null,
  selectedArcFIDs: readonly number[],
  arcFID: number,
): ArcPathCapture | null {
  const unique = [...new Set(selectedArcFIDs.filter((fid) => Number.isSafeInteger(fid) && fid >= 0))];
  if (unique.length !== 1 || unique[0] !== arcFID) return null;
  return resolveArcPath(map, arcFID);
}

export function isArcPointsSnapshotForPath(
  snapshot: GeometryTopologyArcPointsSnapshot | null,
  arc: ArcPathCapture | null,
  draftIdentity: string | null,
): snapshot is GeometryTopologyArcPointsSnapshot {
  if (!snapshot || !arc || !draftIdentity || snapshot.arcFID !== arc.feature.fid || snapshot.draftIdentity !== draftIdentity) return false;
  if (!snapshot.revision || snapshot.rows.length !== arc.path.length) return false;
  const rowIndexes = new Set<number>();
  const pointIndexes = new Set<number>();
  let expectedInternalPointIndex = 0;
  for (const row of snapshot.rows) {
    if (!Number.isSafeInteger(row.rowIndex) || row.rowIndex < 0 || row.rowIndex >= arc.path.length || rowIndexes.has(row.rowIndex)) return false;
    const sourcePoint = arc.path[row.rowIndex];
    if (!sourcePoint || !Number.isFinite(row.x) || !Number.isFinite(row.y) || row.x !== sourcePoint.x || row.y !== sourcePoint.y) return false;
    rowIndexes.add(row.rowIndex);
    if (row.pointKind === 'internal') {
      const index = row.internalPointIndex;
      if (row.rowIndex === 0 || row.rowIndex === arc.path.length - 1
        || !Number.isSafeInteger(index) || index !== expectedInternalPointIndex || pointIndexes.has(index!)) return false;
      pointIndexes.add(index!);
      expectedInternalPointIndex += 1;
    } else if (row.pointKind === 'start') {
      if (row.rowIndex !== 0) return false;
    } else if (row.pointKind === 'end') {
      if (row.rowIndex !== arc.path.length - 1) return false;
    } else {
      return false;
    }
  }
  if (rowIndexes.size !== arc.path.length) return false;
  const internalRows = snapshot.rows.filter((row) => row.pointKind === 'internal');
  if (pointIndexes.size !== internalRows.length) return false;
  return internalRows.every((row) => Number.isSafeInteger(row.internalPointIndex));
}

export function internalArcPointHits(
  snapshot: GeometryTopologyArcPointsSnapshot,
  point: GeometryTopologyCoordinate,
  tolerance: number,
): InternalArcPointHit[] {
  if (!(tolerance >= 0) || !Number.isFinite(tolerance) || !isFiniteCoordinate(point)) return [];
  const matches: InternalArcPointHit[] = [];
  for (const row of snapshot.rows) {
    if (row.pointKind !== 'internal' || !Number.isSafeInteger(row.internalPointIndex) || (row.internalPointIndex ?? -1) < 0) continue;
    const distance = Math.hypot(point.x - row.x, point.y - row.y);
    if (Number.isFinite(distance) && distance <= tolerance) {
      matches.push({ arcFID: snapshot.arcFID, pointIndex: row.internalPointIndex!, point: { x: row.x, y: row.y }, distance });
    }
  }
  return matches.sort((left, right) => left.distance - right.distance || left.pointIndex - right.pointIndex);
}

export function uniqueInternalArcPointHit(
  snapshot: GeometryTopologyArcPointsSnapshot | null,
  point: GeometryTopologyCoordinate,
  tolerance: number,
): InternalArcPointHit | null {
  if (!snapshot) return null;
  const hits = internalArcPointHits(snapshot, point, tolerance);
  if (!hits.length || (hits.length > 1 && Math.abs(hits[1].distance - hits[0].distance) <= TIE_EPSILON)) return null;
  return hits[0];
}

/** Match BaseTopology.HitTestNode's axis-aligned square extent, not a radial nearest-node search. */
export function classifyNodeHitExtent(
  map: GeometryTopologyMap | null,
  point: GeometryTopologyCoordinate,
  halfExtent: number,
): NodeHitExtentClassification {
  if (!map || !isFiniteCoordinate(point) || !(halfExtent >= 0) || !Number.isFinite(halfExtent)) return { kind: 'none' };
  const fidCounts = countFIDs(map, 'node');
  const candidates: Array<{ nodeFID: number; point: GeometryTopologyCoordinate; hasUniqueFID: boolean }> = [];
  for (const feature of map.features) {
    if (feature.featureKind !== 'node' || !Number.isSafeInteger(feature.fid) || (feature.fid ?? -1) < 0) continue;
    const path = feature.paths.length === 1 ? feature.paths[0] : null;
    const sourcePoint = path?.length === 1 ? path[0] : null;
    if (!sourcePoint || !isFiniteCoordinate(sourcePoint)) continue;
    if (Math.abs(point.x - sourcePoint.x) <= halfExtent && Math.abs(point.y - sourcePoint.y) <= halfExtent) {
      candidates.push({ nodeFID: feature.fid!, point: sourcePoint, hasUniqueFID: fidCounts.get(feature.fid!) === 1 });
    }
  }
  if (candidates.length === 0) return { kind: 'none' };
  if (candidates.length === 1 && candidates[0].hasUniqueFID) {
    return { kind: 'unique', nodeFID: candidates[0].nodeFID, point: candidates[0].point };
  }
  return {
    kind: 'ambiguous',
    candidateCount: candidates.length,
    nodeFIDs: [...new Set(candidates.map((candidate) => candidate.nodeFID))].sort((left, right) => left - right),
  };
}

export function nearestArcSegmentHit(
  map: GeometryTopologyMap | null,
  point: GeometryTopologyCoordinate,
  tolerance: number,
  onlyArcFID?: number,
): ProjectedSegmentHit | null {
  if (!map || !isFiniteCoordinate(point) || !(tolerance >= 0) || !Number.isFinite(tolerance)) return null;
  const fidCounts = countFIDs(map, 'arc');
  const hits: ProjectedSegmentHit[] = [];
  for (const feature of map.features) {
    if (feature.featureKind !== 'arc' || (onlyArcFID !== undefined && feature.fid !== onlyArcFID)) continue;
    if (!Number.isSafeInteger(feature.fid) || (feature.fid ?? -1) < 0 || fidCounts.get(feature.fid!) !== 1 || feature.paths.length !== 1) continue;
    const path = feature.paths[0];
    if (path.length < 2 || !path.every(isFiniteCoordinate)) continue;
    for (let sourceSegmentIndex = 0; sourceSegmentIndex < path.length - 1; sourceSegmentIndex += 1) {
      const projection = projectToSegment(point, path[sourceSegmentIndex], path[sourceSegmentIndex + 1]);
      if (projection.distance <= tolerance) {
        hits.push({ feature, arcFID: feature.fid!, sourceSegmentIndex, point: projection.point, distance: projection.distance });
      }
    }
  }
  hits.sort((left, right) => left.distance - right.distance
    || left.arcFID - right.arcFID || left.sourceSegmentIndex - right.sourceSegmentIndex);
  if (!hits.length) return null;
  const tied = hits.filter((hit) => Math.abs(hit.distance - hits[0].distance) <= TIE_EPSILON);
  if (tied.length > 1) {
    // At a shared source vertex, either neighboring segment is an exact valid locator. Choose
    // the lower source index only when both projections resolve to that same stored vertex.
    if (tied.length === 2 && tied[0].feature === tied[1].feature && tied[0].arcFID === tied[1].arcFID) {
      const lower = tied[0].sourceSegmentIndex < tied[1].sourceSegmentIndex ? tied[0] : tied[1];
      const upper = lower === tied[0] ? tied[1] : tied[0];
      const sharedVertex = lower.feature.paths[0]?.[lower.sourceSegmentIndex + 1];
      if (upper.sourceSegmentIndex === lower.sourceSegmentIndex + 1 && sharedVertex
        && isFiniteCoordinate(sharedVertex)
        && Math.hypot(lower.point.x - sharedVertex.x, lower.point.y - sharedVertex.y) <= TIE_EPSILON
        && Math.hypot(upper.point.x - sharedVertex.x, upper.point.y - sharedVertex.y) <= TIE_EPSILON) {
        return lower;
      }
    }
    // Cross-arc overlaps, self-intersections, and non-adjacent segment ties stay unresolved.
    return null;
  }
  return hits[0];
}

/** Resolve a unique nearest source arc; adjacent segment ties on that same arc are harmless here. */
export function nearestArcFeatureHit(
  map: GeometryTopologyMap | null,
  point: GeometryTopologyCoordinate,
  tolerance: number,
): ArcFeatureHit | null {
  if (!map || !isFiniteCoordinate(point) || !(tolerance >= 0) || !Number.isFinite(tolerance)) return null;
  const fidCounts = countFIDs(map, 'arc');
  const byFID = new Map<number, ArcFeatureHit>();
  for (const feature of map.features) {
    if (feature.featureKind !== 'arc' || !Number.isSafeInteger(feature.fid) || (feature.fid ?? -1) < 0 || fidCounts.get(feature.fid!) !== 1 || feature.paths.length !== 1) continue;
    const path = feature.paths[0];
    if (path.length < 2 || !path.every(isFiniteCoordinate)) continue;
    let distance = Number.POSITIVE_INFINITY;
    for (let index = 0; index < path.length - 1; index += 1) {
      distance = Math.min(distance, projectToSegment(point, path[index], path[index + 1]).distance);
    }
    if (distance <= tolerance) {
      const existing = byFID.get(feature.fid!);
      if (!existing) byFID.set(feature.fid!, { feature, arcFID: feature.fid!, distance });
      else if (existing.feature !== feature) {
        // Duplicate FID rows are not a safe source identity.
        byFID.set(feature.fid!, { feature: existing.feature, arcFID: feature.fid!, distance: Number.NaN });
      } else if (distance < existing.distance) {
        byFID.set(feature.fid!, { feature, arcFID: feature.fid!, distance });
      }
    }
  }
  const hits = [...byFID.values()].filter((hit) => Number.isFinite(hit.distance)).sort((left, right) => left.distance - right.distance || left.arcFID - right.arcFID);
  if (!hits.length || (hits.length > 1 && Math.abs(hits[1].distance - hits[0].distance) <= TIE_EPSILON)) return null;
  return hits[0];
}

export function nearestArcVertexDistance(path: readonly GeometryTopologyCoordinate[], point: GeometryTopologyCoordinate): number {
  if (!isFiniteCoordinate(point)) return Number.POSITIVE_INFINITY;
  let best = Number.POSITIVE_INFINITY;
  for (const vertex of path) {
    if (!isFiniteCoordinate(vertex)) continue;
    best = Math.min(best, Math.hypot(point.x - vertex.x, point.y - vertex.y));
  }
  return best;
}

export function projectToSegment(
  point: GeometryTopologyCoordinate,
  start: GeometryTopologyCoordinate,
  end: GeometryTopologyCoordinate,
): { point: GeometryTopologyCoordinate; distance: number; fraction: number } {
  const dx = end.x - start.x;
  const dy = end.y - start.y;
  const lengthSquared = dx * dx + dy * dy;
  const fraction = lengthSquared > 0
    ? Math.max(0, Math.min(1, ((point.x - start.x) * dx + (point.y - start.y) * dy) / lengthSquared))
    : 0;
  const projected = { x: start.x + fraction * dx, y: start.y + fraction * dy };
  return { point: projected, distance: Math.hypot(point.x - projected.x, point.y - projected.y), fraction };
}

function countFIDs(map: GeometryTopologyMap, kind: 'node' | 'arc'): Map<number, number> {
  const counts = new Map<number, number>();
  for (const feature of map.features) {
    if (feature.featureKind !== kind || !Number.isSafeInteger(feature.fid) || (feature.fid ?? -1) < 0) continue;
    counts.set(feature.fid!, (counts.get(feature.fid!) ?? 0) + 1);
  }
  return counts;
}
