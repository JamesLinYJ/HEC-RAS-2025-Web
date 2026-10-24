import assert from 'node:assert/strict';
import {
  advanceGdbRasterVisibilityFrameCache,
  canReuseGdbRasterVisibilityFrame,
  rebindGdbRasterSourceIdentity,
} from '../frontend/src/features/map/rasterVisibilityReuse.js';

const valid = {
  proof: { previousVisualizationRevision: 'VIS-A', visualizationRevision: 'VIS-B' },
  sameScope: true,
  currentVisualizationRevision: 'vis-b',
  frameVisualizationRevision: 'vis-a',
  candidateSourceRevision: 'GDB-1',
  manifestRevision: 'gdb-1',
  frameSourceRevision: 'GDB-1',
  frameMapSourceRevision: 'gdb-1',
  candidateMapType: 'SingleBandRasterMapLayer',
  frameMapType: 'SingleBandRasterMapLayer',
  candidateSourcePath: '/fixture/data.gdb',
  manifestSourcePath: '/fixture/data.gdb',
  frameSourcePath: '/fixture/data.gdb',
  candidateLocatorKey: '{"nodePath":[3,7],"filename":"GDB:data.gdb/float"}',
  frameLocatorKey: '{"nodePath":[3,7],"filename":"GDB:data.gdb/float"}',
};

assert.equal(canReuseGdbRasterVisibilityFrame(valid), true, 'a receipt-proven visibility-only transition can reuse its frame');
assert.equal(canReuseGdbRasterVisibilityFrame({ ...valid, proof: undefined }), false, 'unknown .vis changes fail closed');
assert.equal(canReuseGdbRasterVisibilityFrame({ ...valid, proof: { previousVisualizationRevision: 'VIS-A', visualizationRevision: 'VIS-C' } }), false,
  'a frame cannot skip an intermediate visualization revision');
assert.equal(canReuseGdbRasterVisibilityFrame({ ...valid, candidateSourceRevision: 'GDB-2' }), false,
  'a changed GDB manifest revision invalidates reuse');
assert.equal(canReuseGdbRasterVisibilityFrame({ ...valid, candidateSourcePath: '/other/data.gdb' }), false,
  'a changed GDB directory invalidates reuse');
assert.equal(canReuseGdbRasterVisibilityFrame({ ...valid, frameMapType: 'ImageRasterMapLayer' }), false,
  'a changed map type invalidates reuse');
assert.equal(canReuseGdbRasterVisibilityFrame({ ...valid, candidateLocatorKey: '{"nodePath":[3,8]}' }), false,
  'a changed exact child locator invalidates reuse');
assert.equal(canReuseGdbRasterVisibilityFrame({ ...valid, sameScope: false }), false,
  'a changed project or saved-map scope invalidates reuse');

const locator = { nodePath: [3, 7], nodeName: 'Raster float', mapType: 'SingleBandRasterMapLayer', filename: 'GDB:data.gdb/float' };
const priorSource = {
  visualizationRevision: 'VIS-A',
  sourceMapID: 'map-1',
  gdbSource: { map: { expectedVisualizationRevision: 'VIS-A' }, node: { nodePath: [3, 7] }, source: { revision: 'GDB-1' } },
};
const reboundSource = rebindGdbRasterSourceIdentity(priorSource, locator, valid.proof, 'map-1');
assert.equal(reboundSource.visualizationRevision, 'VIS-B', 'outer raster identity advances to the receipt revision');
assert.equal(reboundSource.gdbSource.map.expectedVisualizationRevision, 'VIS-B', 'nested map request advances to the receipt revision');
assert.deepEqual(reboundSource.gdbSource.node, locator, 'nested GDB child identity is refreshed from the exact current locator');

const rasterKeys = ['float32_lz77', 'rgb_lz77', 'rgba_lz77', ...Array.from({ length: 41 }, (_, index) => `raster-${index + 4}`)];
const allRasterCandidates = new Map(rasterKeys.map((key) => [key, { sourceRevision: 'GDB-1' }]));
let frameCache = new Map([['float32_lz77', { visualizationRevision: 'VIS-0', sourceRevision: 'GDB-1' }]]);
let visibilityRevision = 'VIS-0';
let checkedRasters = new Set(['float32_lz77']);

function applyVisibilityReceipt(nextRevision) {
  const proof = { previousVisualizationRevision: visibilityRevision, visualizationRevision: nextRevision };
  frameCache = advanceGdbRasterVisibilityFrameCache({
    cachedFrames: frameCache,
    currentCandidates: allRasterCandidates,
    proof,
    currentVisualizationRevision: nextRevision,
    scopeMatches: true,
    rebind: (frame, candidate) => frame.visualizationRevision === proof.previousVisualizationRevision &&
      frame.sourceRevision === candidate.sourceRevision
      ? { ...frame, visualizationRevision: nextRevision }
      : null,
  });
  visibilityRevision = nextRevision;
}

function renderedCachedCount() {
  return [...checkedRasters].filter((key) => frameCache.has(key)).length;
}

checkedRasters.delete('float32_lz77');
applyVisibilityReceipt('VIS-1');
assert.equal(renderedCachedCount(), 0, 'a zero-checked-candidate step retains hidden frames without displaying them');
checkedRasters.add('rgb_lz77');
applyVisibilityReceipt('VIS-2');
assert.equal(renderedCachedCount(), 0, 'turning on a different uncached image does not reveal a hidden raster');
frameCache.set('rgb_lz77', { visualizationRevision: visibilityRevision, sourceRevision: 'GDB-1' });
checkedRasters.delete('rgb_lz77');
applyVisibilityReceipt('VIS-3');
assert.equal(renderedCachedCount(), 0, 'hiding RGB advances cache identities while retaining hidden source frames');
checkedRasters.add('rgba_lz77');
applyVisibilityReceipt('VIS-4');
assert.equal(renderedCachedCount(), 0, 'turning on RGBA does not show unrelated cached images');
frameCache.set('rgba_lz77', { visualizationRevision: visibilityRevision, sourceRevision: 'GDB-1' });

for (let index = 0; index < rasterKeys.length; index += 1) {
  const key = rasterKeys[index];
  if (checkedRasters.has(key)) continue;
  if (!frameCache.has(key)) frameCache.set(key, { visualizationRevision: visibilityRevision, sourceRevision: 'GDB-1' });
  checkedRasters.add(key);
  applyVisibilityReceipt(`VIS-${index + 5}`);
  assert.equal(renderedCachedCount(), checkedRasters.size, `restoring ${key} preserves the exact checked roster`);
}
assert.equal(frameCache.size, 44, 'all 44 loaded raster frames survive the visibility-only proof chain');

process.stdout.write('Receipt-backed GDB raster visibility cache checks passed.\n');
