import { useEffect, useRef, useState } from 'react';
import type { FeaturePageProps } from '../../../shared/workbench';
import { errorDiagnostics, message, text } from '../../../shared/workbench';
import { AttributeTableDataGrid } from './AttributeTableDataGrid';
import { AddAttributeTableColumnContentDialog } from './AddAttributeTableColumnContentDialog';
import { RenameAttributeTableColumnContentDialog } from './RenameAttributeTableColumnContentDialog';
import type { AttributeTableColumn, AttributeTableData, AttributeTableMutation, AttributeTableSourceCapture, AttributeTableSourceFamilyCapture } from './attributeTableTypes';
import { isAttributeTableData, isShapefileFamilyCapture, isSupportedAttributeCellType, isWorkspaceCapture, sameSourceCapture, sameSourceFamilyCapture, sameSourcePath } from './attributeTableTypes';
import './attributes.css';

type Busy = 'load' | 'transform' | null;
type DialogState = { kind: 'add' } | { kind: 'rename'; column: AttributeTableColumn } | null;

function rawDiagnostic(error: unknown): string {
  const diagnostic = errorDiagnostics(error);
  if (typeof diagnostic === 'string') return diagnostic;
  if (diagnostic !== undefined) {
    try { return JSON.stringify(diagnostic, null, 2); } catch { return String(diagnostic); }
  }
  return error instanceof Error ? error.message : String(error);
}

function extension(path: string): string {
  const name = path.split(/[\\/]/).at(-1) ?? '';
  const index = name.lastIndexOf('.');
  return index >= 0 ? name.slice(index).toLocaleLowerCase() : '';
}

function expectedColumnType(type: Extract<AttributeTableMutation, { operation: 'addColumn' }>['type']): string {
  return type === 'Integer' ? 'Int32' : type === 'Numeric' ? 'Double' : type;
}

function matchesMutation(before: AttributeTableData, after: AttributeTableData, mutation: AttributeTableMutation): boolean {
  if (!sameSourcePath(before.sourcePath, after.sourcePath) || before.shapeType !== after.shapeType ||
      before.rows.length !== after.rows.length || before.rows.some((row, index) => row.id !== after.rows[index]?.id)) return false;
  if (mutation.operation === 'addColumn') {
    const added = after.columns.filter(column => !before.columns.some(previous => previous.id === column.id));
    return after.columns.length === before.columns.length + 1 && added.length === 1 && added[0]?.name === mutation.name &&
      added[0]?.type === expectedColumnType(mutation.type) && before.columns.every(column => after.columns.some(next => next.id === column.id));
  }
  if (mutation.operation === 'renameColumn') {
    return after.columns.length === before.columns.length && before.columns.every(column => {
      const next = after.columns.find(item => item.id === column.id);
      return Boolean(next && (column.id === mutation.columnId ? next.name === mutation.name : next.name === column.name) && next.type === column.type);
    });
  }
  if (mutation.operation === 'removeColumn') {
    return after.columns.length === before.columns.length - 1 && !after.columns.some(column => column.id === mutation.columnId) &&
      before.columns.filter(column => column.id !== mutation.columnId).every(column => after.columns.some(next => next.id === column.id && next.name === column.name && next.type === column.type));
  }
  return after.columns.length === before.columns.length && before.columns.every(column =>
    after.columns.some(next => next.id === column.id && next.name === column.name && next.type === column.type));
}

