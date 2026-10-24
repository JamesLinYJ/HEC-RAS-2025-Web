import { useEffect, useRef, useState } from 'react';
import type { Language } from '../../../shared/workbench';
import type { MapExtent } from '../mapTypes';
import { normalizeShortcut, shortcutFromKeyboardEvent } from './View';
import type { SavedMapView } from './View';

interface ViewEditorProps {
  language: Language;
  view: SavedMapView | null;
  currentExtent: MapExtent | null;
  disabled?: boolean;
  disabledReason?: string;
  onRename: (name: string) => void;
  onResetExtent: (extent: MapExtent) => void;
  onDelete: () => void;
  onShortcut: (shortcut: string) => void;
}

function numberLabel(value: number): string {
  return Number.isFinite(value) ? Number(value.toPrecision(9)).toString() : '—';
}

export function ViewEditor({ language, view, currentExtent, disabled = false, disabledReason, onRename, onResetExtent, onDelete, onShortcut }: ViewEditorProps) {
  const zh = language === 'zh';
  const [name, setName] = useState(view?.name ?? '');
  const [shortcut, setShortcut] = useState(view?.shortcut ?? 'None');
  const [capturing, setCapturing] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [shortcutError, setShortcutError] = useState<string | null>(null);
  const shortcutInputRef = useRef<HTMLInputElement | null>(null);

  useEffect(() => {
    setName(view?.name ?? '');
    setShortcut(view?.shortcut ?? 'None');
    setCapturing(false);
    setConfirmDelete(false);
    setShortcutError(null);
  }, [view?.name, view?.entrySHA256]);

  useEffect(() => {
    if (capturing) shortcutInputRef.current?.focus();
  }, [capturing]);

  if (!view) {
    return <div className="map-view-editor-empty">{zh ? '选择一个已保存的视图以编辑。' : 'Select a saved view to edit it.'}</div>;
  }

  const extent = currentExtent;
  const validCurrentExtent = extent && Number.isFinite(extent.minX) && Number.isFinite(extent.minY) &&
    Number.isFinite(extent.maxX) && Number.isFinite(extent.maxY) && extent.minX < extent.maxX && extent.minY < extent.maxY;
  const normalizedShortcut = normalizeShortcut(shortcut);

  return (
    <section className="map-view-editor" aria-label={zh ? `编辑地图视图：${view.name}` : `Edit map view: ${view.name}`}>
      <label className="map-view-editor-field">
        <span>{zh ? '视图名称' : 'View name'}</span>
        <input data-testid="map-view-name-edit" value={name} maxLength={120} disabled={disabled}
          onChange={(event) => { setName(event.target.value); setConfirmDelete(false); }} />
      </label>
      <div className="map-view-editor-actions">
        <button type="button" data-testid="map-view-rename" disabled={disabled || !name.trim() || name === view.name}
          title={disabledReason} onClick={() => onRename(name.trim())}>{zh ? '重命名' : 'Rename'}</button>
        <button type="button" data-testid="map-view-reset" disabled={disabled || !validCurrentExtent}
          title={!validCurrentExtent ? (zh ? '当前地图范围不可用' : 'Current map extent is unavailable') : disabledReason}
          onClick={() => { if (extent) onResetExtent(extent); }}>{zh ? '更新为当前范围' : 'Use current extent'}</button>
        {confirmDelete ? (
          <>
            <button type="button" className="is-danger" data-testid="map-view-delete-confirm" disabled={disabled}
              onClick={() => { setConfirmDelete(false); onDelete(); }}>{zh ? '确认删除' : 'Confirm delete'}</button>
            <button type="button" data-testid="map-view-delete-cancel" disabled={disabled}
              onClick={() => setConfirmDelete(false)}>{zh ? '取消' : 'Cancel'}</button>
          </>
        ) : (
          <button type="button" className="is-danger" data-testid="map-view-delete" disabled={disabled}
            title={disabledReason} onClick={() => setConfirmDelete(true)}>{zh ? '删除' : 'Delete'}</button>
        )}
      </div>

      <label className="map-view-editor-field">
        <span>{zh ? '快捷键' : 'Shortcut'}</span>
        <input ref={shortcutInputRef} data-testid="map-view-shortcut" value={shortcut} maxLength={32} disabled={disabled}
          readOnly={capturing}
          aria-invalid={Boolean(shortcutError)}
          onChange={(event) => { setShortcut(event.target.value); setShortcutError(null); }}
          onKeyDown={(event) => {
            if (!capturing) return;
            event.preventDefault();
            event.stopPropagation();
            if (event.key === 'Escape') { setCapturing(false); return; }
            if (['Control', 'Alt', 'Shift', 'Meta'].includes(event.key)) return;
            const formatted = shortcutFromKeyboardEvent(event.nativeEvent);
            if (!formatted || ['Control', 'Alt', 'Shift', 'Meta'].includes(event.key)) {
              setShortcutError(zh ? '无法识别该按键，请输入快捷键或重新录制。' : 'That key could not be recognized. Enter or record a shortcut.');
              return;
            }
            setShortcut(formatted);
            setShortcutError(null);
            setCapturing(false);
          }} />
      </label>
      <div className="map-view-editor-actions">
        <button type="button" data-testid="map-view-shortcut-capture" disabled={disabled}
          aria-pressed={capturing} onClick={() => { setCapturing(true); setShortcutError(null); }}>
          {capturing ? (zh ? '按下快捷键…（Esc 取消）' : 'Press a shortcut… (Esc cancels)') : (zh ? '录制按键' : 'Record key')}
        </button>
        <button type="button" data-testid="map-view-shortcut-save" disabled={disabled || !normalizedShortcut || normalizedShortcut === view.shortcut}
          title={disabledReason} onClick={() => {
            if (!normalizedShortcut) { setShortcutError(zh ? '快捷键格式无效。' : 'Invalid shortcut format.'); return; }
            onShortcut(normalizedShortcut);
          }}>{zh ? '保存快捷键' : 'Save shortcut'}</button>
      </div>
      {shortcutError && <p className="map-view-editor-error" role="alert">{shortcutError}</p>}
      <dl className="map-view-extent" data-testid="map-view-extent">
        <div><dt>{zh ? '左' : 'Left'}</dt><dd>{numberLabel(view.extent.left)}</dd></div>
        <div><dt>{zh ? '下' : 'Bottom'}</dt><dd>{numberLabel(view.extent.bottom)}</dd></div>
        <div><dt>{zh ? '右' : 'Right'}</dt><dd>{numberLabel(view.extent.right)}</dd></div>
        <div><dt>{zh ? '上' : 'Top'}</dt><dd>{numberLabel(view.extent.top)}</dd></div>
      </dl>
      {disabledReason && <p className="map-view-editor-note">{disabledReason}</p>}
    </section>
  );
}
