import { useEffect, useMemo, useRef, useState } from 'react';
import type { BackendGateway, Language, WorkbenchContext } from '../../../shared/workbench';
import { errorDiagnostics, message, text } from '../../../shared/workbench';
import { rgbaFrameImage } from '../../map/mapImages';
import type { MapDocumentTab } from '../../map/mapTypes';
import {
  exportContextKey,
  findRasterCandidates,
  isRecord,
  isServerAbsolutePath,
  loadExportMapDocuments,
  normalizeServerPath,
  parseRgbInspection,
  parseRgbPreview,
  parseRgbReceipt,
  pathFileName,
  rasterSourceIdentity,
  type RasterCandidate,
  type RasterPlotOption,
  type RgbRasterInspection,
  type RgbRasterPreview,
  type RgbRasterReceipt,
} from './exportWorkflow';

interface Props {
  backend: BackendGateway;
  context: WorkbenchContext;
  language: Language;
  onBusyStateChange?: (busy: boolean) => void;
}

type Phase = 'catalog' | 'inspect' | 'preview' | 'execute' | 'cancelling' | 'uncertain' | null;
interface Progress { fraction: number | null; label: string }
interface InspectedSource {
  candidateKey: string;
  ownerKey: string;
  source: ReturnType<typeof rasterSourceIdentity> & { sourceRevision: string };
  value: RgbRasterInspection;
}

const inspectMethod = 'rasterExport.inspect';
const previewMethod = 'rasterExport.preview';
const executeMethod = 'rasterExport.execute';

