import type { Language } from '../../shared/workbench';

export interface ResultSeries {
  id: string;
  name: string;
  units?: string;
  elementIndex?: number;
  values: Array<number | null>;
}

export interface ResultDataset {
  name: string;
  times: string[];
  series: ResultSeries[];
  source: 'user-file' | 'backend-result';
  sourceLabel: string;
}

function numberOrNull(value: unknown): number | null {
  if (typeof value === 'number') return Number.isFinite(value) ? value : null;
  if (typeof value !== 'string' || value.trim() === '') return null;
  const parsed = Number(value.trim());
  return Number.isFinite(parsed) ? parsed : null;
}

function delimiterFor(text: string): string {
  const line = text.split(/\r?\n/, 1)[0] ?? '';
  const choices = [',', '\t', ';'];
  return choices.map((delimiter) => ({ delimiter, count: line.split(delimiter).length - 1 }))
    .sort((a, b) => b.count - a.count)[0]?.delimiter ?? ',';
}

function localized(language: Language, zh: string, en: string): string {
  return language === 'zh' ? zh : en;
}

function parseDelimited(text: string, delimiter: string, language: Language): string[][] {
  const rows: string[][] = [];
  let row: string[] = [];
  let cell = '';
  let quoted = false;
  for (let index = 0; index < text.length; index += 1) {
    const character = text[index] ?? '';
    if (character === '"') {
      if (quoted && text[index + 1] === '"') {
        cell += '"';
        index += 1;
      } else {
        quoted = !quoted;
      }
    } else if (!quoted && character === delimiter) {
      row.push(cell);
      cell = '';
    } else if (!quoted && (character === '\n' || character === '\r')) {
      if (character === '\r' && text[index + 1] === '\n') index += 1;
      row.push(cell);
      if (row.some((item) => item.trim() !== '')) rows.push(row);
      row = [];
      cell = '';
    } else {
      cell += character;
    }
  }
  if (quoted) throw new Error(localized(language, '文件中有未闭合的引号字段。', 'The file contains an unclosed quoted field.'));
  row.push(cell);
  if (row.some((item) => item.trim() !== '')) rows.push(row);
  return rows;
}

function timeColumnIndex(headers: string[]): number {
  const match = headers.findIndex((header) => /^(time|date|datetime|timestamp|step|time step|时刻|时间|时间步|日期)$/i.test(header.trim()));
  return match >= 0 ? match : 0;
}

function fromTable(headers: string[], rows: unknown[][], sourceName: string, language: Language): ResultDataset {
  if (headers.length < 2 || rows.length === 0) throw new Error(localized(language, '请提供时间列和至少一个数值数据列。', 'Include a time column and at least one numeric data column.'));
  const timeIndex = timeColumnIndex(headers);
  const times = rows.map((row, index) => String(row[timeIndex] ?? index + 1));
  const series = headers.flatMap((header, columnIndex) => {
    if (columnIndex === timeIndex) return [];
    const values = rows.map((row) => numberOrNull(row[columnIndex]));
    if (!values.some((value) => value !== null)) return [];
    return [{ id: 'file-' + columnIndex, name: header.trim() || localized(language, `序列 ${columnIndex}`, `Series ${columnIndex}`), values }];
  });
  if (series.length === 0) throw new Error(localized(language, '文件中没有找到数值序列。', 'No numeric series were found in this file.'));
  return { name: sourceName, times, series, source: 'user-file', sourceLabel: localized(language, '用户导入数据', 'Imported user data') };
}

function fromJson(value: unknown, sourceName: string, language: Language): ResultDataset {
  if (Array.isArray(value) && value.length > 0 && value.every((row) => row !== null && typeof row === 'object' && !Array.isArray(row))) {
    const records = value as Array<Record<string, unknown>>;
    const headers = Array.from(new Set(records.flatMap((row) => Object.keys(row))));
    return fromTable(headers, records.map((row) => headers.map((header) => row[header])), sourceName, language);
  }
  if (value !== null && typeof value === 'object') {
    const object = value as Record<string, unknown>;
    if (Array.isArray(object.times) && Array.isArray(object.series)) {
      const times = object.times.map((item) => String(item));
      const series = object.series.flatMap((item, index) => {
        if (item === null || typeof item !== 'object') return [];
        const record = item as Record<string, unknown>;
        const rawValues = record.values;
        if (!Array.isArray(rawValues)) return [];
        return [{ id: 'json-' + index, name: String(record.name ?? localized(language, `序列 ${index + 1}`, `Series ${index + 1}`)), units: typeof record.units === 'string' ? record.units : undefined, values: times.map((_, pointIndex) => numberOrNull(rawValues[pointIndex])) }];
      });
      if (times.length && series.length) return { name: sourceName, times, series, source: 'user-file', sourceLabel: localized(language, '用户导入数据', 'Imported user data') };
    }
    if (Array.isArray(object.points)) {
      const points = object.points as Array<Record<string, unknown>>;
      const valid = points.filter((point) => point && typeof point === 'object');
      const times = valid.map((point, index) => String(point.time ?? point.Time ?? point.profileName ?? point.ProfileName ?? index + 1));
      const values = valid.map((point) => numberOrNull(point.value ?? point.Value));
      if (times.length && values.some((item) => item !== null)) {
        return { name: sourceName, times, series: [{ id: 'json-points', name: String(object.name ?? object.variable ?? localized(language, '数值', 'Value')), values }], source: 'user-file', sourceLabel: localized(language, '用户导入数据', 'Imported user data') };
      }
    }
  }
  throw new Error(localized(language, 'JSON 数据结构无效。请提供记录数组、{ times, series }，或包含 { points } 的时间序列对象。', 'Expected rows, { times, series }, or a { points } time-series object.'));
}

export function parseUserData(text: string, sourceName: string, language: Language): ResultDataset {
  const content = text.replace(/^\uFEFF/, '').trim();
  if (!content) throw new Error(localized(language, '所选文件为空。', 'The selected file is empty.'));
  if (sourceName.toLowerCase().endsWith('.json') || content.startsWith('{') || content.startsWith('[')) {
    try {
      return fromJson(JSON.parse(content) as unknown, sourceName, language);
    } catch (error) {
      if (error instanceof SyntaxError) {
        const detail = error.message;
        throw new Error(localized(language, `JSON 格式无效。原始解析错误：${detail}`, `The JSON is invalid. Parser error: ${detail}`));
      }
      throw error;
    }
  }
  const rows = parseDelimited(content, delimiterFor(content), language);
  const headers = rows[0]?.map((header) => header.trim()) ?? [];
  return fromTable(headers, rows.slice(1), sourceName, language);
}

export function downloadCsv(dataset: ResultDataset, visibleSeries: ResultSeries[], language: Language): void {
  const quote = (value: string): string => '"' + value.replaceAll('"', '""') + '"';
  const rows = [['Time', ...visibleSeries.map((series) => series.name)], ...dataset.times.map((time, index) => [time, ...visibleSeries.map((series) => series.values[index] === null || series.values[index] === undefined ? '' : String(series.values[index]))])];
  const csv = '\uFEFF' + rows.map((row) => row.map(quote).join(',')).join('\r\n');
  const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = (dataset.name.replace(/\.[^.]+$/, '') || localized(language, '结果', 'results')) + '.csv';
  anchor.click();
  URL.revokeObjectURL(url);
}
