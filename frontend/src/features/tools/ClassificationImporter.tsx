import { useEffect, useMemo, useRef, useState } from 'react';
import type { FeaturePageProps } from '../../shared/workbench';
import { message, text } from '../../shared/workbench';
import type { ModelSnapshot } from '../editors/model';
import { assertModelSnapshot, hasUncapturedSessionReferences, modelLoadRequest, modelSaveRequest, sourceOwnerKey, updatedModelContext } from './coreImportWorkflow';
import type { ClassificationImportFile, ClassificationImportInspection, ClassificationImportItem } from './coreImportWorkflow';
import { ClassificationRasterImporterDialog } from './ClassificationRasterImporterDialog';
import { ClassificationVectorImporter } from './ClassificationVectorImporter';
import './coreImportWorkflow.css';

type Props = FeaturePageProps;
interface ImportEntry { item: ClassificationImportItem; selection: ClassificationImportFile; included: boolean }

function rawError(error: unknown): string { return error instanceof Error ? error.message : String(error); }
function label(language: Props['language'], zh: string, en: string): string { return text(language, zh, en); }

function modelContextReady(context: Props['context']): boolean {
  return Boolean(context.projectPath && context.projectXml !== null && context.projectSessionID);
}

function isAbsoluteServerPath(path: string): boolean {
  return path.startsWith('/') || path.startsWith('\\\\') || /^[a-z]:[\\/]/i.test(path);
}

function extension(path: string): string { return path.split(/[\\/]/).pop()?.split('.').pop()?.toLowerCase() ?? ''; }

function supportedPath(path: string): boolean {
  return isAbsoluteServerPath(path) && ['shp', 'tif', 'tiff'].includes(extension(path));
}

function validInspection(value: unknown, sourceCount: number): value is ClassificationImportInspection {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const result = value as Partial<ClassificationImportInspection>;
  if (!Array.isArray(result.items) || result.items.length !== sourceCount || typeof result.projection !== 'string') return false;
  return result.items.every(item => {
    const selection = item?.selection;
    if (!selection || typeof selection.file !== 'string' || !isAbsoluteServerPath(selection.file) ||
        !/^[\da-f]{64}$/i.test(selection.revision) || !['Raster', 'Polygon'].includes(selection.kind)) return false;
    if (selection.kind === 'Raster') return Boolean(item.raster && typeof item.raster.rgba === 'string' &&
      Number.isInteger(item.raster.width) && item.raster.width > 0 && Number.isInteger(item.raster.height) && item.raster.height > 0 &&
      Number.isInteger(item.rows) && Number.isInteger(item.columnsCount) && Number.isInteger(item.bands));
    return Array.isArray(item.columns) && Number.isInteger(item.featureCount) && item.featureCount > 0 &&
      Number.isInteger(item.previewFeatureCount);
  });
}

