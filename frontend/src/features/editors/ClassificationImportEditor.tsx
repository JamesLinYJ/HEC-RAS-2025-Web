import { useEffect, useMemo, useRef, useState } from 'react';
import type { EditorPageProps, ClassificationImportFile, ClassificationImportInspection, ClassificationImportItem } from './model';
import { rgbaFrameImage } from '../map/mapImages';
import '../tools/coreImportWorkflow.css';

interface ImportEntry {
  item: ClassificationImportItem;
  selection: ClassificationImportFile;
  included: boolean;
}

type BusyAction = 'inspect' | 'commit' | null;

function label(language: EditorPageProps['language'], zh: string, en: string): string {
  return language === 'zh' ? zh : en;
}

function isAbsoluteServerPath(value: string): boolean {
  return value.startsWith('/') || value.startsWith('\\\\') || /^[a-z]:[\\/]/i.test(value);
}

function extension(value: string): string {
  return value.split(/[\\/]/).pop()?.split('.').pop()?.toLowerCase() ?? '';
}

function pathKey(value: string): string {
  const path = value.replace(/\\/g, '/').replace(/\/+$/, '');
  return /^[a-z]:\//i.test(path) || path.startsWith('//') ? path.toLowerCase() : path;
}

function supportedPath(value: string): boolean {
  return isAbsoluteServerPath(value) && ['shp', 'tif', 'tiff'].includes(extension(value));
}

function validInspection(value: unknown, paths: string[]): value is ClassificationImportInspection {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const result = value as Partial<ClassificationImportInspection>;
  if (!Array.isArray(result.items) || result.items.length !== paths.length || typeof result.projection !== 'string') return false;
  const expectedKeys = paths.map(pathKey);
  const responseKeys = result.items.map((item) => pathKey(item.selection?.file ?? ''));
  if (new Set(responseKeys).size !== responseKeys.length || responseKeys.some((key, index) => key !== expectedKeys[index])) return false;
  return result.items.every((item, index) => {
    const selection = item?.selection;
    if (!selection || !isAbsoluteServerPath(selection.file) || !/^[\da-f]{64}$/i.test(selection.revision)
      || !['Raster', 'Polygon'].includes(selection.kind)) return false;
    const expectedKind = ['tif', 'tiff'].includes(extension(paths[index] ?? '')) ? 'Raster' : 'Polygon';
    if (selection.kind !== expectedKind) return false;
    if (selection.kind === 'Raster') {
      const raster = item.raster;
      return Boolean(raster && typeof raster.rgba === 'string' && raster.rgba.length > 0
        && Number.isInteger(raster.width) && raster.width > 0
        && Number.isInteger(raster.height) && raster.height > 0
        && Number.isInteger(item.rows) && (item.rows ?? 0) > 0
        && Number.isInteger(item.columnsCount) && (item.columnsCount ?? 0) > 0
        && Number.isInteger(item.bands) && (item.bands ?? 0) > 0);
    }
    return Array.isArray(item.columns) && item.columns.every((column) => typeof column === 'string')
      && Number.isInteger(item.featureCount) && item.featureCount > 0
      && Number.isInteger(item.previewFeatureCount) && item.previewFeatureCount >= 0
      && (selection.column == null || item.columns.includes(selection.column));
  });
}

function validSelections(entries: ImportEntry[]): boolean {
  const chosen = entries.filter((entry) => entry.included);
  if (chosen.length === 0) return false;
  const rasterNames = chosen.filter((entry) => entry.selection.kind === 'Raster')
    .map((entry) => entry.selection.name?.trim().toLocaleLowerCase()).filter((name): name is string => Boolean(name));
  if (rasterNames.length !== new Set(rasterNames).size) return false;
  return chosen.every(({ item, selection }) => selection.file === item.selection.file
    && selection.revision === item.selection.revision
    && selection.kind === item.selection.kind
    && (selection.kind !== 'Raster' || Boolean(selection.name?.trim()))
    && (selection.kind !== 'Polygon' || selection.column == null || item.columns.includes(selection.column)));
}

function rasterImage(item: ClassificationImportItem): { image: string; error: string } {
  if (!item.raster) return { image: '', error: '' };
  try { return { image: rgbaFrameImage(item.raster.rgba, item.raster.width, item.raster.height), error: '' }; }
  catch (error) { return { image: '', error: error instanceof Error ? error.message : String(error) }; }
}

