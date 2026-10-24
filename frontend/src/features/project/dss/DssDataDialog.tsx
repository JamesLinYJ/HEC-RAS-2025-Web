import { useEffect, useRef, type ReactNode } from 'react';

interface DssDataDialogProps {
  open: boolean;
  title: string;
  label: string;
  closeLabel: string;
  onClose: () => void;
  children: ReactNode;
}

export function DssDataDialog({ open, title, label, closeLabel, onClose, children }: DssDataDialogProps) {
  const dialogRef = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;
    if (open && !dialog.open) dialog.showModal();
    if (!open && dialog.open) dialog.close();
    return () => {
      if (dialog.open) dialog.close();
    };
  }, [open]);

  return <dialog
    ref={dialogRef}
    className="project-dss-dialog"
    aria-label={label}
    aria-modal="true"
    onCancel={(event) => { event.preventDefault(); onClose(); }}
    onClose={onClose}
    onClick={(event) => { if (event.target === event.currentTarget) onClose(); }}
  >
    <header className="project-dss-dialog-header">
      <h2>{title}</h2>
      <button type="button" aria-label={closeLabel} onClick={onClose}>×</button>
    </header>
    <div className="project-dss-dialog-content">{children}</div>
  </dialog>;
}
