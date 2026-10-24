import { useEffect, useRef, useState } from 'react';
import type { BackendGateway, Language, WorkbenchContext } from '../../../shared/workbench';
import { errorDiagnostics, message, text } from '../../../shared/workbench';
import { AttributeTableDataGrid } from '../attributes/AttributeTableDataGrid';
import type { AttributeTableData, AttributeTableSourceCapture } from '../attributes/attributeTableTypes';
import { isAttributeTableData, isShapefileFamilyCapture, isWorkspaceCapture, sameSourceCapture, sameSourceFamilyCapture, sameSourcePath } from '../attributes/attributeTableTypes';
import type { ShapefileFamilyCapture } from './vectorImportTypes';
import { sameMapSourceFamily } from './vectorImportTypes';

interface Props {
  language: Language;
  backend: BackendGateway;
  context: WorkbenchContext;
  sourcePath: string;
  expectedFamily: ShapefileFamilyCapture;
}

function diagnostic(error: unknown): string {
  const value = errorDiagnostics(error);
  if (typeof value === 'string') return value;
  if (value !== undefined) {
    try { return JSON.stringify(value, null, 2); } catch { return String(value); }
  }
  return error instanceof Error ? error.message : String(error);
}

function safeLength(value: unknown): number | undefined {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return undefined;
  const length = (value as Partial<AttributeTableData>).rows?.length;
  return typeof length === 'number' && Number.isSafeInteger(length) ? length : undefined;
}

