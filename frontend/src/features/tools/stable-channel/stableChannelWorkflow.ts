import type { BackendGateway, BackendRequestOptions, WorkbenchContext } from '../../../shared/workbench';

export const stableChannelMethods = { open: 'stableChannel.open', compute: 'stableChannel.compute', save: 'stableChannel.save' } as const;
export const solveTargets = ['Depth', 'Width', 'Slope', 'ParticleSize'] as const;
export type SolveTarget = typeof solveTargets[number];
export const solutionMethods = ['Lane', 'Shields', 'UserDefinedMobility'] as const;
export type SolutionMethod = typeof solutionMethods[number];
export const equations = ['Strickler', 'Manning'] as const;
export type Equation = typeof equations[number];
export const partNames = ['leftBank', 'bed', 'rightBank'] as const;
export type PartName = typeof partNames[number];
export const scalarNames = ['depth', 'discharge', 'slope', 'leftBankSideSlope', 'rightBankSideSlope', 'temperature', 'width'] as const;
export type ScalarName = typeof scalarNames[number];
export const partNumberNames = ['d', 'angleOfRepose', 'roughness', 'specificGravity', 'shieldsParameter'] as const;
export type PartNumberName = typeof partNumberNames[number];
export type ChannelPart = Record<PartNumberName, number | null> & { equation: Equation };
export type ChannelState = Record<ScalarName, number | null> & Record<PartName, ChannelPart> & { solutionMethod: SolutionMethod };
export type RawPart = Record<PartNumberName, string> & { equation: Equation };
export type ChannelDraft = Record<ScalarName, string> & Record<PartName, RawPart> & { solutionMethod: SolutionMethod; solveFor: SolveTarget[] };
export interface ChannelSource { sourceID: string; projectPath: string | null; projectRevision: string | null;
  designPath: string | null; designRevision: string | null; inMemory: boolean }
export interface ChannelOpen { requestID: string; source: ChannelSource; state: ChannelState }
export interface ChannelCompute { requestID: string; sourceID: string; state: ChannelState; solveFor: SolveTarget[]; computed: boolean; errorCode: string | null }
export interface ChannelSave { requestID: string; sourceID: string; designPath: string; designRevision: string; state: ChannelState }
export interface ChannelCapture { context: WorkbenchContext; contextKey: string; backend: BackendGateway; backendRevision: number; source: ChannelSource }
export interface ChannelSaveRequest { requestID: string; source: ChannelSource; state: ChannelState; coreLayerContext: WorkbenchContext['coreLayerContext'] }
export interface ChannelPendingSave { capture: ChannelCapture; request: ChannelSaveRequest; draft: ChannelDraft; operationID: string | null }

