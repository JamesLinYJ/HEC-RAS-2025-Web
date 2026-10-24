export type CsvValue = string | number | null | undefined;

function csvCell(value: CsvValue): string {
  if (value === null || value === undefined || (typeof value === 'number' && !Number.isFinite(value))) return '';
  const raw = String(value);
  return /[",\r\n]/.test(raw) ? `"${raw.replace(/"/g, '""')}"` : raw;
}

export function toCsv(rows: readonly (readonly CsvValue[])[]): string {
  return rows.map((row) => row.map(csvCell).join(',')).join('\r\n');
}

export function downloadCsv(filename: string, csv: string): void {
  const blob = new Blob([`\uFEFF${csv}`], { type: 'text/csv;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  link.hidden = true;
  document.body.append(link);
  link.click();
  link.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 0);
}

export async function copyCsv(csv: string): Promise<void> {
  if (!navigator.clipboard?.writeText) throw new Error('Clipboard API is unavailable in this browser context.');
  await navigator.clipboard.writeText(csv);
}
