import type { EditorPageProps } from './model';
import { SchemaPage } from './SchemaFieldRenderer';
import { ManningsNEditor } from './ManningsNEditor';

/** React composition for NValueCatalogSummary and ManningsNEditor. */
export function NValueEditor(props: EditorPageProps) {
  const pageID = props.pageId === 'NValueCatalogSummary' ? props.pageId : 'ManningsNEditor';
  return pageID === 'ManningsNEditor' ? <ManningsNEditor {...props} /> : <SchemaPage {...props} pageId={pageID} />;
}
