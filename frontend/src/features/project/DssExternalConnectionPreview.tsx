import { useEffect, useRef, useState } from 'react';
import type { BackendGateway, Language } from '../../shared/workbench';
import { errorDiagnostics, message, text } from '../../shared/workbench';

interface DssRecordDescriptor {
  path: string;
  recordType: string;
  parts: string[];
  comprisingPaths: string[];
}

interface DssCatalogResponse {
  path: string;
  revision: string;
  records: DssRecordDescriptor[];
}

interface DssReadResponse {
  path: string;
  revision: string;
  records: DssRecordResult[];
}

interface DssRecordResult {
  path: string;
  recordType: string;
  kind: string;
  error?: string | null;
  timeSeries?: {
    times: string[];
    values: Array<number | null>;
    qualities?: number[] | null;
    units: string;
    dataType: string;
  } | null;
  pairedData?: {
    ordinates: Array<number | null>;
    curves: Array<{ label: string; values: Array<number | null> }>;
    independentUnits: string;
    dependentUnits: string;
    independentType: string;
    dependentType: string;
  } | null;
  location?: {
    x: number | null;
    y: number | null;
    z: number | null;
    coordinateSystem: string;
    coordinateID: number;
    horizontalUnits: number;
    horizontalDatum: number;
    verticalUnits: number;
    verticalDatum: number;
    timeZoneName: string;
    supplemental: string;
  } | null;
}

interface Props {
  language: Language;
  backend: BackendGateway;
  path: string;
}

interface CatalogState extends DssCatalogResponse {}

function isSha256(value: unknown): value is string {
  return typeof value === 'string' && /^[0-9a-f]{64}$/i.test(value);
}

function sameServerPath(left: string, right: string): boolean {
  const normalize = (value: string): string => value.replace(/\\/g, '/').replace(/\/+$/, '');
  const a = normalize(left);
  const b = normalize(right);
  const windows = (value: string): boolean => /^[A-Za-z]:\//.test(value) || value.startsWith('//');
  return windows(a) && windows(b) ? a.toLowerCase() === b.toLowerCase() : a === b;
}

function rawError(error: unknown): string {
  const details = errorDiagnostics(error);
  if (details instanceof Error) return `${details.name}: ${details.message}${details.stack ? `\n${details.stack}` : ''}`;
  try { return JSON.stringify(details, null, 2) ?? String(details); }
  catch { return String(details); }
}

function valueText(value: number | null | undefined): string {
  return value === null || value === undefined ? '—' : String(value);
}

