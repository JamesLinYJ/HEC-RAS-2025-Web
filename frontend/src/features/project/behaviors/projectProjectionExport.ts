import type { WorkbenchContext } from '../../../shared/workbench';
import { record, validateSessionReferenceContext } from '../../../shared/sessionReferences';
import { isAbsoluteServerPath } from '../ProjectTypes';

export const projectProjectionExportMethod = 'project.projection.export';
export const projectProjectionOwner = (id: string): string => `project.projection-export:${id}`;
const sha256 = (value: unknown): value is string => typeof value === 'string' && /^[a-f\d]{64}$/i.test(value);
export interface ProjectProjectionSource {
  context: WorkbenchContext;
  /** The catalog projection must come from this exact project/XML/Core capture. */
  catalogSourceKey: string | null;
  projectionWKT: string | null;
}
export interface ProjectProjectionCapture {
  id: string;
  source: ProjectProjectionSource;
  sourceKey: string;
  request: {
    projectPath: string; projectXml: string; expectedProjectRevision: string; destinationPath: string;
    projectSessionID: string; coreLayerContext: Record<string, unknown> | null;
  };
}
export interface ProjectProjectionReceipt { projectPath: string; projectRevision: string; outputPath: string }
export interface ProjectProjectionFile { sourcePath: string; canonicalPath: string; revision: string; length: number }

export function projectProjectionSourceKey(context: WorkbenchContext): string {
  // The selected model never supplies the project path or revision.
  return JSON.stringify([context.projectSessionID, context.projectPath, context.projectXml,
    context.projectRevision, context.coreLayerContext ?? null]);
}
export function projectProjectionSourceIssue(source: ProjectProjectionSource): string | undefined {
  const { context } = source;
  if (!context.projectPath || !isAbsoluteServerPath(context.projectPath) || /[\r\n\0]/.test(context.projectPath))
    return '请先打开已保存的服务器工程；内存工程和浏览器本地文件不能导出投影。';
  if (!context.projectSessionID || !context.projectXml?.trim() || !sha256(context.projectRevision))
    return '工程会话、XML 或源修订不完整，请重新打开工程。';
  try { if (context.coreLayerContext) validateSessionReferenceContext(context.coreLayerContext, context); }
  catch { return '工程外部来源身份与当前会话不一致，请重新读取工程。'; }
  if (source.catalogSourceKey !== projectProjectionSourceKey(context)) return '正在等待当前工程的投影信息，请刷新目录后重试。';
  if (!source.projectionWKT?.trim()) return '当前工程没有可导出的投影；本地坐标系不生成 ESRI 投影文件。';
  return undefined;
}
export function projectProjectionDestination(projectPath: string): string {
  if (!isAbsoluteServerPath(projectPath) || /[\r\n\0]/.test(projectPath)) throw new Error('工程路径不是服务器绝对路径。');
  const slash = projectPath.includes('\\') ? '\\' : '/';
  const index = Math.max(projectPath.lastIndexOf('/'), projectPath.lastIndexOf('\\'));
  if (index < 0 || index === projectPath.length - 1) throw new Error('工程文件路径无效。');
  return `${projectPath.slice(0, index)}${slash}Exports${slash}Projection.prj`;
}
export function captureProjectProjection(source: ProjectProjectionSource, id: string): ProjectProjectionCapture {
  const issue = projectProjectionSourceIssue(source);
  if (issue) throw new Error(issue);
  const detached = structuredClone(source), context = detached.context;
  return { id, source: detached, sourceKey: projectProjectionSourceKey(context), request: {
    projectPath: context.projectPath!, projectXml: context.projectXml!, expectedProjectRevision: context.projectRevision!,
    destinationPath: projectProjectionDestination(context.projectPath!), projectSessionID: context.projectSessionID,
    coreLayerContext: context.coreLayerContext ?? null,
  } };
}
export function sameProjectionPath(left: string, right: string): boolean {
  const normalize = (path: string): string => {
    const value = path.replace(/\\/g, '/');
    return /^[A-Za-z]:\//.test(value) || value.startsWith('//') ? value.toLowerCase() : value;
  };
  return normalize(left) === normalize(right);
}
export function parseProjectProjectionReceipt(value: unknown, capture: ProjectProjectionCapture): ProjectProjectionReceipt {
  if (!record(value) || typeof value.projectPath !== 'string' || !sameProjectionPath(value.projectPath, capture.request.projectPath) ||
      !sha256(value.projectRevision) || value.projectRevision.toUpperCase() !== capture.request.expectedProjectRevision.toUpperCase() ||
      typeof value.outputPath !== 'string') throw new Error('投影导出回执与捕获的工程或源修订不一致。');
  const output = value.outputPath.replace(/\\/g, '/'), destination = capture.request.destinationPath.replace(/\\/g, '/');
  const directory = destination.slice(0, destination.lastIndexOf('/'));
  if (!sameProjectionPath(output.slice(0, output.lastIndexOf('/')), directory) ||
      !/^Projection(?: \([1-9]\d{0,2}\))?\.prj$/.test(output.slice(output.lastIndexOf('/') + 1)))
    throw new Error('投影导出回执未指向当前工程 Exports 目录中的唯一 Projection.prj 文件。');
  return { projectPath: value.projectPath, projectRevision: value.projectRevision, outputPath: value.outputPath };
}
export function parseProjectProjectionFile(value: unknown, receipt: ProjectProjectionReceipt): ProjectProjectionFile {
  if (!record(value) || typeof value.sourcePath !== 'string' || !sameProjectionPath(value.sourcePath, receipt.outputPath) ||
      typeof value.canonicalPath !== 'string' || !sameProjectionPath(value.canonicalPath, receipt.outputPath) ||
      !sha256(value.revision) || typeof value.length !== 'number' || !Number.isSafeInteger(value.length) || value.length <= 0)
    throw new Error('投影文件读回未确认同一文件、有效 SHA-256 和非零长度。');
  return { sourcePath: value.sourcePath, canonicalPath: value.canonicalPath, revision: value.revision, length: value.length };
}
