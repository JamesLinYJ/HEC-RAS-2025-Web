import type { EditorPageProps } from './model';
import { EvapotranspirationControl } from './EvapotranspirationControl';

/** React port of Ras.Visual.WinUI.Controls.EvapotranspirationPage. */
export function EvapotranspirationPage(props: EditorPageProps) {
  return <section className="editor-schema-page editor-meteorology-page" aria-label={props.language === 'zh' ? '蒸散' : 'Evapotranspiration'}>
    <div className="editor-schema-heading"><div><p className="editor-eyebrow">{props.language === 'zh' ? '空间边界条件' : 'Spatial boundary condition'}</p><h2>{props.language === 'zh' ? '蒸散' : 'Evapotranspiration'}</h2></div></div>
    <EvapotranspirationControl {...props} />
  </section>;
}