export function RgbTiffExporterControl({ backend, context, language, onBusyStateChange }: Props) {
  const contextKey = exportContextKey(context);
  const contextKeyRef = useRef(contextKey);
  contextKeyRef.current = contextKey;
  const requestGeneration = useRef(0);
  const activeRequest = useRef<{ controller: AbortController; id: number } | null>(null);
  const propsRef = useRef({ contextKey, scopeKey: '', backend, onBusyStateChange });
  propsRef.current.contextKey = contextKey;
  propsRef.current.backend = backend;
  propsRef.current.onBusyStateChange = onBusyStateChange;

  const [documents, setDocuments] = useState<MapDocumentTab[]>([]);
  const [projectXmlSHA, setProjectXmlSHA] = useState<string | null>(null);
  const [candidateKey, setCandidateKey] = useState('');
  const [inspection, setInspection] = useState<InspectedSource | null>(null);
  const [cellSizeText, setCellSizeText] = useState('');
  const [region, setRegion] = useState<'FullLayer' | 'CurrentView'>('FullLayer');
  const [viewValues, setViewValues] = useState({ minX: '', minY: '', maxX: '', maxY: '', pixelWidth: '640', pixelHeight: '480' });
  const [plotValues, setPlotValues] = useState<Record<string, string>>({});
  const [outputPath, setOutputPath] = useState('');
  const [preview, setPreview] = useState<RgbRasterPreview | null>(null);
  const [previewRequestKey, setPreviewRequestKey] = useState('');
  const [previewImage, setPreviewImage] = useState('');
  const [receipt, setReceipt] = useState<RgbRasterReceipt | null>(null);
  const [phase, setPhase] = useState<Phase>('catalog');
  const [progress, setProgress] = useState<Progress | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [status, setStatus] = useState('');
  const [diagnostics, setDiagnostics] = useState<unknown>(null);

  const candidates = useMemo(() => findRasterCandidates(documents), [documents]);
  const selectedCandidate = candidates.find(candidate => rasterCandidateKey(candidate) === candidateKey) ?? null;
  const selectedDocument = selectedCandidate?.document ?? null;
  const activePlotOptions = inspection?.value.plotOptions ?? [];
  const activePlotValues = enabledPlotValues(activePlotOptions, plotValues);
  const sourceOwnerKey = inspection ? JSON.stringify([contextKey, inspection.candidateKey,
    inspection.source.visualizationRevision, inspection.value.sourceRevision]) : '';
  const requestScopeKey = JSON.stringify([contextKey, candidateKey, region, viewValues, cellSizeText, plotValues, outputPath]);
  propsRef.current.scopeKey = requestScopeKey;
  const inspectionIsCurrent = Boolean(inspection && inspection.ownerKey === sourceOwnerKey && inspection.candidateKey === candidateKey);

  useEffect(() => {
    const generation = ++requestGeneration.current;
    const controller = new AbortController();
    activeRequest.current?.controller.abort('Project export context changed.');
    activeRequest.current = { controller, id: generation };
    setDocuments([]);
    setProjectXmlSHA(null);
    setCandidateKey('');
    setInspection(null);
    setCellSizeText('');
    setRegion('FullLayer');
    setViewValues({ minX: '', minY: '', maxX: '', maxY: '', pixelWidth: '640', pixelHeight: '480' });
    setPlotValues({});
    setOutputPath('');
    setPreview(null);
    setPreviewRequestKey('');
    setPreviewImage('');
    setReceipt(null);
    setError(null);
    setDiagnostics(null);
    setStatus('');
    setPhase('catalog');
    void loadExportMapDocuments(backend, context, controller.signal).then(value => {
      if (controller.signal.aborted || generation !== requestGeneration.current || contextKeyRef.current !== contextKey) return;
      setDocuments(value.documents);
      setProjectXmlSHA(value.projectXmlSHA);
      setPhase(null);
      const found = findRasterCandidates(value.documents);
      if (found.length > 0) setCandidateKey(rasterCandidateKey(found[0]));
      setStatus(found.length > 0
        ? text(language, `Host 地图目录中有 ${found.length} 个栅格图层。`, `The Host map catalog contains ${found.length} raster layers.`)
        : text(language, 'Host 地图目录中没有可导出的栅格图层。', 'The Host map catalog has no raster layers to export.'));
    }).catch(caught => {
      if (controller.signal.aborted || generation !== requestGeneration.current || contextKeyRef.current !== contextKey) return;
      setError(caught);
      setDiagnostics(errorDiagnostics(caught));
      setPhase(null);
    });
    return () => {
      controller.abort('RGB exporter context changed or closed.');
      if (activeRequest.current?.controller === controller) activeRequest.current = null;
      if (requestGeneration.current === generation) requestGeneration.current += 1;
    };
  }, [backend, context, contextKey]);

  useEffect(() => () => {
    requestGeneration.current += 1;
    activeRequest.current?.controller.abort('RGB exporter closed.');
    activeRequest.current = null;
  }, []);

  useEffect(() => {
    setInspection(null);
    setCellSizeText('');
    setPlotValues({});
    setPreview(null);
    setPreviewRequestKey('');
    setPreviewImage('');
    setReceipt(null);
    setError(null);
    setDiagnostics(null);
    setStatus('');
  }, [candidateKey]);

  useEffect(() => {
    if (!preview) { setPreviewImage(''); return; }
    try { setPreviewImage(rgbaFrameImage(preview.raster.rgba, preview.raster.width, preview.raster.height)); }
    catch (caught) { setPreviewImage(''); setError(caught); setDiagnostics(errorDiagnostics(caught)); }
  }, [preview]);

  useEffect(() => {
    propsRef.current.onBusyStateChange?.(phase !== null && phase !== 'uncertain');
  }, [phase, onBusyStateChange]);

  const isCurrent = (id: number, scope: string): boolean => requestGeneration.current === id
    && contextKeyRef.current === contextKey && propsRef.current.contextKey === contextKey
    && propsRef.current.scopeKey === scope;

  const begin = (next: Exclude<Phase, null | 'cancelling' | 'uncertain'>) => {
    activeRequest.current?.controller.abort('A newer RGB export request started.');
    const controller = new AbortController();
    const id = ++requestGeneration.current;
    activeRequest.current = { controller, id };
    setPhase(next);
    setError(null);
    setDiagnostics(null);
    setProgress(null);
    return { controller, id, scope: propsRef.current.scopeKey };
  };

  const makeCurrentView = () => {
    if (region === 'FullLayer') return null;
    const numbers = Object.fromEntries(Object.entries(viewValues).map(([key, value]) => [key, Number(value)])) as Record<keyof typeof viewValues, number>;
    if (Object.values(viewValues).some(value => !value.trim()) || Object.values(numbers).some(value => !Number.isFinite(value))) {
      throw new Error(text(language, '请输入完整的范围坐标和像素大小。', 'Enter all extent coordinates and pixel dimensions.'));
    }
    if (numbers.maxX <= numbers.minX || numbers.maxY <= numbers.minY || !Number.isInteger(numbers.pixelWidth) ||
        !Number.isInteger(numbers.pixelHeight) || numbers.pixelWidth < 1 || numbers.pixelHeight < 1 || numbers.pixelWidth * numbers.pixelHeight > 2_097_152) {
      throw new Error(text(language, '指定范围必须递增，像素尺寸为正整数且总像素不超过 Host 限制。', 'The extent must be ordered and pixel dimensions must be positive integers within the Host limit.'));
    }
    return numbers;
  };

  const loadGdbManifest = async (candidate: RasterCandidate, controller: AbortController): Promise<Record<string, unknown>> => {
    if (!backend.supports('map.gdb.catalog')) throw new Error(text(language, '当前 C# Host 未发布 File Geodatabase 目录接口。', 'The C# Host has not advertised File Geodatabase catalog access.'));
    const sourcePath = candidate.node.assetID;
    const expectedSourceRevision = candidate.node.diagnosticSourceRevision;
    if (!sourcePath) throw new Error('Host 地图树没有提供 GDB 路径。');
    if (expectedSourceRevision && !/^[\da-f]{64}$/i.test(expectedSourceRevision)) throw new Error('Host 地图树中的 GDB 修订格式无效。');
    const raw = requireRecord(await backend.request<unknown>('map.gdb.catalog', {
      sourcePath,
      ...(expectedSourceRevision ? { expectedSourceRevision } : {}),
    }, { signal: controller.signal }), 'map.gdb.catalog 响应');
    const source = requireRecord(raw.source, 'GDB 源清单');
    if (source.sourcePath !== sourcePath || typeof source.revision !== 'string' || !/^[\da-f]{64}$/i.test(source.revision) ||
        expectedSourceRevision && source.revision.toUpperCase() !== expectedSourceRevision.toUpperCase() || !Array.isArray(source.files)) {
      throw new Error('Host 返回的 GDB 文件清单与地图树来源修订不匹配。');
    }
    const matches = Array.isArray(raw.rasterSubdatasets) ? raw.rasterSubdatasets.filter(value => isRecord(value) && value.connectionString === candidate.locator.filename) : [];
    if (matches.length !== 1) throw new Error('GDB 栅格节点未匹配到唯一的 Host 子数据集。');
    const child = matches[0] as Record<string, unknown>;
    if (child.status !== 'available' || child.canRender !== true || child.mapType !== candidate.locator.mapType ||
        !Number.isInteger(child.width) || !Number.isInteger(child.height)) {
      throw new Error('Host 目录报告的 GDB 栅格子数据集不可渲染或与地图类型不符。');
    }
    return source;
  };

  const inspect = async (): Promise<void> => {
    setInspection(null);
    setPreview(null);
    setPreviewRequestKey('');
    setPreviewImage('');
    setReceipt(null);
    setStatus('');
    if (!selectedCandidate || !projectXmlSHA || !context.projectPath) {
      setError(new Error(text(language, '请选择已保存地图目录中的栅格图层。', 'Choose a raster layer from a saved map catalog.')));
      return;
    }
    if (!backend.supports(inspectMethod)) {
      setError(new Error(text(language, '当前 C# Host 未发布 RGB TIFF 检查接口。', 'The C# Host has not advertised RGB TIFF inspection.')));
      return;
    }
    const request = begin('inspect');
    try {
      const view = makeCurrentView();
      const gdbSource = selectedCandidate.gdb ? await loadGdbManifest(selectedCandidate, request.controller) : undefined;
      if (!isCurrent(request.id, request.scope)) return;
      const sourceIdentity = rasterSourceIdentity(context, selectedCandidate.document, selectedCandidate, projectXmlSHA, gdbSource);
      const result = parseRgbInspection(await backend.request<unknown>(inspectMethod, {
        source: sourceIdentity,
        projectXml: context.projectXml,
        currentView: view,
      }, { signal: request.controller.signal }));
      if (!isCurrent(request.id, request.scope)) return;
      if (result.sourceID !== sourceIdentity.id ||
          (sourceIdentity.sourceRevision !== null && result.sourceRevision.toUpperCase() !== sourceIdentity.sourceRevision.toUpperCase()) ||
          result.visualizationRevision.toUpperCase() !== sourceIdentity.visualizationRevision.toUpperCase()) {
        throw new Error('RGB TIFF 检查响应与地图栅格源身份或修订不匹配。');
      }
      const source = { ...sourceIdentity, sourceRevision: result.sourceRevision, visualizationRevision: result.visualizationRevision };
      setInspection({ candidateKey, ownerKey: JSON.stringify([contextKey, candidateKey, source.visualizationRevision, source.sourceRevision]), source, value: result });
      setCellSizeText(String(result.suggestedCellSize ?? ''));
      setPlotValues(Object.fromEntries(result.plotOptions.map(option => [option.propertyName, option.value])));
      setStatus(text(language, `已核对栅格修订 ${result.sourceRevision}，全图范围已由 C# Host 返回。`, `Verified raster revision ${result.sourceRevision}; full-layer bounds came from the C# Host.`));
    } catch (caught) {
      if (!isCurrent(request.id, request.scope)) return;
      setError(caught);
      setDiagnostics(errorDiagnostics(caught));
    } finally {
      if (requestGeneration.current === request.id) { activeRequest.current = null; setPhase(null); }
    }
  };

  const currentRasterRequest = () => {
    if (!inspection || !inspectionIsCurrent || !selectedCandidate || !selectedDocument) {
      throw new Error(text(language, '请先检查所选栅格源及其当前修订。', 'Inspect the selected raster source and current revision first.'));
    }
    const cellSize = Number(cellSizeText);
    if (!Number.isFinite(cellSize) || cellSize <= 0) throw new Error(text(language, '像元大小必须为大于零的有限数值。', 'Cell size must be a finite number greater than zero.'));
    const currentView = makeCurrentView();
    return {
      source: inspection.source,
      projectXml: context.projectXml,
      region,
      cellSize,
      currentView,
      plotOptions: activePlotValues,
    };
  };

  const previewRaster = async (): Promise<void> => {
    setPreview(null);
    setPreviewRequestKey('');
    setPreviewImage('');
    setReceipt(null);
    let requestID: number | null = null;
    let requestScope: string | null = null;
    try {
      const parameters = currentRasterRequest();
      const requestKey = JSON.stringify(parameters);
      if (!backend.supports(previewMethod)) throw new Error(text(language, '当前 C# Host 未发布 RGB TIFF 预览接口。', 'The C# Host has not advertised RGB TIFF preview.'));
      const request = begin('preview');
      requestID = request.id;
      requestScope = request.scope;
      const result = parseRgbPreview(await backend.request<unknown>(previewMethod, parameters, { signal: request.controller.signal }));
      if (!isCurrent(request.id, request.scope)) return;
      if (!inspection || result.sourceID !== inspection.source.id || result.sourceRevision.toUpperCase() !== inspection.value.sourceRevision.toUpperCase() ||
          result.visualizationRevision.toUpperCase() !== inspection.value.visualizationRevision.toUpperCase() || result.cellSize !== parameters.cellSize ||
          result.raster.id !== inspection.source.id || result.raster.sourceMapID !== inspection.source.sourceMapID) {
        throw new Error('RGB 预览响应与当前栅格修订或显示选项不匹配。');
      }
      setPreview(result);
      setPreviewRequestKey(requestKey);
      setStatus(text(language, `Host 预览 ${result.outputWidth} × ${result.outputHeight} px，估算 ${formatBytes(result.estimatedFileSizeBytes)}。`, `Host preview is ${result.outputWidth} × ${result.outputHeight} px, estimated ${formatBytes(result.estimatedFileSizeBytes)}.`));
    } catch (caught) {
      if (requestID !== null && (requestGeneration.current !== requestID || requestScope === null || !isCurrent(requestID, requestScope))) return;
      setError(caught);
      setDiagnostics(errorDiagnostics(caught));
    } finally {
      if (requestID !== null && requestGeneration.current === requestID && activeRequest.current?.id === requestID) {
        activeRequest.current = null;
        setPhase(null);
      }
    }
  };

  const execute = async (): Promise<void> => {
    setReceipt(null);
    setError(null);
    setDiagnostics(null);
    let dispatched = false;
    let requestID: number | null = null;
    let requestScope: string | null = null;
    let outcomeUncertain = false;
    try {
      const parameters = currentRasterRequest();
      const destination = outputPath.trim();
      if (!destination || !isServerAbsolutePath(destination) || !destination.toLowerCase().endsWith('.tif')) {
        throw new Error(text(language, '请输入 .tif 结尾的服务器绝对输出路径；浏览器本机路径不会传到 C# 服务。', 'Enter an absolute server path ending in .tif. Browser-local paths are not sent to the C# service.'));
      }
      if (!preview || previewRequestKey !== JSON.stringify(parameters) || !inspection || preview.sourceID !== inspection.source.id ||
          preview.sourceRevision.toUpperCase() !== inspection.value.sourceRevision.toUpperCase() ||
          preview.visualizationRevision.toUpperCase() !== inspection.value.visualizationRevision.toUpperCase() ||
          preview.cellSize !== parameters.cellSize) {
        throw new Error(text(language, '当前选项尚未通过 Host 预览，请先预览。', 'Preview the current settings through the Host before exporting.'));
      }
      if (!backend.supports(executeMethod)) throw new Error(text(language, '当前 C# Host 未发布可取消的 RGB TIFF 导出接口。', 'The C# Host has not advertised cancellable RGB TIFF export.'));
      const request = begin('execute');
      dispatched = true;
      requestID = request.id;
      requestScope = request.scope;
      const raw = await backend.request<unknown>(executeMethod, { ...parameters, outputPath: destination }, {
        signal: request.controller.signal,
        onProgress: value => { if (isCurrent(request.id, request.scope)) setProgress(parseProgress(value, language)); },
      });
      if (!isCurrent(request.id, request.scope)) {
        if (requestGeneration.current === request.id) {
          outcomeUncertain = true;
          setError(new Error(text(language, '导出回执属于已变化的源或设置，无法确认当前目标文件状态。', 'The export receipt belongs to a changed source or setting, so the current destination state is unconfirmed.')));
          setPhase('uncertain');
          setStatus(text(language, '请先核对服务器输出目录，再考虑重试。', 'Check the server output directory before retrying.'));
        }
        return;
      }
      const result = parseRgbReceipt(raw);
      if (!sameRgbReceipt(result, parameters.source, preview, parameters.cellSize, destination)) {
        throw new Error('RGB TIFF 回执与当前源修订、地图可视化或输出路径不匹配。');
      }
      setReceipt(result);
      setPreview(null);
      setPreviewRequestKey('');
      setPreviewImage('');
      setStatus(result.state === 'cancelled'
        ? text(language, 'C# Host 已确认导出取消；没有提交 TIFF 文件。', 'The C# Host confirmed cancellation; no TIFF was committed.')
        : text(language, `Host 已确认写入 GeoTIFF：${result.outputPath}。`, `The Host confirmed the GeoTIFF at ${result.outputPath}.`));
    } catch (caught) {
      if (requestID !== null && requestGeneration.current !== requestID) return;
      if (requestID !== null && requestScope !== null && !isCurrent(requestID, requestScope)) {
        outcomeUncertain = true;
        setError(new Error(text(language, '导出请求与当前源或设置不再一致。', 'The export request no longer matches the current source or settings.')));
        setDiagnostics(errorDiagnostics(caught));
        setPhase('uncertain');
        setStatus(text(language, '请先核对服务器输出目录，再考虑重试。', 'Check the server output directory before retrying.'));
        return;
      }
      setError(caught);
      setDiagnostics(errorDiagnostics(caught));
      if (dispatched && (isUnconfirmedCancellation(caught) || !(caught instanceof Error && caught.name === 'ServerBackendError'))) {
        outcomeUncertain = true;
        setPhase('uncertain');
        setStatus(text(language, '导出终态或回执尚未确认；请先核对服务器输出目录，不要重复写入同一路径。', 'The export terminal state or receipt is unconfirmed. Check the server output directory before retrying the same path.'));
      }
    } finally {
      if (requestID !== null && requestGeneration.current === requestID && !outcomeUncertain) {
        activeRequest.current = null;
        setPhase(null);
      }
    }
  };

  const cancel = (): void => {
    const active = activeRequest.current;
    if (!active || phase !== 'execute' || active.controller.signal.aborted) return;
    setPhase('cancelling');
    setStatus(text(language, '已请求 C# Host 取消；正在等待实际终态…', 'Cancellation requested; waiting for the C# Host terminal state…'));
    active.controller.abort('User requested RGB TIFF export cancellation.');
  };

  const canPreview = phase === null && backend.supports(previewMethod) && inspectionIsCurrent;
  const previewMatchesCurrentRequest = (): boolean => {
    if (!preview || !inspection || !inspectionIsCurrent) return false;
    try {
      const parameters = currentRasterRequest();
      return previewRequestKey === JSON.stringify(parameters) && preview.sourceID === inspection.source.id &&
        preview.sourceRevision.toUpperCase() === inspection.value.sourceRevision.toUpperCase() &&
        preview.visualizationRevision.toUpperCase() === inspection.value.visualizationRevision.toUpperCase() &&
        preview.cellSize === parameters.cellSize;
    } catch {
      return false;
    }
  };
  const canExecute = phase === null && backend.supports(executeMethod) && previewMatchesCurrentRequest()
    && isServerAbsolutePath(outputPath) && outputPath.trim().toLowerCase().endsWith('.tif');

  return <section className="tools-export-dialog-content tools-rgb-tiff-exporter" aria-label={text(language, 'RGB GeoTIFF 导出', 'RGB GeoTIFF export')}>
    <header className="tools-export-dialog-heading">
      <div><span className="tools-kicker">RGBA TIFF</span><h3>{text(language, 'RGB 栅格导出', 'RGB raster export')}</h3>
        <p>{text(language, '从已保存地图目录选择单波段栅格源，Host 按当前 RasterSymbology 与绘图选项生成 RGBA GeoTIFF。坐标参考信息由 Host 读取当前工程投影；工程未设置投影时不会推断 CRS。', 'Choose a single-band raster from the saved map catalog; the Host renders an RGBA GeoTIFF with its current RasterSymbology and plot options. The Host reads projection metadata from the current project and does not infer a CRS when the project has none.')}</p></div>
      <span className={`tools-runtime-pill ${backend.supports(inspectMethod) && backend.supports(previewMethod) && backend.supports(executeMethod) ? 'is-connected' : 'is-unavailable'}`}>
        {backend.supports(inspectMethod) && backend.supports(previewMethod) && backend.supports(executeMethod) ? 'C# Host' : text(language, '接口未发布', 'Unavailable')}
      </span>
    </header>

    <div className="tools-export-grid">
      <label className="tools-field"><span>{text(language, '已保存地图栅格', 'Saved map raster')}</span>
        <select value={candidateKey} onChange={event => setCandidateKey(event.target.value)} disabled={phase !== null || candidates.length === 0}>
          <option value="">{phase === 'catalog' ? text(language, '正在读取地图…', 'Reading maps…') : text(language, '选择栅格图层', 'Choose a raster layer')}</option>
          {candidates.map(candidate => <option key={rasterCandidateKey(candidate)} value={rasterCandidateKey(candidate)}>
            {candidate.document.name} · {candidate.node.name} · {candidate.locator.mapType}{candidate.gdb ? ' · GDB' : ''}
          </option>)}
        </select>
      </label>
      <div className="tools-field tools-export-action-field"><span>{text(language, '来源检查', 'Source inspection')}</span>
        <button type="button" className="tools-button tools-button-secondary" onClick={() => void inspect()} disabled={phase !== null || !selectedCandidate || !backend.supports(inspectMethod)}>
          {phase === 'inspect' ? text(language, '正在检查…', 'Inspecting…') : text(language, '检查源与绘图选项', 'Inspect source and plot options')}
        </button>
      </div>
    </div>

    {inspection && inspectionIsCurrent && <>
      <div className="tools-export-source-receipt"><strong>{inspection.value.sourceName}</strong>
        <code>{inspection.source.sourcePath}</code>
        <span>Source SHA-256: {inspection.value.sourceRevision}</span>
        <span>.vis SHA-256: {inspection.value.visualizationRevision}</span>
        <span>{text(language, '全图范围', 'Full-layer extent')}: {formatExtent(inspection.value.fullExtent)}</span>
      </div>
      <div className="tools-export-grid">
        <label className="tools-field"><span>{text(language, '导出范围', 'Export region')}</span>
          <select value={region} onChange={event => setRegion(event.target.value as 'FullLayer' | 'CurrentView')} disabled={phase !== null}>
            <option value="FullLayer">{text(language, '全栅格范围', 'Full layer extent')}</option>
            <option value="CurrentView">{text(language, '指定范围', 'Specified extent')}</option>
          </select>
        </label>
        <label className="tools-field"><span>{text(language, '像元大小', 'Cell size')}</span>
          <input inputMode="decimal" value={cellSizeText} onChange={event => setCellSizeText(event.target.value)} disabled={phase !== null} />
          <small>{text(language, 'Host 建议值基于检查时的地图视口（若可用）；此工具页不携带地图相机，请输入大于零的工程坐标单位。', 'The Host suggestion uses the map view from inspection when available. This tool page has no camera state; enter a positive value in project coordinate units.')}</small>
        </label>
      </div>
      {region === 'CurrentView' && <details className="tools-export-extent-editor" open>
        <summary>{text(language, '指定导出范围', 'Specified export extent')}</summary>
        <p>{text(language, '此工具页没有地图相机状态；输入工程坐标范围。Host 会将范围与栅格有效范围求交。', 'This tool page has no map camera state. Enter project-coordinate bounds; the Host intersects them with the valid raster extent.')}</p>
        <div className="tools-export-grid tools-export-grid-four">
          {(['minX', 'minY', 'maxX', 'maxY'] as const).map(key => <label className="tools-field" key={key}><span>{key}</span>
            <input inputMode="decimal" value={viewValues[key]} onChange={event => setViewValues(current => ({ ...current, [key]: event.target.value }))} disabled={phase !== null} />
          </label>)}
        </div>
        <div className="tools-export-grid">
          <label className="tools-field"><span>{text(language, '视口像素宽', 'Viewport pixel width')}</span><input inputMode="numeric" value={viewValues.pixelWidth} onChange={event => setViewValues(current => ({ ...current, pixelWidth: event.target.value }))} disabled={phase !== null} /></label>
          <label className="tools-field"><span>{text(language, '视口像素高', 'Viewport pixel height')}</span><input inputMode="numeric" value={viewValues.pixelHeight} onChange={event => setViewValues(current => ({ ...current, pixelHeight: event.target.value }))} disabled={phase !== null} /></label>
        </div>
      </details>}
      {activePlotOptions.length > 0 && <section className="tools-export-plot-options"><h4>{text(language, 'RasterRenderLayer 绘图选项', 'RasterRenderLayer plot options')}</h4>
        {activePlotOptions.map(option => <PlotOptionField key={option.propertyName} option={option} value={plotValues[option.propertyName] ?? option.value}
          enabled={plotOptionEnabled(option, plotValues)} language={language} disabled={phase !== null}
          onChange={value => setPlotValues(current => ({ ...current, [option.propertyName]: value }))} />)}
      </section>}
      <label className="tools-field"><span>{text(language, '服务器 GeoTIFF 输出路径', 'Server GeoTIFF output path')}</span>
        <input value={outputPath} onChange={event => { setOutputPath(event.target.value); setReceipt(null); }} disabled={phase !== null || phase === 'uncertain'} placeholder={text(language, '例如 /srv/ras/exports/raster.tif', 'For example /srv/ras/exports/raster.tif')} />
        <small>{text(language, 'C# 服务所在机器解析此绝对路径；执行由你点击按钮后才开始，Web 不会声称可下载服务器文件。', 'The C# service resolves this absolute path. Export starts only when you click Export; Web does not claim to download server files.')}</small>
      </label>
      <div className="tools-export-actions">
        <button type="button" className="tools-button tools-button-secondary" onClick={() => void previewRaster()} disabled={!canPreview}>
          {phase === 'preview' ? text(language, 'Host 正在渲染预览…', 'Rendering on Host…') : text(language, '生成 TIFF 预览', 'Preview TIFF')}
        </button>
        {phase === 'execute' || phase === 'cancelling'
          ? <button type="button" className="tools-button tools-button-secondary" onClick={cancel} disabled={phase !== 'execute'}>{phase === 'cancelling' ? text(language, '等待取消终态…', 'Waiting for cancellation…') : text(language, '取消导出', 'Cancel export')}</button>
          : <button type="button" className="tools-button tools-button-primary" onClick={() => void execute()} disabled={!canExecute || phase === 'uncertain'}>{phase === 'uncertain' ? text(language, '终态未确认', 'Outcome unconfirmed') : text(language, '导出 GeoTIFF', 'Export GeoTIFF')}</button>}
      </div>
    </>}

    {progress && <div className="tools-export-progress" role="status"><div>{progress.label}</div>{progress.fraction !== null && <progress max={1} value={progress.fraction} />}</div>}
    {preview && <section className="tools-rgb-preview" aria-label={text(language, 'Host RGB TIFF 预览', 'Host RGB TIFF preview')}>
      <div className="tools-subpanel-heading"><h4>{text(language, '输出预览', 'Output preview')}</h4><span>{preview.outputWidth} × {preview.outputHeight} px · {formatBytes(preview.estimatedFileSizeBytes)}</span></div>
      {previewImage ? <img src={previewImage} alt={text(language, 'C# Host 渲染的栅格像素预览', 'Raster pixel preview rendered by the C# Host')} /> : <p>{text(language, 'Host 返回的像素帧暂不可显示。', 'The Host pixel frame cannot be displayed.')}</p>}
      <p>{text(language, '输出范围', 'Output extent')}: {formatExtent(preview.extent)} · {text(language, '像元大小', 'Cell size')}: {preview.cellSize}</p>
    </section>}
    {receipt && <div className={`tools-export-receipt ${receipt.state === 'completed' ? 'is-success' : 'is-cancelled'}`} role="status">
      <strong>{receipt.state === 'completed' ? text(language, 'Host 已确认完成', 'Host confirmed completion') : text(language, 'Host 已确认取消', 'Host confirmed cancellation')}</strong>
      {receipt.state === 'completed' && <><p>{receipt.outputPath}</p><p>{receipt.width} × {receipt.height} px · {receipt.artifacts[0]?.sizeBytes} bytes</p>
        {receipt.artifacts.map(artifact => <p key={artifact.path}><code>{artifact.path}</code> · SHA-256 {artifact.sha256}</p>)}</>}
    </div>}
    {status && <p className="tools-export-status" role="status">{status}</p>}
    {error !== null && <div className="tools-export-error" role="alert"><p>{message(error, language)}</p><details><summary>{text(language, 'C# Host 诊断', 'C# Host diagnostics')}</summary><pre>{safeJson(diagnostics ?? errorDiagnostics(error))}</pre></details></div>}
    {phase === 'catalog' && <p className="tools-export-status" role="status">{text(language, '正在读取服务器保存的地图目录与栅格节点…', 'Reading saved map and raster nodes from the server…')}</p>}
  </section>;
}