export class StableChannelError extends Error {
  constructor(message: string, readonly details?: unknown) { super(message); this.name = 'StableChannelError'; }
}
const fail = (detail: string): never => { throw new StableChannelError('服务器回执与本次稳定河道请求不一致，已保留当前输入。', detail); };
const record = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);
export const isSHA = (value: unknown): value is string => typeof value === 'string' && /^[0-9a-f]{64}$/i.test(value);
const shaEqual = (a: unknown, b: unknown): boolean => isSHA(a) && isSHA(b) && a.toUpperCase() === b.toUpperCase();
export const sameChannelPath = (a: string | null, b: string | null): boolean => a === b || (a !== null && b !== null && a.replaceAll('\\', '/') === b.replaceAll('\\', '/'));
export function channelContextKey(context: WorkbenchContext): string {
  return JSON.stringify([context.projectSessionID, context.projectPath, context.projectRevision?.toUpperCase(), context.projectXml, context.coreLayerContext ?? null]);
}
export function channelCaptureCurrent(capture: ChannelCapture | null, context: WorkbenchContext, backend: BackendGateway, backendRevision = 0): boolean {
  return !!capture && capture.contextKey === channelContextKey(context) && capture.backend === backend && capture.backendRevision === backendRevision;
}
export function parseChannelState(raw: unknown): ChannelState {
  if (!record(raw)) return fail('missing state');
  const number = (value: unknown): number | null => value === null ? null : typeof value === 'number' && Number.isFinite(value) ? value : fail('non-finite or missing state number');
  if (!solutionMethods.includes(raw.solutionMethod as SolutionMethod)) return fail('invalid solution method');
  const result = { solutionMethod: raw.solutionMethod } as ChannelState;
  for (const name of scalarNames) result[name] = number(raw[name]);
  for (const name of partNames) {
    const part = raw[name];
    if (!record(part) || !equations.includes(part.equation as Equation)) return fail('missing part or equation');
    result[name] = { equation: part.equation } as ChannelPart;
    for (const field of partNumberNames) result[name][field] = number(part[field]);
  }
  return result;
}
export function parseChannelOpen(raw: unknown, requestID: string, context: WorkbenchContext, memoryID: string): ChannelOpen {
  if (!record(raw) || raw.requestID !== requestID || !record(raw.source)) return fail('open request identity');
  const source = raw.source;
  if (typeof source.sourceID !== 'string' || typeof source.inMemory !== 'boolean') return fail('source identity');
  if (!sameChannelPath(typeof source.projectPath === 'string' ? source.projectPath : null, context.projectPath)) return fail('project path');
  if (context.projectPath ? !shaEqual(source.projectRevision, context.projectRevision) : source.projectRevision !== null || source.projectPath !== null) return fail('project revision');
  if (source.inMemory) {
    if (source.sourceID !== memoryID || source.designPath !== null || source.designRevision !== null) return fail('memory source');
  } else if (!context.projectPath || !isSHA(source.sourceID) || typeof source.designPath !== 'string' || !source.designPath || !isSHA(source.designRevision)) return fail('persisted design source');
  return { requestID, source: source as unknown as ChannelSource, state: parseChannelState(raw.state) };
}
/** Open cannot consume ProjectXml. Prove that the captured XML is precisely the saved file first. */
export function assertChannelProjectReadback(raw: unknown, context: WorkbenchContext): void {
  if (!record(raw) || raw.success !== true || typeof raw.projectPath !== 'string' ||
      !sameChannelPath(raw.projectPath, context.projectPath) || !shaEqual(raw.revision, context.projectRevision))
    throw new StableChannelError('工程文件修订已变化，请重新打开工程后读取稳定河道。');
  if (typeof context.projectXml !== 'string' || raw.projectXml !== context.projectXml)
    throw new StableChannelError('当前工程 XML 有未保存修改。请先保存或明确放弃工程修改，再读取稳定河道。');
}
export async function assertChannelSourceID(source: ChannelSource): Promise<void> {
  if (source.inMemory) return;
  const bytes = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(`${source.projectPath}\n${source.designPath}`));
  const hash = Array.from(new Uint8Array(bytes), item => item.toString(16).padStart(2, '0')).join('');
  if (!shaEqual(hash, source.sourceID)) fail('persisted source hash');
}
export function parseChannelCompute(raw: unknown, requestID: string, source: ChannelSource, targets: SolveTarget[]): ChannelCompute {
  if (!record(raw) || raw.requestID !== requestID || raw.sourceID !== source.sourceID || typeof raw.computed !== 'boolean' ||
      !Array.isArray(raw.solveFor) || JSON.stringify(raw.solveFor) !== JSON.stringify(targets) ||
      !(raw.errorCode === null || typeof raw.errorCode === 'string') || (raw.computed && raw.errorCode !== null) || (!raw.computed && !raw.errorCode)) return fail('compute identity or outcome');
  const state = parseChannelState(raw.state);
  if (raw.computed) {
    for (const target of targets) {
      const values = target === 'ParticleSize' ? partNames.map(part => state[part].d) : [state[target.toLowerCase() as 'depth' | 'width' | 'slope']];
      if (values.some(value => value === null)) return fail('missing solved number');
    }
  }
  return { requestID, sourceID: source.sourceID, solveFor: [...targets], state, computed: raw.computed, errorCode: raw.errorCode as string | null };
}
export function parseChannelSave(raw: unknown, pending: ChannelPendingSave): ChannelSave {
  if (!record(raw) || raw.requestID !== pending.request.requestID || raw.sourceID !== pending.request.source.sourceID ||
      typeof raw.designPath !== 'string' || !sameChannelPath(raw.designPath, pending.request.source.designPath) || !isSHA(raw.designRevision)) return fail('save receipt identity');
  const state = parseChannelState(raw.state);
  if (JSON.stringify(state) !== JSON.stringify(parseChannelState(pending.request.state))) return fail('saved state differs from submitted state');
  return { requestID: pending.request.requestID, sourceID: pending.request.source.sourceID, designPath: raw.designPath, designRevision: raw.designRevision, state };
}
export const channelDraftKey = (draft: ChannelDraft): string => JSON.stringify(draft);
const rawNumber = (value: number | null): string => value === null ? '' : String(value);
export function channelDraftFromState(state: ChannelState, solveFor: SolveTarget[] = []): ChannelDraft {
  const draft = { solutionMethod: state.solutionMethod, solveFor: [...solveFor] } as ChannelDraft;
  for (const name of scalarNames) draft[name] = rawNumber(state[name]);
  for (const name of partNames) {
    draft[name] = { equation: state[name].equation } as RawPart;
    for (const field of partNumberNames) draft[name][field] = rawNumber(state[name][field]);
  }
  return draft;
}
export function channelStateFromDraft(draft: ChannelDraft): ChannelState {
  const number = (raw: string): number | null => {
    if (!raw.trim()) return null;
    if (!/^[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?$/.test(raw.trim()) || !Number.isFinite(Number(raw)))
      throw new StableChannelError('有数值尚未输入完整，请检查表单；原始输入已保留。');
    return Number(raw);
  };
  const state = { solutionMethod: draft.solutionMethod } as ChannelState;
  for (const name of scalarNames) state[name] = number(draft[name]);
  for (const name of partNames) {
    state[name] = { equation: draft[name].equation } as ChannelPart;
    for (const field of partNumberNames) state[name][field] = number(draft[name][field]);
  }
  return parseChannelState(state);
}
export function channelComputeInputError(draft: ChannelDraft): string | null {
  if (draft.solveFor.length !== 2 || new Set(draft.solveFor).size !== 2) return '请选择恰好两个求解目标。';
  let state: ChannelState;
  try { state = channelStateFromDraft(draft); } catch { return '有数值尚未输入完整，请检查表单。'; }
  if (state.temperature === null || state.temperature < 40 || state.temperature > 120) return '水温须处于原求解器支持的 40–120 °F 范围。';
  const required = [state.discharge, state.leftBankSideSlope, state.rightBankSideSlope];
  for (const target of ['Depth', 'Width', 'Slope'] as const) if (!draft.solveFor.includes(target)) required.push(state[target.toLowerCase() as 'depth' | 'width' | 'slope']);
  for (const part of partNames) {
    required.push(state[part].angleOfRepose, state[part].roughness, state[part].specificGravity);
    if (!draft.solveFor.includes('ParticleSize')) required.push(state[part].d);
    if (draft.solutionMethod === 'UserDefinedMobility') required.push(state[part].shieldsParameter);
  }
  return required.some(value => value === null) ? '请填写所有已知参数；求解目标可以留空。' : null;
}
/** Update only solved outputs; whitespace, exponent spelling and inactive inputs stay intact. */
export function mergeChannelComputed(draft: ChannelDraft, state: ChannelState): ChannelDraft {
  const next = structuredClone(draft);
  for (const target of draft.solveFor) {
    if (target === 'ParticleSize') for (const part of partNames) next[part].d = rawNumber(state[part].d);
    else { const field = target.toLowerCase() as 'depth' | 'width' | 'slope'; next[field] = rawNumber(state[field]); }
  }
  return next;
}
export function captureChannelOperation(pending: ChannelPendingSave, operationID: string): void {
  if (!operationID || pending.operationID && pending.operationID !== operationID) throw new StableChannelError('返回的保存任务编号与原任务不一致。');
  pending.operationID = operationID;
}
export async function resumeChannelSave(pending: ChannelPendingSave, options?: BackendRequestOptions): Promise<unknown> {
  if (!pending.operationID || !pending.capture.backend.resumeOperation) throw new StableChannelError('原请求没有可续查的任务编号或连接，请保留本次输入并核对服务器。');
  return pending.capture.backend.resumeOperation(pending.operationID, options);
}
export const channelTerminalConfirmed = (error: unknown): boolean => record(error) && error.terminalConfirmed === true;
export function channelComputeError(code: string | null): string {
  return ({ selectExactlyTwoTargets: '请选择恰好两个求解目标。', unsupportedSolveFor: '求解目标组合不受支持。', invalidSolutionMethod: '求解方法无效。', invalidEquation: '糙率公式无效。', missingPart: '左岸、河床或右岸参数不完整。', missingInput: '请补全已知参数。', temperatureOutsideSourceRange: '水温须处于 40–120 °F。', solverFailed: '原求解器未能完成计算，请检查参数组合。', nonFiniteResult: '原求解器未得到有限数值，请检查参数组合。', sourceChanged: '工程或稳定河道设计已变化，请核对来源后重新读取。' } as Record<string, string>)[code ?? ''] ?? '计算未完成，已保留本次输入。';
}
