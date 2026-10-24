import type { ReactNode } from 'react';
import type { CatalogTreeNode } from '../mapTypes';

export interface MapTreeProperty {
  id: string;
  label: string;
  value: string;
}

export interface MapTreeLayerError {
  code?: string;
  severity?: string;
  evaluatedMessage?: string;
  logData?: Array<{ key?: string; value?: string | null }>;
}

/** The map tree wire payload is richer than the shared project-tree TS view. */
export type MapTreeNodeData = CatalogTreeNode & {
  catalogSummary?: {
    name?: string | null;
    filename?: string | null;
    dateCreated?: string | null;
    dateLastModified?: string | null;
    description?: string | null;
  } | null;
  backupSummary?: {
    filename?: string;
    dateCreated?: string;
    dateLastModified?: string;
    canSaveAsNewGeometry?: boolean;
    revision?: string | null;
    parentGeometryPath?: string | null;
    parentGeometryRevision?: string | null;
    projectRevision?: string | null;
  } | null;
  properties?: MapTreeProperty[] | null;
  layerErrors?: MapTreeLayerError[] | null;
  resultComputeState?: string | null;
  resultComputeFailureReason?: string | null;
  resultComputeSourcePath?: string | null;
  resultComputeSourceRevision?: string | null;
  classificationEditModeOpacity?: number | null;
};

export type MapTreeLanguage = 'zh' | 'en';

export interface NodePresentation {
  icon: string;
  kind: string;
  detail?: ReactNode;
  status?: {
    text: string;
    tone?: 'neutral' | 'success' | 'warning' | 'error' | 'busy';
  };
}

export interface NodePresentationProps {
  node: MapTreeNodeData;
  language: MapTreeLanguage;
}

export type MapTreeNodeKind =
  | 'folder' | 'geometry' | 'plan' | 'result' | 'multiResult' | 'terrain'
  | 'nValue' | 'classification' | 'boundaryCondition' | 'terrainInput'
  | 'flowArea' | 'culvertBarrel' | 'pointLayer' | 'polylineLayer' | 'polygonLayer'
  | 'generic';