function PlotOptionField({ option, value, enabled, language, disabled, onChange }: {
  option: RasterPlotOption; value: string; enabled: boolean; language: Language; disabled: boolean; onChange: (value: string) => void;
}) {
  return <label className="tools-export-plot-option">
    <span>{option.label}{option.description ? <small>{option.description}</small> : null}</span>
    {option.kind === 'bool' ? <input type="checkbox" checked={value === 'true'} onChange={event => onChange(event.target.checked ? 'true' : 'false')} disabled={disabled || !enabled} />
      : <select value={value} onChange={event => onChange(event.target.value)} disabled={disabled || !enabled}>
        {option.enumValues.map(choice => <option value={choice.value} key={choice.value}>{choice.label}</option>)}
      </select>}
    {!option.isEnabled && !enabled && <small>{text(language, '当前依赖选项未启用', 'The controlling option is not enabled')}</small>}
  </label>;
}

function rasterCandidateKey(candidate: RasterCandidate): string {
  return JSON.stringify([candidate.document.mapID, candidate.document.mapIndex, candidate.locator.nodePath,
    candidate.node.assetID, candidate.locator.filename, candidate.locator.filenameRelative]);
}

function plotOptionEnabled(option: RasterPlotOption, values: Record<string, string>): boolean {
  return option.isEnabled || (option.enabledIf ? values[option.enabledIf] === 'true' : false);
}