export function DssExternalConnectionPreview({ language, backend, path }: Props) {
  const canCatalog = backend.supports('dss.catalog');
  const canRead = backend.supports('dss.read');
  const [catalog, setCatalog] = useState<CatalogState | null>(null);
  const [selectedPath, setSelectedPath] = useState('');
  const [record, setRecord] = useState<DssRecordResult | null>(null);
  const [busy, setBusy] = useState<'catalog' | 'read' | null>(null);
  const [error, setError] = useState<string>();
  const [details, setDetails] = useState<string>();
  const generation = useRef(0);
  const abort = useRef<AbortController | undefined>(undefined);

  useEffect(() => {
    const requestGeneration = ++generation.current;
    abort.current?.abort();
    abort.current = undefined;
    setCatalog(null);
    setSelectedPath('');
    setRecord(null);
    setError(undefined);
    setDetails(undefined);
    setBusy(null);
    if (!path || !path.toLowerCase().endsWith('.dss') || !canCatalog) return;

    const controller = new AbortController();
    abort.current = controller;
    setBusy('catalog');
    void backend.request<DssCatalogResponse>('dss.catalog', { path }, { signal: controller.signal }).then((response) => {
      if (requestGeneration !== generation.current || controller.signal.aborted) return;
      if (!response || !sameServerPath(response.path, path) || !isSha256(response.revision) || !Array.isArray(response.records)) {
        throw new Error('dss.catalog returned an incomplete source path, revision, or record catalog.');
      }
      setCatalog(response);
    }).catch((requestError) => {
      if (requestGeneration !== generation.current || controller.signal.aborted) return;
      setError(message(requestError, language));
      setDetails(rawError(requestError));
    }).finally(() => {
      if (requestGeneration === generation.current && !controller.signal.aborted) setBusy(null);
    });
    return () => {
      controller.abort();
    };
  }, [backend, canCatalog, language, path]);

  const readSelected = async (): Promise<void> => {
    if (!catalog || !selectedPath || !canRead) return;
    const requestGeneration = ++generation.current;
    abort.current?.abort();
    const controller = new AbortController();
    abort.current = controller;
    setBusy('read');
    setError(undefined);
    setDetails(undefined);
    setRecord(null);
    try {
      const response = await backend.request<DssReadResponse>('dss.read', {
        path: catalog.path,
        revision: catalog.revision,
        recordPaths: [selectedPath],
      }, { signal: controller.signal });
      if (requestGeneration !== generation.current || controller.signal.aborted) return;
      if (!response || !sameServerPath(response.path, catalog.path) || response.revision.toLowerCase() !== catalog.revision.toLowerCase() ||
          !Array.isArray(response.records) || response.records.length !== 1 || response.records[0]?.path !== selectedPath) {
        throw new Error('dss.read returned a different source revision or record path than the selected catalog item.');
      }
      setRecord(response.records[0]);
    } catch (requestError) {
      if (requestGeneration !== generation.current || controller.signal.aborted) return;
      setError(message(requestError, language));
      setDetails(rawError(requestError));
    } finally {
      if (requestGeneration === generation.current && !controller.signal.aborted) setBusy(null);
    }
  };

  return (
    <section className="project-dss-preview" aria-label={text(language, 'DSS 记录预览', 'DSS record preview')}>
      <div className="project-dss-preview-heading">
        <div><h4>{text(language, 'DSS 目录与记录', 'DSS catalog and records')}</h4><small title={path}>{path}</small></div>
        {catalog && <code title={catalog.revision}>{text(language, '源 SHA-256', 'Source SHA-256')} {catalog.revision}</code>}
      </div>
      {!canCatalog && <p className="project-form-error"><span>!</span>{text(language, '当前服务未提供 dss.catalog。', 'The connected service does not provide dss.catalog.')}</p>}
      {path && !path.toLowerCase().endsWith('.dss') && <p className="project-form-hint">{text(language, '此外部文件连接不是 DSS 文件。', 'This external file connection is not a DSS file.')}</p>}
      {busy === 'catalog' && <p className="project-tree-state"><span className="project-spinner" />{text(language, '正在读取 DSS 目录…', 'Loading the DSS catalog…')}</p>}
      {catalog && <>
        <div className="project-dss-record-picker">
          <label htmlFor="project-dss-record">{text(language, `记录（${catalog.records.length}）`, `Records (${catalog.records.length})`)}</label>
          <select id="project-dss-record" value={selectedPath} disabled={busy !== null || catalog.records.length === 0} onChange={(event) => { setSelectedPath(event.target.value); setRecord(null); }}>
            <option value="">{text(language, '选择真实 DSS 记录', 'Choose a source DSS record')}</option>
            {catalog.records.map((item) => <option key={item.path} value={item.path} title={item.path}>{item.path}</option>)}
          </select>
          <button type="button" className="project-button project-button-secondary" data-testid="project-dss-record-read" disabled={!canRead || busy !== null || !selectedPath} onClick={() => void readSelected()}>
            {busy === 'read' ? text(language, '正在读取…', 'Reading…') : text(language, '读取记录', 'Read record')}
          </button>
          {!canRead && <small>{text(language, '当前服务未提供 dss.read。', 'The connected service does not provide dss.read.')}</small>}
        </div>
        {selectedPath && <div className="project-dss-record-parts">
          {catalog.records.find((item) => item.path === selectedPath)?.parts.map((part, index) => <span key={`${index}:${part || 'empty'}`}>{String.fromCharCode(65 + index)}: {part || '—'}</span>)}
        </div>}
        {catalog.records.length === 0 && <p className="project-tree-state">{text(language, '服务返回的 DSS 目录为空。', 'The service returned an empty DSS catalog.')}</p>}
      </>}
      {record && <DssRecordDetails language={language} record={record} />}
      {error && <div className="project-form-error" role="alert"><span>!</span><div>{error}{details && <details><summary>{text(language, '原始诊断', 'Raw diagnostics')}</summary><pre>{details}</pre></details>}</div></div>}
    </section>
  );
}

