import type { FeaturePageProps } from '../../../shared/workbench';

export type SessionHdfKind = 'plan' | 'result' | 'terrain';

export interface WorkspaceFileCapture {
  sourcePath: string;
  canonicalPath: string;
  revision: string;
  length: number;
}

export interface SessionHdfReferenceCapture {
  referenceID: string;
  projectSessionID: string;
  projectPath: string;
  kind: SessionHdfKind;
  sourcePath: string;
  canonicalSourcePath: string;
  sourceRevision: string;
  name: string;
  resultPlanName: string | null;
  sessionOnly: boolean;
}

export interface SessionHdfInspection {
  projectPath: string;
  projectSessionID: string;
  projectRevision: string;
  projectXmlRevision: string;
  sourcePath: string;
  canonicalSourcePath: string;
  sourceRevision: string;
  kind: SessionHdfKind;
  name: string;
  resultPlanName: string | null;
  description: string;
  fileSize: number;
  sessionOnly: boolean;
  reference: SessionHdfReferenceCapture;
}

export interface SessionHdfMapDocument {
  mapIndex: number;
  mapName: string;
  mapType: string;
}

export interface SessionHdfMapCatalog {
  projectRevision: string;
  visualizationRevision: string | null;
  maps: SessionHdfMapDocument[];
  activeMapIndex: number | null;
}

export interface SessionHdfMapTreeReceipt {
  roots: unknown[];
  previousVisualizationRevision: string;
  visualizationRevision: string;
  expandedNodeIDs: string[];
  selectedNodeID: string | null;
  hiddenLayerIDs: string[];
}

export interface SessionHdfComposedContextReceipt {
  projectPath: string;
  projectSessionID: string;
  projectRevision: string;
  previousContextIdentityToken: string | null;
  nextContext: Record<string, unknown>;
}

export type SessionHdfMutationBegin = NonNullable<FeaturePageProps['onBeginSessionReferenceMutation']>;
export type SessionHdfMutationCommit = (
  sourceContext: FeaturePageProps['context'],
  nextCoreLayerContext: Record<string, unknown>,
  leaseID: string,
) => boolean;
export type SessionHdfMutationEnd = NonNullable<FeaturePageProps['onEndSessionReferenceMutation']>;

export type SessionHdfImportControlProps = Pick<FeaturePageProps,
  'language' | 'backend' | 'context' | 'backendRevision' | 'hasUnsavedDrafts' | 'dirtyOwners' |
  'onBeginSessionReferenceMutation' | 'onCommitSessionReferences' | 'onEndSessionReferenceMutation'>;
