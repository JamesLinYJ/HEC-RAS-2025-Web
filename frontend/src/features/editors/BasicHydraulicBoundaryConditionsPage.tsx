import type { EditorPageProps } from './model';
import { BasicHydraulicBoundaryConditions } from './BasicHydraulicBoundaryConditions';

/** React page counterpart of Ras.Visual.WinUI.Controls.BasicHydraulicBoundaryConditionsPage. */
export function BasicHydraulicBoundaryConditionsPage(props: EditorPageProps) {
  return <section className="editor-schema-page" aria-label={props.language === 'zh' ? '基本水力边界条件' : 'Basic hydraulic boundary conditions'}>
    <div className="editor-schema-heading">
      <div>
        <p className="editor-eyebrow">{props.language === 'zh' ? '水力边界' : 'Hydraulic boundaries'}</p>
        <h2>{props.language === 'zh' ? '边界线与载荷' : 'Boundary lines and payloads'}</h2>
      </div>
      <details className="editor-source-details"><summary>{props.language === 'zh' ? '来源控件' : 'Source controls'}</summary><code>BasicHydraulicBoundaryConditionsPage → BasicHydraulicBoundaryConditions</code></details>
    </div>
    <BasicHydraulicBoundaryConditions {...props} />
  </section>;
}
