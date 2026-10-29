import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// Integration regression: the retained CPU solver, not a mocked numerical response.
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const worker = process.env.HECRAS_FLOOD_WORKER || path.join(root, 'backend/Host/bin/Release/net10.0', process.platform === 'win32' ? 'HECRASBackend.exe' : 'HECRASBackend');
const base = { name: '数值回归', rainfall: 0, stormDuration: 60, breachStart: 5, breachDuration: 1, breachWidth: 60, reservoirLevel: 18, rainPattern: 'middle' };
async function run(changes) {
  const scenario = { ...base, ...changes }, started = performance.now();
  const child = spawn(worker, [], { cwd: root, env: process.env, stdio: ['pipe', 'pipe', 'pipe'] });
  let stdout = '', stderr = '';
  child.stdout.on('data', b => { stdout += b; });
  child.stderr.on('data', b => { stderr = (stderr + b).slice(-4000); });
  child.stdin.end(JSON.stringify({ id: 'flood-regression', method: 'flood.compute', parameters: scenario }));
  const timer = setTimeout(() => child.kill('SIGKILL'), 120000);
  try {
    const code = await new Promise((resolve, reject) => { child.once('error', reject); child.once('exit', resolve); });
    const response = JSON.parse(stdout);
    assert.equal(code, 0, JSON.stringify(response.error) + stderr);
    const r = response.result;
    assert.equal(r.state, 'completed');
    assert.deepEqual(r.scenario, scenario);
    assert.ok(r.steps > 0 && r.frames.length >= 2);
    assert.equal(r.frames[0].timeSeconds, 0);
    assert.equal(r.frames.at(-1).timeSeconds, r.durationSeconds);
    const n = r.geometry.cells.length;
    assert.equal(r.geometry.areas.reduce((a,b)=>a+b,0), 840 * 1200);
    let maxMassError = 0;
    for (const [index,f] of r.frames.entries()) {
      if (index) assert.ok(f.timeSeconds > r.frames[index-1].timeSeconds);
      const data = Buffer.from(f.fields, 'base64');
      assert.equal(data.length, n * 16);
      for (let i=0;i<n*4;i++) assert.ok(Number.isFinite(data.readFloatLE(i*4)));
      assert.ok(f.storedVolume >= 0 && f.maxDepth >= 0 && f.maxSpeed >= 0);
      maxMassError = Math.max(maxMassError, Math.abs(f.massBalanceError));
      if (!scenario.rainfall && f.timeSeconds <= scenario.breachStart*60) {
        assert.ok(Math.abs(f.discharge) < 1e-8, 'Closed dam must have zero flux');
        assert.ok(Math.abs(f.storedVolume-r.frames[0].storedVolume) < 1e-5, 'Closed lake must retain its volume');
        assert.ok(f.maxSpeed < 1e-8, 'Lake at rest must remain at rest');
        for (let i=0;i<n;i++) if(r.geometry.centers[i][1]>r.geometry.damY) assert.ok(data.readFloatLE((n+i)*4)<1e-7, 'No flood downstream before breach');
      }
    }
    // 0.25% includes the existing solver's wetting/drying and iterative tolerances.
    assert.ok(maxMassError / r.frames[0].storedVolume < .0025, `Water balance error ${maxMassError}`);
    const expectedRain = scenario.rainfall*scenario.stormDuration/60*(.22+.78*2/Math.PI);
    assert.ok(Math.abs(r.frames.at(-1).rainTotal-expectedRain)<1e-8);
    const peak = Math.max(...r.frames.map(f=>f.discharge));
    console.log(JSON.stringify({scenario, seconds:(performance.now()-started)/1000, cells:n, steps:r.steps, frames:r.frames.length, peakDischarge:peak, relativeMassError:maxMassError/r.frames[0].storedVolume}));
    return { ...r, peak };
  } finally { clearTimeout(timer); }
}
const dry = await run({});
const wet = await run({ rainfall:65 });
const narrow = await run({ breachWidth:15 });
const delayed = await run({ breachStart:10 });
assert.ok(wet.frames.at(-1).storedVolume > dry.frames.at(-1).storedVolume + 1000, 'Rain must enter the numerical water balance');
assert.ok(narrow.peak < dry.peak*.6, 'Narrower breach must reduce peak discharge');
assert.ok(Math.abs(dry.peak-delayed.peak)/dry.peak < 1e-7, 'Time shift alone preserves the dry hydrograph');
await run({ rainfall:65,stormDuration:120,breachStart:35,breachDuration:25 });
await run({ rainfall:120,stormDuration:240,breachStart:120,breachDuration:60,breachWidth:100,reservoirLevel:26,rainPattern:'back' });
console.log('Flood compute integration passed: closed dam, still lake, rainfall, breach width/timing, finite frames, water balance and full-range inputs.');
