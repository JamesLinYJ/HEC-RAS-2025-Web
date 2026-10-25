import type { WorkbenchContext } from '../../../shared/workbench';

/** Wire contracts retain the original builder's property and value order. */
export interface R2RProjectCapture {
  projectPath: string; projectRevision: string; projectXml: string; projectXmlFingerprint: string;
  coreLayerContext?: Record<string, unknown> | null;
}
export interface R2RPlanDraft {
  assetPath: string; revision: string; fields: Record<string, string>; sourceState?: string | null;
  planTimeWindowEdits?: unknown[] | null; editorID?: string | null; ownerContextID?: string | null;
}
export interface R2RSource {
  sessionID: string; captureKey: string; capture: R2RProjectCapture; drafts: R2RPlanDraft[];
}
export interface R2RValue { value: string; label: string; selected: boolean; removable: boolean }
export interface R2RProperty {
  propertyName: string; displayName: string; valueType: string; inputHint: string;
  isClosedSet: boolean; initialValues: R2RValue[];
}
export interface R2RRule { name: string; reason: string; enabled: boolean }
export interface R2RPlanChoice { planPath: string; revision: string; name: string }
export interface R2ROptions {
  plans: R2RPlanChoice[]; selectedPlanPath: string | null; outputDirectory: string | null;
  properties: R2RProperty[]; rules: R2RRule[];
}
export interface R2RAxis { propertyName: string; values: R2RValue[]; newValue: string }
export interface R2RForm {
  axes: R2RAxis[]; enabledRuleNames: string[]; collapseIrrelevant: boolean; outputDirectory: string;
  overwrite: boolean; nameEmbeddedPlan: boolean; writeManifest: boolean;
}
export interface R2RPreviewRequest {
  capture: R2RProjectCapture; planPath: string; planRevision: string; planDraft: R2RPlanDraft | null;
  axes: { propertyName: string; values: string[] }[]; enabledRuleNames: string[];
  collapseIrrelevant: boolean; outputDirectory: string;
}
export interface R2RAssignment {
  propertyName: string; displayName: string; value: string; isRelevant: boolean; irrelevantReason: string | null;
}
export interface R2RCombination {
  index: number; label: string; filename: string; isExcluded: boolean; isCollapsed: boolean;
  exclusionReason: string | null; assignments: R2RAssignment[];
}
export interface R2RPreview {
  inspectionID: string; outputDirectory: string; writeCount: number; summary: string; hint: string;
  combinations: R2RCombination[];
}
export interface R2RInspected {
  source: R2RSource; request: R2RPreviewRequest; preview: R2RPreview; formKey: string;
}
export type R2RFileStatus = 'Written' | 'Skipped' | 'Failed' | 'Excluded' | 'Cancelled' | 'NotRun';
export interface R2RFile {
  filename: string; status: R2RFileStatus; filePath: string | null; fileSizeBytes: number; message: string | null;
}
export interface R2REmitReceipt {
  state: 'completed' | 'failed' | 'cancelled'; written: number; skipped: number; failed: number; excluded: number;
  wasCancelled: boolean; wasAborted: boolean; abortReason: string | null; manifestPath: string | null;
  sourcesStillCurrent: boolean; files: R2RFile[];
}
export interface R2RProgress { fraction?: number; message: string; filename?: string; status?: string }

