import type { EditorPageProps } from './model';
import { BasicHydraulicBoundaryConditionsPage } from './BasicHydraulicBoundaryConditionsPage';
import { BoundaryConditionTimeWindow } from './BoundaryConditionTimeWindow';

/** React page composition for the source hydraulic BoundaryConditionEditor branch. */
export function BoundaryConditionLineEditorPage(props: EditorPageProps) {
  return <section className="editor-boundary-lines" aria-label={props.language === 'zh' ? '水力边界线编辑器' : 'Hydraulic boundary line editor'}>
    <BoundaryConditionTimeWindow {...props} />
    <BasicHydraulicBoundaryConditionsPage {...props} />
  </section>;
}