/** Source-aligned ClassificationImporter workflow with server inspection and an explicit immediate-save confirmation. */
export function ClassificationImportEditor(props: EditorPageProps) {
  const workflow = props.classificationImport;
  const owner = workflow?.owner ?? `${props.asset.kind}:${props.asset.path}:${props.snapshot.revision}`;
  const ownerRef = useRef(owner);
  ownerRef.current = owner;
  const generation = useRef(0);
  const controller = useRef<AbortController | null>(null);
  const [expanded, setExpanded] = useState(false);
  const [paths, setPaths] = useState('');
  const [inspection, setInspection] = useState<ClassificationImportInspection>();
  const [entries, setEntries] = useState<ImportEntry[]>([]);
  const [inspectionRevision, setInspectionRevision] = useState('');
  const [busy, setBusy] = useState<BusyAction>(null);
  const [confirmedImmediateSave, setConfirmedImmediateSave] = useState(false);
  const [error, setError] = useState('');
  const [cancelNote, setCancelNote] = useState('');
  const [savedRevision, setSavedRevision] = useState('');

  const filePaths = useMemo(() => [...new Set(paths.split(/\r?\n/).map((path) => path.trim()).filter(Boolean))], [paths]);
  const pathsValid = filePaths.length > 0 && filePaths.every(supportedPath);
  const selectedEntries = entries.filter((entry) => entry.included);
  const choicesValid = validSelections(entries);
  const currentRevision = inspectionRevision === props.snapshot.revision;
  const canInspect = Boolean(workflow?.canInspect && pathsValid && busy === null);
  const canCommit = Boolean(workflow?.canImport && inspection && currentRevision && choicesValid
    && confirmedImmediateSave && busy === null);

  useEffect(() => {
    generation.current += 1;
    controller.current?.abort();
    controller.current = null;
    setPaths(''); setInspection(undefined); setEntries([]); setInspectionRevision('');
    setBusy(null); setConfirmedImmediateSave(false); setError(''); setCancelNote(''); setSavedRevision('');
    setExpanded(false);
  }, [owner]);

  useEffect(() => () => {
    generation.current += 1;
    controller.current?.abort();
    controller.current = null;
  }, []);

  const isCurrent = (requestID: number, signal: AbortSignal): boolean =>
    generation.current === requestID && ownerRef.current === owner && !signal.aborted;

  const begin = (): { id: number; signal: AbortSignal } => {
    controller.current?.abort();
    const next = new AbortController();
    controller.current = next;
    return { id: ++generation.current, signal: next.signal };
  };

  const changePaths = (value: string): void => {
    generation.current += 1;
    controller.current?.abort();
    controller.current = null;
    setPaths(value); setInspection(undefined); setEntries([]); setInspectionRevision('');
    setBusy(null); setConfirmedImmediateSave(false); setError(''); setCancelNote(''); setSavedRevision('');
  };

  const inspect = async (): Promise<void> => {
    if (!canInspect || !workflow) return;
    const request = begin();
    setBusy('inspect'); setInspection(undefined); setEntries([]); setInspectionRevision('');
    setConfirmedImmediateSave(false); setError(''); setCancelNote(''); setSavedRevision('');
    try {
      const result = await workflow.inspect(filePaths, request.signal);
      if (!isCurrent(request.id, request.signal)) return;
      if (result.revision !== props.snapshot.revision || !validInspection(result.inspection, filePaths)) {
        throw new Error(label(props.language, '检查回执与当前模型修订或来源列表不匹配，请重新检查。', 'The inspection receipt does not match the current model revision or source list. Inspect again.'));
      }
      setInspection(result.inspection);
      setInspectionRevision(result.revision);
      setEntries(result.inspection.items.map((item) => ({ item, selection: { ...item.selection }, included: true })));
    } catch (caught) {
      if (request.signal.aborted && generation.current === request.id) setCancelNote(label(props.language, '来源检查已取消；没有执行导入保存。', 'Source inspection was cancelled; no import save was attempted.'));
      else if (isCurrent(request.id, request.signal)) setError(caught instanceof Error ? caught.message : String(caught));
    } finally {
      if (generation.current === request.id) setBusy(null);
    }
  };

  const commit = async (): Promise<void> => {
    if (!canCommit || !workflow || !inspection) return;
    const request = begin();
    setBusy('commit'); setConfirmedImmediateSave(false); setError(''); setCancelNote(''); setSavedRevision('');
    try {
      const selected = selectedEntries.map(({ item, selection }) => {
        if (selection.file !== item.selection.file || selection.revision !== item.selection.revision
          || selection.kind !== item.selection.kind || (selection.kind === 'Raster' && !selection.name?.trim())
          || (selection.kind === 'Polygon' && selection.column != null && !item.columns.includes(selection.column))) {
          throw new Error(label(props.language, `来源选项已失效：${item.selection.file}`, `The source selection is no longer valid: ${item.selection.file}`));
        }
        return { ...selection, ...(selection.kind === 'Raster' ? { name: selection.name?.trim() } : {}) };
      });
      const receipt = await workflow.commit(inspectionRevision, selected, request.signal);
      if (!isCurrent(request.id, request.signal)) return;
      if (!receipt) {
        setCancelNote(label(props.language,
          '没有收到并接纳新的模型保存回执，最终状态未确认。请重新载入该模型并检查后再决定是否重试。',
          'A new model-save receipt was not received and accepted. The final status is unconfirmed. Reload and inspect the model before retrying.'));
        return;
      }
      if (!receipt.revision || receipt.revision === inspectionRevision || !receipt.classification) {
        throw new Error(label(props.language, '保存回执没有确认新的分类模型修订。', 'The save receipt did not confirm a new classification model revision.'));
      }
      setSavedRevision(receipt.revision);
      setInspection(undefined); setEntries([]); setInspectionRevision(''); setError(''); setCancelNote('');
    } catch (caught) {
      if (request.signal.aborted && generation.current === request.id) setCancelNote(label(props.language,
        '已请求取消，但分类导入可能已在服务端完成；最终保存状态未确认。重新载入模型并核对后再决定是否重试。',
        'Cancellation was requested, but classification import may have completed on the server. The final save status is unconfirmed. Reload and verify the model before retrying.'));
      else if (isCurrent(request.id, request.signal)) setError(caught instanceof Error ? caught.message : String(caught));
    } finally {
      if (generation.current === request.id) setBusy(null);
    }
  };

  const updateEntry = (file: string, update: (entry: ImportEntry) => ImportEntry): void => {
    setConfirmedImmediateSave(false);
    setEntries((current) => current.map((entry) => entry.item.selection.file === file ? update(entry) : entry));
  };

  const blockedMessage = workflow?.blockedMessage ?? label(props.language,
    '分类来源导入当前不可用；请确认这是可编辑的分类图层，且服务端已发布来源检查和导入保存能力。',
    'Classification import is unavailable. Select an editable classification layer with source-inspection and import-save capabilities.');

  return (
    <section className="ci-panel editor-classification-import" aria-labelledby="classification-import-title">
      <header className="ci-import-heading">
        <div><h3 id="classification-import-title">{label(props.language, '导入分类来源', 'Import classification sources')}</h3>
          <p className="ci-help">{label(props.language, '检查服务器可访问的 TIFF 栅格或多边形 Shapefile，并预览真实返回数据。确认后会立即保存模型。', 'Inspect server-visible TIFF rasters or polygon shapefiles and preview returned data. Confirmation saves the model immediately.')}</p></div>
        <button type="button" className="ci-button" aria-expanded={expanded} data-testid="classification-import-toggle" onClick={() => setExpanded((value) => !value)}>
          {expanded ? label(props.language, '收起导入', 'Hide import') : label(props.language, '选择来源', 'Choose sources')}
        </button>
      </header>
      {!workflow?.canInspect && <div className="ci-warning" role="status">{blockedMessage}</div>}
      {expanded && <>
        <label className="ci-field"><span>{label(props.language, '服务器可访问的绝对路径（每行一个 .tif/.tiff 或多边形 .shp）', 'Absolute server-visible paths (one .tif/.tiff or polygon .shp per line)')}</span>
          <textarea data-testid="classification-import-paths" value={paths} disabled={busy !== null} spellCheck={false}
            onChange={(event) => changePaths(event.currentTarget.value)} placeholder="/data/classes.tif\n/data/classes.shp" /></label>
        <p className="ci-help">{label(props.language,
          '此入口使用服务端路径，不上传本机文件。Shapefile 的 .shx、.dbf、.prj、.cpg 修订由服务端一并校验。',
          'This workflow uses server-visible paths and does not upload local files. The server also verifies available .shx, .dbf, .prj and .cpg shapefile sidecars.')}</p>
        {filePaths.length > 0 && !pathsValid && <div className="ci-warning" role="status">{label(props.language, '路径须为绝对路径，扩展名限于 .tif、.tiff 或 .shp。', 'Paths must be absolute and end in .tif, .tiff or .shp.')}</div>}
        <div className="ci-actions">
          <button type="button" className="ci-button ci-primary" data-testid="classification-import-inspect" disabled={!canInspect} onClick={() => void inspect()}>
            {busy === 'inspect' ? label(props.language, '正在检查…', 'Inspecting…') : label(props.language, '检查并预览', 'Inspect and preview')}
          </button>
          {busy === 'commit' && <span className="ci-inline-meta" role="status">{label(props.language, '正在导入并保存；可用页面顶部的取消按钮请求中止。', 'Import and save are in progress. Use the page-level cancel action to request cancellation.')}</span>}
        </div>
        {error && <div className="ci-error" role="alert">{error}</div>}
        {cancelNote && <div className="ci-warning" role="status">{cancelNote}</div>}
        {savedRevision && <div className="ci-success" role="status"><strong>{label(props.language, '分类导入已由模型保存回执确认。', 'Classification import was confirmed by the model-save receipt.')}</strong> · <code>{savedRevision}</code></div>}
        {inspection && <section className="ci-subdialog" aria-labelledby="classification-import-preview-title">
          <header className="ci-import-heading"><h4 id="classification-import-preview-title">{label(props.language, '来源预览与导入选项', 'Source preview and import options')}</h4>
            <span className="ci-inline-meta">{label(props.language, '目标投影', 'Target projection')}: {inspection.projection || '—'} · {label(props.language, '模型修订', 'Model revision')}: <code>{inspectionRevision}</code></span></header>
          {!currentRevision && <div className="ci-warning" role="status">{label(props.language, '模型修订已变化，请重新检查来源。', 'The model revision changed. Inspect the sources again.')}</div>}
          {entries.map(({ item, selection, included }) => item.selection.kind === 'Raster' ? (
            <ClassificationRasterImportEntry key={`${item.selection.file}:${item.selection.revision}`} language={props.language} item={item} selection={selection}
              included={included} disabled={busy !== null} onIncludeChange={(value) => updateEntry(item.selection.file, (entry) => ({ ...entry, included: value }))}
              onSelectionChange={(value) => updateEntry(item.selection.file, (entry) => ({ ...entry, selection: value }))} />
          ) : (
            <ClassificationVectorImportEntry key={`${item.selection.file}:${item.selection.revision}`} language={props.language} item={item} selection={selection}
              included={included} disabled={busy !== null} onIncludeChange={(value) => updateEntry(item.selection.file, (entry) => ({ ...entry, included: value }))}
              onSelectionChange={(value) => updateEntry(item.selection.file, (entry) => ({ ...entry, selection: value }))} />
          ))}
          {!choicesValid && <div className="ci-warning" role="status">{label(props.language, '至少选择一个来源；栅格名称须有效且不能重复，矢量分类列须来自检查返回的字符串列。', 'Select at least one source. Raster names must be valid and unique, and vector classification fields must be inspected string columns.')}</div>}
          {selectedEntries.some(({ selection }) => selection.kind === 'Polygon') && <p className="ci-help">{label(props.language,
            '矢量要素会追加到现有多边形分类输入；若不存在，服务端会新建默认名称的输入。分类字段使用所选字符串列。',
            'Vector features are appended to the existing polygon input; if none exists, the service creates its default input. The selected string column supplies class values.')}</p>}
          <label className="ci-check-line"><input type="checkbox" data-testid="classification-import-confirm-checkbox" checked={confirmedImmediateSave} disabled={busy !== null}
            onChange={(event) => setConfirmedImmediateSave(event.currentTarget.checked)} />
            {label(props.language, '我确认：导入会立即写入分类模型文件，不进入普通草稿保存流程。', 'I confirm that import writes to the classification model immediately instead of the ordinary draft-save flow.')}
          </label>
          <div className="ci-actions">
            <button type="button" className="ci-button ci-primary" data-testid="classification-import-commit" disabled={!canCommit} onClick={() => void commit()}>
              {busy === 'commit' ? label(props.language, '正在导入并保存…', 'Importing and saving…') : label(props.language, '确认导入并立即保存', 'Confirm import and save now')}
            </button>
            <span className="ci-inline-meta">{selectedEntries.length} / {entries.length} {label(props.language, '个来源已选择', 'sources selected')}</span>
          </div>
          {!workflow?.canImport && <div className="ci-warning" role="status">{blockedMessage}</div>}
        </section>}
      </>}
    </section>
  );
}

