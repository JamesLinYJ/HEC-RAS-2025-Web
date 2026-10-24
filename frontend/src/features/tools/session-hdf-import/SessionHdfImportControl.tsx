import { useEffect, useMemo, useRef, useState } from 'react';
import { errorDiagnostics, message, text } from '../../../shared/workbench';
import type { SessionHdfImportControlProps, SessionHdfInspection, SessionHdfKind, SessionHdfMapCatalog, SessionHdfMapDocument } from './sessionHdfImportTypes';
import {
  currentProjectXmlRevision, parseComposedSessionHdfContext, parseSessionHdfInspection,
  parseSessionHdfMapCatalog, parseSessionHdfMapTreeReceipt, parseWorkspaceFileCapture,
  sessionHdfKindLabel, sessionHdfMethods, supportedMapTypes,
} from './sessionHdfImportWorkflow';
import './sessionHdfImport.css';
import '../coreImportWorkflow.css';

interface ReadState {
  inspection: SessionHdfInspection;
  catalog: SessionHdfMapCatalog;
  owner: string;
}

interface CommitState {
  projectOwner: string;
  kind: SessionHdfKind;
  name: string;
  referenceID: string;
  mapName: string;
  mapType: string;
  mapIndex: number;
  visualizationRevision: string;
}

interface FailureState {
  message: string;
  diagnostic: string;
}

const operationList = Object.values(sessionHdfMethods);

function json(value: unknown): string {
  try { return JSON.stringify(value, null, 2) ?? String(value); }
  catch { return String(value); }
}

function rawError(error: unknown): string {
  const diagnostics = errorDiagnostics(error);
  const serialized = json(diagnostics);
  if (serialized !== '{}' && serialized !== 'undefined') return serialized;
  return error instanceof Error ? (error.stack || error.message) : String(error);
}

function identityToken(value: Record<string, unknown> | null | undefined): string | null {
  if (!value) return null;
  const token = value.identityToken ?? value.IdentityToken;
  if (typeof token === 'string' && token.trim()) return token;
  return JSON.stringify(value) ?? null;
}

function projectOwner(props: SessionHdfImportControlProps): string {
  const { context } = props;
  return JSON.stringify([context.projectSessionID, context.projectPath, context.projectRevision, context.projectXml]);
}

function captureOwner(props: SessionHdfImportControlProps): string {
  return JSON.stringify([projectOwner(props), identityToken(props.context.coreLayerContext)]);
}

function isDirty(props: SessionHdfImportControlProps): boolean {
  return Boolean(props.hasUnsavedDrafts || (props.dirtyOwners && props.dirtyOwners.size > 0));
}

function currentSessionHdfReferences(value: Record<string, unknown> | null | undefined, projectSessionID: string): Record<string, unknown>[] {
  if (!value) return [];
  const references = value.sessionHdfReferences ?? value.SessionHdfReferences;
  if (!Array.isArray(references)) return [];
  return references.filter((entry): entry is Record<string, unknown> => {
    if (typeof entry !== 'object' || entry === null || Array.isArray(entry)) return false;
    const candidate = entry as Record<string, unknown>;
    const session = candidate.projectSessionID ?? candidate.ProjectSessionID;
    const id = candidate.referenceID ?? candidate.ReferenceID;
    const sessionOnly = candidate.sessionOnly ?? candidate.SessionOnly;
    return typeof id === 'string' && id.trim().length > 0 && sessionOnly === true &&
      typeof session === 'string' && session.toLowerCase() === projectSessionID.toLowerCase();
  });
}

function mapLabel(map: SessionHdfMapDocument, zh: boolean): string {
  return `${map.mapName} · ${map.mapType} · #${map.mapIndex}${map.mapType === 'Project' ? ` · ${zh ? '工程地图' : 'Project map'}` : ''}`;
}

