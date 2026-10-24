import type { FeaturePageProps } from '../../shared/workbench';
import EditorWorkbench from './EditorWorkbench';
import './editors.css';

export default function EditorsFeaturePage(props: FeaturePageProps) {
  return <EditorWorkbench {...props} />;
}
