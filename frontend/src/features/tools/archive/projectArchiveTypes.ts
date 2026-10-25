import type { BackendGateway, WorkbenchContext } from '../../../shared/workbench';

export interface ProjectArchiveRequest {
  projectPath: string;
  includeResults: boolean;
  expectedProjectSHA256: string;
}
export interface ProjectArchiveSource {
  context: WorkbenchContext;
  identity: string;
  backend: BackendGateway;
  backendRevision: number;
}
export interface ProjectArchiveReceipt {
  state: 'completed' | 'cancelled';
  cleanupToken: string;
  archivePath: string;
  projectPath: string;
  sourceProjectSHA256: string;
  projectSHA256: string;
  inventorySHA256: string;
  entryCount: number;
  includeResults: boolean;
}
export interface ProjectArchiveProgress { fraction?: number; message: string; taskName: string }
export interface ProjectArchiveRun {
  localID: string;
  operationID: string | null;
  source: ProjectArchiveSource;
  request: ProjectArchiveRequest;
  phase: 'running' | 'unknown' | 'unverified' | 'terminal';
  tracking: boolean;
  cancelRequested: boolean;
  transport: string | null;
  progress: ProjectArchiveProgress | null;
  receipt: ProjectArchiveReceipt | null;
  rawReceipt?: unknown;
  error?: unknown;
  cleanup: 'retained' | 'cleaning' | 'failed' | 'cleaned';
  cleanupError?: unknown;
}
