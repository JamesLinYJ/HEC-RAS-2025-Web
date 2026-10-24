import { useEffect, useRef, useState } from 'react';
import type { BackendGateway, Language, WorkbenchContext } from '../../../shared/workbench';
import { errorDiagnostics, message, text } from '../../../shared/workbench';
import type { MapDocumentTab } from '../../map/mapTypes';
import { sameSourcePath } from '../attributes/attributeTableTypes';
import { captureMapOwner, isNewShapefileCreateReceipt, type NewShapefileCreateReceipt } from './vectorImportTypes';

type ShapeType = NewShapefileCreateReceipt['shapeType'];
type ProjectionChoice = NewShapefileCreateReceipt['projectionChoice'];

interface CreateRequest {
  map: Awaited<ReturnType<typeof captureMapOwner>>['request'];
  expectedProjectRevision: string;
  projectSessionID: string;
  mapID: string;
  operationID: string;
  destinationPath: string;
  shapeType: ShapeType;
  projectionChoice: ProjectionChoice;
}

interface Props {
  open: boolean;
  language: Language;
  context: WorkbenchContext;
  backend: BackendGateway;
  targetMap: MapDocumentTab;
  onClose: () => void;
  onRequestStateChange: (pending: boolean) => void;
  onCreated: (receipt: NewShapefileCreateReceipt) => void;
}

function newOperationID(): string {
  if (!globalThis.crypto?.randomUUID) throw new Error('当前浏览器不支持安全的 Shapefile 创建请求身份。');
  return globalThis.crypto.randomUUID();
}

function rawDiagnostic(error: unknown): string {
  const value = errorDiagnostics(error);
  if (typeof value === 'string') return value;
  if (value !== undefined) {
    try { return JSON.stringify(value, null, 2); } catch { return String(value); }
  }
  return error instanceof Error ? error.message : String(error);
}

function normalizedAbsoluteDestination(value: string): string | undefined {
  const path = value.trim().replaceAll('\\', '/');
  const drive = /^[a-z]:\//i.test(path);
  const unc = path.startsWith('//') && !path.startsWith('///');
  const posix = path.startsWith('/') && !unc;
  if (!drive && !unc && !posix) return undefined;
  if (!path.endsWith('.shp')) return undefined;
  const segments = (drive ? path.slice(3) : unc ? path.slice(2) : path.slice(1)).split('/');
  if (segments.some(segment => !segment || segment === '.' || segment === '..')) return undefined;
  if (unc && segments.length < 3) return undefined;
  return path;
}

function requestMatchesContext(request: CreateRequest, context: WorkbenchContext, targetMap: MapDocumentTab): boolean {
  return Boolean(context.projectPath && context.projectRevision && context.projectXml !== null &&
    request.projectSessionID.toLocaleLowerCase() === context.projectSessionID.toLocaleLowerCase() &&
    sameSourcePath(request.map.projectPath, context.projectPath) &&
    request.expectedProjectRevision.toLocaleUpperCase() === context.projectRevision.toLocaleUpperCase() &&
    request.map.projectXml === context.projectXml &&
    request.map.mapID.toLocaleLowerCase() === targetMap.mapID.toLocaleLowerCase() &&
    request.map.mapIndex === targetMap.mapIndex && request.map.mapName === targetMap.name && request.map.mapType === targetMap.mapType);
}

function matchesReceipt(receipt: NewShapefileCreateReceipt, request: CreateRequest): boolean {
  return receipt.operationID.toLocaleLowerCase() === request.operationID.toLocaleLowerCase() &&
    receipt.projectSessionID.toLocaleLowerCase() === request.projectSessionID.toLocaleLowerCase() &&
    receipt.mapID.toLocaleLowerCase() === request.mapID.toLocaleLowerCase() &&
    sameSourcePath(receipt.projectPath, request.map.projectPath) &&
    receipt.projectRevision.toLocaleUpperCase() === request.expectedProjectRevision.toLocaleUpperCase() &&
    (receipt.visualizationRevision ?? '').toLocaleUpperCase() ===
      (request.map.expectedVisualizationRevision ?? '').toLocaleUpperCase() &&
    receipt.mapIndex === request.map.mapIndex && receipt.mapName === request.map.mapName &&
    receipt.mapType === request.map.mapType &&
    sameSourcePath(receipt.destinationPath, request.destinationPath) &&
    receipt.shapeType === request.shapeType && receipt.projectionChoice === request.projectionChoice;
}

