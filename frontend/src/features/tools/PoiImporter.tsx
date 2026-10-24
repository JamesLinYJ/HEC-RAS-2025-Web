import { useEffect, useMemo, useRef, useState } from 'react';
import type { Language } from '../../shared/workbench';
import type { LocalSourceFile } from './LocalFilesImportSource';

type CsvTable = { headers: string[]; rows: string[][] };
type PointFeature = { type: 'Feature'; id?: string | number; geometry: { type: 'Point'; coordinates: number[] }; properties: Record<string, unknown> | null };
type FeatureCollection = { type: 'FeatureCollection'; features: Array<{ type: string; geometry?: { type?: string; coordinates?: unknown }; id?: string | number; properties?: Record<string, unknown> | null }> };

function localized(language: Language, zh: string, en: string): string {
  return language === 'zh' ? zh : en;
}

function parseCsv(source: string, language: Language): CsvTable {
  const rows: string[][] = [];
  let row: string[] = [];
  let cell = '';
  let quoted = false;
  for (let index = 0; index < source.length; index += 1) {
    const character = source[index]!;
    if (quoted) {
      if (character === '"' && source[index + 1] === '"') { cell += '"'; index += 1; }
      else if (character === '"') quoted = false;
      else cell += character;
    } else if (character === '"' && cell.length === 0) quoted = true;
    else if (character === ',') { row.push(cell); cell = ''; }
    else if (character === '\n' || character === '\r') {
      if (character === '\r' && source[index + 1] === '\n') index += 1;
      row.push(cell); cell = '';
      if (row.some(value => value.trim() !== '')) rows.push(row);
      row = [];
    } else cell += character;
  }
  if (quoted) throw new Error(localized(language, 'CSV 中存在未闭合的引号字段。', 'CSV contains an unterminated quoted value.'));
  if (cell.length > 0 || row.length > 0) { row.push(cell); if (row.some(value => value.trim() !== '')) rows.push(row); }
  const headers = (rows.shift() ?? []).map(value => value.trim().replace(/^\uFEFF/, ''));
  if (headers.length < 2 || headers.some(value => !value)) throw new Error(localized(language, 'CSV 必须包含至少两个非空列名。', 'CSV needs a header row with at least two named columns.'));
  if (new Set(headers).size !== headers.length) throw new Error(localized(language, 'CSV 列名必须唯一。', 'CSV column names must be unique.'));
  return { headers, rows: rows.map(values => headers.map((_, column) => values[column] ?? '')) };
}

function parsePointGeoJson(source: string, language: Language): PointFeature[] {
  let value: FeatureCollection;
  try {
    value = JSON.parse(source) as FeatureCollection;
  } catch (caught) {
    const detail = caught instanceof Error ? caught.message : String(caught);
    throw new Error(localized(language, `GeoJSON 格式无效。原始解析错误：${detail}`, `Invalid GeoJSON. Parser error: ${detail}`));
  }
  if (!value || typeof value !== 'object' || value.type !== 'FeatureCollection' || !Array.isArray(value.features)) throw new Error(localized(language, '请选择有效的 GeoJSON 要素集合（FeatureCollection）。', 'Choose a GeoJSON FeatureCollection.'));
  return value.features.map((feature, index) => {
    if (feature.type !== 'Feature' || feature.geometry?.type !== 'Point' || !Array.isArray(feature.geometry.coordinates)) {
      throw new Error(localized(language, `第 ${index + 1} 个要素不是点要素（Point）。`, `Feature ${index + 1} is not a Point feature.`));
    }
    const coordinates = feature.geometry.coordinates;
    if (coordinates.length < 2 || !Number.isFinite(coordinates[0]) || !Number.isFinite(coordinates[1])) {
      throw new Error(localized(language, `第 ${index + 1} 个要素的 XY 坐标无效。`, `Feature ${index + 1} has invalid XY coordinates.`));
    }
    return {
      type: 'Feature',
      ...(feature.id === undefined ? {} : { id: feature.id }),
      geometry: { type: 'Point', coordinates: coordinates as number[] },
      properties: feature.properties ?? null,
    };
  });
}

