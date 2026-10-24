import type { AssetReference, BackendGateway, Language } from '../../shared/workbench';
import { editorPageTitle } from './editorLocalization';
import type { TerrainMapProjectOwner, TerrainTopologyCommand, TerrainTopologyResponse } from './terrainTopologyTypes';
import type { CumulusImportWorkflow } from './cumulusTypes';

export interface SchemaChoice {
  id: string;
  title: string;
  debugOnly?: boolean | null;
  titleIsUserData?: boolean | null;
  description?: string | null;
}

export interface EditorFieldSchema {
  id: string;
  title: string;
  kind: string;
  sourceType: string;
  sourceControl?: string;
  readOnly?: boolean;
  allowZero?: boolean;
  choices?: SchemaChoice[];
  enabledBy?: string[];
  visibleBy?: string[];
  help?: string;
  placeholder?: string;
  scope?: string;
  minimum?: number | null;
  maximum?: number | null;
}

export interface EditorSchemaGroup {
  title: string;
  fields: string[];
}

export interface EditorSchema {
  id: string;
  title: string;
  source: string;
  fields: EditorFieldSchema[];
  groups?: EditorSchemaGroup[];
  children?: string[];
  preferredColumnWidth?: number | null;
}

export interface EditorSchemaDocument {
  sourceRepository?: string;
  schemas: EditorSchema[];
  tables?: Array<{ id: string; source: string; columns: EditorFieldSchema[] }>;
  inventory?: unknown[] | Record<string, unknown>;
}

export interface ModelImage {
  id: string;
  name: string;
  data: string;
}

export interface ModelRow {
  id: string;
  values: Record<string, string>;
}

export interface ModelSample {
  id: string;
  x: number;
  y?: number | null;
  series: string;
  datasetId?: string | null;
}

export interface ModelTable {
  columns: Array<{ id: string; title: string; sourceType: string; readOnly?: boolean; width?: number | null; autoSuggestions?: string[] }>;
  canAddRows?: boolean;
  canCopyRows?: boolean;
  canDeleteRows?: boolean;
  selectedRowIDs?: string[];
  requestedSelectionRowIDs?: string[];
}

export interface ModelSnapshot {
  revision: string;
  fields: Record<string, string>;
  images: Record<string, ModelImage[]>;
  canEdit: boolean;
  fieldGroups: Record<string, string[]>;
  availableOperations: string[];
  sourceState?: string | null;
  /** Rebased external-core identity returned with a committed Host snapshot. */
  coreLayerContext?: Record<string, unknown> | null;
  readOnlyFields?: string[] | null;
  fieldChoices?: Record<string, SchemaChoice[]>;
  fieldTitles?: Record<string, string>;
  planTimeWindowFieldIDs?: { mode: string; start: string; end: string } | null;
  boundary?: {
    lines: Array<{
      id: string;
      scope: string;
      name: string;
      payloads: Array<{ id: string; scope: string; name: string; friendlyName: string; editorSchemaId: string; isHydraulic: boolean; isEnabled: boolean; variable: string; description: string; valueUnits?: DisplayUnitData | null }>;
      activeHydraulicPayloadId?: string | null;
      newPayload?: { fields: Record<string, string>; choices: Record<string, SchemaChoice[]>; constantUnits: Record<string, string>; valueUnits?: Record<string, DisplayUnitData> | null };
      geometry?: Array<{ x: number; y: number }> | null;
    }>;
    series: Record<string, {
      pageId: string;
      scope: string;
      schemaId: string;
      tableId?: string | null;
      samples: ModelSample[];
      rows: ModelRow[];
      columns: Array<{ id: string; title: string; sourceType: string; readOnly: boolean; width: number }>;
      canEditX: boolean;
      xIsDate: boolean;
      operations: string[];
      stations: ModelRow[];
      xTitle: string;
      yTitle: string;
    }>;
    selectedLineId?: string | null;
    selectedPayloadId?: string | null;
      spatial?: Record<string, SpatialPointData> | null;
  } | null;
  grids?: Record<string, SpatialGridData> | null;
  manning?: { classifications: Array<{ id: string; title: string }>; rows: ModelRow[]; warning?: string | null } | null;
  classification?: {
    inputs: Array<{ id: string; scope: string; name: string; type: string; canRemove: boolean; filename?: string | null; tableId?: string | null; rows: ModelRow[]; polygons: Array<{ id: string; scope: string; classification: string; vertexCount: number; vertices: Array<{ x: number; y: number; z?: string | null; m?: string | null }>; hasZ: boolean; hasM: boolean }>; extent?: string | null }>;
    selectedId?: string | null;
    classes: string[];
    canUndoGeometry?: boolean;
    canRedoGeometry?: boolean;
  } | null;
  terrain?: {
    inputs: Array<{ id: string; scope: string; name: string; type: string; filename?: string | null; schema?: string | null; error?: string | null; validExtent?: { minX: number; maxX: number; minY: number; maxY: number } | null; overviewCount?: number | null; overviews?: string[] | null; sourceVectorIndex?: number | null }>;
    selectedId?: string | null;
    rows?: Record<string, ModelRow[]> | null;
    tables?: Record<string, ModelTable> | null;
    fieldChoices?: Record<string, SchemaChoice[]> | null;
    textSuggestions?: Record<string, string[]> | null;
    importDraftToken?: string | null;
    importCleanupWarning?: string | null;
    importCleanupToken?: string | null;
  } | null;
}

