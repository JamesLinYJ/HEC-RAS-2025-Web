import type { AssetReference, Language } from '../../shared/workbench';
import { editorPageTitle } from './editorLocalization';

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
  grids?: Record<string, unknown> | null;
  manning?: { classifications: Array<{ id: string; title: string }>; rows: ModelRow[]; warning?: string | null } | null;
  classification?: {
    inputs: Array<{ id: string; scope: string; name: string; type: string; canRemove: boolean; filename?: string | null; tableId?: string | null; rows: ModelRow[]; polygons: Array<{ id: string; scope: string; classification: string; vertexCount: number; vertices: Array<{ x: number; y: number; z?: string | null; m?: string | null }>; hasZ: boolean; hasM: boolean }>; extent?: string | null }>;
    selectedId?: string | null;
    classes: string[];
    canUndoGeometry?: boolean;
    canRedoGeometry?: boolean;
  } | null;
  terrain?: {
    inputs: Array<{ id: string; scope: string; name: string; type: string; filename?: string | null; schema?: string | null; error?: string | null; validExtent?: { minX: number; maxX: number; minY: number; maxY: number } | null; overviewCount?: number | null; overviews?: string[] | null }>;
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
  /** Dynamic fields returned with the terrain draft projection. */
  terrainReadOnlyFields?: string[];
  /** Selection-scoped terrain values used to distinguish navigation from edits. */
  terrainSelectionBaselineFields?: Record<string, string | null>;
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
  onTerrainTransform?: (command: TerrainTransformCommand) => Promise<boolean>;
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
  'Boundary Conditions': ['BoundaryConditionsSummary', 'BoundaryConditionCatalogSummary', 'BoundaryConditionTimeWindow', 'BoundaryConditionLineEditor', 'BoundaryConditionLinePayloadsList', 'BoundaryConditionLineQuickEditPicker', 'BoundaryConditionNode', 'BoundaryConditionPicker', 'PayloadListItem', 'ConstantPayloadEditor', 'ExternalPayloadEditor', 'RegularTimeSeriesPayloadEditor', 'RegularSpatiallyVariableTimeSeriesPayloadEditor', 'PrecipitationControl', 'EvapotranspirationControl', 'WindForcesControl', 'WindConstantControl', 'AirPressureControl', 'AirDensityControl', 'SpatialBoundaryConditionRasterImporter'],
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
