import assert from 'node:assert/strict';
import {
  resultHoverEntryMatchesCurrentFrame,
  resultHoverFramesMatchContext,
} from '../frontend/src/features/map/mapResultHoverIdentity.ts';

const context = {
  sourceID: 'result-node-1',
  mapID: 'A1B2C3D4-E5F6-4711-8222-010203040506',
  mapType: 'Depth',
  nodeType: 'MultiResult',
  timelineProfileIndex: 4,
};
const selections = [
  { selectionIndex: 0, resultPath: '/results/anchor.hdf', resultRevision: 'A'.repeat(64) },
  { selectionIndex: 1, resultPath: '/results/child.hdf', resultRevision: 'B'.repeat(64) },
];
const frames = [
  {
    sourceID: context.sourceID,
    sourceMapID: `{${context.mapID.toLowerCase()}}`,
    resultPath: selections[0].resultPath,
    revision: selections[0].resultRevision.toLowerCase(),
    mapType: 'Depth',
    profileIndex: 4,
    selectionIndices: [0],
  },
  {
    sourceID: context.sourceID,
    sourceMapID: context.mapID,
    resultPath: selections[1].resultPath,
    revision: selections[1].resultRevision,
    mapType: 'Depth',
    profileIndex: 9,
    selectionIndices: [1],
  },
];

assert.equal(resultHoverFramesMatchContext(frames, selections, context), true,
  'MultiResult child images may use their own native profile indices while slot 0 anchors the timeline.');
assert.equal(resultHoverEntryMatchesCurrentFrame(frames, selections, context, {
  resultPath: selections[1].resultPath,
  resultRevision: selections[1].resultRevision,
  mapType: 'Depth',
  profileIndex: 9,
}), true, 'A child Result hover value must match its rendered native profile.');
assert.equal(resultHoverEntryMatchesCurrentFrame(frames, selections, context, {
  resultPath: selections[1].resultPath,
  resultRevision: selections[1].resultRevision,
  mapType: 'Depth',
  profileIndex: 4,
}), false, 'A child Result value at the anchor index must be rejected when its rendered native index differs.');
assert.equal(resultHoverEntryMatchesCurrentFrame(frames, selections, context, {
  resultPath: selections[1].resultPath,
  resultRevision: 'C'.repeat(64),
  mapType: 'Depth',
  profileIndex: 9,
}), false, 'A stale child source revision must be rejected.');
assert.equal(resultHoverEntryMatchesCurrentFrame(frames, selections, context, {
  resultPath: '/results/not-rendered.hdf',
  resultRevision: 'C'.repeat(64),
  mapType: 'Depth',
  profileIndex: 9,
}), false, 'A value without a matching rendered source frame must be rejected.');

assert.equal(resultHoverFramesMatchContext([
  { ...frames[0], profileIndex: 5 }, frames[1],
], selections, context), false, 'Slot 0 must still use the selected timeline profile index.');
assert.equal(resultHoverFramesMatchContext([
  { ...frames[1], selectionIndices: [0] },
], selections, context), false, 'A rendered frame cannot claim a selection slot backed by another source.');
assert.equal(resultHoverFramesMatchContext([
  { ...frames[1], sourceID: 'another-result-node' },
], selections, context), false, 'Frames from another active Result node must be rejected.');
assert.equal(resultHoverFramesMatchContext([
  frames[0], { ...frames[1], profileIndex: -1 },
], selections, context), false, 'An unavailable child profile is not a rendered native frame.');

const singleResultContext = { ...context, nodeType: 'Result' };
assert.equal(resultHoverFramesMatchContext([frames[0]], [selections[0]], singleResultContext), true);
assert.equal(resultHoverFramesMatchContext([
  { ...frames[0], profileIndex: 9 },
], [selections[0]], singleResultContext), false,
  'A standalone Result must use its selected profile index directly.');

process.stdout.write('Map Result hover identity: MultiResult native profiles and stale-source guards passed.\n');
