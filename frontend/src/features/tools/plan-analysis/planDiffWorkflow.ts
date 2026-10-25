import type { WorkbenchContext } from '../../../shared/workbench';
import type { PlanDiffOption } from './PlanDiffOption';
import { planDiffOptionKey } from './PlanDiffOption';

export interface PlanDiffProjectCapture {
  projectPath: string;
  projectRevision: string;
  projectXml: string;
  projectXmlFingerprint: string;
  coreLayerContext: Record<string, unknown> | null;
}

export interface PlanDiffOptions {
  projectPath: string;
  projectRevision: string;
  projectXmlFingerprint: string;
  options: PlanDiffOption[];
}

export interface PlanDiffRow {
  key: string;
  leftValue: string;
  rightValue: string;
  differs: boolean;
}

export interface PlanDiffResult {
  projectPath: string;
  projectRevision: string;
  projectXmlFingerprint: string;
  left: PlanDiffOption;
  right: PlanDiffOption;
  rows: PlanDiffRow[];
}

const sha256 = /^[\da-f]{64}$/i;
const absolutePath = /^(?:\/|[a-z]:[\\/]|\\\\)/i;

function stable(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(stable);
  if (value && typeof value === 'object') return Object.fromEntries(Object.entries(value)
    .sort(([left], [right]) => left < right ? -1 : left > right ? 1 : 0)
    .map(([key, entry]) => [key, stable(entry)]));
  return value;
}

/** Includes the selected source even when the project itself has not changed. */
export function planDiffSourceKey(context: WorkbenchContext, backendRevision = 0): string {
  return JSON.stringify([context.projectSessionID, context.projectPath, context.projectRevision,
    context.projectXml, stable(context.coreLayerContext ?? null), stable(context.selectedAsset), backendRevision]);
}

export function planDiffComparisonKey(sourceKey: string, draftCaptureKey: string,
  left: PlanDiffOption, right: PlanDiffOption): string {
  return JSON.stringify([sourceKey, draftCaptureKey, planDiffOptionKey(left), planDiffOptionKey(right)]);
}

export async function capturePlanDiffProject(context: WorkbenchContext): Promise<PlanDiffProjectCapture> {
  if (!context.projectSessionID || !context.projectPath || !absolutePath.test(context.projectPath)
      || !/\.ras$/i.test(context.projectPath) || !context.projectRevision || !sha256.test(context.projectRevision)
      || typeof context.projectXml !== 'string' || !context.projectXml.trim()) {
    throw new Error('请先打开具有完整源修订和工程 XML 的已保存工程。');
  }
  // Copy before awaiting the hash: the request owns these exact source values.
  const projectPath = context.projectPath;
  const projectRevision = context.projectRevision;
  const projectXml = context.projectXml;
  const coreLayerContext = context.coreLayerContext ? structuredClone(context.coreLayerContext) : null;
  if (!globalThis.crypto?.subtle) throw new Error('当前浏览器无法校验工程 XML 的 SHA-256，未发出比较请求。');
  const bytes = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(projectXml));
  const projectXmlFingerprint = Array.from(new Uint8Array(bytes), byte => byte.toString(16).padStart(2, '0'))
    .join('').toUpperCase();
  return { projectPath, projectRevision, projectXml, projectXmlFingerprint, coreLayerContext };
}

function record(value: unknown, label: string): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error(`${label}不是有效对象。`);
  return value as Record<string, unknown>;
}

function nonempty(value: unknown): value is string { return typeof value === 'string' && value.trim().length > 0; }
function sameRevision(left: unknown, right: string): boolean {
  return typeof left === 'string' && sha256.test(left) && left.toUpperCase() === right.toUpperCase();
}

function assertProject(value: Record<string, unknown>, capture: PlanDiffProjectCapture): void {
  if (value.projectPath !== capture.projectPath || !sameRevision(value.projectRevision, capture.projectRevision)
      || !sameRevision(value.projectXmlFingerprint, capture.projectXmlFingerprint)) {
    throw new Error('计划分析回执与当前工程路径、源修订或 XML 快照不匹配，未采用返回数据。');
  }
}

