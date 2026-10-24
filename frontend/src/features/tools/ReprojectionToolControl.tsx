import { useEffect, useMemo, useRef, useState } from 'react';
import type { BackendGateway, Language, WorkbenchContext } from '../../shared/workbench';
import { message, text } from '../../shared/workbench';
import type { ProjectionMatch } from '../project/ProjectTypes';
import {
  createCoordinateRows,
  formatCoordinate,
  projectionOptionLabel,
  readProjectProjection,
  type CoordinateRowDraft,
  type ProjectionOption,
} from './geoprocessingUi';

interface ReprojectionToolControlProps {
  language: Language;
  context: WorkbenchContext;
  backend: BackendGateway;
  onClose: () => void;
  onDirtyStateChange?: (owner: string, dirty: boolean) => void;
}

interface ReprojectionRowResult {
  id: string;
  status: 'transformed' | 'blank' | 'invalid' | 'failed' | 'unavailable';
  x?: number | null;
  y?: number | null;
}

interface ReprojectionSnapshot {
  sourceSummary: string;
  destinationSummary: string;
  errorCode?: string | null;
  errorMessage?: string | null;
  rows: ReprojectionRowResult[];
  sourceProjectionSummary?: {
    projectionName?: string | null;
    validity?: string | null;
    unitName?: string | null;
    epsgCode?: string | null;
    utmZone?: string | null;
  } | null;
  destinationProjectionSummary?: {
    projectionName?: string | null;
    validity?: string | null;
    unitName?: string | null;
    epsgCode?: string | null;
    utmZone?: string | null;
  } | null;
}

interface ProjectionSideProps {
  side: 'source' | 'destination';
  language: Language;
  backend: BackendGateway;
  context: WorkbenchContext;
  option: ProjectionOption;
  customText: string;
  onOptionChange: (value: ProjectionOption) => void;
  onCustomTextChange: (value: string) => void;
}

