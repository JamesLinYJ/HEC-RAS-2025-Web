import { CulvertsEditor } from './CulvertsEditor';
import type { CulvertsEditorProps } from './CulvertsEditor';

export type GeometryCulvertsPageProps = CulvertsEditorProps;

/** Workspace route adapter; the source-matched CulvertsEditor owns the mounted UI. */
export function GeometryCulvertsPage(props: GeometryCulvertsPageProps) {
  return <CulvertsEditor {...props} />;
}

export default GeometryCulvertsPage;
