import type { CatalogTreeNodeLocator } from '../mapTypes';

/** The exact saved map identity supplied by the map owner for one selected vector row. */
export interface VectorLayerMapRequest {
  projectPath: string;
  projectXml: string | null;
  projectXmlSHA: string | null;
  mapID: string;
  mapIndex: number;
  mapName: string;
  mapType: string;
  expectedVisualizationRevision: string | null;
  projectSessionID: string;
  coreLayerContext?: unknown;
  externalCoreLayerReferences?: unknown[] | null;
  resultSelectionOverrides?: unknown[] | null;
  computeReadLease?: unknown | null;
}

export interface VectorLayerSourceFile {
  inputPath: string;
  path: string;
  role: string;
  required: boolean;
  exists: boolean;
  revision: string | null;
}

export interface VectorLayerSourceFamily {
  family: string;
  sourcePath: string;
  revision: string;
  files: VectorLayerSourceFile[];
}

/** A single persisted vector node, never a local/drawn layer or a guessed path. */
export interface VectorLayerSource {
  key: string;
  map: VectorLayerMapRequest;
  node: CatalogTreeNodeLocator;
  layerID: string;
  expectedProjectRevision: string;
  expectedFamily: VectorLayerSourceFamily | null;
  /** Source SHA; GDB uses its manifest revision, other families use the primary `source` file SHA. */
  sourceRevision: string;
}

export interface VectorStyleValues {
  penColor: string;
  penWidth: number;
  fillColor: string;
  pointSize: number;
  haloColor: string;
  haloThickness: number;
  textEmphasis: number;
}

export type VectorStyleProperty = keyof VectorStyleValues;

export interface VectorSymbologyDefinition {
  definitionID: string;
  name: string;
  layerName: string;
  vectorType: string;
  editableProperties: VectorStyleProperty[];
  defaultValues: VectorStyleValues;
  globalValues: VectorStyleValues;
}

/** Mirrors the retained Host renderer payload; `sourceOverrides` are read-only here. */
export interface MapVectorPlotStyle {
  definitionID: string;
  base: VectorStyleValues;
  sourceOverrides: Array<{ sourceFeatureIndex: number; values: VectorStyleValues }>;
  isProjectOverride: boolean | null;
}

export interface VectorPlotOptionChoice {
  value: string;
  label: string;
}

export interface VectorPlotOptionDescriptor {
  propertyName: string;
  name: string;
  label: string;
  description: string;
  kind: 'bool' | 'enum' | string;
  value: string;
  enumValues: VectorPlotOptionChoice[];
  isEnabled: boolean;
  enabledIf: string | null;
}

export interface VectorLayerSymbologySnapshot {
  mapID: string;
  layerID: string;
  node: CatalogTreeNodeLocator;
  projectPath: string;
  projectXmlSHA: string | null;
  projectRevision: string;
  projectSessionID: string;
  sourcePath: string;
  sourceRevision: string;
  visualizationRevision: string | null;
  styleSHA256: string;
  globalSymbologyRevision: string;
  definitions: VectorSymbologyDefinition[];
  styles: MapVectorPlotStyle[];
  plotOptions: VectorPlotOptionDescriptor[];
  projectConditions: VectorProjectCondition[];
  hasUnsupportedProjectConditions: boolean;
  hasUnknownProjectSymbologyContent: boolean;
}

export interface VectorProjectCondition {
  definitionID: string;
  property: string;
  conditions: Array<{
    column: string;
    operator: string;
    threshold: string;
    resultValue: string;
  }>;
}

export interface VectorLayerSymbologySaveRequest {
  map: VectorLayerMapRequest;
  node: CatalogTreeNodeLocator;
  layerID: string;
  expectedProjectRevision: string;
  expectedFamily: VectorLayerSourceFamily | null;
  sourceRevision: string;
  expectedStyleSHA256: string;
  expectedGlobalSymbologyRevision: string;
  styles: MapVectorPlotStyle[];
}

export type VectorLayerSymbologySaveReceipt = VectorLayerSymbologySnapshot;

export interface VectorRenderViewport {
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
  pixelWidth: number;
  pixelHeight: number;
}

export interface VectorRenderPreviewDraft {
  styles: MapVectorPlotStyle[];
  plotOptions: Record<string, string>;
}

export interface VectorLayerStyleEditorProps {
  backend: import('../../../shared/workbench').BackendGateway;
  language: import('../../../shared/workbench').Language;
  sourceCapture: VectorLayerSource | null;
  viewport: VectorRenderViewport | null;
  onReceipt: (source: VectorLayerSource, receipt: VectorLayerSymbologySaveReceipt) => boolean;
  onPreviewChange: (source: VectorLayerSource, draft: VectorRenderPreviewDraft | null) => void;
  onCommitStateChange?: (busy: boolean) => void;
  onDirtyStateChange?: (owner: string, dirty: boolean) => void;
  backendRevision?: number;
  settingsEpoch?: number;
  hasUnsavedDrafts?: boolean;
  dirtyOwners?: ReadonlySet<string>;
}
