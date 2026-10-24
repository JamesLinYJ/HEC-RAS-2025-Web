import type { MapVisibilityTransition } from './mapTypes';

export interface GdbRasterVisibilityFrameReuseInput {
  proof: MapVisibilityTransition | null | undefined;
  sameScope: boolean;
  currentVisualizationRevision: string | null | undefined;
  frameVisualizationRevision: string | null | undefined;
  candidateSourceRevision: string | null | undefined;
  manifestRevision: string | null | undefined;
  frameSourceRevision: string | null | undefined;
  frameMapSourceRevision: string | null | undefined;
  candidateMapType: string | null | undefined;
  frameMapType: string | null | undefined;
  candidateSourcePath: string | null | undefined;
  manifestSourcePath: string | null | undefined;
  frameSourcePath: string | null | undefined;
  candidateLocatorKey: string | null | undefined;
  frameLocatorKey: string | null | undefined;
}

export function canReuseGdbRasterVisibilityFrame(input: GdbRasterVisibilityFrameReuseInput): boolean;

export function rebindGdbRasterSourceIdentity(
  rasterSource: Record<string, unknown>,
  locator: import('./mapTypes').CatalogTreeNodeLocator,
  proof: MapVisibilityTransition,
  sourceMapID: string,
): Record<string, unknown>;

export interface AdvanceGdbRasterVisibilityFrameCacheInput<TFrame, TCandidate> {
  cachedFrames: ReadonlyMap<string, TFrame>;
  currentCandidates: ReadonlyMap<string, TCandidate>;
  proof: MapVisibilityTransition | null | undefined;
  currentVisualizationRevision: string | null | undefined;
  scopeMatches: boolean;
  rebind: (frame: TFrame, candidate: TCandidate) => TFrame | null;
}

export function advanceGdbRasterVisibilityFrameCache<TFrame, TCandidate>(
  input: AdvanceGdbRasterVisibilityFrameCacheInput<TFrame, TCandidate>,
): Map<string, TFrame>;