export const r2rMethods = { options: 'plan.r2rMatrix.options', preview: 'plan.r2rMatrix.preview', emit: 'plan.r2rMatrix.emit' } as const;
const failure = (detail: string): never => { throw new Error(`R2R 服务数据未通过核对：${detail}。`); };
const record = (value: unknown): Record<string, unknown> => {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return failure('缺少对象');
  return value as Record<string, unknown>;
};
const str = (value: unknown): string => typeof value === 'string' ? value : failure('字符串字段无效');
const nonempty = (value: unknown): string => str(value).trim() ? str(value) : failure('身份字段为空');
const nullable = (value: unknown): string | null => value === null || value === undefined ? null : str(value);
const bool = (value: unknown): boolean => typeof value === 'boolean' ? value : failure('开关字段无效');
const count = (value: unknown): number => Number.isSafeInteger(value) && (value as number) >= 0 ? value as number : failure('计数字段无效');
const array = (value: unknown): unknown[] => Array.isArray(value) ? value : failure('列表字段无效');
export const isR2RSHA = (value: unknown): value is string => typeof value === 'string' && /^[a-f\d]{64}$/i.test(value);
const sha = (value: unknown): string => isR2RSHA(value) ? value : failure('缺少 SHA-256 修订');
const equalSHA = (left: unknown, right: string): boolean => sha(left).toUpperCase() === sha(right).toUpperCase();
export function sameR2RPath(left: string, right: string): boolean {
  const normalize = (value: string): string => {
    const normalized = value.replace(/\\/g, '/').replace(/\/+$/, '');
    return /^(?:[a-z]:\/|\/\/)/i.test(normalized) ? normalized.toLowerCase() : normalized;
  };
  return normalize(left) === normalize(right);
}
export const isR2RAbsolutePath = (value: string): boolean => /^(?:\/|[a-z]:[\\/]|\\\\)/i.test(value);
const unique = (values: string[], detail: string): void => { if (new Set(values).size !== values.length) failure(detail); };

export async function captureR2RSource(context: WorkbenchContext, captureKey: string, drafts: readonly R2RPlanDraft[]): Promise<R2RSource> {
  if (!context.projectSessionID || !context.projectPath || !isR2RAbsolutePath(context.projectPath) ||
      !context.projectXml || !isR2RSHA(context.projectRevision) || !captureKey) failure('请先打开有完整修订的工程');
  // Clone before the digest yields: the request must never read a later editor draft.
  const captured = structuredClone({ context, drafts: [...drafts] });
  const bytes = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(captured.context.projectXml!));
  const fingerprint = Array.from(new Uint8Array(bytes), byte => byte.toString(16).padStart(2, '0')).join('').toUpperCase();
  unique(captured.drafts.map(draft => draft.assetPath), '同一计划有多个草稿');
  for (const draft of captured.drafts) {
    nonempty(draft.assetPath); sha(draft.revision); record(draft.fields);
    if (!Object.values(draft.fields).every(value => typeof value === 'string')) failure('草稿字段必须保留原始字符串');
  }
  return { sessionID: captured.context.projectSessionID, captureKey, drafts: captured.drafts,
    capture: { projectPath: captured.context.projectPath!, projectRevision: captured.context.projectRevision!,
      projectXml: captured.context.projectXml!, projectXmlFingerprint: fingerprint, coreLayerContext: captured.context.coreLayerContext } };
}