function enabledPlotValues(options: RasterPlotOption[], values: Record<string, string>): Record<string, string> {
  return Object.fromEntries(options.filter(option => plotOptionEnabled(option, values))
    .map(option => [option.propertyName, values[option.propertyName] ?? option.value]));
}

function sameRgbReceipt(
  result: RgbRasterReceipt,
  source: { id: string; sourceRevision: string; visualizationRevision: string },
  preview: RgbRasterPreview,
  cellSize: number,
  outputPath: string,
): boolean {
  if (result.sourceID !== source.id || result.sourceRevision.toUpperCase() !== source.sourceRevision.toUpperCase() ||
      result.visualizationRevision.toUpperCase() !== source.visualizationRevision.toUpperCase() || result.cellSize !== cellSize) return false;
  if (result.state === 'cancelled') return result.outputPath === null && result.artifacts.length === 0;
  return Boolean(result.outputPath && normalizeServerPath(result.outputPath) === normalizeServerPath(outputPath) &&
    result.artifacts.length === 1 && normalizeServerPath(result.artifacts[0].path) === normalizeServerPath(outputPath) &&
    pathFileName(result.outputPath).toLowerCase().endsWith('.tif') && result.width === preview.outputWidth &&
    result.height === preview.outputHeight && result.extent.minX === preview.extent.minX &&
    result.extent.minY === preview.extent.minY && result.extent.maxX === preview.extent.maxX &&
    result.extent.maxY === preview.extent.maxY);
}