export function AttributeTableEditorControl({ language, context, backend }: FeaturePageProps) {
  const zh = language === 'zh';
  const selectedAsset = context.selectedAsset;
  const [sourcePath, setSourcePath] = useState(selectedAsset?.path ?? '');
  const owner = JSON.stringify([context.projectSessionID, context.projectPath, context.projectRevision, context.projectXml,
    selectedAsset?.path ?? null, selectedAsset?.kind ?? null, selectedAsset?.revision ?? null, sourcePath.trim()]);
  const ownerRef = useRef(owner); ownerRef.current = owner;
  const generation = useRef(0);
  const controller = useRef<AbortController | null>(null);
  const [capture, setCapture] = useState<AttributeTableSourceCapture>();
  const [familyCapture, setFamilyCapture] = useState<AttributeTableSourceFamilyCapture>();
  const [table, setTable] = useState<AttributeTableData>();
  const [busy, setBusy] = useState<Busy>(null);
  const [previewMutationCount, setPreviewMutationCount] = useState(0);
  const [staleSource, setStaleSource] = useState(false);
  const [dialog, setDialog] = useState<DialogState>(null);
  const [error, setError] = useState<string>();
  const [diagnostic, setDiagnostic] = useState<string>();
  const [notice, setNotice] = useState<string>();

  const supportsInspect = backend.supports('workspace.file.inspect');
  const supportsLoad = backend.supports('attributeTable.load');
  const supportsTransform = backend.supports('attributeTable.transform');
  const supportsFamilyCapture = backend.supports('map.vector.captureShapefile');

  useEffect(() => {
    generation.current += 1;
    controller.current?.abort();
    controller.current = null;
    setCapture(undefined); setFamilyCapture(undefined); setTable(undefined); setBusy(null); setPreviewMutationCount(0); setStaleSource(false); setDialog(null);
    setError(undefined); setDiagnostic(undefined); setNotice(undefined);
  }, [owner]);

  useEffect(() => () => {
    generation.current += 1;
    controller.current?.abort();
    controller.current = null;
  }, []);

  useEffect(() => { setSourcePath(selectedAsset?.path ?? ''); }, [selectedAsset?.path]);

  const isCurrent = (id: number, requestOwner: string, signal: AbortSignal): boolean =>
    generation.current === id && ownerRef.current === requestOwner && !signal.aborted;

  const begin = (): { id: number; signal: AbortSignal; requestOwner: string } => {
    controller.current?.abort();
    const next = new AbortController();
    controller.current = next;
    const id = ++generation.current;
    return { id, signal: next.signal, requestOwner: owner };
  };

  const inspectPath = async (path: string, signal: AbortSignal): Promise<AttributeTableSourceCapture> => {
    if (!supportsInspect) throw new Error(text(language, 'C# 服务未发布 workspace.file.inspect，无法自动捕获来源 SHA-256。', 'The C# server has not advertised workspace.file.inspect, so the source SHA-256 cannot be captured automatically.'));
    const response: unknown = await backend.request('workspace.file.inspect', { sourcePath: path }, { signal });
    if (!isWorkspaceCapture(response, path)) throw new Error(text(language, '来源检查回执缺少匹配路径、SHA-256 或文件长度。', 'The source inspection receipt is missing a matching path, SHA-256, or file length.'));
    return response;
  };

  const inspectShapefileFamily = async (path: string, sourceRevision: string,
    request: { id: number; signal: AbortSignal; requestOwner: string }): Promise<AttributeTableSourceFamilyCapture> => {
    if (!supportsFamilyCapture) throw new Error(text(language,
      'C# 服务未发布 Shapefile 文件族检查；无法确认 .shp、.shx、.dbf 与可选 .prj 的完整来源身份。',
      'The C# server has not advertised Shapefile family capture, so the complete .shp, .shx, .dbf, and optional .prj source identity cannot be verified.'));
    try {
      const response: unknown = await backend.request('map.vector.captureShapefile', { sourcePath: path }, { signal: request.signal });
      if (!isShapefileFamilyCapture(response, path, sourceRevision)) {
        throw new Error(text(language, 'C# Host 文件族回执缺少完整的必需文件身份，或主文件 SHA-256 与检查结果不一致。',
          'The C# Host family receipt is missing required file identities or its primary-file SHA-256 does not match the inspected source.'));
      }
      return response;
    } catch (caught) {
      if (!request.signal.aborted && isCurrent(request.id, request.requestOwner, request.signal)) {
        setStaleSource(true);
        setNotice(text(language, '无法确认完整 Shapefile 文件族；当前预览修改已锁定。请重新检查来源。',
          'The complete Shapefile family could not be verified; preview edits are locked. Inspect the source again.'));
      }
      throw caught;
    }
  };

  const markStale = (request: { id: number; signal: AbortSignal; requestOwner: string }): void => {
    if (!isCurrent(request.id, request.requestOwner, request.signal)) return;
    setStaleSource(true);
    setNotice(text(language, '源文件在检查期间发生变化，已停止后续属性表预览修改。请重新检查并载入。', 'The source changed during inspection. Further attribute-table preview edits are blocked; inspect and load it again.'));
  };

  const loadTable = async (): Promise<void> => {
    const requestedPath = sourcePath.trim();
    if (!requestedPath || busy) return;
    const request = begin();
    setBusy('load'); setCapture(undefined); setFamilyCapture(undefined); setTable(undefined); setPreviewMutationCount(0); setStaleSource(false);
    setError(undefined); setDiagnostic(undefined); setNotice(undefined);
    try {
      const inspected = await inspectPath(requestedPath, request.signal);
      if (!isCurrent(request.id, request.requestOwner, request.signal)) return;
      setCapture(inspected);
      if (extension(inspected.canonicalPath) !== '.shp') {
        setNotice(extension(inspected.canonicalPath) === '.geojson' || extension(inspected.canonicalPath) === '.json'
          ? text(language, '来源路径与 SHA-256 已由 Host 检查；当前 Host 的属性表读取只支持 Shapefile（.shp），GeoJSON 属性表暂不可用。', 'The Host inspected this path and SHA-256. Attribute-table loading currently supports Shapefiles (.shp) only; GeoJSON tables are unavailable.')
          : text(language, '来源路径与 SHA-256 已由 Host 检查；当前属性表读取只支持 Shapefile（.shp）。', 'The Host inspected this path and SHA-256. Attribute-table loading currently supports Shapefiles (.shp) only.'));
        return;
      }
      if (!supportsLoad) throw new Error(text(language, 'C# 服务未发布 Shapefile 属性表读取能力。', 'The C# server has not advertised Shapefile attribute-table loading.'));
      if (!supportsFamilyCapture) throw new Error(text(language,
        '当前服务无法捕获 Shapefile 完整文件族；为避免把单个 .shp 文件误报为完整来源，已停止载入。',
        'The current server cannot capture the complete Shapefile family. Loading is blocked so a single .shp file is not presented as a complete source.'));
      const familyBefore = await inspectShapefileFamily(inspected.canonicalPath, inspected.revision, request);
      if (!isCurrent(request.id, request.requestOwner, request.signal)) return;
      const loaded: unknown = await backend.request('attributeTable.load', { path: inspected.canonicalPath }, { signal: request.signal });
      if (!isCurrent(request.id, request.requestOwner, request.signal)) return;
      if (!isAttributeTableData(loaded) || !sameSourcePath(loaded.sourcePath, inspected.canonicalPath)) {
        throw new Error(text(language, '属性表回执的来源路径或行列身份无效。', 'The attribute-table receipt has an invalid source path or row/column identity.'));
      }
      const afterLoad = await inspectPath(inspected.canonicalPath, request.signal);
      if (!isCurrent(request.id, request.requestOwner, request.signal)) return;
      const familyAfter = await inspectShapefileFamily(inspected.canonicalPath, afterLoad.revision, request);
      if (!isCurrent(request.id, request.requestOwner, request.signal)) return;
      if (!sameSourceCapture(inspected, afterLoad) || !sameSourceFamilyCapture(familyBefore, familyAfter)) {
        markStale(request);
        throw new Error(text(language, '属性表读取期间 Shapefile 主文件或文件族（.shp/.shx/.dbf/可选 .prj）发生变化。',
          'The Shapefile primary file or its family (.shp/.shx/.dbf/optional .prj) changed while loading the table.'));
      }
      setCapture(inspected); setFamilyCapture(familyBefore); setTable(loaded); setPreviewMutationCount(0); setStaleSource(false);
      setNotice(text(language, '属性表已从 C# Host 读取。字段修改仅更新服务器返回的内存预览，不会写入 Shapefile。', 'The C# Host loaded the attribute table. Field edits update only the returned in-memory preview and do not write the Shapefile.'));
    } catch (caught) {
      if (request.signal.aborted) {
        if (ownerRef.current === request.requestOwner) setNotice(text(language, '来源检查或读取已取消；过期响应不会更新当前属性表。', 'Source inspection or loading was cancelled; stale responses cannot update the current table.'));
      } else if (isCurrent(request.id, request.requestOwner, request.signal)) {
        setError(message(caught, language)); setDiagnostic(rawDiagnostic(caught));
      }
    } finally {
      if (generation.current === request.id) setBusy(null);
    }
  };

  const applyMutation = async (mutation: AttributeTableMutation): Promise<boolean> => {
    const sourceTable = table;
    const sourceCapture = capture;
    const sourceFamily = familyCapture;
    if (!sourceTable || !sourceCapture || !sourceFamily || busy || staleSource || !supportsTransform || !supportsFamilyCapture) return false;
    const request = begin();
    setBusy('transform'); setError(undefined); setDiagnostic(undefined); setNotice(undefined);
    try {
      const before = await inspectPath(sourceCapture.canonicalPath, request.signal);
      if (!isCurrent(request.id, request.requestOwner, request.signal)) return false;
      const familyBefore = await inspectShapefileFamily(sourceCapture.canonicalPath, before.revision, request);
      if (!isCurrent(request.id, request.requestOwner, request.signal)) return false;
      if (!sameSourceCapture(sourceCapture, before) || !sameSourceFamilyCapture(sourceFamily, familyBefore)) {
        markStale(request);
        throw new Error(text(language, '原始 Shapefile 文件族已变化；本次预览修改未提交。', 'The original Shapefile family changed; this preview mutation was not applied.'));
      }
      const transformed: unknown = await backend.request('attributeTable.transform', { table: sourceTable, mutation }, { signal: request.signal });
      if (!isCurrent(request.id, request.requestOwner, request.signal)) return false;
      if (!isAttributeTableData(transformed) || !matchesMutation(sourceTable, transformed, mutation)) {
        throw new Error(text(language, 'C# 属性表变换回执与本次行列操作不匹配，未更新预览。', 'The C# attribute-table transformation receipt does not match this row/column operation. The preview was not updated.'));
      }
      const after = await inspectPath(sourceCapture.canonicalPath, request.signal);
      if (!isCurrent(request.id, request.requestOwner, request.signal)) return false;
      const familyAfter = await inspectShapefileFamily(sourceCapture.canonicalPath, after.revision, request);
      if (!isCurrent(request.id, request.requestOwner, request.signal)) return false;
      if (!sameSourceCapture(sourceCapture, after) || !sameSourceFamilyCapture(sourceFamily, familyAfter)) {
        markStale(request);
        throw new Error(text(language, 'Shapefile 文件族在预览变换期间发生变化；保留原预览并停止后续修改。', 'The Shapefile family changed during preview transformation. The existing preview is retained and further edits are blocked.'));
      }
      setTable(transformed); setPreviewMutationCount(value => value + 1);
      setNotice(text(language, 'C# Host 已返回更新后的内存表预览；源文件未写入。', 'The C# Host returned an updated in-memory table preview; the source file was not written.'));
      return true;
    } catch (caught) {
      if (request.signal.aborted) {
        if (ownerRef.current === request.requestOwner) setNotice(text(language, '属性表预览操作已取消；当前已确认预览保持不变。', 'The attribute-table preview operation was cancelled; the last confirmed preview is unchanged.'));
      } else if (isCurrent(request.id, request.requestOwner, request.signal)) {
        setError(message(caught, language)); setDiagnostic(rawDiagnostic(caught));
      }
      return false;
    } finally {
      if (generation.current === request.id) setBusy(null);
    }
  };

  const changePath = (value: string): void => {
    controller.current?.abort();
    generation.current += 1;
    setSourcePath(value); setCapture(undefined); setFamilyCapture(undefined); setTable(undefined); setBusy(null); setPreviewMutationCount(0); setStaleSource(false);
    setDialog(null); setError(undefined); setDiagnostic(undefined); setNotice(undefined);
  };

  const cancelActive = (): void => {
    if (!busy) return;
    controller.current?.abort();
    setNotice(text(language, '已请求取消当前读取或预览操作；过期回执不会覆盖当前状态。', 'Cancellation was requested; stale receipts will not replace the current state.'));
  };

  const removeColumn = async (column: AttributeTableColumn): Promise<void> => {
    const accepted = typeof window === 'undefined' || window.confirm(text(language,
      `只从当前内存预览中移除字段“${column.name}”，不会写入源文件。继续？`,
      `Remove “${column.name}” from the in-memory preview only? The source file will not be written.`));
    if (accepted) await applyMutation({ operation: 'removeColumn', columnId: column.id });
  };

  const isShapefilePath = extension(sourcePath) === '.shp';
  const loadDisabled = busy !== null || !sourcePath.trim() || !supportsInspect ||
    (isShapefilePath && (!supportsLoad || !supportsFamilyCapture));
  const unsupportedColumnTypes = Boolean(table?.columns.some(column => !isSupportedAttributeCellType(column.type)));
  const mutationDisabled = busy !== null || staleSource || !capture || !familyCapture || !supportsTransform || !supportsFamilyCapture || unsupportedColumnTypes;

  return <section className="attribute-manager" aria-labelledby="attribute-manager-title">
    <header className="attribute-manager-heading"><div><span className="attribute-eyebrow">{zh ? '来源管理 · 属性表' : 'Source manager · Attribute table'}</span>
      <h3 id="attribute-manager-title">{zh ? '属性表预览与字段编辑' : 'Attribute table preview and field editing'}</h3>
      <p>{zh ? '输入 C# Host 可见的来源路径。主文件 SHA-256 由 workspace.file.inspect 捕获，Shapefile 的 .shp/.shx/.dbf/可选 .prj 文件族由地图来源检查返回；字段变更仅由 Host 转换为内存预览。' : 'Enter a path visible to the C# Host. workspace.file.inspect captures the primary-file SHA-256; the map-source service captures the .shp/.shx/.dbf/optional .prj family. Host transformations update only an in-memory preview.'}</p></div>
      <span className={`attribute-host-state ${supportsInspect && supportsLoad && supportsTransform && supportsFamilyCapture ? 'is-ready' : ''}`}>{supportsInspect && supportsLoad && supportsTransform && supportsFamilyCapture ? (zh ? 'Host 预览能力可用' : 'Host preview available') : (zh ? 'Host 能力未完整发布' : 'Host capabilities incomplete')}</span></header>
    <div className="attribute-source-picker"><label><span>{zh ? 'Shapefile / GeoJSON 来源路径（由服务端访问）' : 'Shapefile / GeoJSON source path (server-visible)'}</span>
      <input value={sourcePath} spellCheck={false} autoComplete="off" placeholder={zh ? '/服务器可见路径/边界.shp' : '/server-visible/path/boundary.shp'} onChange={event => changePath(event.target.value)} /></label>
      <button type="button" className="is-primary" disabled={loadDisabled} onClick={() => void loadTable()}>{busy === 'load' ? (zh ? '检查 / 读取中…' : 'Inspecting / loading…') : isShapefilePath ? (zh ? '检查文件族并载入表' : 'Inspect family and load table') : (zh ? '检查来源' : 'Inspect source')}</button>
      <button type="button" disabled={busy === null} onClick={cancelActive}>{zh ? '请求取消' : 'Request cancel'}</button>
    </div>
    <div className="attribute-identity-grid">
      <div><dt>{zh ? '输入路径' : 'Entered path'}</dt><dd><code>{sourcePath.trim() || '—'}</code></dd></div>
      <div><dt>{zh ? '规范来源路径' : 'Canonical source path'}</dt><dd><code>{capture?.canonicalPath ?? '—'}</code></dd></div>
      <div><dt>SHA-256</dt><dd><code>{capture?.revision ?? (zh ? '尚未检查' : 'Not inspected')}</code></dd></div>
      <div><dt>{zh ? '文件长度' : 'File length'}</dt><dd>{capture ? `${capture.length.toLocaleString(language === 'zh' ? 'zh-CN' : 'en-US')} ${zh ? '字节' : 'bytes'}` : '—'}</dd></div>
    </div>
    {familyCapture && <div className="attribute-family-capture"><div className="attribute-family-heading"><strong>{zh ? 'Shapefile 完整来源文件族' : 'Complete Shapefile source family'}</strong>
      <span>{zh ? `文件族修订 · ${familyCapture.files.length} 个 Host 文件身份项` : `Family revision · ${familyCapture.files.length} captured file identity entries`}</span></div>
      <code>{familyCapture.revision}</code><details><summary>{zh ? '查看 Host 返回的路径与文件修订' : 'View Host-returned paths and file revisions'}</summary>
        <ul>{familyCapture.files.map(file => <li key={`${file.role}:${file.path}`}><span>{file.role} · {file.required ? (zh ? '必需' : 'required') : (zh ? '可选' : 'optional')}</span>
          <code>{file.path}</code><code>{file.revision ?? (zh ? '文件不存在 / 无修订' : 'Missing / no revision')}</code></li>)}</ul>
      </details></div>}
    {(!supportsInspect || !supportsLoad || !supportsTransform || !supportsFamilyCapture) && <p className="attribute-warning">{zh ? '当前 C# 服务未完整发布来源检查、Shapefile 完整文件族捕获、属性表读取或内存预览变换；缺少的动作已禁用。' : 'The current C# server has not advertised source inspection, complete Shapefile family capture, attribute-table loading, or in-memory transformation. Unavailable actions are disabled.'}</p>}
    {table && <>
      <div className={`attribute-preview-banner ${staleSource ? 'is-warning' : ''}`} role="status"><strong>{zh ? '内存预览 · 不写入源文件' : 'In-memory preview · source file is not written'}</strong>
        <span>{zh ? `${table.shapeType} · ${previewMutationCount} 项 Host 预览修改` : `${table.shapeType} · ${previewMutationCount} Host preview mutations`}</span>
        <button type="button" disabled={busy !== null || loadDisabled} onClick={() => void loadTable()}>{zh ? '重新载入 / 丢弃预览' : 'Reload / discard preview'}</button>
      </div>
      {staleSource && <p className="attribute-warning">{zh ? '来源主文件或 Shapefile 文件族身份已变化，或完整文件族无法验证。当前预览仅供查看，字段操作已锁定。' : 'The primary source or Shapefile family changed, or the complete family could not be verified. The preview is view-only and field actions are locked.'}</p>}
      {!supportsTransform && <p className="attribute-warning">{zh ? '当前服务不支持属性表变换，编辑动作不可用。' : 'Attribute-table transformations are unavailable on the current server.'}</p>}
      {unsupportedColumnTypes && <p className="attribute-warning">{zh ? '表中含有当前 Host 变换服务不支持的字段类型；为避免改变未知类型的单元格，所有字段编辑已禁用。' : 'The table contains a field type unsupported by the Host transformation service. All edits are disabled to preserve unknown typed cells.'}</p>}
      <AttributeTableDataGrid language={language} table={table} busy={busy !== null || mutationDisabled} onAdd={() => setDialog({ kind: 'add' })}
        onRename={column => setDialog({ kind: 'rename', column })} onRemove={column => void removeColumn(column)}
        onSetCells={cells => applyMutation({ operation: 'setCells', cells }).then(() => undefined)} />
    </>}
    {notice && <p className="attribute-notice" role="status">{notice}</p>}
    {error && <div className="attribute-error" role="alert"><strong>{error}</strong></div>}
    {diagnostic && <details className="attribute-diagnostic"><summary>{zh ? 'Host 原始诊断' : 'Raw Host diagnostic'}</summary><pre>{diagnostic}</pre></details>}
    <AddAttributeTableColumnContentDialog open={dialog?.kind === 'add'} language={language} busy={busy !== null || mutationDisabled}
      existingNames={table?.columns.map(column => column.name) ?? []} onApply={(name, type) => applyMutation({ operation: 'addColumn', name, type })} onClose={() => setDialog(null)} />
    <RenameAttributeTableColumnContentDialog open={dialog?.kind === 'rename'} language={language} busy={busy !== null || mutationDisabled}
      originalName={dialog?.kind === 'rename' ? dialog.column.name : ''}
      existingNames={table?.columns.filter(column => column.id !== (dialog?.kind === 'rename' ? dialog.column.id : '')).map(column => column.name) ?? []}
      onApply={name => dialog?.kind === 'rename' ? applyMutation({ operation: 'renameColumn', columnId: dialog.column.id, name }) : Promise.resolve(false)}
      onClose={() => setDialog(null)} />
  </section>;
}
