import type { EditorPageProps } from './model';
import { BoundaryConditionTimeWindow } from './BoundaryConditionTimeWindow';
import { BoundaryConditionLineAndPayloadPickerPage } from './BoundaryConditionLineAndPayloadPickerPage';
import { DetailedBoundaryConditionHeader } from './DetailedBoundaryConditionHeader';

/** React page composition for the source hydraulic BoundaryConditionEditor branch. */
export function BoundaryConditionLineEditorPage(props: EditorPageProps) {
  return <section className="editor-boundary-lines" aria-label={props.language === 'zh' ? '水力边界线编辑器' : 'Hydraulic boundary line editor'}>
    <DetailedBoundaryConditionHeader {...props} />
    <BoundaryConditionTimeWindow {...props} />
    <BoundaryConditionLineAndPayloadPickerPage {...props} />
  </section>;
}