export interface DisplayUnitData {
  sourceId: string;
  selectionId: string;
  choices: Array<{ id: string; scale: number; offset: number; unitTypeId?: string | null; name?: string | null }>;
  sourceUnitTypeId?: string | null;
}

export interface SpatialPointData {
  stations: Array<{ id: string; scope: string; name: string; x: number; y: number }>;
  inputs: Array<{ id: string; scope: string; stationId?: string | null; pageId?: string | null }>;
  unit: string;
  isVector: boolean;
  valueUnits: DisplayUnitData;
  heightUnits: DisplayUnitData;
  intervalUnits: DisplayUnitData;
  defaultHeight: number;
}

export interface SpatialGridPreview {
  width: number;
  height: number;
  /** System.Text.Json serializes the C# byte[] as Base64. */
  rgba: string;
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
  minimum?: number | null;
  maximum?: number | null;
}

export interface ClassificationMapData {
  raster: SpatialGridPreview | null;
  legend: Array<{ id: number; name: string; color: string }>;
  selectedRaster?: SpatialGridPreview | null;
  selectedInputId?: string | null;
  projection?: string | null;
}

export interface ClassificationImportFile {
  file: string;
  revision: string;
  kind: 'Raster' | 'Polygon';
  column?: string | null;
  name?: string | null;
}

export interface ClassificationImportItem {
  selection: ClassificationImportFile;
  columns: string[];
  featureCount: number;
  previewFeatureCount: number;
  geoJson?: string | null;
  raster?: SpatialGridPreview | null;
  rows?: number;
  columnsCount?: number;
  bands?: number;
}

export interface ClassificationImportInspection {
  items: ClassificationImportItem[];
  projection: string;
}

/** Host-backed source inspection followed by an immediate, receipt-checked import save. */
export interface ClassificationImportWorkflow {
  owner: string;
  canInspect: boolean;
  canImport: boolean;
  blockedMessage?: string;
  inspect: (files: string[], signal: AbortSignal) => Promise<{ revision: string; inspection: ClassificationImportInspection }>;
  commit: (revision: string, files: ClassificationImportFile[], signal: AbortSignal) => Promise<ModelSnapshot | null>;
}

export interface NValuePreviewData {
  revision: string;
  projectRevision: string;
  mode: string;
  raster: SpatialGridPreview | null;
  legendStops: Array<{ value: number; red: number; green: number; blue: number; alpha: number }>;
  histogram: { minimum: number; maximum: number; binWidth: number; binArea: number[] } | null;
}

export interface ClassificationGeometryMutation {
  operation: 'create' | 'replace' | 'delete' | 'undo' | 'redo' | 'paste';
  inputId?: string | null;
  polygonIds?: string[] | null;
  vertices?: Array<{ x: number; y: number; z?: string | null; m?: string | null }> | null;
  hasZ?: boolean;
  hasM?: boolean;
  classification?: string | null;
  features?: Array<{ vertices: Array<{ x: number; y: number; z?: string | null; m?: string | null }>; hasZ: boolean; hasM: boolean; classification: string }>| null;
}

