export function sourceText(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

export function ScalarInput({ label, sourceName, value, onChange }: {
  label: string;
  sourceName: string;
  value: unknown;
  onChange: (value: string) => void;
}) {
  return <label className="hydraulics-field">
    <span>{label}<code>{sourceName}</code></span>
    <input inputMode="decimal" value={sourceText(value)} onChange={event => onChange(event.currentTarget.value)} />
  </label>;
}

export function BooleanInput({ label, sourceName, value, onChange }: {
  label: string;
  sourceName: string;
  value: unknown;
  onChange: (value: boolean) => void;
}) {
  return <label className="hydraulics-toggle">
    <span>{label}<code>{sourceName}</code></span>
    <input type="checkbox" checked={value === true} onChange={event => onChange(event.currentTarget.checked)} />
  </label>;
}

export function NumericArrayEditor({ label, sourceName, values, onChange }: {
  label: string;
  sourceName: string;
  values: unknown;
  onChange: (index: number, value: string) => void;
}) {
  const entries = Array.isArray(values) ? values : [];
  return <fieldset className="hydraulics-array-editor">
    <legend>{label} <code>{sourceName}</code></legend>
    {entries.length === 0
      ? <p className="hydraulics-muted">没有可编辑的数据项。</p>
      : <div className="hydraulics-array-grid">{entries.map((value, index) => <label key={`${sourceName}:${index}`}>
        <span>{index + 1}</span>
        <input aria-label={`${sourceName} ${index + 1}`} inputMode="decimal" value={sourceText(value)} onChange={event => onChange(index, event.currentTarget.value)} />
      </label>)}</div>}
  </fieldset>;
}

export function NumericMatrixEditor({ label, sourceName, values, columnLabels, onChange }: {
  label: string;
  sourceName: string;
  values: unknown;
  columnLabels?: unknown;
  onChange: (row: number, column: number, value: string) => void;
}) {
  const rows = Array.isArray(values) ? values : [];
  const labels = Array.isArray(columnLabels) ? columnLabels : [];
  const columnCount = Math.max(labels.length, ...rows.map(row => Array.isArray(row) ? row.length : 0), 0);
  return <fieldset className="hydraulics-matrix-editor">
    <legend>{label} <code>{sourceName}</code></legend>
    {rows.length === 0
      ? <p className="hydraulics-muted">没有可编辑的数据行。</p>
      : <div className="hydraulics-table-scroll"><table className="hydraulics-table">
        <thead><tr><th>行</th>{Array.from({ length: columnCount }, (_, column) => <th key={column}>
          {labels[column] === undefined ? column + 1 : sourceText(labels[column]) || column + 1}
        </th>)}</tr></thead>
        <tbody>{rows.map((row, rowIndex) => <tr key={`${sourceName}:${rowIndex}`}>
          <th>{rowIndex + 1}</th>
          {Array.from({ length: columnCount }, (_, column) => {
            const cell = Array.isArray(row) ? row[column] : undefined;
            return <td key={column}><input
              aria-label={`${sourceName} ${rowIndex + 1}, ${column + 1}`}
              inputMode="decimal"
              value={sourceText(cell)}
              disabled={!Array.isArray(row) || column >= row.length}
              onChange={event => onChange(rowIndex, column, event.currentTarget.value)}
            /></td>;
          })}
        </tr>)}</tbody>
      </table></div>}
  </fieldset>;
}

export function updateStringArray(source: unknown, index: number, value: string): string[] {
  const copy = Array.isArray(source) ? [...source] : [];
  if (index >= 0 && index < copy.length) copy[index] = value;
  return copy.map(item => typeof item === 'string' ? item : String(item ?? ''));
}

export function updateStringMatrix(source: unknown, row: number, column: number, value: string): string[][] {
  const copy = Array.isArray(source) ? source.map(item => Array.isArray(item) ? [...item] : item) : [];
  const selected = copy[row];
  if (Array.isArray(selected) && column >= 0 && column < selected.length) selected[column] = value;
  return copy.map(item => Array.isArray(item) ? item.map(cell => typeof cell === 'string' ? cell : String(cell ?? '')) : []);
}
