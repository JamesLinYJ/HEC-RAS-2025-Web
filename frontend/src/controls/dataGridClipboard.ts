/** Parse clipboard text as TSV without changing the contents of any cell. */
export function parseClipboardMatrix(text: string): string[][] | null {
  const rows: string[][] = [];
  let row: string[] = [];
  let cell = '';
  let insideQuotedCell = false;
  let afterClosingQuote = false;
  let endedWithRowSeparator = false;

  for (let index = 0; index < text.length; index += 1) {
    const character = text[index]!;

    if (insideQuotedCell) {
      if (character === '"') {
        if (text[index + 1] === '"') {
          cell += '"';
          index += 1;
        } else {
          insideQuotedCell = false;
          afterClosingQuote = true;
        }
      } else {
        cell += character;
      }
      endedWithRowSeparator = false;
      continue;
    }

    if (afterClosingQuote) {
      if (character === '\t') {
        row.push(cell);
        cell = '';
        afterClosingQuote = false;
        endedWithRowSeparator = false;
        continue;
      }
      if (character === '\r' || character === '\n') {
        row.push(cell);
        rows.push(row);
        row = [];
        cell = '';
        afterClosingQuote = false;
        if (character === '\r' && text[index + 1] === '\n') index += 1;
        endedWithRowSeparator = true;
        continue;
      }
      return null;
    }

    if (character === '"') {
      if (cell.length !== 0) return null;
      insideQuotedCell = true;
      endedWithRowSeparator = false;
      continue;
    }
    if (character === '\t') {
      row.push(cell);
      cell = '';
      endedWithRowSeparator = false;
      continue;
    }
    if (character === '\r' || character === '\n') {
      row.push(cell);
      rows.push(row);
      row = [];
      cell = '';
      if (character === '\r' && text[index + 1] === '\n') index += 1;
      endedWithRowSeparator = true;
      continue;
    }

    cell += character;
    endedWithRowSeparator = false;
  }

  if (insideQuotedCell) return null;
  if (!endedWithRowSeparator) {
    row.push(cell);
    rows.push(row);
  }
  return rows.length > 0 ? rows : [['']];
}

/** Encode a TSV matrix with RFC-style quoted fields for tabs, quotes, and line breaks. */
export function serializeClipboardMatrix(matrix: readonly (readonly string[])[]): string {
  return matrix.map(row => row.map(value => {
    if (value !== '' && !/[\t\r\n"]/.test(value)) return value;
    return `"${value.replace(/"/g, '""')}"`;
  }).join('\t')).join('\r\n');
}

/** Return whether every TSV cell fits in the current filtered grid before any writes occur. */
export function clipboardMatrixFits(
  matrix: readonly (readonly string[])[],
  originRow: number,
  originColumn: number,
  availableRows: number,
  availableColumns: number,
): boolean {
  if (!Number.isInteger(originRow) || !Number.isInteger(originColumn) || originRow < 0 || originColumn < 0) return false;
  if (matrix.length === 0 || originRow + matrix.length > availableRows) return false;
  return matrix.every(row => row.length > 0 && originColumn + row.length <= availableColumns);
}