export interface ClassificationTransformCommand {
  operation: 'remove' | 'reorder' | 'geometry';
  itemId?: string;
  from?: number;
  to?: number;
  geometry?: ClassificationGeometryMutation;
}

export type ManningTransformOperation = 'refresh' | 'setNLCD';

export interface SpatialGridData {
  layerId: string;
  times: string[];
  components: string[];
  units: string;
  projection: string;
  rows: number;
  columns: number;
  selectedTimeId?: string | null;
  selectedComponent?: string | null;
  preview?: SpatialGridPreview | null;
  error?: string | null;
}

export interface SpatialGridBand {
  id: string;
  file: string;
  dataset: string;
  band: number;
  revision: string;
  time?: string | null;
  issueTime?: string | null;
  units: string;
  dataType: string;
  included: boolean;
  component: string;
}

export interface SpatialGridInspection {
  bands: SpatialGridBand[];
  subdatasets: string[];
  preview?: SpatialGridPreview | null;
  unitOptions: string[];
  units: string;
  dataType: string;
  dssGridSources?: Array<{ id: string; groupPath: string; title: string; times: string[]; recordPaths: string[]; units: string; dataType: string; projection: string; rows: number; columns: number; cellSize: number; left: number; bottom: number; right: number; top: number }> | null;
  dssGridDraftIdentity?: string | null;
  dssGridSourcePath?: string | null;
  dssGridRevision?: string | null;
}

export interface SpatialGridSelection {
  layerId: string;
  bands: SpatialGridBand[];
  units: string;
  dataType: string;
  timeBehavior: 'ProjectBackwards' | 'ProjectForwards';
}

export interface BoundaryResultCatalogItem {
  id: string;
  name: string;
  path: string;
}

export interface BoundaryInitialConditionsInspection {
  assetPath: string;
  assetRevision: string;
  projectPath: string;
  projectRevision: string;
  projectXmlFingerprint: string;
  resultPath: string;
  resultRevision: string | null;
  timestamp: string;
  numberCulture: string;
  canAttach: boolean;
  errorMessage: string | null;
}

export interface BoundaryInitialConditionsReceipt {
  operationID: string;
  assetPath: string;
  projectPath: string;
  resultPath: string;
  resultRevision: string;
  projectRevision: string;
  projectXml: string;
  connectionName: string;
  timestamp: string;
  model: ModelSnapshot;
  alreadyCommitted: boolean;
}

export interface BoundaryInitialConditionsStatus {
  state: string;
  receipt: BoundaryInitialConditionsReceipt | null;
  errorMessage: string | null;
}

export type BoundaryInitialConditionsOperationState =
  | 'attaching' | 'uncertain' | 'notFound' | 'prepared' | 'rolledBack' | 'conflict' | 'awaitingContext';

export interface BoundaryInitialConditionsOperationView {
  operationID: string;
  resultPath: string;
  resultRevision: string;
  state: BoundaryInitialConditionsOperationState;
  message: string;
  canRetry: boolean;
}

export interface BoundaryInitialConditionsWorkflow {
  canStart: boolean;
  busy: boolean;
  blockedByOtherOwner: boolean;
  blockedMessage?: string;
  operation?: BoundaryInitialConditionsOperationView | null;
  loadResults: (signal?: AbortSignal) => Promise<BoundaryResultCatalogItem[]>;
  inspect: (resultPath: string, timestamp: string, signal?: AbortSignal) => Promise<BoundaryInitialConditionsInspection>;
  attach: (inspection: BoundaryInitialConditionsInspection) => Promise<boolean>;
  recover: () => Promise<boolean>;
  retry: () => Promise<boolean>;
}

export type SpatialTransformOperation = 'addStation' | 'removeStation' | 'addGauge' | 'removeGauge';

