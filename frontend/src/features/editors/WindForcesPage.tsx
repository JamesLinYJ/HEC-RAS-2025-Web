import type { EditorPageProps } from './model';
import { WindForcesControl } from './WindForcesControl';

/** React port of Ras.Visual.WinUI.Controls.WindForcesPage. */
export function WindForcesPage(props: EditorPageProps) {
  return <section className="editor-schema-page editor-meteorology-page" aria-label={props.language === 'zh' ? '风力' : 'Wind forces'}>
    <div className="editor-schema-heading"><div><p className="editor-eyebrow">{props.language === 'zh' ? '空间边界条件' : 'Spatial boundary condition'}</p><h2>{props.language === 'zh' ? '风力' : 'Wind forces'}</h2></div></div>
    <WindForcesControl {...props} />
  </section>;
}