export function SessionHdfImportControl(props: SessionHdfImportControlProps) {
  const { language, backend, context, backendRevision, onBeginSessionReferenceMutation,
    onCommitSessionReferences, onEndSessionReferenceMutation } = props;
  const zh = language === 'zh';
  const [sourcePath, setSourcePath] = useState('');
  const [readState, setReadState] = useState<ReadState | null>(null);
  const [selectedMapIndex, setSelectedMapIndex] = useState('');
  const [readBusy, setReadBusy] = useState(false);
  const [commitBusy, setCommitBusy] = useState(false);
  const [failure, setFailure] = useState<FailureState | null>(null);
  const [committed, setCommitted] = useState<CommitState | null>(null);
  const projectIdentity = projectOwner(props);
  const owner = captureOwner(props);
  const ownerRef = useRef(owner);
  ownerRef.current = owner;
  const generation = useRef(0);
  const readController = useRef<AbortController | null>(null);

  useEffect(() => {
    generation.current += 1;
    readController.current?.abort();
    readController.current = null;
    setReadState(null);
    setSelectedMapIndex('');
    setReadBusy(false);
    setFailure(null);
    setSourcePath('');
    setCommitted(null);
  }, [projectIdentity]);

  useEffect(() => {
    generation.current += 1;
    readController.current?.abort();
    readController.current = null;
    setReadState(null);
    setSelectedMapIndex('');
    setReadBusy(false);
    setFailure(null);
  }, [owner]);

  useEffect(() => () => {
    generation.current += 1;
    readController.current?.abort();
    readController.current = null;
  }, []);

  const missingOperations = useMemo(() => operationList.filter(method => !backend.supports(method)), [backend, backendRevision]);
  const contextReady = Boolean(context.projectPath && context.projectRevision && context.projectXml?.trim() && context.projectSessionID);
  const dirty = isDirty(props);
  const captureReady = Boolean(readState && readState.owner === owner);
  const eligibleMaps = useMemo(() => readState
    ? readState.catalog.maps.filter(map => supportedMapTypes(readState.inspection.kind).includes(map.mapType))
    : [], [readState]);
  const selectedMap = eligibleMaps.find(map => String(map.mapIndex) === selectedMapIndex) ?? null;
  const canCommitSession = Boolean(onBeginSessionReferenceMutation && onCommitSessionReferences && onEndSessionReferenceMutation);
  const canAdd = Boolean(captureReady && selectedMap && !readBusy && !commitBusy && !dirty && !missingOperations.length &&
    canCommitSession && contextReady && readState?.catalog.visualizationRevision);
  const registeredReferences = currentSessionHdfReferences(context.coreLayerContext, context.projectSessionID);

  const readSourceAndMaps = async (): Promise<void> => {
    if (!contextReady) {
      setFailure({ message: text(language, '请先打开已保存的工程。', 'Open a saved project first.'), diagnostic: '' });
      return;
    }
    if (missingOperations.length) {
      setFailure({ message: text(language, '服务尚未提供完整的 Session HDF 导入操作。', 'The service does not advertise the complete Session HDF import workflow.'), diagnostic: missingOperations.join('\n') });
      return;
    }
    const requestedPath = sourcePath;
    if (!requestedPath.trim()) {
      setFailure({ message: text(language, '请输入服务端可访问的 HDF 绝对路径。', 'Enter an absolute HDF path visible to the service.'), diagnostic: '' });
      return;
    }

    readController.current?.abort();
    const controller = new AbortController();
    readController.current = controller;
    const requestOwner = owner;
    const requestGeneration = ++generation.current;
    setReadBusy(true);
    setReadState(null);
    setSelectedMapIndex('');
    setFailure(null);
    try {
      const projectXmlRevision = await currentProjectXmlRevision(context);
      const fileValue: unknown = await backend.request(sessionHdfMethods.fileInspect, { sourcePath: requestedPath }, { signal: controller.signal });
      const fileCapture = parseWorkspaceFileCapture(fileValue, requestedPath);
      const inspectValue: unknown = await backend.request(sessionHdfMethods.inspect, {
        projectPath: context.projectPath,
        projectSessionID: context.projectSessionID,
        projectRevision: context.projectRevision,
        projectXml: context.projectXml,
        projectXmlRevision,
        sourcePath: fileCapture.sourcePath,
        sourceRevision: fileCapture.revision,
        ...(context.coreLayerContext ? { coreLayerContext: context.coreLayerContext } : {}),
      }, { signal: controller.signal });
      const inspection = parseSessionHdfInspection(inspectValue, { context, projectXmlRevision, source: fileCapture });
      const mapListValue: unknown = await backend.request(sessionHdfMethods.mapList, {
        projectPath: context.projectPath,
        projectXml: context.projectXml,
        projectXmlSHA: projectXmlRevision,
        expectedProjectRevision: context.projectRevision,
      }, { signal: controller.signal });
      const catalog = parseSessionHdfMapCatalog(mapListValue, context);
      if (controller.signal.aborted || generation.current !== requestGeneration || ownerRef.current !== requestOwner) return;
      const compatible = catalog.maps.filter(map => supportedMapTypes(inspection.kind).includes(map.mapType));
      const defaultMap = compatible.find(map => map.mapIndex === catalog.activeMapIndex) ?? compatible[0];
      setReadState({ inspection, catalog, owner: requestOwner });
      setSelectedMapIndex(defaultMap ? String(defaultMap.mapIndex) : '');
      if (!compatible.length) {
        setFailure({
          message: text(language, '当前工程没有可接收此 HDF 类型的已保存地图。', 'The project has no saved map that can accept this HDF type.'),
          diagnostic: `${inspection.kind}: ${catalog.maps.map(map => `${map.mapType} #${map.mapIndex}`).join(', ') || '(no saved maps)'}`,
        });
      }
    } catch (error) {
      if (!controller.signal.aborted && generation.current === requestGeneration && ownerRef.current === requestOwner) {
        setFailure({ message: message(error, language), diagnostic: rawError(error) });
      }
    } finally {
      if (generation.current === requestGeneration) setReadBusy(false);
    }
  };

  const addToMap = async (): Promise<void> => {
    if (!readState || readState.owner !== owner || !selectedMap || !contextReady) return;
    if (dirty) {
      setFailure({ message: text(language, '请先处理当前工程中的未保存草稿，再添加会话引用。', 'Resolve unsaved project drafts before adding a session reference.'), diagnostic: '' });
      return;
    }
    if (!onBeginSessionReferenceMutation || !onCommitSessionReferences || !onEndSessionReferenceMutation) {
      setFailure({ message: text(language, '当前界面没有提供工程会话引用保护回调。', 'The current view does not provide project-session reference guards.'), diagnostic: '' });
      return;
    }
    if (missingOperations.length) {
      setFailure({ message: text(language, '服务尚未提供完整的 Session HDF 导入操作。', 'The service does not advertise the complete Session HDF import workflow.'), diagnostic: missingOperations.join('\n') });
      return;
    }
    const sourceContext = context;
    const requestOwner = owner;
    const leaseID = onBeginSessionReferenceMutation(sourceContext);
    if (!leaseID) {
      setFailure({ message: text(language, '工程会话当前不可锁定；请关闭未保存编辑或重试。', 'The project session could not be reserved; close unsaved edits or retry.'), diagnostic: '' });
      return;
    }
    setCommitBusy(true);
    setFailure(null);
    try {
      if (ownerRef.current !== requestOwner) throw new Error(text(language, '工程会话或外部引用捕获已变化，请重新检查来源。', 'The project session or external-reference capture changed. Inspect the source again.'));
      const projectXmlRevision = await currentProjectXmlRevision(sourceContext);
      if (projectXmlRevision.toUpperCase() !== readState.inspection.projectXmlRevision.toUpperCase()) {
        throw new Error(text(language, '工程 XML 快照已变化，请重新检查来源。', 'The project XML snapshot changed. Inspect the source again.'));
      }

      const freshListValue: unknown = await backend.request(sessionHdfMethods.mapList, {
        projectPath: sourceContext.projectPath,
        projectXml: sourceContext.projectXml,
        projectXmlSHA: projectXmlRevision,
        expectedProjectRevision: sourceContext.projectRevision,
      });
      const freshCatalog = parseSessionHdfMapCatalog(freshListValue, sourceContext);
      const expectedVisualizationRevision = freshCatalog.visualizationRevision;
      const freshMap = freshCatalog.maps.find(map => map.mapIndex === selectedMap.mapIndex &&
        map.mapName === selectedMap.mapName && map.mapType === selectedMap.mapType);
      if (!freshMap || !supportedMapTypes(readState.inspection.kind).includes(freshMap.mapType) ||
          !expectedVisualizationRevision) {
        throw new Error(text(language, '所选地图已变化或不再可用，请重新读取地图目录。', 'The selected map changed or is no longer available. Read the map catalog again.'));
      }

      const composedValue: unknown = await backend.request(sessionHdfMethods.compose, {
        projectPath: sourceContext.projectPath,
        projectSessionID: sourceContext.projectSessionID,
        projectRevision: sourceContext.projectRevision,
        projectXml: sourceContext.projectXml,
        projectXmlRevision,
        ...(sourceContext.coreLayerContext ? { coreLayerContext: sourceContext.coreLayerContext } : {}),
        addSessionHdfReferences: [readState.inspection.reference],
      });
      const composed = parseComposedSessionHdfContext(composedValue, {
        context: sourceContext,
        sourceCoreLayerContext: sourceContext.coreLayerContext ?? null,
        reference: readState.inspection.reference,
      });
      if (ownerRef.current !== requestOwner) throw new Error(text(language, '工程会话已变化，未提交地图树操作。', 'The project session changed; the map tree was not modified.'));

      const treeValue: unknown = await backend.request(sessionHdfMethods.mapPerform, {
        map: {
          projectPath: sourceContext.projectPath,
          projectXml: sourceContext.projectXml,
          projectXmlSHA: projectXmlRevision,
          mapID: `session-hdf-${sourceContext.projectSessionID}-${freshMap.mapIndex}`,
          mapIndex: freshMap.mapIndex,
          mapName: freshMap.mapName,
          mapType: freshMap.mapType,
          expectedVisualizationRevision,
          projectSessionID: sourceContext.projectSessionID,
          coreLayerContext: composed.nextContext,
        },
        action: 'addSessionHdf',
        sourcePath: readState.inspection.reference.sourcePath,
        sourceRevision: readState.inspection.reference.sourceRevision,
      });
      const treeReceipt = parseSessionHdfMapTreeReceipt(treeValue, expectedVisualizationRevision, {
        reference: readState.inspection.reference,
        nextContext: composed.nextContext,
        mapType: freshMap.mapType,
      });
      const adopted = onCommitSessionReferences(sourceContext, composed.nextContext, leaseID);
      if (!adopted) {
        throw new Error(text(language,
          '地图树操作已返回服务回执，但新引用未被当前工程会话接纳。请重新读取地图目录并核对工程状态。',
          'The map operation returned a receipt, but the current project session did not adopt the new reference. Read the map catalog again and verify the project state.'));
      }
      setCommitted({
        projectOwner: projectIdentity,
        kind: readState.inspection.kind,
        name: readState.inspection.name,
        referenceID: readState.inspection.reference.referenceID,
        mapName: freshMap.mapName,
        mapType: freshMap.mapType,
        mapIndex: freshMap.mapIndex,
        visualizationRevision: treeReceipt.visualizationRevision,
      });
      setReadState(null);
      setSelectedMapIndex('');
    } catch (error) {
      setFailure({ message: message(error, language), diagnostic: rawError(error) });
    } finally {
      try { onEndSessionReferenceMutation(leaseID); }
      catch (endError) {
        setFailure({ message: message(endError, language), diagnostic: rawError(endError) });
      } finally {
        setCommitBusy(false);
      }
    }
  };

  const inspection = captureReady ? readState?.inspection ?? null : null;
  const allCapabilitiesAvailable = missingOperations.length === 0;

  return (
    <section className="ci-panel session-hdf-panel" data-testid="session-hdf-import-control">
      <div className="ci-import-heading">
        <div><span className="ci-kicker">{zh ? '会话 HDF 引用' : 'SESSION HDF REFERENCES'}</span>
          <h3>{zh ? '将计划、计算结果或地形加入地图' : 'Add a Plan, Result, or Terrain to a map'}</h3></div>
        <span className="ci-chip">{zh ? '仅当前工程会话' : 'Current project session only'}</span>
      </div>
      <p className="ci-explanation">
        {zh
          ? '来源文件保留在原路径，不复制到工程，也不写入 .ras。服务会校验文件 SHA、工程会话和地图修订；外部引用只在当前已打开的工程会话中登记。'
          : 'The source stays at its original path. It is not copied or written into the .ras file. The service verifies file SHA, project session, and map revision; the external reference is registered only in the open project session.'}
      </p>

      {!contextReady && <div className="ci-warning" role="status">{zh ? '请先打开并保存工程。' : 'Open and save a project first.'}</div>}
      {missingOperations.length > 0 && <div className="ci-warning" role="status" data-testid="session-hdf-capability-warning">
        {zh ? '服务未发布完整的 Session HDF 工作流，添加操作已停用。' : 'The service has not advertised the complete Session HDF workflow; adding is disabled.'}
        <details><summary>{zh ? '缺少的操作' : 'Missing operations'}</summary><pre>{missingOperations.join('\n')}</pre></details>
      </div>}
      {dirty && <div className="ci-warning" role="status">{zh ? '当前工程存在未保存草稿；草稿处理完成前不会登记引用或修改地图。' : 'This project has unsaved drafts. No reference or map changes will be made until they are resolved.'}</div>}

      <label className="ci-field" htmlFor="session-hdf-source-path">
        <span>{zh ? '服务端可见的 HDF 绝对路径' : 'Absolute HDF path visible to the service'}</span>
        <input id="session-hdf-source-path" data-testid="session-hdf-source-path" type="text" value={sourcePath}
          onChange={event => {
            setSourcePath(event.currentTarget.value);
            setReadState(null);
            setSelectedMapIndex('');
            setFailure(null);
          }} placeholder={zh ? '输入服务端可访问的绝对路径' : 'Enter an absolute path visible to the service'}
          autoComplete="off" spellCheck={false} disabled={!contextReady || readBusy || commitBusy} />
      </label>
      <div className="ci-actions">
        <button type="button" className="ci-button ci-primary" data-testid="session-hdf-inspect" disabled={!contextReady || !sourcePath.trim() || readBusy || commitBusy || !allCapabilitiesAvailable}
          onClick={() => void readSourceAndMaps()}>
          {readBusy ? (zh ? '检查来源并读取地图…' : 'Inspecting source and reading maps…') : (zh ? '检查来源并读取地图目录' : 'Inspect source and read map catalog')}
        </button>
      </div>

      {inspection && <div className="ci-result" data-testid="session-hdf-inspection">
        <div className="ci-result-heading"><span className="ci-state-dot" /><strong>{zh ? '来源检查完成' : 'Source inspection complete'}</strong>
          <span className="ci-chip">{sessionHdfKindLabel(inspection.kind, language)}</span></div>
        <dl className="ci-details">
          <div><dt>{zh ? '名称' : 'Name'}</dt><dd>{inspection.name}</dd></div>
          {inspection.resultPlanName && <div><dt>{zh ? '结果所属计划' : 'Result plan'}</dt><dd>{inspection.resultPlanName}</dd></div>}
          <div><dt>{zh ? '来源路径' : 'Source path'}</dt><dd><code>{inspection.sourcePath}</code></dd></div>
          <div><dt>{zh ? '规范来源路径' : 'Canonical source path'}</dt><dd><code>{inspection.canonicalSourcePath}</code></dd></div>
          <div><dt>{zh ? '来源 SHA-256' : 'Source SHA-256'}</dt><dd><code>{inspection.sourceRevision}</code></dd></div>
          {inspection.description && <div><dt>{zh ? '说明' : 'Description'}</dt><dd>{inspection.description}</dd></div>}
          <div><dt>{zh ? '会话引用' : 'Session reference'}</dt><dd><code>{inspection.reference.referenceID}</code></dd></div>
          <div><dt>{zh ? '工程会话' : 'Project session'}</dt><dd><code>{inspection.projectSessionID}</code></dd></div>
          <div><dt>{zh ? '工程修订' : 'Project revision'}</dt><dd><code>{inspection.projectRevision}</code></dd></div>
          <div><dt>{zh ? '工程 XML SHA-256' : 'Project XML SHA-256'}</dt><dd><code>{inspection.projectXmlRevision}</code></dd></div>
          <div><dt>{zh ? '文件大小' : 'File size'}</dt><dd>{inspection.fileSize.toLocaleString()} {zh ? '字节' : 'bytes'}</dd></div>
        </dl>
      </div>}

      {captureReady && readState && <div className="session-hdf-map-picker">
        <label className="ci-field" htmlFor="session-hdf-map-select">
          <span>{zh ? '目标地图（按当前 .vis 目录）' : 'Target map (from current .vis catalog)'}</span>
          <select id="session-hdf-map-select" data-testid="session-hdf-map-select" value={selectedMapIndex}
            onChange={event => setSelectedMapIndex(event.currentTarget.value)} disabled={commitBusy || !eligibleMaps.length}>
            {!eligibleMaps.length && <option value="">{zh ? '没有兼容的已保存地图' : 'No compatible saved map'}</option>}
            {eligibleMaps.map(map => <option key={`${map.mapIndex}:${map.mapName}:${map.mapType}`} value={map.mapIndex}>
              {mapLabel(map, zh)}{map.mapIndex === readState.catalog.activeMapIndex ? ` · ${zh ? '活动' : 'active'}` : ''}
            </option>)}
          </select>
        </label>
        <div className="ci-inline-meta">
          {zh ? '地图可视化修订：' : 'Map visualization revision: '}
          <code>{readState.catalog.visualizationRevision ?? '—'}</code>
        </div>
        <div className="ci-actions">
          <button type="button" className="ci-button ci-primary" data-testid="session-hdf-add-to-map" disabled={!canAdd}
            onClick={() => void addToMap()}>
            {commitBusy ? (zh ? '登记引用并更新地图…' : 'Registering reference and updating map…') : (zh ? '添加到所选地图' : 'Add to selected map')}
          </button>
        </div>
      </div>}

      {committed && committedOwnerMatches(committed, context, projectIdentity) && <div className="ci-success" role="status" data-testid="session-hdf-commit-receipt">
        <strong>{zh ? '已登记到当前工程会话' : 'Registered in the current project session'}</strong>
        <span>{sessionHdfKindLabel(committed.kind, language)} · {committed.name} → {committed.mapName} · {committed.mapType} · #{committed.mapIndex}</span>
        <details><summary>{zh ? '地图回执与引用身份' : 'Map receipt and reference identity'}</summary>
          <code>{committed.referenceID}</code><code>{committed.visualizationRevision}</code>
        </details>
      </div>}

      {registeredReferences.length > 0 && <div className="session-hdf-registered" data-testid="session-hdf-registered-list">
        <div className="ci-import-heading"><h3>{zh ? '当前会话已登记的 HDF 引用' : 'HDF references registered in this session'}</h3>
          <span className="ci-chip">{registeredReferences.length}</span></div>
        <ul className="ci-operation-list">
          {registeredReferences.map((reference, index) => {
            const rawKind = reference.kind ?? reference.Kind;
            const kind = rawKind === 'plan' || rawKind === 'result' || rawKind === 'terrain' ? rawKind : null;
            const rawName = reference.name ?? reference.Name;
            const name = typeof rawName === 'string' ? rawName : '—';
            const rawPath = reference.sourcePath ?? reference.SourcePath;
            const path = typeof rawPath === 'string' ? rawPath : '—';
            const rawID = reference.referenceID ?? reference.ReferenceID;
            const referenceID = typeof rawID === 'string' ? rawID : undefined;
            return <li key={referenceID ?? index} {...(referenceID ? { 'data-reference-id': referenceID } : {})}>
              <strong>{kind ? sessionHdfKindLabel(kind, language) : (zh ? '会话引用' : 'Session reference')}</strong>
              <span>{name}<code>{path}</code></span>
            </li>;
          })}
        </ul>
      </div>}

      {failure && <div className="ci-error" role="alert" data-testid="session-hdf-error">
        <strong>{failure.message}</strong>
        {failure.diagnostic && <details><summary>{zh ? '原始诊断' : 'Raw diagnostic'}</summary><pre>{failure.diagnostic}</pre></details>}
      </div>}

    </section>
  );
}

function committedOwnerMatches(commit: CommitState, context: SessionHdfImportControlProps['context'], owner: string): boolean {
  return Boolean(context.projectPath && context.projectSessionID && commit.projectOwner === owner && owner === JSON.stringify([
    context.projectSessionID, context.projectPath, context.projectRevision, context.projectXml,
  ]));
}

export default SessionHdfImportControl;
