import type { ModelRow } from './model';

export type ClassificationMappingValidationCode =
  | 'duplicate-source-identity'
  | 'invalid-source-key'
  | 'duplicate-source-key'
  | 'row-set-changed'
  | 'row-identity-changed'
  | 'invalid-row-shape';

/**
 * The Host identifies a raster mapping by its original Int32 key and emits a
 * revision-scoped row ID for that key. Only the Value cell can change; this
 * guard keeps the draft in the shape ClassificationEditorService accepts.
 */
export function validateClassificationMappingRows(sourceRows: ModelRow[], rows: ModelRow[]): ClassificationMappingValidationCode | null {
  const sourceById = new Map<string, ModelRow>();
  const keys = new Set<number>();

  for (const row of sourceRows) {
    if (!row.id || sourceById.has(row.id)) return 'duplicate-source-identity';
    const sourceColumns = Object.keys(row.values);
    if (sourceColumns.length !== 2 || !sourceColumns.includes('Key') || !sourceColumns.includes('Value') || typeof row.values.Value !== 'string') return 'invalid-row-shape';
    const keyText = row.values.Key;
    if (typeof keyText !== 'string' || !/^(?:0|-?[1-9]\d*)$/.test(keyText)) return 'invalid-source-key';
    const key = Number(keyText);
    if (!Number.isInteger(key) || key < -2147483648 || key > 2147483647) return 'invalid-source-key';
    if (keys.has(key)) return 'duplicate-source-key';
    keys.add(key);
    sourceById.set(row.id, row);
  }

  if (rows.length !== sourceRows.length || new Set(rows.map((row) => row.id)).size !== rows.length) return 'row-set-changed';
  for (const row of rows) {
    const original = sourceById.get(row.id);
    if (!original) return 'row-set-changed';
    if (row.values.Key !== original.values.Key) return 'row-identity-changed';
    const keys = Object.keys(row.values);
    if (keys.length !== 2 || !keys.includes('Key') || !keys.includes('Value') || typeof row.values.Value !== 'string') return 'invalid-row-shape';
  }
  return null;
}
