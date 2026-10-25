import type { BackendGateway, BackendRequestOptions, WorkbenchContext } from '../shared/workbench';
import { record } from '../shared/sessionReferences';

/** Snapshot-scoped gateway: old requests retain their old capture after a registry change. */
export function sessionBackend(backend: BackendGateway, context: WorkbenchContext): BackendGateway {
  const core = context.coreLayerContext;
  if (!core || !context.projectPath) return backend;
  const projectPath = context.projectPath;
  const inject = (value: unknown, depth: number): unknown => {
    if (!record(value) || depth > 3) return value;
    const result = { ...value };
    if (value.projectPath === projectPath &&
        (value.projectSessionID === undefined || value.projectSessionID === context.projectSessionID) &&
        !Object.prototype.hasOwnProperty.call(value, 'coreLayerContext')) result.coreLayerContext = core;
    // Only DTO containers that can carry a project request are traversed.
    // Arrays and source feature payloads are never walked or rewritten.
    for (const key of ['map', 'model', 'request', 'project'] as const)
      if (record(value[key])) result[key] = inject(value[key], depth + 1);
    return result;
  };
  return {
    supports: method => backend.supports(method),
    request: <T = unknown>(method: string, parameters: unknown, options?: BackendRequestOptions) =>
      backend.request<T>(method, method === 'project.open' || method === 'project.create' ? parameters : inject(parameters, 0), options),
    ...(backend.resumeOperation ? { resumeOperation: backend.resumeOperation.bind(backend) } : {}),
    ...(backend.subscribeCapabilities ? { subscribeCapabilities: backend.subscribeCapabilities.bind(backend) } : {}),
    ...(backend.uploadWorkspace ? { uploadWorkspace: backend.uploadWorkspace.bind(backend) } : {}),
    ...(backend.workspaceArchiveUrl ? { workspaceArchiveUrl: backend.workspaceArchiveUrl.bind(backend) } : {}),
    ...(backend.projectArchiveUrl ? { projectArchiveUrl: backend.projectArchiveUrl.bind(backend) } : {}),
    ...(backend.uploadMapSources ? { uploadMapSources: backend.uploadMapSources.bind(backend) } : {}),
  };
}
