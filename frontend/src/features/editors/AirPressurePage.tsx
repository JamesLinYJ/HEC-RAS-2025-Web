import type { EditorPageProps } from './model';
import { AirPressureControl } from './AirPressureControl';

/** React port of Ras.Visual.WinUI.Controls.AirPressurePage. */
export function AirPressurePage(props: EditorPageProps) {
  return <section className="editor-schema-page editor-meteorology-page" aria-label={props.language === 'zh' ? '气压' : 'Air pressure'}>
    <div className="editor-schema-heading"><div><p className="editor-eyebrow">{props.language === 'zh' ? '空间边界条件' : 'Spatial boundary condition'}</p><h2>{props.language === 'zh' ? '气压' : 'Air pressure'}</h2></div></div>
    <AirPressureControl {...props} />
  </section>;
}
