import { useEffect, useRef, useState } from 'react';
import type { BackendGateway, Language } from '../../shared/workbench';
import { errorDiagnostics, message, text } from '../../shared/workbench';
import { DssCatalogViewer } from './dss/DssCatalogViewer';
import { LocationInformationView } from './dss/LocationInformationView';
import { PairedDataDataTableView } from './dss/PairedDataDataTableView';
import { PairedDataGraphView } from './dss/PairedDataGraphView';
import { TimeSeriesDataTableView } from './dss/TimeSeriesDataTableView';
import { TimeSeriesGraph } from './dss/TimeSeriesGraph';
import { TimeSeriesGraphWindow } from './dss/TimeSeriesGraphWindow';
import type { DssCatalogResponse, DssReadResponse, DssRecordResult } from './dss/types';
import './dss/dss.css';

interface Props {
  language: Language;
  backend: BackendGateway;
  path: string;
}

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

function DssRecordPreview({ language, record, sourceRevision }: { language: Language; record: DssRecordResult; sourceRevision: string }) {
  const zh = language === 'zh';
  const hasPreview = !!record.timeSeries || !!record.pairedData || !!record.location;

  return <div className="project-dss-record-result">
    <div className="project-dss-result-heading"><strong>{record.recordType}</strong><code>{record.path}</code></div>
    {record.error && <div className="project-form-error" role="status"><span>!</span>{record.error}</div>}
    {record.timeSeries && <>
      <TimeSeriesGraph data={record.timeSeries} pathname={record.path} language={language} />
      <TimeSeriesGraphWindow data={record.timeSeries} pathname={record.path} sourceRevision={sourceRevision} language={language} />
      <TimeSeriesDataTableView data={record.timeSeries} pathname={record.path} sourceRevision={sourceRevision} language={language} />
    </>}
    {record.pairedData && <>
      <PairedDataGraphView data={record.pairedData} pathname={record.path} language={language} />
      <PairedDataDataTableView data={record.pairedData} pathname={record.path} sourceRevision={sourceRevision} language={language} />
    </>}
    {record.location && <LocationInformationView location={record.location} language={language} />}
    {!record.error && !hasPreview && <p role="status">{record.kind === 'unsupported'
      ? (zh ? `此预览不支持 ${record.recordType} 类型的图表或表格。` : `This preview does not support a graph or table for ${record.recordType}.`)
      : (zh ? `Host 未为 ${record.kind} 返回可预览数据。` : `The host returned no preview data for ${record.kind}.`)}</p>}
  </div>;
}

export function DssExternalConnectionPreview({ language, backend, path }: Props) {
  const canCatalog = backend.supports('dss.catalog');
  const canRead = backend.supports('dss.read');
  const [catalog, setCatalog] = useState<DssCatalogResponse | null>(null);
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
      if (!response || !sameServerPath(response.path, path) || !isSha256(response.revision) || !Array.isArray(response.records) ||
          response.records.some((item) => !item || typeof item.path !== 'string' || !item.path || typeof item.recordType !== 'string' ||
            !Array.isArray(item.parts) || item.parts.length !== 6 || item.parts.some((part) => typeof part !== 'string')) ||
          new Set(response.records.map((item) => item.path)).size !== response.records.length) {
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
      abort.current?.abort();
      abort.current = undefined;
    };
  }, [backend, canCatalog, language, path]);

  const selectRecord = (nextPath: string): void => {
    if (nextPath === selectedPath) return;
    generation.current += 1;
    abort.current?.abort();
    abort.current = undefined;
    setBusy(null);
    setSelectedPath(nextPath);
    setRecord(null);
    setError(undefined);
    setDetails(undefined);
  };

  const readSelected = async (): Promise<void> => {
    if (!catalog || !selectedPath || !canRead) return;
    const source = catalog;
    const recordPath = selectedPath;
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
        path: source.path,
        revision: source.revision,
        recordPaths: [recordPath],
      }, { signal: controller.signal });
      if (requestGeneration !== generation.current || controller.signal.aborted) return;
      if (!response || !sameServerPath(response.path, source.path) || response.revision.toLowerCase() !== source.revision.toLowerCase() ||
          !Array.isArray(response.records) || response.records.length !== 1 || response.records[0]?.path !== recordPath) {
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

  return <section className="project-dss-preview" aria-label={text(language, 'DSS 记录预览', 'DSS record preview')}>
    <div className="project-dss-preview-heading">
      <div><h4>{text(language, 'DSS 目录与记录', 'DSS catalog and records')}</h4><small title={path}>{path}</small></div>
      {catalog && <code title={catalog.revision}>{text(language, '源 SHA-256', 'Source SHA-256')} {catalog.revision}</code>}
    </div>
    <p className="project-dss-version-note">{text(language, '此预览使用当前 DSS 7 服务读取链路；DSS 6 文件尚未接通。', 'This preview uses the current DSS 7 service path; DSS 6 files are not supported.')}</p>
    {!canCatalog && <p className="project-form-error"><span>!</span>{text(language, '当前服务未提供 dss.catalog。', 'The connected service does not provide dss.catalog.')}</p>}
    {path && !path.toLowerCase().endsWith('.dss') && <p className="project-form-hint">{text(language, '此外部文件连接不是 DSS 文件。', 'This external file connection is not a DSS file.')}</p>}
    {busy === 'catalog' && <p className="project-tree-state"><span className="project-spinner" />{text(language, '正在读取 DSS 目录…', 'Loading the DSS catalog…')}</p>}
    {catalog && <DssCatalogViewer
      records={catalog.records}
      selectedPath={selectedPath}
      language={language}
      busy={busy !== null}
      canRead={canRead}
      onSelectedPathChange={selectRecord}
      onRead={() => void readSelected()}
    />}
    {catalog && record && <DssRecordPreview key={`${catalog.path}:${catalog.revision}:${record.path}`} language={language} record={record} sourceRevision={catalog.revision} />}
    {error && <div className="project-form-error" role="alert"><span>!</span><div>{error}{details && <details><summary>{text(language, '原始诊断', 'Raw diagnostics')}</summary><pre>{details}</pre></details>}</div></div>}
  </section>;
}
