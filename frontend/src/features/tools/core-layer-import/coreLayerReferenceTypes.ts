import type { WorkbenchContext } from '../../../shared/workbench';

/** Host DTOs for the session-only Geometry/Boundary Condition reference flow. */
export interface CoreLayerReferenceCapture {
  referenceID: string;
  projectSessionID: string;
  projectPath: string;
  kind: 'geometry' | 'boundaryCondition';
  sourcePath: string;
  canonicalSourcePath: string;
  sourceRevision: string;
  name: string;
  sessionOnly: boolean;
}

export interface CoreLayerRequestContext {
  projectSessionID: string;
  projectPath: string;
  projectRevision: string | null;
  references: CoreLayerReferenceCapture[];
  identityToken: string;
  sessionHdfReferences?: unknown[];
}

export interface WorkspaceReferencesComposeRequest {
  projectPath: string;
  projectSessionID: string;
  projectRevision: string;
  projectXml: string;
  projectXmlRevision: string;
  coreLayerContext?: CoreLayerRequestContext;
  addCoreReferences: CoreLayerReferenceCapture[];
}

export interface WorkspaceReferencesComposeReceipt {
  projectPath: string;
  projectSessionID: string;
  projectRevision: string;
  previousContextIdentityToken: string | null;
  nextContext: CoreLayerRequestContext;
}

export interface SessionReferenceMutationCallbacks {
  onBeginSessionReferenceMutation?: (sourceContext: WorkbenchContext) => string | null;
  onCommitSessionReferences?: (
    sourceContext: WorkbenchContext,
    nextCoreLayerContext: Record<string, unknown>,
    leaseID: string,
  ) => boolean;
  onEndSessionReferenceMutation?: (leaseID: string) => void;
}

export interface CoreLayerMapChoice {
  mapIndex: number;
  mapName: string;
  mapType: string;
}