function parseProgress(value: unknown, language: Language): Progress {
  if (!isRecord(value)) return { fraction: null, label: String(value) };
  const label = [value.taskName, value.message].filter((item): item is string => typeof item === 'string' && item.length > 0).join(' · ');
  const fraction = typeof value.fraction === 'number' && Number.isFinite(value.fraction) ? Math.max(0, Math.min(1, value.fraction)) : null;
  return { fraction, label: label || text(language, 'C# Host 正在写入 RGBA TIFF。', 'The C# Host is writing the RGBA TIFF.') };
}

function isUnconfirmedCancellation(value: unknown): boolean {
  if (!isRecord(value)) return false;
  return value.name === 'AbortError' || value.terminalConfirmed === false || value.code === 'OPERATION_STATUS_UNCONFIRMED';
}

function formatExtent(extent: { minX: number; minY: number; maxX: number; maxY: number }): string {
  return `[${extent.minX}, ${extent.minY}] – [${extent.maxX}, ${extent.maxY}]`;
}

function formatBytes(value: number): string {
  if (!Number.isFinite(value) || value < 0) return '—';
  if (value < 1024) return `${value} B`;
  const units = ['KiB', 'MiB', 'GiB', 'TiB'];
  let size = value;
  let unit = -1;
  do { size /= 1024; unit += 1; } while (size >= 1024 && unit < units.length - 1);
  return `${size.toFixed(1)} ${units[unit]}`;
}

function requireRecord(value: unknown, description: string): Record<string, unknown> {
  if (!isRecord(value)) throw new Error(`${description}不是有效对象。`);
  return value;
}

function safeJson(value: unknown): string {
  try { return JSON.stringify(value, null, 2); } catch { return String(value); }
}
