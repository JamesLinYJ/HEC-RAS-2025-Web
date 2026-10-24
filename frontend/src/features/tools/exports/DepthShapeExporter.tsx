import { useEffect, useRef, useState } from 'react';
import type { BackendGateway, Language, WorkbenchContext } from '../../../shared/workbench';
import { errorDiagnostics, message, text } from '../../../shared/workbench';
import type { MapDocumentTab } from '../../map/mapTypes';
import {
  createExportMapRequest,
  exportContextKey,
  isRecord,
  isServerAbsolutePath,
  loadExportMapDocuments,
  normalizeServerPath,
  parseDepthInspection,
  parseDepthPreview,
  parseDepthReceipt,
  parseResultSourceContext,
  pathFileName,
  type ExportCatalogTreeNodeLocator,
  type DepthExportInspection,
  type DepthExportOwner,
  type DepthExportPreview,
  type DepthExportReceipt,
} from './exportWorkflow';

interface Props {
  backend: BackendGateway;
  context: WorkbenchContext;
  language: Language;
  onCompleted?: (receipt: DepthExportReceipt) => void;
  onBusyStateChange?: (busy: boolean) => void;
}

type Phase = 'catalog' | 'inspect' | 'preview' | 'execute' | 'cancelling' | 'uncertain' | null;
interface Progress { fraction: number | null; label: string }

const inspectMethod = 'depthExport.inspect';
const previewMethod = 'depthExport.preview';
const executeMethod = 'depthExport.execute';

