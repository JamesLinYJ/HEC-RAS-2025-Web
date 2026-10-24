import type { EditorPageProps } from './model';
import { SchemaPage } from './SchemaFieldRenderer';

/** React port of Ras.Interface.WinUI.Controls.PlanComputeSettingsPage. */
export function PlanComputeSettingsPage(props: EditorPageProps) {
  return <SchemaPage {...props} pageId="PlanComputeSettingsPage" />;
}