function verifyProject(value: Record<string, unknown>, source: R2RSource): void {
  if (!sameR2RPath(nonempty(value.projectPath), source.capture.projectPath) ||
      !equalSHA(value.projectRevision, source.capture.projectRevision) ||
      !equalSHA(value.projectXmlFingerprint, source.capture.projectXmlFingerprint)) failure('工程路径、修订或 XML 指纹不一致');
}
export function r2rDraftFor(source: R2RSource, plan: R2RPlanChoice): R2RPlanDraft | null {
  const matches = source.drafts.filter(draft => sameR2RPath(draft.assetPath, plan.planPath));
  if (matches.length > 1 || matches[0] && !equalSHA(matches[0].revision, plan.revision)) failure('计划草稿与源修订不一致');
  return matches[0] ?? null;
}
export function parseR2ROptions(raw: unknown, source: R2RSource, requestedPath: string | null): R2ROptions {
  const value = record(raw); verifyProject(value, source);
  const plans = array(value.plans).map(item => {
    const row = record(item); return { planPath: nonempty(row.planPath), revision: sha(row.revision), name: str(row.name) };
  });
  unique(plans.map(plan => plan.planPath), '计划目录身份重复');
  const selectedPlanPath = nullable(value.selectedPlanPath);
  if (plans.length > 0 && (!selectedPlanPath || !plans.some(plan => sameR2RPath(plan.planPath, selectedPlanPath)))) failure('选中的计划不在目录中');
  if (requestedPath && (!selectedPlanPath || !sameR2RPath(requestedPath, selectedPlanPath))) failure('服务端返回了其他计划');
  const properties = array(value.properties).map(item => {
    const row = record(item), isClosedSet = bool(row.isClosedSet);
    const initialValues = array(row.initialValues).map(itemValue => {
      const choice = record(itemValue);
      const result = { value: str(choice.value), label: str(choice.label), selected: bool(choice.selected), removable: bool(choice.removable) };
      if (isClosedSet && result.removable) failure('枚举值不允许删除');
      return result;
    });
    unique(initialValues.map(choice => choice.value), '候选原值重复');
    return { propertyName: nonempty(row.propertyName), displayName: str(row.displayName), valueType: nonempty(row.valueType),
      inputHint: str(row.inputHint), isClosedSet, initialValues };
  });
  unique(properties.map(property => property.propertyName), '参数属性重复');
  const rules = array(value.rules).map(item => {
    const row = record(item); return { name: nonempty(row.name), reason: str(row.reason), enabled: bool(row.enabled) };
  });
  unique(rules.map(rule => rule.name), '规则身份重复');
  return { plans, selectedPlanPath, outputDirectory: nullable(value.outputDirectory), properties, rules };
}

export function initialR2RForm(options: R2ROptions): R2RForm {
  return { axes: [], enabledRuleNames: options.rules.filter(rule => rule.enabled).map(rule => rule.name),
    collapseIrrelevant: true, outputDirectory: options.outputDirectory ?? '', overwrite: false,
    nameEmbeddedPlan: true, writeManifest: true };
}
export function addR2RAxis(form: R2RForm, property: R2RProperty): R2RForm {
  if (form.axes.some(axis => axis.propertyName === property.propertyName)) throw new Error('每个参数只能添加一个变化轴。');
  return { ...form, axes: [...form.axes, { propertyName: property.propertyName, values: structuredClone(property.initialValues), newValue: '' }] };
}
export function addR2RValues(axis: R2RAxis, property: R2RProperty): R2RAxis {
  if (property.isClosedSet) throw new Error('此参数只允许选择 Host 提供的枚举值。');
  const values = property.valueType === 'String' ? [axis.newValue] : axis.newValue.split(/[,;]/);
  if (!values.length || values.some(value => !value.trim())) throw new Error('请输入完整的候选值；多个数值可用逗号或分号分隔。');
  if (new Set([...axis.values.map(value => value.value), ...values]).size !== axis.values.length + values.length) throw new Error('候选值重复，请保留每个原值一次。');
  // Parsing and semantic deduplication belong to the retained C# property, not JS Number/Date.
  return { ...axis, newValue: '', values: [...axis.values, ...values.map(value => ({ value, label: value, selected: true, removable: true }))] };
}
export function makeR2RPreviewRequest(source: R2RSource, options: R2ROptions, planPath: string, form: R2RForm): R2RPreviewRequest {
  const plan = options.plans.find(item => sameR2RPath(item.planPath, planPath));
  if (!plan || !options.selectedPlanPath || !sameR2RPath(planPath, options.selectedPlanPath)) throw new Error('请重新读取当前计划的参数。');
  if (!form.outputDirectory.trim() || !isR2RAbsolutePath(form.outputDirectory)) throw new Error('请输入 C# 服务所在机器上的绝对输出目录。');
  unique(form.axes.map(axis => axis.propertyName), '参数轴重复');
  let combinations = 1;
  const axes = form.axes.map(axis => {
    const property = options.properties.find(item => item.propertyName === axis.propertyName);
    if (!property) throw new Error('参数目录已变化，请重新添加此参数轴。');
    if (axis.newValue.trim()) throw new Error('还有尚未加入列表的候选值，请先添加或清空。');
    const values = axis.values.filter(value => value.selected).map(value => value.value);
    if (!values.length) throw new Error(`参数 ${property.propertyName} 至少需要选择一个值。`);
    unique(values, '候选原值重复');
    if (values.some(value => !value.trim())) throw new Error('候选值不能为空。');
    if (property.isClosedSet && values.some(value => !property.initialValues.some(choice => choice.value === value))) throw new Error('枚举值不在 Host 当前目录中。');
    combinations *= values.length;
    if (combinations > 2048) throw new Error('组合不能超过 2048 个，请减少候选值。');
    return { propertyName: axis.propertyName, values };
  });
  unique(form.enabledRuleNames, '启用规则重复');
  if (form.enabledRuleNames.some(name => !options.rules.some(rule => rule.name === name))) throw new Error('规则目录已变化，请重新读取参数。');
  return structuredClone({ capture: source.capture, planPath: plan.planPath, planRevision: plan.revision,
    planDraft: r2rDraftFor(source, plan), axes, enabledRuleNames: form.enabledRuleNames,
    collapseIrrelevant: form.collapseIrrelevant, outputDirectory: form.outputDirectory });
}