export interface ModelDraft {
  /** Boundary editor source identity returned by a backend draft transform. */
  sourceState: string | null;
  /** Current uncommitted boundary projection returned by model.boundaryTransform. */
  boundaryView?: ModelSnapshot['boundary'];
  /** Field alias groups from the same draft projection, including newly created series. */
  boundaryFieldGroups?: ModelSnapshot['fieldGroups'];
  /** Read-only dynamic fields from that projection, used when building later draft requests. */
  boundaryReadOnlyFields?: string[];
  /** Current uncommitted terrain projection returned by model.terrainTransform. */
  terrainView?: ModelSnapshot['terrain'];
  /** Current uncommitted classification projection returned by model.classificationTransform. */
  classificationView?: ModelSnapshot['classification'];
  /** Current uncommitted Manning projection returned by model.manningTransform. */
  manningView?: ModelSnapshot['manning'];
  /** Dynamic fields returned with the terrain draft projection. */
  terrainReadOnlyFields?: string[];
  /** Selection-scoped terrain values used to distinguish navigation from edits. */
  terrainSelectionBaselineFields?: Record<string, string | null>;
  /** Classification input navigation fields do not dirty the source model by themselves. */
  classificationSelectionBaselineFields?: Record<string, string | null>;
  fields: Record<string, string>;
  images: Record<string, ModelImage[]>;
  rows: Record<string, ModelRow[]>;
  samples: Record<string, ModelSample[]>;
  sampleText: Record<string, Record<string, { x: string; y: string }>>;
  changedCells: Record<string, Array<{ rowID: string; columnID: string }>>;
}

export type SeriesTransformOperation = 'appendRow' | 'removeLastRow' | 'clear' | 'interpolate' | 'addStation';
export type BoundaryPayloadTransformOperation = 'create' | 'remove';
export type TerrainTransformOperation = 'add' | 'reorder' | 'remove' | 'selectInput' | 'addRow' | 'copyRows' | 'removeRows' | 'forceSetLeveeProperty' | 'selectRows';

export interface TerrainTransformCommand {
  operation: TerrainTransformOperation;
  itemId?: string | null;
  from?: number;
  to?: number;
  inputKind?: 'functionalSurface' | 'polygonVectorMod' | 'channelVectorMod' | 'simpleCutFill';
  hintExtent?: { minX: number; maxX: number; minY: number; maxY: number };
  tableId?: string;
  rowIDs?: string[];
  leveeForceSet?: { command: 'forceSetMode' | 'forceSetModType'; value: string; rowIDs: string[] };
}

export interface EditorPageProps {
  backend?: BackendGateway;
  canEditTerrainTopology?: boolean;
  terrainMapOwner?: TerrainMapProjectOwner;
  onTerrainTopology?: (command: TerrainTopologyCommand) => Promise<TerrainTopologyResponse | null>;
  language: Language;
  asset: AssetReference;
  snapshot: ModelSnapshot;
  draft: ModelDraft;
  schemas: EditorSchema[];
  tableSchemas?: Array<{ id: string; source: string; columns: Array<{ id: string; title: string; sourceType: string; readOnly?: boolean }> }>;
  pageId: string;
  onPageChange: (pageId: string) => void;
  onFieldChange: (fieldID: string, value: string) => void;
  onImagesChange: (fieldID: string, images: ModelImage[]) => void;
  onRowCellChange: (tableID: string, rowID: string, columnID: string, value: string) => void;
  onSampleChange?: (pageID: string, sampleID: string, column: 'x' | 'y', value: string) => void;
  canTransformSeries?: boolean;
  onSeriesTransform?: (pageID: string, operation: SeriesTransformOperation) => Promise<void>;
  onBoundaryTransform?: (lineID: string, operation: BoundaryPayloadTransformOperation, payloadID: string | null, fields: Record<string, string>) => Promise<boolean>;
  boundaryInitialConditions?: BoundaryInitialConditionsWorkflow;
  canTransformSpatial?: boolean;
  onSpatialTransform?: (layerID: string, operation: SpatialTransformOperation, itemID: string | null) => Promise<boolean>;
  canImportGrid?: boolean;
  onGridInspect?: (layerID: string, files: string[], subdataset?: string, bands?: SpatialGridBand[], previewID?: string, signal?: AbortSignal) => Promise<SpatialGridInspection>;
  onGridFrame?: (layerID: string, timeID?: string, component?: string, signal?: AbortSignal) => Promise<SpatialGridData>;
  onGridImport?: (selection: SpatialGridSelection) => Promise<boolean>;
  /** Real Host-backed Cumulus discovery, coverage and immediate-save workflow. */
  cumulusImport?: CumulusImportWorkflow;
  onTerrainTransform?: (command: TerrainTransformCommand) => Promise<boolean>;
  onClassificationTransform?: (command: ClassificationTransformCommand) => Promise<boolean>;
  onClassificationMap?: (signal?: AbortSignal) => Promise<ClassificationMapData>;
  classificationImport?: ClassificationImportWorkflow;
  onManningTransform?: (operation: ManningTransformOperation) => Promise<boolean>;
  canPreviewNValue?: boolean;
  onNValuePreview?: (signal?: AbortSignal) => Promise<NValuePreviewData>;
}