function DssRecordDetails({ language, record }: { language: Language; record: DssRecordResult }) {
  const zh = language === 'zh';
  const series = record.timeSeries;
  const paired = record.pairedData;
  const rowLimit = 100;
  return (
    <div className="project-dss-record-result">
      <div className="project-dss-result-heading"><strong>{record.recordType}</strong><code>{record.path}</code></div>
      {record.error && <div className="project-form-error" role="status"><span>!</span>{record.error}</div>}
      {series && <>
        <p>{zh ? `时序记录：${series.values.length} 个源值；单位 ${series.units || '未提供'}；数据类型 ${series.dataType || '未提供'}` : `Time series: ${series.values.length} source values; units ${series.units || 'Not provided'}; data type ${series.dataType || 'Not provided'}`}</p>
        <div className="project-dss-table-scroll"><table><thead><tr><th>{zh ? '时间' : 'Time'}</th><th>{zh ? '值' : 'Value'}</th></tr></thead><tbody>
          {series.values.slice(0, rowLimit).map((value, index) => <tr key={`${series.times[index] ?? index}:${index}`}><td>{series.times[index] ?? '—'}</td><td>{valueText(value)}</td></tr>)}
        </tbody></table></div>
        {series.values.length > rowLimit && <small>{zh ? `仅显示前 ${rowLimit} 项；共 ${series.values.length} 项。` : `Showing the first ${rowLimit} of ${series.values.length} values.`}</small>}
        {series.qualities && <details><summary>{zh ? `源质量数据（${series.qualities.length} 项）` : `Source quality data (${series.qualities.length})`}</summary><pre>{JSON.stringify(series.qualities)}</pre></details>}
      </>}
      {paired && <>
        <p>{zh ? `配对数据：自变量 ${paired.independentType || '未提供'}（${paired.independentUnits || '未提供'}）；因变量 ${paired.dependentType || '未提供'}（${paired.dependentUnits || '未提供'}）` : `Paired data: independent ${paired.independentType || 'Not provided'} (${paired.independentUnits || 'Not provided'}); dependent ${paired.dependentType || 'Not provided'} (${paired.dependentUnits || 'Not provided'})`}</p>
        <div className="project-dss-table-scroll"><table><thead><tr><th>{paired.independentType || 'X'}</th>{paired.curves.map((curve, index) => <th key={`${curve.label}:${index}`}>{curve.label || `${zh ? '曲线' : 'Curve'} ${index + 1}`}</th>)}</tr></thead><tbody>
          {paired.ordinates.slice(0, rowLimit).map((ordinate, row) => <tr key={`${ordinate ?? 'missing'}:${row}`}><td>{valueText(ordinate)}</td>{paired.curves.map((curve, column) => <td key={`${curve.label}:${column}`}>{valueText(curve.values[row])}</td>)}</tr>)}
        </tbody></table></div>
        {paired.ordinates.length > rowLimit && <small>{zh ? `仅显示前 ${rowLimit} 行；共 ${paired.ordinates.length} 行。` : `Showing the first ${rowLimit} of ${paired.ordinates.length} rows.`}</small>}
      </>}
      {record.location && <details><summary>{zh ? '位置记录' : 'Location record'}</summary><dl>{Object.entries(record.location).map(([key, value]) => <div key={key}><dt>{key}</dt><dd>{String(value ?? '—')}</dd></div>)}</dl></details>}
      {!record.error && !series && !paired && !record.location && <p>{zh ? `服务未提供 ${record.kind} 类型的可视数据。` : `The service did not return preview data for record kind ${record.kind}.`}</p>}
    </div>
  );
}
