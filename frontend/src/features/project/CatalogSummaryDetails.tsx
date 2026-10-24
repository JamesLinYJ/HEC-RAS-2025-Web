import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';

export interface CatalogSummaryField {
  id: string;
  label: string;
  value?: string | null;
}

/** Read-only detail rows retain source values and show an explicit empty state. */
export function CatalogSummaryDetails({ language, title, fields }: {
  language: Language;
  title: string;
  fields: readonly CatalogSummaryField[];
}) {
  const missing = text(language, '未提供', 'Not provided');
  return (
    <section className="project-detail-properties">
      <h4>{title}</h4>
      <dl>{fields.map((field) => <div key={field.id}>
        <dt>{field.label}</dt>
        <dd>{field.value?.trim() ? field.value : missing}</dd>
      </div>)}</dl>
    </section>
  );
}