export function DepthShapeExporter({ backend, context, language, onCompleted, onBusyStateChange }: Props) {
  const contextKey = exportContextKey(context);
  const contextKeyRef = useRef(contextKey);
  contextKeyRef.current = contextKey;
  const languageRef = useRef(language);
  languageRef.current = language;
  const requestGeneration = useRef(0);
  const activeRequest = useRef<{ controller: AbortController; id: number } | null>(null);
  const propsRef = useRef({ contextKey, ownerKey: '', mapOwnerKey: '', backend, onCompleted, onBusyStateChange });
  propsRef.current = { contextKey, ownerKey: '', mapOwnerKey: '', backend, onCompleted, onBusyStateChange };

  const [documents, setDocuments] = useState<MapDocumentTab[]>([]);
  const [projectXmlSHA, setProjectXmlSHA] = useState<string | null>(null);
  const [mapID, setMapID] = useState('');
  const [inspection, setInspection] = useState<DepthExportInspection | null>(null);
  const [inspectionOwnerKey, setInspectionOwnerKey] = useState('');
  const [selectedOwnerKey, setSelectedOwnerKey] = useState('');
  const [profileIndexText, setProfileIndexText] = useState('');
  const [cellSizeText, setCellSizeText] = useState('');
  const [simplifyText, setSimplifyText] = useState('');
  const [outputPath, setOutputPath] = useState('');
  const [viewportValues, setViewportValues] = useState({ minX: '', minY: '', maxX: '', maxY: '', pixelWidth: '640', pixelHeight: '480' });
  const [preview, setPreview] = useState<DepthExportPreview | null>(null);
  const [receipt, setReceipt] = useState<DepthExportReceipt | null>(null);
  const [phase, setPhase] = useState<Phase>('catalog');
  const [progress, setProgress] = useState<Progress | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [status, setStatus] = useState('');
  const [diagnostics, setDiagnostics] = useState<unknown>(null);

  const selectedDocument = documents.find(document => document.mapID === mapID) ?? null;
  const selectedOwner = inspection?.owners.find(owner => ownerKey(owner) === selectedOwnerKey) ?? null;
  const selectedProfile = selectedOwner?.profiles.find(profile => profile.index === Number(profileIndexText)) ?? null;
  const currentRequestOwner = selectedDocument
    ? JSON.stringify([contextKey, selectedDocument.mapID, selectedDocument.mapIndex, selectedDocument.name,
      selectedDocument.mapType, selectedDocument.visualizationRevision, inspection?.contextToken ?? null,
      selectedOwner?.resultPath ?? null, selectedOwner?.resultRevision ?? null, profileIndexText,
      cellSizeText, simplifyText, viewportValues, outputPath])
    : contextKey;
  propsRef.current.ownerKey = currentRequestOwner;
  propsRef.current.mapOwnerKey = mapInspectionOwnerKey(contextKey, selectedDocument, '');
  const inspectionIsCurrent = Boolean(inspection && inspectionOwnerKey === mapInspectionOwnerKey(contextKey, selectedDocument, inspection.contextToken));

  useEffect(() => {
    const generation = ++requestGeneration.current;
    const controller = new AbortController();
    activeRequest.current?.controller.abort('Project export context changed.');
    activeRequest.current = { controller, id: generation };
    setDocuments([]);
    setProjectXmlSHA(null);
    setMapID('');
    setInspection(null);
    setInspectionOwnerKey('');
    setSelectedOwnerKey('');
    setProfileIndexText('');
    setCellSizeText('');
    setSimplifyText('');
    setPreview(null);
    setReceipt(null);
    setError(null);
    setDiagnostics(null);
    setStatus('');
    setPhase('catalog');
    void loadExportMapDocuments(backend, context, controller.signal).then(value => {
      if (controller.signal.aborted || generation !== requestGeneration.current || contextKeyRef.current !== contextKey) return;
      setDocuments(value.documents);
      setProjectXmlSHA(value.projectXmlSHA);
      setMapID(value.documents[0]?.mapID ?? '');
      setPhase(null);
      setStatus(value.documents.length > 0
        ? text(languageRef.current, `已读取 ${value.documents.length} 个已保存地图。`, `Read ${value.documents.length} saved maps.`)
        : text(languageRef.current, '当前工程没有已保存地图。', 'The project has no saved maps.'));
    }).catch(caught => {
      if (controller.signal.aborted || generation !== requestGeneration.current || contextKeyRef.current !== contextKey) return;
      setError(caught);
      setDiagnostics(errorDiagnostics(caught));
      setPhase(null);
    });
    return () => {
      controller.abort('Depth exporter context changed or closed.');
      if (activeRequest.current?.controller === controller) activeRequest.current = null;
      if (requestGeneration.current === generation) requestGeneration.current += 1;
    };
  }, [backend, context, contextKey]);

  useEffect(() => () => {
    requestGeneration.current += 1;
    activeRequest.current?.controller.abort('Depth exporter closed.');
    activeRequest.current = null;
  }, []);

  useEffect(() => {
    setInspection(null);
    setInspectionOwnerKey('');
    setSelectedOwnerKey('');
    setProfileIndexText('');
    setPreview(null);
    setReceipt(null);
    setError(null);
    setDiagnostics(null);
    if (phase !== 'catalog') setStatus('');
  }, [mapID]);

  useEffect(() => {
    setPreview(null);
    setReceipt(null);
    setError(null);
    setDiagnostics(null);
    setStatus('');
  }, [selectedOwnerKey, profileIndexText, cellSizeText, simplifyText, viewportValues]);

  useEffect(() => {
    propsRef.current.onBusyStateChange?.(phase !== null && phase !== 'uncertain');
  }, [phase, onBusyStateChange]);

  const isCurrent = (id: number, owner: string): boolean => requestGeneration.current === id
    && contextKeyRef.current === contextKey && propsRef.current.contextKey === contextKey
    && (propsRef.current.ownerKey === owner || propsRef.current.mapOwnerKey === owner);

  const begin = (next: Exclude<Phase, null | 'cancelling' | 'uncertain'>, owner: string) => {
    activeRequest.current?.controller.abort('A newer depth export request started.');
    const controller = new AbortController();
    const id = ++requestGeneration.current;
    activeRequest.current = { controller, id };
    setPhase(next);
    setError(null);
    setDiagnostics(null);
    setProgress(null);
    return { controller, id, owner };
  };

  const inspect = async (): Promise<void> => {
    setInspection(null);
    setInspectionOwnerKey('');
    setSelectedOwnerKey('');
    setPreview(null);
    setReceipt(null);
    setStatus('');
    if (!selectedDocument || !projectXmlSHA || !context.projectPath || !context.projectRevision) {
      setError(new Error(text(language, '请选择有有效 .vis 修订的已保存地图。', 'Select a saved map with a verified .vis revision.')));
      return;
    }
    if (!backend.supports('map.resultSources') || !backend.supports(inspectMethod)) {
      setError(new Error(text(language, '当前 C# Host 未发布 Result 来源检查或深度导出检查接口。', 'The C# Host has not advertised map result-source inspection or depth export inspection.')));
      return;
    }
    const request = begin('inspect', mapInspectionOwnerKey(contextKey, selectedDocument, ''));
    try {
      const map = createExportMapRequest(context, selectedDocument, projectXmlSHA);
      const sourceContext = parseResultSourceContext(await backend.request<unknown>('map.resultSources', {
        map, selectionOverrides: [],
      }, { signal: request.controller.signal }), { context, document: selectedDocument, projectXmlSHA });
      if (!isCurrent(request.id, request.owner)) return;
      const result = parseDepthInspection(await backend.request<unknown>(inspectMethod, {
        map, selectionOverrides: [], contextToken: sourceContext.contextToken,
      }, { signal: request.controller.signal }));
      if (!isCurrent(request.id, request.owner)) return;
      if (result.mapID !== selectedDocument.mapID || result.contextToken !== sourceContext.contextToken) {
        throw new Error('深度导出检查与当前地图来源目录不属于同一上下文。');
      }
      setInspection(result);
      setInspectionOwnerKey(mapInspectionOwnerKey(contextKey, selectedDocument, result.contextToken));
      const firstOwner = result.owners[0];
      if (firstOwner) {
        setSelectedOwnerKey(ownerKey(firstOwner));
        const profileIndex = firstOwner.currentProfileIndex !== null && firstOwner.profiles.some(profile => profile.index === firstOwner.currentProfileIndex)
          ? firstOwner.currentProfileIndex : firstOwner.profiles[0].index;
        setProfileIndexText(String(profileIndex));
        setCellSizeText(String(result.defaultCellSize));
        setSimplifyText(String(result.defaultSimplifyDistance));
      }
      setStatus(result.owners.length > 0
        ? text(language, `Host 返回 ${result.owners.length} 个可导出 Result 来源。`, `Host returned ${result.owners.length} exportable Result sources.`)
        : text(language, 'Host 未找到当前地图中可导出的 Result 来源。', 'The Host found no exportable Result sources in this map.'));
    } catch (caught) {
      if (!isCurrent(request.id, request.owner)) return;
      setError(caught);
      setDiagnostics(errorDiagnostics(caught));
    } finally {
      if (requestGeneration.current === request.id) {
        activeRequest.current = null;
        setPhase(null);
      }
    }
  };

  const parseViewportForm = () => {
    const values = Object.fromEntries(Object.entries(viewportValues).map(([key, value]) => [key, Number(value)])) as Record<keyof typeof viewportValues, number>;
    if (Object.values(viewportValues).some(value => !value.trim()) || Object.values(values).some(value => !Number.isFinite(value))) {
      throw new Error(text(language, '请输入完整的有限范围和预览像素尺寸。', 'Enter finite bounds and preview pixel dimensions.'));
    }
    if (values.maxX <= values.minX || values.maxY <= values.minY || !Number.isInteger(values.pixelWidth) ||
      !Number.isInteger(values.pixelHeight) || values.pixelWidth < 1 || values.pixelHeight < 1 || values.pixelWidth * values.pixelHeight > 4_194_304) {
      throw new Error(text(language, '范围必须递增，像素尺寸必须为正整数且总像素不超过 Host 限制。', 'Bounds must be ordered; pixel sizes must be positive integers within the Host preview limit.'));
    }
    return values;
  };

  const currentExportInputs = () => {
    if (!selectedDocument || !inspection || !inspectionIsCurrent || !selectedOwner || !selectedProfile) {
      throw new Error(text(language, '请先检查当前地图，并选择 Result 来源和已保存剖面。', 'Inspect the current map and choose a Result source and saved profile first.'));
    }
    const cellSize = Number(cellSizeText);
    const simplifyDistance = Number(simplifyText);
    if (!Number.isFinite(cellSize) || cellSize <= 0 || !Number.isFinite(simplifyDistance) || simplifyDistance < 0) {
      throw new Error(text(language, '网格像元大小必须大于零，简化距离必须为非负有限值。', 'Cell size must be positive and simplify distance must be finite and nonnegative.'));
    }
    return {
      map: createExportMapRequest(context, selectedDocument, projectXmlSHA),
      selectionOverrides: [] as unknown[],
      contextToken: inspection.contextToken,
      resultNode: selectedOwner.node,
      selectionIndex: 0 as const,
      resultPath: selectedOwner.resultPath,
      resultRevision: selectedOwner.resultRevision,
      profileIndex: selectedProfile.index,
      viewport: parseViewportForm(),
      cellSize,
      simplifyDistance,
    };
  };

  const previewMatchesCurrentInputs = (): boolean => {
    if (!preview) return false;
    try {
      const parameters = currentExportInputs();
      return Boolean(selectedDocument && preview.mapID === selectedDocument.mapID &&
        preview.contextToken === parameters.contextToken && preview.resultPath === parameters.resultPath &&
        preview.resultRevision.toUpperCase() === parameters.resultRevision.toUpperCase() &&
        preview.profileIndex === parameters.profileIndex && preview.cellSize === parameters.cellSize &&
        preview.simplifyDistance === parameters.simplifyDistance &&
        sameLocator(preview.resultNode, parameters.resultNode) &&
        JSON.stringify(preview.viewport) === JSON.stringify(parameters.viewport));
    } catch {
      return false;
    }
  };

  const previewDepth = async (): Promise<void> => {
    setPreview(null);
    setReceipt(null);
    let requestID: number | null = null;
    let requestOwner: string | null = null;
    try {
      const parameters = currentExportInputs();
      if (!backend.supports(previewMethod)) throw new Error(text(language, '当前 C# Host 未发布深度等值线预览接口。', 'The C# Host has not advertised depth-contour preview.'));
      const request = begin('preview', currentRequestOwner);
      requestID = request.id;
      requestOwner = request.owner;
      const result = parseDepthPreview(await backend.request<unknown>(previewMethod, parameters, { signal: request.controller.signal }));
      if (!isCurrent(request.id, request.owner)) return;
      if (!selectedDocument || result.mapID !== selectedDocument.mapID || result.contextToken !== parameters.contextToken ||
          result.resultPath !== parameters.resultPath || result.resultRevision.toUpperCase() !== parameters.resultRevision.toUpperCase() ||
          result.profileIndex !== parameters.profileIndex || result.cellSize !== parameters.cellSize ||
          result.simplifyDistance !== parameters.simplifyDistance || !sameLocator(result.resultNode, parameters.resultNode) ||
          JSON.stringify(result.viewport) !== JSON.stringify(parameters.viewport)) {
        throw new Error('深度预览响应与当前地图、Result 修订或预览参数不匹配。');
      }
      setPreview(result);
      setStatus(text(language, `Host 已生成 ${result.polygons.length} 个多边形与 ${result.polylines.length} 条折线预览。`, `Host previewed ${result.polygons.length} polygons and ${result.polylines.length} polylines.`));
    } catch (caught) {
      if (requestID !== null && (requestGeneration.current !== requestID || requestOwner === null || !isCurrent(requestID, requestOwner))) return;
      setError(caught);
      setDiagnostics(errorDiagnostics(caught));
    } finally {
      if (requestID !== null && requestGeneration.current === requestID && activeRequest.current?.id === requestID) {
        activeRequest.current = null;
        setPhase(null);
      }
    }
  };

  const executeDepth = async (): Promise<void> => {
    setReceipt(null);
    setError(null);
    setDiagnostics(null);
    let outcomeUncertain = false;
    let requestID: number | null = null;
    let requestOwner: string | null = null;
    try {
      const parameters = currentExportInputs();
      const destination = outputPath.trim();
      if (!destination || !isServerAbsolutePath(destination) || !destination.toLowerCase().endsWith('.shp')) {
        throw new Error(text(language, '请输入 .shp 结尾的服务器绝对输出路径；浏览器本机路径不会传到 C# 服务。', 'Enter an absolute server path ending in .shp. Browser-local paths are not sent to the C# service.'));
      }
      if (!backend.supports(executeMethod)) throw new Error(text(language, '当前 C# Host 未发布可取消的深度 Shapefile 导出接口。', 'The C# Host has not advertised cancellable depth Shapefile export.'));
      if (!preview || !selectedDocument || preview.mapID !== selectedDocument.mapID ||
          preview.contextToken !== parameters.contextToken || preview.resultPath !== parameters.resultPath ||
          preview.resultRevision.toUpperCase() !== parameters.resultRevision.toUpperCase() ||
          preview.profileIndex !== parameters.profileIndex || preview.cellSize !== parameters.cellSize ||
          preview.simplifyDistance !== parameters.simplifyDistance || JSON.stringify(preview.viewport) !== JSON.stringify(parameters.viewport)) {
        throw new Error(text(language, '当前导出设置尚未通过 Host 预览，请先重新预览。', 'Preview the current settings through the Host before exporting.'));
      }
      const request = begin('execute', currentRequestOwner);
      requestID = request.id;
      requestOwner = request.owner;
      const raw = await backend.request<unknown>(executeMethod, { ...parameters, outputPath: destination }, {
        signal: request.controller.signal,
        onProgress: value => {
          if (!isCurrent(request.id, request.owner)) return;
          setProgress(parseProgress(value, language));
        },
      });
      if (!isCurrent(request.id, request.owner)) {
        if (requestGeneration.current === request.id) {
          outcomeUncertain = true;
          setError(new Error(text(language, '导出回执属于已变化的来源或设置，无法确认当前目标文件状态。', 'The export receipt belongs to a changed source or setting, so the destination state is unconfirmed.')));
          setPhase('uncertain');
          setStatus(text(language, '请先核对服务器输出目录，再考虑重试。', 'Check the server output directory before retrying.'));
        }
        return;
      }
      const result = parseDepthReceipt(raw);
      if (!sameDepthReceipt(result, parameters, destination)) throw new Error('深度 Shapefile 回执与当前地图、来源修订或目标路径不匹配。');
      setReceipt(result);
      setPreview(null);
      if (result.state === 'cancelled') {
        setStatus(text(language, 'C# Host 已确认导出取消；没有提交 Shapefile 文件。', 'The C# Host confirmed cancellation; no Shapefile files were committed.'));
      } else {
        setStatus(text(language, `Host 已确认输出 ${result.polygonCount} 个面，写入 ${result.artifacts.length} 个服务器文件。`, `The Host confirmed ${result.polygonCount} polygons across ${result.artifacts.length} server files.`));
        propsRef.current.onCompleted?.(result);
      }
    } catch (caught) {
      if (requestID !== null && requestGeneration.current !== requestID) return;
      if (requestID !== null && requestOwner !== null && !isCurrent(requestID, requestOwner)) {
        outcomeUncertain = true;
        setError(new Error(text(language, '导出请求与当前来源或设置不再一致。', 'The export request no longer matches the current source or settings.')));
        setDiagnostics(errorDiagnostics(caught));
        setPhase('uncertain');
        setStatus(text(language, '请先核对服务器输出目录，再考虑重试。', 'Check the server output directory before retrying.'));
        return;
      }
      setError(caught);
      setDiagnostics(errorDiagnostics(caught));
      if (requestID !== null && !hasConfirmedTerminalFailure(caught)) {
        outcomeUncertain = true;
        setPhase('uncertain');
        setStatus(text(language, '导出终态或回执尚未确认；请先核对服务器输出目录，不要重复写入同一路径。', 'The export terminal state or receipt is unconfirmed. Check the server output directory before retrying the same destination.'));
      }
    } finally {
      if (requestID !== null && activeRequest.current?.id === requestID && requestGeneration.current === requestID && !outcomeUncertain) {
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
    active.controller.abort('User requested depth export cancellation.');
  };

  const canPreview = phase === null && backend.supports(previewMethod) && inspectionIsCurrent && Boolean(selectedOwner && selectedProfile);
  const canExecute = phase === null && backend.supports(executeMethod) && previewMatchesCurrentInputs()
    && isServerAbsolutePath(outputPath) && outputPath.trim().toLowerCase().endsWith('.shp');

  return (
    <section className="tools-export-dialog-content tools-depth-exporter" aria-label={text(language, '深度 Shapefile 导出', 'Depth Shapefile export')}>
      <header className="tools-export-dialog-heading">
        <div><span className="tools-kicker">DEPTH CONTOUR</span><h3>{text(language, '深度等值线 Shapefile', 'Depth contour Shapefile')}</h3>
          <p>{text(language, '从已保存地图中的 Result/MultiResult 和存储剖面生成零深度等值线。', 'Generate zero-depth contours from a saved map Result/MultiResult and stored profile.')}</p></div>
        <span className={`tools-runtime-pill ${backend.supports(inspectMethod) && backend.supports(previewMethod) && backend.supports(executeMethod) ? 'is-connected' : 'is-unavailable'}`}>
          {backend.supports(inspectMethod) && backend.supports(previewMethod) && backend.supports(executeMethod) ? 'C# Host' : text(language, '接口未发布', 'Unavailable')}
        </span>
      </header>

      <div className="tools-export-grid">
        <label className="tools-field"><span>{text(language, '已保存地图', 'Saved map')}</span>
          <select value={mapID} onChange={event => setMapID(event.target.value)} disabled={phase !== null || documents.length === 0}>
            <option value="">{phase === 'catalog' ? text(language, '正在读取地图…', 'Reading maps…') : text(language, '选择地图', 'Choose a map')}</option>
            {documents.map(document => <option key={document.mapID} value={document.mapID}>{document.name} — {document.mapType}</option>)}
          </select>
        </label>
        <div className="tools-field tools-export-action-field"><span>{text(language, '来源检查', 'Source inspection')}</span>
          <button type="button" className="tools-button tools-button-secondary" onClick={() => void inspect()} disabled={phase !== null || !selectedDocument || !backend.supports(inspectMethod) || !backend.supports('map.resultSources')}>
            {phase === 'inspect' ? text(language, '正在检查…', 'Inspecting…') : text(language, '检查 Result 来源', 'Inspect Result sources')}
          </button>
        </div>
      </div>

      {inspection && inspectionIsCurrent && inspection.owners.length > 0 && <div className="tools-export-grid">
        <label className="tools-field"><span>{text(language, 'Result / MultiResult', 'Result / MultiResult')}</span>
          <select value={selectedOwnerKey} onChange={event => setSelectedOwnerKey(event.target.value)} disabled={phase !== null}>
            {inspection.owners.map(owner => <option key={ownerKey(owner)} value={ownerKey(owner)}>{owner.name} · {owner.resultName} · {owner.resultPath}</option>)}
          </select>
        </label>
        <label className="tools-field"><span>{text(language, '存储剖面', 'Stored profile')}</span>
          <select value={profileIndexText} onChange={event => setProfileIndexText(event.target.value)} disabled={phase !== null || !selectedOwner}>
            {selectedOwner?.profiles.map(profile => <option key={profile.index} value={profile.index}>{profile.index} · {profile.name} · {profile.time}</option>)}
          </select>
        </label>
      </div>}

      {inspection && inspectionIsCurrent && inspection.owners.length > 0 && <>
        <div className="tools-export-grid">
          <label className="tools-field"><span>{text(language, '栅格像元大小', 'Raster cell size')}</span>
            <input inputMode="decimal" value={cellSizeText} onChange={event => setCellSizeText(event.target.value)} disabled={phase !== null} />
          </label>
          <label className="tools-field"><span>{text(language, '简化距离', 'Simplify distance')}</span>
            <input inputMode="decimal" value={simplifyText} onChange={event => setSimplifyText(event.target.value)} disabled={phase !== null} />
          </label>
        </div>
        <details className="tools-export-extent-editor"><summary>{text(language, '预览范围与分辨率', 'Preview extent and resolution')}</summary>
          <p>{text(language, '工具页没有地图相机状态；请填写要预览的工程坐标范围。Host 会用同一范围生成真实轮廓预览。', 'This tool page does not carry the map camera. Enter the project-coordinate extent to preview; the Host generates contours for this exact range.')}</p>
          <div className="tools-export-grid tools-export-grid-four">
            {(['minX', 'minY', 'maxX', 'maxY'] as const).map(key => <label className="tools-field" key={key}><span>{key}</span>
              <input inputMode="decimal" value={viewportValues[key]} onChange={event => setViewportValues(current => ({ ...current, [key]: event.target.value }))} disabled={phase !== null} />
            </label>)}
          </div>
          <div className="tools-export-grid">
            <label className="tools-field"><span>{text(language, '预览像素宽', 'Preview pixel width')}</span><input inputMode="numeric" value={viewportValues.pixelWidth} onChange={event => setViewportValues(current => ({ ...current, pixelWidth: event.target.value }))} disabled={phase !== null} /></label>
            <label className="tools-field"><span>{text(language, '预览像素高', 'Preview pixel height')}</span><input inputMode="numeric" value={viewportValues.pixelHeight} onChange={event => setViewportValues(current => ({ ...current, pixelHeight: event.target.value }))} disabled={phase !== null} /></label>
          </div>
        </details>
        <label className="tools-field"><span>{text(language, '服务器 Shapefile 输出路径', 'Server Shapefile output path')}</span>
          <input value={outputPath} onChange={event => { setOutputPath(event.target.value); setReceipt(null); }} disabled={phase !== null || phase === 'uncertain'} placeholder={text(language, '例如 /srv/ras/exports/depth.shp', 'For example /srv/ras/exports/depth.shp')} />
          <small>{text(language, 'C# 服务所在机器解析此绝对路径。执行由你点击按钮后才开始；Web 不会下载或改写本机路径。', 'The C# service resolves this absolute path. Export starts only when you click Export; Web does not download or reinterpret local paths.')}</small>
        </label>
        <div className="tools-export-actions">
          <button type="button" className="tools-button tools-button-secondary" onClick={() => void previewDepth()} disabled={!canPreview}>
            {phase === 'preview' ? text(language, 'Host 正在预览…', 'Previewing on Host…') : text(language, '生成 Host 预览', 'Preview on Host')}
          </button>
          {phase === 'execute' || phase === 'cancelling'
            ? <button type="button" className="tools-button tools-button-secondary" onClick={cancel} disabled={phase !== 'execute'}>{phase === 'cancelling' ? text(language, '等待取消终态…', 'Waiting for cancellation…') : text(language, '取消导出', 'Cancel export')}</button>
            : <button type="button" className="tools-button tools-button-primary" onClick={() => void executeDepth()} disabled={!canExecute || phase === 'uncertain'}>{phase === 'uncertain' ? text(language, '终态未确认', 'Outcome unconfirmed') : text(language, '导出 Shapefile', 'Export Shapefile')}</button>}
        </div>
      </>}

      {progress && <div className="tools-export-progress" role="status"><div>{progress.label}</div>{progress.fraction !== null && <progress max={1} value={progress.fraction} />}</div>}
      {preview && <DepthContourPreview preview={preview} language={language} />}
      {receipt && <div className={`tools-export-receipt ${receipt.state === 'completed' ? 'is-success' : 'is-cancelled'}`} role="status">
        <strong>{receipt.state === 'completed' ? text(language, 'Host 已确认完成', 'Host confirmed completion') : text(language, 'Host 已确认取消', 'Host confirmed cancellation')}</strong>
        {receipt.state === 'completed' && <><p>{receipt.outputPath}</p><p>{text(language, `多边形 ${receipt.polygonCount} 个；文件 ${receipt.artifacts.length} 个。`, `${receipt.polygonCount} polygons; ${receipt.artifacts.length} files.`)}</p>
          <ul>{receipt.artifacts.map(artifact => <li key={artifact.path}><code>{artifact.path}</code> · {artifact.sizeBytes} bytes · SHA-256 {artifact.sha256}</li>)}</ul></>}
      </div>}
      {status && <p className="tools-export-status" role="status">{status}</p>}
      {error !== null && <div className="tools-export-error" role="alert"><p>{message(error, language)}</p><details><summary>{text(language, 'C# Host 诊断', 'C# Host diagnostics')}</summary><pre>{safeJson(diagnostics ?? errorDiagnostics(error))}</pre></details></div>}
      {phase === 'catalog' && <p className="tools-export-status" role="status">{text(language, '正在读取服务器保存的地图目录…', 'Reading saved map catalog from the server…')}</p>}
    </section>
  );
}

function DepthContourPreview({ preview, language }: { preview: DepthExportPreview; language: Language }) {
  const width = preview.viewport.maxX - preview.viewport.minX;
  const height = preview.viewport.maxY - preview.viewport.minY;
  const transform = (line: number[][]) => line.map(([x, y]) => {
    const screenX = ((x - preview.viewport.minX) / width) * 640;
    const screenY = (1 - (y - preview.viewport.minY) / height) * 420;
    return `${screenX},${screenY}`;
  }).join(' ');
  return <section className="tools-depth-preview" aria-label={text(language, 'Host 深度等值线预览', 'Host depth contour preview')}>
    <div className="tools-subpanel-heading"><h4>{text(language, 'Host 轮廓预览', 'Host contour preview')}</h4>
      <span>{preview.profileName} · {preview.profileTime} · {preview.polygons.length + preview.polylines.length} {text(language, '条几何', 'geometries')}</span></div>
    <svg viewBox="0 0 640 420" role="img" aria-label={text(language, 'C# Host 返回的深度轮廓线', 'Depth contours returned by the C# Host')} preserveAspectRatio="none">
      <rect x="0" y="0" width="640" height="420" fill="#f5f7fa" />
      {preview.polygons.map((polygon, index) => <polygon key={`p-${index}`} points={transform(polygon)} fill="none" stroke="#c83232" strokeWidth="2" vectorEffect="non-scaling-stroke" />)}
      {preview.polylines.map((line, index) => <polyline key={`l-${index}`} points={transform(line)} fill="none" stroke="#c83232" strokeWidth="2" vectorEffect="non-scaling-stroke" />)}
    </svg>
  </section>;
}

function ownerKey(owner: DepthExportOwner): string {
  return JSON.stringify([owner.node.rootName, owner.node.nodeType, owner.node.nodeName, owner.node.nodePath,
    owner.selectionIndex, owner.resultPath, owner.resultRevision]);
}

function mapInspectionOwnerKey(contextKey: string, document: MapDocumentTab | null, token: string): string {
  return JSON.stringify([contextKey, document?.mapID ?? null, document?.mapIndex ?? null,
    document?.name ?? null, document?.mapType ?? null, document?.visualizationRevision ?? null, token]);
}

function sameLocator(left: ExportCatalogTreeNodeLocator, right: ExportCatalogTreeNodeLocator): boolean {
  return JSON.stringify(left) === JSON.stringify(right);
}

function sameDepthReceipt(result: DepthExportReceipt, request: {
  map: { mapID: string }; contextToken: string; resultNode: ExportCatalogTreeNodeLocator; resultPath: string;
  resultRevision: string; profileIndex: number; cellSize: number; simplifyDistance: number;
}, outputPath: string): boolean {
  if (result.mapID !== request.map.mapID || result.contextToken !== request.contextToken ||
      !sameLocator(result.resultNode, request.resultNode) || result.resultPath !== request.resultPath ||
      result.resultRevision.toUpperCase() !== request.resultRevision.toUpperCase() || result.profileIndex !== request.profileIndex ||
      result.cellSize !== request.cellSize || result.simplifyDistance !== request.simplifyDistance) return false;
  if (result.state === 'cancelled') return result.outputPath === null && result.artifacts.length === 0;
  if (!result.outputPath || normalizeServerPath(result.outputPath) !== normalizeServerPath(outputPath)) return false;
  const outputName = pathFileName(outputPath);
  const stem = outputName.slice(0, outputName.lastIndexOf('.')).toLowerCase();
  const parent = normalizeServerPath(outputPath).slice(0, normalizeServerPath(outputPath).lastIndexOf('/'));
  const artifactNames = result.artifacts.map(artifact => pathFileName(artifact.path));
  const sameFamily = result.artifacts.every(artifact => {
    const normalized = normalizeServerPath(artifact.path);
    const artifactParent = normalized.slice(0, normalized.lastIndexOf('/'));
    const name = pathFileName(artifact.path).toLowerCase();
    return artifactParent === parent && name.startsWith(`${stem}.`);
  });
  return sameFamily && artifactNames.some(name => name.toLowerCase() === `${stem}.shp`)
    && artifactNames.some(name => name.toLowerCase() === `${stem}.shx`)
    && artifactNames.some(name => name.toLowerCase() === `${stem}.dbf`);
}

function parseProgress(value: unknown, language: Language): Progress {
  if (!isRecord(value)) return { fraction: null, label: String(value) };
  const label = [value.taskName, value.message].filter((item): item is string => typeof item === 'string' && item.length > 0).join(' · ');
  const fraction = typeof value.fraction === 'number' && Number.isFinite(value.fraction) ? Math.max(0, Math.min(1, value.fraction)) : null;
  return { fraction, label: label || text(language, 'C# Host 正在生成 Shapefile。', 'The C# Host is generating the Shapefile.') };
}

function hasConfirmedTerminalFailure(value: unknown): boolean {
  return isRecord(value) && value.terminalConfirmed === true;
}

function safeJson(value: unknown): string {
  try { return JSON.stringify(value, null, 2); } catch { return String(value); }
}
