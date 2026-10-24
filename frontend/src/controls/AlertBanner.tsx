import type { ReactNode } from 'react';

export type AlertBannerSeverity = 'information' | 'warning' | 'error';

const severityMark: Record<AlertBannerSeverity, string> = {
  information: 'i',
  warning: '!',
  error: '×',
};

/** Severity-bearing message presentation for an actual message or UI failure. */
export function AlertBanner({ severity, header, description, children }: {
  severity: AlertBannerSeverity;
  header?: string;
  description?: string;
  children?: ReactNode;
}) {
  return <div className={`ras-alert-banner is-${severity}`} data-alert-severity={severity}>
    <span className="ras-alert-banner-icon" aria-hidden="true">{severityMark[severity]}</span>
    <div className="ras-alert-banner-content">
      {header && <strong className="ras-alert-banner-header">{header}</strong>}
      {description && <p>{description}</p>}
      {children}
    </div>
  </div>;
}

export default AlertBanner;