function ProjectionSearch({ language, backend, onSelect }: {
  language: Language;
  backend: BackendGateway;
  onSelect: (code: string) => void;
}) {
  const [query, setQuery] = useState('');
  const [matches, setMatches] = useState<ProjectionMatch[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const searchGeneration = useRef(0);
  const controller = useRef<AbortController | null>(null);
  const canSearch = backend.supports('projection.search');

  useEffect(() => () => {
    searchGeneration.current += 1;
    controller.current?.abort();
  }, []);

  const search = async (): Promise<void> => {
    if (!canSearch || !query.trim()) return;
    const generation = ++searchGeneration.current;
    controller.current?.abort();
    const requestController = new AbortController();
    controller.current = requestController;
    setBusy(true);
    setError(undefined);
    try {
      const result = await backend.request<ProjectionMatch[]>(
        'projection.search', { query: query.trim(), limit: 30 }, { signal: requestController.signal },
      );
      if (generation === searchGeneration.current) setMatches(Array.isArray(result) ? result : []);
    } catch (requestError) {
      if (generation === searchGeneration.current && !requestController.signal.aborted) {
        setError(message(requestError, language));
        setMatches([]);
      }
    } finally {
      if (generation === searchGeneration.current) setBusy(false);
    }
  };

  return <div className="gp-ui-search-row">
    <input
      aria-label={text(language, '搜索 EPSG 坐标系', 'Search EPSG coordinate systems')}
      value={query}
      disabled={!canSearch || busy}
      placeholder={text(language, '搜索名称或 EPSG 代码', 'Search name or EPSG code')}
      onChange={event => setQuery(event.currentTarget.value)}
      onKeyDown={event => { if (event.key === 'Enter') { event.preventDefault(); void search(); } }}
    />
    <button className="gp-ui-button" type="button" disabled={!canSearch || busy || !query.trim()} onClick={() => void search()}>
      {busy ? '…' : text(language, '搜索', 'Search')}
    </button>
    {error && <div className="gp-ui-error" role="alert">{error}</div>}
    {matches.length > 0 && <ul className="gp-ui-search-results">
      {matches.map(match => <li key={match.id}>
        <button type="button" onClick={() => { onSelect(String(match.code)); setMatches([]); }}>
          <strong>{match.authority}:{match.code}</strong><span>{match.name}</span>
        </button>
      </li>)}
    </ul>}
    {!canSearch && <p className="gp-ui-hint">{text(language, '投影目录搜索尚未接入；可直接输入 EPSG 代码或 WKT / PROJ 文本。', 'Projection search is unavailable; enter an EPSG code or WKT / PROJ text directly.')}</p>}
  </div>;
}

function ProjectionSide({ side, language, backend, context, option, customText, onOptionChange, onCustomTextChange }: ProjectionSideProps) {
  const [identifyBusy, setIdentifyBusy] = useState(false);
  const [fileName, setFileName] = useState('');
  const [error, setError] = useState<string>();
  const identifyController = useRef<AbortController | null>(null);
  const canIdentify = backend.supports('reprojection.identify');
  const path = context.selectedAsset?.path;
  const currentPath = useRef(path);
  const label = side === 'source' ? text(language, '源坐标系', 'Source projection') : text(language, '目标坐标系', 'Destination projection');

  useEffect(() => {
    currentPath.current = path;
    identifyController.current?.abort();
    setIdentifyBusy(false);
    setError(undefined);
  }, [path]);

  useEffect(() => () => identifyController.current?.abort(), []);

  const identifySelectedFile = async (): Promise<void> => {
    if (!path || !canIdentify) return;
    identifyController.current?.abort();
    const controller = new AbortController();
    identifyController.current = controller;
    setIdentifyBusy(true);
    setError(undefined);
    try {
      const result = await backend.request<{
        recognizedPath?: string | null;
        wkt?: string | null;
        projectionSummary?: { projectionName?: string | null } | null;
        errorMessage?: string | null;
      }>('reprojection.identify', { paths: [path] }, { signal: controller.signal });
      if (controller.signal.aborted || currentPath.current !== path) return;
      if (result.recognizedPath !== path) throw new Error(text(language, '投影识别回执没有指向请求的资产路径。', 'The projection identification response does not refer to the requested asset path.'));
      if (!result.wkt) throw new Error(result.errorMessage || text(language, '后端未能从当前资产识别出投影。', 'The backend could not identify a projection from the selected asset.'));
      onOptionChange('Custom');
      onCustomTextChange(result.wkt);
      setError(undefined);
    } catch (requestError) {
      if (!controller.signal.aborted) setError(message(requestError, language));
    } finally {
      if (!controller.signal.aborted) setIdentifyBusy(false);
    }
  };

  const readProjectionFile = async (file: File | undefined): Promise<void> => {
    if (!file) return;
    setError(undefined);
    try {
      const value = await file.text();
      if (!value.trim()) throw new Error(text(language, '投影文件为空。', 'The projection file is empty.'));
      setFileName(file.name);
      onOptionChange('Custom');
      onCustomTextChange(value);
    } catch (requestError) {
      setError(message(requestError, language));
    }
  };

  return <section className="gp-ui-panel" aria-label={label}>
    <div className="gp-ui-panel-heading"><h3>{label}</h3><small>{side === 'source' ? 'SourceProjectionOption' : 'DestProjectionOption'}</small></div>
    <label className="gp-ui-field">
      <span>{text(language, '坐标参考', 'Coordinate reference')}</span>
      <select value={option} onChange={event => {
        const next = event.currentTarget.value as ProjectionOption;
        onOptionChange(next);
        if (next !== 'Custom') onCustomTextChange('');
        setError(undefined);
      }}>
        {(['Project', 'WGS84', 'WebMercator', 'Custom'] as ProjectionOption[]).map(value =>
          <option value={value} key={value}>{projectionOptionLabel(value, language)}</option>)}
      </select>
    </label>
    {option === 'Project' && <p className="gp-ui-hint">
      {context.projectPath
        ? text(language, '工程投影来自当前 .ras 文件的 Projection 定义。', 'The project projection is read from the current .ras Projection definition.')
        : text(language, '当前没有打开工程；服务端会返回“无工程投影”，可切换到 WGS84、Web Mercator 或自定义。', 'No project is open. The server will report that the project projection is unavailable; choose WGS84, Web Mercator, or Custom.')}
    </p>}
    {option === 'Custom' && <>
      <label className="gp-ui-field">
        <span>{text(language, 'EPSG 代码、WKT 或 PROJ 字符串', 'EPSG code, WKT, or PROJ string')}</span>
        <textarea rows={3} value={customText} onChange={event => onCustomTextChange(event.currentTarget.value)} placeholder="例如 26910、WKT[…] 或 +proj=…" />
      </label>
      <ProjectionSearch language={language} backend={backend} onSelect={code => onCustomTextChange(code)} />
      <div className="gp-ui-projection-actions">
        <label className="gp-ui-file-read">
          {text(language, '读取本机 .prj 文本', 'Read a local .prj file')}
          <input type="file" accept=".prj,text/plain" hidden onChange={event => { void readProjectionFile(event.currentTarget.files?.[0]); event.currentTarget.value = ''; }} />
        </label>
        <button className="gp-ui-button" type="button" disabled={!path || !canIdentify || identifyBusy} onClick={() => void identifySelectedFile()}>
          {identifyBusy ? text(language, '识别中…', 'Identifying…') : text(language, '从当前资产读取投影', 'Identify from selected asset')}
        </button>
        {!canIdentify && <span className="gp-ui-hint">{text(language, 'reprojection.identify 未注册。', 'reprojection.identify is not registered.')}</span>}
      </div>
      {fileName && <p className="gp-ui-hint">{text(language, '已读取：', 'Loaded: ')}{fileName}</p>}
      {path && <p className="gp-ui-hint" title={path}>{text(language, '当前资产路径：', 'Selected asset path: ')}{path}</p>}
    </>}
    {error && <div className="gp-ui-error" role="alert">{error}</div>}
  </section>;
}

function parseSnapshot(value: unknown, expectedIDs: Set<string>): ReprojectionSnapshot {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('The reprojection response is not an object.');
  const raw = value as Record<string, unknown>;
  if (!Array.isArray(raw.rows) || typeof raw.sourceSummary !== 'string' || typeof raw.destinationSummary !== 'string') {
    throw new Error('The reprojection response is missing its row or projection summary.');
  }
  const rows: ReprojectionRowResult[] = raw.rows.map(item => {
    if (!item || typeof item !== 'object' || Array.isArray(item)) throw new Error('The reprojection response contains an invalid coordinate row.');
    const row = item as Record<string, unknown>;
    if (typeof row.id !== 'string' || !expectedIDs.has(row.id) ||
        !['transformed', 'blank', 'invalid', 'failed', 'unavailable'].includes(String(row.status))) {
      throw new Error('The reprojection response changed a coordinate row identity or status.');
    }
    return { id: row.id, status: row.status as ReprojectionRowResult['status'],
      x: typeof row.x === 'number' ? row.x : null, y: typeof row.y === 'number' ? row.y : null };
  });
  if (rows.length !== expectedIDs.size || new Set(rows.map(row => row.id)).size !== expectedIDs.size) {
    throw new Error('The reprojection response did not preserve all coordinate row identities.');
  }
  return {
    sourceSummary: raw.sourceSummary,
    destinationSummary: raw.destinationSummary,
    errorCode: typeof raw.errorCode === 'string' ? raw.errorCode : null,
    errorMessage: typeof raw.errorMessage === 'string' ? raw.errorMessage : null,
    rows,
    sourceProjectionSummary: raw.sourceProjectionSummary as ReprojectionSnapshot['sourceProjectionSummary'],
    destinationProjectionSummary: raw.destinationProjectionSummary as ReprojectionSnapshot['destinationProjectionSummary'],
  };
}

function rowStatusLabel(status: ReprojectionRowResult['status'] | undefined, language: Language): string {
  if (!status) return text(language, '待转换', 'Pending');
  const labels: Record<ReprojectionRowResult['status'], [string, string]> = {
    transformed: ['已转换', 'Transformed'], blank: ['空行', 'Blank'], invalid: ['输入无效', 'Invalid input'],
    failed: ['转换失败', 'Failed'], unavailable: ['不可用', 'Unavailable'],
  };
  return labels[status][language === 'zh' ? 0 : 1];
}

export function ReprojectionToolControl({ language, context, backend, onClose, onDirtyStateChange }: ReprojectionToolControlProps) {
  const zh = language === 'zh';
  const projectProjection = useMemo(() => readProjectProjection(context.projectXml), [context.projectXml]);
  const [sourceOption, setSourceOption] = useState<ProjectionOption>('WGS84');
  const [destinationOption, setDestinationOption] = useState<ProjectionOption>('Project');
  const [sourceCustomText, setSourceCustomText] = useState('');
  const [destinationCustomText, setDestinationCustomText] = useState('');
  const [rows, setRows] = useState<CoordinateRowDraft[]>(() => createCoordinateRows(10));
  const [snapshot, setSnapshot] = useState<ReprojectionSnapshot>();
  const [requestError, setRequestError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [cancelled, setCancelled] = useState(false);
  const controller = useRef<AbortController | null>(null);
  const generation = useRef(0);
  const canTransform = backend.supports('reprojection.transform');
  const dirty = rows.some(row => row.xText !== '' || row.yText !== '') || sourceCustomText !== '' || destinationCustomText !== '' || sourceOption !== 'WGS84' || destinationOption !== 'Project';
  const returnedRows = new Map((snapshot?.rows ?? []).map(row => [row.id, row]));

  useEffect(() => {
    onDirtyStateChange?.('tools.reprojection', dirty);
    return () => onDirtyStateChange?.('tools.reprojection', false);
  }, [dirty, onDirtyStateChange]);

  useEffect(() => () => {
    generation.current += 1;
    controller.current?.abort();
  }, []);

  useEffect(() => {
    const hasCoordinates = rows.some(row => row.xText.trim() || row.yText.trim());
    if (!hasCoordinates) {
      controller.current?.abort();
      setSnapshot(undefined);
      setRequestError(undefined);
      setBusy(false);
      setCancelled(false);
      return;
    }
    if (!canTransform) {
      setSnapshot(undefined);
      setRequestError(text(language, '当前 C# 服务未注册 reprojection.transform。', 'The current C# service has not registered reprojection.transform.'));
      setBusy(false);
      return;
    }

    const requestGeneration = ++generation.current;
    controller.current?.abort();
    const requestController = new AbortController();
    controller.current = requestController;
    setSnapshot(undefined);
    setRequestError(undefined);
    setCancelled(false);
    const requestRows = rows.map(row => ({ id: row.id, xText: row.xText || null, yText: row.yText || null }));
    const request = {
      sourceOption,
      destinationOption,
      projectProjection,
      sourceCustomProjectionText: sourceOption === 'Custom' ? sourceCustomText : null,
      destinationCustomProjectionText: destinationOption === 'Custom' ? destinationCustomText : null,
      rows: requestRows,
      numberCulture: zh ? 'zh-CN' : 'en-US',
    };
    const timer = window.setTimeout(() => {
      setBusy(true);
      void backend.request<unknown>('reprojection.transform', request, { signal: requestController.signal })
        .then(value => {
          if (requestGeneration !== generation.current || requestController.signal.aborted) return;
          setSnapshot(parseSnapshot(value, new Set(requestRows.map(row => row.id))));
        })
        .catch(error => {
          if (requestGeneration !== generation.current || requestController.signal.aborted) return;
          setRequestError(message(error, language));
        })
        .finally(() => {
          if (requestGeneration === generation.current && !requestController.signal.aborted) setBusy(false);
        });
    }, 220);
    return () => {
      window.clearTimeout(timer);
      requestController.abort();
    };
  }, [backend, canTransform, destinationCustomText, destinationOption, language, projectProjection, rows, sourceCustomText, sourceOption, zh]);

  const updateRow = (id: string, key: 'xText' | 'yText', value: string): void => {
    setRows(current => current.map(row => row.id === id ? { ...row, [key]: value } : row));
  };

  const addRows = (): void => setRows(current => [...current, ...createCoordinateRows(5)]);

  const cancelTransform = (): void => {
    generation.current += 1;
    controller.current?.abort();
    setBusy(false);
    setSnapshot(undefined);
    setRequestError(undefined);
    setCancelled(true);
  };

  const sourceSummary = snapshot?.sourceProjectionSummary;
  const destinationSummary = snapshot?.destinationProjectionSummary;

  return <section className="gp-ui-reprojection" aria-labelledby="gp-ui-reprojection-title">
    <header className="gp-ui-reprojection-header">
      <div>
        <span className="tools-kicker">{zh ? '投影转换' : 'REPROJECTION'}</span>
        <h2 id="gp-ui-reprojection-title">{zh ? '坐标批量转换' : 'Coordinate reprojection'}</h2>
        <p>{zh ? '逐行输入 X / Y；结果由 C# 投影服务返回，行身份沿用原始表格。' : 'Enter X / Y coordinates by row. The C# projection service returns results using the original row identities.'}</p>
      </div>
      <button className="gp-ui-button" type="button" onClick={onClose}>{zh ? '返回地理处理' : 'Back to geoprocessing'}</button>
    </header>

    <div className="gp-ui-reprojection-grid">
      <ProjectionSide side="source" language={language} backend={backend} context={context} option={sourceOption} customText={sourceCustomText}
        onOptionChange={setSourceOption} onCustomTextChange={setSourceCustomText} />
      <ProjectionSide side="destination" language={language} backend={backend} context={context} option={destinationOption} customText={destinationCustomText}
        onOptionChange={setDestinationOption} onCustomTextChange={setDestinationCustomText} />
    </div>

    {!canTransform && <div className="gp-ui-warning" role="status">{zh ? '坐标表可编辑，但 reprojection.transform 未注册，因此不会显示模拟转换结果。' : 'Coordinates remain editable, but reprojection.transform is not registered, so no simulated result is shown.'}</div>}
    {snapshot?.errorMessage && <div className="gp-ui-warning" role="status">{zh ? '后端投影诊断：' : 'Backend projection diagnostic: '}{snapshot.errorMessage}{snapshot.errorCode ? ` (${snapshot.errorCode})` : ''}</div>}
    {requestError && <div className="gp-ui-error" role="alert">{zh ? '转换请求失败：' : 'Transform request failed: '}{requestError}</div>}
    {cancelled && <div className="gp-ui-status" role="status">{zh ? '已请求取消转换。' : 'Transform cancellation was requested.'}</div>}

    <section className="gp-ui-coordinate-panel" aria-label={zh ? '坐标表' : 'Coordinate table'}>
      <div className="gp-ui-panel-heading">
        <h3>{zh ? '坐标表' : 'Coordinate rows'}</h3>
        <small>{snapshot ? `${snapshot.sourceSummary} → ${snapshot.destinationSummary}` : text(language, '修改后自动调用后端', 'Transforms automatically after edits')}</small>
      </div>
      {(sourceSummary || destinationSummary) && <p className="gp-ui-hint">
        {[sourceSummary?.projectionName, sourceSummary?.epsgCode && `EPSG:${sourceSummary.epsgCode}`, sourceSummary?.unitName,
          destinationSummary?.projectionName, destinationSummary?.epsgCode && `EPSG:${destinationSummary.epsgCode}`, destinationSummary?.unitName]
          .filter(Boolean).join(' · ')}
      </p>}
      <div className="gp-ui-table-wrap">
        <table className="gp-ui-table">
      <thead><tr><th>#</th><th>SourceX</th><th>SourceY</th><th>DestX</th><th>DestY</th><th>{zh ? '状态' : 'Status'}</th></tr></thead>
          <tbody>{rows.map((row, index) => {
            const result = returnedRows.get(row.id);
            return <tr key={row.id} data-row-id={row.id}>
              <td>{index + 1}</td>
              <td><input aria-label={`${zh ? '第' : 'Row '}${index + 1}${zh ? '行源 X' : ' source X'}`} inputMode="decimal" value={row.xText} onChange={event => updateRow(row.id, 'xText', event.currentTarget.value)} /></td>
              <td><input aria-label={`${zh ? '第' : 'Row '}${index + 1}${zh ? '行源 Y' : ' source Y'}`} inputMode="decimal" value={row.yText} onChange={event => updateRow(row.id, 'yText', event.currentTarget.value)} /></td>
              <td>{formatCoordinate(result?.x, language)}</td><td>{formatCoordinate(result?.y, language)}</td>
              <td><span className="gp-ui-row-status" data-status={result?.status ?? 'pending'}>{busy ? text(language, '转换中…', 'Transforming…') : rowStatusLabel(result?.status, language)}</span></td>
            </tr>;
          })}</tbody>
        </table>
      </div>
      <div className="gp-ui-coordinate-actions">
        <button className="gp-ui-button" type="button" onClick={addRows}>{zh ? '添加 5 行' : 'Add 5 rows'}</button>
        {busy && <button className="gp-ui-button gp-ui-button-danger" type="button" onClick={cancelTransform}>{zh ? '取消转换' : 'Cancel transform'}</button>}
        <span className="gp-ui-hint">{zh ? '初始 10 行；按原 ReprojectionToolVM 的 AddRowsCommand 每次增加 5 行。' : 'Starts with 10 rows and adds 5 rows per the original ReprojectionToolVM AddRowsCommand.'}</span>
      </div>
    </section>
    {snapshot && !snapshot.errorMessage && <div className="gp-ui-success" role="status">
      {zh ? `后端返回 ${snapshot.rows.filter(row => row.status === 'transformed').length} 行已转换坐标。该工具只生成坐标表结果，不会写入文件。` : `The backend returned ${snapshot.rows.filter(row => row.status === 'transformed').length} transformed coordinate rows. This tool returns a coordinate table and does not write a file.`}
    </div>}
    {snapshot && <details className="gp-ui-status"><summary>{zh ? '坐标转换回执' : 'Transform response'}</summary><pre>{JSON.stringify(snapshot, null, 2)}</pre></details>}
  </section>;
}
