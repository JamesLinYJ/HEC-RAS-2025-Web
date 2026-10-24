import type { EditorPageProps } from './model';
import { BoundaryConditionLineEditor } from './BoundaryConditionLineEditor';
import { BoundaryConditionTimeWindow } from './BoundaryConditionTimeWindow';

/** React page composition for the source hydraulic BoundaryConditionEditor branch. */
export function BoundaryConditionLineEditorPage(props: EditorPageProps) {
  return <section className="editor-schema-page editor-boundary-lines" aria-label={props.language === 'zh' ? '水力边界线编辑器' : 'Hydraulic boundary line editor'}>
    <div className="editor-schema-heading">
      <div>
        <p className="editor-eyebrow">{props.language === 'zh' ? '水力边界' : 'Hydraulic boundaries'}</p>
        <h2>{props.language === 'zh' ? '边界线与载荷' : 'Boundary lines and payloads'}</h2>
      </div>
      <details className="editor-source-details"><summary>{props.language === 'zh' ? '来源控件' : 'Source control'}</summary><code>BoundaryConditionEditor → BoundaryConditionLineEditor</code></details>
    </div>
    <BoundaryConditionTimeWindow {...props} />
    <BoundaryConditionLineEditor {...props} />
  </section>;
}