export function NewShapefileDialog({ open, language, context, backend, targetMap, onClose, onRequestStateChange, onCreated }: Props) {
  const zh = language === 'zh';
  const owner = JSON.stringify([context.projectSessionID, context.projectPath, context.projectRevision,
    context.projectXml, targetMap.id, targetMap.mapID, targetMap.mapIndex, targetMap.name, targetMap.mapType,
    targetMap.visualizationRevision]);
  const ownerRef = useRef(owner); ownerRef.current = owner;
  const generation = useRef(0);
  const controller = useRef<AbortController | null>(null);
  const [destinationPath, setDestinationPath] = useState('');
  const [shapeType, setShapeType] = useState<ShapeType>('Polyline');
  const [projectionChoice, setProjectionChoice] = useState<ProjectionChoice>('Project');
  const [operationID, setOperationID] = useState(() => newOperationID());
  const [submittedRequest, setSubmittedRequest] = useState<CreateRequest>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const [detail, setDetail] = useState<string>();
  const [notice, setNotice] = useState<string>();
  const submittedRequestRef = useRef<CreateRequest | undefined>(undefined);
  submittedRequestRef.current = submittedRequest;

  useEffect(() => {
    generation.current += 1;
    controller.current?.abort();
    controller.current = null;
    setBusy(false); setError(undefined); setDetail(undefined); setNotice(undefined);
    if (!submittedRequestRef.current) {
      setDestinationPath(''); setShapeType('Polyline'); setProjectionChoice('Project');
      setOperationID(newOperationID());
    } else {
      setNotice(text(language, '保留了此前提交的创建请求身份。请返回原工程和目标地图后，用同一请求恢复创建回执。',
        'The submitted creation request identity is retained. Return to its original project and target map to recover the receipt with the same request.'));
    }
  }, [owner]);

  useEffect(() => () => {
    generation.current += 1;
    controller.current?.abort();
    controller.current = null;
  }, []);

  const submit = async (): Promise<void> => {
    if (busy) return;
    if (!backend.supports('map.shapefile.create')) {
      setError(text(language, '当前服务不支持创建 Shapefile。',
        'The current service does not support Shapefile creation.'));
      return;
    }
    const path = destinationPath.trim();
    if (!submittedRequest && !normalizedAbsoluteDestination(path)) {
      setError(text(language, '请输入规范的服务器绝对路径：使用 / 或 \\ 分隔目录，不含 . 或 .. 路径段，并以小写 .shp 结尾。',
        'Enter a normalized absolute server path with no . or .. segments and a lowercase .shp extension.'));
      return;
    }
    if (submittedRequest && !requestMatchesContext(submittedRequest, context, targetMap)) {
      setError(text(language, '此重试请求属于另一工程会话或目标地图。切回原工程和地图后才能恢复创建回执。',
        'This retry request belongs to another project session or target map. Return to the original project and map to recover its receipt.'));
      return;
    }

    const requestOwner = owner;
    controller.current?.abort();
    const requestController = new AbortController();
    controller.current = requestController;
    const requestID = ++generation.current;
    setBusy(true); setError(undefined); setDetail(undefined); setNotice(undefined);
    const isCurrent = (): boolean => generation.current === requestID && ownerRef.current === requestOwner && !requestController.signal.aborted;
    let request: CreateRequest | undefined = submittedRequest;
    try {
      if (!request) {
        const mapOwner = await captureMapOwner(backend, context, targetMap, requestController.signal);
        if (!isCurrent()) return;
        request = {
          map: mapOwner.request,
          expectedProjectRevision: context.projectRevision!,
          projectSessionID: context.projectSessionID,
          mapID: targetMap.mapID,
          operationID,
          destinationPath: normalizedAbsoluteDestination(path)!,
          shapeType,
          projectionChoice,
        };
        setSubmittedRequest(request);
        onRequestStateChange(true);
      }
      if (!request) throw new Error('创建请求未能固定目标地图与路径。');
      const requestToSend = request;
      const response: unknown = await backend.request('map.shapefile.create', requestToSend, { signal: requestController.signal });
      if (!isCurrent()) return;
      if (!isNewShapefileCreateReceipt(response) || !matchesReceipt(response, requestToSend)) {
        throw new Error(text(language, '新建 Shapefile 回执与所提交的工程、地图、路径或创建选项不一致；请勿把它当作已完成。',
          'The new-Shapefile receipt does not match the submitted project, map, path, or creation options; do not treat the operation as complete.'));
      }
      onCreated(response);
      setNotice(text(language, 'Shapefile 文件已创建并生成回执；后续地图导入失败时可使用此回执重试。',
        'The Shapefile was created with a receipt; use that receipt if a later map import needs to be retried.'));
      setSubmittedRequest(undefined); setOperationID(newOperationID()); setDestinationPath('');
      onRequestStateChange(false);
      onClose();
    } catch (caught) {
      if (request) { setSubmittedRequest(request); onRequestStateChange(true); }
      if (requestController.signal.aborted) {
        if (ownerRef.current === requestOwner) {
          setNotice(text(language, '已请求取消，但文件提交可能已完成。保留原请求身份；请重试同一请求以恢复创建回执，不要改用相同路径创建新文件。',
            'Cancellation was requested, but publication may have completed. The original request identity is retained; retry that exact request to recover its receipt instead of creating another file at the same path.'));
        }
      } else if (isCurrent()) {
        setError(message(caught, language)); setDetail(rawDiagnostic(caught));
        if (request) setNotice(text(language, '目标路径和创建参数已冻结；保留创建请求身份，可用完全相同的请求重试并恢复创建回执。',
          'The destination and creation options are frozen. The request identity is retained so an exact retry can recover the receipt.'));
      }
    } finally {
      if (generation.current === requestID) setBusy(false);
    }
  };

  const canSubmit = !busy && (submittedRequest
    ? requestMatchesContext(submittedRequest, context, targetMap)
    : Boolean(normalizedAbsoluteDestination(destinationPath)));

  return <div className="vector-import-backdrop" role="presentation" hidden={!open} aria-hidden={!open}>
    <section className="vector-import-dialog" role="dialog" aria-modal="true" aria-labelledby="new-shapefile-dialog-title">
      <header><div><span className="tools-kicker">{zh ? 'Vector / Shapefile' : 'Vector / Shapefile'}</span>
        <h3 id="new-shapefile-dialog-title">{zh ? '新建 Shapefile' : 'New Shapefile'}</h3>
        <p>{zh ? `目标地图：${targetMap.name} · ${targetMap.mapType} · #${targetMap.mapIndex}` : `Target map: ${targetMap.name} · ${targetMap.mapType} · #${targetMap.mapIndex}`}</p></div>
        <button type="button" className="attribute-close" disabled={busy} onClick={onClose} aria-label={zh ? '关闭' : 'Close'}>×</button></header>
      <p className="vector-import-note">{zh ? '服务只允许在完整 .shp/.shx/.dbf/.prj 文件族均不存在时创建；发生冲突不会覆盖现有文件。创建回执包含实际文件修订和目标地图身份。' : 'The service creates only when the complete .shp/.shx/.dbf/.prj family is absent. Existing files are never overwritten; the receipt carries actual file revisions and target-map identity.'}</p>
      <div className="vector-import-form-grid">
        <label><span>{zh ? '服务器目标路径 (.shp)' : 'Server destination path (.shp)'}</span>
          <input type="text" value={submittedRequest?.destinationPath ?? destinationPath} disabled={busy || Boolean(submittedRequest)}
            placeholder={zh ? '/server/path/new-layer.shp' : '/server/path/new-layer.shp'} onChange={event => setDestinationPath(event.target.value)} /></label>
        <label><span>{zh ? '几何类型' : 'Geometry type'}</span><select value={submittedRequest?.shapeType ?? shapeType} disabled={busy || Boolean(submittedRequest)}
          onChange={event => setShapeType(event.target.value as ShapeType)}><option value="Point">Point</option><option value="Polyline">Polyline</option><option value="Polygon">Polygon</option></select></label>
        <label><span>{zh ? '坐标参考' : 'Coordinate reference'}</span><select value={submittedRequest?.projectionChoice ?? projectionChoice} disabled={busy || Boolean(submittedRequest)}
          onChange={event => setProjectionChoice(event.target.value as ProjectionChoice)}><option value="Project">{zh ? '工程投影' : 'Project projection'}</option><option value="Local">{zh ? '本地工程坐标' : 'Local project coordinates'}</option></select></label>
      </div>
      <div className="vector-import-request-identity"><span>{zh ? '创建请求身份' : 'Creation request identity'}</span><code>{submittedRequest?.operationID ?? operationID}</code></div>
      <footer>
        <button type="button" className="ci-button" disabled={busy} onClick={onClose}>{zh ? '取消 / 关闭' : 'Cancel / close'}</button>
        {busy && <button type="button" className="ci-button" onClick={() => controller.current?.abort()}>{zh ? '请求取消' : 'Request cancellation'}</button>}
        <button type="button" className="ci-button ci-primary" disabled={!canSubmit} onClick={() => void submit()}>
          {busy ? (zh ? '正在创建 Shapefile…' : 'Creating Shapefile…') : submittedRequest ? (zh ? '用相同请求身份重试 / 恢复回执' : 'Retry / recover receipt with same identity') : (zh ? '创建 Shapefile' : 'Create Shapefile')}</button>
      </footer>
      {notice && <p className="vector-import-notice" role="status">{notice}</p>}
      {error && <div className="vector-import-error" role="alert"><strong>{error}</strong>{detail && <details><summary>{zh ? '原始 Host 诊断' : 'Raw Host diagnostic'}</summary><pre>{detail}</pre></details>}</div>}
    </section>
  </div>;
}