function ClassificationRasterImportEntry({ language, item, selection, included, disabled, onIncludeChange, onSelectionChange }: {
  language: EditorPageProps['language']; item: ClassificationImportItem; selection: ClassificationImportFile;
  included: boolean; disabled: boolean; onIncludeChange: (value: boolean) => void; onSelectionChange: (value: ClassificationImportFile) => void;
}) {
  const raster = item.raster;
  const preview = useMemo(() => rasterImage(item), [item]);
  return <article className="ci-import-row" aria-label={label(language, '分类栅格来源', 'Classification raster source')}>
    <label className="ci-check-line"><input type="checkbox" checked={included} disabled={disabled} onChange={(event) => onIncludeChange(event.currentTarget.checked)} />
      <strong>{selection.name || item.selection.file}</strong></label>
    <code>{item.selection.file}</code><code>{item.selection.revision}</code>
    <div className="ci-inline-meta">{item.rows} × {item.columnsCount} · {item.bands} {label(language, '波段', 'bands')} · {label(language, '像元范围', 'Value range')}: {raster?.minimum ?? '—'} – {raster?.maximum ?? '—'}</div>
    <div className="ci-inline-meta">{label(language, '坐标范围', 'Extent')}: {raster ? `${raster.minX}, ${raster.minY} – ${raster.maxX}, ${raster.maxY}` : '—'}</div>
    {preview.image && <img className="ci-grid-preview" src={preview.image} alt={label(language, '分类栅格检查预览', 'Classification raster inspection preview')} />}
    {preview.error && <div className="ci-warning" role="status">{label(language, '预览图无法显示', 'Preview image cannot be displayed')}: {preview.error}</div>}
    <label className="ci-field"><span>{label(language, '分类图层名称', 'Classification layer name')}</span>
      <input value={selection.name ?? ''} disabled={disabled} onChange={(event) => onSelectionChange({ ...selection, name: event.currentTarget.value })} /></label>
  </article>;
}