export function r2rFormKey(form: R2RForm): string { return JSON.stringify(form); }
export function isR2RInspectionCurrent(inspection: R2RInspected | null, sessionID: string, captureKey: string, planPath: string, form: R2RForm): boolean {
  return !!inspection && inspection.source.sessionID === sessionID && inspection.source.captureKey === captureKey &&
    sameR2RPath(inspection.request.planPath, planPath) && inspection.formKey === r2rFormKey(form);
}
export function parseR2RPreview(raw: unknown, source: R2RSource, request: R2RPreviewRequest): R2RPreview {
  const value = record(raw); verifyProject(value, source);
  if (!sameR2RPath(nonempty(value.planPath), request.planPath) || !equalSHA(value.planRevision, request.planRevision)) failure('预览属于其他计划修订');
  const outputDirectory = nonempty(value.outputDirectory);
  if (!sameR2RPath(outputDirectory, request.outputDirectory)) failure('预览输出目录不一致');
  const combinations = array(value.combinations).map((item, index) => {
    const row = record(item);
    const assignments = array(row.assignments).map((itemAssignment, axisIndex) => {
      const assignment = record(itemAssignment), propertyName = nonempty(assignment.propertyName);
      if (propertyName !== request.axes[axisIndex]?.propertyName) failure('组合属性顺序与请求不一致');
      return { propertyName, displayName: str(assignment.displayName), value: str(assignment.value),
        isRelevant: bool(assignment.isRelevant), irrelevantReason: nullable(assignment.irrelevantReason) };
    });
    const result = { index: count(row.index), label: str(row.label), filename: str(row.filename),
      isExcluded: bool(row.isExcluded), isCollapsed: bool(row.isCollapsed), exclusionReason: nullable(row.exclusionReason), assignments };
    if (result.index !== index + 1 || assignments.length !== request.axes.length || result.isCollapsed && !result.isExcluded ||
        result.isExcluded && !result.exclusionReason || !result.isExcluded && (!result.filename.endsWith('.r2r.h5') || /[/\\]/.test(result.filename))) failure('组合行身份、顺序或文件名无效');
    return result;
  });
  if (!combinations.length || combinations.length > 2048) failure('组合数量无效');
  unique(combinations.filter(row => !row.isExcluded).map(row => row.filename.toLowerCase()), '输出文件名重复');
  const writeCount = count(value.writeCount);
  if (writeCount !== combinations.filter(row => !row.isExcluded).length) failure('可写数量与组合行不一致');
  return { inspectionID: sha(value.inspectionID), outputDirectory, writeCount, summary: str(value.summary), hint: str(value.hint), combinations };
}

