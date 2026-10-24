import { AlertBanner, type AlertBannerSeverity } from './AlertBanner';

/** Message plus explicit cancel/continue events, matching the original action control. */
export function ConfirmActionControl({
  message,
  severity,
  cancelLabel,
  continueLabel,
  continueIsDangerous = false,
  autoFocusCancel = false,
  onCancel,
  onContinue,
}: {
  message: string;
  severity: AlertBannerSeverity;
  cancelLabel: string;
  continueLabel: string;
  continueIsDangerous?: boolean;
  autoFocusCancel?: boolean;
  onCancel: () => void;
  onContinue: () => void;
}) {
  return <div className="ras-confirm-action-control">
    <AlertBanner severity={severity} description={message} />
    <div className="ras-dialog-actions">
      <button type="button" autoFocus={autoFocusCancel} onClick={onCancel}>{cancelLabel}</button>
      <button type="button" className={continueIsDangerous ? 'ras-danger' : undefined} onClick={onContinue}>{continueLabel}</button>
    </div>
  </div>;
}

export default ConfirmActionControl;
