import { useEffect, useRef, type KeyboardEvent, type ReactNode } from 'react';
import type { Language } from '../../../shared/workbench';
import './vectorImport.css';

interface VectorImporterDialogProps {
  open: boolean;
  busy: boolean;
  language: Language;
  title: string;
  description: string;
  children: ReactNode;
  onClose: () => void;
}

/** Accessible modal surface for the existing single-owner VectorImporter workflow. */
export function VectorImporterDialog({ open, busy, language, title, description, children, onClose }: VectorImporterDialogProps) {
  const dialogRef = useRef<HTMLElement>(null);
  const zh = language === 'zh';

  useEffect(() => {
    if (!open) return;
    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : undefined;
    dialogRef.current?.querySelector<HTMLElement>('[data-dialog-initial-focus]')?.focus();
    return () => previousFocus?.focus();
  }, [open]);

  const containFocus = (event: KeyboardEvent<HTMLElement>): void => {
    if (event.key === 'Escape') { event.preventDefault(); if (!busy) onClose(); return; }
    if (event.key !== 'Tab') return;
    const items = [...(dialogRef.current?.querySelectorAll<HTMLElement>(
      'button:not(:disabled), a[href], input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])',
    ) ?? [])].filter(item => item.offsetParent !== null);
    if (!items.length) { event.preventDefault(); dialogRef.current?.focus(); return; }
    const first = items[0]; const last = items[items.length - 1];
    if (event.shiftKey && (document.activeElement === first || document.activeElement === dialogRef.current)) {
      event.preventDefault(); last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault(); first.focus();
    }
  };

  return <div className="vector-import-backdrop" hidden={!open} aria-hidden={!open}>
    <section ref={dialogRef} className="vector-import-dialog vector-import-workflow" role="dialog" aria-modal="true" aria-labelledby="vector-import-title"
      tabIndex={-1} onKeyDown={containFocus}>
    <header><div><span className="tools-kicker">{zh ? '工程 / Shapefile' : 'PROJECT / SHAPEFILE'}</span>
      <h3 id="vector-import-title">{title}</h3><p>{description}</p></div>
      <button type="button" className="attribute-close" data-dialog-initial-focus disabled={busy} onClick={onClose} aria-label={zh ? '关闭' : 'Close'}>×</button>
    </header>
    <div className="vector-import-dialog-content">{children}</div>
    </section>
  </div>;
}
