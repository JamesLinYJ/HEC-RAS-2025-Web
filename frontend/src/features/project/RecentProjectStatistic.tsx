import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';

interface RecentProjectStatisticProps {
  language: Language;
  value: number;
}

/** Displays only the count of persisted recent-project records available to this UI. */
export function RecentProjectStatistic({ language, value }: RecentProjectStatisticProps) {
  return (
    <div className="project-recent-statistic" aria-label={text(language, '本机保存的最近工程记录数', 'Saved recent project records')}>
      <span aria-hidden="true">◷</span>
      <strong>{value}</strong>
      <small>{text(language, '条最近记录', 'recent records')}</small>
    </div>
  );
}
