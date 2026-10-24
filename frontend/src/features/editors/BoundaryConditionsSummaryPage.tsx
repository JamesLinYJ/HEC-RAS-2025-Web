import type { EditorPageProps } from './model';
import { BoundaryConditionsSummary } from './BoundaryConditionsSummary';
import { SmallBoundaryConditionsSummary } from './SmallBoundaryConditionsSummary';

/** React port of Ras.Visual.WinUI.Controls.BoundaryConditionsSummary. */
export function BoundaryConditionsSummaryPage(props: EditorPageProps) {
  return <section className="editor-schema-page" aria-label={props.language === 'zh' ? '边界条件概览' : 'Boundary conditions summary'}>
    <div className="editor-schema-heading">
      <div><p className="editor-eyebrow">{props.language === 'zh' ? '边界条件' : 'Boundary conditions'}</p><h2>{props.language === 'zh' ? '边界条件概览' : 'Boundary conditions summary'}</h2></div>
    </div>
    <div className="editor-boundary-summary-layout" style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 320px), 1fr))', alignItems: 'start', gap: 14 }}>
      <SmallBoundaryConditionsSummary {...props} />
      <BoundaryConditionsSummary {...props} />
    </div>
  </section>;
}