/** Include read-only metadata attached to a transformed boundary draft projection. */
export function isEditorFieldReadOnly(props: Pick<EditorPageProps, 'snapshot' | 'draft'>, fieldID: string): boolean {
  return props.snapshot.readOnlyFields?.includes(fieldID) === true
    || props.draft.boundaryReadOnlyFields?.includes(fieldID) === true;
}

export const SCHEMAS_BY_KIND: Record<string, string[]> = {
  Plan: ['PlanEditorControl', 'PlanSettingsPage', 'PlanComputeSettingsPage', 'PlanCatalogSummary'],
  Geometry: ['GeometryCatalogSummary', 'GeometrySummaryPage'],
  Terrain: ['TerrainCatalogSummary', 'TerrainProperties', 'TerrainLayersEditor', 'RasterTerrainInputProperties', 'FunctionalTerrainInputProperties', 'ChannelModTerrainInputProperties'],
  'Boundary Conditions': ['BoundaryConditionsSummary', 'BoundaryConditionCatalogSummary', 'BoundaryConditionTimeWindow', 'BoundaryConditionLineEditor', 'BoundaryConditionLinePayloadsList', 'BoundaryConditionLineQuickEditPicker', 'BoundaryConditionNode', 'BoundaryConditionPicker', 'PayloadListItem', 'ConstantPayloadEditor', 'ExternalPayloadEditor', 'RegularTimeSeriesPayloadEditor', 'RegularSpatiallyVariableTimeSeriesPayloadEditor', 'PrecipitationControl', 'EvapotranspirationControl', 'WindForcesControl', 'WindConstantControl', 'AirPressureControl', 'AirDensityControl', 'SpatialBoundaryConditionRasterImporter', 'SpatialDataSummaryPage'],
  'Surface Layers': ['ClassificationCatalogSummary', 'ClassificationLayerSummary', 'ClassificationLayersEditor', 'NValueCatalogSummary', 'ManningsNEditor', 'ConstantManningsNEditor', 'ClassificationLayerManningNEditor'],
  Classification: ['ClassificationCatalogSummary', 'ClassificationLayerSummary', 'ClassificationLayersEditor'],
  NValues: ['NValueCatalogSummary', 'ManningsNEditor', 'ConstantManningsNEditor', 'ClassificationLayerManningNEditor'],
};

export function pageTitle(pageID: string, language: Language): string {
  return editorPageTitle(pageID, language);
}

export function schemaForPage(schemas: EditorSchema[], pageID: string): EditorSchema | undefined {
  return schemas.find((schema) => schema.id === pageID);
}

export function snapshotRows(snapshot: ModelSnapshot): Record<string, ModelRow[]> {
  const rows: Record<string, ModelRow[]> = { ...(snapshot.terrain?.rows ?? {}) };
  if (snapshot.manning?.rows) rows['ClassificationLayerManningNEditor/InterpretationTable'] = snapshot.manning.rows.map((row) => ({ id: row.id, values: { ...row.values } }));
  for (const series of Object.values(snapshot.boundary?.series ?? {})) {
    if (series.tableId) rows[series.tableId] = series.rows.map((row) => ({ id: row.id, values: { ...row.values } }));
    if (series.tableId) rows[`${series.pageId}/Stations`] = series.stations.map((row) => ({ id: row.id, values: { ...row.values } }));
  }
  for (const input of snapshot.classification?.inputs ?? []) {
    if (input.tableId) rows[input.tableId] = input.rows.map((row) => ({ id: row.id, values: { ...row.values } }));
  }
  return rows;
}

export function schemaForAsset(asset: AssetReference | null): string[] {
  if (!asset) return [];
  return SCHEMAS_BY_KIND[asset.kind] ?? [];
}