function escapeCsv(value: unknown): string {
  const raw = value === null || value === undefined ? '' : String(value);
  return /[",\r\n]/.test(raw) ? `"${raw.replaceAll('"', '""')}"` : raw;
}

function download(filename: string, content: string, mime: string): void {
  const url = URL.createObjectURL(new Blob([content], { type: mime }));
  const anchor = document.createElement('a');
  anchor.href = url; anchor.download = filename; anchor.click();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}

export function PoiImporter({ language, source }: { language: Language; source: LocalSourceFile | null }) {
  const zh = language === 'zh';
  const defaultOutputName = localized(language, '点数据', 'points');
  const [xColumn, setXColumn] = useState('');
  const [yColumn, setYColumn] = useState('');
  const [error, setError] = useState<string>();
  const [output, setOutput] = useState<string>();
  const [outputName, setOutputName] = useState(defaultOutputName);
  const previousDefaultOutputName = useRef(defaultOutputName);

  useEffect(() => {
    setOutputName(current => current === previousDefaultOutputName.current ? defaultOutputName : current);
    previousDefaultOutputName.current = defaultOutputName;
  }, [defaultOutputName]);

  const isCsv = source?.file.name.toLowerCase().endsWith('.csv') || source?.file.type === 'text/csv';
  const parsedCsv = useMemo(() => {
    if (!source || !isCsv) return undefined;
    try { return { table: parseCsv(source.text, language), error: undefined }; }
    catch (caught) { return { table: undefined, error: caught instanceof Error ? caught.message : String(caught) }; }
  }, [isCsv, language, source]);
  const csv = parsedCsv?.table;
  const csvError = parsedCsv?.error;
  const geojsonError = useMemo(() => {
    if (!source || isCsv) return undefined;
    try { parsePointGeoJson(source.text, language); return undefined; } catch (caught) { return caught instanceof Error ? caught.message : String(caught); }
  }, [isCsv, language, source]);

  const convert = (): void => {
    setError(undefined); setOutput(undefined);
    if (!source) return;
    try {
      if (isCsv) {
        const table = parseCsv(source.text, language);
        const xIndex = table.headers.indexOf(xColumn);
        const yIndex = table.headers.indexOf(yColumn);
        if (xIndex < 0 || yIndex < 0 || xIndex === yIndex) throw new Error(localized(language, '请选择不同的 X 坐标列和 Y 坐标列。', 'Choose different X and Y columns.'));
        const features = table.rows.map((row, index) => {
          const x = Number(row[xIndex]); const y = Number(row[yIndex]);
          if (!Number.isFinite(x) || !Number.isFinite(y)) throw new Error(localized(language, `第 ${index + 2} 行坐标无效。`, `Row ${index + 2} has invalid coordinates.`));
          const properties: Record<string, string> = {};
          table.headers.forEach((header, column) => { if (column !== xIndex && column !== yIndex) properties[header] = row[column] ?? ''; });
          return { type: 'Feature', id: index + 1, geometry: { type: 'Point', coordinates: [x, y] }, properties };
        });
        setOutput(JSON.stringify({ type: 'FeatureCollection', features }, null, 2));
      } else {
        const features = parsePointGeoJson(source.text, language);
        const properties = [...new Set(features.flatMap(feature => Object.keys(feature.properties ?? {})))];
        const headers = ['x', 'y', ...properties];
        const lines = [headers.map(escapeCsv).join(',')];
        for (const feature of features) {
          const row: unknown[] = [feature.geometry.coordinates[0], feature.geometry.coordinates[1], ...properties.map(key => feature.properties?.[key])];
          lines.push(row.map(escapeCsv).join(','));
        }
        setOutput(lines.join('\r\n'));
      }
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : String(caught));
    }
  };

  const downloadOutput = (): void => {
    if (!output) return;
    const extension = isCsv ? 'geojson' : 'csv';
    download(`${outputName.trim() || localized(language, '点数据', 'points')}.${extension}`, output, isCsv ? 'application/geo+json' : 'text/csv;charset=utf-8');
  };

  return (
    <section className="tools-subpanel">
      <div className="tools-subpanel-heading">
        <div><span className="tools-kicker">{zh ? '点要素导入' : 'POINT IMPORT'}</span><h3>{zh ? 'CSV ⇄ GeoJSON 点要素' : 'CSV ⇄ GeoJSON points'}</h3></div>
        <span className="tools-badge">{zh ? '本地转换' : 'Local conversion'}</span>
      </div>
      {!source ? <p className="tools-muted">{zh ? '先选择一个 CSV 或 GeoJSON 文件。' : 'Choose a CSV or GeoJSON file first.'}</p> : (
        <>
          <div className="tools-file-meta"><span className="tools-file-mark">{isCsv ? 'CSV' : 'JSON'}</span><div><strong>{source.file.name}</strong><small>{isCsv ? `${csv?.rows.length ?? 0} ${zh ? '行' : 'rows'}` : `${geojsonError ? '—' : zh ? 'GeoJSON 点' : 'GeoJSON Point'} ${zh ? '点要素' : 'features'}`}</small></div></div>
          {isCsv && csv && <div className="tools-field-grid">
            <label className="tools-field"><span>{zh ? 'X 坐标列' : 'X coordinate column'}</span><select value={xColumn || csv.headers[0]} onChange={event => setXColumn(event.target.value)}>{csv.headers.map(name => <option key={name} value={name}>{name}</option>)}</select></label>
            <label className="tools-field"><span>{zh ? 'Y 坐标列' : 'Y coordinate column'}</span><select value={yColumn || csv.headers[1]} onChange={event => setYColumn(event.target.value)}>{csv.headers.map(name => <option key={name} value={name}>{name}</option>)}</select></label>
          </div>}
          {isCsv && !csv && <div className="tools-inline-error" role="alert">{csvError ?? (zh ? 'CSV 表头无效或无法解析。' : 'The CSV header is invalid or could not be parsed.')}</div>}
          {!isCsv && geojsonError && <div className="tools-inline-error">{geojsonError}</div>}
          <div className="tools-field-grid">
            <label className="tools-field"><span>{zh ? '输出文件名' : 'Output filename'}</span><input value={outputName} onChange={event => setOutputName(event.target.value)} /></label>
          </div>
          <div className="tools-actions"><button type="button" className="tools-button tools-button-primary" onClick={convert} disabled={isCsv ? !csv : Boolean(geojsonError)}>{zh ? (isCsv ? '转换为 GeoJSON' : '转换为 CSV') : (isCsv ? 'Convert to GeoJSON' : 'Convert to CSV')}</button>
            {output && <button type="button" className="tools-button tools-button-secondary" onClick={downloadOutput}>{zh ? '下载结果' : 'Download result'}</button>}</div>
          {error && <div className="tools-inline-error" role="alert">{error}</div>}
          {output && <pre className="tools-output-preview">{output.slice(0, 7000)}{output.length > 7000 ? '\n…' : ''}</pre>}
        </>
      )}
      <p className="tools-footnote">{zh ? '此转换保留属性值文本；只处理点坐标，不执行栅格或几何分析。' : 'This conversion preserves attribute values as text and handles point coordinates only; it does not run raster or geometry analysis.'}</p>
    </section>
  );
}
