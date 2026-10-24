export type GeometryFeatureKind = 'node' | 'arc' | 'region' | 'flowArea' | 'barrel' | 'barrelProperties' | 'openingProperties';

export interface GeometryTopologyFactorSelection {
  featureKind: 'arc' | 'region';
  featureIDs: number[];
}

export interface GeometryColumn {
  id: string;
  title: string;
  sourceType: string;
  readOnly?: boolean;
  isTag?: boolean;
  isUserDefined?: boolean;
}

export interface GeometryField {
  id: string;
  title: string;
  sourceType: string;
  choices?: string[];
  readOnly?: boolean;
  minimum?: number;
  maximum?: number;
  forceSetCommand?: string;
  showClearButton?: boolean;
  clearCommand?: string;
  errorValidationSource?: string;
  errorValidationPropertyName?: string;
}

export interface GeometryRow {
  id: string;
  fid?: number;
  values: Record<string, string>;
  x?: number | null;
  y?: number | null;
  startNodeID?: number | null;
  endNodeID?: number | null;
  polyline?: Array<{ x: number; y: number; z?: string | null; m?: string | null }>;
  errors?: Record<string, string[]>;
}

export interface GeometryTable {
  featureKind: string;
  columns: GeometryColumn[];
  editableFields: GeometryField[];
  rows: GeometryRow[];
}

export interface GeometryTopologySnapshot {
  revision: string;
  draftState: string;
  canEdit: boolean;
  isDirty: boolean;
  arcs: GeometryTable;
  regions: GeometryTable;
  nodes: GeometryTable;
  arcTags?: Array<{ id: string; type: string }>;
  regionTags?: Array<{ id: string; type: string }>;
  recentArcTags?: Array<{ name: string; type: string; value: string }>;
  recentRegionTags?: Array<{ name: string; type: string; value: string }>;
  completion?: {
    sourceRevision: string;
    draftIdentity: string;
    isComplete: boolean;
    messages: GeometryTopologyCompletionMessage[];
  } | null;
  canUndo?: boolean;
  canRedo?: boolean;
}

export interface GeometryTopologyCompletionMessage {
  id: string;
  code: string;
  severity: string;
  regionCompletionSeverity?: string | null;
  text: string;
  logData?: Array<{ key: string; value: string; valueType: string; integerValue?: number | null }>;
  targets?: Array<{ featureKind: string; fid: number; tableRowID: string; mapFeatureIDs: string[] }>;
}

export interface GeometryTopologyArcPointsSnapshot {
  revision: string;
  draftIdentity: string;
  arcFID: number;
  rows: Array<{
    rowIndex: number;
    pointKind: 'start' | 'internal' | 'end' | string;
    internalPointIndex?: number | null;
    endpointNodeIndex?: number | null;
    x: number;
    y: number;
  }>;
}

export interface GeometryTopologyRecipeSnapshot {
  revision: string;
  draftIdentity: string;
  controlPointFIDs: number[];
  hasSelection: boolean;
  hasMismatch: boolean;
  commonRecipe?: string | null;
  rawText: string;
  isValid: boolean;
  parseIssue?: string | null;
  steps: Array<{ name: string; parameters: Record<string, string> }>;
  stepSchema: Array<{
    name: string;
    aliases: string[];
    parameters: Array<{ name: string; sourceType: string; defaultValue: string }>;
  }>;
  selectedRecipes: Array<{ fid: number; recipe: string }>;
  selectedPatchTypes?: string[] | null;
}

export type GeometryTopologyFilterOperator = 'Equals' | 'NotEquals' | 'Contains' | 'DoesNotContain'
  | 'GreaterThan' | 'AtLeast' | 'LessThan' | 'AtMost' | 'IsBlank' | 'IsNotBlank';

export interface GeometryTopologyFilterClause {
  type: 'Tag' | 'Property';
  tagDefinitionID?: string | null;
  propertyColumnID?: string | null;
  operator: GeometryTopologyFilterOperator;
  value?: string | null;
}

export interface GeometryTopologyPropertyDescriptor {
  name: string;
  columnName: string;
  sourceType: string;
  isEnum?: boolean;
}

