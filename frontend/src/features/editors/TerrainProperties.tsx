import type { EditorPageProps } from './model';
import { SchemaPage } from './SchemaFieldRenderer';

/** React port of Ras.Visual.WinUI.Controls.TerrainProperties. */
export function TerrainProperties(props: EditorPageProps) {
  return <SchemaPage {...props} pageId="TerrainProperties" />;
}
