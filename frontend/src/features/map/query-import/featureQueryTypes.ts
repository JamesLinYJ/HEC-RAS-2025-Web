import type { ReactNode } from 'react';
import type { BackendGateway, Language, WorkbenchContext } from '../../../shared/workbench';
import type { MapDocumentTab } from '../mapTypes';

/** Service query kind and form fields retain the identifiers used by the original queriers. */
export type FeatureQueryKind = 'NLD' | 'NSI' | 'NFHL' | 'OSM';
export interface FeatureQueryExtent { minX: number; minY: number; maxX: number; maxY: number }
export interface FeatureQueryPolygonSource { id: string; name: string }
export interface FeatureQueryFormValue {
  nldLayerId: string;
  useBoundaryPolygon: boolean;
  polygonSourceId: string;
  rawQl: string;
  queryLabel: string;
}
export interface FeatureQueryControlProps {
  language: Language;
  disabled: boolean;
  value: FeatureQueryFormValue;
  onChange: (patch: Partial<FeatureQueryFormValue>) => void;
  polygonSources: FeatureQueryPolygonSource[];
  extent: FeatureQueryExtent | null;
  backend: BackendGateway;
  backendRevision?: number;
}

export interface FeatureQueryMapCapture {
  projectPath: string;
  projectRevision: string;
  projectXmlSHA: string | null;
  visualizationPath: string;
  visualizationRevision: string | null;
  mapID: string | null;
  mapIndex: number | null;
  mapName: string | null;
  mapType: string;
  isPersisted: boolean;
}
export interface FeatureQuerySourceFile {
  inputPath: string; path: string; role: string; required: boolean; exists: boolean; revision: string | null;
}
export interface FeatureQuerySourceFamily {
  family: string; sourcePath: string; revision: string; files: FeatureQuerySourceFile[];
}
export interface FeatureQueryExportedSource {
  path: string; revision: string; name: string; mapType: string; featureCount: number;
  lineColor: string; fillColor: string; lineWidth: number; family: FeatureQuerySourceFamily | null;
}
/** Result of the separate, explicitly initiated map mutation. */
export interface FeatureQueryMapImportReport {
  status: 'complete' | 'partial' | 'failed' | 'unknown';
  imported: string[];
  failures: { path: string; message: string }[];
  message?: string;
  operationID?: string | null;
  /** Resume the same map operation after an unknown outcome; never redispatch. */
  resume?: () => Promise<FeatureQueryMapImportReport>;
}
export interface FeatureQueryImporterProps {
  backend: BackendGateway;
  backendRevision?: number;
  context: WorkbenchContext;
  document: MapDocumentTab | null;
  extent: FeatureQueryExtent | null;
  coordinatesAreProject: boolean;
  /** An independently detected project CRS problem, distinct from canvas coordinate ownership. */
  projectionIssue?: 'local' | 'missing' | 'invalid' | 'unknown' | null;
  language: Language;
  dirtyOwner: string;
  onDirtyStateChange?: (owner: string, dirty: boolean) => void;
  onImportSources: (sourceContext: WorkbenchContext, target: MapDocumentTab,
    sources: FeatureQueryExportedSource[], isCurrent: () => boolean) => Promise<FeatureQueryMapImportReport>;
}
/** The source-specific dialogs wrap this shared workflow and supply one native form. */
export interface FeatureQueryDialogProps extends FeatureQueryImporterProps {
  kind: FeatureQueryKind;
  onClose: () => void;
  onKindChange?: (kind: FeatureQueryKind) => void;
  renderControl: (props: FeatureQueryControlProps) => ReactNode;
}
