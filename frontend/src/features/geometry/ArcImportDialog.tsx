import { useEffect, useMemo, useRef, useState } from 'react';
import type { BackendGateway, Language, WorkbenchContext, MapSourceUploadResult, UploadedMapSource } from '../../shared/workbench';
import { errorDiagnostics, message } from '../../shared/workbench';
import { DropTarget, validateShapefileFamilyFiles } from '../tools/vector-import/DropTarget';
import { sameSourcePath } from '../tools/attributes/attributeTableTypes';
import { ImportingPreviewList } from './ImportingPreviewList';
import { isGeometryVectorImportInspection, sameGeometryVectorImportCapture,
  type GeometryVectorImportCapture, type GeometryVectorImportInspection,
  type GeometryVectorImportMode, type GeometryVectorImportSelectedSource } from './vectorImportTypes';
import type { GeometryModelRequest, GeometryTopologySnapshot } from './geometryTypes';
import './geometryArcImport.css';

interface ApplyReceipt {
  sourceCapture: GeometryVectorImportCapture;
  result: GeometryTopologySnapshot;
  imported: GeometryVectorImportSelectedSource[];
  mode: GeometryVectorImportMode;
  mergeContiguousArcs: boolean;
  removeExternalArcs: boolean;
}

interface ArcImportDialogProps {
  open: boolean;
  language: Language;
  context: WorkbenchContext;
  backend: BackendGateway;
  model: GeometryModelRequest | null;
  topology?: GeometryTopologySnapshot;
  capture?: GeometryVectorImportCapture;
  canInspect: boolean;
  canApply: boolean;
  onInspect: (paths: string[], capture: GeometryVectorImportCapture, signal: AbortSignal) => Promise<GeometryVectorImportInspection>;
  onApply: (sources: GeometryVectorImportSelectedSource[], mode: GeometryVectorImportMode,
    mergeContiguousArcs: boolean, removeExternalArcs: boolean, capture: GeometryVectorImportCapture,
    signal: AbortSignal) => Promise<GeometryTopologySnapshot>;
  onClose: () => void;
}

interface RequestOwner {
  generation: number;
  projectOwner: string;
  capture: GeometryVectorImportCapture;
  signal: AbortSignal;
}

function projectOwnerKey(context: WorkbenchContext, model: GeometryModelRequest | null): string {
  return JSON.stringify([context.projectSessionID, context.projectPath, context.projectRevision, context.projectXml,
    model?.assetPath ?? null, model?.kind ?? null, model?.coreLayerContext ?? null]);
}

function captureKey(capture?: GeometryVectorImportCapture): string {
  return capture ? JSON.stringify([capture.revision, capture.draftState, capture.draftIdentity]) : '';
}

function validServerPath(value: string): boolean {
  const path = value.trim().replaceAll('\\', '/');
  const absolute = path.startsWith('/') || /^[a-z]:\//i.test(path) || (path.startsWith('//') && !path.startsWith('///'));
  return absolute && !path.split('/').some(segment => segment === '.' || segment === '..');
}

function parseServerPaths(value: string, language: Language): string[] {
  const paths = value.split(/\r?\n/).map(path => path.trim()).filter(Boolean);
  if (paths.length === 0) throw new Error(language === 'zh' ? '请输入一个或多个服务端可访问的 .shp 绝对路径。' : 'Enter one or more absolute .shp paths accessible to the service.');
  if (paths.some(path => !path.toLocaleLowerCase().endsWith('.shp') || !validServerPath(path))) {
    throw new Error(language === 'zh' ? '每行都必须是服务端可访问的 .shp 绝对路径；浏览器本地路径不能直接使用。' : 'Each line must be an absolute server-visible .shp path; browser-local paths cannot be used directly.');
  }
  if (paths.some((path, index) => paths.slice(index + 1).some(other => sameSourcePath(path, other)))) {
    throw new Error(language === 'zh' ? '来源路径重复，请保留每个 .shp 路径一次。' : 'Duplicate source paths were entered; keep each .shp path once.');
  }
  return paths;
}

function hasUploadedSourceRevision(source: GeometryVectorImportInspection['sources'][number], expected: UploadedMapSource): boolean {
  const sourceFile = source.family.files.find(file => file.role === 'source');
  return sameSourcePath(source.path, expected.sourceCanonicalPath) &&
    sourceFile?.revision?.toLocaleUpperCase() === expected.sourceRevision.toLocaleUpperCase();
}

