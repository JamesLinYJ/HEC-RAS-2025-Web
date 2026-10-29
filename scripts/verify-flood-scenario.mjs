import assert from 'node:assert/strict';
import { accumulatedRain, DEFAULT_SCENARIO, minuteLabel, parseScenario, rainAt, scenarioStage, sceneDuration } from '../frontend/src/features/dam-break/scenario.ts';

const base = DEFAULT_SCENARIO;
assert.equal(rainAt(base, -1), 0);
assert.equal(rainAt(base, base.stormDuration), 0);
assert.equal(rainAt({ ...base, rainfall: 0 }, 30), 0);
assert.equal(accumulatedRain({ ...base, rainfall: 0 }, 90), 0);
assert.equal(accumulatedRain(base, -5), 0);
for (const pattern of ['front', 'middle', 'back']) {
  const s = { ...base, rainPattern: pattern };
  const peak = pattern === 'front' ? .25 : pattern === 'back' ? .75 : .5;
  assert.equal(rainAt(s, peak * s.stormDuration), s.rainfall);
  const expected = s.rainfall * s.stormDuration / 60 * (.22 + .78 * 2 / Math.PI);
  assert.ok(Math.abs(accumulatedRain(s, s.stormDuration) - expected) < .01);
  assert.equal(accumulatedRain(s, 240), accumulatedRain(s, s.stormDuration));
  let previous = 0;
  for (let minute = 0; minute <= sceneDuration(s); minute++) {
    const current = accumulatedRain(s, minute);
    assert.ok(current >= previous);
    previous = current;
  }
}
assert.equal(scenarioStage(base, base.breachStart - 1).index, 0);
assert.equal(scenarioStage(base, base.breachStart).index, 1);
assert.equal(scenarioStage(base, base.breachStart + base.breachDuration).index, 2);
assert.equal(scenarioStage(base, sceneDuration(base)).index, 3);
assert.equal(sceneDuration({ ...base, breachStart: 120, breachDuration: 60 }), 240);
assert.equal(minuteLabel(61.5), '01:01:30');
assert.deepEqual(parseScenario(base), base);
for (const bad of [null, [], {}, { ...base, rainfall: Infinity }, { ...base, reservoirLevel: NaN }, { ...base, breachDuration: 0 }, { ...base, rainfall: '65' }, { ...base, rainPattern: 'unexpected' }]) assert.equal(parseScenario(bad), null);
assert.equal('unexpected' in parseScenario({ ...base, unexpected: true }), false);
console.log('Flood scenario checks passed: rainfall units/integration, stage boundaries, and stored-input validation. No solver assertions.');
