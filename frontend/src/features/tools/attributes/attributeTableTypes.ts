export interface AttributeTableColumn {
  id: string;
  name: string;
  type: string;
  readOnly?: boolean;
}

export interface AttributeTableRow {
  id: string;
  values: Record<string, string | null>;
}

export interface AttributeTableData {
  sourcePath: string;
  shapeType: string;
  columns: AttributeTableColumn[];
  rows: AttributeTableRow[];
  initValueTypesToDefault?: boolean;
}

export interface AttributeTableSourceCapture {
  sourcePath: string;
  canonicalPath: string;
  revision: string;
  length: number;
}

export interface AttributeTableSourceFamilyFile {
  inputPath: string;
  path: string;
  role: string;
  required: boolean;
  exists: boolean;
  revision: string | null;
}

/** Exact MapSourceFamilyCapture returned by map.vector.captureShapefile. */
export interface AttributeTableSourceFamilyCapture {
  family: 'shapefile';
  sourcePath: string;
  revision: string;
  files: AttributeTableSourceFamilyFile[];
}

export interface AttributeCellEdit {
  rowId: string;
  columnId: string;
  value: string | null;
}

export type AttributeTableMutation =
  | { operation: 'addColumn'; name: string; type: 'String' | 'Integer' | 'Numeric' | 'Boolean' }
  | { operation: 'renameColumn'; columnId: string; name: string }
  | { operation: 'removeColumn'; columnId: string }
  | { operation: 'setCells'; cells: AttributeCellEdit[] };

const knownCellTypes = new Set(['String', 'Int32', 'Double', 'Single', 'Boolean']);

export function isSupportedAttributeCellType(type: string): boolean {
  return knownCellTypes.has(type);
}

export function isWorkspaceCapture(value: unknown, requestedPath: string): value is AttributeTableSourceCapture {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const capture = value as Partial<AttributeTableSourceCapture>;
  return capture.sourcePath === requestedPath && typeof capture.canonicalPath === 'string' && capture.canonicalPath.length > 0 &&
    typeof capture.revision === 'string' && /^[\da-f]{64}$/i.test(capture.revision) &&
    typeof capture.length === 'number' && Number.isSafeInteger(capture.length) && capture.length >= 0;
}

export function sameSourceCapture(left: AttributeTableSourceCapture, right: AttributeTableSourceCapture): boolean {
  return sameSourcePath(left.canonicalPath, right.canonicalPath) &&
    left.revision.toLocaleUpperCase() === right.revision.toLocaleUpperCase() && left.length === right.length;
}

export function isShapefileFamilyCapture(value: unknown, expectedSourcePath: string,
  expectedSourceRevision: string): value is AttributeTableSourceFamilyCapture {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const capture = value as Partial<AttributeTableSourceFamilyCapture>;
  if (capture.family !== 'shapefile' || typeof capture.sourcePath !== 'string' ||
      !sameSourcePath(capture.sourcePath, expectedSourcePath) || typeof capture.revision !== 'string' ||
      !/^[\da-f]{64}$/i.test(capture.revision) || !Array.isArray(capture.files)) return false;
  const roles = new Map<string, AttributeTableSourceFamilyFile>();
  for (const rawFile of capture.files) {
    if (!rawFile || typeof rawFile !== 'object' || Array.isArray(rawFile)) return false;
    const file = rawFile as Partial<AttributeTableSourceFamilyFile>;
    if (typeof file.inputPath !== 'string' || !file.inputPath || typeof file.path !== 'string' || !file.path ||
        typeof file.role !== 'string' || !file.role || typeof file.required !== 'boolean' ||
        typeof file.exists !== 'boolean' || !(file.revision === null ||
          (typeof file.revision === 'string' && /^[\da-f]{64}$/i.test(file.revision))) || roles.has(file.role)) return false;
    roles.set(file.role, file as AttributeTableSourceFamilyFile);
  }
  const source = roles.get('source');
  const shx = roles.get('shx');
  const dbf = roles.get('dbf');
  const prj = roles.get('prj');
  return roles.size === 4 && source?.required === true && source.exists && source.revision !== null &&
    sameSourcePath(source.path, capture.sourcePath) && source.revision.toLocaleUpperCase() === expectedSourceRevision.toLocaleUpperCase() &&
    shx?.required === true && shx.exists && shx.revision !== null &&
    dbf?.required === true && dbf.exists && dbf.revision !== null &&
    prj?.required === false && (prj.exists ? prj.revision !== null : prj.revision === null);
}

export function sameSourceFamilyCapture(left: AttributeTableSourceFamilyCapture,
  right: AttributeTableSourceFamilyCapture): boolean {
  if (left.family !== right.family || !sameSourcePath(left.sourcePath, right.sourcePath) ||
      left.revision.toLocaleUpperCase() !== right.revision.toLocaleUpperCase() || left.files.length !== right.files.length) return false;
  const rightFiles = new Map(right.files.map(file => [file.role, file]));
  return left.files.every(file => {
    const next = rightFiles.get(file.role);
    return next !== undefined && sameSourcePath(file.inputPath, next.inputPath) && sameSourcePath(file.path, next.path) &&
      file.required === next.required && file.exists === next.exists &&
      (file.revision ?? '').toLocaleUpperCase() === (next.revision ?? '').toLocaleUpperCase();
  });
}

export function sameSourcePath(left: string, right: string): boolean {
  return left.replaceAll('\\', '/') === right.replaceAll('\\', '/');
}

export function isAttributeTableData(value: unknown): value is AttributeTableData {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const table = value as Partial<AttributeTableData>;
  if (typeof table.sourcePath !== 'string' || !table.sourcePath || typeof table.shapeType !== 'string' ||
      !Array.isArray(table.columns) || !Array.isArray(table.rows)) return false;
  const columnIds = new Set<string>();
  for (const rawColumn of table.columns) {
    if (!rawColumn || typeof rawColumn !== 'object') return false;
    const column = rawColumn as Partial<AttributeTableColumn>;
    if (typeof column.id !== 'string' || !column.id || typeof column.name !== 'string' || !column.name.trim() ||
        typeof column.type !== 'string' || (column.readOnly !== undefined && typeof column.readOnly !== 'boolean') ||
        columnIds.has(column.id)) return false;
    columnIds.add(column.id);
  }
  const rowIds = new Set<string>();
  for (const rawRow of table.rows) {
    if (!rawRow || typeof rawRow !== 'object') return false;
    const row = rawRow as Partial<AttributeTableRow>;
    if (typeof row.id !== 'string' || !row.id || !row.values || typeof row.values !== 'object' || Array.isArray(row.values) || rowIds.has(row.id)) return false;
    const keys = Object.keys(row.values);
    if (keys.length !== columnIds.size || keys.some(key => !columnIds.has(key))) return false;
    if (Object.values(row.values).some(cell => cell !== null && typeof cell !== 'string')) return false;
    rowIds.add(row.id);
  }
  return true;
}

export function formatAttributeCell(value: string | null | undefined): string {
  return value == null ? '∅' : value;
}
