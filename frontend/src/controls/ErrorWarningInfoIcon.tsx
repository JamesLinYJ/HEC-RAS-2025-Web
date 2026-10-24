import type { Language } from '../shared/workbench';

export type ErrorWarningInfoSeverity = 'error' | 'warning' | 'info';

const labels: Record<Language, Record<ErrorWarningInfoSeverity, string>> = {
  zh: { error: '错误', warning: '警告', info: '信息' },
  en: { error: 'Error', warning: 'Warning', info: 'Information' },
};

/** Severity icon used by status-bearing compute controls. */
export function ErrorWarningInfoIcon({ severity, language, label }: {
  severity: ErrorWarningInfoSeverity;
  language: Language;
  label?: string;
}) {
  const accessibleLabel = label ?? labels[language][severity];
  return <span className={`compute-severity-icon is-${severity}`} role="img" aria-label={accessibleLabel}>
    <span aria-hidden="true">{severity === 'error' ? '!' : severity === 'warning' ? '▲' : 'i'}</span>
  </span>;
}

export default ErrorWarningInfoIcon;
