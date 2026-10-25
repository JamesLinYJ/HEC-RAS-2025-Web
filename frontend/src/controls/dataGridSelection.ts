export interface GridCellIdentity {
  rowID: string;
  columnID: string;
}

export interface GridCellPosition {
  rowIndex: number;
  columnIndex: number;
}

export type GridArrowKey = 'ArrowUp' | 'ArrowDown' | 'ArrowLeft' | 'ArrowRight';

/** C# DataGrid Ctrl+Arrow uses the displayed table boundary, including empty cells. */
export function gridArrowDestination(
  from: GridCellPosition, key: GridArrowKey, rowCount: number, columnCount: number, toEdge: boolean,
): GridCellPosition | null {
  if (![rowCount, columnCount, from.rowIndex, from.columnIndex].every(Number.isInteger) ||
      rowCount < 1 || columnCount < 1 || from.rowIndex < 0 || from.rowIndex >= rowCount ||
      from.columnIndex < 0 || from.columnIndex >= columnCount) return null;
  const { rowIndex, columnIndex } = from;
  switch (key) {
    case 'ArrowUp': return { rowIndex: toEdge ? 0 : Math.max(0, rowIndex - 1), columnIndex };
    case 'ArrowDown': return { rowIndex: toEdge ? rowCount - 1 : Math.min(rowCount - 1, rowIndex + 1), columnIndex };
    case 'ArrowLeft': return { rowIndex, columnIndex: toEdge ? 0 : Math.max(0, columnIndex - 1) };
    case 'ArrowRight': return { rowIndex, columnIndex: toEdge ? columnCount - 1 : Math.min(columnCount - 1, columnIndex + 1) };
  }
}

export interface GridCellEdit {
  identity: GridCellIdentity;
  value: string;
}

/** Resolve against the filtered/sorted view once, before callbacks can reorder or remove rows. */
export function planGridCellEdits<Row, Column extends { id: string; readOnly?: boolean }>(
  edits: readonly GridCellEdit[], rows: readonly Row[], columns: readonly Column[],
  getRowID: (row: Row) => string, canEdit?: (row: Row, column: Column) => boolean,
): { accepted: Array<GridCellEdit & { row: Row; column: Column }>; skipped: number } {
  const rowByID = new Map(rows.map(row => [getRowID(row), row]));
  const columnByID = new Map(columns.map(column => [column.id, column]));
  const accepted: Array<GridCellEdit & { row: Row; column: Column }> = [];
  const seen = new Set<string>();
  let skipped = 0;
  for (const edit of edits) {
    const key = JSON.stringify([edit.identity.rowID, edit.identity.columnID]);
    if (seen.has(key)) continue;
    seen.add(key);
    const row = rowByID.get(edit.identity.rowID);
    const column = columnByID.get(edit.identity.columnID);
    if (!rowByID.has(edit.identity.rowID) || !column || column.readOnly || canEdit?.(row!, column) === false) {
      skipped += 1;
      continue;
    }
    accepted.push({ ...edit, row: row!, column });
  }
  return { accepted, skipped };
}

export interface GridClipboardScope {
  rows: unknown;
  columns: unknown;
  resetKey: unknown;
  viewKey: string;
  selectionKey: string;
  interactionVersion: number;
  adapters: readonly unknown[];
}

/** Clipboard permissions can resolve after the selected table or adapter has changed. */
export function sameGridClipboardScope(left: GridClipboardScope, right: GridClipboardScope): boolean {
  return left.rows === right.rows && left.columns === right.columns && Object.is(left.resetKey, right.resetKey) &&
    left.viewKey === right.viewKey && left.selectionKey === right.selectionKey &&
    left.interactionVersion === right.interactionVersion && left.adapters.length === right.adapters.length &&
    left.adapters.every((adapter, index) => adapter === right.adapters[index]);
}