function rawDiagnostic(error: unknown): string | undefined {
  const value = errorDiagnostics(error);
  if (typeof value === 'string') return value;
  if (value === undefined) return error instanceof Error ? error.message : String(error);
  try { return JSON.stringify(value, null, 2); } catch { return String(value); }
}

function uploadReceiptMatches(result: MapSourceUploadResult, context: WorkbenchContext, files: readonly File[]): boolean {
  const selectedPrimary = files.filter(file => file.name.toLocaleLowerCase().endsWith('.shp'));
  return Boolean(result.uploadID && /^[\da-f]{32}$/i.test(result.uploadID) &&
    result.projectPath === context.projectPath && result.projectRevision === context.projectRevision &&
    result.projectSessionID === context.projectSessionID && result.sources.length === selectedPrimary.length &&
    result.sources.every(source => source.kind === 'shapefile' && /^[\da-f]{64}$/i.test(source.sourceRevision) &&
      source.sourcePath.toLocaleLowerCase().endsWith('.shp') && source.sourceCanonicalPath === source.sourcePath &&
      selectedPrimary.some(file => file.name.toLocaleLowerCase() === source.fileName.toLocaleLowerCase())));
}

export function ArcImportDialog({ open, language, context, backend, model, topology, capture,
  canInspect, canApply, onInspect, onApply, onClose }: ArcImportDialogProps) {
  const zh = language === 'zh';
  const dialogRef = useRef<HTMLDialogElement>(null);
  const requestController = useRef<AbortController | null>(null);
  const generation = useRef(0);
  const projectOwner = projectOwnerKey(context, model);
  const projectOwnerRef = useRef(projectOwner); projectOwnerRef.current = projectOwner;
  const captureRef = useRef(capture); captureRef.current = capture;
  const captureOwner = captureKey(capture);
  const previousCaptureOwner = useRef(captureOwner);
  const [localFiles, setLocalFiles] = useState<File[]>([]);
  const [fileError, setFileError] = useState<string>();
  const [serverPaths, setServerPaths] = useState('');
  const [uploadReceipt, setUploadReceipt] = useState<{ uploadID: string; sources: UploadedMapSource[] }>();
  const [inspection, setInspection] = useState<GeometryVectorImportInspection>();
  const [selectedPaths, setSelectedPaths] = useState<Set<string>>(new Set());
  const [mode, setMode] = useState<GeometryVectorImportMode>('Merge');
  const [mergeContiguousArcs, setMergeContiguousArcs] = useState(false);
  const [removeExternalArcs, setRemoveExternalArcs] = useState(false);
  const [busy, setBusy] = useState<'upload' | 'inspect' | 'apply' | null>(null);
  const busyRef = useRef(busy); busyRef.current = busy;
  const [error, setError] = useState<string>();
  const [detail, setDetail] = useState<string>();
  const [notice, setNotice] = useState<string>();
  const [applied, setApplied] = useState<ApplyReceipt>();

  const inspectionCurrent = Boolean(inspection && capture && captureOwner &&
    inspection.revision === capture.revision && inspection.draftIdentity === capture.draftIdentity);
  const selectedSources = useMemo(() => inspectionCurrent && inspection
    ? inspection.sources.filter(source => selectedPaths.has(source.path)).map(source => ({ path: source.path, family: source.family }))
    : [], [inspection, inspectionCurrent, selectedPaths]);
  const selectedFeatureCount = inspectionCurrent && inspection
    ? inspection.sources.filter(source => selectedPaths.has(source.path)).reduce((sum, source) => sum + source.featureCount, 0) : 0;

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;
    if (open && !dialog.open) dialog.showModal();
    if (!open && dialog.open) dialog.close();
    return () => { if (dialog.open) dialog.close(); };
  }, [open]);

  useEffect(() => {
    if (previousCaptureOwner.current === captureOwner) return;
    previousCaptureOwner.current = captureOwner;
    setInspection(undefined);
    setSelectedPaths(new Set());
    setError(undefined); setDetail(undefined);
    if (busyRef.current === 'inspect') {
      requestController.current?.abort(); requestController.current = null; generation.current += 1;
      setBusy(null);
      setNotice(zh ? '拓扑草稿在检查期间发生变化；已取消旧来源检查，请重试。' : 'The topology draft changed during inspection; the old request was canceled. Inspect again.');
    }
  // Every returned topology revision or draft identity invalidates an old preview.
  }, [captureOwner, zh]);

  useEffect(() => {
    requestController.current?.abort(); requestController.current = null; generation.current += 1;
    setBusy(null); setError(undefined); setDetail(undefined); setNotice(undefined);
    setLocalFiles([]); setFileError(undefined); setServerPaths(''); setUploadReceipt(undefined);
    setInspection(undefined); setSelectedPaths(new Set()); setApplied(undefined);
  }, [projectOwner]);

  useEffect(() => {
    if (open) return;
    requestController.current?.abort(); requestController.current = null; generation.current += 1;
    setBusy(null); setInspection(undefined); setSelectedPaths(new Set());
  }, [open]);

  useEffect(() => () => {
    requestController.current?.abort(); requestController.current = null; generation.current += 1;
  }, []);

  const beginRequest = (): RequestOwner | undefined => {
    const currentCapture = captureRef.current;
    if (!currentCapture) return undefined;
    requestController.current?.abort();
    const controller = new AbortController();
    requestController.current = controller;
    return { generation: ++generation.current, projectOwner, capture: currentCapture, signal: controller.signal };
  };

  const isProjectCurrent = (request: RequestOwner): boolean => generation.current === request.generation &&
    projectOwnerRef.current === request.projectOwner && !request.signal.aborted;

  const isDraftCurrent = (request: RequestOwner): boolean => isProjectCurrent(request) &&
    sameGeometryVectorImportCapture(captureRef.current ?? { revision: '', draftState: '', draftIdentity: '' }, request.capture);

  const inspectWithRequest = async (paths: string[], request: RequestOwner,
    uploadedSources: UploadedMapSource[] = []): Promise<void> => {
    setBusy('inspect'); setError(undefined); setDetail(undefined); setNotice(undefined); setInspection(undefined);
    try {
      const result = await onInspect(paths, request.capture, request.signal);
      if (!isDraftCurrent(request)) return;
      if (!isGeometryVectorImportInspection(result, request.capture, paths)) {
        throw new Error(zh ? '检查回执与当前拓扑草稿或服务器来源路径不一致。' : 'The inspection receipt does not match the current topology draft or server source paths.');
      }
      if (uploadedSources.length) {
        for (const uploaded of uploadedSources) {
          const matched = result.sources.find(source => sameSourcePath(source.path, uploaded.sourceCanonicalPath));
          if (!matched || !hasUploadedSourceRevision(matched, uploaded)) {
            throw new Error(zh ? `服务端检查回执与上传 SHA 不一致：${uploaded.fileName}。` : `The server inspection does not match the uploaded SHA for ${uploaded.fileName}.`);
          }
        }
      }
      setInspection(result);
      setSelectedPaths(new Set(result.sources.filter(source => source.featureCount > 0).map(source => source.path)));
      setNotice(zh ? 'Host 已返回线 / 面几何与完整文件族检查结果。选择来源后可应用到当前未保存几何草稿。' : 'The Host returned line/polygon geometry and complete source-family checks. Select sources to apply them to the unsaved geometry draft.');
    } catch (caught) {
      if (request.signal.aborted || (caught instanceof DOMException && caught.name === 'AbortError')) {
        if (projectOwnerRef.current === request.projectOwner) setNotice(zh ? '来源检查已取消；未更新当前几何草稿。' : 'Source inspection was canceled; the current geometry draft was not updated.');
      } else if (isProjectCurrent(request)) {
        setError(message(caught, language)); setDetail(rawDiagnostic(caught));
      }
    } finally {
      if (generation.current === request.generation) setBusy(null);
    }
  };

  const inspectServerPaths = async (): Promise<void> => {
    if (!canInspect || busy) return;
    let paths: string[];
    try { paths = parseServerPaths(serverPaths, language); }
    catch (caught) { setError(message(caught, language)); setDetail(undefined); return; }
    const request = beginRequest();
    if (!request) { setError(zh ? '当前几何草稿尚未提供可验证的源修订和草稿身份。' : 'The current geometry draft has no verifiable source revision and draft identity.'); return; }
    setUploadReceipt(undefined);
    await inspectWithRequest(paths, request);
  };

  const uploadAndInspect = async (): Promise<void> => {
    if (busy) return;
    const fileIssue = validateShapefileFamilyFiles(localFiles, language);
    if (fileIssue) { setError(fileIssue); return; }
    if (!backend.uploadMapSources || !context.projectPath || !context.projectRevision || !context.projectSessionID) {
      setError(zh ? '当前服务不能把浏览器文件上传到已保存工程；请改用服务端可访问路径，或连接支持工程来源上传的 Host。' : 'This service cannot upload browser files into the saved project. Use server-visible paths or connect a Host that supports project source uploads.');
      return;
    }
    if (!canInspect || !capture) { setError(zh ? '当前拓扑草稿不支持来源检查。' : 'The current topology draft cannot inspect vector sources.'); return; }
    const request = beginRequest();
    if (!request) { setError(zh ? '当前几何草稿尚未提供可验证的源修订和草稿身份。' : 'The current geometry draft has no verifiable source revision and draft identity.'); return; }
    setBusy('upload'); setError(undefined); setDetail(undefined); setNotice(undefined); setInspection(undefined);
    try {
      const uploaded = await backend.uploadMapSources(context, localFiles, { signal: request.signal });
      if (!isProjectCurrent(request)) return;
      if (!uploadReceiptMatches(uploaded, context, localFiles)) {
        throw new Error(zh ? '工程来源上传回执与当前工程、选中文件或 SHA-256 不一致。' : 'The source-upload receipt does not match the current project, selected files, or SHA-256 identities.');
      }
      const paths = uploaded.sources.map(source => source.sourceCanonicalPath);
      setUploadReceipt({ uploadID: uploaded.uploadID, sources: uploaded.sources });
      setLocalFiles([]); setServerPaths(paths.join('\n'));
      setNotice(zh ? '来源已复制到当前工程的 sources 目录；正在检查服务端文件族。上传不会修改几何或地图。' : 'Sources were copied into the current project sources folder; checking the server-side families. Uploading does not modify geometry or maps.');
      await inspectWithRequest(paths, request, uploaded.sources);
    } catch (caught) {
      if (request.signal.aborted || (caught instanceof DOMException && caught.name === 'AbortError')) {
        if (projectOwnerRef.current === request.projectOwner) setNotice(zh ? '上传 / 检查已取消。若上传请求已经到达服务端，来源文件可能已留在工程 sources 目录；几何草稿未被应用。' : 'Upload/inspection was canceled. If the upload reached the service, source files may remain in the project sources folder; the geometry draft was not applied.');
      } else if (isProjectCurrent(request)) {
        setError(message(caught, language)); setDetail(rawDiagnostic(caught));
      }
    } finally {
      if (generation.current === request.generation) setBusy(null);
    }
  };

  const apply = async (): Promise<void> => {
    if (!inspectionCurrent || !inspection || !canApply || busy || selectedSources.length === 0 || selectedFeatureCount === 0) return;
    const request = beginRequest();
    if (!request || inspection.revision !== request.capture.revision || inspection.draftIdentity !== request.capture.draftIdentity ||
        !sameGeometryVectorImportCapture(captureRef.current ?? { revision: '', draftState: '', draftIdentity: '' }, request.capture)) {
      setError(zh ? '预览对应的拓扑草稿已变化；请重新检查来源。' : 'The topology draft changed since preview; inspect the sources again.');
      return;
    }
    setBusy('apply'); setError(undefined); setDetail(undefined); setNotice(undefined);
    try {
      const result = await onApply(selectedSources, mode, mergeContiguousArcs, removeExternalArcs, request.capture, request.signal);
      if (!isProjectCurrent(request)) return;
      setApplied({ sourceCapture: request.capture, result, imported: selectedSources, mode, mergeContiguousArcs, removeExternalArcs });
      setNotice(zh ? '几何导入回执已核对，并已进入当前几何拓扑草稿；尚未保存模型文件。' : 'The geometry import result was verified and adopted into the current topology draft; the model file has not been saved.');
    } catch (caught) {
      if (request.signal.aborted || (caught instanceof DOMException && caught.name === 'AbortError')) {
        if (projectOwnerRef.current === request.projectOwner) setNotice(zh ? '几何导入已取消或因草稿变化停止；旧请求结果没有覆盖当前草稿。请重新检查来源。' : 'The geometry import was canceled or stopped after a draft change; an old request did not overwrite the current draft. Inspect sources again.');
      } else if (isProjectCurrent(request)) {
        setError(message(caught, language)); setDetail(rawDiagnostic(caught));
      }
    } finally {
      if (generation.current === request.generation) setBusy(null);
    }
  };

  const cancel = (): void => {
    const active = busy;
    requestController.current?.abort(); requestController.current = null; generation.current += 1;
    setBusy(null); setInspection(undefined); setSelectedPaths(new Set());
    setNotice(active === 'upload'
      ? (zh ? '已请求取消上传。若上传请求已到达服务端，来源文件可能已留在工程 sources 目录；尚未应用到几何草稿。' : 'Upload cancellation was requested. If the request reached the service, source files may remain in project sources; nothing was applied to the geometry draft.')
      : (zh ? '已请求取消；尚未将新拓扑草稿回执应用到当前编辑器。' : 'Cancellation requested; no new topology snapshot was adopted into the current editor.'));
  };

  return <dialog ref={dialogRef} className="geometry-arc-import-dialog" aria-labelledby="geometry-arc-import-title"
    onCancel={event => { event.preventDefault(); if (busy) cancel(); onClose(); }}>
    <header className="geometry-arc-import-header"><div><span className="geometry-eyebrow">HEC-RAS 2025 · {zh ? '几何拓扑' : 'GEOMETRY TOPOLOGY'}</span>
      <h2 id="geometry-arc-import-title">{zh ? '导入线 / 面几何' : 'Import line / polygon geometry'}</h2>
      <p>{zh ? '按原 ArcImportDialog 的 Merge / NoMerge 与合并选项，将 Host 检查后的来源加入当前几何草稿。' : 'Use the original ArcImportDialog Merge / NoMerge and merge options to add Host-inspected sources to the current geometry draft.'}</p></div>
      <button type="button" className="geometry-icon-button" onClick={() => { if (busy) cancel(); onClose(); }} aria-label={zh ? '关闭' : 'Close'}>{zh ? '关闭' : 'Close'}</button>
    </header>

    {!context.projectPath || !context.projectRevision || !context.projectSessionID || context.projectXml === null || !model
      ? <div className="geometry-local-banner">{zh ? '请先选择具有已保存工程上下文的 Geometry 资产。' : 'Select a Geometry asset in a saved project context first.'}</div>
      : !topology || !capture
        ? <div className="geometry-local-banner">{zh ? '当前拓扑快照缺少 Host 提供的源修订或草稿身份；检查与应用均已禁用。' : 'The current topology snapshot lacks a Host-provided source revision or draft identity; inspection and apply are disabled.'}</div>
        : <>
          <div className="geometry-arc-import-owner"><div><span>{zh ? '几何来源' : 'Geometry source'}</span><code>{model.assetPath}</code></div>
            <div><span>{zh ? '源修订' : 'Source revision'}</span><code>{capture.revision}</code></div>
            <div><span>{zh ? '草稿身份' : 'Draft identity'}</span><code>{capture.draftIdentity}</code></div></div>
          {!topology.canEdit && <p className="geometry-arc-import-warning">{zh ? '当前几何拓扑为只读；Host 的线 / 面导入检查和应用均要求可编辑拓扑。' : 'The current geometry topology is read-only; Host vector import inspection and apply both require an editable topology.'}</p>}

          <section className="geometry-arc-import-section"><header><h3>{zh ? '从浏览器选择文件' : 'Select files from this browser'}</h3>
            <p>{zh ? '选择完整的 .shp/.shx/.dbf 文件族，.prj 可选。上传到当前工程 sources 后会自动调用 Host 检查。' : 'Choose complete .shp/.shx/.dbf families; .prj is optional. Uploading to project sources is followed by Host inspection.'}</p></header>
            <DropTarget language={language} files={localFiles} disabled={busy !== null}
              onFilesChange={setLocalFiles} onValidationChange={setFileError} />
            {fileError && <p className="geometry-arc-import-warning" role="alert">{fileError}</p>}
            <div className="geometry-arc-import-actions"><button type="button" className="geometry-primary"
              disabled={busy !== null || !localFiles.length || !canInspect || !backend.uploadMapSources || Boolean(fileError)} onClick={() => void uploadAndInspect()}>
              {busy === 'upload' ? (zh ? '正在上传…' : 'Uploading…') : busy === 'inspect' ? (zh ? '正在检查…' : 'Inspecting…') : (zh ? '上传并检查来源' : 'Upload and inspect sources')}</button>
              <span className="geometry-muted">{zh ? '浏览器本地路径不会当作服务器路径。' : 'Browser-local paths are never treated as server paths.'}</span></div>
            {!backend.uploadMapSources && <p className="geometry-arc-import-warning">{zh ? '当前后端未提供工程来源上传能力；可在下方使用服务端已有路径。' : 'The current backend does not provide project-source upload; use an existing server-side path below.'}</p>}
          </section>

          <details className="geometry-arc-import-manual"><summary>{zh ? '使用服务端已有的 Shapefile 路径' : 'Use existing server-side Shapefile paths'}</summary>
            <p className="geometry-muted">{zh ? '每行一个服务端绝对 .shp 路径；Host 会在检查和应用时重新核验完整文件族。' : 'Enter one absolute server-side .shp path per line; the Host rechecks the complete file family during inspection and apply.'}</p>
            <label className="geometry-arc-import-paths"><span>{zh ? '服务器路径' : 'Server paths'}</span>
              <textarea value={serverPaths} disabled={busy !== null} spellCheck={false} placeholder={zh ? '/data/roads.shp' : '/data/roads.shp'}
                onChange={event => { setServerPaths(event.currentTarget.value); setInspection(undefined); setSelectedPaths(new Set()); }} /></label>
            <button type="button" className="geometry-secondary" disabled={busy !== null || !canInspect} onClick={() => void inspectServerPaths()}>
              {busy === 'inspect' ? (zh ? '正在检查…' : 'Inspecting…') : (zh ? '检查服务器来源' : 'Inspect server sources')}</button>
          </details>

          {uploadReceipt && <details className="geometry-arc-upload-receipt"><summary>{zh ? `上传回执 · ${uploadReceipt.sources.length} 个 Shapefile 主文件` : `Upload receipt · ${uploadReceipt.sources.length} Shapefile primary files`}</summary>
            <p>{zh ? '文件已复制到工程 sources；此回执不表示已导入几何。' : 'Files were copied into project sources; this receipt does not mean they were imported into geometry.'}</p>
            <code>{uploadReceipt.uploadID}</code><ul>{uploadReceipt.sources.map(source => <li key={source.sourcePath}><code>{source.sourcePath}</code><code>{source.sourceRevision}</code></li>)}</ul>
          </details>}

          {inspection && <ImportingPreviewList language={language} inspection={inspection} selectedPaths={selectedPaths}
            disabled={busy !== null || !inspectionCurrent} onSelectedPathsChange={setSelectedPaths} />}

          {inspectionCurrent && <section className="geometry-arc-import-options"><h3>{zh ? '导入选项' : 'Import options'}</h3>
            <fieldset disabled={busy !== null || !inspectionCurrent}><legend>{zh ? '拓扑处理方式' : 'Topology handling'}</legend>
              <label><input type="radio" name="geometry-vector-import-mode" value="Merge" checked={mode === 'Merge'} onChange={() => setMode('Merge')} />
                <span><strong>{zh ? '导入并合并' : 'Import and merge'}</strong><small>{zh ? '尝试将交叉和重叠区段与现有 / 新来源合并；拓扑可能产生小幅变化。' : 'Try to merge intersections and overlapping sections with existing and imported arcs; topology may change slightly.'}</small></span></label>
              <label><input type="radio" name="geometry-vector-import-mode" value="NoMerge" checked={mode === 'NoMerge'} onChange={() => setMode('NoMerge')} />
                <span><strong>{zh ? '导入但不合并' : 'Import without merge'}</strong><small>{zh ? '保留未解决的交叉或重叠；之后可再执行合并。' : 'Leave unresolved intersections or overlaps for a later merge operation.'}</small></span></label>
            </fieldset>
            <label className="geometry-arc-import-toggle"><input type="checkbox" checked={mergeContiguousArcs} disabled={busy !== null || !inspectionCurrent}
              onChange={event => setMergeContiguousArcs(event.currentTarget.checked)} />
              <span>{zh ? '合并共线的连续弧段节点' : 'Merge collinear nodes on contiguous arcs'}</span></label>
            <label className="geometry-arc-import-toggle"><input type="checkbox" checked={removeExternalArcs} disabled={busy !== null || !inspectionCurrent}
              onChange={event => setRemoveExternalArcs(event.currentTarget.checked)} />
              <span>{zh ? '移除外部弧段' : 'Remove external arcs'}</span></label>
            <p className="geometry-muted">{zh ? '当前 Host 导入契约不传递图层属性，也不提供属性冲突合并；原对话框的元数据优先级选项在此禁用。坐标由 Host 按目标几何工程投影读取。' : 'The current Host import contract does not carry layer attributes or merge attribute conflicts, so the original metadata-priority option is unavailable. The Host reads coordinates into the target geometry project projection.'}</p>
            <div className="geometry-arc-import-actions"><button type="button" className="geometry-primary" disabled={busy !== null || !canApply || !inspectionCurrent || selectedSources.length === 0 || selectedFeatureCount === 0}
              onClick={() => void apply()}>{busy === 'apply' ? (zh ? '正在应用到几何草稿…' : 'Applying to geometry draft…') : (zh ? `应用 ${selectedSources.length} 个来源 / ${selectedFeatureCount} 个要素` : `Apply ${selectedSources.length} sources / ${selectedFeatureCount} features`)}</button>
            </div>
          </section>}
          {inspectionCurrent && !canApply && <p className="geometry-arc-import-warning">{zh ? '当前 Host 未发布可编辑的几何导入能力；来源预览为只读，不能应用到草稿。' : 'The current Host does not advertise editable geometry import; this source preview is read-only and cannot be applied to the draft.'}</p>}
        </>}

    {applied && <section className="geometry-arc-import-receipt" role="status"><strong>{zh ? '几何草稿回执已核对' : 'Geometry draft result verified'}</strong>
      <dl><div><dt>{zh ? '输入源修订' : 'Input source revision'}</dt><dd><code>{applied.sourceCapture.revision}</code></dd></div>
        <div><dt>{zh ? '新草稿身份' : 'New draft identity'}</dt><dd><code>{applied.result.completion?.draftIdentity ?? '—'}</code></dd></div>
        <div><dt>{zh ? '导入来源' : 'Imported sources'}</dt><dd>{applied.imported.length}</dd></div>
        <div><dt>{zh ? '处理模式' : 'Mode'}</dt><dd>{applied.mode}</dd></div>
        <div><dt>{zh ? '合并连续弧段' : 'Merge contiguous arcs'}</dt><dd>{applied.mergeContiguousArcs ? (zh ? '是' : 'Yes') : (zh ? '否' : 'No')}</dd></div>
        <div><dt>{zh ? '移除外部弧段' : 'Remove external arcs'}</dt><dd>{applied.removeExternalArcs ? (zh ? '是' : 'Yes') : (zh ? '否' : 'No')}</dd></div></dl>
      <p>{zh ? '结果已进入 Geometry 页面的同一拓扑草稿与撤销历史；使用页面“保存”按钮后才会写入模型文件。' : 'The result is in the same Geometry topology draft and undo history. It reaches the model file only when you use the page Save action.'}</p>
    </section>}
    {notice && <p className="geometry-arc-import-notice" role="status">{notice}</p>}
    {error && <div className="geometry-error geometry-arc-import-error" role="alert"><strong>{error}</strong>{detail && <details><summary>{zh ? '原始 Host 诊断' : 'Raw Host diagnostic'}</summary><pre>{detail}</pre></details>}</div>}
    <footer className="geometry-arc-import-footer"><span className="geometry-muted">{zh ? '源修订与草稿身份会在检查、排队应用和回执确认时再次核对。' : 'Source revision and draft identity are rechecked at inspection, queued apply, and result adoption.'}</span>
      <span className="geometry-arc-import-footer-actions">{busy && <button type="button" className="geometry-secondary" onClick={cancel}>{zh ? '请求取消' : 'Request cancellation'}</button>}
        <button type="button" className="geometry-secondary" disabled={busy !== null} onClick={onClose}>{zh ? '关闭' : 'Close'}</button></span></footer>
  </dialog>;
}
