import { useEffect, useMemo, useRef, useState } from 'react';
import type { BackendGateway, Language, WorkbenchContext } from '../../../shared/workbench';
import { errorDiagnostics, message, text } from '../../../shared/workbench';
import type { LoadedMapDocumentCatalog } from '../../map/ProjectTreeListener';
import type { MapDocumentTab } from '../../map/mapTypes';
import { ShapefileAttributeTableViewer } from './ShapefileAttributeTableViewer';
import { NewShapefileDialog } from './NewShapefileDialog';
import { VectorImporterDialog } from './VectorImporterDialog';
import { sameSourcePath } from '../attributes/attributeTableTypes';
import { captureMapOwner, inspectShapefileFamily, isShapefileMapImportReceipt, isNewShapefileCreateReceipt,
  sameMapSourceFamily, type NewShapefileCreateReceipt, type ShapefileFamilyCapture, type ShapefileMapImportReceipt } from './vectorImportTypes';
import './vectorImport.css';

interface PendingMapMutation {
  sourcePath: string;
  mapID: string;
  mapIndex: number;
  mapName: string;
  startedAtCatalogVersion: number;
}

type CreationVerification = { status: 'checking' } | { status: 'verified'; family: ShapefileFamilyCapture } |
  { status: 'invalid'; message: string };

export interface VectorImportOutcome {
  status: 'created' | 'awaiting-map-refresh' | 'imported' | 'retry-ready' | 'uncertain';
  sourcePath?: string;
  message?: string;
}

export interface VectorImporterProps {
  open: boolean;
  language: Language;
  context: WorkbenchContext;
  backend: BackendGateway;
  catalog?: LoadedMapDocumentCatalog;
  catalogReadVersion: number;
  hasUnsavedDrafts?: boolean;
  createdReceipt: NewShapefileCreateReceipt | null;
  importOutcome: VectorImportOutcome | null;
  onCreatedReceipt: (receipt: NewShapefileCreateReceipt | null) => void;
  onImportOutcome: (outcome: VectorImportOutcome | null) => void;
  onRefreshCatalog: () => void;
  onClose: () => void;
}

function diagnostic(error: unknown): string {
  const value = errorDiagnostics(error);
  if (typeof value === 'string') return value;
  if (value !== undefined) {
    try { return JSON.stringify(value, null, 2); } catch { return String(value); }
  }
  return error instanceof Error ? error.message : String(error);
}

function treeContainsPath(nodes: MapDocumentTab['roots'], sourcePath: string): boolean {
  return nodes.some(node => {
    const locator = node.locator;
    if (locator?.nodeType === 'MapLayer' && locator.filename && sameSourcePath(locator.filename, sourcePath)) return true;
    return Boolean(node.children?.length && treeContainsPath(node.children, sourcePath));
  });
}

function importReceiptMatches(receipt: ShapefileMapImportReceipt, target: MapDocumentTab,
  expectedPreviousRevision: string): boolean {
  return isShapefileMapImportReceipt(receipt, target, expectedPreviousRevision);
}

