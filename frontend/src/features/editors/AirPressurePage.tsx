import type { EditorPageProps } from './model';
import { AirPressureControl } from './AirPressureControl';
import { AirPressureHeader } from './AirPressureHeader';

/** React port of Ras.Visual.WinUI.Controls.AirPressurePage. */
export function AirPressurePage(props: EditorPageProps) {
  return <section className="editor-schema-page editor-meteorology-page" aria-label={props.language === 'zh' ? '气压' : 'Air pressure'}>
    <AirPressureHeader {...props} />
    <AirPressureControl {...props} />
  </section>;
}
