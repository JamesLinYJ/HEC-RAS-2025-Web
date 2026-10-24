import assert from 'node:assert/strict';
import { mapHoverEntryText, resultMapTypeText } from '../frontend/src/features/map/resultMapTypeText.ts';

const mapTypes = [
  ['Depth', '水深'],
  ['WaterSurfaceElevation', '水面高程'],
  ['Water Surface', '水面高程'],
  ['Velocity', '流速'],
  ['DepthTimesVelocity', '水深×流速'],
  ['D*V', '水深×流速'],
  ['DepthTimesVelocitySquared', '水深×流速²'],
  ['D*V^2', '水深×流速²'],
];
for (const [source, expected] of mapTypes) {
  assert.equal(resultMapTypeText('zh', source), expected, `Chinese label for ${source}`);
  assert.equal(resultMapTypeText('en', source), source, `English source label for ${source}`);
}
assert.equal(resultMapTypeText('zh', 'UnknownMapType'), 'UnknownMapType', 'unknown types remain visible as serialized');
assert.equal(resultMapTypeText('zh', '  D * V  '), '水深×流速', 'known serializer spellings tolerate surrounding spacing');

const exact = 'Plan CPU — Depth: 1.0009842 m';
assert.equal(mapHoverEntryText('zh', {
  kind: 'result', mapType: 'Depth', value: '1.0009842', label: exact,
}), 'Plan CPU — 水深: 1.0009842 m', 'only the final known Result field is localized');
assert.equal(mapHoverEntryText('zh', {
  kind: 'result', mapType: 'WaterSurfaceElevation', value: '-12.500',
  label: 'Plan CPU — Water Surface: -12.500 ft',
}), 'Plan CPU — 水面高程: -12.500 ft');
assert.equal(mapHoverEntryText('zh', {
  kind: 'result', mapType: 'DepthTimesVelocitySquared', value: '0.003',
  label: 'Plan CPU — DepthTimesVelocitySquared: 0.003 m^3/s^2',
}), 'Plan CPU — 水深×流速²: 0.003 m^3/s^2');
assert.equal(mapHoverEntryText('zh', {
  kind: 'result', mapType: 'Depth', value: '1.0009842',
  label: 'Depth is part of this source name: 1.0009842 m',
}), 'Depth is part of this source name: 1.0009842 m', 'a source name is not rewritten');
assert.equal(mapHoverEntryText('zh', {
  kind: 'result', mapType: 'UnknownMapType', value: '1', label: 'Plan CPU — UnknownMapType: 1 m',
}), 'Plan CPU — UnknownMapType: 1 m', 'unknown Result field labels remain unchanged');
assert.equal(mapHoverEntryText('zh', {
  kind: 'result', mapType: 'Depth', value: '1', label: 'Plan CPU — Velocity: 1 m',
}), 'Plan CPU — Velocity: 1 m', 'mismatched field labels remain unchanged');
assert.equal(mapHoverEntryText('zh', {
  kind: 'raster', mapType: 'Depth', value: '1', label: 'Plan CPU — Depth: 1 m',
}), 'Plan CPU — Depth: 1 m', 'non-Result kinds are not relabeled');
assert.equal(mapHoverEntryText('zh', {
  kind: 'terrain', mapType: null, value: '22.75', label: 'Ground — Terrain: 22.75 m',
}), 'Ground — 地形高程: 22.75 m', 'a structured Terrain field is localized independently of Result types');
assert.equal(mapHoverEntryText('zh', {
  kind: 'terrain', mapType: null, value: '22.75', label: 'Terrain Study: 22.75 m',
}), 'Terrain Study: 22.75 m', 'terrain/source names are not rewritten');
assert.equal(mapHoverEntryText('en', {
  kind: 'result', mapType: 'Depth', value: '1.0009842', label: exact,
}), exact, 'English hover labels remain byte-for-byte unchanged');

process.stdout.write('Map Result labels: Chinese/English labels and identity-preserving hover formatting passed.\n');
