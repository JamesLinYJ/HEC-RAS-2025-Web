import type { EditorPageProps } from './model';
import { AirDensityControl } from './AirDensityControl';

/** React port of Ras.Visual.WinUI.Controls.AirDensityPage. */
export function AirDensityPage(props: EditorPageProps) {
  return <section className="editor-schema-page editor-meteorology-page" aria-label={props.language === 'zh' ? '空气密度' : 'Air density'}>
    <div className="editor-schema-heading"><div><p className="editor-eyebrow">{props.language === 'zh' ? '空间边界条件' : 'Spatial boundary condition'}</p><h2>{props.language === 'zh' ? '空气密度' : 'Air density'}</h2></div></div>
    <AirDensityControl {...props} />
  </section>;
}
