import { projectProjectionExportMethod, projectProjectionSourceIssue, type ProjectProjectionSource } from './projectProjectionExport';

/** Counterpart of GenericProjectBehaviors.cs; this batch migrates ExportProjection. */
export const GenericProjectBehaviors = {
  ExportProjection: {
    name: '导出投影', description: '将当前工程投影导出为 ESRI 投影文件（.prj）。',
    method: projectProjectionExportMethod,
    disabledReason: projectProjectionSourceIssue,
    canExecute: (source: ProjectProjectionSource): boolean => !projectProjectionSourceIssue(source),
  },
};
