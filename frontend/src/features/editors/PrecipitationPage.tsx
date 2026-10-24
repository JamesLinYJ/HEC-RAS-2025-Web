import type { EditorPageProps } from './model';
import { PrecipitationControl } from './PrecipitationControl';

/** React port of Ras.Visual.WinUI.Controls.PrecipitationPage. */
export function PrecipitationPage(props: EditorPageProps) {
  return <section className="editor-schema-page editor-meteorology-page" aria-label={props.language === 'zh' ? '降水' : 'Precipitation'}>
    <div className="editor-schema-heading"><div><p className="editor-eyebrow">{props.language === 'zh' ? '空间边界条件' : 'Spatial boundary condition'}</p><h2>{props.language === 'zh' ? '降水' : 'Precipitation'}</h2></div></div>
    <PrecipitationControl {...props} />
  </section>;
}
