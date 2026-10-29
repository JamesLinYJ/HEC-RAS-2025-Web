/** Presentation-only scenario. Never serialize these values as a HEC-RAS model. */
export interface FloodScenario {
  name: string;
  rainfall: number;
  stormDuration: number;
  breachStart: number;
  breachDuration: number;
  breachWidth: number;
  reservoirLevel: number;
  rainPattern: 'front' | 'middle' | 'back';
}

export const scenarioPresets = {
  storm: { name: '暴雨 · 渐进溃坝', rainfall: 65, stormDuration: 120, breachStart: 35, breachDuration: 25, breachWidth: 60, reservoirLevel: 18, rainPattern: 'middle' },
  sudden: { name: '突发 · 快速溃坝', rainfall: 25, stormDuration: 90, breachStart: 15, breachDuration: 5, breachWidth: 85, reservoirLevel: 22, rainPattern: 'front' },
  slow: { name: '持续降雨 · 缓慢发展', rainfall: 40, stormDuration: 180, breachStart: 70, breachDuration: 45, breachWidth: 40, reservoirLevel: 14, rainPattern: 'back' },
} satisfies Record<string, FloodScenario>;

export const DEFAULT_SCENARIO: FloodScenario = { ...scenarioPresets.storm };
export const sceneDuration = (s: FloodScenario): number => Math.max(s.stormDuration, s.breachStart + s.breachDuration + 60);
export const clamp = (v: number, lo = 0, hi = 1): number => Math.min(hi, Math.max(lo, v));
export const ease = (v: number): number => { const x = clamp(v); return x * x * (3 - 2 * x); };

export function rainAt(s: FloodScenario, minute: number): number {
  if (minute < 0 || minute >= s.stormDuration || s.rainfall === 0) return 0;
  const x = minute / s.stormDuration;
  const peak = s.rainPattern === 'front' ? 0.25 : s.rainPattern === 'back' ? 0.75 : 0.5;
  const position = x < peak ? x / peak : (1 - x) / (1 - peak);
  return s.rainfall * (0.22 + 0.78 * Math.sin(clamp(position) * Math.PI / 2));
}

export function accumulatedRain(s: FloodScenario, minute: number): number {
  const end = Math.min(clamp(minute, 0, sceneDuration(s)), s.stormDuration);
  const n = 160, step = end / n;
  let total = 0;
  for (let i = 0; i < n; i++) total += rainAt(s, (i + 0.5) * step) * step / 60;
  return total;
}

export function scenarioStage(s: FloodScenario, minute: number): { index: number; title: string; note: string } {
  if (minute < s.breachStart) return { index: 0, title: '降雨蓄水', note: '降雨进入库区，水面逐渐抬升' };
  if (minute < s.breachStart + s.breachDuration) return { index: 1, title: '溃口发展', note: '溃口逐步扩展，水流向下游释放' };
  if (minute < s.breachStart + s.breachDuration + 35) return { index: 2, title: '洪水演进', note: '沿河谷观察洪水前缘的推进' };
  return { index: 3, title: '下游扩散', note: '观察下游低洼地带的积水变化' };
}

export function minuteLabel(minute: number): string {
  const whole = Math.max(0, Math.floor(minute));
  return `${Math.floor(whole / 60).toString().padStart(2, '0')}:${(whole % 60).toString().padStart(2, '0')}:${Math.floor((minute - whole) * 60).toString().padStart(2, '0')}`;
}

export const SCENARIO_KEY = 'hecras.flood-studio.visual-scenario.v1';
export function parseScenario(value: unknown): FloodScenario | null {
  if (!value || typeof value !== 'object') return null;
  const s = value as Record<string, unknown>;
  const ranges: Record<string, [number, number]> = { rainfall: [0, 120], stormDuration: [60, 240], breachStart: [5, 120], breachDuration: [1, 60], breachWidth: [15, 100], reservoirLevel: [8, 26] };
  for (const [key, [lo, hi]] of Object.entries(ranges)) if (typeof s[key] !== 'number' || !Number.isFinite(s[key]) || s[key] < lo || s[key] > hi) return null;
  if (!['front', 'middle', 'back'].includes(String(s.rainPattern)) || typeof s.name !== 'string' || s.name.length > 80) return null;
  return { name: s.name, rainfall: s.rainfall as number, stormDuration: s.stormDuration as number, breachStart: s.breachStart as number, breachDuration: s.breachDuration as number, breachWidth: s.breachWidth as number, reservoirLevel: s.reservoirLevel as number, rainPattern: s.rainPattern as FloodScenario['rainPattern'] };
}