export function ClassificationImporter({ language, context, backend, onContextChange, onContextCommit, hasUnsavedDrafts }: Props) {
  const asset = context.selectedAsset?.kind === 'Surface Layers' ? context.selectedAsset : null;
  const owner = sourceOwnerKey(context);
  const ownerRef = useRef(owner); ownerRef.current = owner;
  const generation = useRef(0);
  const controller = useRef<AbortController | null>(null);
  const acceptedOwner = useRef<string | null>(null);
  const [paths, setPaths] = useState('');
  const [snapshot, setSnapshot] = useState<ModelSnapshot>();
  const [inspection, setInspection] = useState<ClassificationImportInspection>();
  const [entries, setEntries] = useState<ImportEntry[]>([]);
  const [busy, setBusy] = useState<'inspect' | 'import' | null>(null);
  const [error, setError] = useState<string>();
  const [rawDiagnostic, setRawDiagnostic] = useState<string>();
  const [cancelNote, setCancelNote] = useState<string>();
  const [savedRevision, setSavedRevision] = useState<string>();
  const [savedFiles, setSavedFiles] = useState<ClassificationImportFile[]>([]);

  const filePaths = useMemo(() => [...new Set(paths.split(/\r?\n/).map(path => path.trim()).filter(Boolean))], [paths]);
  const supportsLoad = backend.supports('model.load');
  const supportsInspect = backend.supports('model.classificationInspect');
  const supportsImport = backend.supports('model.classificationImport');
  const ready = modelContextReady(context) && !hasUncapturedSessionReferences(context, asset);
  const pathListValid = filePaths.length > 0 && filePaths.every(supportedPath);
  const canInspect = Boolean(asset && ready && pathListValid && supportsLoad && supportsInspect && !hasUnsavedDrafts && busy === null);
  const chosenEntries = entries.filter(entry => entry.included);
  const selectionsValid = chosenEntries.length > 0 && chosenEntries.every(({ item, selection }) =>
    selection.file === item.selection.file && selection.revision === item.selection.revision &&
    selection.kind === item.selection.kind && Boolean(selection.name?.trim()) &&
    (selection.kind !== 'Polygon' || !selection.column || item.columns.includes(selection.column)));
  const canImport = Boolean(inspection && snapshot && ready && supportsImport && selectionsValid &&
    !hasUnsavedDrafts && busy === null && ownerRef.current === owner);

  useEffect(() => {
    if (acceptedOwner.current === owner) { acceptedOwner.current = null; return; }
    generation.current += 1;
    controller.current?.abort();
    controller.current = null;
    setPaths(''); setSnapshot(undefined); setInspection(undefined); setEntries([]);
    setBusy(null); setError(undefined); setRawDiagnostic(undefined); setCancelNote(undefined);
    setSavedRevision(undefined); setSavedFiles([]);
  }, [owner]);

  useEffect(() => () => {
    generation.current += 1;
    controller.current?.abort();
    controller.current = null;
  }, []);

  const isCurrent = (id: number, signal: AbortSignal): boolean =>
    generation.current === id && ownerRef.current === owner && !signal.aborted;

  const begin = (): { id: number; signal: AbortSignal } => {
    controller.current?.abort();
    const next = new AbortController();
    controller.current = next;
    const id = ++generation.current;
    return { id, signal: next.signal };
  };

  const updatePaths = (value: string): void => {
    generation.current += 1;
    controller.current?.abort();
    controller.current = null;
    setBusy(null); setPaths(value); setSnapshot(undefined); setInspection(undefined); setEntries([]);
    setError(undefined); setRawDiagnostic(undefined); setCancelNote(undefined); setSavedRevision(undefined); setSavedFiles([]);
  };

  const loadClassification = async (signal: AbortSignal): Promise<ModelSnapshot> => {
    if (!asset || !context.projectPath || context.projectXml === null) {
      throw new Error(label(language, '请从当前工程选择一个地表图层模型。', 'Select a surface-layer model from the current project.'));
    }
    if (!backend.supports('model.load')) throw new Error(label(language, 'C# 服务未发布 model.load。', 'The C# server has not advertised model.load.'));
    const value = await backend.request<ModelSnapshot>('model.load', modelLoadRequest(context, asset), { signal });
    assertModelSnapshot(value);
    if (!value.classification) throw new Error(label(language,
      '该 Surface Layers 文件是糙率图层（NValue）或不含分类图层，当前分类导入接口不支持它。',
      'This Surface Layers file is a Manning (NValue) layer or contains no classification layer; classification import does not support it.'));
    return value;
  };

  const inspect = async (): Promise<void> => {
    if (!canInspect || !asset) return;
    const request = begin();
    setBusy('inspect'); setError(undefined); setRawDiagnostic(undefined); setCancelNote(undefined);
    setSnapshot(undefined); setInspection(undefined); setEntries([]); setSavedRevision(undefined); setSavedFiles([]);
    try {
      const loaded = await loadClassification(request.signal);
      if (!isCurrent(request.id, request.signal)) return;
      const model = modelSaveRequest(context, asset, loaded);
      const result = await backend.request<ClassificationImportInspection>('model.classificationInspect', {
        model, files: filePaths,
      }, { signal: request.signal });
      if (!isCurrent(request.id, request.signal)) return;
      if (!validInspection(result, filePaths.length)) {
        throw new Error(label(language, '分类检查回执缺少匹配来源、SHA-256、类型或预览数据。', 'The classification inspection receipt is missing a matching source, SHA-256, type, or preview.'));
      }
      const inputKinds = filePaths.map(path => ['tif', 'tiff'].includes(extension(path)) ? 'Raster' : 'Polygon');
      if (result.items.some((item, index) => item.selection.kind !== inputKinds[index])) {
        throw new Error(label(language, '服务端分类来源类型与输入路径顺序不匹配。', 'The server classification source types do not match the input path order.'));
      }
      setSnapshot(loaded);
      setInspection(result);
      setEntries(result.items.map(item => ({ item, selection: { ...item.selection }, included: true })));
    } catch (caught) {
      if (request.signal.aborted && generation.current === request.id) setCancelNote(label(language, '分类来源检查已取消。', 'Classification source inspection was cancelled.'));
      else if (isCurrent(request.id, request.signal)) { setError(message(caught, language)); setRawDiagnostic(rawError(caught)); }
    } finally {
      if (generation.current === request.id) setBusy(null);
    }
  };

  const commitImport = async (): Promise<void> => {
    if (!canImport || !asset || !snapshot || !inspection) return;
    const request = begin();
    setBusy('import'); setError(undefined); setRawDiagnostic(undefined); setCancelNote(undefined);
    setSavedRevision(undefined); setSavedFiles([]);
    try {
      if (!modelContextReady(context) || hasUncapturedSessionReferences(context, asset)) {
        throw new Error(label(language, '当前工程会话或外部引用捕获已变化，请重新检查分类来源。', 'The project session or external-reference capture changed. Inspect classification sources again.'));
      }
      if (!snapshot.classification) throw new Error(label(language, '当前检查快照不是分类图层。', 'The inspected snapshot is not a classification layer.'));
      const selected = chosenEntries.map(({ item, selection }) => {
        if (selection.file !== item.selection.file || selection.revision !== item.selection.revision ||
            selection.kind !== item.selection.kind || !selection.name?.trim() ||
            (selection.kind === 'Polygon' && selection.column && !item.columns.includes(selection.column))) {
          throw new Error(label(language, `来源选择无效：${item.selection.file}`, `Invalid source selection: ${item.selection.file}`));
        }
        return { ...selection, name: selection.name.trim() };
      });
      const saved = await backend.request<ModelSnapshot>('model.classificationImport', {
        model: modelSaveRequest(context, asset, snapshot), files: selected,
      }, { signal: request.signal });
      if (!isCurrent(request.id, request.signal)) return;
      assertModelSnapshot(saved);
      if (!saved.classification || !saved.revision.trim() || saved.revision === snapshot.revision) {
        throw new Error(label(language, '分类导入保存回执未确认新的分类模型修订。', 'The classification import save receipt did not confirm a new model revision.'));
      }
      const nextContext = updatedModelContext(context, asset, saved);
      const accepted = onContextCommit ? onContextCommit(context, nextContext) : true;
      if (!accepted) {
        setError(label(language, '服务端已返回保存回执，但工作台拒绝了过期上下文；请重新载入模型。', 'The server returned a save receipt, but the workbench rejected the stale context. Reload the model.'));
        return;
      }
      acceptedOwner.current = sourceOwnerKey(nextContext);
      if (!onContextCommit) onContextChange(nextContext);
      setSnapshot(saved); setSavedRevision(saved.revision); setSavedFiles(selected);
      setInspection(undefined); setEntries([]); setError(undefined); setRawDiagnostic(undefined);
    } catch (caught) {
      if (request.signal.aborted && generation.current === request.id) setCancelNote(label(language,
        '已请求取消，但未收到最终保存回执；导入状态未确认。重新载入模型后再检查，勿将此操作视为完成。',
        'Cancellation was requested, but no final save receipt arrived. The import status is unconfirmed; reload the model before retrying and do not treat this as complete.'));
      else if (isCurrent(request.id, request.signal)) { setError(message(caught, language)); setRawDiagnostic(rawError(caught)); }
    } finally {
      if (generation.current === request.id) setBusy(null);
    }
  };

  const cancel = (): void => {
    controller.current?.abort();
    generation.current += 1;
    setBusy(null); setSnapshot(undefined); setInspection(undefined); setEntries([]);
    setCancelNote(label(language, '已请求取消。服务端最终状态未知；请重新载入模型并检查来源后再决定是否重试。',
      'Cancellation was requested. The server’s terminal state is unknown; reload the model and reinspect before deciding whether to retry.'));
  };

  const updateEntry = (file: string, update: (entry: ImportEntry) => ImportEntry): void => {
    setEntries(current => current.map(entry => entry.item.selection.file === file ? update(entry) : entry));
  };

  return (
    <section className="ci-panel" aria-labelledby="classification-importer-title">
      <header className="ci-import-heading">
        <div><span className="ci-kicker">ClassificationImporter · ClassificationVectorImporter · ClassificationRasterImporterDialog</span>
          <h3 id="classification-importer-title">{label(language, '地表分类导入', 'Surface classification import')}</h3></div>
        <span className={`tools-runtime-pill ${supportsInspect && supportsImport ? 'is-connected' : 'is-unavailable'}`}>
          {supportsInspect && supportsImport ? label(language, 'C# 操作已发布', 'C# operations advertised') : label(language, 'C# 操作未就绪', 'C# operations unavailable')}
        </span>
      </header>
      {!asset ? <div className="ci-warning" role="status">{label(language, '请从工程浏览器选择“地表图层”后使用分类导入。', 'Select a Surface Layers asset in the project browser to use classification import.')}</div> : <>
        <dl className="ci-details"><div><dt>{label(language, '分类模型', 'Classification model')}</dt><dd>{asset.name}</dd></div>
          <div><dt>{label(language, '模型路径', 'Model path')}</dt><dd>{asset.path}</dd></div>
          <div><dt>{label(language, '模型修订', 'Model revision')}</dt><dd>{snapshot?.revision ?? asset.revision ?? '—'}</dd></div></dl>
        <label className="ci-field"><span>{label(language, '服务端可见来源路径（每行一个 .tif/.tiff 或多边形 .shp）', 'Server-visible source paths (one .tif/.tiff or polygon .shp per line)')}</span>
          <textarea value={paths} disabled={busy !== null} spellCheck={false} onChange={event => updatePaths(event.target.value)} placeholder="/data/classes.tif\n/data/classes.shp" /></label>
        <p className="ci-help">{label(language, '该 C# Host 接收服务端路径，不提供本地文件上传。Shapefile 的关联文件由服务端按同目录读取并一并校验修订。',
          'This C# Host accepts server-visible paths and does not upload local files. The server reads and revision-checks shapefile sidecars from the same directory.')}</p>
        {hasUnsavedDrafts && <div className="ci-warning" role="status">{label(language, '请先保存或丢弃其他编辑器草稿，再检查或导入分类图层。', 'Save or discard other editor drafts before inspecting or importing classification layers.')}</div>}
        {!modelContextReady(context) && <div className="ci-warning" role="status">{label(language, '分类导入需要当前工程路径、XML 快照和会话。', 'Classification import requires the active project path, XML snapshot, and session.')}</div>}
        {hasUncapturedSessionReferences(context, asset) && <div className="ci-warning" role="status">{label(language, '此资产携带会话外部引用但缺少精确捕获；请从当前地图树重新选择。', 'This asset carries session-only references without an exact capture. Select it again from the active map tree.')}</div>}
        {filePaths.length > 0 && !pathListValid && <div className="ci-warning" role="status">{label(language, '每个来源都必须是 C# 服务端可见的绝对路径，扩展名限于 .tif、.tiff 或 .shp。', 'Each source must be an absolute path visible to C# and end in .tif, .tiff, or .shp.')}</div>}
        <div className="ci-actions">
          <button type="button" className="ci-button ci-primary" disabled={!canInspect} onClick={() => void inspect()}>{busy === 'inspect' ? label(language, '载入并检查中…', 'Loading and inspecting…') : label(language, '载入并检查来源', 'Load and inspect sources')}</button>
          {busy === 'import' && <button type="button" className="ci-button ci-danger" onClick={cancel}>{label(language, '请求取消', 'Request cancellation')}</button>}
          {savedRevision && <span className="ci-inline-meta">{label(language, '新模型修订', 'New model revision')}: <code>{savedRevision}</code></span>}
        </div>
        {busy && <div className="ci-warning" role="status">{busy === 'inspect' ? label(language, 'C# 正在载入分类模型并检查每个来源。', 'C# is loading the classification model and inspecting each source.') : label(language, 'C# 正在导入分类来源并保存模型。', 'C# is importing classification sources and saving the model.')}</div>}
        {error && <div className="ci-error" role="alert"><strong>{error}</strong>{rawDiagnostic && <details><summary>{label(language, '原始诊断', 'Raw diagnostic')}</summary><pre>{rawDiagnostic}</pre></details>}</div>}
        {cancelNote && <div className="ci-warning" role="status">{cancelNote}</div>}
        {inspection && <section className="ci-subdialog" aria-labelledby="classification-inspection-title">
          <header className="ci-import-heading"><div><span className="ci-kicker">model.classificationInspect</span><h3 id="classification-inspection-title">{label(language, '检查结果与导入选择', 'Inspection results and import selections')}</h3></div>
            <small>{label(language, '目标投影', 'Target projection')}: {inspection.projection || '—'}</small></header>
          <p className="ci-help">{label(language, '仅使用 C# 检查返回的来源路径与 SHA-256。点击“导入并保存分类”才会写入模型；成功仅由新模型保存回执确认。',
            'The inspected server paths and SHA-256 values are pinned. The model is written only after clicking Import and save; success requires a new model-save receipt.')}</p>
          {entries.map(entry => entry.selection.kind === 'Raster' ? <ClassificationRasterImporterDialog key={`${entry.item.selection.file}:${entry.item.selection.revision}`}
            language={language} item={entry.item} selection={entry.selection} included={entry.included} busy={busy !== null} projection={inspection.projection}
            onIncludeChange={included => updateEntry(entry.item.selection.file, current => ({ ...current, included }))}
            onSelectionChange={selection => updateEntry(entry.item.selection.file, current => ({ ...current, selection }))} /> :
            <ClassificationVectorImporter key={`${entry.item.selection.file}:${entry.item.selection.revision}`}
              language={language} item={entry.item} selection={entry.selection} included={entry.included} busy={busy !== null}
              onIncludeChange={included => updateEntry(entry.item.selection.file, current => ({ ...current, included }))}
              onSelectionChange={selection => updateEntry(entry.item.selection.file, current => ({ ...current, selection }))} />)}
          <div className="ci-actions"><button type="button" className="ci-button ci-primary" disabled={!canImport} onClick={() => void commitImport()}>
            {busy === 'import' ? label(language, '导入并保存中…', 'Importing and saving…') : label(language, '导入并保存分类', 'Import and save classification')}</button>
            <span className="ci-inline-meta">{chosenEntries.length} / {entries.length} {label(language, '个来源已选择', 'sources selected')}</span></div>
        </section>}
        {savedRevision && <section className="ci-success" aria-live="polite"><strong>{label(language, '分类导入已由保存回执确认', 'Classification import confirmed by save receipt')}</strong>
          <div>{label(language, '新模型 SHA-256', 'New model SHA-256')}: <code>{savedRevision}</code></div>
          <ul className="ci-operation-list">{savedFiles.map(file => <li key={`${file.file}:${file.revision}`}><strong>{file.kind}</strong><span>{file.name} · {file.file}</span><code>{file.revision}</code></li>)}</ul>
        </section>}
      </>}
    </section>
  );
}
