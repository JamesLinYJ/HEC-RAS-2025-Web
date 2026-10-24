import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';

export interface RasCatalogSummaryHeaderProps {
  language: Language;
  icon: string;
  typeLabel: string;
  title: string;
}

/** React counterpart of the shared WinUI RasCatalogSummaryHeader control. */
export function RasCatalogSummaryHeader({ language, icon, typeLabel, title }: RasCatalogSummaryHeaderProps) {
  return (
    <header className="project-catalog-summary-header">
      <div className="project-detail-icon" aria-hidden="true">{icon}</div>
      <div className="project-catalog-summary-heading">
        <div className="project-detail-kicker">{text(language, '目录对象', 'CATALOG ITEM')} · {typeLabel}</div>
        <h3 title={title}>{title}</h3>
      </div>
    </header>
  );
}