export function parseR2REmitReceipt(raw: unknown, inspected: R2RInspected): R2REmitReceipt {
  const value = record(raw); verifyProject(value, inspected.source);
  if (!sameR2RPath(nonempty(value.planPath), inspected.request.planPath) || !equalSHA(value.planRevision, inspected.request.planRevision) ||
      !equalSHA(value.inspectionID, inspected.preview.inspectionID) ||
      !sameR2RPath(nonempty(value.outputDirectory), inspected.preview.outputDirectory)) failure('导出回执与提交来源、预览或输出目录不一致');
  const files = array(value.files).map((item, index) => {
    const row = record(item), combo = inspected.preview.combinations[index];
    const filename = str(row.filename), status = str(row.status) as R2RFileStatus;
    if (!combo || combo.filename !== filename || !['Written', 'Skipped', 'Failed', 'Excluded', 'Cancelled', 'NotRun'].includes(status) ||
        combo.isExcluded !== (status === 'Excluded')) failure('逐文件回执与预览行不一致');
    const filePath = nullable(row.filePath), fileSizeBytes = count(row.fileSizeBytes);
    if (filePath && !sameR2RPath(filePath, `${inspected.preview.outputDirectory.replace(/[/\\]+$/, '')}/${filename}`) ||
        status === 'Written' && (!filePath || fileSizeBytes <= 0) || status === 'Skipped' && !filePath) failure('实际输出文件路径或大小无效');
    return { filename, status, filePath, fileSizeBytes, message: nullable(row.message) };
  });
  if (files.length !== inspected.preview.combinations.length) failure('逐文件回执数量不完整');
  const state = str(value.state) as R2REmitReceipt['state'];
  const written = count(value.written), skipped = count(value.skipped), failed = count(value.failed), excluded = count(value.excluded);
  const wasCancelled = bool(value.wasCancelled), wasAborted = bool(value.wasAborted);
  for (const [status, amount] of [['Written', written], ['Skipped', skipped], ['Failed', failed], ['Excluded', excluded]] as const) {
    if (files.filter(file => file.status === status).length !== amount) failure('汇总数量与实际文件状态不一致');
  }
  if (!['completed', 'failed', 'cancelled'].includes(state) || (state === 'cancelled') !== wasCancelled ||
      state === 'completed' && (failed > 0 || wasAborted || files.some(file => file.status === 'NotRun' || file.status === 'Cancelled')) ||
      state === 'failed' && !wasAborted && failed === 0) failure('导出终态与逐文件状态不一致');
  const manifestPath = nullable(value.manifestPath);
  if (manifestPath) {
    const split = manifestPath.replace(/\\/g, '/').lastIndexOf('/');
    if (!sameR2RPath(manifestPath.replace(/\\/g, '/').slice(0, split), inspected.preview.outputDirectory) || !manifestPath.endsWith('.csv')) failure('清单不属于实际输出目录');
  }
  return { state, written, skipped, failed, excluded, wasCancelled, wasAborted, abortReason: nullable(value.abortReason),
    manifestPath, sourcesStillCurrent: bool(value.sourcesStillCurrent), files };
}

export function parseR2RProgress(raw: unknown): R2RProgress | null {
  if (!raw || typeof raw !== 'object') return null;
  const value = raw as Record<string, unknown>, values = value.values && typeof value.values === 'object' ? value.values as Record<string, unknown> : {};
  return { fraction: typeof value.fraction === 'number' && Number.isFinite(value.fraction) ? Math.max(0, Math.min(1, value.fraction)) : undefined,
    message: typeof value.message === 'string' ? value.message : '', filename: typeof values.filename === 'string' ? values.filename : undefined,
    status: typeof values.status === 'string' ? values.status : values.phase === 'writing' ? 'Writing' : undefined };
}

/** A failed transport is not evidence that the worker stopped or rolled back files. */
export function r2rConfirmedTerminal(error: unknown): boolean {
  return !!error && typeof error === 'object' && 'terminalConfirmed' in error && error.terminalConfirmed === true;
}
