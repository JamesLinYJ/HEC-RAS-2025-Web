import type { EditorPageProps } from './model';
import { PrecipitationEditor } from './PrecipitationEditor';

/** React port of Ras.Visual.WinUI.Controls.PrecipitationPage. */
export function PrecipitationPage(props: EditorPageProps) {
  return <section className="editor-schema-page editor-meteorology-page" aria-label={props.language === 'zh' ? '降水' : 'Precipitation'}>
    <PrecipitationEditor {...props} />
  </section>;
}