export function VectorImporter({ open, language, context, backend, catalog, catalogReadVersion,
  hasUnsavedDrafts, createdReceipt, importOutcome, onCreatedReceipt, onImportOutcome, onRefreshCatalog, onClose }: VectorImporterProps) {
  const zh = language === 'zh';
  const maps = useMemo(() => catalog?.documents ?? [], [catalog]);
  const ownerBase = JSON.stringify([context.projectSessionID, context.projectPath, context.projectRevision, context.projectXml]);
  const ownerRef = useRef(ownerBase); ownerRef.current = ownerBase;
  const generation = useRef(0);
  const controller = useRef<AbortController | null>(null);
  const [targetMapID, setTargetMapID] = useState('');
  const [sourcePath, setSourcePath] = useState('');
  const [inspection, setInspection] = useState<{ sourcePath: string; family: ShapefileFamilyCapture }>();
  const [busy, setBusy] = useState<'inspect' | 'import' | 'verify-created' | null>(null);
  const [error, setError] = useState<string>();
  const [detail, setDetail] = useState<string>();
  const [notice, setNotice] = useState<string>();
  const [creationVerification, setCreationVerification] = useState<CreationVerification>();
  const [newDialogOpen, setNewDialogOpen] = useState(false);
  const [createDialogMap, setCreateDialogMap] = useState<MapDocumentTab>();
  const [createRequestPinned, setCreateRequestPinned] = useState(false);
  const [importReceipt, setImportReceipt] = useState<ShapefileMapImportReceipt>();
  const [pendingMapMutation, setPendingMapMutation] = useState<PendingMapMutation>();
  const [awaitingMapRefresh, setAwaitingMapRefresh] = useState(false);
  const [reconciledImportExists, setReconciledImportExists] = useState(false);

  const targetMap = maps.find(map => map.mapID === targetMapID);
  const newDialogMap = createDialogMap ?? targetMap;
  const capabilities = useMemo(() => ({
    inspect: backend.supports('workspace.file.inspect') && backend.supports('map.vector.captureShapefile'),
    table: backend.supports('attributeTable.load'),
    create: backend.supports('map.shapefile.create'),
    import: backend.supports('map.vector.importShapefile'),
    maps: backend.supports('map.document.list') && backend.supports('map.tree.load'),
  }), [backend]);
  const blockedByDraft = Boolean(hasUnsavedDrafts);

  useEffect(() => {
    if (targetMapID && maps.some(map => map.mapID === targetMapID)) return;
    const preferred = maps.find(map => map.mapIndex === catalog?.activeMapIndex) ?? maps[0];
    setTargetMapID(preferred?.mapID ?? '');
  }, [catalog?.activeMapIndex, catalogReadVersion, maps, targetMapID]);

  useEffect(() => {
    generation.current += 1;
    controller.current?.abort();
    controller.current = null;
    setInspection(undefined); setBusy(null); setError(undefined); setDetail(undefined); setNotice(undefined);
    setImportReceipt(undefined); setPendingMapMutation(undefined); setAwaitingMapRefresh(false); setReconciledImportExists(false);
    onImportOutcome(null);
  }, [ownerBase]);

  useEffect(() => {
    if (catalogReadVersion <= 0) return;
    generation.current += 1;
    controller.current?.abort();
    controller.current = null;
    setInspection(undefined); setBusy(null); setError(undefined); setDetail(undefined);
    setImportReceipt(undefined); setReconciledImportExists(false);
    // Keep pendingMapMutation so the freshly loaded map tree can reconcile it.
  }, [catalogReadVersion]);

  useEffect(() => () => {
    generation.current += 1;
    controller.current?.abort();
    controller.current = null;
  }, []);

  useEffect(() => {
    if (!pendingMapMutation || !catalog || catalogReadVersion <= pendingMapMutation.startedAtCatalogVersion) return;
    const map = catalog.documents.find(document => document.mapID === pendingMapMutation.mapID &&
      document.mapIndex === pendingMapMutation.mapIndex && document.name === pendingMapMutation.mapName);
    if (!map) {
      setAwaitingMapRefresh(true);
      setNotice(text(language, '重新读取的地图目录未确认目标地图；导入保持锁定。',
        'The refreshed map catalog did not confirm the target map; imports remain locked.'));
      return;
    }
    if (treeContainsPath(map.roots, pendingMapMutation.sourcePath)) {
      setReconciledImportExists(true); setAwaitingMapRefresh(false); setPendingMapMutation(undefined);
      setNotice(text(language, '重新读取的地图树已确认该 Shapefile 路径存在于目标地图中；未重复提交。',
        'The refreshed map tree confirms this Shapefile path is already in the target map; no duplicate import was submitted.'));
      onImportOutcome({ status: 'imported', sourcePath: pendingMapMutation.sourcePath });
    } else {
      setReconciledImportExists(false); setAwaitingMapRefresh(false); setPendingMapMutation(undefined);
      setNotice(text(language, '重新读取的目标地图树中没有该来源路径；可以使用当前文件族修订重新尝试导入。',
        'The refreshed target map tree contains no matching source path; the import can be retried with the current family revision.'));
      onImportOutcome({ status: 'retry-ready', sourcePath: pendingMapMutation.sourcePath });
    }
  }, [catalog, catalogReadVersion, language, onImportOutcome, pendingMapMutation]);

  const requestOwner = (): { id: number; signal: AbortSignal; owner: string } => {
    controller.current?.abort();
    const requestController = new AbortController();
    controller.current = requestController;
    return { id: ++generation.current, signal: requestController.signal, owner: ownerBase };
  };

  const isCurrent = (request: { id: number; signal: AbortSignal; owner: string }): boolean =>
    generation.current === request.id && ownerRef.current === request.owner && !request.signal.aborted;

  const inspectSource = async (): Promise<void> => {
    const requestedPath = sourcePath.trim();
    if (!requestedPath || busy || awaitingMapRefresh) return;
    if (!capabilities.inspect) {
      setError(text(language, '当前服务无法完整检查 Shapefile 来源；导入已禁用。',
        'The current service cannot inspect the complete Shapefile source; import is disabled.'));
      return;
    }
    const request = requestOwner();
    setBusy('inspect'); setError(undefined); setDetail(undefined); setNotice(undefined); setInspection(undefined);
    try {
      const inspected = await inspectShapefileFamily(backend, requestedPath, request.signal);
      if (!isCurrent(request)) return;
      setInspection({ sourcePath: requestedPath, family: inspected.family });
      setNotice(text(language, '已检查 Shapefile 来源文件族。可以读取只读属性表，或导入到所选地图。',
        'The Shapefile source family has been checked. Load a read-only table or import it into the selected map.'));
    } catch (caught) {
      if (request.signal.aborted) {
        if (ownerRef.current === request.owner) setNotice(text(language, '来源检查已取消；未继续导入。', 'Source inspection was cancelled; import did not continue.'));
      } else if (isCurrent(request)) {
        setError(message(caught, language)); setDetail(diagnostic(caught));
      }
    } finally {
      if (generation.current === request.id) setBusy(null);
    }
  };

  const importSource = async (path: string, expectedFamily: ShapefileFamilyCapture, label: 'selected' | 'created'): Promise<void> => {
    if (!targetMap || busy || awaitingMapRefresh || reconciledImportExists) return;
    if (blockedByDraft) {
      setError(text(language, '存在未保存的工程编辑；请先保存或丢弃草稿，再更新地图目录。',
        'Unsaved project edits are active; save or discard them before refreshing and mutating the map.'));
      return;
    }
    if (!capabilities.import || !capabilities.inspect || !capabilities.maps) {
      setError(text(language, '当前服务无法同时读取目标地图、检查 Shapefile 文件族并执行地图导入；导入已禁用。',
        'The current service cannot read the target map, check the Shapefile family, and import it together; importing is disabled.'));
      return;
    }
    const currentMap = targetMap;
    const request = requestOwner();
    setBusy('import'); setError(undefined); setDetail(undefined); setNotice(undefined); setImportReceipt(undefined);
    setReconciledImportExists(false);
    let mutationSubmitted = false;
    try {
      const source = await inspectShapefileFamily(backend, path, request.signal);
      if (!isCurrent(request)) return;
      if (!sameMapSourceFamily(expectedFamily, source.family)) {
        throw new Error(text(language, '来源文件族相对预览/创建回执已变化；导入停止，请重新检查来源。',
          'The source family changed since its preview or creation receipt; import stopped. Inspect the source again.'));
      }
      const mapOwner = await captureMapOwner(backend, context, currentMap, request.signal);
      if (!isCurrent(request)) return;
      const payload = {
        map: mapOwner.request,
        sourcePath: source.family.sourcePath,
        expectedProjectRevision: context.projectRevision,
        expectedFamily: source.family,
      };
      mutationSubmitted = true;
      const response: unknown = await backend.request('map.vector.importShapefile', payload, { signal: request.signal });
      if (!isCurrent(request)) return;
      if (!importReceiptMatches(response as ShapefileMapImportReceipt, currentMap, mapOwner.visualizationRevision)) {
        throw new Error(text(language, '地图导入结果与目标地图或前一修订不一致；地图状态需要重新读取。',
          'The map import result does not match the target map or previous revision; the map state must be reloaded.'));
      }
      const receipt = response as ShapefileMapImportReceipt;
      setImportReceipt(receipt);
      setPendingMapMutation({ sourcePath: source.family.sourcePath, mapID: currentMap.mapID,
        mapIndex: currentMap.mapIndex, mapName: currentMap.name, startedAtCatalogVersion: catalogReadVersion });
      setAwaitingMapRefresh(true);
      setNotice(text(language, label === 'created'
        ? '新建文件族未重写。服务已返回地图导入结果；请重新读取地图树以确认结果。'
        : '服务已返回地图导入结果；请重新读取地图树以确认来源路径。',
      label === 'created'
        ? 'The created file family was not rewritten. The service returned a map-import result; reload the map tree to confirm it.'
        : 'The service returned a map-import result; reload the map tree to confirm the source path.'));
      onImportOutcome({ status: 'awaiting-map-refresh', sourcePath: source.family.sourcePath });
    } catch (caught) {
      const stillOwnsView = isCurrent(request);
      if (mutationSubmitted && stillOwnsView) {
        setPendingMapMutation({ sourcePath: expectedFamily.sourcePath, mapID: currentMap.mapID,
          mapIndex: currentMap.mapIndex, mapName: currentMap.name, startedAtCatalogVersion: catalogReadVersion });
        setAwaitingMapRefresh(true);
        onImportOutcome({ status: 'uncertain', sourcePath: expectedFamily.sourcePath,
          message: text(language, '导入结果未确认；请重新读取地图树后再决定是否重试。',
            'The import result is unconfirmed; reload the map tree before deciding whether to retry.') });
      }
      if (request.signal.aborted) {
        if (ownerRef.current === request.owner) setNotice(text(language, mutationSubmitted
          ? '导入已请求取消，但服务可能已提交；先重新读取地图目录并核对来源路径。'
          : '导入前检查已取消；没有提交地图变更。',
        mutationSubmitted
          ? 'Import cancellation was requested, but the service may have committed it; reload the map catalog and check the source path first.'
          : 'Pre-import checks were cancelled; no map mutation was submitted.'));
      } else if (isCurrent(request)) {
        setError(message(caught, language)); setDetail(diagnostic(caught));
        if (mutationSubmitted) setNotice(text(language,
          '保留现有 Shapefile 创建回执；请先重新读取地图目录。若地图树确认来源尚未导入，可使用同一文件族回执重试，不会再次创建或覆盖文件。',
          'The existing Shapefile receipt is retained. Reload the map catalog first; if the map tree confirms it was not imported, retry using the same family receipt without recreating or overwriting files.'));
      }
    } finally {
      if (generation.current === request.id) setBusy(null);
    }
  };

  const verifyCreatedReceipt = async (): Promise<void> => {
    const receipt = createdReceipt;
    if (!receipt || busy) return;
    if (!isNewShapefileCreateReceipt(receipt) ||
        receipt.projectSessionID.toLocaleLowerCase() !== context.projectSessionID.toLocaleLowerCase() ||
        !sameSourcePath(receipt.projectPath, context.projectPath ?? '') ||
        receipt.projectRevision.toLocaleUpperCase() !== (context.projectRevision ?? '').toLocaleUpperCase()) {
      setCreationVerification({ status: 'invalid', message: text(language,
        '创建回执属于其他工程会话或工程修订；保留回执供核对，但不会从当前上下文导入。',
        'The creation receipt belongs to another project session or revision. It is retained for review but will not be imported from this context.') });
      return;
    }
    const request = requestOwner();
    setBusy('verify-created'); setCreationVerification({ status: 'checking' }); setError(undefined); setDetail(undefined);
    try {
      const current = await inspectShapefileFamily(backend, receipt.family.sourcePath, request.signal);
      if (!isCurrent(request)) return;
      if (!sameMapSourceFamily(receipt.family, current.family)) {
        throw new Error(text(language, '已创建 Shapefile 文件族相对创建回执发生变化，暂停导入。',
          'The created Shapefile family differs from its creation receipt; import is paused.'));
      }
      setCreationVerification({ status: 'verified', family: current.family });
      onImportOutcome({ status: 'created', sourcePath: current.family.sourcePath });
    } catch (caught) {
      if (request.signal.aborted) return;
      if (isCurrent(request)) setCreationVerification({ status: 'invalid', message: message(caught, language) });
    } finally {
      if (generation.current === request.id) setBusy(null);
    }
  };

  useEffect(() => {
    if (!createdReceipt) {
      setCreationVerification(undefined);
      return;
    }
    void verifyCreatedReceipt();
    // `verifyCreatedReceipt` captures the current owner and receipt; catalogReadVersion
    // deliberately triggers a fresh source-family verification after map reconciliation.
  }, [createdReceipt?.operationID, context.projectSessionID, context.projectPath, context.projectRevision,
    catalogReadVersion, backend]);

  const sourceImportDisabled = !targetMap || !inspection || busy !== null || awaitingMapRefresh ||
    reconciledImportExists || blockedByDraft || !capabilities.import;
  const createdImportDisabled = !targetMap || !createdReceipt || creationVerification?.status !== 'verified' ||
    busy !== null || awaitingMapRefresh || reconciledImportExists || blockedByDraft || !capabilities.import ||
    createdReceipt.mapID.toLocaleLowerCase() !== targetMap.mapID.toLocaleLowerCase() ||
    createdReceipt.mapIndex !== targetMap.mapIndex || createdReceipt.mapName !== targetMap.name || createdReceipt.mapType !== targetMap.mapType;
  const createdPathConfirmed = Boolean(createdReceipt && importOutcome?.status === 'imported' && importOutcome.sourcePath &&
    sameSourcePath(importOutcome.sourcePath, createdReceipt.family.sourcePath));

  return <>
    <VectorImporterDialog open={open} busy={busy !== null} language={language}
      title={text(language, '新建或导入矢量', 'Create or import vector data')}
      description={text(language, '使用服务端可访问路径；浏览器本地文件上传继续使用地图页现有功能。', 'Use paths accessible to the service. Browser-local uploads remain in the existing map-page workflow.')}
      onClose={onClose}>
        {!context.projectPath || !context.projectRevision || context.projectXml === null
          ? <p className="vector-import-warning">{zh ? '请先打开并保存工程。' : 'Open and save a project first.'}</p>
          : !catalog || catalog.projectRevision.toLocaleUpperCase() !== context.projectRevision.toLocaleUpperCase()
            ? <p className="vector-import-warning">{zh ? '尚无与当前工程修订匹配的地图目录；请关闭此窗口并读取工程地图目录。' : 'No map catalog matches the current project revision. Close this dialog and load the project map catalog.'}</p>
            : maps.length === 0
              ? <p className="vector-import-warning">{zh ? '当前工程没有已保存地图。为避免创建隐式的目标地图，新建或导入矢量操作已禁用。' : 'The project has no saved maps. Vector create/import actions are disabled instead of selecting an implicit target map.'}</p>
              : <>
                <div className="vector-import-target-grid">
                  <label><span>{zh ? '工程地图目录中的目标地图' : 'Target map from the project catalog'}</span>
                    <select value={targetMap?.mapID ?? ''} disabled={busy !== null || awaitingMapRefresh || createRequestPinned || Boolean(createDialogMap)}
                      onChange={event => setTargetMapID(event.target.value)}>{maps.map(map => <option key={map.mapID} value={map.mapID}>
                        #{map.mapIndex} · {map.name} · {map.mapType}
                      </option>)}</select></label>
                  {targetMap && <dl className="vector-import-map-identity"><div><dt>{zh ? '目标地图' : 'Target map'}</dt><dd>{targetMap.name} · {targetMap.mapType} · #{targetMap.mapIndex}</dd></div>
                  </dl>}
                  {targetMap && <details className="vector-import-map-revision"><summary>{zh ? '地图版本详情' : 'Map revision details'}</summary>
                    <code>{targetMap.visualizationRevision ?? '—'}</code></details>}
                </div>
                <div className="vector-import-section-heading"><div><span className="tools-kicker">01 / {zh ? '导入现有 Shapefile' : 'IMPORT EXISTING SHAPEFILE'}</span>
                  <h4>{zh ? '服务器来源检查' : 'Server source inspection'}</h4></div></div>
                <p className="tools-muted">{zh ? '此入口只接受服务端可访问的路径；浏览器本地路径不能直接作为服务端文件。来源检查会核验必需的 .shp/.shx/.dbf 及存在时的 .prj。' : 'This entry accepts paths accessible to the service; browser-local paths are not treated as server files. Source checks verify the required .shp/.shx/.dbf files and any existing .prj.'}</p>
                <div className="vector-import-source-form">
                  <label><span>{zh ? 'Shapefile 服务器路径 (.shp)' : 'Server Shapefile path (.shp)'}</span>
                    <input value={sourcePath} disabled={busy !== null || awaitingMapRefresh} onChange={event => {
                      setSourcePath(event.target.value); setInspection(undefined); setReconciledImportExists(false);
                    }}
                      placeholder={zh ? '/server/data/roads.shp' : '/server/data/roads.shp'} /></label>
                  <div className="ci-actions">
                    <button type="button" className="ci-button" disabled={busy !== null || awaitingMapRefresh || !sourcePath.trim() || !capabilities.inspect}
                      onClick={() => void inspectSource()}>{busy === 'inspect' ? (zh ? '正在检查文件族…' : 'Inspecting file family…') : (zh ? '检查来源文件族' : 'Inspect source family')}</button>
                    {busy !== null && <button type="button" className="ci-button" onClick={() => controller.current?.abort()}>{zh ? '请求取消' : 'Request cancellation'}</button>}
                    <button type="button" className="ci-button ci-primary" disabled={sourceImportDisabled}
                      onClick={() => inspection && void importSource(inspection.family.sourcePath, inspection.family, 'selected')}>
                      {busy === 'import' ? (zh ? '正在导入…' : 'Importing…') : (zh ? '导入到所选地图' : 'Import into selected map')}</button>
                    <button type="button" className="ci-button" disabled={busy !== null || awaitingMapRefresh || !targetMap || blockedByDraft || !capabilities.create || Boolean(createdReceipt)}
                      onClick={() => { if (targetMap) { setCreateDialogMap(targetMap); setNewDialogOpen(true); } }}>{zh ? '新建空 Shapefile' : 'Create empty Shapefile'}</button>
                  </div>
                </div>
                {inspection && <div className="vector-import-capture-card"><strong>{zh ? '来源已检查' : 'Source checked'}</strong>
                  <dl className="vector-import-metadata"><div><dt>{zh ? '规范路径' : 'Canonical path'}</dt><dd><code>{inspection.family.sourcePath}</code></dd></div>
                    <div><dt>{zh ? '来源文件身份项' : 'Source-family identities'}</dt><dd>{inspection.family.files.filter(file => file.exists).length} / {inspection.family.files.length}</dd></div></dl>
                  <details><summary>{zh ? '查看来源文件身份' : 'View source-file identities'}</summary>
                    <p>{zh ? '文件族修订：' : 'Family revision: '}<code>{inspection.family.revision}</code></p>
                    <ul className="vector-import-family-list">{inspection.family.files.map(file => <li key={file.role}><span>{file.role} · {file.required ? (zh ? '必需' : 'required') : (zh ? '可选' : 'optional')}</span>
                      <code>{file.path}</code><code>{file.revision ?? (zh ? '不存在 / 无修订' : 'Missing / no revision')}</code></li>)}</ul></details>
                </div>}
                {inspection && capabilities.table && <ShapefileAttributeTableViewer key={`${inspection.family.revision}:${catalogReadVersion}`}
                  language={language} backend={backend} context={context} sourcePath={inspection.family.sourcePath} expectedFamily={inspection.family} />}
                {createdReceipt && <section className="vector-import-created-receipt" aria-label={zh ? '新建 Shapefile 回执' : 'New Shapefile receipt'}>
                  <div className="vector-import-section-heading"><div><span className="tools-kicker">02 / {zh ? '已创建文件族回执' : 'CREATED FILE-FAMILY RECEIPT'}</span>
                    <h4>{zh ? createdReceipt.shapeType + ' Shapefile' : createdReceipt.shapeType + ' Shapefile'}</h4></div>
                    <span className={`vector-import-readonly-pill ${creationVerification?.status === 'verified' ? 'is-ready' : ''}`}>
                  {creationVerification?.status === 'checking' ? (zh ? '正在复核' : 'Verifying') : creationVerification?.status === 'verified' ? (zh ? '文件族一致' : 'Family verified') : (zh ? '回执待复核' : 'Receipt needs verification')}</span></div>
                  <details><summary>{zh ? '创建回执身份与来源版本' : 'Creation receipt identity and source revision'}</summary>
                    <dl className="vector-import-metadata"><div><dt>{zh ? '创建操作身份' : 'Create operation ID'}</dt><dd><code>{createdReceipt.operationID}</code></dd></div>
                      <div><dt>{zh ? '目标路径' : 'Destination path'}</dt><dd><code>{createdReceipt.destinationPath}</code></dd></div>
                      <div><dt>{zh ? '文件族修订' : 'Family revision'}</dt><dd><code>{createdReceipt.family.revision}</code></dd></div>
                      <div><dt>{zh ? '创建时地图修订' : 'Map revision at create'}</dt><dd><code>{createdReceipt.visualizationRevision ?? '—'}</code></dd></div></dl>
                  </details>
                  {creationVerification?.status === 'invalid' && <p className="vector-import-warning">{creationVerification.message}</p>}
                  <div className="ci-actions"><button type="button" className="ci-button" disabled={busy !== null || blockedByDraft}
                    onClick={() => void verifyCreatedReceipt()}>{zh ? '复核已创建文件族' : 'Recheck created family'}</button>
                    {createdPathConfirmed && <button type="button" className="ci-button" onClick={() => {
                      onCreatedReceipt(null); onImportOutcome(null); setCreationVerification(undefined); setImportReceipt(undefined);
                    }}>{zh ? '清除已确认回执' : 'Clear confirmed receipt'}</button>}
                    <button type="button" className="ci-button ci-primary" disabled={createdImportDisabled}
                      onClick={() => void importSource(createdReceipt.family.sourcePath, creationVerification?.status === 'verified' ? creationVerification.family : createdReceipt.family, 'created')}>
                      {zh ? '将此回执文件导入目标地图' : 'Import this receipted file into target map'}</button></div>
                  {creationVerification?.status === 'verified' && <ShapefileAttributeTableViewer key={`created:${creationVerification.family.revision}:${catalogReadVersion}`}
                    language={language} backend={backend} context={context} sourcePath={creationVerification.family.sourcePath} expectedFamily={creationVerification.family} />}
                  <p className="tools-muted">{zh ? '导入会复用此创建回执。地图导入失败或结果不确定时，不会再次创建或覆盖此文件。先重新读取地图树，再决定是否重试。' : 'Import reuses this creation receipt. Failed or uncertain map import never recreates or overwrites the file; reload the map tree before retrying.'}</p>
                </section>}
                {importReceipt && <div className="vector-import-receipt" role="status"><strong>{zh ? '地图导入结果' : 'Map import result'}</strong>
                  <span>#{importReceipt.mapIndex} · {importReceipt.mapName} · {importReceipt.mapType}</span>
                  <small>{zh ? '前一修订' : 'Previous revision'} <code>{importReceipt.previousVisualizationRevision}</code></small>
                  <small>{zh ? '当前修订' : 'Current revision'} <code>{importReceipt.visualizationRevision}</code></small>
                  <button type="button" className="ci-button" disabled={busy !== null} onClick={onRefreshCatalog}>{zh ? '重新读取地图目录 / 树' : 'Reload map catalog / tree'}</button>
                </div>}
                {awaitingMapRefresh && <div className="vector-import-warning" role="status">
                  <strong>{zh ? '地图结果需复核' : 'Map result requires reconciliation'}</strong>
                  <p>{zh ? '任何已提交或结果不确定的导入后，都先重新读取工程地图目录与目标地图树。确认来源已存在时停止重试；确认不存在时才可复用原文件族修订重试。' : 'After a submitted or uncertain import, reload the project catalog and target map tree. Stop retrying if the source is present; retry with the same family revision only after confirming it is absent.'}</p>
                  <button type="button" className="ci-button ci-primary" disabled={busy !== null} onClick={onRefreshCatalog}>{zh ? '重新读取地图目录与树' : 'Reload map catalog and tree'}</button>
                </div>}
                {reconciledImportExists && <p className="vector-import-notice">{zh ? '此 Shapefile 已在地图树中确认；为避免重复图层，导入动作已锁定。' : 'The map tree confirms this Shapefile is already attached; import is locked to prevent duplicates.'}</p>}
                {blockedByDraft && <p className="vector-import-warning">{zh ? '工程存在未保存草稿，地图源变更暂不可用。' : 'Unsaved project drafts are active; map-source mutations are unavailable.'}</p>}
              </>}
        {(capabilities.import || capabilities.create || capabilities.inspect) && <details className="vector-import-capability-list"><summary>{zh ? '服务能力详情' : 'Service capability details'}</summary>
          {['map.document.list', 'map.tree.load', 'workspace.file.inspect', 'map.vector.captureShapefile', 'attributeTable.load',
            'map.vector.importShapefile', 'map.shapefile.create'].map(method => <div key={method}><code>{method}</code>
              <span className={backend.supports(method) ? 'is-ready' : ''}>{backend.supports(method) ? (zh ? '可用' : 'Available') : (zh ? '未发布 · 已禁用' : 'Not advertised · disabled')}</span></div>)}
        </details>}
        {notice && <p className="vector-import-notice" role="status">{notice}</p>}
        {error && <div className="vector-import-error" role="alert"><strong>{error}</strong>{detail && <details><summary>{zh ? '原始 Host 诊断' : 'Raw Host diagnostic'}</summary><pre>{detail}</pre></details>}</div>}
    </VectorImporterDialog>
    {newDialogMap && <NewShapefileDialog open={newDialogOpen && open} language={language} context={context} backend={backend}
      targetMap={newDialogMap} onClose={() => {
        setNewDialogOpen(false);
        if (!createRequestPinned) setCreateDialogMap(undefined);
      }} onRequestStateChange={pending => {
        setCreateRequestPinned(pending);
        if (!pending) setCreateDialogMap(undefined);
      }} onCreated={receipt => {
        onCreatedReceipt(receipt);
        onImportOutcome({ status: 'created', sourcePath: receipt.family.sourcePath });
        setNewDialogOpen(false); setCreateRequestPinned(false); setCreateDialogMap(undefined);
        setNotice(text(language, '已保存创建回执；将先复核文件族身份，再开放导入。',
          'The creation receipt is stored. The family identity will be rechecked before import is enabled.'));
      }} />}
  </>;
}