export function ShapefileAttributeTableViewer({ language, backend, context, sourcePath, expectedFamily }: Props) {
  const zh = language === 'zh';
  const owner = JSON.stringify([context.projectSessionID, context.projectPath, context.projectRevision,
    context.projectXml, sourcePath, expectedFamily.revision]);
  const ownerRef = useRef(owner); ownerRef.current = owner;
  const generation = useRef(0);
  const controller = useRef<AbortController | null>(null);
  const [busy, setBusy] = useState(false);
  const [loaded, setLoaded] = useState<{ table: AttributeTableData; capture: AttributeTableSourceCapture; family: ShapefileFamilyCapture }>();
  const [error, setError] = useState<string>();
  const [detail, setDetail] = useState<string>();
  const [notice, setNotice] = useState<string>();

  useEffect(() => {
    generation.current += 1;
    controller.current?.abort();
    controller.current = null;
    setBusy(false); setLoaded(undefined); setError(undefined); setDetail(undefined); setNotice(undefined);
  }, [owner]);

  useEffect(() => () => {
    generation.current += 1;
    controller.current?.abort();
    controller.current = null;
  }, []);

  const isCurrent = (id: number, requestOwner: string, signal: AbortSignal): boolean =>
    generation.current === id && ownerRef.current === requestOwner && !signal.aborted;

  const loadTable = async (): Promise<void> => {
    if (busy) return;
    if (!backend.supports('workspace.file.inspect') || !backend.supports('map.vector.captureShapefile') ||
        !backend.supports('attributeTable.load')) {
      setError(text(language, '当前服务无法核对完整 Shapefile 来源并读取属性表；查看功能已禁用。',
        'The current service cannot verify the complete Shapefile source and read its attribute table; viewing is disabled.'));
      return;
    }
    const requestOwner = owner;
    controller.current?.abort();
    const requestController = new AbortController();
    controller.current = requestController;
    const requestID = ++generation.current;
    setBusy(true); setLoaded(undefined); setError(undefined); setDetail(undefined); setNotice(undefined);
    try {
      const captureRaw: unknown = await backend.request('workspace.file.inspect', { sourcePath }, { signal: requestController.signal });
      if (!isCurrent(requestID, requestOwner, requestController.signal)) return;
      if (!isWorkspaceCapture(captureRaw, sourcePath) || !captureRaw.canonicalPath.toLocaleLowerCase().endsWith('.shp')) {
        throw new Error(text(language, '服务未为所选 .shp 返回匹配的路径和来源修订。',
          'The service did not return a matching path and source revision for the selected .shp.'));
      }
      const capture = captureRaw;
      const familyBeforeRaw: unknown = await backend.request('map.vector.captureShapefile',
        { sourcePath: capture.canonicalPath }, { signal: requestController.signal });
      if (!isCurrent(requestID, requestOwner, requestController.signal)) return;
      if (!isShapefileFamilyCapture(familyBeforeRaw, capture.canonicalPath, capture.revision) ||
          !sameMapSourceFamily(expectedFamily, familyBeforeRaw)) {
        throw new Error(text(language, 'Shapefile 文件族已变化，或读取结果与导入器捕获的来源不一致；属性表查看已停止。',
          'The Shapefile family changed or no longer matches the importer capture; attribute-table loading stopped.'));
      }
      const familyBefore = familyBeforeRaw;
      const tableRaw: unknown = await backend.request('attributeTable.load', { path: capture.canonicalPath },
        { signal: requestController.signal });
      if (!isCurrent(requestID, requestOwner, requestController.signal)) return;
      if (!isAttributeTableData(tableRaw) || !sameSourcePath(tableRaw.sourcePath, capture.canonicalPath)) {
        throw new Error(text(language, '属性表读取结果的来源路径或行列身份无效。',
          'The table result has an invalid source path or row/column identity.'));
      }
      const afterRaw: unknown = await backend.request('workspace.file.inspect', { sourcePath: capture.canonicalPath },
        { signal: requestController.signal });
      if (!isCurrent(requestID, requestOwner, requestController.signal)) return;
      if (!isWorkspaceCapture(afterRaw, capture.canonicalPath)) {
        throw new Error(text(language, '读取后服务未返回匹配的来源文件修订。',
          'The service did not return a matching source-file revision after reading the table.'));
      }
      const familyAfterRaw: unknown = await backend.request('map.vector.captureShapefile',
        { sourcePath: capture.canonicalPath }, { signal: requestController.signal });
      if (!isCurrent(requestID, requestOwner, requestController.signal)) return;
      if (!isShapefileFamilyCapture(familyAfterRaw, capture.canonicalPath, afterRaw.revision) ||
          !sameSourceCapture(capture, afterRaw) || !sameSourceFamilyCapture(familyBefore, familyAfterRaw)) {
        throw new Error(text(language, '读取属性表期间 Shapefile 文件族发生变化；不显示过期读取结果。',
          'The Shapefile family changed while the table was being read; the stale result is not displayed.'));
      }
      setLoaded({ table: tableRaw, capture, family: familyBefore });
      setNotice(text(language, '属性表已读取，仅供查看；此预览不会写入源 Shapefile。',
        'The attribute table is ready for viewing; this preview does not write the source Shapefile.'));
    } catch (caught) {
      if (!requestController.signal.aborted && isCurrent(requestID, requestOwner, requestController.signal)) {
        setError(message(caught, language)); setDetail(diagnostic(caught));
      } else if (requestController.signal.aborted && ownerRef.current === requestOwner) {
        setNotice(text(language, '属性表读取已取消；已丢弃过期回执。', 'Attribute-table loading was cancelled; the stale receipt was discarded.'));
      }
    } finally {
      if (generation.current === requestID) setBusy(false);
    }
  };

  return <section className="vector-import-table-viewer">
    <div className="vector-import-section-heading"><div><span className="tools-kicker">03 / {zh ? '只读属性表' : 'READ-ONLY ATTRIBUTE TABLE'}</span>
      <h4>{zh ? 'Shapefile 属性表' : 'Shapefile attribute table'}</h4></div>
      <span className="vector-import-readonly-pill">{zh ? '只读 · 不写文件' : 'READ ONLY · NO FILE WRITE'}</span></div>
    <p className="tools-muted">{zh ? '读取前后均复核 .shp/.shx/.dbf 及存在时的 .prj；任一来源文件变化时不会展示过期结果。' : 'The required .shp/.shx/.dbf files and any existing .prj are checked before and after loading. Results are discarded if a source file changes.'}</p>
    <div className="ci-actions">
      <button type="button" className="ci-button" disabled={busy || !sourcePath || !expectedFamily}
        onClick={() => void loadTable()}>{busy ? (zh ? '正在读取属性表…' : 'Reading attribute table…') : (zh ? '读取属性表（只读）' : 'Load attribute table (read only)')}</button>
      {busy && <button type="button" className="ci-button" onClick={() => controller.current?.abort()}>{zh ? '请求取消' : 'Request cancellation'}</button>}
    </div>
    {loaded && <>
      <dl className="vector-import-metadata"><div><dt>{zh ? '来源路径' : 'Source path'}</dt><dd><code>{loaded.capture.canonicalPath}</code></dd></div>
        <div><dt>{zh ? '几何类型' : 'Geometry type'}</dt><dd>{loaded.table.shapeType}</dd></div>
        <div><dt>{zh ? '记录数' : 'Records'}</dt><dd>{safeLength(loaded.table) ?? 0}</dd></div>
        <div><dt>{zh ? '字段数' : 'Fields'}</dt><dd>{loaded.table.columns.length}</dd></div></dl>
      <details className="vector-import-family-details"><summary>{zh ? '来源文件版本' : 'Source file revisions'}</summary>
        <p>SHA-256 <code>{loaded.capture.revision}</code> · {zh ? '文件族修订：' : 'Family revision: '}<code>{loaded.family.revision}</code></p>
        <ul>{loaded.family.files.map(file => <li key={file.role}><span>{file.role} · {file.required ? (zh ? '必需' : 'required') : (zh ? '可选' : 'optional')}</span>
          <code>{file.path}</code><code>{file.revision ?? (zh ? '不存在 / 无修订' : 'Missing / no revision')}</code></li>)}</ul>
      </details>
      <AttributeTableDataGrid language={language} table={loaded.table} busy onAdd={() => undefined} onRename={() => undefined}
        onRemove={() => undefined} onSetCells={async () => undefined} />
    </>}
    {notice && <p className="vector-import-notice" role="status">{notice}</p>}
    {error && <div className="vector-import-error" role="alert"><strong>{error}</strong>{detail && <details><summary>{zh ? '原始 Host 诊断' : 'Raw Host diagnostic'}</summary><pre>{detail}</pre></details>}</div>}
  </section>;
}