export interface GeometryTopologyFilterSnapshot {
  revision: string;
  draftIdentity: string;
  featureKind: 'arc' | 'region';
  matchedFeatureIDs: number[];
  propertyDescriptors: GeometryTopologyPropertyDescriptor[];
  tagDefinitions: Array<{ id: string; type: string }>;
}

export interface GeometryMeshFlowArea {
  id: string;
  rowIndex: number;
  name: string;
  targetedCellSpacing?: number | null;
  hasMesh: boolean;
  cellCount: number;
  faceCount: number;
  nodeCount: number;
  hasMeshErrors: boolean;
  hasPropertyTables: boolean;
}

export interface GeometryMeshSnapshot {
  revision: string;
  draftState: string;
  topologyFingerprint: string;
  canEdit: boolean;
  isDirty: boolean;
  flowAreas: GeometryMeshFlowArea[];
  regions: GeometryTable;
  selectedRegionIDs: number[];
  patchSizing?: {
    state: string;
    kind?: string | null;
    message: string;
    selectedRegionIDs: number[];
    dimensions: Array<{
      id: string;
      title: string;
      arcCount: number;
      sizeFunction?: string | null;
      sizeFunctionHasMismatch: boolean;
      liveValue?: number | null;
      liveValueHasMismatch: boolean;
      canResize: boolean;
      canApply: boolean;
      canChangeMode: boolean;
    }>;
  } | null;
  generationReport?: { succeeded: boolean; partial: boolean; meshWasReplaced: boolean; cellCount?: number | null; messages: string[] } | null;
  prepareSummary?: { succeeded?: boolean | null; failureReason?: string | null; softwareVersion?: string | null; startTime?: string | null; totalDurationSeconds?: number | null; prepareLog?: string | null } | null;
  preparePending: boolean;
}

export interface GeometryMeshDegreeScan {
  maxFacesPerNode: number;
  issues: Array<{
    id: string;
    flowAreaID: string;
    flowAreaName: string;
    meshIndex: number;
    nodeIndex: number;
    faceCount: number;
    x?: number | null;
    y?: number | null;
    cellsExtent?: { minX: number; minY: number; maxX: number; maxY: number } | null;
    cells: Array<{
      cellIndex: number;
      coordinates: Array<{ x: number; y: number }>;
    }>;
  }>;
}

export interface GeometryCulvertSnapshot {
  revision: string;
  draftState: string;
  canEdit: boolean;
  isDirty: boolean;
  barrels: GeometryTable;
  barrelProperties: GeometryTable;
  openingProperties: GeometryTable;
  associatedTerrainName?: string | null;
  dependencyRevision?: string | null;
}

export interface GeometryParentSnapshot {
  revision: string;
  sourceState?: string | null;
  fields: Record<string, string>;
  images: Record<string, Array<{ id: string; name: string; data?: string | number[] }>>;
  canEdit: boolean;
  fieldTitles?: Record<string, string>;
}

export interface GeometryModelRequest {
  assetPath: string;
  kind: string;
  projectPath: string | null;
  projectXml?: string | null;
  coreLayerContext?: Record<string, unknown>;
}

export function asObject(value: unknown): Record<string, unknown> {
  return typeof value === 'object' && value !== null ? value as Record<string, unknown> : {};
}

export function asArray<T>(value: unknown): T[] {
  return Array.isArray(value) ? value as T[] : [];
}

export function asString(value: unknown, fallback = ''): string {
  return typeof value === 'string' ? value : fallback;
}

export function asBoolean(value: unknown): boolean {
  return value === true;
}

export function topologyTable(snapshot: GeometryTopologySnapshot | undefined, kind: 'node' | 'arc' | 'region'): GeometryTable | undefined {
  return snapshot?.[kind === 'node' ? 'nodes' : kind === 'arc' ? 'arcs' : 'regions'];
}

export function localRow(kind: string, columns: GeometryColumn[], fields: GeometryField[]): GeometryRow {
  const id = `local-${globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(16).slice(2)}`}`;
  const values: Record<string, string> = {};
  for (const column of columns) values[column.id] = '';
  for (const field of fields) values[field.id] ??= '';
  values.__localDraft = kind;
  return { id, fid: -1, values };
}

export function updateLocalRows(rows: GeometryRow[], id: string, fieldID: string, value: string): GeometryRow[] {
  return rows.map((row) => row.id === id ? { ...row, values: { ...row.values, [fieldID]: value } } : row);
}
