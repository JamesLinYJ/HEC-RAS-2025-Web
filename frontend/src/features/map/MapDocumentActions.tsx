import { useEffect, useRef, useState } from 'react';
import type { Language, MapDocumentDeleteReceipt, MapVisualizationCommitReceipt } from '../../shared/workbench';
import type { MapDocumentTab } from './mapTypes';
import { mapDocumentKind } from './mapText';

export type MapDocumentActionNotice = {
  action: 'rename' | 'delete';
  needsReconcile: boolean;
  message: string;
  diagnostic?: unknown;
  receipt?: MapVisualizationCommitReceipt | MapDocumentDeleteReceipt;
};

interface Props {
  document: MapDocumentTab | null;
  documents: readonly MapDocumentTab[];
  canManage: boolean;
  canRename?: boolean;
  canDelete?: boolean;
  blockedReason?: string;
  busy: boolean;
  notice: MapDocumentActionNotice | null;
  onRename: (name: string) => Promise<void>;
  onDelete: () => Promise<void>;
  onReconcile: () => Promise<void>;
  language: Language;
}

type Editor = 'rename' | 'delete' | null;

/** Inline document actions keep map editing in place instead of stacking dialogs. */
export function MapDocumentActions({ document, documents, canManage, canRename = canManage, canDelete = canManage, blockedReason, busy, notice, onRename, onDelete, onReconcile, language }: Props) {
  const [open, setOpen] = useState(false);
  const [editor, setEditor] = useState<Editor>(null);
  const [name, setName] = useState('');
  const [validation, setValidation] = useState('');
  const detailsRef = useRef<HTMLDetailsElement>(null);

  useEffect(() => {
    setEditor(null);
    setValidation('');
    setOpen(false);
    if (detailsRef.current) detailsRef.current.open = false;
  }, [document?.id]);

  const copy = language === 'zh' ? {
    title: '地图操作', rename: '重命名地图…', remove: '删除地图…',
    name: '地图名称', renamePrompt: '修改地图文档名称。此操作只更新工程地图目录。',
    deletePrompt: '删除此地图文档？只会从工程地图目录移除，不会删除地图来源文件。',
    confirmRename: '确认重命名', confirmDelete: '确认删除地图', cancel: '取消',
    errorEmpty: '请输入地图名称。', errorLong: '地图名称不能超过 128 个字符。',
    errorControl: '地图名称不能包含控制字符。', errorDuplicate: '此工程中已有同名地图。',
    reconcile: '重新读取并核对', reading: '正在提交…',
    status: '地图操作状态', noMap: '请先选择一个已保存的地图文档。',
  } : {
    title: 'Map actions', rename: 'Rename map…', remove: 'Delete map…',
    name: 'Map name', renamePrompt: 'Change the saved map document name. This only updates the project map catalog.',
    deletePrompt: 'Delete this map document? This removes it from the project map catalog and leaves source files untouched.',
    confirmRename: 'Confirm rename', confirmDelete: 'Confirm map deletion', cancel: 'Cancel',
    errorEmpty: 'Enter a map name.', errorLong: 'Map names must be 128 characters or fewer.',
    errorControl: 'Map names cannot contain control characters.', errorDuplicate: 'A map with this name already exists in the project.',
    reconcile: 'Reread and reconcile', reading: 'Committing…',
    status: 'Map operation status', noMap: 'Select a saved map document first.',
  };

  const closeEditor = () => {
    if (busy) return;
    setEditor(null);
    setValidation('');
  };

  const submitRename = async () => {
    if (!document || busy) return;
    const normalized = name.trim();
    if (!normalized) { setValidation(copy.errorEmpty); return; }
    if (normalized.length > 128) { setValidation(copy.errorLong); return; }
    if (/[\u0000-\u001f\u007f-\u009f]/.test(normalized)) { setValidation(copy.errorControl); return; }
    if (documents.some(candidate => candidate.id !== document.id &&
        candidate.name.toLowerCase() === normalized.toLowerCase())) {
      setValidation(copy.errorDuplicate);
      return;
    }
    setValidation('');
    await onRename(normalized);
    setEditor(null);
  };

  return <details className="map-document-manager" ref={detailsRef} onToggle={event => setOpen(event.currentTarget.open)}>
    <summary data-testid="map-document-actions-toggle" aria-label={copy.title} aria-expanded={open}>
      {copy.title} <span aria-hidden="true">▾</span>
    </summary>
    {open && <div className="map-document-manager-panel" role="group" aria-label={copy.title}>
      {!document && <p className="map-document-manager-hint">{copy.noMap}</p>}
      {document && <>
        <div className="map-document-manager-identity">{document.name}<small>{mapDocumentKind(language, document.mapType)}</small></div>
        {editor === null && <div className="map-document-manager-actions">
          <button type="button" data-testid="map-document-rename-action" disabled={!canRename || busy || Boolean(notice?.needsReconcile)}
            title={!canRename ? blockedReason : undefined} onClick={() => { setName(document.name); setValidation(''); setEditor('rename'); }}>
            {copy.rename}
          </button>
          <button type="button" data-testid="map-document-delete-action" disabled={!canDelete || busy || Boolean(notice?.needsReconcile)}
            title={!canDelete ? blockedReason : undefined} onClick={() => setEditor('delete')}>
            {copy.remove}
          </button>
        </div>}
        {editor === 'rename' && <form className="map-document-manager-form" data-testid="map-document-rename-form"
          onSubmit={event => { event.preventDefault(); void submitRename(); }}>
          <p>{copy.renamePrompt}</p>
          <label>{copy.name}
            <input data-testid="map-document-rename-input" autoFocus value={name} maxLength={128}
              disabled={busy} onChange={event => { setName(event.currentTarget.value); setValidation(''); }}
              onKeyDown={event => { if (event.key === 'Escape') { event.preventDefault(); closeEditor(); } }} />
          </label>
          {validation && <div className="map-document-manager-error" role="alert">{validation}</div>}
          <div className="map-document-manager-actions">
            <button type="submit" data-testid="map-document-rename-confirm" disabled={busy || !name.trim() || name.trim() === document.name}>
              {busy ? copy.reading : copy.confirmRename}
            </button>
            <button type="button" data-testid="map-document-rename-cancel" disabled={busy} onClick={closeEditor}>{copy.cancel}</button>
          </div>
        </form>}
        {editor === 'delete' && <div className="map-document-manager-form" data-testid="map-document-delete-confirmation" role="group">
          <p>{copy.deletePrompt}</p>
          <div className="map-document-manager-actions">
            <button type="button" data-testid="map-document-delete-confirm" disabled={busy} onClick={async () => { await onDelete(); setEditor(null); }}>
              {busy ? copy.reading : copy.confirmDelete}
            </button>
            <button type="button" data-testid="map-document-delete-cancel" disabled={busy} onClick={closeEditor}>{copy.cancel}</button>
          </div>
        </div>}
      </>}
      {notice && <div className={'map-document-manager-notice' + (notice.needsReconcile ? ' is-warning' : '')}
        data-testid="map-document-mutation-status" role={notice.needsReconcile ? 'alert' : 'status'}>
        <strong>{copy.status}</strong>
        <span>{notice.message}</span>
        {notice.needsReconcile && <button type="button" data-testid="map-document-reconcile" disabled={busy}
          onClick={() => void onReconcile()}>{copy.reconcile}</button>}
      </div>}
    </div>}
  </details>;
}
