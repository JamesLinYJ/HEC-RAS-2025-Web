import { parseScenario, type FloodScenario } from './scenario';

export interface FloodGeometry {
  vertices: number[][]; cells: number[][]; centers: number[][]; areas: number[];
  bedElevation: number[]; damEdges: number[][]; damY: number; damCrest: number; reservoirStage: number;
}
export interface FloodFrame {
  timeSeconds: number; fields: string; rainRate: number; rainTotal: number; breachFraction: number;
  discharge: number; storedVolume: number; wetArea: number; maxDepth: number; maxSpeed: number; damCrests: number[];
}
export interface FloodResult {
  state: 'completed'; runId: string; modelVersion: string; scenario: FloodScenario; geometry: FloodGeometry;
  fieldEncoding: string; frames: FloodFrame[]; steps: number; durationSeconds: number;
}
export interface FloodSample extends Omit<FloodFrame, 'fields'> { values: Float32Array }
export interface DecodedFlood extends FloodResult { decoded: Float32Array[] }
const invalid = () => new Error('收到的推演结果不完整，请重新计算。');
const finite = (n: unknown): n is number => typeof n === 'number' && Number.isFinite(n);

export function validateGeometry(g: FloodGeometry): FloodGeometry {
  if (!g || !Array.isArray(g.vertices) || g.vertices.length > 10000 || g.vertices.length < 4 ||
    !g.vertices.every(v => Array.isArray(v) && v.length === 3 && v.every(finite)) ||
    !Array.isArray(g.cells) || g.cells.length < 1 || g.cells.length > 8000 ||
    !g.cells.every(c => Array.isArray(c) && c.length === 4 && c.every(v => Number.isInteger(v) && v >= 0 && v < g.vertices.length)) ||
    !Array.isArray(g.centers) || g.centers.length !== g.cells.length || !g.centers.every(c => c.length === 2 && c.every(finite)) ||
    !Array.isArray(g.areas) || g.areas.length !== g.cells.length || !g.areas.every(v => finite(v) && v > 0) ||
    !Array.isArray(g.bedElevation) || g.bedElevation.length !== g.cells.length || !g.bedElevation.every(finite) ||
    !Array.isArray(g.damEdges) || !g.damEdges.every(e => e.length === 2 && e.every(v => Number.isInteger(v) && v >= 0 && v < g.vertices.length)) ||
    ![g.damY, g.damCrest, g.reservoirStage].every(finite)) throw invalid();
  return g;
}

export function decodeResult(r: FloodResult): DecodedFlood {
  if (!r || r.state !== 'completed' || !parseScenario(r.scenario) || typeof r.runId !== 'string' || !r.runId ||
      r.fieldEncoding !== 'float32-le/base64:wse,depth,vx,vy' || !finite(r.durationSeconds) || r.durationSeconds <= 0 ||
      !Array.isArray(r.frames) || r.frames.length < 2 || r.frames.length > 122) throw invalid();
  validateGeometry(r.geometry);
  const count = r.geometry.cells.length;
  const decoded = r.frames.map((f, index) => {
    if (!f || ![f.timeSeconds, f.rainRate, f.rainTotal, f.breachFraction, f.discharge, f.storedVolume, f.wetArea, f.maxDepth, f.maxSpeed].every(finite) ||
      (index === 0 ? f.timeSeconds !== 0 : f.timeSeconds <= r.frames[index - 1].timeSeconds) ||
      !Array.isArray(f.damCrests) || f.damCrests.length !== r.geometry.damEdges.length || !f.damCrests.every(finite) ||
      typeof f.fields !== 'string' || f.fields.length > count * 24) throw invalid();
    const raw = atob(f.fields);
    if (raw.length !== count * 16) throw invalid();
    const bytes = Uint8Array.from(raw, v => v.charCodeAt(0)), view = new DataView(bytes.buffer);
    const values = Float32Array.from({ length: count * 4 }, (_, i) => view.getFloat32(i * 4, true));
    if (!values.every(Number.isFinite)) throw invalid();
    return values;
  });
  if (Math.abs(r.frames.at(-1)!.timeSeconds - r.durationSeconds) > .001) throw invalid();
  return { ...r, decoded };
}

/** Linear interpolation between recorded solver output times; no generated water front. */
export function sampleResult(result: DecodedFlood, seconds: number): FloodSample {
  const t = Math.max(0, Math.min(result.durationSeconds, seconds));
  let right = result.frames.findIndex(f => f.timeSeconds >= t);
  if (right < 0) right = result.frames.length - 1;
  const left = Math.max(0, right - 1), a = result.frames[left], b = result.frames[right];
  const weight = a === b ? 0 : (t - a.timeSeconds) / (b.timeSeconds - a.timeSeconds);
  const mix = (x: number, y: number) => x + (y - x) * weight;
  const values = result.decoded[left].map((value, i) => mix(value, result.decoded[right][i]));
  return { timeSeconds: t, values, rainRate: mix(a.rainRate, b.rainRate), rainTotal: mix(a.rainTotal, b.rainTotal),
    breachFraction: mix(a.breachFraction, b.breachFraction), discharge: mix(a.discharge, b.discharge),
    storedVolume: mix(a.storedVolume, b.storedVolume), wetArea: mix(a.wetArea, b.wetArea),
    maxDepth: mix(a.maxDepth, b.maxDepth), maxSpeed: mix(a.maxSpeed, b.maxSpeed), damCrests: a.damCrests.map((v,i) => mix(v,b.damCrests[i])) };
}
