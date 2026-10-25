import { useLayoutEffect, useRef, useState, type KeyboardEvent } from 'react';
import { createPortal } from 'react-dom';
import type { Language } from '../shared/workbench';

interface Props {
  language: Language;
  x: number;
  y: number;
  canCopy: boolean;
  canPaste: boolean;
  canClear: boolean;
  canSelectAll: boolean;
  onCopy: () => void;
  onPaste: () => void;
  onClear: () => void;
  onSelectAll: () => void;
  onClose: (restoreFocus: boolean) => void;
}

/** Browser menu for DataGrid's existing cell commands; no row/column mutation is implied. */
export function DataGridContextMenuFlyout({ language, x, y, canCopy, canPaste, canClear, canSelectAll,
  onCopy, onPaste, onClear, onSelectAll, onClose }: Props) {
  const menuRef = useRef<HTMLDivElement>(null);
  const closeRef = useRef(onClose);
  closeRef.current = onClose;
  const [position, setPosition] = useState({ x, y });
  const zh = language === 'zh';

  useLayoutEffect(() => {
    const menu = menuRef.current;
    if (!menu) return;
    const rect = menu.getBoundingClientRect();
    setPosition({ x: Math.max(8, Math.min(x, window.innerWidth - rect.width - 8)),
      y: Math.max(8, Math.min(y, window.innerHeight - rect.height - 8)) });
    (menu.querySelector<HTMLButtonElement>('button:not(:disabled)') ?? menu).focus();
    const outside = (event: globalThis.PointerEvent): void => {
      if (event.target instanceof Node && !menu.contains(event.target)) closeRef.current(false);
    };
    const repositioned = (event: Event): void => {
      if (!(event.target instanceof Node) || !menu.contains(event.target)) closeRef.current(false);
    };
    document.addEventListener('pointerdown', outside, true);
    window.addEventListener('resize', repositioned);
    window.addEventListener('scroll', repositioned, true);
    return () => {
      document.removeEventListener('pointerdown', outside, true);
      window.removeEventListener('resize', repositioned);
      window.removeEventListener('scroll', repositioned, true);
    };
  }, [x, y]);

  const navigate = (event: KeyboardEvent<HTMLDivElement>): void => {
    event.stopPropagation();
    if (event.key === 'Escape') { event.preventDefault(); onClose(true); return; }
    if (event.key === 'Tab') { onClose(false); return; }
    if (!['ArrowUp', 'ArrowDown', 'Home', 'End'].includes(event.key)) return;
    event.preventDefault();
    const items = Array.from(menuRef.current?.querySelectorAll<HTMLButtonElement>('button:not(:disabled)') ?? []);
    if (!items.length) return;
    const current = items.findIndex(item => item === document.activeElement);
    const next = event.key === 'Home' ? 0 : event.key === 'End' ? items.length - 1 :
      (current + (event.key === 'ArrowUp' ? -1 : 1) + items.length) % items.length;
    items[next]?.focus();
  };

  return createPortal(<div ref={menuRef} className="ras-data-grid-context-menu" role="menu" tabIndex={-1}
    data-testid="data-grid-context-menu" aria-label={zh ? '单元格操作' : 'Cell actions'}
    style={{ left: position.x, top: position.y }} onKeyDown={navigate}
    onContextMenu={event => event.preventDefault()}>
    <button type="button" role="menuitem" data-testid="data-grid-copy" disabled={!canCopy} onClick={onCopy}>
      <span>{zh ? '复制' : 'Copy'}</span><kbd>Ctrl/⌘ C</kbd>
    </button>
    <button type="button" role="menuitem" data-testid="data-grid-paste" disabled={!canPaste} onClick={onPaste}>
      <span>{zh ? '粘贴' : 'Paste'}</span><kbd>Ctrl/⌘ V</kbd>
    </button>
    <button type="button" role="menuitem" data-testid="data-grid-clear" disabled={!canClear} onClick={onClear}>
      <span>{zh ? '清空' : 'Clear'}</span><kbd>Delete / ⌫</kbd>
    </button>
    <div role="separator" />
    <button type="button" role="menuitem" data-testid="data-grid-select-all" disabled={!canSelectAll} onClick={onSelectAll}>
      <span>{zh ? '全选' : 'Select all'}</span><kbd>Ctrl/⌘ A</kbd>
    </button>
  </div>, document.body);
}
