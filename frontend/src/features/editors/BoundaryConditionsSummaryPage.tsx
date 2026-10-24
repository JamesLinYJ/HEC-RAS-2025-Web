import type { EditorPageProps } from './model';
import { SchemaPage } from './SchemaFieldRenderer';

/** React port of Ras.Visual.WinUI.Controls.BoundaryConditionsSummary. */
export function BoundaryConditionsSummaryPage(props: EditorPageProps) {
  return <SchemaPage {...props} pageId="BoundaryConditionsSummary" />;
}
