export type AssociationTargetKind = 'plan' | 'geometry';
export type AssociationOptionKind = 'geometry' | 'boundaryCondition' | 'terrain' | 'nValue';

/** An existing, project-owned model file and its captured content identity. */
export interface AssociationOption {
  name: string;
  path: string;
  revision: string;
  sha256: string;
  exists: boolean;
}

export interface AssociationState {
  geometry?: AssociationOption | null;
  boundaryCondition?: AssociationOption | null;
  terrain?: AssociationOption | null;
  nValue?: AssociationOption | null;
}

export interface AssociationSourceDependency {
  kind: string;
  name: string;
  path: string;
  canonicalPath: string;
  revision: string | null;
  sha256: string | null;
  exists: boolean;
}

export interface AssociationTargetIdentity {
  kind: AssociationTargetKind;
  name: string;
  path: string;
  revision: string;
  sha256: string;
}

export interface ProjectAssociationInspection {
  canCommit: boolean;
  validationMessage?: string | null;
  targetKind: AssociationTargetKind;
  projectPath: string;
  projectRevision: string;
  projectXmlSHA: string;
  projectSessionID: string;
  target: AssociationTargetIdentity;
  before: AssociationState;
  associationSHA256: string;
  options: Record<AssociationOptionKind, AssociationOption[]>;
  optionsSHA256: string;
  dependencies: AssociationSourceDependency[];
  sourceReferencesSHA256: string;
}

export interface AssociationProjectSnapshot {
  success: boolean;
  projectPath: string;
  projectXml: string;
  revision: string;
  projectXmlSHA: string;
  projectSessionID: string;
  coreLayerContext?: Record<string, unknown> | null;
}

export interface AssociationCommitReceipt {
  success: boolean;
  targetKind: AssociationTargetKind;
  project: AssociationProjectSnapshot;
  previousProjectRevision: string;
  target: AssociationTargetIdentity;
  before: AssociationState;
  after: AssociationState;
  sourceCapture: {
    dependencies: AssociationSourceDependency[];
    sourceReferencesSHA256: string;
  };
  previousSourceReferencesSHA256: string;
  sourceReferencesSHA256: string;
}