export function parsePlanDiffOption(value: unknown): PlanDiffOption {
  const option = record(value, '计划来源');
  if (!nonempty(option.id) || !nonempty(option.planPath) || !absolutePath.test(option.planPath)
      || typeof option.planRevision !== 'string' || !sha256.test(option.planRevision)
      || !(option.resultPath === null || (nonempty(option.resultPath) && absolutePath.test(option.resultPath)))
      || !(option.resultRevision === null || (typeof option.resultRevision === 'string' && sha256.test(option.resultRevision)))
      || typeof option.displayName !== 'string' || typeof option.headerName !== 'string'
      || typeof option.isResult !== 'boolean'
      || (!option.isResult && (option.resultPath !== null || option.resultRevision !== null))) {
    throw new Error('计划来源目录缺少原始 ID、路径、修订或标签，未采用不完整的来源。');
  }
  return {
    id: option.id, planPath: option.planPath, planRevision: option.planRevision,
    resultPath: option.resultPath, resultRevision: option.resultRevision,
    displayName: option.displayName, headerName: option.headerName, isResult: option.isResult,
  };
}

export function parsePlanDiffOptions(value: unknown, capture: PlanDiffProjectCapture): PlanDiffOptions {
  const response = record(value, '计划来源目录');
  assertProject(response, capture);
  if (!Array.isArray(response.options)) throw new Error('计划来源回执没有返回来源列表。');
  const options = response.options.map(parsePlanDiffOption);
  if (new Set(options.map(option => option.id)).size !== options.length)
    throw new Error('计划来源目录包含重复 ID，无法确定比较对象。');
  const revisions = new Map<string, string>();
  for (const option of options) {
    const previous = revisions.get(option.planPath);
    if (previous && !sameRevision(option.planRevision, previous))
      throw new Error('同一 Plan 的来源修订不一致，请重新读取目录。');
    revisions.set(option.planPath, option.planRevision);
  }
  return { projectPath: capture.projectPath, projectRevision: response.projectRevision as string,
    projectXmlFingerprint: response.projectXmlFingerprint as string, options };
}

function sameOption(returned: PlanDiffOption, expected: PlanDiffOption): boolean {
  return returned.id === expected.id && returned.planPath === expected.planPath
    && sameRevision(returned.planRevision, expected.planRevision) && returned.resultPath === expected.resultPath
    && (returned.resultRevision === expected.resultRevision || (expected.resultRevision !== null
      && sameRevision(returned.resultRevision, expected.resultRevision)))
    && returned.displayName === expected.displayName && returned.headerName === expected.headerName
    && returned.isResult === expected.isResult;
}

export function parsePlanDiffResult(value: unknown, capture: PlanDiffProjectCapture,
  expectedLeft: PlanDiffOption, expectedRight: PlanDiffOption): PlanDiffResult {
  const response = record(value, '计划比较回执');
  assertProject(response, capture);
  const left = parsePlanDiffOption(response.left);
  const right = parsePlanDiffOption(response.right);
  if (!sameOption(left, expectedLeft) || !sameOption(right, expectedRight))
    throw new Error('计划比较回执的左右来源、关联 Plan 或修订已变化，请重新读取来源目录。');
  if (!Array.isArray(response.rows)) throw new Error('计划比较回执没有返回属性行。');
  const rows = response.rows.map((value): PlanDiffRow => {
    const row = record(value, '计划属性行');
    if (!nonempty(row.key) || typeof row.leftValue !== 'string' || typeof row.rightValue !== 'string'
        || typeof row.differs !== 'boolean' || row.differs !== (row.leftValue !== row.rightValue))
      throw new Error('计划属性行缺少原始字符串值，或差异标记与原始值不一致。');
    return { key: row.key, leftValue: row.leftValue, rightValue: row.rightValue, differs: row.differs };
  });
  if (rows.some((row, index) => index > 0 && rows[index - 1].key >= row.key))
    throw new Error('计划比较回执的属性键重复或不符合原始顺序。');
  return { projectPath: capture.projectPath, projectRevision: response.projectRevision as string,
    projectXmlFingerprint: response.projectXmlFingerprint as string, left, right, rows };
}

export interface PlanDiffRequest {
  generation: number;
  key: string;
  controller: AbortController;
}

/** A later request or explicit cancellation retires the old request even if its source returns. */
export class PlanDiffRequestGate {
  private generation = 0;
  private request: PlanDiffRequest | null = null;

  begin(key: string): PlanDiffRequest {
    this.retire();
    const request = { generation: this.generation, key, controller: new AbortController() };
    this.request = request;
    return request;
  }

  accepts(request: PlanDiffRequest, currentKey: string): boolean {
    return this.request === request && request.generation === this.generation
      && request.key === currentKey && !request.controller.signal.aborted;
  }

  retire(): void {
    this.generation += 1;
    this.request?.controller.abort();
    this.request = null;
  }
}
