import { useEffect, useRef, type ReactNode } from 'react';

/** Native modal dialog shell; Escape is routed to the caller's cancel action. */
export function FramedContentDialog({ open, title, children, onCancel }: {
  open: boolean;
  title: string;
  children: ReactNode;
  onCancel: () => void;
}) {
  const dialogRef = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;
    if (open && !dialog.open) dialog.showModal();
    else if (!open && dialog.open) dialog.close();
  }, [open]);

  return <dialog
    className="ras-dialog"
    ref={dialogRef}
    aria-label={title}
    onCancel={(event) => {
      event.preventDefault();
      onCancel();
    }}
  >
    <h2>{title}</h2>
    {children}
  </dialog>;
}

export default FramedContentDialog;
