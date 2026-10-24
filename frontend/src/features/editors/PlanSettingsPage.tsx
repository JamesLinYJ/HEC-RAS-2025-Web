import type { EditorPageProps } from './model';
import { SchemaPage } from './SchemaFieldRenderer';

/** React port of Ras.Interface.WinUI.Controls.PlanSettingsPage. */
export function PlanSettingsPage(props: EditorPageProps) {
  return <SchemaPage {...props} pageId="PlanSettingsPage" />;
}
