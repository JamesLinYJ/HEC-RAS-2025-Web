import type { EditorPageProps } from './model';
import { SchemaPage } from './SchemaFieldRenderer';

/** React port of Ras.Visual.WinUI.Controls.ClassificationLayerSummary. */
export function ClassificationLayerSummary(props: EditorPageProps) {
  return <SchemaPage {...props} pageId="ClassificationLayerSummary" />;
}
