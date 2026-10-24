export interface ResultModelIdentity {
  name: string;
  description: string | null;
  shortID: string | null;
  filename: string | null;
  dateCreated: string | null;
  dateLastModified: string | null;
  embeddedInResult: boolean;
}

export interface ResultInspectorSnapshot {
  resultPath: string;
  revision: string;
  name: string;
  hasComputed: boolean;
  needsMigration: boolean;
  profileCount: number;
  computeSummary: null | {
    startTime: string;
    endTime: string;
    success: boolean;
    failureReason: string | null;
    solver: string | null;
    softwareVersion: string | null;
    totalDuration: string;
    totalDurationSeconds: number;
    solverInitDuration: string;
    solverInitDurationSeconds: number;
    outputDuration: string;
    outputDurationSeconds: number;
    computeLog: string | null;
  };
  plan: ResultModelIdentity | null;
  geometry: ResultModelIdentity | null;
  boundaryCondition: ResultModelIdentity | null;
  exactTerrainName: string | null;
  associatedTerrainName: string | null;
  overrideTerrainName: string | null;
  showAutoDetectedTerrain: boolean;
  terrainChoices: string[];
  outputBlocks: Array<{ name: string; profileCount: number; profileNames: string[]; times: string[] }>;
  layerErrors: Array<{
    code: string;
    severity: string;
    evaluatedMessage: string;
    logData: Array<{ key: string; value: string | null }>;
  }>;
}

export interface ResultPlanPropertyItem {
  label: string;
  value: string;
  isLocalizedValue: boolean;
}

export interface ResultPlanPropertyGroup {
  title: string;
  items: ResultPlanPropertyItem[];
}

export interface ResultPlanCandidate {
  resultPath: string;
  revision: string;
  name: string;
  planName: string | null;
  hasPlan: boolean;
}

export interface ResultPlanCandidateGroup {
  planName: string | null;
  hasPlan: boolean;
  results: ResultPlanCandidate[];
}

export interface ResultPlanContextSnapshot {
  projectPath: string;
  projectRevision: string;
  resultPath: string;
  revision: string;
  anchorName: string;
  anchorPlanName: string | null;
  anchorHasPlan: boolean;
  anchor?: ResultPlanCandidate;
  embeddedPlanGroups: ResultPlanPropertyGroup[];
  candidateGroups: ResultPlanCandidateGroup[];
}

export interface ResultPlanAssociationsSnapshot {
  projectPath: string;
  projectRevision: string;
  resultPath: string;
  revision: string;
  hasPlan: boolean;
  planName: string | null;
  geometryName: string | null;
  boundaryConditionName: string | null;
}

export interface ResultPlanEmbeddedImage {
  id: string;
  name: string;
  description: string | null;
  dateLastModified: string | null;
  x: number;
  y: number;
  byteCount: number;
  /** System.Text.Json encodes the original byte[] payload as base64. */
  data: string;
}

export interface ResultPlanSettingsSnapshot {
  projectPath: string;
  projectRevision: string;
  resultPath: string;
  revision: string;
  resultName: string;
  hasPlan: boolean;
  planName: string | null;
  planDescription: string | null;
  geometryName: string | null;
  boundaryConditionName: string | null;
  embeddedPlanGroups: ResultPlanPropertyGroup[];
  images: ResultPlanEmbeddedImage[];
}

export interface ResultPlanSettingsSaveReceipt {
  resultPath: string;
  previousRevision: string;
  revision: string;
  planName: string;
  planDescription: string;
  snapshot: ResultPlanSettingsSnapshot;
}