function ClassificationVectorImportEntry({ language, item, selection, included, disabled, onIncludeChange, onSelectionChange }: {
  language: EditorPageProps['language']; item: ClassificationImportItem; selection: ClassificationImportFile;
  included: boolean; disabled: boolean; onIncludeChange: (value: boolean) => void; onSelectionChange: (value: ClassificationImportFile) => void;
}) {
  return <article className="ci-import-row" aria-label={label(language, '分类矢量来源', 'Classification vector source')}>
    <label className="ci-check-line"><input type="checkbox" checked={included} disabled={disabled} onChange={(event) => onIncludeChange(event.currentTarget.checked)} />
      <strong>{item.selection.file}</strong></label>
    <code>{item.selection.file}</code><code>{item.selection.revision}</code>
    <div className="ci-inline-meta">{label(language, '多边形要素', 'Polygon features')}: {item.featureCount} · {label(language, '预览要素', 'Preview features')}: {item.previewFeatureCount}</div>
    <label className="ci-field"><span>{label(language, '分类字段（字符串列）', 'Classification field (string column)')}</span>
      <select value={selection.column ?? ''} disabled={disabled} onChange={(event) => onSelectionChange({ ...selection, column: event.currentTarget.value || null })}>
        <option value="">{label(language, '不使用字段（NoData）', 'No field (NoData)')}</option>
        {item.columns.map((column) => <option key={column} value={column}>{column}</option>)}
      </select>
    </label>
    {item.geoJson && <details><summary>{label(language, '查看服务端返回的矢量预览', 'View returned vector preview')}</summary><pre className="ci-preview-data">{item.geoJson}</pre></details>}
  </article>;
}
