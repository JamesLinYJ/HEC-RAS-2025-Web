import { ConfirmActionControl } from './ConfirmActionControl';
import type { AlertBannerSeverity } from './AlertBanner';
import { FramedContentDialog } from './FramedContentDialog';

/** Modal owner for a confirmation action; the caller owns the accepted transaction. */
export function ConfirmActionControlDialog({
  open,
  title,
  message,
  severity = 'warning',
  cancelLabel,
  continueLabel,
  continueIsDangerous = false,
  autoFocusCancel = false,
  onCancel,
  onContinue,
}: {
  open: boolean;
  title: string;
  message: string;
  severity?: AlertBannerSeverity;
  cancelLabel: string;
  continueLabel: string;
  continueIsDangerous?: boolean;
  autoFocusCancel?: boolean;
  onCancel: () => void;
  onContinue: () => void;
}) {
  return <FramedContentDialog open={open} title={title} onCancel={onCancel}>
    <ConfirmActionControl
      message={message}
      severity={severity}
      cancelLabel={cancelLabel}
      continueLabel={continueLabel}
      continueIsDangerous={continueIsDangerous}
      autoFocusCancel={autoFocusCancel}
      onCancel={onCancel}
      onContinue={onContinue}
    />
  </FramedContentDialog>;
}

export default ConfirmActionControlDialog;
