const rasterMapTypes = new Set([
  'SingleBandRasterMapLayer',
  'ImageRasterMapLayer',
  'DiscreteRasterMapLayer',
]);

function revisionMatches(left, right) {
  return typeof left === 'string' && left.trim() !== '' &&
    typeof right === 'string' && right.trim() !== '' && left.toUpperCase() === right.toUpperCase();
}

/**
 * A frame may cross a .vis revision only when the caller has a receipt-backed
 * visibility-only proof and every source identity still matches exactly.
 */
export function canReuseGdbRasterVisibilityFrame(input) {
  const proof = input?.proof;
  if (!proof || input.sameScope !== true ||
      !revisionMatches(input.currentVisualizationRevision, proof.visualizationRevision) ||
      !revisionMatches(input.frameVisualizationRevision, proof.previousVisualizationRevision) ||
      revisionMatches(proof.previousVisualizationRevision, proof.visualizationRevision) ||
      !revisionMatches(input.candidateSourceRevision, input.manifestRevision) ||
      !revisionMatches(input.frameSourceRevision, input.manifestRevision) ||
      !revisionMatches(input.frameMapSourceRevision, input.manifestRevision) ||
      !rasterMapTypes.has(input.candidateMapType) || input.candidateMapType !== input.frameMapType ||
      typeof input.candidateSourcePath !== 'string' || input.candidateSourcePath.trim() === '' ||
      input.candidateSourcePath !== input.manifestSourcePath || input.frameSourcePath !== input.manifestSourcePath ||
      typeof input.candidateLocatorKey !== 'string' || input.candidateLocatorKey.trim() === '' ||
      input.candidateLocatorKey !== input.frameLocatorKey) {
    return false;
  }
  return true;
}

/** Rebind the saved-map identity after the caller has proved safe frame reuse. */
export function rebindGdbRasterSourceIdentity(rasterSource, locator, proof, sourceMapID) {
  const gdbSource = rasterSource.gdbSource;
  const map = gdbSource.map;
  return {
    ...rasterSource,
    visualizationRevision: proof.visualizationRevision,
    nodePath: Array.isArray(locator.nodePath) ? [...locator.nodePath] : locator.nodePath,
    nodeName: locator.nodeName,
    mapLayerType: locator.mapType,
    filename: locator.filename,
    filenameRelative: locator.filenameRelative ?? null,
    sourceMapID,
    gdbSource: {
      ...gdbSource,
      map: { ...map, expectedVisualizationRevision: proof.visualizationRevision },
      node: locator,
    },
  };
}

/** Advance cached entries across one proven revision while pruning stale sources. */
export function advanceGdbRasterVisibilityFrameCache(input) {
  const proof = input?.proof;
  if (!proof || input.scopeMatches !== true ||
      !revisionMatches(input.currentVisualizationRevision, proof.visualizationRevision) ||
      revisionMatches(proof.previousVisualizationRevision, proof.visualizationRevision)) {
    return new Map();
  }

  const advanced = new Map();
  for (const [identity, frame] of input.cachedFrames) {
    const candidate = input.currentCandidates.get(identity);
    if (candidate === undefined) continue;
    const rebound = input.rebind(frame, candidate);
    if (rebound !== null) advanced.set(identity, rebound);
  }
  return advanced;
}
